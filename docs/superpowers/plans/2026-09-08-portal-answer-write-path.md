# Portal Answer Write Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A signed-in Tax or AQS reviewer can save an answer and attach fail reasons on a review assigned to them, from the portal, in a browser.

**Architecture:** The browser stops creating `al_response`. It PATCHes one allowlisted memo column, `al_answerrequest`, on `al_reviewinstance` with a JSON payload naming the question version, the answer and its fail reasons. A synchronous `AnswerRequestPlugin` — registered on Update of `al_reviewinstance` filtered to that column, exactly as `SubmitRequestPlugin` is on `al_submitrequested` — parses the payload and creates or updates the `al_response` and its `al_failreason_response` associations under the plug-in user service. `ResponseGuardPlugin` still fires on that create/update and stays the sole authority for the PP-11 submission lock, the AD-023 answer shape and AD-020 section ownership. Nothing about the rules moves.

**Tech Stack:** C# on `net462` (Dataverse plug-in constraint), xunit 2.9.2 with the repo's `FakeOrganizationService`, `DataContractJsonSerializer` for payload parsing (in-box on net462 — do **not** add `System.Text.Json`), Power Pages Liquid web templates, `pac` 2.11.2.

**Spec:** `docs/deployment/2026-09-08-portal-signin-repair.md` sections 20–35, which contain the diagnosis this plan acts on. There is no separate design doc; that record is the spec.

## Global Constraints

- **Target framework is `net462`.** `plugins/OutcomeTesting.Plugins` and its test project both pin it. `System.Text.Json` is not available. Use `System.Runtime.Serialization.Json.DataContractJsonSerializer`.
- **Never add authorization checks against the caller in a portal-path plug-in.** Power Pages Web API writes arrive as the site's application user, so `InitiatingUserId` is never the checker. The gate is the Contact-scoped `Review Instance - assigned to me` table permission. This is AD-053/AD-047 and is stated in both `SubmitRequestPlugin` and `ResponseGuardPlugin`.
- **`ResponseGuardPlugin` remains the sole authority** for the submission lock, the AD-023 column match, the option subset and AD-020 section ownership. The new plug-in must not re-implement or duplicate any of them.
- **Plug-in failure messages must carry the existing prefixes** so the page can branch: `PRECONDITION: `, `CONFLICT: `. See `ResponseGuardPlugin`.
- **Never expose a table name, query, id or stack trace in a portal-visible message** (PP-16, NFR-SEC-01).
- **The plug-in assembly reaches an environment through `pac plugin push` only, never a solution import** (AD-061/AD-062). Use `plugins/deploy/Pack-Schema-Solution.ps1` if a solution is packed at all.
- **`src/` is the schema source of truth via the AD-013 round trip:** change the column in DEV, export, copy over `src/`. Do not hand-author `src/Entities/al_ReviewInstance/Entity.xml`.
- **Portal deploys go through `powerpages/Deploy-Portal.ps1` only.** Running `pac pages upload` directly on this site destroys table permissions; the reasons are at the top of that script.
- **Run both gates before every portal deploy:** `powerpages/Check-PortalSecurity.ps1` and `powerpages/Check-ComponentIds.ps1`.

---

## File Structure

| File | Responsibility |
|---|---|
| `plugins/OutcomeTesting.Plugins/AnswerRequest.cs` (new) | The payload contract and its parser. Pure, no `IOrganizationService`. |
| `plugins/OutcomeTesting.Plugins/AnswerWriter.cs` (new) | Given a parsed payload and a service, upsert the `al_response` and reconcile its fail reasons. All Dataverse calls for this path live here. |
| `plugins/OutcomeTesting.Plugins/AnswerRequestPlugin.cs` (new) | Thin plug-in shell: message filter, Target checks, call `AnswerWriter`. Mirrors `SubmitRequestPlugin`. |
| `plugins/OutcomeTesting.Plugins.Tests/AnswerRequestTests.cs` (new) | Parser tests. |
| `plugins/OutcomeTesting.Plugins.Tests/AnswerWriterTests.cs` (new) | Upsert and fail-reason reconciliation tests against `FakeOrganizationService`. |
| `powerpages/outcome-testing---outcometesting/sitesetting.yml` (modify) | Add `al_answerrequest` to `Webapi/al_reviewinstance/fields`. |
| `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html` (modify) | Replace the `al_responses` POST/PATCH and the `$ref` associate/disassociate with one PATCH of `al_answerrequest`. |
| `src/Entities/al_ReviewInstance/Entity.xml` (modify, by round trip) | Carries the new column after export. |
| `docs/deployment/2026-09-08-portal-signin-repair.md` (modify) | The record. |

`AnswerRequest` and `AnswerWriter` are separate because the parser is pure and cheap to test exhaustively, while the writer needs a fake service. Splitting them keeps each test file focused, and matches how `ResponseRules` sits apart from `ResponseGuardPlugin`.

---

### Task 1: The payload contract and parser

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/AnswerRequest.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/AnswerRequestTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public sealed class AnswerRequestPayload` with `string QuestionVersionId`, `string AnswerText`, `int? AnswerChoice`, `int[] AnswerChoices`, `string AnswerDate`, `string[] FailReasons`.
  - `public static class AnswerRequest` with `public static AnswerRequestPayload Parse(string json)` — throws `InvalidPluginExecutionException` prefixed `PRECONDITION: ` on unusable input, and `public static Guid RequireQuestionVersion(AnswerRequestPayload payload)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class AnswerRequestTests
    {
        [Fact]
        public void Parses_a_single_choice_answer()
        {
            var payload = AnswerRequest.Parse(
                "{\"questionVersionId\":\"3ce90bf2-64a1-f111-b8dd-e4fade069307\"," +
                "\"answerChoice\":120910305}");

            Assert.Equal(
                Guid.Parse("3ce90bf2-64a1-f111-b8dd-e4fade069307"),
                AnswerRequest.RequireQuestionVersion(payload));
            Assert.Equal(120910305, payload.AnswerChoice);
            Assert.Null(payload.AnswerText);
        }

        [Fact]
        public void Refuses_a_payload_with_no_question_version()
        {
            var payload = AnswerRequest.Parse("{\"answerChoice\":120910305}");
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AnswerRequest.RequireQuestionVersion(payload));
            Assert.StartsWith("PRECONDITION: ", error.Message);
        }

        [Fact]
        public void Refuses_text_that_is_not_json()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AnswerRequest.Parse("not json"));
            Assert.StartsWith("PRECONDITION: ", error.Message);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AnswerRequestTests`
Expected: FAIL — `AnswerRequest` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The answer payload the review page PATCHes onto al_reviewinstance.al_answerrequest.
    ///
    /// The browser cannot create an al_response directly: Power Pages refuses the
    /// @odata.bind association with 90040106, and no table permission value fixes it
    /// (2026-09-08 record, sections 20-35). So the page sends this instead and
    /// AnswerWriter does the write server-side.
    ///
    /// DataContractJsonSerializer rather than System.Text.Json: this assembly targets
    /// net462 and System.Text.Json is not available to it.
    /// </summary>
    [DataContract]
    public sealed class AnswerRequestPayload
    {
        [DataMember(Name = "questionVersionId")]
        public string QuestionVersionId { get; set; }

        [DataMember(Name = "answerText")]
        public string AnswerText { get; set; }

        [DataMember(Name = "answerChoice")]
        public int? AnswerChoice { get; set; }

        [DataMember(Name = "answerChoices")]
        public int[] AnswerChoices { get; set; }

        [DataMember(Name = "answerDate")]
        public string AnswerDate { get; set; }

        [DataMember(Name = "failReasons")]
        public string[] FailReasons { get; set; }
    }

    public static class AnswerRequest
    {
        private const string PreconditionPrefix = "PRECONDITION: ";

        public static AnswerRequestPayload Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "That answer arrived empty and was not saved.");
            }

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(AnswerRequestPayload));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var payload = (AnswerRequestPayload)serializer.ReadObject(stream);
                    if (payload == null)
                    {
                        throw new InvalidPluginExecutionException(
                            PreconditionPrefix + "That answer could not be read and was not saved.");
                    }

                    return payload;
                }
            }
            catch (InvalidPluginExecutionException)
            {
                throw;
            }
            catch (Exception)
            {
                // The exception text can carry the payload, and the payload is an answer.
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "That answer could not be read and was not saved.");
            }
        }

        public static Guid RequireQuestionVersion(AnswerRequestPayload payload)
        {
            Guid id;
            if (payload == null
                || string.IsNullOrWhiteSpace(payload.QuestionVersionId)
                || !Guid.TryParse(payload.QuestionVersionId, out id))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "An answer must say which question it answers.");
            }

            return id;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AnswerRequestTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/AnswerRequest.cs plugins/OutcomeTesting.Plugins.Tests/AnswerRequestTests.cs
git commit -m "feat(answering): the payload a review page sends instead of creating a response"
```

---

### Task 2: Upsert the response

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/AnswerWriter.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/AnswerWriterTests.cs`

**Interfaces:**
- Consumes: `AnswerRequest.Parse`, `AnswerRequest.RequireQuestionVersion`, `AnswerRequestPayload` from Task 1. `ResponseRules.BuildResponseCode(Guid reviewId, Guid questionVersionId)` returns `string`.
- Produces: `public static class AnswerWriter` with
  `public static Guid Save(IOrganizationService service, Guid reviewId, AnswerRequestPayload payload)` returning the response id, and
  `public static Guid? FindExisting(IOrganizationService service, Guid reviewId, Guid questionVersionId)`.

The alternate key `al_responsecode` is what makes a repeat save an update rather than a rival answer. `ResponseGuardPlugin` stamps it on Create; this finds it on the way in.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class AnswerWriterTests
    {
        private static readonly Guid ReviewId = Guid.Parse("b63c53c7-cca9-4111-8aac-e4fade069307");
        private static readonly Guid VersionId = Guid.Parse("3ce90bf2-64a1-4111-8bdd-e4fade069307");

        private static AnswerRequestPayload Choice(int value)
        {
            return new AnswerRequestPayload
            {
                QuestionVersionId = VersionId.ToString("D"),
                AnswerChoice = value,
            };
        }

        [Fact]
        public void Creates_a_response_when_none_exists()
        {
            var svc = new FakeOrganizationService();

            var id = AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceYes));

            var created = svc.Retrieve("al_response", id, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            Assert.Equal(ReviewId, created.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id);
            Assert.Equal(VersionId, created.GetAttributeValue<EntityReference>("al_questionversionid").Id);
            Assert.Equal(ResponseRules.ChoiceYes, created.GetAttributeValue<OptionSetValue>("al_answerchoice").Value);
        }

        [Fact]
        public void Updates_the_existing_response_rather_than_adding_a_second()
        {
            var svc = new FakeOrganizationService();
            var first = AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceYes));

            var second = AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceNo));

            Assert.Equal(first, second);
            var row = svc.Retrieve("al_response", first, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            Assert.Equal(ResponseRules.ChoiceNo, row.GetAttributeValue<OptionSetValue>("al_answerchoice").Value);
        }

        [Fact]
        public void Does_not_set_the_lookups_on_update()
        {
            // The lookups are immutable once set: a second save must not be able to move an
            // answer onto another review, which is the one thing the Parent-scoped table
            // permission cannot police once the row exists.
            var svc = new FakeOrganizationService();
            AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceYes));

            svc.ClearUpdateLog();
            AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceNo));

            var update = Assert.Single(svc.UpdateLog);
            Assert.False(update.Contains("al_reviewinstanceid"));
            Assert.False(update.Contains("al_questionversionid"));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AnswerWriterTests`
Expected: FAIL — `AnswerWriter` does not exist, and `FakeOrganizationService` has no `UpdateLog`/`ClearUpdateLog`.

- [ ] **Step 3: Add the update log to the fake, then write the implementation**

First read `plugins/OutcomeTesting.Plugins.Tests/FakeOrganizationService.cs` and add, following its existing style:

```csharp
        public System.Collections.Generic.List<Entity> UpdateLog { get; } = new System.Collections.Generic.List<Entity>();

        public void ClearUpdateLog()
        {
            UpdateLog.Clear();
        }
```

and append `UpdateLog.Add(entity);` at the top of its existing `Update(Entity entity)` method.

Then create `AnswerWriter.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Writes an answer on behalf of the review page.
    ///
    /// Every rule still belongs to ResponseGuardPlugin, which fires on the Create and
    /// Update this issues. Nothing here validates an answer; doing so would put the same
    /// rule in two places and let them drift.
    /// </summary>
    public static class AnswerWriter
    {
        private const string ResponseEntity = "al_response";

        public static Guid? FindExisting(IOrganizationService service, Guid reviewId, Guid questionVersionId)
        {
            var code = ResponseRules.BuildResponseCode(reviewId, questionVersionId);

            var query = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
            };
            query.Criteria.AddCondition("al_responsecode", ConditionOperator.Equal, code);

            var found = service.RetrieveMultiple(query);
            return found.Entities.Count == 0 ? (Guid?)null : found.Entities[0].Id;
        }

        public static Guid Save(IOrganizationService service, Guid reviewId, AnswerRequestPayload payload)
        {
            var questionVersionId = AnswerRequest.RequireQuestionVersion(payload);
            var existing = FindExisting(service, reviewId, questionVersionId);

            var row = new Entity(ResponseEntity);
            ApplyAnswer(row, payload);

            if (existing.HasValue)
            {
                // The lookups are deliberately absent: an answer never moves review or
                // question once it exists.
                row.Id = existing.Value;
                service.Update(row);
                return existing.Value;
            }

            row["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", reviewId);
            row["al_questionversionid"] = new EntityReference("al_questionversion", questionVersionId);
            return service.Create(row);
        }

        /// <summary>
        /// Every answer column is written on every save, including as null, so clearing an
        /// answer clears it. ResponseGuardPlugin reads the effective value and decides
        /// whether the shape is legal for the question's response type (AD-023).
        /// </summary>
        private static void ApplyAnswer(Entity row, AnswerRequestPayload payload)
        {
            row["al_answertext"] = string.IsNullOrWhiteSpace(payload.AnswerText) ? null : payload.AnswerText;

            row["al_answerchoice"] = payload.AnswerChoice.HasValue
                ? new OptionSetValue(payload.AnswerChoice.Value)
                : null;

            if (payload.AnswerChoices == null || payload.AnswerChoices.Length == 0)
            {
                row["al_answerchoices"] = null;
            }
            else
            {
                var choices = new OptionSetValueCollection();
                foreach (var choice in payload.AnswerChoices)
                {
                    choices.Add(new OptionSetValue(choice));
                }

                row["al_answerchoices"] = choices;
            }

            DateTime parsed;
            row["al_answerdate"] = !string.IsNullOrWhiteSpace(payload.AnswerDate)
                && DateTime.TryParse(
                    payload.AnswerDate,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal
                        | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out parsed)
                ? (object)parsed
                : null;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AnswerWriterTests`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/AnswerWriter.cs plugins/OutcomeTesting.Plugins.Tests/AnswerWriterTests.cs plugins/OutcomeTesting.Plugins.Tests/FakeOrganizationService.cs
git commit -m "feat(answering): upsert an answer server-side, keyed on the response code"
```

---

### Task 3: Reconcile fail reasons

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/AnswerWriter.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/AnswerWriterTests.cs`

**Interfaces:**
- Consumes: `AnswerWriter.Save` from Task 2.
- Produces: `public static void ReconcileFailReasons(IOrganizationService service, Guid responseId, string[] wanted)`, called from `Save` after the row exists.

The `$ref` associate the page used is the same refused mechanism as the create, so fail reasons travel in the same payload. `ResponseGuardPlugin` already guards Associate/Disassociate on `al_failreason_response` and keeps doing so.

- [ ] **Step 1: Write the failing test**

```csharp
        [Fact]
        public void Attaches_the_reasons_named_and_removes_the_ones_not()
        {
            var svc = new FakeOrganizationService();
            var keep = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var drop = Guid.Parse("22222222-2222-4222-8222-222222222222");

            var payload = Choice(ResponseRules.ChoiceNo);
            payload.FailReasons = new[] { keep.ToString("D"), drop.ToString("D") };
            var id = AnswerWriter.Save(svc, ReviewId, payload);

            var second = Choice(ResponseRules.ChoiceNo);
            second.FailReasons = new[] { keep.ToString("D") };
            AnswerWriter.Save(svc, ReviewId, second);

            Assert.Contains(svc.Associations, a => a.RelatedId == keep);
            Assert.Contains(svc.Disassociations, d => d.RelatedId == drop);
            Assert.DoesNotContain(svc.Disassociations, d => d.RelatedId == keep);
            Assert.Equal(id, svc.Associations[0].TargetId);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AnswerWriterTests`
Expected: FAIL — `FakeOrganizationService` records no associations.

- [ ] **Step 3: Record associations in the fake, then implement**

In `FakeOrganizationService.cs`, following its existing style, add:

```csharp
        public sealed class Link
        {
            public Guid TargetId { get; set; }
            public Guid RelatedId { get; set; }
            public string Relationship { get; set; }
        }

        public System.Collections.Generic.List<Link> Associations { get; } = new System.Collections.Generic.List<Link>();
        public System.Collections.Generic.List<Link> Disassociations { get; } = new System.Collections.Generic.List<Link>();
```

and implement `Associate`/`Disassociate` to append one `Link` per related reference, and make `RetrieveMultiple` on the `al_failreason_response` intersect return the currently associated ids (or, simpler and sufficient here, expose the live set through a `CurrentLinks` helper the writer does not use — the writer must read current links through `RetrieveMultiple`, so implement the intersect query in the fake).

Then in `AnswerWriter.cs`, add the constant and method and call it at the end of `Save`:

```csharp
        private const string FailReasonRelationship = "al_failreason_response";

        public static void ReconcileFailReasons(IOrganizationService service, Guid responseId, string[] wanted)
        {
            var desired = new System.Collections.Generic.HashSet<Guid>();
            if (wanted != null)
            {
                foreach (var raw in wanted)
                {
                    Guid parsed;
                    if (Guid.TryParse(raw, out parsed))
                    {
                        desired.Add(parsed);
                    }
                }
            }

            var current = CurrentReasons(service, responseId);

            var toAdd = new EntityReferenceCollection();
            foreach (var id in desired)
            {
                if (!current.Contains(id))
                {
                    toAdd.Add(new EntityReference("al_failreason", id));
                }
            }

            var toRemove = new EntityReferenceCollection();
            foreach (var id in current)
            {
                if (!desired.Contains(id))
                {
                    toRemove.Add(new EntityReference("al_failreason", id));
                }
            }

            var target = new EntityReference("al_response", responseId);
            var relationship = new Relationship(FailReasonRelationship);

            if (toAdd.Count > 0)
            {
                service.Associate("al_response", responseId, relationship, toAdd);
            }

            if (toRemove.Count > 0)
            {
                service.Disassociate("al_response", responseId, relationship, toRemove);
            }
        }

        private static System.Collections.Generic.HashSet<Guid> CurrentReasons(
            IOrganizationService service, Guid responseId)
        {
            var query = new QueryExpression("al_failreason") { ColumnSet = new ColumnSet(false) };
            var link = query.AddLink("al_al_failreason_al_response", "al_failreasonid", "al_failreasonid");
            link.LinkCriteria.AddCondition("al_responseid", ConditionOperator.Equal, responseId);

            var set = new System.Collections.Generic.HashSet<Guid>();
            foreach (var row in service.RetrieveMultiple(query).Entities)
            {
                set.Add(row.Id);
            }

            return set;
        }
```

and at the end of `Save`, before each `return`, call:

```csharp
            ReconcileFailReasons(service, responseId, payload.FailReasons);
```

Restructure `Save` so it computes `responseId` once, reconciles, then returns it.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: PASS, whole suite green.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/AnswerWriter.cs plugins/OutcomeTesting.Plugins.Tests/
git commit -m "feat(answering): fail reasons travel with the answer instead of a refused associate"
```

---

### Task 4: The plug-in shell

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/AnswerRequestPlugin.cs`
- Reference: `plugins/OutcomeTesting.Plugins/SubmitRequestPlugin.cs` — read it in full first; this mirrors it deliberately.

**Interfaces:**
- Consumes: `AnswerRequest.Parse`, `AnswerWriter.Save`.
- Produces: `public class AnswerRequestPlugin : PluginBase` — registered in Task 6.

- [ ] **Step 1: Write the implementation**

```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Portal answering without the browser creating anything. Registered as a synchronous
    /// post-operation step on Update of <c>al_reviewinstance</c>, filtered to
    /// <c>al_answerrequest</c>.
    ///
    /// The page used to POST al_response with two @odata.bind lookups. Power Pages refuses
    /// that association with 90040106 and no table permission value changes it — five were
    /// tried on 2026-09-08 and the message never moved. A PATCH of an allowlisted column on
    /// a review the caller is assigned to does get through, which is what this is built on,
    /// and it is the same mechanic SubmitRequestPlugin already uses for submission.
    ///
    /// Authorization is deliberately not checked against the caller, for the reason given in
    /// SubmitRequestPlugin: a Power Pages write arrives as the site's application user. The
    /// boundary is the Contact-scoped "Review Instance - assigned to me" permission, so
    /// reaching this plug-in already means the platform allowed a write on a review assigned
    /// to the signed-in contact (AD-047, AD-053).
    ///
    /// Every rule about the answer itself stays in ResponseGuardPlugin, which fires on the
    /// Create or Update this issues.
    /// </summary>
    public class AnswerRequestPlugin : PluginBase
    {
        private const string ReviewEntity = "al_reviewinstance";
        private const string AnswerRequestAttr = "al_answerrequest";

        public AnswerRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AnswerRequestPlugin))
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
            if (entity == null || !string.Equals(entity.LogicalName, ReviewEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!entity.Contains(AnswerRequestAttr))
            {
                return;
            }

            var json = entity.GetAttributeValue<string>(AnswerRequestAttr);
            if (string.IsNullOrWhiteSpace(json))
            {
                // Clearing the column is not an answer.
                return;
            }

            var payload = AnswerRequest.Parse(json);
            AnswerWriter.Save(service, entity.Id, payload);
        }
    }
}
```

- [ ] **Step 2: Build and run the whole suite**

Run: `dotnet build plugins/OutcomeTesting.Plugins` then `dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: build succeeds, suite green.

- [ ] **Step 3: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/AnswerRequestPlugin.cs
git commit -m "feat(answering): the trigger-column plug-in the review page writes to"
```

---

### Task 5: The `al_answerrequest` column

**Files:**
- Modify (by AD-013 round trip, not by hand): `src/Entities/al_ReviewInstance/Entity.xml`

- [ ] **Step 1: Create the column in DEV**

In make.powerapps.com, on the **Review Instance** (`al_reviewinstance`) table, add a column:

- Display name: `Answer Request`
- Logical/schema name: `al_answerrequest`
- Type: **Multiline Text**, format Text, **max length 8000**
- Required: Optional

8000 because a multi-select answer with several fail-reason GUIDs plus free text has to fit; 4000 is close enough to the edge to be worth avoiding, and the column is transient.

- [ ] **Step 2: Verify it exists and is writable**

```powershell
$env:DOTNET_ROLL_FORWARD='LatestMajor'
# expect one row, al_answerrequest listed
& "C:\Program Files\dotnet\dotnet.exe" run --project plugins\OutcomeTesting.Registration -- fetch https://org0b075da8.crm11.dynamics.com "<fetch top='1'><entity name='al_reviewinstance'><attribute name='al_answerrequest'/></entity></fetch>"
```

Expected: succeeds. A missing column fails with "doesn't contain attribute with Name = 'al_answerrequest'".

- [ ] **Step 3: Round-trip the schema into `src/`**

Follow the existing AD-013 procedure used for every other schema change in this repo: export the unmanaged solution from DEV and copy the exported folder over `src/`. Do not hand-edit `Entity.xml`.

- [ ] **Step 4: Confirm the round trip captured it**

Run: `grep -c "al_answerrequest" src/Entities/al_ReviewInstance/Entity.xml`
Expected: non-zero.

- [ ] **Step 5: Commit**

```bash
git add src/Entities/al_ReviewInstance/Entity.xml
git commit -m "feat(schema): al_answerrequest, the column the review page writes an answer to"
```

---

### Task 6: Register the step

**Files:**
- None in the repo unless Task 6 Step 3 applies.

**Interfaces:**
- Consumes: `AnswerRequestPlugin` from Task 4.

- [ ] **Step 1: Push the assembly**

Run: `pac plugin push` per AD-061 — the assembly never reaches an environment through a solution import.

- [ ] **Step 2: Register the step**

Register `OutcomeTesting.Plugins.AnswerRequestPlugin` on:

- Message: `Update`
- Primary entity: `al_reviewinstance`
- Stage: **Post-operation**, Mode: **Synchronous**
- Filtering attributes: `al_answerrequest` **only**

Synchronous because the page must see the refusal on its own request, exactly as submission does. Post-operation because the row must exist and carry its id.

- [ ] **Step 3: Verify by query**

```powershell
$env:DOTNET_ROLL_FORWARD='LatestMajor'
& "C:\Program Files\dotnet\dotnet.exe" run --project plugins\OutcomeTesting.Registration -- fetch https://org0b075da8.crm11.dynamics.com "<fetch><entity name='sdkmessageprocessingstep'><attribute name='name'/><attribute name='filteringattributes'/><attribute name='stage'/><attribute name='mode'/><link-entity name='plugintype' from='plugintypeid' to='plugintypeid' alias='pt'><attribute name='typename'/><filter><condition attribute='typename' operator='eq' value='OutcomeTesting.Plugins.AnswerRequestPlugin'/></filter></link-entity></entity></fetch>"
```

Expected: exactly one row, `filteringattributes` = `al_answerrequest`, stage 40, mode 0.

If registration was done by hand rather than by script, note that in the deployment record — `SubmitRequestPlugin` has no registration script either, and that gap is why this step cannot be reproduced from the repo.

---

### Task 7: Allowlist the column

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/sitesetting.yml`

- [ ] **Step 1: Add the column to the Web API allowlist**

Change the value of `Webapi/al_reviewinstance/fields` from `al_submitrequested` to `al_submitrequested,al_answerrequest`, and update its `adx_description` to say the second column carries an answer payload that `AnswerRequestPlugin` consumes, and that status, submitted-on and the assigned contact stay off the list.

- [ ] **Step 2: Run both gates**

```powershell
& ".\powerpages\Check-PortalSecurity.ps1"
& ".\powerpages\Check-ComponentIds.ps1"
```

Expected: both pass.

- [ ] **Step 3: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/sitesetting.yml
git commit -m "feat(portal): allowlist al_answerrequest for the review page"
```

---

### Task 8: Rewrite the autosave

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`

Read the whole `{% if editable %}` autosave script block first. The `api()` helper and its `shell.getTokenDeferred()` token handling are correct and stay; only what it sends changes.

- [ ] **Step 1: Replace `save()`**

The two `al_responses` calls go. One PATCH replaces them:

```js
              function save() {
                var body = { al_answerrequest: JSON.stringify(currentRequest()) };
                show('Saving...', 'saving');

                function done() {
                  var now = new Date();
                  show('Saved ' + two(now.getHours()) + ':' + two(now.getMinutes()), 'saved');
                }

                api('PATCH', 'al_reviewinstances(' + REVIEW_ID + ')', body,
                  done,
                  function (raw, httpStatus) { show(friendlyError(raw, httpStatus), 'error'); });
              }
```

- [ ] **Step 2: Build the payload from the existing controls**

Add `currentRequest()` next to the existing `currentPayload()`, reusing it so the answer columns are built in exactly one place:

```js
              /*
               * The answer and its reasons in one object. It travels as a JSON string on
               * al_answerrequest rather than as an al_response create, because Power Pages
               * refuses the create's @odata.bind association (90040106) whatever the table
               * permissions say. AnswerRequestPlugin does the write.
               */
              function currentRequest() {
                var answer = currentPayload();
                var request = {
                  questionVersionId: questionVersion,
                  answerText: answer.al_answertext === undefined ? null : answer.al_answertext,
                  answerChoice: answer.al_answerchoice === undefined ? null : answer.al_answerchoice,
                  answerChoices: answer.al_answerchoices === undefined ? null : answer.al_answerchoices,
                  answerDate: answer.al_answerdate === undefined ? null : answer.al_answerdate,
                  failReasons: selectedReasonIds()
                };
                return request;
              }

              function selectedReasonIds() {
                var boxes = root.querySelectorAll('[data-ot-reason]');
                var ids = [];
                for (var i = 0; i < boxes.length; i++) {
                  if (boxes[i].checked) { ids.push(boxes[i].value); }
                }
                return ids;
              }
```

- [ ] **Step 3: Make a reason tick save like any other change**

Replace the whole `toggleReason` function — including its `$ref` POST and DELETE and its "Choose an answer first" guard, which existed only because an association needed a row to hang off — with:

```js
              /*
               * A reason is now part of the answer payload, so ticking one is just another
               * change to save. The old "choose an answer first" rule went with the
               * association it protected.
               */
              function toggleReason() {
                queue();
              }
```

and change the listener that calls it to `boxes[r].addEventListener('change', toggleReason);` — the closure over `box` is no longer needed.

- [ ] **Step 4: Delete what is now unreachable**

Remove `idFromHeader`, and the `data-response-id` reads and writes in `save()`. Leave the `data-response-id` attribute on the markup: the Liquid still renders it and the fail-reason pre-tick logic above still uses `existing.al_responseid`.

- [ ] **Step 5: Run both gates and deploy**

```powershell
& ".\powerpages\Check-PortalSecurity.ps1"
& ".\powerpages\Check-ComponentIds.ps1"
& ".\powerpages\Deploy-Portal.ps1" -OrgUrl https://org0b075da8.crm11.dynamics.com
```

Expected: both gates pass; deploy reports 13 of 13 table permissions and "Portal deployed and verified".

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html
git commit -m "fix(portal): the review page asks for an answer instead of creating one"
```

---

### Task 9: Prove it in a browser

This is the only step that turns the plan into a fixed fault. Every "proof" this project has recorded to date drove the plug-ins through the SDK as an administrator, which bypasses table permissions entirely.

- [ ] **Step 1: Restart the site**

Power Platform admin centre, the site's **Site Actions → Restart site**. Template and permission caches are why.

- [ ] **Step 2: Save an answer as a reviewer**

Sign in as a contact holding `AL Portal - Tax Reviewer` or `AL Portal - AQS Reviewer`, open a review assigned to them, change one answer, and watch for "Saved".

- [ ] **Step 3: Confirm the row exists, by query, not by the page**

```powershell
$env:DOTNET_ROLL_FORWARD='LatestMajor'
& "C:\Program Files\dotnet\dotnet.exe" run --project plugins\OutcomeTesting.Registration -- fetch https://org0b075da8.crm11.dynamics.com "<fetch><entity name='al_response'><attribute name='al_responseid'/><attribute name='createdon'/><attribute name='al_answerchoice'/><order attribute='createdon' descending='true'/></entity></fetch>"
```

Expected: a row whose `createdon` is today. Before this change there were 108 rows, every one created by `svc automate aq` on 2026-09-06 and none from the portal.

- [ ] **Step 4: Tick a fail reason on a non-pass answer and re-run the query**

Expected: the `al_failreason_response` link exists. Check with:

`<fetch><entity name='al_failreason'><attribute name='al_name'/><link-entity name='al_al_failreason_al_response' from='al_failreasonid' to='al_failreasonid' intersect='true'><filter><condition attribute='al_responseid' operator='eq' value='THE-RESPONSE-ID'/></filter></link-entity></entity></fetch>`

- [ ] **Step 5: Submit the review and confirm it transitions**

Expected: the submit button reports success, and `al_reviewstatus` becomes Submitted (120910212). A `PRECONDITION:` refusal naming unanswered mandatory questions is also a pass for this step — it proves the path reaches `SubmitReviewPlugin`.

---

### Task 10: Record it

**Files:**
- Modify: `docs/deployment/2026-09-08-portal-signin-repair.md`

- [ ] **Step 1: Write the closing sections**

Cover, in the record's existing style: that five permission-value fixes failed and why the sixth was a different kind of change; that the `PATCH al_reviewinstance` versus `POST al_response` comparison is what proved the association was the fault rather than the caller's permissions; what the trigger column widens and what still bounds it; and — plainly — whether an answer actually saved.

- [ ] **Step 2: Note the two paths this plan does not fix**

`POST al_caseassignments` (claim) and `POST al_signoffs` (T&C sign-off) both create a row with an `@odata.bind` and will fail the same way for the same reason. They are out of scope here and need the same treatment. Say so, so the next reader does not rediscover it.

- [ ] **Step 3: Commit**

```bash
git add docs/deployment/2026-09-08-portal-signin-repair.md
git commit -m "docs: the answer write path, and what proved the association was the fault"
```

---

## Self-Review

**Spec coverage.** The diagnosis in sections 20–35 requires removing the browser-side create (Tasks 1–4, 8), keeping `ResponseGuardPlugin` authoritative (Tasks 2, 4 — neither re-implements a rule), covering the fail-reason association which uses the same refused mechanism (Task 3, Task 8 Step 3), and proving it in a browser (Task 9). The two other bind-carrying writes are explicitly scoped out and recorded (Task 10 Step 2).

**Type consistency.** `AnswerRequestPayload` property names are used identically in Tasks 1, 2, 3 and 8. `AnswerWriter.Save(IOrganizationService, Guid, AnswerRequestPayload) → Guid` is defined in Task 2 and called with that signature in Tasks 3 and 4. `AnswerRequest.Parse(string) → AnswerRequestPayload` and `RequireQuestionVersion(AnswerRequestPayload) → Guid` are defined in Task 1 and used in Tasks 2 and 4. The JSON member names in Task 1 match the JavaScript keys in Task 8 Step 2 exactly: `questionVersionId`, `answerText`, `answerChoice`, `answerChoices`, `answerDate`, `failReasons`.

**Known risk, stated rather than hidden.** `al_answerrequest` retains the last answer payload on the review row. `SubmitRequestPlugin` sets the precedent by leaving `al_submitrequested` true, and the payload is visible only to people who can already read the review — but it is residue, and clearing it from the plug-in would re-enter the same step. If it matters, clear it from an asynchronous step, and do that as its own change with its own test.
