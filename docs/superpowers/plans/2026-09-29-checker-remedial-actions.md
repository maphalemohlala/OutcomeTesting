# Checker Remedial Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The checker writes a remedial action for every fail point before submitting a Tax or AQS check; the submit copies each one onto its remediation action; the adviser answers "Action performed" (Yes/No) with an optional note instead of writing the remedial action.

**Architecture:** The checker's text is parked on the review (`al_pendingremedialactions`, JSON) through a contact trigger column and a plug-in, exactly as "Who carries this fail" parks `al_pendingaccountability`. `SubmitReviewPlugin` refuses a submit that owes a remediation while any item lacks text, and `Remediation.Raise` stamps each action's new write-once `al_remedialaction`. A new choice `al_actionperformed` replaces the response text as the completion requirement on new rows; `al_adviserresponse` becomes the adviser's optional note. Four renderings draw a new "Action performed" column.

**Tech Stack:** C# net462 Dataverse plug-ins (xUnit, `FakeOrganizationService`), Power Pages Liquid + inline ES5 script, React/TypeScript Code App (vitest, `tsc -b`), the `OutcomeTesting.Registration` deploy tool.

**Spec:** `docs/superpowers/specs/2026-09-29-checker-remedial-actions-design.md`

## Global Constraints

- Every environment write targets **Env_AQ_Dev** (`https://org0b075da8.crm11.dynamics.com/`) only. TEST and PROD are the project owner's call.
- `al_actionperformed` values: **Yes = 120910815, No = 120910816** (free across `src/`, `plugins/`, `app/src/`, `powerpages/`; re-check in DEV before creating).
- `al_remedialaction`: multiline text, max **4000**. `al_pendingremedialactions` and `al_remedialactionsrequest`: multiline text, max **100000**.
- The overall (no itemised fail point) key is the literal string **`__overall__`**.
- Stored and posted JSON shape: an **array of `{ "item": "...", "text": "..." }`**. The portal request wraps it: `{ "reviewId": "<guid>", "actions": [ ... ] }`.
- Plug-in assembly targets net462 with no JSON package: use `DataContractJsonSerializer`, as `AccountabilityRequestPlugin` does.
- Portal scripts are ES5 (`var`, no arrow functions, no template literals), matching the existing template scripts.
- Run plug-in tests with `$env:DOTNET_ROLL_FORWARD='Major'`. Type-check the app with `npx tsc -b` (not `--noEmit`).
- Match the surrounding comment density: every new public member gets an XML doc comment that says *why*, in the codebase's voice.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A checker unticks a fail point after writing its action, then ticks it again** → the words come back. Pinned in Task 7 (the page keeps every text in its map) and Task 1 (`Refusal` ignores stale keys).
2. **An item whose text contains `&`, quotes or `<`** → the key the page saves equals the key the server computes (Liquid escapes, `getAttribute` unescapes). Pinned in Task 1 (round-trip) and Task 7 (the item function keeps the characters).
3. **The checker types and presses Submit inside the save debounce** → the save lands before the submit request is sent. Pinned in Task 7 (submit handler awaits `otRemedial.flush` with a callback before `submit()`).
4. **Tax-then-AQS where both reviews ticked the same fail point** → each action gets its own checker's words. Pinned in Task 4 (`Collect` aligns each review's items against that review's own map).
5. **A rejected sign-off reopens an action** → the adviser can change Action performed again. Pinned in Task 5 (guard allows it at In progress).

---

### Task 1: `RemedialActions` - the parked map, the gate rule, and the payload

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/RemedialActions.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/RemedialActionsTests.cs`

**Interfaces:**
- Produces (all `public static` on `RemedialActions` unless noted):
  - `const string OverallKey = "__overall__"`, `const string PendingAttr = "al_pendingremedialactions"`, `const string ActionAttr = "al_remedialaction"`, `const string ActionPerformedAttr = "al_actionperformed"`, `const int ActionPerformedYes = 120910815`, `const int ActionPerformedNo = 120910816`, `const int MaxLength = 4000`
  - `Dictionary<string, string> Parse(string json)`: never null
  - `string Serialise(IDictionary<string, string> map)`: null when nothing to keep
  - `string TextFor(IDictionary<string, string> map, string item)`: trimmed, or null
  - `List<string> Align(IList<string> items, IDictionary<string, string> map)`: parallel to `items`
  - `string Refusal(IList<string> items, IDictionary<string, string> map)`: null when complete
  - `Dictionary<string, string> Pending(IOrganizationService service, Guid reviewId)`
  - `void EnsureWritten(IOrganizationService service, Guid reviewId, DateTime asOf)`: throws `InvalidPluginExecutionException` with `CommandHelpers.PreconditionPrefix`
  - `string ActionPerformedLabel(int? value)`: "Yes" / "No" / null
  - classes `RemedialActionEntry { string Item; string Text; }` and `RemedialActionsRequestPayload { string ReviewId; List<RemedialActionEntry> Actions; static RemedialActionsRequestPayload Parse(string json) }`

- [ ] **Step 1: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/RemedialActionsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The checker's remedial actions, parked on the review until the submit raises the
    /// actions they belong to (project owner, 2026-09-29: the checker fills in the remedial
    /// action, not the adviser).
    ///
    /// The map is keyed by the item's text exactly as Remediation.NonPassItems produces it,
    /// because that is the only thing the page and the server both know about an item before
    /// any action exists.
    /// </summary>
    public class RemedialActionsTests
    {
        private static Dictionary<string, string> Map(params string[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                map[pairs[i]] = pairs[i + 1];
            }

            return map;
        }

        [Fact]
        public void A_serialised_map_parses_back_to_itself_including_awkward_characters()
        {
            var map = Map(
                "AML - \"ID\" & address <check>: No", "Re-verify & retain.",
                RemedialActions.OverallKey, "Explain the rationale.");

            var back = RemedialActions.Parse(RemedialActions.Serialise(map));

            Assert.Equal("Re-verify & retain.", back["AML - \"ID\" & address <check>: No"]);
            Assert.Equal("Explain the rationale.", back[RemedialActions.OverallKey]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"item\":\"x\"}")]
        public void Nothing_readable_parses_to_an_empty_map(string json)
        {
            Assert.Empty(RemedialActions.Parse(json));
        }

        [Fact]
        public void Serialise_keeps_only_items_with_words_and_returns_null_when_none_do()
        {
            Assert.Null(RemedialActions.Serialise(Map("First", "   ", "Second", "")));

            var kept = RemedialActions.Parse(RemedialActions.Serialise(Map("First", " Fix it. ", "Second", " ")));
            Assert.Single(kept);
            Assert.Equal("Fix it.", kept["First"]);
        }

        [Fact]
        public void The_refusal_names_the_first_item_with_no_words()
        {
            var refusal = RemedialActions.Refusal(
                new List<string> { "First: Fail", "Second: No" },
                Map("First: Fail", "Done."));

            Assert.Contains("'Second: No'", refusal);
        }

        [Fact]
        public void Every_item_answered_is_no_refusal_and_stale_keys_are_ignored()
        {
            // "Old: Fail" was written for a fail point the checker later unticked. It stays
            // in the map so ticking it again brings the words back, and the gate ignores it.
            Assert.Null(RemedialActions.Refusal(
                new List<string> { "First: Fail" },
                Map("First: Fail", "Done.", "Old: Fail", "")));
        }

        [Fact]
        public void With_nothing_itemised_the_overall_action_is_what_is_owed()
        {
            var refusal = RemedialActions.Refusal(new List<string>(), Map());
            Assert.Contains("overall remedial action", refusal);

            Assert.Null(RemedialActions.Refusal(
                new List<string>(), Map(RemedialActions.OverallKey, "Explain the rationale.")));
        }

        [Fact]
        public void Align_gives_each_item_its_own_words_in_item_order()
        {
            var texts = RemedialActions.Align(
                new List<string> { "B", "A", "C" },
                Map("A", "a words", "B", "b words"));

            Assert.Equal(new[] { "b words", "a words", null }, texts.ToArray());
        }

        [Fact]
        public void EnsureWritten_refuses_a_review_that_parked_nothing()
        {
            var service = new FakeOrganizationService();
            var reviewId = Guid.NewGuid();
            service.Seed("al_reviewinstance", reviewId);

            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActions.EnsureWritten(service, reviewId, DateTime.UtcNow));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, thrown.Message);
        }

        [Fact]
        public void EnsureWritten_passes_a_review_whose_overall_action_is_parked()
        {
            var service = new FakeOrganizationService();
            var reviewId = Guid.NewGuid();
            service.Seed(
                "al_reviewinstance", reviewId,
                RemedialActions.PendingAttr,
                RemedialActions.Serialise(Map(RemedialActions.OverallKey, "Explain the rationale.")));

            RemedialActions.EnsureWritten(service, reviewId, DateTime.UtcNow);
        }

        [Fact]
        public void The_portal_payload_carries_the_review_and_its_entries()
        {
            var payload = RemedialActionsRequestPayload.Parse(
                "{\"reviewId\":\"f3f3f3f3-0003-4003-8003-f3f3f3f3f3f3\",\"actions\":[{\"item\":\"First\",\"text\":\"Fix it.\"}]}");

            Assert.Equal("f3f3f3f3-0003-4003-8003-f3f3f3f3f3f3", payload.ReviewId);
            Assert.Equal("First", payload.Actions[0].Item);
            Assert.Equal("Fix it.", payload.Actions[0].Text);
            Assert.Null(RemedialActionsRequestPayload.Parse("garbage"));
        }

        [Theory]
        [InlineData(RemedialActions.ActionPerformedYes, "Yes")]
        [InlineData(RemedialActions.ActionPerformedNo, "No")]
        [InlineData(null, null)]
        [InlineData(1, null)]
        public void Action_performed_reads_as_the_form_says_it(int? value, string expected)
        {
            Assert.Equal(expected, RemedialActions.ActionPerformedLabel(value));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (PowerShell): `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~RemedialActionsTests"`
Expected: build FAILS with `The name 'RemedialActions' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `plugins/OutcomeTesting.Plugins/RemedialActions.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The checker's remedial actions (project owner, 2026-09-29): what the adviser is to do
    /// about each thing the checker marked down, written by the CHECKER before the check is
    /// submitted. Before this the adviser wrote the remedial action themselves, in
    /// <c>al_adviserresponse</c>, after the remediation reached them (AD-095).
    ///
    /// The actions do not exist until the submit raises them, so the words are parked on the
    /// review in <see cref="PendingAttr"/> - the same shape "Who carries this fail" uses for
    /// <c>al_pendingaccountability</c> - keyed by each item's text exactly as
    /// <see cref="Remediation.NonPassItems"/> produces it. That text is the only thing the
    /// page and the server both know about an item before any action exists. A review with
    /// nothing itemised owes one action for the whole check, keyed <see cref="OverallKey"/>.
    ///
    /// Stored as an array of entries rather than a JSON object because
    /// DataContractJsonSerializer writes a dictionary as key/value pairs, and an array is a
    /// shape the page can write without knowing that.
    /// </summary>
    public static class RemedialActions
    {
        /// <summary>The single action a check with no itemised fail point owes.</summary>
        public const string OverallKey = "__overall__";

        /// <summary><c>al_reviewinstance</c>: the parked map, cleared once its actions are raised.</summary>
        public const string PendingAttr = "al_pendingremedialactions";

        /// <summary><c>al_remediationaction</c>: the checker's words. Written once, on Create.</summary>
        public const string ActionAttr = "al_remedialaction";

        /// <summary><c>al_remediationaction</c>: the adviser's Yes / No.</summary>
        public const string ActionPerformedAttr = "al_actionperformed";

        public const int ActionPerformedYes = 120910815;
        public const int ActionPerformedNo = 120910816;

        /// <summary>The length of <see cref="ActionAttr"/>, and the most one entry may carry.</summary>
        public const int MaxLength = 4000;

        /// <summary>The stored map, or an empty one when there is nothing readable.</summary>
        public static Dictionary<string, string> Parse(string json)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json))
            {
                return map;
            }

            List<RemedialActionEntry> entries;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    entries = new DataContractJsonSerializer(typeof(List<RemedialActionEntry>))
                        .ReadObject(stream) as List<RemedialActionEntry>;
                }
            }
            catch (SerializationException)
            {
                return map;
            }
            catch (InvalidCastException)
            {
                return map;
            }

            if (entries == null)
            {
                return map;
            }

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var key = (entry.Item ?? string.Empty).Trim();
                var text = (entry.Text ?? string.Empty).Trim();
                if (key.Length > 0 && text.Length > 0)
                {
                    map[key] = text;
                }
            }

            return map;
        }

        /// <summary>
        /// The map as stored, with blank entries dropped. Null when nothing is left, so the
        /// column reads as empty rather than as an empty array.
        /// </summary>
        public static string Serialise(IDictionary<string, string> map)
        {
            var entries = new List<RemedialActionEntry>();
            if (map != null)
            {
                foreach (var pair in map)
                {
                    var key = (pair.Key ?? string.Empty).Trim();
                    var text = (pair.Value ?? string.Empty).Trim();
                    if (key.Length > 0 && text.Length > 0)
                    {
                        entries.Add(new RemedialActionEntry { Item = key, Text = text });
                    }
                }
            }

            if (entries.Count == 0)
            {
                return null;
            }

            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(List<RemedialActionEntry>)).WriteObject(stream, entries);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>The words parked for one item, trimmed, or null when there are none.</summary>
        public static string TextFor(IDictionary<string, string> map, string item)
        {
            if (map == null || item == null)
            {
                return null;
            }

            string text;
            if (!map.TryGetValue(item.Trim(), out text) || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return text.Trim();
        }

        /// <summary>Each item's words, in item order, with null where there are none.</summary>
        public static List<string> Align(IList<string> items, IDictionary<string, string> map)
        {
            var texts = new List<string>();
            if (items == null)
            {
                return texts;
            }

            foreach (var item in items)
            {
                texts.Add(TextFor(map, item));
            }

            return texts;
        }

        /// <summary>
        /// Why this review cannot be submitted yet, or null when every item it will raise has
        /// its words. Keys that are no longer items are ignored: they are fail points the
        /// checker unticked, kept so that ticking one again brings the words back.
        /// </summary>
        public static string Refusal(IList<string> items, IDictionary<string, string> map)
        {
            var listed = new List<string>();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        listed.Add(item.Trim());
                    }
                }
            }

            if (listed.Count == 0)
            {
                return TextFor(map, OverallKey) != null
                    ? null
                    : "Write the overall remedial action under 'Fail points and remedial actions' before "
                        + "submitting. This check owes a remediation, and what you write there is what the "
                        + "adviser is asked to do.";
            }

            foreach (var item in listed)
            {
                if (TextFor(map, item) == null)
                {
                    return "Write the remedial action for '" + item + "' under 'Fail points and remedial "
                        + "actions' before submitting.";
                }
            }

            return null;
        }

        /// <summary>The map parked on a review.</summary>
        public static Dictionary<string, string> Pending(IOrganizationService service, Guid reviewId)
        {
            var review = service.Retrieve("al_reviewinstance", reviewId, new ColumnSet(PendingAttr));
            return Parse(review.GetAttributeValue<string>(PendingAttr));
        }

        /// <summary>
        /// The submit gate: refuses a review that owes a remediation while any item it will
        /// raise has no remedial action. Called by SubmitReviewPlugin only where remediation is
        /// owed, including a Tax fail that defers its raising to the AQS submit (AD-184) -
        /// checked at the Tax submit, while the Tax checker can still fix it.
        /// </summary>
        public static void EnsureWritten(IOrganizationService service, Guid reviewId, DateTime asOf)
        {
            var refusal = Refusal(Remediation.NonPassItems(service, reviewId, asOf), Pending(service, reviewId));
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }
        }

        /// <summary>
        /// The Yes / No the form prints, without a metadata read. Two values, fixed by this
        /// solution, so the label is known here the way the portal and the Code App know it.
        /// </summary>
        public static string ActionPerformedLabel(int? value)
        {
            if (value == ActionPerformedYes)
            {
                return "Yes";
            }

            return value == ActionPerformedNo ? "No" : null;
        }
    }

    /// <summary>One parked remedial action: the item it answers and the checker's words.</summary>
    [DataContract]
    public sealed class RemedialActionEntry
    {
        [DataMember(Name = "item")]
        public string Item { get; set; }

        [DataMember(Name = "text")]
        public string Text { get; set; }
    }

    /// <summary>
    /// What the portal writes onto <c>contact.al_remedialactionsrequest</c>: the review the
    /// words belong to and every entry the page holds for it.
    /// </summary>
    [DataContract]
    public sealed class RemedialActionsRequestPayload
    {
        [DataMember(Name = "reviewId")]
        public string ReviewId { get; set; }

        [DataMember(Name = "actions")]
        public List<RemedialActionEntry> Actions { get; set; }

        public static RemedialActionsRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return new DataContractJsonSerializer(typeof(RemedialActionsRequestPayload))
                        .ReadObject(stream) as RemedialActionsRequestPayload;
                }
            }
            catch (SerializationException)
            {
                return null;
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~RemedialActionsTests"`
Expected: PASS (all of them). If `"{\"item\":\"x\"}"` does not return empty (a single object read as a list throws a different exception), catch that exception type in `Parse` as well and re-run.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/RemedialActions.cs plugins/OutcomeTesting.Plugins.Tests/RemedialActionsTests.cs
git commit -m "feat(remediation): park the checker's remedial actions on the review

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `RemedialActionsRequestPlugin` - the portal's write path

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/RemedialActionsRequestPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/RemedialActionsRequestPluginTests.cs`

**Interfaces:**
- Consumes: `RemedialActions.PendingAttr`, `RemedialActions.MaxLength`, `RemedialActions.Serialise`, `RemedialActionsRequestPayload` (Task 1)
- Produces: `RemedialActionsRequestPlugin.RequestAttr = "al_remedialactionsrequest"`; `public static void Apply(IOrganizationService service, Guid contactId, RemedialActionsRequestPayload payload)`

- [ ] **Step 1: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/RemedialActionsRequestPluginTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The portal parks the checker's remedial actions by writing JSON onto the checker's own
    /// contact row, because a Power Pages page cannot call a Custom API (AD-053). The guard is
    /// the review itself: open, and assigned to the contact whose row carried the request.
    /// </summary>
    public class RemedialActionsRequestPluginTests
    {
        private static readonly Guid ContactId = Guid.Parse("c1c1c1c1-1111-4111-8111-c1c1c1c1c1c1");
        private static readonly Guid ReviewId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

        private static FakeOrganizationService Service(Guid? assignedTo, bool submitted = false)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, RemedialActionsRequestPlugin.RequestAttr, "{}");
            var review = svc.Seed("al_reviewinstance", ReviewId);
            if (assignedTo.HasValue)
            {
                review["al_assignedcontactid"] = new EntityReference("contact", assignedTo.Value);
            }

            if (submitted)
            {
                review["al_submittedon"] = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);
            }

            return svc;
        }

        private static RemedialActionsRequestPayload Payload(params string[] pairs)
        {
            var payload = new RemedialActionsRequestPayload
            {
                ReviewId = ReviewId.ToString("D"),
                Actions = new List<RemedialActionEntry>(),
            };
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                payload.Actions.Add(new RemedialActionEntry { Item = pairs[i], Text = pairs[i + 1] });
            }

            return payload;
        }

        [Fact]
        public void Parks_the_words_on_the_review_and_clears_the_request()
        {
            var svc = Service(ContactId);

            RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First: Fail", " Fix it. ", "Second: No", "  "));

            var parked = RemedialActions.Parse(
                svc.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>(RemedialActions.PendingAttr));
            Assert.Equal("Fix it.", parked["First: Fail"]);
            Assert.False(parked.ContainsKey("Second: No"));
            Assert.Null(svc.Row("contact", ContactId).GetAttributeValue<string>(RemedialActionsRequestPlugin.RequestAttr));
        }

        [Fact]
        public void Refuses_a_checker_the_review_is_not_assigned_to()
        {
            var svc = Service(Guid.NewGuid());

            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First: Fail", "Fix it.")));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, thrown.Message);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Refuses_a_submitted_review()
        {
            var svc = Service(ContactId, submitted: true);

            Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First: Fail", "Fix it.")));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Refuses_a_request_that_names_no_review()
        {
            var payload = Payload("First: Fail", "Fix it.");
            payload.ReviewId = "not-a-guid";

            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(Service(ContactId), ContactId, payload));
            Assert.StartsWith(CommandHelpers.ValidationPrefix, thrown.Message);
        }

        [Fact]
        public void Refuses_an_action_longer_than_the_column_holds()
        {
            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(
                    Service(ContactId), ContactId, Payload("First: Fail", new string('x', RemedialActions.MaxLength + 1))));

            Assert.Contains("'First: Fail'", thrown.Message);
        }

        [Fact]
        public void Emptying_every_box_clears_the_parked_map()
        {
            var svc = Service(ContactId);
            svc.Row("al_reviewinstance", ReviewId)[RemedialActions.PendingAttr] = "[{\"item\":\"First\",\"text\":\"Old\"}]";

            RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First", ""));

            Assert.Null(svc.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>(RemedialActions.PendingAttr));
        }

        [Fact]
        public void The_plugin_ignores_its_own_clear()
        {
            var svc = Service(ContactId);
            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityId = ContactId;
            provider.Context.InputParameters["Target"] = new Entity("contact", ContactId)
            {
                [RemedialActionsRequestPlugin.RequestAttr] = null,
            };

            new RemedialActionsRequestPlugin(null, null).Execute(provider);

            Assert.Empty(svc.Updates);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~RemedialActionsRequestPluginTests"`
Expected: build FAILS, `RemedialActionsRequestPlugin` not found.

- [ ] **Step 3: Write the implementation**

Create `plugins/OutcomeTesting.Plugins/RemedialActionsRequestPlugin.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Parks the checker's remedial actions on the review, from the portal (project owner,
    /// 2026-09-29).
    ///
    /// The portal cannot invoke a Custom API (AD-053), so the page PATCHes this JSON onto the
    /// signed-in contact's own row - the Self-scoped contact permission makes that the only
    /// contact it can reach - and this plug-in does the write. The same shape as the claim,
    /// the sign-off, the regrade, the case header and "Who carries this fail".
    ///
    /// The guard is the review: open, and assigned to that contact. Tighter than the case
    /// header's "any open review on this case", because the words belong to one check and the
    /// checker doing it is the only person who should be writing them.
    ///
    /// No audit event: nothing has been decided yet. The checker is filling in a form they have
    /// not submitted, like every answer above it; the submit is what makes it a record.
    ///
    /// Register with:
    /// <c>registerstep &lt;orgUrl&gt; OutcomeTesting.Plugins.RemedialActionsRequestPlugin Update contact 40 al_remedialactionsrequest sync</c>.
    /// </summary>
    public class RemedialActionsRequestPlugin : PluginBase
    {
        public const string RequestAttr = "al_remedialactionsrequest";

        private const string ContactEntity = "contact";
        private const string ReviewEntity = "al_reviewinstance";

        public RemedialActionsRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RemedialActionsRequestPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            object target;
            if (!context.InputParameters.TryGetValue("Target", out target))
            {
                return;
            }

            var entity = target as Entity;
            if (entity == null
                || !string.Equals(entity.LogicalName, ContactEntity, StringComparison.OrdinalIgnoreCase)
                || !entity.Contains(RequestAttr))
            {
                return;
            }

            var raw = entity.GetAttributeValue<string>(RequestAttr);
            if (string.IsNullOrWhiteSpace(raw))
            {
                // The clear below comes back through the pipeline as an update to null.
                return;
            }

            var payload = RemedialActionsRequestPayload.Parse(raw);
            if (payload == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The remedial actions could not be read.");
            }

            Apply(service, context.PrimaryEntityId, payload);
        }

        /// <summary>
        /// Validates the request against the review and parks it. Public and static so the rule
        /// is testable without a plug-in context. Every save carries the page's whole map, so
        /// this replaces what was parked rather than merging into it.
        /// </summary>
        public static void Apply(IOrganizationService service, Guid contactId, RemedialActionsRequestPayload payload)
        {
            Guid reviewId;
            if (payload == null || !Guid.TryParse(payload.ReviewId, out reviewId) || reviewId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The remedial actions must name the check they belong to.");
            }

            var review = service.Retrieve(ReviewEntity, reviewId, new ColumnSet("al_assignedcontactid", "al_submittedon"));

            if (review.GetAttributeValue<DateTime?>("al_submittedon").HasValue)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This check has been submitted, so its remedial actions can no longer be changed here.");
            }

            var assigned = review.GetAttributeValue<EntityReference>("al_assignedcontactid");
            if (assigned == null || assigned.Id != contactId)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Only the checker assigned to this check can write its remedial actions.");
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in payload.Actions ?? new List<RemedialActionEntry>())
            {
                if (entry == null)
                {
                    continue;
                }

                var item = (entry.Item ?? string.Empty).Trim();
                var text = (entry.Text ?? string.Empty).Trim();
                if (item.Length == 0 || text.Length == 0)
                {
                    continue;
                }

                if (text.Length > RemedialActions.MaxLength)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "A remedial action can be at most "
                        + RemedialActions.MaxLength + " characters. Shorten the one for '" + item + "'.");
                }

                map[item] = text;
            }

            // Cleared first, in the same transaction: a refusal below rolls it back with it.
            service.Update(new Entity(ContactEntity, contactId) { [RequestAttr] = null });

            service.Update(new Entity(ReviewEntity, reviewId)
            {
                [RemedialActions.PendingAttr] = RemedialActions.Serialise(map),
            });
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~RemedialActionsRequestPluginTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/RemedialActionsRequestPlugin.cs plugins/OutcomeTesting.Plugins.Tests/RemedialActionsRequestPluginTests.cs
git commit -m "feat(remediation): the portal's write path for the checker's remedial actions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: `Remediation.Raise` stamps each action with the checker's words

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/Remediation.cs` (`Raise` at ~line 682, `RaiseOne` at ~line 759)
- Test: `plugins/OutcomeTesting.Plugins.Tests/RemediationTests.cs` (append)

**Interfaces:**
- Consumes: `RemedialActions.ActionAttr`, `RemedialActions.MaxLength` (Task 1)
- Produces: `Remediation.Raise(..., DateTime raisedOn, IList<string> remedialActions = null, string overallRemedialAction = null)`. `remedialActions` is parallel to `items` (blank items included); `overallRemedialAction` goes on the un-indexed action.

- [ ] **Step 1: Write the failing tests**

Append to the `RemediationTests` class in `plugins/OutcomeTesting.Plugins.Tests/RemediationTests.cs` (it already has `Monday` and the `System.Linq` / `System.Collections.Generic` usings):

```csharp
        [Fact]
        public void Stamps_each_action_with_the_checkers_remedial_action()
        {
            var service = new FakeOrganizationService();

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-020",
                Guid.NewGuid(),
                1,
                "Fail",
                null,
                new List<string> { "First issue", "Second issue" },
                null,
                Monday,
                new List<string> { "Fix the first.", "Fix the second." });

            Assert.Equal(
                new[] { "Fix the first.", "Fix the second." },
                service.Creates.Select(c => c.GetAttributeValue<string>(RemedialActions.ActionAttr)).ToArray());
        }

        [Fact]
        public void Keeps_each_remedial_action_with_its_item_when_a_blank_item_is_skipped()
        {
            var service = new FakeOrganizationService();

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-021",
                Guid.NewGuid(),
                1,
                "Fail",
                null,
                new List<string> { "First issue", "  ", "Third issue" },
                null,
                Monday,
                new List<string> { "Fix the first.", "ignored", "Fix the third." });

            Assert.Equal(
                new[] { "Fix the first.", "Fix the third." },
                service.Creates.Select(c => c.GetAttributeValue<string>(RemedialActions.ActionAttr)).ToArray());
        }

        [Fact]
        public void Stamps_the_single_action_with_the_overall_remedial_action()
        {
            var service = new FakeOrganizationService();

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-022",
                Guid.NewGuid(),
                1,
                "Pass with issues",
                null,
                new List<string>(),
                null,
                Monday,
                null,
                "Explain the rationale to the client.");

            Assert.Equal(
                "Explain the rationale to the client.",
                Assert.Single(service.Creates).GetAttributeValue<string>(RemedialActions.ActionAttr));
        }

        [Fact]
        public void Leaves_the_column_absent_when_the_checker_wrote_nothing()
        {
            var service = new FakeOrganizationService();

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-023",
                Guid.NewGuid(),
                1,
                "Fail",
                null,
                new List<string> { "First issue" },
                null,
                Monday);

            Assert.False(Assert.Single(service.Creates).Contains(RemedialActions.ActionAttr));
        }

        [Fact]
        public void Clips_an_over_long_remedial_action_to_the_column()
        {
            var service = new FakeOrganizationService();

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-024",
                Guid.NewGuid(),
                1,
                "Fail",
                null,
                new List<string> { "First issue" },
                null,
                Monday,
                new List<string> { new string('x', RemedialActions.MaxLength + 10) });

            Assert.Equal(
                RemedialActions.MaxLength,
                Assert.Single(service.Creates).GetAttributeValue<string>(RemedialActions.ActionAttr).Length);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~RemediationTests"`
Expected: build FAILS: `No overload for method 'Raise' takes 11 arguments`.

- [ ] **Step 3: Write the implementation**

In `Remediation.cs`, change the `Raise` signature and body. Replace from `public static IList<Guid> Raise(` to the end of that method with:

```csharp
        public static IList<Guid> Raise(
            IOrganizationService service,
            EntityReference caseRef,
            string caseReference,
            Guid reviewId,
            int sequence,
            string reason,
            string observation,
            IList<string> items,
            EntityReference adviserContact,
            DateTime raisedOn,
            IList<string> remedialActions = null,
            string overallRemedialAction = null)
        {
            var raised = new List<Guid>();

            // No item list is not a reason to raise nothing: a grade can require remediation
            // with no pass/fail test point behind it, and that case keeps the un-indexed code
            // it has always had, so a replay still finds the row it raised.
            if (items == null || items.Count == 0)
            {
                raised.Add(RaiseOne(
                    service,
                    caseRef,
                    caseReference,
                    reviewId,
                    ActionCode(caseReference, sequence),
                    Describe(reason, observation, null),
                    adviserContact,
                    raisedOn,
                    overallRemedialAction));
                return raised;
            }

            // Indexed by position in the list the checker's words were aligned to, which
            // includes any blank item - so a skipped blank does not shift every later action
            // onto its neighbour's words.
            var index = 0;
            for (var position = 0; position < items.Count; position++)
            {
                var item = items[position];
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                index++;
                raised.Add(RaiseOne(
                    service,
                    caseRef,
                    caseReference,
                    reviewId,
                    ActionCode(caseReference, sequence, index),
                    DescribeItem(reason, observation, item),
                    adviserContact,
                    raisedOn,
                    remedialActions != null && position < remedialActions.Count ? remedialActions[position] : null));
            }

            // Every item was blank, which the list should never carry - fall back to the one
            // action rather than leaving the case in remediation with nothing to do.
            if (raised.Count == 0)
            {
                raised.Add(RaiseOne(
                    service,
                    caseRef,
                    caseReference,
                    reviewId,
                    ActionCode(caseReference, sequence),
                    Describe(reason, observation, null),
                    adviserContact,
                    raisedOn,
                    overallRemedialAction));
            }

            return raised;
        }
```

Extend the XML doc comment above `Raise` with one paragraph:

```csharp
        /// <paramref name="remedialActions"/> is the checker's remedial action for each item,
        /// parallel to <paramref name="items"/>, and <paramref name="overallRemedialAction"/> is
        /// the one for a check with nothing itemised (project owner, 2026-09-29). Either may be
        /// null: an action raised without words still raises, and the adviser can still answer it.
```

Change `RaiseOne`'s signature to add a trailing `string remedialAction` parameter, and just before `if (adviserContact != null)` add:

```csharp
            // The checker's words, written once, here. RemediationResponseGuardPlugin refuses
            // every later write to the column, so this is the only moment it can be set.
            var remedial = (remedialAction ?? string.Empty).Trim();
            if (remedial.Length > 0)
            {
                action[RemedialActions.ActionAttr] = remedial.Length <= RemedialActions.MaxLength
                    ? remedial
                    : remedial.Substring(0, RemedialActions.MaxLength);
            }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~RemediationTests"`
Expected: PASS, including every existing `RemediationTests` case (the replay test still asserts `Assert.Empty(service.Updates)`).

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/Remediation.cs plugins/OutcomeTesting.Plugins.Tests/RemediationTests.cs
git commit -m "feat(remediation): each raised action carries the checker's remedial action

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The submit gate, and handing each review's words to `Raise`

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs` (`FinaliseReview` ~line 932-1019, `RaiseRemediation` ~line 1312-1353)
- Modify (fixtures only): `plugins/OutcomeTesting.Plugins.Tests/TaxFailReachesAqsTests.cs`, and whichever of `TaxOutcomeStampTests.cs`, `RouteChangeOrphanTests.cs`, `SubmitReviewCallerTests.cs` fail in Step 5
- Test: `plugins/OutcomeTesting.Plugins.Tests/RemedialActionsSubmitTests.cs`

**Interfaces:**
- Consumes: `RemedialActions.EnsureWritten`, `Pending`, `Align`, `TextFor`, `OverallKey`, `PendingAttr`, `ActionAttr` (Task 1); `Remediation.Raise(..., remedialActions, overallRemedialAction)` (Task 3)
- Produces: nothing new publicly. The behaviour is that a submit owing remediation is refused without the words, and the raised actions carry them.

- [ ] **Step 1: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/RemedialActionsSubmitTests.cs`:

```csharp
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The submit refuses a check that owes a remediation while any remedial action is
    /// unwritten, and hands each review's words to the actions it raises (project owner,
    /// 2026-09-29). Driven through the real Submit, Tax then AQS, because the Tax words are
    /// written at one submit and raised at the next (AD-184).
    /// </summary>
    public class RemedialActionsSubmitTests
    {
        private static readonly Guid CaseId = Guid.Parse("e1e1e1e1-0001-4001-8001-e1e1e1e1e1e1");
        private static readonly Guid RouteId = Guid.Parse("e2e2e2e2-0002-4002-8002-e2e2e2e2e2e2");
        private static readonly Guid TaxReviewId = Guid.Parse("e3e3e3e3-0003-4003-8003-e3e3e3e3e3e3");
        private static readonly Guid AqsReviewId = Guid.Parse("e4e4e4e4-0004-4004-8004-e4e4e4e4e4e4");
        private static readonly Guid ChecklistVersionId = Guid.Parse("e5e5e5e5-0005-4005-8005-e5e5e5e5e5e5");

        private static FakeOrganizationService Case(int taxAnswer, int aqsGrade)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", true, "al_requiresaqsreview", true);
            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "RA-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.ReviewInProgress),
                "al_adviseremail", "adviser@example.invalid",
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));
            svc.Seed("al_checklistversion", ChecklistVersionId);

            Review(svc, TaxReviewId, ResponseRules.ReviewTypeTax, 1);
            Review(svc, AqsReviewId, ResponseRules.ReviewTypeAqs, 2);
            Answer(svc, TaxReviewId, "Q-TAX-02", taxAnswer);
            Answer(svc, AqsReviewId, GradingRules.GradeQuestionCode, aqsGrade);
            return svc;
        }

        private static void Review(FakeOrganizationService svc, Guid id, int reviewType, int sequence)
        {
            svc.Seed(
                "al_reviewinstance", id,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistVersionId),
                "al_sequence", sequence,
                "statecode", new OptionSetValue(0));
        }

        private static void Answer(FakeOrganizationService svc, Guid reviewId, string questionCode, int choice)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            svc.Seed("al_question", questionId, "al_questioncode", questionCode);
            svc.Seed("al_questionversion", versionId, "al_questionid", new EntityReference("al_question", questionId));
            svc.Seed(
                "al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "al_answerchoice", new OptionSetValue(choice),
                "statecode", new OptionSetValue(0));
        }

        private static void Park(FakeOrganizationService svc, Guid reviewId, string overall)
        {
            svc.Row("al_reviewinstance", reviewId)[RemedialActions.PendingAttr] =
                "[{\"item\":\"" + RemedialActions.OverallKey + "\",\"text\":\"" + overall + "\"}]";
        }

        private static void Submit(FakeOrganizationService svc, Guid reviewId)
        {
            SubmitReviewPlugin.Submit(
                svc, reviewId, "ra-" + Guid.NewGuid().ToString("N"), null,
                Guid.NewGuid(), Guid.NewGuid(), requireCallerOwnsReview: false, details: "remedial actions");
        }

        private static Entity[] ActionsOn(FakeOrganizationService svc)
        {
            var query = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet(RemedialActions.ActionAttr),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, CaseId);
            return svc.RetrieveMultiple(query).Entities.ToArray();
        }

        [Fact]
        public void A_tax_fail_with_no_remedial_action_is_refused_at_the_tax_submit()
        {
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePass);

            var thrown = Assert.Throws<InvalidPluginExecutionException>(() => Submit(svc, TaxReviewId));

            Assert.Contains("overall remedial action", thrown.Message);
        }

        [Fact]
        public void The_tax_checkers_words_reach_the_action_the_aqs_submit_raises()
        {
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePass);
            Park(svc, TaxReviewId, "Correct the tax wrapper.");

            Submit(svc, TaxReviewId);
            Assert.Empty(ActionsOn(svc));

            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);

            Assert.Equal("Correct the tax wrapper.", Assert.Single(ActionsOn(svc)).GetAttributeValue<string>(RemedialActions.ActionAttr));
            Assert.Null(svc.Row("al_reviewinstance", TaxReviewId).GetAttributeValue<string>(RemedialActions.PendingAttr));
        }

        [Fact]
        public void An_aqs_grade_that_owes_remediation_is_refused_without_words()
        {
            var svc = Case(ResponseRules.ChoicePass, ResponseRules.ChoicePassWithIssues);

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);

            Assert.Throws<InvalidPluginExecutionException>(() => Submit(svc, AqsReviewId));
        }

        [Fact]
        public void An_aqs_grade_that_owes_remediation_raises_with_the_aqs_words()
        {
            var svc = Case(ResponseRules.ChoicePass, ResponseRules.ChoicePassWithIssues);
            Park(svc, AqsReviewId, "Rewrite the suitability report.");

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);

            Assert.Equal(
                "Rewrite the suitability report.",
                Assert.Single(ActionsOn(svc)).GetAttributeValue<string>(RemedialActions.ActionAttr));
        }

        [Fact]
        public void A_clean_case_owes_no_words_at_all()
        {
            var svc = Case(ResponseRules.ChoicePass, ResponseRules.ChoicePass);

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);

            Assert.Empty(ActionsOn(svc));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~RemedialActionsSubmitTests"`
Expected: FAIL. The two "refused" tests fail (no exception thrown) and the two "words reach" tests fail (`al_remedialaction` is null).

- [ ] **Step 3: Add the gate to `FinaliseReview`**

In the AQS branch, directly after the `gradeRefusal` block and before `CreateOutcome(...)`, insert:

```csharp
                // The checker's remedial actions (project owner, 2026-09-29). Checked before the
                // outcome is created so a refusal leaves nothing half-written - the whole submit
                // is one transaction, but a refusal this early is also one the page can explain.
                if (OutcomeRules.RequiresRemediation(outcomeValue, remedialFlagged))
                {
                    RemedialActions.EnsureWritten(service, targetId, DateTime.UtcNow);
                }
```

In the Tax branch, directly after the `TryTaxResultRequiresRemediation` refusal block and before `var aqsStillToCome = ...`, insert:

```csharp
                // Checked whether or not the raising is deferred to the AQS submit (AD-184): the
                // Tax checker is the one who can write these, and after this submit they cannot.
                if (taxRequiresRemediation || remedialFlagged)
                {
                    RemedialActions.EnsureWritten(service, targetId, DateTime.UtcNow);
                }
```

- [ ] **Step 4: Hand the words to `Raise`**

Replace the body of `RaiseRemediation` (keep its signature and doc comment) with:

```csharp
        {
            var items = new List<string>();
            var texts = new List<string>();
            var overall = new List<string>();
            var drained = new List<Guid>();
            string observation = null;

            // Tax first: it was checked first, and an adviser reading a combined list will
            // look for the Tax points where the Tax check left them.
            if (deferredTax != null)
            {
                Collect(service, deferredTax.ReviewId, items, texts, overall, drained);
                observation = AnswerTextFor(service, deferredTax.ReviewId, TaxObservationQuestionCode);
            }

            Collect(service, reviewId, items, texts, overall, drained);

            var own = AnswerTextFor(service, reviewId, observationQuestionCode);
            if (!string.IsNullOrWhiteSpace(own))
            {
                // Both checkers' words when both wrote some, rather than one silently winning.
                observation = string.IsNullOrWhiteSpace(observation) ? own : observation + " " + own;
            }

            Remediation.Raise(
                service,
                caseRef,
                caseReference,
                reviewId,
                sequence,
                reason,
                observation,
                items,
                Remediation.AdviserContact(service, caseRef),
                DateTime.UtcNow,
                texts,
                overall.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, overall));

            // Cleared once raised, on every review the words came from - the deferred Tax leg's
            // included - so a later regrade cannot raise from words about a result that no
            // longer stands.
            foreach (var drainedId in drained)
            {
                service.Update(new Entity(ReviewEntity, drainedId) { [RemedialActions.PendingAttr] = null });
            }
        }

        /// <summary>
        /// One review's items and the words its own checker parked for them, added to the
        /// running lists. Aligned per review rather than across the combined list, because a
        /// Tax-then-AQS case can tick the same fail point on both checks, and each action must
        /// carry the words of the checker who marked it.
        /// </summary>
        private static void Collect(
            IOrganizationService service,
            Guid reviewId,
            List<string> items,
            List<string> texts,
            List<string> overall,
            List<Guid> drained)
        {
            var reviewItems = Remediation.NonPassItems(service, reviewId, DateTime.UtcNow);
            var map = RemedialActions.Pending(service, reviewId);

            items.AddRange(reviewItems);
            texts.AddRange(RemedialActions.Align(reviewItems, map));

            var general = RemedialActions.TextFor(map, RemedialActions.OverallKey);
            if (general != null)
            {
                overall.Add(general);
            }

            if (map.Count > 0)
            {
                drained.Add(reviewId);
            }
        }
```

- [ ] **Step 5: Run the whole plug-in suite and repair the end-to-end fixtures**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: `RemedialActionsSubmitTests` PASS. Existing end-to-end submit tests that submit a non-pass (for example `TaxFailReachesAqsTests`) now FAIL with "Write the overall remedial action...". That is the gate doing its job, not a regression.

For each failing file, park an overall action on every review its seeding helper creates. In `TaxFailReachesAqsTests.Review(...)` add one attribute pair to the `svc.Seed("al_reviewinstance", id, ...)` call:

```csharp
                "al_pendingremedialactions", "[{\"item\":\"__overall__\",\"text\":\"Put it right.\"}]",
```

Add the same pair to the review seed in any of `TaxOutcomeStampTests.cs`, `RouteChangeOrphanTests.cs` and `SubmitReviewCallerTests.cs` that fail. Change nothing else in those files. If a test still fails after that, stop and report it: it is not a fixture problem.

Re-run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs plugins/OutcomeTesting.Plugins.Tests/
git commit -m "feat(submit): refuse a remediation without the checker's remedial actions, and raise with them

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Completion needs Action performed; the guard protects the new columns

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/CompleteRemediationPlugin.cs` (~line 136-170)
- Modify: `plugins/OutcomeTesting.Plugins/RemediationResponseGuardPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/CompleteRemediationCallerTests.cs` (append), `plugins/OutcomeTesting.Plugins.Tests/RemediationResponseGuardTests.cs` (append)

**Interfaces:**
- Consumes: `RemedialActions.ActionAttr`, `ActionPerformedAttr`, `ActionPerformedYes`, `ActionPerformedNo` (Task 1)
- Produces: `public static string CompleteRemediationPlugin.CompletionRefusal(Entity action)`; `public static string RemediationResponseGuardPlugin.WriteOnceRefusal(Entity update)`; `"al_actionperformed"` joins `RemediationResponseGuardPlugin.ResponseColumns`

- [ ] **Step 1: Write the failing tests**

Append to `CompleteRemediationCallerTests`:

```csharp
        [Fact]
        public void A_row_carrying_the_checkers_action_completes_on_an_answer_not_on_text()
        {
            var action = new Entity("al_remediationaction", ActionId)
            {
                [RemedialActions.ActionAttr] = "Re-verify the ID.",
            };
            Assert.Contains("Action performed", CompleteRemediationPlugin.CompletionRefusal(action));

            action[RemedialActions.ActionPerformedAttr] = new OptionSetValue(RemedialActions.ActionPerformedNo);
            Assert.Null(CompleteRemediationPlugin.CompletionRefusal(action));
        }

        [Fact]
        public void A_row_raised_before_the_change_still_needs_the_advisers_words()
        {
            var action = new Entity("al_remediationaction", ActionId);
            Assert.Contains("Record what you did", CompleteRemediationPlugin.CompletionRefusal(action));

            action["al_adviserresponse"] = "Rebuilt the report.";
            Assert.Null(CompleteRemediationPlugin.CompletionRefusal(action));
        }

        [Fact]
        public void A_no_with_no_note_completes()
        {
            // A No goes to the supervisor, who can reject it back (project owner, 2026-09-29).
            var svc = Action(response: null);
            svc.Row("al_remediationaction", ActionId)[RemedialActions.ActionAttr] = "Re-verify the ID.";
            svc.Row("al_remediationaction", ActionId)[RemedialActions.ActionPerformedAttr] =
                new OptionSetValue(RemedialActions.ActionPerformedNo);

            Complete(svc, OwnerId, requireCallerOwnsAction: true);

            Assert.Equal(
                StatusCompleted,
                svc.Row("al_remediationaction", ActionId).GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
        }
```

Append to `RemediationResponseGuardTests`:

```csharp
        [Fact]
        public void The_checkers_remedial_action_cannot_be_written_after_it_is_raised()
        {
            var update = new Entity("al_remediationaction", ActionId) { [RemedialActions.ActionAttr] = "Changed." };

            var refusal = RemediationResponseGuardPlugin.WriteOnceRefusal(update);

            Assert.NotNull(refusal);
            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal);
            Assert.Null(RemediationResponseGuardPlugin.WriteOnceRefusal(Write(response: "A note.")));
        }

        [Fact]
        public void Action_performed_freezes_once_the_action_is_completed()
        {
            var current = Action(Remediation.StatusCompleted);
            current[RemedialActions.ActionPerformedAttr] = new OptionSetValue(RemedialActions.ActionPerformedYes);
            var update = new Entity("al_remediationaction", ActionId)
            {
                [RemedialActions.ActionPerformedAttr] = new OptionSetValue(RemedialActions.ActionPerformedNo),
            };

            Assert.NotNull(RemediationResponseGuardPlugin.Refusal(current, update));
        }

        [Fact]
        public void Action_performed_opens_again_when_a_rejection_reopens_the_action()
        {
            var current = Action(Remediation.StatusInProgress);
            current[RemedialActions.ActionPerformedAttr] = new OptionSetValue(RemedialActions.ActionPerformedYes);
            var update = new Entity("al_remediationaction", ActionId)
            {
                [RemedialActions.ActionPerformedAttr] = new OptionSetValue(RemedialActions.ActionPerformedNo),
            };

            Assert.Null(RemediationResponseGuardPlugin.Refusal(current, update));
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~CompleteRemediationCallerTests|FullyQualifiedName~RemediationResponseGuardTests"`
Expected: build FAILS: `CompletionRefusal` / `WriteOnceRefusal` not found.

- [ ] **Step 3: Implement the completion rule**

In `CompleteRemediationPlugin.Complete`, extend the retrieve's column set:

```csharp
                new ColumnSet(ActionStatus, "ownerid", ActionAdviserResponse, "al_outcomecaseid", ReviewLookup,
                    RemedialActions.ActionAttr, RemedialActions.ActionPerformedAttr),
```

Replace the `if (string.IsNullOrWhiteSpace(action.GetAttributeValue<string>(ActionAdviserResponse))) { throw ... }` block with:

```csharp
            var completionRefusal = CompletionRefusal(action);
            if (completionRefusal != null)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + completionRefusal);
            }
```

Add to the class:

```csharp
        /// <summary>
        /// What an action still needs before it can be completed, or null when nothing.
        ///
        /// Two rules, chosen by the row (project owner, 2026-09-29). A row carrying the
        /// checker's remedial action asks the adviser only whether it was performed - Yes or
        /// No, and a No still completes, because the supervisor decides what a No means. A row
        /// raised before the change has no checker's words, so the adviser's own text is still
        /// the thing the supervisor attests to (BR-008) and is still required.
        /// </summary>
        public static string CompletionRefusal(Entity action)
        {
            if (!string.IsNullOrWhiteSpace(action.GetAttributeValue<string>(RemedialActions.ActionAttr)))
            {
                return action.GetAttributeValue<OptionSetValue>(RemedialActions.ActionPerformedAttr) == null
                    ? "Answer 'Action performed' (Yes or No) for this action before marking it complete."
                    : null;
            }

            return string.IsNullOrWhiteSpace(action.GetAttributeValue<string>(ActionAdviserResponse))
                ? "Record what you did about this action before marking it complete."
                : null;
        }
```

- [ ] **Step 4: Implement the guard changes**

In `RemediationResponseGuardPlugin`:

1. Add `"al_actionperformed",` to `ResponseColumns`, after `"al_clientcontactrequired",`. Add a line to its doc comment: "`al_actionperformed` joined on 2026-09-29: the adviser's Yes / No is what the supervisor attests to on a row carrying the checker's remedial action."
2. In `ExecuteDataversePlugin`, directly after the `TcOnlyRefusal` block, insert:

```csharp
            var rewrite = WriteOnceRefusal(update);
            if (rewrite != null)
            {
                throw new InvalidPluginExecutionException(rewrite);
            }
```

3. Add the method:

```csharp
        /// <summary>
        /// The refusal for any write to the checker's remedial action after it was raised, or
        /// null when the write does not touch it.
        ///
        /// <c>Remediation.Raise</c> sets the column on Create, and there is no legitimate later
        /// writer: the words are the checker's, the check is submitted, and a portal write
        /// cannot say who made it (AD-053). So this is refused on presence, for the reason
        /// <see cref="TcOnlyRefusal"/> gives, rather than on change.
        /// </summary>
        public static string WriteOnceRefusal(Entity update)
        {
            if (update == null || !update.Contains(RemedialActions.ActionAttr))
            {
                return null;
            }

            return CommandHelpers.PreconditionPrefix +
                "The remedial action is the checker's, set when the check was submitted, and cannot be changed here.";
        }
```

4. Update the class doc comment's `registerstep` line and the "All five filtering attributes" paragraph to list seven: `al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice,al_actionperformed,al_remedialaction`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/CompleteRemediationPlugin.cs plugins/OutcomeTesting.Plugins/RemediationResponseGuardPlugin.cs plugins/OutcomeTesting.Plugins.Tests/CompleteRemediationCallerTests.cs plugins/OutcomeTesting.Plugins.Tests/RemediationResponseGuardTests.cs
git commit -m "feat(remediation): complete on Action performed; the checker's words are write-once

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The emailed remediation PDF draws Action performed

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/RemediationDocument.cs` (table at ~line 89, band ~line 108, details ~line 146, form grid ~line 186, column set ~line 244)
- Test: `plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs`

**Interfaces:**
- Consumes: `RemedialActions.ActionAttr`, `ActionPerformedAttr`, `ActionPerformedLabel` (Task 1)

- [ ] **Step 1: Write the failing test and move the existing assertion**

In `CompletedCheckDocumentTests.The_remediation_form_numbers_the_issues_and_says_who_completed_them`, change `first.Cells[7].Text` to `first.Cells[8].Text` (Sign-off moves one column right). Then append:

```csharp
        [Fact]
        public void The_remediation_form_draws_the_checkers_action_and_whether_it_was_performed()
        {
            var service = Case();
            var action = Action(service, completed: true);
            action[RemedialActions.ActionAttr] = "Re-verify the client's ID.";
            action[RemedialActions.ActionPerformedAttr] = new OptionSetValue(RemedialActions.ActionPerformedYes);
            action["al_adviserresponse"] = "Done on 26 Sep.";

            var blocks = RemediationDocument.Blocks(service, Ref(), new DateTime(2026, 9, 29));
            var table = Tables(blocks).First(t => t.Rows[0].Cells[0].Text == "No.");

            Assert.Equal("Action performed", table.Rows[0].Cells[3].Text);
            var first = table.Rows.First(r => r.Cells[0].Text == "1");
            Assert.Equal("Re-verify the client's ID.", first.Cells[2].Text);
            Assert.Equal("Yes\nDone on 26 Sep.", first.Cells[3].Text);
        }
```

If `PdfCell.Text` joins runs with something other than `\n`, match what `Stacked(...)` produces in the existing header assertion (`"OUTCOME\nInsufficient evidence"`) and build the cell the same way.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~CompletedCheckDocumentTests"`
Expected: FAIL on both (index 8 does not exist yet; no "Action performed" header).

- [ ] **Step 3: Implement**

In `RemediationDocument.cs`:

```csharp
            var table = new PdfTable(0.04, 0.18, 0.18, 0.1, 0.08, 0.08, 0.08, 0.08, 0.18);
            table.AddHeader(
                PdfCell.Head("No."), PdfCell.Head("Issue / fail reason"), PdfCell.Head("Remedial action"),
                PdfCell.Head("Action performed"), PdfCell.Head("Owner"), PdfCell.Head("Target date"),
                PdfCell.Head("Status"), PdfCell.Head("Age"), PdfCell.Head("Sign-off"));
```

The band: `new PdfCell { Span = 9, Fill = PdfCell.BandFill }`.

The `details` array becomes:

```csharp
                var checkerAction = action.GetAttributeValue<string>(RemedialActions.ActionAttr);
                var adviserText = action.GetAttributeValue<string>("al_adviserresponse");

                var details = new[]
                {
                    // The checker's words where the row has them (2026-09-29); a row raised
                    // before that carries the adviser's own remedial action, as it always did.
                    Text(string.IsNullOrWhiteSpace(checkerAction) ? adviserText : checkerAction),
                    Performed(action, checkerAction, adviserText),
                    owner != null && !string.IsNullOrWhiteSpace(owner.Name) ? owner.Name : "Nobody assigned",
                    Day(action.GetAttributeValue<DateTime?>("al_duedate")),
                    status == null ? "—" : labels.Label("al_remediationaction", "al_actionstatus", status.Value),
                    opened.HasValue ? Age(opened.Value, completed ?? today) : "—",
                    SignOff(latest, completed, labels),
                };
```

Add the helper to the class:

```csharp
        /// <summary>
        /// The Action performed cell: the adviser's Yes / No with their optional note under it.
        /// A row raised before the checker wrote remedial actions has nothing to answer here.
        /// </summary>
        private static string Performed(Entity action, string checkerAction, string adviserText)
        {
            if (string.IsNullOrWhiteSpace(checkerAction))
            {
                return "—";
            }

            var answer = action.GetAttributeValue<OptionSetValue>(RemedialActions.ActionPerformedAttr);
            var label = RemedialActions.ActionPerformedLabel(answer == null ? (int?)null : answer.Value) ?? "—";
            return string.IsNullOrWhiteSpace(adviserText) ? label : label + "\n" + adviserText.Trim();
        }
```

The two form-grid rows each gain one span on their last cell so they fill nine columns: change the fourth cell in each `table.Add(...)` from span `2` to span `3`.

Add `RemedialActions.ActionAttr, RemedialActions.ActionPerformedAttr` to the `ColumnSet` in `Actions(...)`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/RemediationDocument.cs plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs
git commit -m "feat(pdf): the remediation form draws Action performed

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The review page's "Fail points and remedial actions" card

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`
- Test: `app/src/features/reviews/portalRemedialActions.test.ts`

**Interfaces:**
- Consumes: the portal request column `al_remedialactionsrequest` and payload shape (Task 2); the gate's refusal text (Task 1)
- Produces: `window.otRemedial = { missing(): string|null, flush(done: (ok: boolean) => void) }`, and the pure function `otRemedialItems(answers, failPoints, isTax)` between the markers `/* ot-remedial-items:start */` and `/* ot-remedial-items:end */`

- [ ] **Step 1: Write the failing tests**

Create `app/src/features/reviews/portalRemedialActions.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * The checker writes a remedial action for every fail point before submitting (project
 * owner, 2026-09-29). The server's list (Remediation.NonPassItems) is the one that counts;
 * the page mirrors it so the checker sees the rows as they tick, and a disagreement surfaces
 * as a refused submit naming the item. The mirror is the part most likely to drift, so it is
 * executed here rather than only read.
 */

const template = reviewTemplate.replace(/\r\n/g, '\n');

type Answer = { code: string; type: string; text: string; value: string };
type Items = (answers: Answer[], failPoints: string[], isTax: boolean) => { owed: boolean; items: string[] };

function itemsFunction(): Items {
  const start = template.indexOf('/* ot-remedial-items:start */');
  const end = template.indexOf('/* ot-remedial-items:end */');
  expect(start).toBeGreaterThan(-1);
  expect(end).toBeGreaterThan(start);
  const source = template.slice(start, end);
  return new Function(`${source}; return otRemedialItems;`)() as Items;
}

const PASS_FAIL_INSUFFICIENT = '120910006';
const YES_NO_NA = '120910008';
const YES_NO_INSUFFICIENT = '120910009';
const YES_NO = '120910007';

const answer = (code: string, type: string, text: string, value: string): Answer => ({ code, type, text, value });

describe('the fail points the page lists', () => {
  const items = itemsFunction();

  it('lists each non-pass answer as "question: answer", then each ticked fail point', () => {
    const result = items(
      [
        answer('Q-E1-01', PASS_FAIL_INSUFFICIENT, ' Client objectives clearly evidenced ', '120910301'),
        answer('Q-E1-02', PASS_FAIL_INSUFFICIENT, 'Risk profile', '120910300'),
        answer('Q-AML-01', YES_NO_NA, 'ID verification completed', '120910306'),
        answer('Q-CD-01', YES_NO_INSUFFICIENT, 'Products and services', '120910302'),
        answer('Q-GR-01', '120910010', 'Advice Quality Grade', '120910303'),
      ],
      [' AML - No CRA completed or missing data fields '],
      false,
    );

    expect(result.items).toEqual([
      'Client objectives clearly evidenced: Fail',
      'ID verification completed: No',
      'Products and services: Insufficient evidence',
      'AML - No CRA completed or missing data fields',
    ]);
  });

  it('leaves out the outcome questions, N/A, Yes and plain Yes / No', () => {
    const result = items(
      [
        answer('Q-FQ-01', PASS_FAIL_INSUFFICIENT, 'File quality outcome', '120910301'),
        answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910301'),
        answer('Q-AML-02', YES_NO_NA, 'CRA on file', '120910307'),
        answer('Q-CD-02', YES_NO_INSUFFICIENT, 'Price and value', '120910305'),
        answer('Q-FQ-03', YES_NO, 'Remedial action required?', '120910306'),
      ],
      [],
      false,
    );

    expect(result.items).toEqual([]);
  });

  it('keeps the characters Liquid escapes, so the key matches the server', () => {
    const result = items([answer('Q-X', PASS_FAIL_INSUFFICIENT, 'Fees & "charges" <agreed>', '120910301')], [], false);
    expect(result.items).toEqual(['Fees & "charges" <agreed>: Fail']);
  });

  it('says an AQS check owes remediation on a non-pass grade or a Yes to Remedial action required?', () => {
    expect(items([answer('Q-GR-01', '120910010', 'Grade', '120910303')], [], false).owed).toBe(true);
    expect(items([answer('Q-FQ-03', YES_NO, 'Remedial action required?', '120910305')], [], false).owed).toBe(true);
    expect(items([answer('Q-GR-01', '120910010', 'Grade', '120910300')], [], false).owed).toBe(false);
  });

  it('says a Tax check owes remediation on a Fail or Insufficient outcome or a Yes to its flag', () => {
    expect(items([answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910301')], [], true).owed).toBe(true);
    expect(items([answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910302')], [], true).owed).toBe(true);
    expect(items([answer('Q-FQTAX-03', YES_NO, 'Remedial action required?', '120910305')], [], true).owed).toBe(true);
    expect(items([answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910300')], [], true).owed).toBe(false);
  });
});

describe('the card on the page', () => {
  it('sits after "Who carries this fail" and before the submit, hidden until remediation is owed', () => {
    const accountability = template.indexOf('data-ot-accountability\n');
    const card = template.indexOf('data-ot-remedial\n');
    const submit = template.indexOf('data-ot-submit\n');
    expect(accountability).toBeGreaterThan(-1);
    expect(card).toBeGreaterThan(accountability);
    expect(submit).toBeGreaterThan(card);
    expect(template).toContain('<h2 class="ot-card__title" id="ot-remedial-heading">Fail points and remedial actions</h2>');
  });

  it('draws what the checker already parked, from the review', () => {
    expect(template).toContain('<attribute name="al_pendingremedialactions" />');
    expect(template).toContain('data-ot-remedial-saved="{{ rv.al_pendingremedialactions | escape }}"');
  });

  it('gives every answer row its question text, which is half of each item', () => {
    const roots = template.match(/<tr data-ot-answer\n[\s\S]*?>/g) ?? [];
    expect(roots.length).toBe(2);
    for (const root of roots) {
      expect(root).toContain('data-question-text="{{ qv.al_questiontext | escape }}"');
    }
  });

  it('keeps a row\'s words when its fail point is unticked, so ticking it again brings them back', () => {
    // Rows are redrawn from the map on every change; nothing removes an entry from it.
    expect(template).toContain("box.value = texts[keys[i]] || '';");
    expect(template).not.toContain('delete texts[');
  });

  it('saves through the checker\'s own contact row', () => {
    expect(template).toContain('al_remedialactionsrequest: JSON.stringify({ reviewId: reviewId, actions: entries })');
  });

  it('holds the submit until every row has words and the last save has landed', () => {
    const click = template.indexOf("button.addEventListener('click'");
    const missing = template.indexOf('window.otRemedial.missing()', click);
    const flush = template.indexOf('window.otRemedial.flush(function (ok)', click);
    const header = template.indexOf('window.otHeader.flush', click);
    expect(missing).toBeGreaterThan(click);
    expect(flush).toBeGreaterThan(missing);
    expect(header).toBeGreaterThan(flush);
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `app/`): `npx vitest run src/features/reviews/portalRemedialActions.test.ts`
Expected: FAIL. The markers are not found (`expected -1 to be greater than -1`) and the card strings are missing.

- [ ] **Step 3: Add the question text to the two answer roots**

In the template, the grid row (`<tr data-ot-answer` at ~line 1524) and the meta row (`<tr data-ot-answer` at ~line 1608) each get one attribute, placed after `data-question-code="{{ qcode | escape }}"`:

```liquid
                data-question-text="{{ qv.al_questiontext | escape }}"
```

- [ ] **Step 4: Read the parked map with the review**

In the review fetch (~line 133), after `<attribute name="al_pendingaccountability" />`, add:

```liquid
      <attribute name="al_pendingremedialactions" />
```

- [ ] **Step 5: Add the card and its script**

Directly after the accountability block's closing `{% endif %}` (the one after `window.otAccountability = ...` and `</script>`) and before `{% if editable %}` that opens `<section class="ot-card ot-submit"`, insert:

```liquid
    {% if editable %}
      {% comment %}
        Fail points and remedial actions (project owner, 2026-09-29). The checker writes what
        the adviser is to do about each thing they marked down; the submit copies each one onto
        the remediation action it raises, and the adviser then says whether it was performed.

        Drawn by the script, not the Liquid: the rows are the fail points as they stand, and a
        tick autosaves without re-rendering the page, so a server-drawn list would be stale the
        moment the checker changed an answer (the same fault "Who carries this fail" had on
        IO-SEED-TAX-01). The list mirrors Remediation.NonPassItems; the server recomputes it at
        submit and refuses a missing row by name, so the mirror is a convenience, not the rule.

        Hidden until the check owes a remediation, and not rendered on a submitted review.
      {% endcomment %}
      <section class="ot-card ot-remedial"
               data-ot-remedial
               data-review-id="{{ rv.al_reviewinstanceid }}"
               data-review-kind="{% if is_tax_review %}tax{% else %}aqs{% endif %}"
               data-ot-remedial-saved="{{ rv.al_pendingremedialactions | escape }}"
               hidden
               aria-labelledby="ot-remedial-heading">
        <h2 class="ot-card__title" id="ot-remedial-heading">Fail points and remedial actions</h2>
        <p>
          Each fail point below goes to the adviser with the remedial action you write for it.
          Write one for every row before you submit. The adviser answers whether it was performed.
        </p>
        <div class="ot-table-wrap">
          <table>
            <caption class="ot-visually-hidden">Remedial action for each fail point</caption>
            <thead>
              <tr>
                <th scope="col">No.</th>
                <th scope="col">Fail point</th>
                <th scope="col">Remedial action</th>
              </tr>
            </thead>
            <tbody data-ot-remedial-rows></tbody>
          </table>
        </div>
        <p class="ot-accountability__status" data-ot-remedial-status role="status" aria-live="polite"></p>
      </section>

      <script>
        (function () {
          'use strict';
          var root = document.querySelector('[data-ot-remedial]');
          if (!root) { return; }

          var body = root.querySelector('[data-ot-remedial-rows]');
          var status = root.querySelector('[data-ot-remedial-status]');
          var reviewId = root.getAttribute('data-review-id');
          var isTax = root.getAttribute('data-review-kind') === 'tax';
          var OVERALL = '__overall__';

          /* ot-remedial-items:start */
          /*
           * The items the submit will raise, and whether it will raise any. Mirrors
           * Remediation.NonPassItems and SubmitReviewPlugin's remediation rules; executed by
           * portalRemedialActions.test.ts. Labels are al_response's own, not the page's: the
           * Tax grid prints 120910302 as "Pass with issues", the server writes "Insufficient
           * evidence", and the key has to be the server's.
           */
          function otRemedialItems(answers, failPoints, isTax) {
            var LABELS = { '120910301': 'Fail', '120910302': 'Insufficient evidence', '120910304': 'Potential harm', '120910306': 'No' };
            var OUTCOME = { 'Q-TAX-02': true, 'Q-FQ-01': true, 'Q-FQTAX-01': true };
            var SCALE = { '120910005': 'pf', '120910006': 'pf', '120910012': 'pf', '120910009': 'yni', '120910008': 'ynn' };

            function nonPass(type, value) {
              var scale = SCALE[type];
              if (!scale) { return false; }
              if (scale === 'ynn') { return value === '120910306'; }
              if (scale === 'yni') { return value === '120910306' || value === '120910302'; }
              return value === '120910301' || value === '120910302' || value === '120910304';
            }

            var items = [];
            var owed = false;
            for (var i = 0; i < answers.length; i++) {
              var a = answers[i];
              var code = (a.code || '').toUpperCase();
              if (code === 'Q-FQ-03' || code === 'Q-FQTAX-03') {
                if (a.value === '120910305') { owed = true; }
                continue;
              }
              if (!isTax && code === 'Q-GR-01' && a.value && a.value !== '120910300') { owed = true; }
              if (isTax && code === 'Q-TAX-02' && (a.value === '120910301' || a.value === '120910302')) { owed = true; }
              if (OUTCOME[code] || !a.value || !nonPass(a.type, a.value)) { continue; }
              var text = (a.text || '').trim();
              if (text) { items.push(text + ': ' + LABELS[a.value]); }
            }

            for (var f = 0; f < failPoints.length; f++) {
              var name = (failPoints[f] || '').trim();
              if (name) { items.push(name); }
            }

            return { owed: owed, items: items };
          }
          /* ot-remedial-items:end */

          function answers() {
            var roots = document.querySelectorAll('[data-ot-answer]');
            var found = [];
            for (var i = 0; i < roots.length; i++) {
              var picked = roots[i].querySelector('input[type="radio"]:checked, input[type="checkbox"]:checked');
              var select = roots[i].querySelector('select');
              found.push({
                code: roots[i].getAttribute('data-question-code') || '',
                type: roots[i].getAttribute('data-response-type') || '',
                text: roots[i].getAttribute('data-question-text') || '',
                value: picked ? picked.value : (select ? select.value : '')
              });
            }
            return found;
          }

          function failPoints() {
            var ticked = document.querySelectorAll('[data-ot-failpoints-for] [data-ot-reason]:checked');
            var names = [];
            for (var i = 0; i < ticked.length; i++) {
              var label = document.querySelector('label[for="' + ticked[i].id + '"]');
              names.push(label ? label.textContent : '');
            }
            return names;
          }

          function show(message, kind) {
            status.textContent = message;
            status.className = 'ot-accountability__status' + (kind ? ' ot-inline-status--' + kind : '');
          }

          /*
           * Every text the checker has written on this review, drawn or not. A row that drops
           * out of the list keeps its words here, so unticking and re-ticking a fail point does
           * not lose them; the server ignores keys that are no longer items.
           */
          var texts = {};
          try {
            var saved = JSON.parse(root.getAttribute('data-ot-remedial-saved') || '[]') || [];
            for (var s = 0; s < saved.length; s++) {
              if (saved[s] && saved[s].item) { texts[saved[s].item] = saved[s].text || ''; }
            }
          } catch (e) { texts = {}; }

          var current = [];
          var timer = null;
          var saving = false;
          var saveAgain = false;
          var waiting = [];

          function finish(ok) {
            var list = waiting;
            waiting = [];
            for (var i = 0; i < list.length; i++) { list[i](ok); }
          }

          function save(done) {
            if (done) { waiting.push(done); }
            if (timer) { window.clearTimeout(timer); timer = null; }
            if (saving) { saveAgain = true; return; }
            if (!window.shell || typeof window.shell.getTokenDeferred !== 'function') {
              show('This page did not load completely. Refresh and try again.', 'error');
              finish(false);
              return;
            }

            var entries = [];
            for (var key in texts) {
              if (Object.prototype.hasOwnProperty.call(texts, key) && (texts[key] || '').trim()) {
                entries.push({ item: key, text: texts[key] });
              }
            }

            saving = true;
            show('Saving...', null);

            window.shell.getTokenDeferred().done(function (token) {
              var request = new XMLHttpRequest();
              request.open('PATCH', '/_api/contacts({{ user.id }})', true);
              request.setRequestHeader('Content-Type', 'application/json');
              request.setRequestHeader('__RequestVerificationToken', token);
              request.onreadystatechange = function () {
                if (request.readyState !== 4) { return; }
                saving = false;

                if (request.status >= 200 && request.status < 300) {
                  if (saveAgain) { saveAgain = false; save(null); return; }
                  var now = new Date();
                  show('Saved ' + ('0' + now.getHours()).slice(-2) + ':' + ('0' + now.getMinutes()).slice(-2), 'success');
                  finish(true);
                  return;
                }

                saveAgain = false;
                var message = 'Your remedial actions could not be saved (' + request.status + ').';
                try {
                  var parsed = JSON.parse(request.responseText);
                  if (parsed && parsed.error && parsed.error.message) {
                    message = parsed.error.message
                      .replace('PRECONDITION: ', '').replace('VALIDATION: ', '').replace('UNAUTHORIZED: ', '');
                  }
                } catch (e) { /* the status code is all there is */ }
                show(message, 'error');
                finish(false);
              };
              request.send(JSON.stringify({
                al_remedialactionsrequest: JSON.stringify({ reviewId: reviewId, actions: entries })
              }));
            }).fail(function () {
              saving = false;
              show('This page did not load completely. Refresh and try again.', 'error');
              finish(false);
            });
          }

          function queue() {
            if (timer) { window.clearTimeout(timer); }
            timer = window.setTimeout(function () { save(null); }, 700);
          }

          function onInput(event) {
            texts[event.target.getAttribute('data-ot-remedial-item')] = event.target.value;
            queue();
          }

          function draw() {
            var result = otRemedialItems(answers(), failPoints(), isTax);
            root.hidden = !result.owed;

            var keys = result.items.length ? result.items : [OVERALL];
            if (keys.join('\n') === current.join('\n')) { return; }
            current = keys;

            while (body.firstChild) { body.removeChild(body.firstChild); }
            for (var i = 0; i < keys.length; i++) {
              var row = document.createElement('tr');
              var number = document.createElement('td');
              number.textContent = String(i + 1);
              var label = document.createElement('td');
              label.textContent = keys[i] === OVERALL ? 'Overall' : keys[i];
              var cell = document.createElement('td');
              var box = document.createElement('textarea');
              box.rows = 3;
              box.maxLength = 4000;
              box.required = true;
              box.className = 'ot-response-cell__text';
              box.setAttribute('aria-label', 'Remedial action for ' + (keys[i] === OVERALL ? 'this check' : keys[i]));
              box.setAttribute('data-ot-remedial-item', keys[i]);
              box.value = texts[keys[i]] || '';
              box.addEventListener('input', onInput);
              box.addEventListener('blur', function () { save(null); });
              cell.appendChild(box);
              row.appendChild(number);
              row.appendChild(label);
              row.appendChild(cell);
              body.appendChild(row);
            }
          }

          function missing() {
            if (root.hidden) { return null; }
            for (var i = 0; i < current.length; i++) {
              if (!(texts[current[i]] || '').trim()) {
                return current[i] === OVERALL
                  ? 'Write the overall remedial action under "Fail points and remedial actions" before submitting.'
                  : 'Write the remedial action for "' + current[i] + '" before submitting.';
              }
            }
            return null;
          }

          // Any change outside the card may move the list: an answer, a fail point tick.
          document.addEventListener('change', function (event) {
            if (event.target && !root.contains(event.target)) { draw(); }
          });

          draw();

          window.otRemedial = {
            missing: missing,
            flush: function (done) {
              if (root.hidden) { done(true); return; }
              save(done);
            }
          };
        })();
      </script>
    {% endif %}
```

- [ ] **Step 6: Hold the submit on the card**

In the submit script's `button.addEventListener('click', function () { ... })`, replace everything from the `// The accountability choice, ...` comment to the end of the handler (the final `submit();` before `});`) with:

```js
            // The accountability choice, if the checker made one a moment ago: it is parked
            // on the review and the submit is what turns it into part of the outcome, so it
            // has to land first.
            if (window.otAccountability && typeof window.otAccountability.flush === 'function') {
              window.otAccountability.flush(null);
            }

            /*
             * The remedial actions next (project owner, 2026-09-29). Checked here so the
             * checker is told before a round trip, and saved with a callback rather than fired
             * and forgotten: the submit reads them off the review, so a save still in its
             * debounce when Submit is pressed would be refused by the server as missing.
             */
            if (window.otRemedial) {
              var unwritten = window.otRemedial.missing();
              if (unwritten) {
                show(unwritten, 'error');
                return;
              }

              setBusy(true);
              show('Saving your remedial actions...', 'info');
              window.otRemedial.flush(function (ok) {
                if (!ok) {
                  show('Your remedial actions could not be saved, so the review was not submitted.'
                    + ' The message under them says why.', 'error');
                  setBusy(false);
                  return;
                }
                afterRemedial();
              });
              return;
            }

            afterRemedial();
          });

          function afterRemedial() {
            if (window.otHeader && typeof window.otHeader.flush === 'function') {
              setBusy(true);
              show('Saving your header changes...', 'info');
              window.otHeader.flush(function (ok) {
                if (!ok) {
                  show('Your header changes could not be saved, so the review was not'
                    + ' submitted. Fix that first - the message beside the header says why.',
                    'error');
                  setBusy(false);
                  return;
                }
                submit();
              });
              return;
            }

            submit();
          }
```

Keep the existing "Any outstanding header edit first" comment block above the accountability flush as it is.

- [ ] **Step 7: Run the tests to verify they pass**

Run (from `app/`): `npx vitest run src/features/reviews`
Expected: PASS, including the existing `portal*.test.ts` files for this template.

Then check the template still balances. Count Liquid tag openers against closers and `<script>` pairs as the 2026-09-13 deployment did:

```bash
f=powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html
grep -o '{% *\(if\|for\|capture\|comment\|unless\) ' "$f" | wc -l; grep -o '{% *end\(if\|for\|capture\|comment\|unless\) *%}' "$f" | wc -l
grep -c '<script>' "$f"; grep -c '</script>' "$f"
```

Expected: the two Liquid counts equal each other, and the two script counts equal each other.

- [ ] **Step 8: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html app/src/features/reviews/portalRemedialActions.test.ts
git commit -m "feat(portal): the checker writes a remedial action for each fail point before submitting

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: `OT Remediation` and `OT Case Detail` draw Action performed; the adviser answers it

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html` (fetch ~321, header ~545, group ~702, cells ~737-747 and ~802-812, script `rows()` ~1912, `markSaved()` ~1953, pre-flight ~2003, payload ~2094)
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html` (fetch ~167, header ~700, group ~736, cells ~785 and ~820)
- Modify: `powerpages/outcome-testing---outcometesting/sitesetting.yml` (lines 159 and 183)
- Test: `app/src/features/remediation/portalActionPerformed.test.ts`

**Interfaces:**
- Consumes: `al_remedialaction`, `al_actionperformed` (120910815 / 120910816), `al_adviserresponse` as the note (Tasks 1, 5)

- [ ] **Step 1: Write the failing tests**

Create `app/src/features/remediation/portalActionPerformed.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';

/**
 * The remediation form gains "Action performed" between Remedial action and Owner (project
 * owner, 2026-09-29). The checker now writes the remedial action; the adviser answers Yes or
 * No and may add a note. Both templates draw the same columns, or the case record and the
 * remediation page would describe one remediation two ways.
 */

const remediation = remediationTemplate.replace(/\r\n/g, '\n');
const caseDetail = caseTemplate.replace(/\r\n/g, '\n');

describe.each([
  ['OT Remediation', remediation],
  ['OT Case Detail', caseDetail],
])('%s', (_name, template) => {
  it('draws Action performed between Remedial action and Owner', () => {
    expect(template).toContain(
      '<th scope="col">Remedial action</th>\n                <th scope="col">Action performed</th>\n                <th scope="col">Owner</th>',
    );
  });

  it('spans the check heading across all nine columns', () => {
    expect(template).toContain('<th scope="colgroup" colspan="9">');
    expect(template).not.toContain('<th scope="colgroup" colspan="8">');
  });

  it('reads the two new columns', () => {
    expect(template).toContain('<attribute name="al_remedialaction" />');
    expect(template).toContain('<attribute name="al_actionperformed" />');
  });

  it('shows the checker\'s words where the row has them', () => {
    expect(template).toContain('{{ a.al_remedialaction | escape | newline_to_br }}');
  });
});

describe('the adviser\'s controls on OT Remediation', () => {
  it('offers Yes and No with the solution\'s own values', () => {
    expect(remediation).toContain('value="120910815" data-ot-performed-choice');
    expect(remediation).toContain('value="120910816" data-ot-performed-choice');
  });

  it('keeps the adviser\'s words as an optional note', () => {
    expect(remediation).toContain('Note (optional)');
    expect(remediation).toContain('data-ot-performed-note>{{ a.al_adviserresponse | escape }}</textarea>');
  });

  it('asks for an answer, not words, before signing off a new row', () => {
    expect(remediation).toContain('\'Answer "Action performed" for issue \' + pending[i].number + \' before signing off.\'');
  });

  it('sends the answer with the note', () => {
    expect(remediation).toContain('if (row.performed && row.choice !== null) { payload.al_actionperformed = row.choice; }');
  });

  it('collects both kinds of row in document order', () => {
    expect(remediation).toContain("var cells = document.querySelectorAll('[data-ot-response], [data-ot-performed]');");
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `app/`): `npx vitest run src/features/remediation/portalActionPerformed.test.ts`
Expected: FAIL on every assertion.

- [ ] **Step 3: Change `OT Remediation`**

1. In the actions fetch, after `<attribute name="al_adviserresponse" />` (~line 321), add `<attribute name="al_remedialaction" />` and `<attribute name="al_actionperformed" />` on their own lines at the same indent.
2. In the header, after `<th scope="col">Remedial action</th>` add `<th scope="col">Action performed</th>` at the same indent.
3. Change the group heading's `colspan="8"` to `colspan="9"`.
4. In the itemised branch (inside `{% if ot_shown == 1 %}`), replace the whole `{% if user and a.al_assignedcontactid.id == user.id and a.al_actionstatus.value != 120910602 %} ... {% else %} <td rowspan="{{ ot_count }}">{{ a.al_adviserresponse ... }}</td> {% endif %}` block (keeping the comment above it) with:

```liquid
                          {% comment %}
                            From 2026-09-29 the checker writes the remedial action and the
                            adviser answers whether it was performed, with an optional note in
                            al_adviserresponse. A row raised before that has no checker's words
                            and keeps the adviser's own editable remedial action, so a
                            remediation already in flight can still be finished.
                          {% endcomment %}
                          {% assign ot_mine_open = false %}
                          {% if user and a.al_assignedcontactid.id == user.id and a.al_actionstatus.value != 120910602 %}{% assign ot_mine_open = true %}{% endif %}
                          {% if a.al_remedialaction %}
                            <td rowspan="{{ ot_count }}">{{ a.al_remedialaction | escape | newline_to_br }}</td>
                            {% if ot_mine_open %}
                              <td rowspan="{{ ot_count }}" class="ot-response-cell" data-ot-performed data-action-id="{{ a.al_remediationactionid }}" data-row-no="{{ row_no }}">
                                <fieldset class="ot-performed">
                                  <legend class="ot-visually-hidden">Action performed for issue {{ row_no }}</legend>
                                  <label class="opt"><input type="radio" name="ot-performed-{{ a.al_remediationactionid }}" value="120910815" data-ot-performed-choice{% if a.al_actionperformed.value == 120910815 %} checked{% endif %} /> Yes</label>
                                  <label class="opt"><input type="radio" name="ot-performed-{{ a.al_remediationactionid }}" value="120910816" data-ot-performed-choice{% if a.al_actionperformed.value == 120910816 %} checked{% endif %} /> No</label>
                                </fieldset>
                                <label class="ot-remedial-form__note" for="ot-note-{{ a.al_remediationactionid }}">Note (optional)</label>
                                <textarea id="ot-note-{{ a.al_remediationactionid }}" rows="2" class="ot-response-cell__text" data-ot-performed-note>{{ a.al_adviserresponse | escape }}</textarea>
                              </td>
                            {% else %}
                              <td rowspan="{{ ot_count }}">{{ a.al_actionperformed.label | default: '—' | escape }}{% if a.al_adviserresponse %}<br /><span class="ot-remedial-form__note">{{ a.al_adviserresponse | escape | newline_to_br }}</span>{% endif %}</td>
                            {% endif %}
                          {% elsif ot_mine_open %}
                            <td rowspan="{{ ot_count }}" class="ot-response-cell" data-ot-response data-action-id="{{ a.al_remediationactionid }}" data-row-no="{{ row_no }}">
                              <label class="ot-visually-hidden" for="ot-response-{{ a.al_remediationactionid }}">Remedial action for issue {{ row_no }}</label>
                              <textarea id="ot-response-{{ a.al_remediationactionid }}"
                                        rows="4"
                                        class="ot-response-cell__text"
                                        data-ot-response-text>{{ a.al_adviserresponse | escape }}</textarea>
                            </td>
                            <td rowspan="{{ ot_count }}">—</td>
                          {% else %}
                            <td rowspan="{{ ot_count }}">{{ a.al_adviserresponse | default: '—' | escape | newline_to_br }}</td>
                            <td rowspan="{{ ot_count }}">—</td>
                          {% endif %}
```

5. In the non-itemised branch (the `{% else %}` with `<td>—</td>` as the issue), replace its `{% if user and ... %} ... {% else %} <td>{{ a.al_adviserresponse ... }}</td> {% endif %}` block (keeping its F45 comment) with the same block as step 4 with every `rowspan="{{ ot_count }}"` attribute removed (for example `<td rowspan="{{ ot_count }}">` becomes `<td>`, and `<td rowspan="{{ ot_count }}" class="ot-response-cell" ...>` becomes `<td class="ot-response-cell" ...>`).

6. In the adviser completion script:
   - Change `var cells = document.querySelectorAll('[data-ot-response]');` to `var cells = document.querySelectorAll('[data-ot-response], [data-ot-performed]');`
   - Replace the body of `rows()` with:

```js
    function rows() {
      var found = [];
      for (var i = 0; i < cells.length; i += 1) {
        var performed = cells[i].hasAttribute('data-ot-performed');
        var text = cells[i].querySelector(performed ? '[data-ot-performed-note]' : '[data-ot-response-text]');
        var choice = performed ? cells[i].querySelector('[data-ot-performed-choice]:checked') : null;
        found.push({
          id: cells[i].getAttribute('data-action-id'),
          number: cells[i].getAttribute('data-row-no') || String(i + 1),
          performed: performed,
          choice: choice ? Number(choice.value) : null,
          text: text ? text.value : ''
        });
      }
      return found;
    }
```

   - In `markSaved()`, replace the first loop with:

```js
      for (var i = 0; i < cells.length; i += 1) {
        var text = cells[i].querySelector('[data-ot-response-text], [data-ot-performed-note]');
        if (text) { text.readOnly = true; }
        var choices = cells[i].querySelectorAll('[data-ot-performed-choice]');
        for (var c = 0; c < choices.length; c += 1) { choices[c].disabled = true; }
      }
```

   - Replace the completion pre-flight loop (the one showing `'Record the remedial action for issue '`) with:

```js
        for (var i = 0; i < pending.length; i += 1) {
          if (pending[i].performed) {
            if (pending[i].choice === null) {
              show('Answer "Action performed" for issue ' + pending[i].number + ' before signing off.', 'error');
              return;
            }
          } else if (!pending[i].text.replace(/^\s+|\s+$/g, '')) {
            show('Record the remedial action for issue ' + pending[i].number + ' before signing off.', 'error');
            return;
          }
        }
```

   - After `var payload = { al_adviserresponse: row.text };` add:

```js
          if (row.performed && row.choice !== null) { payload.al_actionperformed = row.choice; }
```

   Make these script edits with the Edit tool, not a shell heredoc: the regex `/^\s+|\s+$/g` loses its backslashes through a quoted heredoc.

- [ ] **Step 4: Change `OT Case Detail`**

1. In its actions fetch, after `<attribute name="al_adviserresponse" />` (~line 167), add `<attribute name="al_remedialaction" />` and `<attribute name="al_actionperformed" />`.
2. Add `<th scope="col">Action performed</th>` after `<th scope="col">Remedial action</th>`.
3. Change `colspan="8"` to `colspan="9"`.
4. Replace `<td rowspan="{{ ot_count }}">{{ a.al_adviserresponse | default: '—' | escape | newline_to_br }}</td>` (~line 785) with:

```liquid
                          {% if a.al_remedialaction %}
                            <td rowspan="{{ ot_count }}">{{ a.al_remedialaction | escape | newline_to_br }}</td>
                            <td rowspan="{{ ot_count }}">{{ a.al_actionperformed.label | default: '—' | escape }}{% if a.al_adviserresponse %}<br /><span class="ot-remedial-form__note">{{ a.al_adviserresponse | escape | newline_to_br }}</span>{% endif %}</td>
                          {% else %}
                            <td rowspan="{{ ot_count }}">{{ a.al_adviserresponse | default: '—' | escape | newline_to_br }}</td>
                            <td rowspan="{{ ot_count }}">—</td>
                          {% endif %}
```

5. Replace `<td>{{ a.al_adviserresponse | default: '—' | escape | newline_to_br }}</td>` (~line 820) with the same block without the `rowspan` attributes.

- [ ] **Step 5: Record the two Web API allowlists locally**

In `powerpages/outcome-testing---outcometesting/sitesetting.yml`:
- `Webapi/al_remediationaction/fields`: append `,al_actionperformed` to `adx_value`.
- `Webapi/contact/fields`: append `,al_remedialactionsrequest` to `adx_value`.

This file records intent only. Task 10 sets DEV from DEV's current value, not from this file.

- [ ] **Step 6: Run the tests and check the balance**

Run (from `app/`): `npx vitest run src/features/remediation`
Expected: PASS, including `portalSupervisorPanels.test.ts`.

Run the same Liquid and `<script>` balance counts as Task 7 Step 7 on both templates. Expected: equal pairs.

- [ ] **Step 7: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html powerpages/outcome-testing---outcometesting/sitesetting.yml app/src/features/remediation/portalActionPerformed.test.ts
git commit -m "feat(portal): Action performed on the remediation form; the adviser answers Yes or No with a note

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: The Code App's remediation page draws Action performed

**Files:**
- Modify: `app/src/features/remediation/remediationMapping.ts`
- Modify: `app/src/features/remediation/RemediationPage.tsx` (`ActionsTable`, ~line 131-181)
- Modify (fixtures): `app/src/features/remediation/remediationForm.test.ts`, `remediationIssues.test.ts`, `remediationRender.test.tsx`
- Test: `app/src/features/remediation/remediationMapping.test.ts`, `remediationRender.test.tsx`

**Interfaces:**
- Produces: `RemediationActionRow` gains `actionPerformed: string | null` and `adviserNote: string | null`; `remedialAction` becomes the checker's words, falling back to the adviser's on older rows; `export const ACTION_PERFORMED: Record<number, string>`

- [ ] **Step 1: Write the failing tests**

Append to `remediationMapping.test.ts` inside `describe('toAction', ...)`:

```ts
  it('reads the checker\'s remedial action, the adviser\'s answer and their note', () => {
    const row = toAction(
      action({
        al_remedialaction: 'Re-verify the client ID',
        al_actionperformed: 120910816,
        al_adviserresponse: 'Client unreachable',
      }),
    );
    expect(row.remedialAction).toBe('Re-verify the client ID');
    expect(row.actionPerformed).toBe('No');
    expect(row.adviserNote).toBe('Client unreachable');
  });

  it('keeps the adviser\'s words as the remedial action on a row raised before the change', () => {
    const row = toAction(action({ al_adviserresponse: 'Reissued the report', al_actionperformed: null }));
    expect(row.remedialAction).toBe('Reissued the report');
    expect(row.actionPerformed).toBeNull();
    expect(row.adviserNote).toBeNull();
  });
```

In `remediationRender.test.tsx`:
- In the `action(...)` fixture add `actionPerformed: null,` and `adviserNote: null,` after `remedialAction: null,`.
- In `'spans the action’s own columns across its items rather than repeating them'`, change `toHaveLength(6)` to `toHaveLength(7)` and `expect(body[0]).toHaveLength(8)` to `toHaveLength(9)`, and change the comment's "Six columns" to "Seven columns follow the issue: remedial action, action performed, owner, target date, status, age and sign-off."
- Append:

```tsx
  it('draws Action performed beside the checker’s remedial action, with the adviser’s note under it', () => {
    const answered = {
      ...action('a1', DESCRIPTION),
      remedialAction: 'Re-verify the client ID',
      actionPerformed: 'Yes',
      adviserNote: 'Done 26 Sep',
    };
    const markup = draw([answered]);
    const header = rows(markup)[0];
    const first = rows(markup)[1];

    expect(header.slice(2, 5)).toEqual(['Remedial action', 'Action performed', 'Owner']);
    expect(first[2]).toBe('Re-verify the client ID');
    expect(first[3]).toBe('Yes Done 26 Sep');
  });
```

In `remediationForm.test.ts` and `remediationIssues.test.ts`, add `actionPerformed: null,` and `adviserNote: null,` to each fixture object after `remedialAction: null,`.

- [ ] **Step 2: Run the tests to verify they fail**

Run (from `app/`): `npx vitest run src/features/remediation`
Expected: FAIL (`actionPerformed` undefined; header has no "Action performed").

- [ ] **Step 3: Implement the mapping**

In `remediationMapping.ts`:
- Add to `RemediationActionRow`, after `remedialAction: string | null;`:

```ts
  /** The adviser's Yes / No, on rows carrying the checker's remedial action (2026-09-29). */
  actionPerformed: string | null;
  /** The adviser's optional note beside it. Null on rows raised before that change. */
  adviserNote: string | null;
```

- Add after `CHANGES_ADVICE`:

```ts
/** al_actionperformed; the same two values the plug-ins and the portal use. */
export const ACTION_PERFORMED: Record<number, string> = { 120910815: 'Yes', 120910816: 'No' };
```

- In `toAction`, replace `remedialAction: text(extra.al_adviserresponse as string | undefined),` with:

```ts
    // The checker writes the remedial action from 2026-09-29 and the adviser's text becomes
    // their note. A row raised before that carries no checker's words, so its remedial action
    // is still the adviser's own, and it has no note.
    remedialAction:
      text(extra.al_remedialaction as string | undefined) ??
      text(extra.al_adviserresponse as string | undefined),
    actionPerformed: choice(record, 'al_actionperformed', ACTION_PERFORMED),
    adviserNote: text(extra.al_remedialaction as string | undefined)
      ? text(extra.al_adviserresponse as string | undefined)
      : null,
```

- [ ] **Step 4: Implement the column**

In `RemediationPage.tsx` `ActionsTable`:
- Add `<th scope="col">Action performed</th>` after `<th scope="col">Remedial action</th>`.
- Change `colSpan={8}` to `colSpan={9}`.
- After `<td rowSpan={lines.length}>{action.remedialAction ?? '—'}</td>` add:

```tsx
                    <td rowSpan={lines.length}>
                      {action.actionPerformed ?? '—'}
                      {action.adviserNote ? (
                        <span className="remediation__form-note"> {action.adviserNote}</span>
                      ) : null}
                    </td>
```

- [ ] **Step 5: Run the tests, the type check and lint**

Run (from `app/`): `npx vitest run src/features/remediation; npx tsc -b; npm run lint`
Expected: tests PASS, `tsc -b` prints nothing, and lint shows 0 errors (warnings that were already there are acceptable).

- [ ] **Step 6: Commit**

```bash
git add app/src/features/remediation/
git commit -m "feat(app): the remediation page draws Action performed and the adviser's note

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Deploy to DEV, prove it, and record it

**Files:**
- Create: `docs/deployment/2026-09-29-checker-remedial-actions.md`
- Modify: `knowledge/decision-log.md` (append AD-225)

**Interfaces:**
- Consumes: everything above.

Set up once per PowerShell session (one tool process at a time; sign-in can take minutes; pipe output through `Select-String -NotMatch '^Connecting'`):

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$T='plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll'
$U='https://org0b075da8.crm11.dynamics.com/'
dotnet build plugins/OutcomeTesting.Registration -c Debug
```

- [ ] **Step 1: Confirm the choice values are free in DEV**

Run: `dotnet $T webapi $U GET "EntityDefinitions(LogicalName='al_remediationaction')/Attributes/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=LogicalName&`$expand=OptionSet(`$select=Options)"`
Expected: no option anywhere with value 120910815 or 120910816. If either is taken, stop and pick the next free pair, then change it in Tasks 1, 7 and 8 and their tests.

- [ ] **Step 2: Create the four columns (in the OutcomeTesting solution)**

```powershell
dotnet $T addmemocolumn $U al_remediationaction al_RemedialAction "Remedial action" 4000 "The checker's remedial action for this row, written once when the check is submitted (2026-09-29)." --confirm $U
dotnet $T addchoicecolumn $U al_remediationaction al_ActionPerformed "Action performed" "120910815:Yes;120910816:No" "The adviser's answer: was the checker's remedial action performed? (2026-09-29)" --confirm $U
dotnet $T addmemocolumn $U al_reviewinstance al_PendingRemedialActions "Pending remedial actions" 100000 "The checker's remedial actions, parked until the submit raises the actions they belong to." --confirm $U
dotnet $T addmemocolumn $U contact al_RemedialActionsRequest "Remedial actions request" 100000 "Portal trigger: the checker's remedial actions for RemedialActionsRequestPlugin." --confirm $U
```

Expected: each prints `created ... in solution OutcomeTesting`. If the sandbox refuses a `--confirm` verb, stop and hand these four lines to the owner as written.

- [ ] **Step 3: Build and push the assembly; register the type and step**

```powershell
dotnet build plugins/OutcomeTesting.Plugins/OutcomeTesting.Plugins.csproj -c Release
dotnet $T pushassembly $U
dotnet $T registertype $U OutcomeTesting.Plugins.RemedialActionsRequestPlugin
dotnet $T registerstep $U OutcomeTesting.Plugins.RemedialActionsRequestPlugin Update contact 40 al_remedialactionsrequest sync
dotnet $T setstepfilter $U "RemediationResponseGuardPlugin: Update of al_remediationaction" al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice,al_actionperformed,al_remedialaction
```

Expected: the pushed byte count matches `plugins/OutcomeTesting.Plugins/bin/Release/net462/OutcomeTesting.Plugins.dll`. Also verify by sha256 of `pluginassembly.content` against the local DLL. The step is added to the solution, and the guard's filter now lists seven columns. If `pushassembly` fails with `PluginType [...] not found`, look for an orphaned type before suspecting the build.

- [ ] **Step 4: Widen the two Web API allowlists from DEV's current values**

Read both first: `dotnet $T fetch $U "<fetch><entity name='mspp_sitesetting'><attribute name='mspp_name'/><attribute name='mspp_value'/><filter type='or'><condition attribute='mspp_name' operator='eq' value='Webapi/al_remediationaction/fields'/><condition attribute='mspp_name' operator='eq' value='Webapi/contact/fields'/></filter></entity></fetch>"`

Then append to what DEV holds, which may differ from `sitesetting.yml`:

```powershell
dotnet $T setsitesetting $U Webapi/al_remediationaction/fields "<DEV value>,al_actionperformed" --confirm $U
dotnet $T setsitesetting $U Webapi/contact/fields "<DEV value>,al_remedialactionsrequest" --confirm $U
```

`setsitesetting --confirm` has been refused to the agent before. If it is refused, hand the two filled-in commands to the owner.

- [ ] **Step 5: Push the three templates**

Diff each template against DEV first (portal uploads overwrite newer DEV state). Then:

```powershell
dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-00000000001b "powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html"
dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000019 "powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html"
dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000015 "powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html"
```

Expected: each reports its old and new character counts.

- [ ] **Step 6: Regenerate the Code App data source and push**

From `app/`:

```powershell
npx pa app add data-source --connector dataverse --table al_remediationaction --non-interactive
npm run build
npx pa app push
```

Expected: the generated model and schema carry `al_remedialaction` and `al_actionperformed`, and the push prints an `/app/` URL with `sourcetime`. Open that URL, not the short `/a/{appId}` one.

- [ ] **Step 7: Prove it in DEV**

On a DEV case with an AQS review assigned to the signed-in portal identity (assign one via `al_AssignCase` if none is), in the portal:
1. Mark two test points Fail and tick one fail point. Confirm the card appears with three rows. Untick the fail point, then tick it again, and confirm its words come back.
2. Press Submit with one row empty. Confirm the page refuses and names the row.
3. Fill every row and submit. Then query:

```powershell
dotnet $T fetch $U "<fetch><entity name='al_remediationaction'><attribute name='al_remediationactioncode'/><attribute name='al_remedialaction'/><filter><condition attribute='al_outcomecaseid' operator='eq' value='<caseId>'/></filter></entity></fetch>"
```

Expected: every action carries its row's words, and the review's `al_pendingremedialactions` is null.

4. As the adviser, open OT Remediation. Answer one row Yes and one No with a note, then sign off. Expected: the case moves to Awaiting Sign-off, and `al_actionperformed` and `al_adviserresponse` hold what was entered.
5. `dotnet $T webapi $U PATCH "al_remediationactions(<id>)" '{"al_remedialaction":"x"}'`. Expected: refused with "The remedial action is the checker's...".
6. Repeat 1-3 on a Tax-then-AQS case with a Tax Fail. Expected: the Tax words appear on the Tax rows after the AQS submit.

- [ ] **Step 8: Record it**

Write `docs/deployment/2026-09-29-checker-remedial-actions.md` in the house shape: what was wrong, the rule, where it is enforced (a table by surface), what ran (a table of commands and results), what was proved live, and what is left for the owner (any refused `--confirm` commands, and promotion). Append to `knowledge/decision-log.md`:

```markdown
| AD-225 | **The checker writes the remedial action; the adviser answers whether it was performed.** A new card on `OT Review Detail`, "Fail points and remedial actions", lists what the submit will raise (`Remediation.NonPassItems`, mirrored client-side) with a required text box per item, or one **Overall** box when nothing is itemised. The words are parked on `al_reviewinstance.al_pendingremedialactions` through `contact.al_remedialactionsrequest` and `RemedialActionsRequestPlugin` (the review must be open and assigned to that contact). `SubmitReviewPlugin` refuses a submit that owes remediation while any item lacks words - at the Tax submit too when raising is deferred (AD-184) - and `Remediation.Raise` stamps each action's write-once `al_remedialaction`. The adviser answers `al_actionperformed` (Yes 120910815 / No 120910816) in a new column between Remedial action and Owner on all four renderings, with `al_adviserresponse` as an optional note; a No still completes and the supervisor decides. Rows raised before this keep the adviser-written remedial action and the old completion rule. | Project owner change request, 2026-09-29. Supersedes AD-095's "the adviser's free-text response is the Remedial action". A new column rather than reusing `al_adviserresponse`, because a portal write cannot say who made it (AD-053) and only a column with no legitimate later writer can be protected. Spec `docs/superpowers/specs/2026-09-29-checker-remedial-actions-design.md`. | 2026-09-29 |
```

- [ ] **Step 9: Commit**

```bash
git add docs/deployment/2026-09-29-checker-remedial-actions.md knowledge/decision-log.md
git commit -m "docs(deployment): checker remedial actions and Action performed into DEV (AD-225)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
