# People Are Identified by Email - Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every place the system identifies an adviser or paraplanner does so by the email on
the case, never by the name. Names become labels.

**Architecture:**
- **One resolver.** The plug-ins share `NotificationOutbox.MatchPerson`, which loses its name
  branch. Every caller passes the case's stored email.
- **Writes carry both.** The edit paths (Code App and portal) and the import write a name and
  an email together. The server refuses a name without an email, and refuses an import row
  without one.
- **Open actions follow the email.** They move when the case's adviser email changes, and
  when a contact with that email appears (a new step on `contact`).
- **Existing data.** A registration-tool command fills blank emails once and re-points open
  actions.

**Tech Stack:**
- Plug-ins: C# (net462), Dataverse SDK, xUnit with `FakeOrganizationService`.
- Code App: React + TypeScript, vitest.
- Portal: Power Pages Liquid templates, pinned by string-assertion tests in `app/`.
- Registration tool: C# net8.

**Spec:** `docs/superpowers/specs/2026-10-02-email-person-identity-design.md`

## Global Constraints

- Plug-in tests: `cd plugins; $env:DOTNET_ROLL_FORWARD='Major'; dotnet test OutcomeTesting.Plugins.Tests`.
  From Bash, prefix `DOTNET_ROLL_FORWARD=Major`.
- App type check is `npx tsc -b` from `app/`. `tsc --noEmit` checks nothing there.
- App tests: `npx vitest run` from `app/`.
- No AD-, OD-, BR-, FR- or PP- codes in user-facing text. Describe the rule in plain words.
  Code comments may cite codes.
- Never write the product name "Outcome Testing" in code, templates or plug-in strings.
- Every environment write targets DEV (`https://org0b075da8.crm11.dynamics.com`) only. TEST
  and PROD steps are handed to the owner as PowerShell commands (`$env:VAR='x'; ...` form).
- Email comparison:
  - Trim and ignore case.
  - Dataverse string equality already ignores case. `FakeOrganizationService` compares
    exactly, so tests seed matching case.
- "Active contact" means `statecode = 0`.
- A completed remediation action (`al_actionstatus` 120910602, `Remediation.StatusCompleted`)
  never moves.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
  Stage only the files each task names. The branch carries unrelated uncommitted work.

## Review Focus

1. **A case whose adviser email changes to an address no contact holds.** The open actions
   must not stay with the previous adviser; they become unassigned. Pinned in Task 1
   (`AssignOpenActions_unassigns_when_the_email_matches_nobody`).
2. **An edit that changes only the adviser NAME, on a legacy case with no email.** The save
   must succeed when the name is sent with an email, and must be refused (not crash) when it
   is sent alone. Pinned in Task 2.
3. **An email typed with different capitals or spaces from the contact's.** It must still
   match. Pinned in Task 1 (trim) and in the Task 7 app helpers (lower-case).
4. **Two active contacts carrying the same email.** Nothing is assigned and the reason names
   the email. Pinned in Task 1 (`Two_contacts_with_one_email_resolve_to_nobody`).
5. **A contact deactivated or reactivated.** The contact step must re-point that adviser's
   open actions both ways, and never touch completed actions. Pinned in Task 4.

---

### Task 1: The plug-ins resolve people by email only

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/CasePeople.cs`
- Modify:
  - `plugins/OutcomeTesting.Plugins/NotificationOutbox.cs` (`ParaplannerEmail` ~617-644, `PersonMatchKind` ~647-662, `MatchParaplanner` ~728-737, `MatchAdviser` ~743-747, `MatchPerson` ~769-841)
  - `plugins/OutcomeTesting.Plugins/CaseAdviser.cs` (whole file)
  - `plugins/OutcomeTesting.Plugins/Remediation.cs` (`AdviserContact` 942-977, `AssignOpenActions` 979-1041, doc at 786-791)
  - `plugins/OutcomeTesting.Plugins/NotificationEmitterPlugin.cs` (`QueueCasePassed` ~258-273)
  - `plugins/OutcomeTesting.Plugins/NotificationRecipients.cs` (`AdviserEmail` ~181-202)
  - `plugins/OutcomeTesting.Plugins/TcManagerRouting.cs` (`ForCase` ~113-120, `ForAdviserEmail` query ~136-145, doc line 33)
  - `plugins/OutcomeTesting.Plugins/CaseAccessReconciler.cs` (lines 85-91)
  - `plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs` (`ParaplannerMatch` 319-327, `AdviserCode` 367-377, doc 190-194)
  - `plugins/OutcomeTesting.Plugins/ImportCasesPlugin.cs` (178-184, 213-240)
  - `plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs` (end of `ApplyFields`, ~1003-1005)
  - `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs` (comment ~260-261 only)
- Test:
  - Create `plugins/OutcomeTesting.Plugins.Tests/EmailIdentityTests.cs`.
  - Update fixtures in the test files listed in Step 6.

**Interfaces:**
- Produces:
  - `static class CasePeople`. Constants `AdviserNameAttr = "al_advisername"`,
    `AdviserEmailAttr = "al_adviseremail"`, `ParaplannerNameAttr = "al_paraplanner"`,
    `ParaplannerEmailAttr = "al_paraplanneremail"`. Methods
    `static string Clean(string value)` (trimmed, or null when blank) and
    `static bool IsEmail(string value)`.
  - `NotificationOutbox.PersonMatchKind { NoEmail, NoContact, Ambiguous, Matched }`.
    `NoEmail` now means "the case carries no email for this person". The old `NoName` and
    the old "contact has no email" meaning are gone.
  - `static PersonMatch NotificationOutbox.MatchPerson(IOrganizationService service, string email, string role)`
  - `static PersonMatch NotificationOutbox.MatchAdviser(IOrganizationService service, string email)`
  - `static PersonMatch NotificationOutbox.MatchParaplanner(IOrganizationService service, string email)`
  - `static string CaseAdviser.EmailFor(string storedEmail)`
  - `static EntityReference Remediation.AdviserContact(IOrganizationService service, EntityReference caseRef)`.
    Same signature; now reads `al_adviseremail`.
  - `static int Remediation.AssignOpenActions(IOrganizationService service, EntityReference caseRef, Guid correlationId)`.
    Same signature; also unassigns when the email matches nobody.
- Removed: `CaseAdviser.FillMissingEmail` and `CaseAdviser.FollowName`.

- [ ] **Step 1: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/EmailIdentityTests.cs`:

```csharp
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// People on a case are identified by email, never by name (owner, 2026-10-02: "two
    /// people can have the same name"). PROD case 256497798 named "Adam Strumidlo"; his contact
    /// was onboarded as "Adam Strumdlio", so a name match found nobody and his remediation was
    /// raised unassigned. His email was right everywhere.
    /// </summary>
    public class EmailIdentityTests
    {
        private static readonly Guid CaseId = Guid.Parse("e1e1e1e1-0000-4000-8000-000000000001");
        private static readonly Guid AdamId = Guid.Parse("e1e1e1e1-0000-4000-8000-000000000002");
        private static readonly Guid OtherAdamId = Guid.Parse("e1e1e1e1-0000-4000-8000-000000000003");
        private static readonly Guid Correlation = Guid.NewGuid();

        private static FakeOrganizationService Case(string name, string email, string paraName = null, string paraEmail = null)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-EMAIL-1",
                "al_advisername", name,
                "al_adviseremail", email,
                "al_paraplanner", paraName,
                "al_paraplanneremail", paraEmail,
                "al_clientname", "Mr Client");
            return svc;
        }

        private static void Contact(FakeOrganizationService svc, Guid id, string fullname, string email, int state = 0)
        {
            svc.Seed("contact", id,
                "fullname", fullname,
                "emailaddress1", email,
                "statecode", new OptionSetValue(state));
        }

        private static EntityReference Ref()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        private static Guid OpenAction(FakeOrganizationService svc, Guid? holder)
        {
            var row = svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "al_name", "Remediation IO-EMAIL-1");
            if (holder.HasValue)
            {
                row["al_assignedcontactid"] = new EntityReference("contact", holder.Value);
            }

            return row.Id;
        }

        [Fact]
        public void A_misspelled_contact_name_does_not_matter_when_the_email_matches()
        {
            var svc = Case("Adam Strumidlo", "adam.strumidlo@example.com");
            Contact(svc, AdamId, "Adam Strumdlio", "adam.strumidlo@example.com");

            Assert.Equal(AdamId, Remediation.AdviserContact(svc, Ref()).Id);
        }

        [Fact]
        public void Two_people_with_one_name_are_told_apart_by_email()
        {
            var svc = Case("Adam Smith", "adam.smith2@example.com");
            Contact(svc, AdamId, "Adam Smith", "adam.smith@example.com");
            Contact(svc, OtherAdamId, "Adam Smith", "adam.smith2@example.com");

            Assert.Equal(OtherAdamId, Remediation.AdviserContact(svc, Ref()).Id);
        }

        [Fact]
        public void A_case_with_no_adviser_email_resolves_to_nobody_even_when_the_name_is_unique()
        {
            var svc = Case("Sam Adviser", null);
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            Assert.Null(Remediation.AdviserContact(svc, Ref()));
            Assert.Equal(NotificationOutbox.PersonMatchKind.NoEmail,
                NotificationOutbox.MatchAdviser(svc, null).Kind);
        }

        [Fact]
        public void Spaces_around_the_stored_email_are_ignored()
        {
            var svc = Case("Sam Adviser", "  sam@example.com ");
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            Assert.Equal(AdamId, Remediation.AdviserContact(svc, Ref()).Id);
        }

        [Fact]
        public void Two_contacts_with_one_email_resolve_to_nobody()
        {
            var svc = Case("Sam Adviser", "sam@example.com");
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");
            Contact(svc, OtherAdamId, "Sam A", "sam@example.com");

            var match = NotificationOutbox.MatchAdviser(svc, "sam@example.com");
            Assert.Equal(NotificationOutbox.PersonMatchKind.Ambiguous, match.Kind);
            Assert.Contains("sam@example.com", match.Reason);
            Assert.Null(Remediation.AdviserContact(svc, Ref()));
        }

        [Fact]
        public void An_inactive_contact_is_nobody()
        {
            var svc = Case("Sam Adviser", "sam@example.com");
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com", state: 1);

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoContact,
                NotificationOutbox.MatchAdviser(svc, "sam@example.com").Kind);
        }

        [Fact]
        public void The_pass_letter_goes_to_the_stored_email_even_with_no_contact()
        {
            var svc = Case("Sam Adviser", "sam@example.com");

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            var queued = svc.Creates.Single(c => c.Contains("al_event"));
            Assert.Equal("sam@example.com", queued.GetAttributeValue<string>("al_recipientemail"));
        }

        [Fact]
        public void The_pass_letter_is_not_addressed_by_name()
        {
            var svc = Case("Sam Adviser", null);
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            Assert.False(svc.Creates.Single(c => c.Contains("al_event")).Contains("al_recipientemail"));
        }

        [Fact]
        public void TC_routing_does_not_fall_back_to_the_name()
        {
            var svc = Case("Sam Adviser", null);
            Contact(svc, AdamId, "Sam Adviser", "sam@example.com");

            Assert.Equal(TcManagerRouting.RoutingKind.NoAdviserEmail,
                TcManagerRouting.ForCase(svc, Ref()).Kind);
        }

        [Fact]
        public void TC_routing_ignores_an_inactive_mapping()
        {
            var svc = Case("Sam Adviser", "sam@example.com");
            Contact(svc, AdamId, "Pat Manager", "pat@example.com");
            svc.Seed("al_advisermapping", Guid.NewGuid(),
                "al_adviseremail", "sam@example.com",
                "al_tcmanagerid", new EntityReference("contact", AdamId),
                "statecode", new OptionSetValue(1));

            Assert.Equal(TcManagerRouting.RoutingKind.NoMapping,
                TcManagerRouting.ForCase(svc, Ref()).Kind);
        }

        [Fact]
        public void The_paraplanner_letter_uses_the_stored_email_and_never_the_name()
        {
            var withEmail = Case("Sam Adviser", "sam@example.com", "Pip Planner", "pip@example.com");
            Assert.Equal("pip@example.com", NotificationOutbox.ParaplannerEmail(withEmail, Ref()));

            var withoutEmail = Case("Sam Adviser", "sam@example.com", "Pip Planner", null);
            Contact(withoutEmail, AdamId, "Pip Planner", "pip@example.com");
            Assert.Null(NotificationOutbox.ParaplannerEmail(withoutEmail, Ref()));
        }

        [Fact]
        public void AssignOpenActions_moves_open_actions_to_the_contact_with_the_case_email()
        {
            var svc = Case("Adam Strumidlo", "adam.strumidlo@example.com");
            Contact(svc, AdamId, "Adam Strumdlio", "adam.strumidlo@example.com");
            var action = OpenAction(svc, null);

            Assert.Equal(1, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Equal(AdamId, svc.Row("al_remediationaction", action)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void AssignOpenActions_unassigns_when_the_email_matches_nobody()
        {
            // The adviser changed to someone not yet onboarded. Leaving the action with the
            // previous adviser would let the wrong person answer it.
            var svc = Case("New Adviser", "new@example.com");
            var action = OpenAction(svc, OtherAdamId);

            Assert.Equal(1, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Null(svc.Row("al_remediationaction", action)
                .GetAttributeValue<EntityReference>("al_assignedcontactid"));
            Assert.DoesNotContain(svc.Creates, c => c.Contains("al_event"));
        }

        [Fact]
        public void AssignOpenActions_never_moves_a_completed_action()
        {
            var svc = Case("New Adviser", "new@example.com");
            var done = svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_assignedcontactid", new EntityReference("contact", OtherAdamId)).Id;

            Assert.Equal(0, Remediation.AssignOpenActions(svc, Ref(), Correlation));
            Assert.Equal(OtherAdamId, svc.Row("al_remediationaction", done)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Theory]
        [InlineData("sam@example.com", true)]
        [InlineData("  sam.o'neil@sub.example.co.uk ", true)]
        [InlineData("Sam Adviser", false)]
        [InlineData("sam@", false)]
        [InlineData("sam@example", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsEmail_accepts_one_address_only(string value, bool expected)
        {
            Assert.Equal(expected, CasePeople.IsEmail(value));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~EmailIdentityTests"`

Expected: a build failure. `CasePeople` does not exist, and `MatchAdviser` takes 3 arguments.

- [ ] **Step 3: Create `CasePeople.cs`**

```csharp
using System.Text.RegularExpressions;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The people on a case are identified by EMAIL, never by name (AD-228, owner 2026-10-02:
    /// "two people can have the same name"). al_advisername and al_paraplanner are labels,
    /// written beside the email and never used to find anyone.
    /// </summary>
    public static class CasePeople
    {
        public const string AdviserNameAttr = "al_advisername";
        public const string AdviserEmailAttr = "al_adviseremail";
        public const string ParaplannerNameAttr = "al_paraplanner";
        public const string ParaplannerEmailAttr = "al_paraplanneremail";

        // One address: something@something.something, no spaces, one @. Deliberately loose -
        // the import and the edits refuse a NAME typed into an email column, not every
        // address RFC 5322 would refuse.
        private static readonly Regex EmailShape =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

        /// <summary>The value trimmed, or null when it is blank.</summary>
        public static string Clean(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        /// <summary>True for one email address, spaces around it ignored.</summary>
        public static bool IsEmail(string value)
        {
            var trimmed = Clean(value);
            return trimmed != null && EmailShape.IsMatch(trimmed);
        }
    }
}
```

- [ ] **Step 4: Make `MatchPerson` email-only (`NotificationOutbox.cs`)**

Replace the `PersonMatchKind` enum body with:

```csharp
        /// <summary>Why a person field did or did not reach somebody.</summary>
        public enum PersonMatchKind
        {
            /// <summary>The case carries no email for this person.</summary>
            NoEmail,

            /// <summary>No active contact holds that email.</summary>
            NoContact,

            /// <summary>Two or more active contacts hold it, so no one of them can be chosen.</summary>
            Ambiguous,

            /// <summary>Exactly one active contact holds it.</summary>
            Matched,
        }
```

Replace `MatchParaplanner`, `MatchAdviser` and `MatchPerson`, including their doc comments,
with:

```csharp
        /// <summary>The para-planner whose email the case stores (AD-228).</summary>
        public static PersonMatch MatchParaplanner(IOrganizationService service, string email)
        {
            return MatchPerson(service, email, "para-planner");
        }

        /// <summary>The adviser whose email the case stores (AD-228).</summary>
        public static PersonMatch MatchAdviser(IOrganizationService service, string email)
        {
            return MatchPerson(service, email, "adviser");
        }

        /// <summary>
        /// Resolves a person by EMAIL to exactly one active contact (AD-228).
        ///
        /// <para>
        /// No name branch. Until 2026-10-02 a blank email fell back to matching
        /// contact.fullname, and a misspelled contact name (PROD, "Adam Strumdlio") or two
        /// advisers of one name left remediation with nobody. A display name describes a
        /// person; only the address identifies them.
        /// </para>
        /// <para>
        /// Two rows are fetched, never one: an email is unique by convention, not by
        /// constraint, and the second row is what proves the match was unambiguous.
        /// </para>
        /// </summary>
        public static PersonMatch MatchPerson(IOrganizationService service, string email, string role)
        {
            var label = string.IsNullOrWhiteSpace(role) ? "person" : role;
            var value = CasePeople.Clean(email);

            if (value == null)
            {
                return new PersonMatch
                {
                    Kind = PersonMatchKind.NoEmail,
                    Reason = "The case carries no " + label + " email, so nobody can be identified.",
                };
            }

            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("emailaddress1", ContactRegistry.StaffCodeAttr),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("emailaddress1", ConditionOperator.Equal, value);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var matches = service.RetrieveMultiple(query).Entities;

            if (matches.Count == 0)
            {
                return new PersonMatch
                {
                    Kind = PersonMatchKind.NoContact,
                    Reason = "No active contact holds the " + label + " email \"" + value + "\".",
                };
            }

            if (matches.Count > 1)
            {
                return new PersonMatch
                {
                    Kind = PersonMatchKind.Ambiguous,
                    Reason = "Two or more active contacts hold the email \"" + value
                        + "\", so nobody can be identified. Remove the duplicate contact.",
                };
            }

            return new PersonMatch
            {
                Kind = PersonMatchKind.Matched,
                Email = CasePeople.Clean(matches[0].GetAttributeValue<string>("emailaddress1")) ?? value,
                Contact = matches[0].ToEntityReference(),
                StaffCode = matches[0].GetAttributeValue<string>(ContactRegistry.StaffCodeAttr),
            };
        }
```

Replace the body and doc comment of `ParaplannerEmail` with:

```csharp
        /// <summary>
        /// The para-planner's email as the case stores it, or null (AD-228). Used directly as
        /// the letter address: a para-planner need not be a contact (BR-009), and the name is
        /// never used to guess one.
        /// </summary>
        public static string ParaplannerEmail(IOrganizationService service, EntityReference outcomeCase)
        {
            if (outcomeCase == null)
            {
                return null;
            }

            var row = service.Retrieve(
                "al_outcomecase", outcomeCase.Id, new ColumnSet(CasePeople.ParaplannerEmailAttr));
            return CasePeople.Clean(row.GetAttributeValue<string>(CasePeople.ParaplannerEmailAttr));
        }
```

- [ ] **Step 5: Rewrite `CaseAdviser.cs`**

```csharp
namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The email a case's adviser is known by (AD-228). The stored al_adviseremail, and only
    /// that: it keys al_advisermapping (the T&amp;C Manager, sign-off, regrade, the supervisor)
    /// and, through NotificationOutbox.MatchAdviser, the adviser's contact (remediation and
    /// access). It is never derived from al_advisername - that derivation is what left PROD
    /// case 256497798 with nobody when the contact's name was misspelled.
    /// </summary>
    public static class CaseAdviser
    {
        public const string EmailAttr = CasePeople.AdviserEmailAttr;
        public const string NameAttr = CasePeople.AdviserNameAttr;

        /// <summary>The adviser's email as the case stores it, trimmed, or null.</summary>
        public static string EmailFor(string storedEmail)
        {
            return CasePeople.Clean(storedEmail);
        }
    }
}
```

- [ ] **Step 6: Update every caller until the plug-in project builds**

- **`Remediation.cs`**: replace `AdviserContact` (942-977), and its doc, with:

```csharp
        /// <summary>
        /// The contact whose email the case stores as its adviser's, or null when the case
        /// has no email, or it matches no active contact or two (AD-228). Never by name.
        /// </summary>
        public static EntityReference AdviserContact(IOrganizationService service, EntityReference caseRef)
        {
            if (caseRef == null)
            {
                return null;
            }

            var row = service.Retrieve("al_outcomecase", caseRef.Id, new ColumnSet(CaseAdviser.EmailAttr));
            var match = NotificationOutbox.MatchAdviser(service, row.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            return match.IsMatch ? match.Contact : null;
        }
```

  In the `Raise` doc (786-791), change "the case carries the adviser as text (AD-029), so the
  name can match no contact or two" to "the case's adviser email can match no contact or two
  (AD-228)".

  In `AssignOpenActions`, change the start and the loop so an unmatched email unassigns:

```csharp
            var adviser = AdviserContact(service, caseRef);

            // ... query unchanged ...

            var moved = 0;
            foreach (var action in CommandHelpers.RetrieveAll(service, query))
            {
                var holder = action.GetAttributeValue<EntityReference>("al_assignedcontactid");

                // The email matches nobody: the action leaves whoever held it, because they
                // are not the adviser on this case. Nobody is told - there is nobody to tell.
                if (adviser == null)
                {
                    if (holder == null)
                    {
                        continue;
                    }

                    service.Update(new Entity(ActionEntity, action.Id) { ["al_assignedcontactid"] = null });
                    moved++;
                    continue;
                }

                if (holder != null && holder.Id == adviser.Id)
                {
                    continue;
                }

                service.Update(new Entity(ActionEntity, action.Id) { ["al_assignedcontactid"] = adviser });
                NotificationEmitterPlugin.QueueRemediationAssigned(service, correlationId, action.Id);
                moved++;
            }

            return moved;
```

  Delete the old `if (adviser == null) { return 0; }`. In the method's doc, replace "the
  adviser now named" with "the contact holding the adviser email now on the case", and add one
  sentence: "An email that matches nobody unassigns the open actions."
- **`NotificationEmitterPlugin.QueueCasePassed`**: add `CaseAdviser.EmailAttr` to the
  `ColumnSet`. Replace the `AdviserContact` line and its comment with:

```csharp
            // The stored adviser email, as an address (AD-228). Not resolved to a contact: the
            // letter reaches the adviser whether or not they are onboarded, and the name is
            // never used to guess one. A blank email still queues the row - see Queue.
            var email = CaseAdviser.EmailFor(caseRow.GetAttributeValue<string>(CaseAdviser.EmailAttr));
```

- **`NotificationRecipients.AdviserEmail`**: retrieve only `TcManagerRouting.CaseAdviserEmailAttr`
  and `return CaseAdviser.EmailFor(row.GetAttributeValue<string>(TcManagerRouting.CaseAdviserEmailAttr));`.
  Remove the `name` and `match` lines. Delete `CaseAdviserNameAttr` only if nothing else in
  the file uses it.
- **`TcManagerRouting.ForCase`**:

```csharp
            // The stored adviser email only (AD-228). The name is never used to find one.
            var row = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet(CaseAdviserEmailAttr));
            return ForAdviserEmail(service, CaseAdviser.EmailFor(row.GetAttributeValue<string>(CaseAdviserEmailAttr)));
```

  In `ForAdviserEmail`, add `query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);`
  after the email condition. That makes the reconciler's `SupervisorFor` and this method agree
  that an inactive mapping is no mapping. At line 33, replace the doc sentence about the
  para-planner being matched by name with: "Both people on a case are identified by email
  (AD-228)."
- **`CaseAccessReconciler.cs` 88-91**:

```csharp
                input.ResolvedSupervisor = SupervisorFor(service, CaseAdviser.EmailFor(
                    outcomeCase.GetAttributeValue<string>("al_adviseremail")));
```

- **`GenerateExportPlugin.cs`**:
  - `ParaplannerMatch` calls
    `NotificationOutbox.MatchParaplanner(service, outcomeCase.GetAttributeValue<string>(ImportRules.ParaplannerEmailAttribute))`.
  - `AdviserCode` calls
    `NotificationOutbox.MatchAdviser(service, outcomeCase.GetAttributeValue<string>("al_adviseremail"))`.
  - Replace the stale doc at 190-194 that says the para-planner has no email column with:
    "Both people are identified by the email the case stores (AD-228)."
- **`ImportCasesPlugin.cs`**:
  - Delete lines 178-184: the comment block and `CaseAdviser.FillMissingEmail(systemService, record);`.
  - Change the two report matches to
    `NotificationOutbox.MatchParaplanner(systemService, record.GetAttributeValue<string>(ImportRules.ParaplannerEmailAttribute))`
    and
    `NotificationOutbox.MatchAdviser(systemService, record.GetAttributeValue<string>("al_adviseremail"))`.
  - Replace the comment "The para-planner is matched to a Contact by name, which is weak"
    with: "Both emails are now required on the row (Task 3); this reports one that no active
    contact holds, or that two hold, on the day of the upload."
- **`UpdateCaseDetailsPlugin.ApplyFields`**: delete the final two comment lines and
  `CaseAdviser.FollowName(service, before, update, changes);`. Task 2 puts the email rule
  there.
- **`SubmitReviewPlugin.cs` ~260-261**: replace the comment saying the para-planner has no
  email column with "The para-planner's stored email (AD-228)."

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet build OutcomeTesting.Plugins.Tests 2>&1 | grep -E " error "`

Expected: errors only in test files. Fix them in Step 7.

- [ ] **Step 7: Fix the test fixtures that encoded name matching**

Apply these rules to each listed test file. They are the only files that reference the
removed APIs or seed a person by name alone:

`AccountabilityCodeTests`, `AdviserNotificationTests`, `CaseAccessReconcilerTests`,
`CaseAdviserEditTests`, `CaseAdviserTests`, `CaseHeaderRequestPluginTests`,
`CompletedCheckDocumentTests`, `CompletedCheckMarkerTests`, `GenerateExportPluginTests`,
`HeaderStaysEditableAfterTaxTests`, `ImportRulesTests`, `NamedAccountabilityTests`,
`NotificationBodiesTests`, `NotificationOutboxTests`, `NotificationRecipientReachesSenderTests`,
`NotificationRecipientTests`, `ParaplannerByEmailTests`, `ParaplannerEmailExportTests`,
`ParaplannerMatchTests`, `PersonMatchTests`, `RemediationAssignmentTests`,
`StaffCodeExportTests`, `StaffCodeMatchTests`, `TcManagerRoutingTests`.

1. **Call sites.** Drop the name argument:
   - `MatchAdviser(svc, email, name)` → `MatchAdviser(svc, email)`
   - `MatchParaplanner(svc, email, name)` → `MatchParaplanner(svc, email)`
   - `MatchPerson(svc, email, name, role)` → `MatchPerson(svc, email, role)`
   - `CaseAdviser.EmailFor(svc, stored, name)` → `CaseAdviser.EmailFor(stored)`
2. **A fixture that seeds a case with `al_advisername` (or `al_paraplanner`) and a contact of
   that name, and expects the person to be found.** Also seed `al_adviseremail` (or
   `al_paraplanneremail`) equal to the contact's `emailaddress1`. Example:
   `RemediationAssignmentTests.Case` becomes
   `svc.Seed("al_outcomecase", CaseId, "al_casereference", "IO-TEST-009", "al_advisername", adviserName, "al_adviseremail", adviserName == "Sam Adviser" ? "sam@example.com" : null);`.
   Do the same in `AdviserNotificationTests.Case`.
3. **A test whose subject is the name fallback.** Examples: "falls back to the name when no
   email", "resolves by name", "two contacts of one name are ambiguous" (the name variant),
   `PersonMatchKind.NoEmail` as "contact has no work email", and every `CaseAdviserTests` /
   `CaseAdviserEditTests` test of `FillMissingEmail` or `FollowName`. Invert it to assert the
   name is NOT used (the result is `NoEmail` / null / no recipient), or delete it where Task 1
   already covers the inverted behaviour in `EmailIdentityTests`. In `ParaplannerMatchTests`:
   - change `PersonMatchKind.NoName` to `PersonMatchKind.NoEmail`;
   - delete the `[InlineData(PersonMatchKind.NoEmail)]` case for "contact has no email", and
     its `switch` arm.
4. **`TcManagerRoutingTests`, and any other test that seeds `al_advisermapping` and expects a
   route.** Add `"statecode", new OptionSetValue(0)` to the mapping seed. The routing query now
   filters on it.
5. **`AssignOpenActions` tests asserting `0` for an unmatched name while an action had a
   holder.** These now expect the holder cleared and a count of 1. That matches
   `EmailIdentityTests.AssignOpenActions_unassigns_when_the_email_matches_nobody`.

- [ ] **Step 8: Run the whole plug-in suite**

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests`

Expected: `Passed!`, with no failures, including the 21 `EmailIdentityTests` (14 facts, plus
7 cases of the email-shape theory).

- [ ] **Step 9: Commit**

```bash
git add plugins/OutcomeTesting.Plugins plugins/OutcomeTesting.Plugins.Tests
git commit -m "feat(plugins): people on a case are resolved by email, never by name

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Edits write a name and an email together

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/CasePeople.cs`
- Modify: `plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs` (the `Editables` map 530-536, the end of `ApplyFields`, the adviser block 239-253)
- Modify: `plugins/OutcomeTesting.Plugins/CaseHeaderRequestPlugin.cs` (`CheckerEditable` 374-401, the adviser block 265-276)
- Test: `plugins/OutcomeTesting.Plugins.Tests/CasePeopleEditTests.cs`

**Interfaces:**
- Consumes: `CasePeople.Clean`, `CasePeople.IsEmail` and the attribute constants (Task 1);
  `Remediation.AssignOpenActions` (Task 1).
- Produces:
  - `static void CasePeople.EnsureEmails(Entity before, Entity update)`. It throws
    `InvalidPluginExecutionException` with `CommandHelpers.ValidationPrefix`.
  - `static bool CasePeople.AdviserEmailChanged(Entity before, Entity update)`.
  - Editable field keys `al_adviseremail` ("Adviser email") and `al_paraplanneremail`
    ("Paraplanner email"), accepted by `al_UpdateCaseDetails` and `al_CaseHeaderRequest`.

- [ ] **Step 1: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/CasePeopleEditTests.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// An edit names a person by name AND email (AD-228). A name alone is refused; an email that
    /// is not one is refused; changing only the label moves nothing.
    /// </summary>
    public class CasePeopleEditTests
    {
        private static Entity Before(string name = "Sam Adviser", string email = "sam@example.com")
        {
            return new Entity("al_outcomecase", Guid.NewGuid())
            {
                ["al_advisername"] = name,
                ["al_adviseremail"] = email,
                ["al_paraplanner"] = "Pip Planner",
                ["al_paraplanneremail"] = "pip@example.com",
            };
        }

        private static Entity Update(params object[] pairs)
        {
            var update = new Entity("al_outcomecase", Guid.NewGuid());
            for (var i = 0; i < pairs.Length; i += 2)
            {
                update[(string)pairs[i]] = pairs[i + 1];
            }

            return update;
        }

        [Fact]
        public void A_name_sent_without_its_email_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update("al_advisername", "Adam Strumidlo")));
            Assert.Contains("adviser's email", error.Message);
        }

        [Fact]
        public void A_paraplanner_name_sent_without_its_email_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update("al_paraplanner", "New Planner")));
            Assert.Contains("paraplanner's email", error.Message);
        }

        [Fact]
        public void A_name_with_a_valid_email_is_accepted()
        {
            CasePeople.EnsureEmails(Before(), Update(
                "al_advisername", "Adam Strumidlo",
                "al_adviseremail", "adam.strumidlo@example.com"));
        }

        [Fact]
        public void A_name_in_the_email_column_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update(
                    "al_advisername", "Adam Strumidlo",
                    "al_adviseremail", "Adam Strumidlo")));
            Assert.Contains("not an email address", error.Message);
        }

        [Fact]
        public void Clearing_the_email_while_a_name_stays_is_refused()
        {
            Assert.Throws<InvalidPluginExecutionException>(() =>
                CasePeople.EnsureEmails(Before(), Update("al_adviseremail", null)));
        }

        [Fact]
        public void Clearing_both_name_and_email_is_allowed()
        {
            CasePeople.EnsureEmails(Before(), Update("al_advisername", null, "al_adviseremail", null));
        }

        [Fact]
        public void A_legacy_case_with_no_email_can_get_one_with_its_name()
        {
            CasePeople.EnsureEmails(Before(email: null), Update(
                "al_advisername", "Sam Adviser",
                "al_adviseremail", "sam@example.com"));
        }

        [Fact]
        public void An_unrelated_edit_on_a_legacy_case_with_no_email_is_not_refused()
        {
            CasePeople.EnsureEmails(Before(email: null), Update("al_clientname", "Mr Client"));
        }

        [Theory]
        [InlineData("sam@example.com", "SAM@example.com ", false)]
        [InlineData("sam@example.com", "other@example.com", true)]
        [InlineData(null, "sam@example.com", true)]
        public void AdviserEmailChanged_ignores_case_and_spaces(string was, string now, bool changed)
        {
            Assert.Equal(changed, CasePeople.AdviserEmailChanged(
                Before(email: was), Update("al_adviseremail", now)));
        }

        [Fact]
        public void A_name_only_change_is_not_an_email_change()
        {
            Assert.False(CasePeople.AdviserEmailChanged(
                Before(), Update("al_advisername", "Sam A", "al_adviseremail", "sam@example.com")));
        }
    }
}
```

Also add one test to `CaseHeaderRequestPluginTests.cs`, next to its existing allowlist tests:

```csharp
        [Fact]
        public void The_portal_may_send_the_two_person_emails()
        {
            CaseHeaderRequestPlugin.EnsureCheckerEditable(new System.Collections.Generic.Dictionary<string, string>
            {
                { "al_adviseremail", "sam@example.com" },
                { "al_paraplanneremail", "pip@example.com" },
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~CasePeopleEditTests|FullyQualifiedName~The_portal_may_send_the_two_person_emails"`

Expected:
- a build failure: `EnsureEmails` and `AdviserEmailChanged` are not defined;
- after a stub, the portal test fails with "Field 'al_adviseremail' cannot be edited".

- [ ] **Step 3: Add the rule to `CasePeople.cs`**

Add `using Microsoft.Xrm.Sdk;` at the top, and inside the class:

```csharp
        /// <summary>
        /// Refuses an edit that names a person without their email, or gives an email that is
        /// not one (AD-228). Only the people the update touches are checked, so an unrelated
        /// edit to a case imported before emails were required still saves.
        /// </summary>
        public static void EnsureEmails(Entity before, Entity update)
        {
            EnsurePair(before, update, AdviserNameAttr, AdviserEmailAttr, "adviser");
            EnsurePair(before, update, ParaplannerNameAttr, ParaplannerEmailAttr, "paraplanner");
        }

        /// <summary>True when this update moves the adviser email (case and spaces ignored).</summary>
        public static bool AdviserEmailChanged(Entity before, Entity update)
        {
            if (update == null || !update.Contains(AdviserEmailAttr))
            {
                return false;
            }

            var was = Clean(before == null ? null : before.GetAttributeValue<string>(AdviserEmailAttr));
            var now = Clean(update.GetAttributeValue<string>(AdviserEmailAttr));
            return !string.Equals(was ?? string.Empty, now ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsurePair(Entity before, Entity update, string nameAttr, string emailAttr, string who)
        {
            if (update == null || (!update.Contains(nameAttr) && !update.Contains(emailAttr)))
            {
                return;
            }

            if (update.Contains(nameAttr) && !update.Contains(emailAttr))
            {
                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix
                    + "Give the " + who + "'s email as well as their name. People on a case are "
                    + "identified by email, because two people can share a name.");
            }

            var name = Clean(update.Contains(nameAttr)
                ? update.GetAttributeValue<string>(nameAttr)
                : before == null ? null : before.GetAttributeValue<string>(nameAttr));
            var email = Clean(update.GetAttributeValue<string>(emailAttr));

            if (email == null)
            {
                if (name != null)
                {
                    throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix
                        + "The " + who + "'s email cannot be cleared while the case still names them.");
                }

                return;
            }

            if (!IsEmail(email))
            {
                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix
                    + "The " + who + "'s email \"" + email + "\" is not an email address.");
            }
        }
```

`CommandHelpers.ValidationPrefix` is in the same assembly.

- [ ] **Step 4: Accept the emails and apply the rule in `UpdateCaseDetailsPlugin.cs`**

In `Editables`, after the `al_advisername` line (534), add:

```csharp
                { "al_adviseremail", new EditableField(EditableKind.Text, "Adviser email") },
```

After the `al_paraplanner` line (536), add:

```csharp
                { "al_paraplanneremail", new EditableField(EditableKind.Text, "Paraplanner email") },
```

At the end of `ApplyFields`, where Task 1 removed `FollowName`, add:

```csharp
            // People are identified by email (AD-228): a name travels with its email, and the
            // email must be one. Both front ends reach here.
            CasePeople.EnsureEmails(before, update);
```

Replace the adviser block (239-253) with:

```csharp
            // Remediation and the adviser's access follow the adviser EMAIL (AD-228), so a new
            // email takes the open actions with it - to the contact holding it, or to nobody
            // when no contact does. A name-only change is a label and moves nothing. Completed
            // actions keep whoever did the work.
            if (CasePeople.AdviserEmailChanged(before, update))
            {
                var moved = Remediation.AssignOpenActions(
                    systemService, new EntityReference(CaseEntity, targetId), context.CorrelationId);
                if (moved > 0)
                {
                    changes.Add("Re-pointed " + moved + " open remediation action(s) to the adviser email now on the case");
                }
            }
```

Confirm that `before` holds both email columns. The `columns` list at line 631 already
includes `CaseAdviser.EmailAttr`. Add `CasePeople.ParaplannerEmailAttr`,
`CasePeople.AdviserNameAttr` and `CasePeople.ParaplannerNameAttr` to that list if they are
missing.

- [ ] **Step 5: The same in `CaseHeaderRequestPlugin.cs`**

In `CheckerEditable`, after `"al_advisername",` add `"al_adviseremail",`, and after
`"al_paraplanner",` add `"al_paraplanneremail",`.

Replace the block at 265-276 with:

```csharp
            // The adviser EMAIL routes remediation (AD-228), so a new email takes the open
            // actions with it, exactly as the Code App's edit does.
            if (CasePeople.AdviserEmailChanged(before, update))
            {
                var moved = Remediation.AssignOpenActions(
                    service, new EntityReference(CaseEntity, caseId), context.CorrelationId);
                if (moved > 0)
                {
                    changes.Add("Re-pointed " + moved + " open remediation action(s) to the adviser email now on the case");
                }
            }
```

The `before` retrieve at line 203 already names `CaseAdviser.EmailAttr`. Add
`CasePeople.ParaplannerEmailAttr`, `CasePeople.AdviserNameAttr` and
`CasePeople.ParaplannerNameAttr` to that column list.

- [ ] **Step 6: Run the whole plug-in suite**

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests`

Expected: `Passed!`. If an existing `UpdateCaseDetailsTests`, `CaseHeaderRequestPluginTests`
or `CaseAdviserEditTests` test edits `al_advisername` alone and expects success, add
`al_adviseremail` to its payload. The rule is intended.

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins plugins/OutcomeTesting.Plugins.Tests
git commit -m "feat(plugins): case edits carry the person's email with their name

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The import rejects a row without the people's emails

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/ImportRules.cs` (`ParseCsv`, after the column loop ~800; `Columns` comments 175-205; the summary at 155 and 675)
- Modify: `app/src/features/imports/caseUpload.ts` (`parseCaseCsv`, after the column loop ~460)
- Create: `app/src/features/cases/casePeople.ts`
- Test: `plugins/OutcomeTesting.Plugins.Tests/ImportRulesTests.cs`, `app/src/features/imports/caseUpload.test.ts`, `app/src/features/cases/casePeople.test.ts`

**Interfaces:**
- Consumes: `CasePeople.IsEmail` (Task 1).
- Produces:
  - `static string ImportRules.PeopleError(IDictionary<string, object> values)`: null, or the
    row's rejection reason.
  - `app/src/features/cases/casePeople.ts` exports:
    - `isEmail(value: string | null | undefined): boolean`
    - `PERSON_PAIRS`
    - `withPersonPairs(changed, form)`
    - `personRefusals(changed, form)`

    Task 6 uses the last three.

- [ ] **Step 1: Write the failing plug-in tests**

Add to `ImportRulesTests.cs`:

```csharp
        private const string PeopleHeader =
            "TaskID,AdviserName,AdviserEmail,AssignedBy,ParaplannerEmail";

        [Theory]
        [InlineData("T1,Sam Adviser,,Pip Planner,pip@example.com", "AdviserEmail")]
        [InlineData("T1,Sam Adviser,sam@example.com,Pip Planner,", "ParaplannerEmail")]
        [InlineData("T1,,sam@example.com,Pip Planner,pip@example.com", "AdviserName")]
        [InlineData("T1,Sam Adviser,sam@example.com,,pip@example.com", "AssignedBy")]
        [InlineData("T1,Sam Adviser,Sam Adviser,Pip Planner,pip@example.com", "is not an email address")]
        public void A_row_missing_a_persons_name_or_email_is_rejected(string row, string reasonFragment)
        {
            var result = ImportRules.ParseCsv(PeopleHeader + "\n" + row);

            Assert.Empty(result.Valid);
            var error = Assert.Single(result.Invalid);
            Assert.Contains(reasonFragment, error.Reason);
        }

        [Fact]
        public void A_file_without_the_email_columns_rejects_every_row()
        {
            var result = ImportRules.ParseCsv("TaskID,AdviserName,AssignedBy\nT1,Sam Adviser,Pip Planner");

            Assert.Empty(result.Valid);
            Assert.Contains("AdviserEmail", Assert.Single(result.Invalid).Reason);
        }
```

The passing path stays covered by the existing valid-row tests once Step 3 updates their
fixtures.

- [ ] **Step 2: Run them to verify they fail**

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~ImportRulesTests"`

Expected: the 6 new cases FAIL (the rows are accepted).

- [ ] **Step 3: Implement `PeopleError` and call it**

In `ImportRules.cs`, add:

```csharp
        /// <summary>
        /// Why a row cannot become a case for want of its people, or null (AD-228, owner
        /// 2026-10-02). Both people need a name, which is what a person reads, and an email,
        /// which is who they are. A row naming someone with no address would route remediation,
        /// sign-off and letters by a name - the matching that misrouted PROD case 256497798.
        /// </summary>
        public static string PeopleError(IDictionary<string, object> values)
        {
            return PersonError(values, "AdviserName", CasePeople.AdviserNameAttr, "AdviserEmail", CasePeople.AdviserEmailAttr)
                ?? PersonError(values, "AssignedBy", CasePeople.ParaplannerNameAttr, "ParaplannerEmail", CasePeople.ParaplannerEmailAttr);
        }

        private static string PersonError(
            IDictionary<string, object> values, string nameHeader, string nameAttr, string emailHeader, string emailAttr)
        {
            object name;
            object email;
            values.TryGetValue(nameAttr, out name);
            values.TryGetValue(emailAttr, out email);

            if (CasePeople.Clean(name as string) == null)
            {
                return "\"" + nameHeader + "\" is empty. Every case needs this person's name.";
            }

            var address = CasePeople.Clean(email as string);
            if (address == null)
            {
                return "\"" + emailHeader + "\" is empty. People are identified by email, so a case "
                    + "cannot be created without it.";
            }

            return CasePeople.IsEmail(address)
                ? null
                : "\"" + emailHeader + "\" value \"" + address + "\" is not an email address.";
        }
```

In `ParseCsv`, immediately before the `// The checklist is the route's only input now (D6)`
block, add:

```csharp
                // People are identified by email (AD-228): a row without both people's name
                // and email is rejected rather than created routable by name.
                if (rowError == null)
                {
                    rowError = PeopleError(values);
                }
```

Update the two summaries that say "only TaskID is mandatory" (lines ~155 and ~675). They
should now read: "TaskID, AdviserName, AdviserEmail, AssignedBy and ParaplannerEmail are
mandatory".

In the `Columns` comments for `AssignedBy` and `ParaplannerEmail`, replace "it is the fallback
when a row carries no address" with "a row without an address is rejected (AD-228)".

- [ ] **Step 4: Bring the existing import fixtures up to the new rule**

Run the suite. Every existing `ImportRulesTests` / `ImportCasesPluginTests` row that is
expected to be valid but lacks these columns now fails. Give those fixtures the four columns
with valid values. Example: append
`,AdviserName,AdviserEmail,AssignedBy,ParaplannerEmail` to the header and
`,Sam Adviser,sam@example.com,Pip Planner,pip@example.com` to each row. Do not weaken the
rule to make an old fixture pass.

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests`

Expected: `Passed!`.

- [ ] **Step 5: Write the failing app tests**

Create `app/src/features/cases/casePeople.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { isEmail, personRefusals, withPersonPairs } from './casePeople';

describe('isEmail', () => {
  it.each([
    ['sam@example.com', true],
    ['  sam.o’neil@sub.example.co.uk ', true],
    ['Sam Adviser', false],
    ['sam@', false],
    ['sam@example', false],
    ['', false],
    [null, false],
  ])('%s -> %s', (value, expected) => {
    expect(isEmail(value)).toBe(expected);
  });
});

describe('withPersonPairs', () => {
  const form = {
    al_advisername: 'Adam Strumidlo',
    al_adviseremail: 'adam.strumidlo@example.com',
    al_paraplanner: 'Pip Planner',
    al_paraplanneremail: 'pip@example.com',
  };

  it('sends the email with a changed name', () => {
    expect(withPersonPairs({ al_advisername: 'Adam Strumidlo' }, form)).toEqual({
      al_advisername: 'Adam Strumidlo',
      al_adviseremail: 'adam.strumidlo@example.com',
    });
  });

  it('sends the name with a changed email', () => {
    expect(withPersonPairs({ al_paraplanneremail: 'pip@example.com' }, form)).toEqual({
      al_paraplanner: 'Pip Planner',
      al_paraplanneremail: 'pip@example.com',
    });
  });

  it('leaves an unrelated change alone', () => {
    expect(withPersonPairs({ al_clientname: 'Mr Client' }, form)).toEqual({ al_clientname: 'Mr Client' });
  });
});

describe('personRefusals', () => {
  it('refuses a changed name with no email', () => {
    expect(
      personRefusals(
        { al_advisername: 'Sam Adviser' },
        { al_advisername: 'Sam Adviser', al_adviseremail: '', al_paraplanner: '', al_paraplanneremail: '' },
      ),
    ).toEqual(["Give the adviser's email as well as their name."]);
  });

  it('refuses an email that is not one', () => {
    expect(
      personRefusals(
        { al_paraplanneremail: 'Pip Planner' },
        { al_advisername: '', al_adviseremail: '', al_paraplanner: 'Pip', al_paraplanneremail: 'Pip Planner' },
      ),
    ).toEqual(['The paraplanner\'s email "Pip Planner" is not an email address.']);
  });

  it('ignores a legacy case whose people were not touched', () => {
    expect(
      personRefusals(
        { al_clientname: 'Mr Client' },
        { al_advisername: 'Sam', al_adviseremail: '', al_paraplanner: '', al_paraplanneremail: '' },
      ),
    ).toEqual([]);
  });
});
```

Add to `app/src/features/imports/caseUpload.test.ts`:

```ts
describe('people on an imported row (email identity)', () => {
  const header = 'TaskID,AdviserName,AdviserEmail,AssignedBy,ParaplannerEmail';

  it.each([
    ['T1,Sam Adviser,,Pip Planner,pip@example.com', 'AdviserEmail'],
    ['T1,Sam Adviser,sam@example.com,Pip Planner,', 'ParaplannerEmail'],
    ['T1,,sam@example.com,Pip Planner,pip@example.com', 'AdviserName'],
    ['T1,Sam Adviser,Sam Adviser,Pip Planner,pip@example.com', 'is not an email address'],
  ])('rejects %s', (row, fragment) => {
    const result = parseCaseCsv(`${header}\n${row}`);
    expect(result.valid).toHaveLength(0);
    expect(result.invalid[0].reason).toContain(fragment);
  });
});
```

Make sure `parseCaseCsv` is imported at the top of that test file.

- [ ] **Step 6: Run them to verify they fail**

Run: `cd app; npx vitest run src/features/cases/casePeople.test.ts src/features/imports/caseUpload.test.ts`

Expected:
- `casePeople.test.ts` FAILS to import (the module does not exist);
- the 4 new upload cases FAIL.

- [ ] **Step 7: Create `app/src/features/cases/casePeople.ts`**

```ts
/**
 * People on a case are identified by EMAIL, never by name (owner, 2026-10-02: two people can
 * share a name). The name is a label written beside the email. The server applies the same
 * rules in CasePeople.cs; these spare a round trip.
 */

const EMAIL_SHAPE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

export function isEmail(value: string | null | undefined): boolean {
  const trimmed = (value ?? '').trim();
  return trimmed !== '' && EMAIL_SHAPE.test(trimmed);
}

export const PERSON_PAIRS = [
  { name: 'al_advisername', email: 'al_adviseremail', who: 'adviser' },
  { name: 'al_paraplanner', email: 'al_paraplanneremail', who: 'paraplanner' },
] as const;

export type PersonForm = Record<(typeof PERSON_PAIRS)[number]['name' | 'email'], string>;

/** The changed fields, with each touched person's other half added so the two travel together. */
export function withPersonPairs(
  changed: Record<string, string>,
  form: PersonForm,
): Record<string, string> {
  const out = { ...changed };
  for (const pair of PERSON_PAIRS) {
    if (pair.name in changed || pair.email in changed) {
      out[pair.name] = form[pair.name] ?? '';
      out[pair.email] = form[pair.email] ?? '';
    }
  }
  return out;
}

/** What stops the save, for the people this save touches only. */
export function personRefusals(changed: Record<string, string>, form: PersonForm): string[] {
  const refusals: string[] = [];
  for (const pair of PERSON_PAIRS) {
    if (!(pair.name in changed) && !(pair.email in changed)) continue;
    const name = (form[pair.name] ?? '').trim();
    const email = (form[pair.email] ?? '').trim();
    if (name !== '' && email === '') {
      refusals.push(`Give the ${pair.who}'s email as well as their name.`);
    } else if (email !== '' && !isEmail(email)) {
      refusals.push(`The ${pair.who}'s email "${email}" is not an email address.`);
    }
  }
  return refusals;
}
```

- [ ] **Step 8: Apply the import rule in `caseUpload.ts`**

Add `import { isEmail } from '../cases/casePeople';`. In `parseCaseCsv`, immediately before
the `// The checklist is the route's only input now` block, add:

```ts
    // People are identified by email: a row without both people's name and email is
    // rejected, as the server rejects it (ImportRules.PeopleError).
    if (rowError === null) {
      rowError = peopleError(record);
    }
```

and below `parseCaseCsv`:

```ts
function peopleError(record: Record<string, unknown>): string | null {
  const people = [
    ['AdviserName', 'al_advisername', 'AdviserEmail', 'al_adviseremail'],
    ['AssignedBy', 'al_paraplanner', 'ParaplannerEmail', 'al_paraplanneremail'],
  ] as const;
  for (const [nameHeader, nameField, emailHeader, emailField] of people) {
    const name = String(record[nameField] ?? '').trim();
    const email = String(record[emailField] ?? '').trim();
    if (name === '') return `"${nameHeader}" is empty. Every case needs this person's name.`;
    if (email === '') {
      return `"${emailHeader}" is empty. People are identified by email, so a case cannot be created without it.`;
    }
    if (!isEmail(email)) return `"${emailHeader}" value "${email}" is not an email address.`;
  }
  return null;
}
```

- [ ] **Step 9: Fix the app's existing valid-row fixtures and run everything**

Run: `cd app; npx vitest run`. As in Step 4, give each existing upload fixture that is expected
to be valid the four people columns with valid values.

Then: `npx tsc -b`

Expected: all tests pass, and `tsc -b` prints nothing.

- [ ] **Step 10: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/ImportRules.cs plugins/OutcomeTesting.Plugins.Tests app/src/features/cases/casePeople.ts app/src/features/cases/casePeople.test.ts app/src/features/imports
git commit -m "feat(import): a row needs both people's name and email

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Open actions find an adviser whose contact appears later

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/AdviserContactPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/AdviserContactPluginTests.cs`

**Interfaces:**
- Consumes:
  - `Remediation.AssignOpenActions` (Task 1)
  - `CaseAccessReconciler.Reconcile(IOrganizationService, Guid, DateTime)`
  - `CasePeople.Clean` (Task 1)
- Produces:
  - step type `OutcomeTesting.Plugins.AdviserContactPlugin`, registered in Task 8 on
    `contact` Create, and Update filtered to `emailaddress1,statecode`, stage 40, sync;
  - `static string AdviserContactPlugin.EmailOf(IPluginExecutionContext context, IOrganizationService service)`;
  - `static IList<Guid> AdviserContactPlugin.CasesFor(IOrganizationService service, string email)`;
  - `static int AdviserContactPlugin.Follow(IOrganizationService service, string email, Guid correlationId)`.
    It returns the number of actions moved.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A contact created, reactivated, or given its email after remediation was raised picks
    /// the open actions up (AD-228). Until now they moved only when someone edited the case.
    /// </summary>
    public class AdviserContactPluginTests
    {
        private static readonly Guid CaseId = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000001");
        private static readonly Guid ContactId = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000002");

        private static FakeOrganizationService World(int contactState = 0)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-LATE-1",
                "al_advisername", "Adam Strumidlo",
                "al_adviseremail", "adam.strumidlo@example.com");
            svc.Seed("contact", ContactId,
                "fullname", "Adam Strumdlio",
                "emailaddress1", "adam.strumidlo@example.com",
                "statecode", new OptionSetValue(contactState));
            return svc;
        }

        private static Guid Action(FakeOrganizationService svc, int status, Guid? holder = null)
        {
            var row = svc.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(status),
                "al_name", "Remediation IO-LATE-1");
            if (holder.HasValue)
            {
                row["al_assignedcontactid"] = new EntityReference("contact", holder.Value);
            }

            return row.Id;
        }

        [Fact]
        public void A_new_contact_takes_the_unassigned_open_actions_on_cases_with_their_email()
        {
            var svc = World();
            var action = Action(svc, Remediation.StatusOpen);

            Assert.Equal(1, AdviserContactPlugin.Follow(svc, "adam.strumidlo@example.com", Guid.NewGuid()));
            Assert.Equal(ContactId, svc.Row("al_remediationaction", action)
                .GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void A_completed_action_is_never_moved()
        {
            var svc = World();
            var done = Action(svc, Remediation.StatusCompleted);

            Assert.Equal(0, AdviserContactPlugin.Follow(svc, "adam.strumidlo@example.com", Guid.NewGuid()));
            Assert.Null(svc.Row("al_remediationaction", done).GetAttributeValue<EntityReference>("al_assignedcontactid"));
        }

        [Fact]
        public void A_deactivated_contact_releases_their_open_actions()
        {
            var svc = World(contactState: 1);
            var action = Action(svc, Remediation.StatusOpen, ContactId);

            Assert.Equal(1, AdviserContactPlugin.Follow(svc, "adam.strumidlo@example.com", Guid.NewGuid()));
            Assert.Null(svc.Row("al_remediationaction", action).GetAttributeValue<EntityReference>("al_assignedcontactid"));
        }

        [Fact]
        public void An_email_no_case_carries_touches_nothing()
        {
            var svc = World();
            Action(svc, Remediation.StatusOpen);

            Assert.Equal(0, AdviserContactPlugin.Follow(svc, "someone.else@example.com", Guid.NewGuid()));
        }

        [Fact]
        public void A_blank_email_touches_nothing()
        {
            Assert.Equal(0, AdviserContactPlugin.Follow(World(), "  ", Guid.NewGuid()));
        }

        [Fact]
        public void The_email_comes_from_the_target_when_the_write_carries_it()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["emailaddress1"] = "new@example.com" };

            Assert.Equal("new@example.com", AdviserContactPlugin.EmailOf(context, World()));
        }

        [Fact]
        public void The_email_is_read_from_the_row_when_only_the_state_changed()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "contact", PrimaryEntityId = ContactId };
            context.InputParameters["Target"] = new Entity("contact", ContactId) { ["statecode"] = new OptionSetValue(0) };

            Assert.Equal("adam.strumidlo@example.com", AdviserContactPlugin.EmailOf(context, World()));
        }
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~AdviserContactPluginTests"`

Expected: a build failure, because `AdviserContactPlugin` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// When a contact is created, reactivated, deactivated or given a new email, the open
    /// remediation actions on every case whose adviser email is theirs follow it (AD-228).
    /// Registered synchronous post-operation on contact Create, and Update filtered to
    /// emailaddress1 and statecode.
    ///
    /// Before this, an action raised for an adviser not yet onboarded stayed unassigned until
    /// somebody edited the case - which is how PROD case 256497798 waited on a contact whose
    /// name was misspelled. The email on the case already said who the adviser was; this lets
    /// the contact catch up with it.
    ///
    /// SYSTEM, as CaseAccessPlugin is: the bookkeeping is a consequence of the write, not the
    /// caller's edit, and onboarding runs as whoever onboards.
    /// </summary>
    public class AdviserContactPlugin : PluginBase
    {
        private const string CaseEntity = "al_outcomecase";

        public AdviserContactPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AdviserContactPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            Follow(system, EmailOf(context, system), context.CorrelationId);
        }

        /// <summary>Re-points the open actions of every case carrying this adviser email.</summary>
        public static int Follow(IOrganizationService service, string email, Guid correlationId)
        {
            var moved = 0;
            foreach (var caseId in CasesFor(service, email))
            {
                moved += Remediation.AssignOpenActions(service, new EntityReference(CaseEntity, caseId), correlationId);
                CaseAccessReconciler.Reconcile(service, caseId, DateTime.UtcNow);
            }

            return moved;
        }

        /// <summary>The contact's email: from the write when it carries one, else from the row.</summary>
        public static string EmailOf(IPluginExecutionContext context, IOrganizationService service)
        {
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target != null && target.Contains("emailaddress1"))
            {
                return CasePeople.Clean(target.GetAttributeValue<string>("emailaddress1"));
            }

            var row = service.Retrieve("contact", context.PrimaryEntityId, new ColumnSet("emailaddress1"));
            return CasePeople.Clean(row.GetAttributeValue<string>("emailaddress1"));
        }

        /// <summary>The cases whose adviser email is this one.</summary>
        public static IList<Guid> CasesFor(IOrganizationService service, string email)
        {
            var ids = new List<Guid>();
            var value = CasePeople.Clean(email);
            if (value == null)
            {
                return ids;
            }

            var query = new QueryExpression(CaseEntity) { ColumnSet = new ColumnSet(false), Criteria = new FilterExpression() };
            query.Criteria.AddCondition(CasePeople.AdviserEmailAttr, ConditionOperator.Equal, value);
            foreach (var row in CommandHelpers.RetrieveAll(service, query))
            {
                ids.Add(row.Id);
            }

            return ids;
        }
    }
}
```

`CaseAccessReconciler.Reconcile` reads the AQS team by name. If any of these tests fail
inside it because the fake has no team rows, seed the two teams and the account the way
`CaseAccessReconcilerTests` does. Do not stub the reconciler out.

- [ ] **Step 4: Run the whole plug-in suite**

Run: `cd plugins; DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests`

Expected: `Passed!`.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/AdviserContactPlugin.cs plugins/OutcomeTesting.Plugins.Tests/AdviserContactPluginTests.cs
git commit -m "feat(plugins): open actions follow an adviser contact that appears later

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Registration commands work by email, and the one-off backfill

**Files:**
- Modify: `plugins/OutcomeTesting.Registration/Program.cs`:
  - the dispatch for `setcasepeople` / `setcaseadviser` / `repointremediation` /
    `backfilladviseremail` (579-601);
  - `SetCaseAdviser` (~9946);
  - `SetCasePeople` (~9995);
  - `RepointRemediation` (~10067);
  - `BackfillAdviserEmail` (~10181-10257, replaced);
  - the usage header near line 154.

**Interfaces:**
- Produces the commands:
  - `setcaseadviser <org> <referenceLike> <name> <email> [--confirm <org>]`
  - `setcasepeople <org> <referenceLike> <email> [--confirm <org>]`, which sets adviser and
    para-planner name and email from the one contact holding that email
  - `repointremediation <org> <caseReference> [--confirm <org>]`, which works by the case's
    adviser email
  - `backfillpeopleemail <org> [--confirm <org>]`, which replaces `backfilladviseremail`

This tool has no test project. Its proof is the DEV dry run in Task 8.

- [ ] **Step 1: Dispatch**

Replace the four dispatch blocks with:

```csharp
if (args.Length >= 5 && args[0].Equals("setcaseadviser", StringComparison.OrdinalIgnoreCase))
{
    return SetCaseAdviser(args[1], args[2], args[3], args[4], ConfirmedFor(args, args[1]));
}

if (args.Length >= 4 && args[0].Equals("setcasepeople", StringComparison.OrdinalIgnoreCase))
{
    return SetCasePeople(args[1], args[2], args[3], ConfirmedFor(args, args[1]));
}

if (args.Length >= 3 && args[0].Equals("repointremediation", StringComparison.OrdinalIgnoreCase))
{
    return RepointRemediation(args[1], args[2], ConfirmedFor(args, args[1]));
}

if (args.Length >= 2 && args[0].Equals("backfillpeopleemail", StringComparison.OrdinalIgnoreCase))
{
    return BackfillPeopleEmail(args[1], ConfirmedFor(args, args[1]));
}
```

Keep the `backfilltaxoutcome` block between them unchanged.

- [ ] **Step 2: A shared contact-by-email lookup for the tool**

Add near the other people helpers:

```csharp
// The one active contact holding this email, or null with the reason. The tool's copy of
// NotificationOutbox.MatchPerson (AD-228): by email only, two rows read so a duplicate shows.
(Entity Contact, string Problem) ContactByEmail(ServiceClient svc, string email)
{
    var value = (email ?? string.Empty).Trim();
    if (value.Length == 0)
    {
        return (null, "no email");
    }

    var query = new QueryExpression("contact") { ColumnSet = new ColumnSet("fullname", "emailaddress1"), TopCount = 2 };
    query.Criteria.AddCondition("emailaddress1", ConditionOperator.Equal, value);
    query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
    var found = svc.RetrieveMultiple(query).Entities;
    return found.Count == 1 ? (found[0], null)
        : found.Count == 0 ? (null, "no active contact holds " + value)
        : (null, "two or more active contacts hold " + value);
}
```

Use whatever type `Connect` returns for `svc` (check its declaration; it is `ServiceClient`
elsewhere in this file).

- [ ] **Step 3: `SetCaseAdviser` takes a name and an email**

Change the signature to
`int SetCaseAdviser(string orgUrl, string referenceLike, string adviserName, string adviserEmail, bool confirm)`.
After the name check, add:

```csharp
    var email = adviserEmail.Trim();
    if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
    {
        Console.Error.WriteLine($"'{email}' is not an email address. People on a case are identified by email.");
        return 1;
    }
```

Then:
- add `<attribute name=\"al_adviseremail\"/>` to its fetch;
- make the comparison and the printout cover both values;
- write `["al_advisername"] = name, ["al_adviseremail"] = email` in the update.

- [ ] **Step 4: `SetCasePeople` works by email**

Rename the parameter `personName` to `personEmail`. Replace the `fullname` fetch and the
`matches.Count != 1` check with:

```csharp
    var (person, problem) = ContactByEmail(svc, personEmail);
    if (person == null)
    {
        Console.Error.WriteLine($"'{personEmail}': {problem}. Nothing written.");
        return 1;
    }

    var name = person.GetAttributeValue<string>("fullname");
    var email = person.GetAttributeValue<string>("emailaddress1").Trim();
    Console.WriteLine($"'{email}' is {name}.");
```

Write `al_advisername`, `al_adviseremail`, `al_paraplanner` and `al_paraplanneremail` from
`name` and `email`, plus `al_checkername` as before. Update the final message to match.

- [ ] **Step 5: `RepointRemediation` works by the case's adviser email**

In the case fetch, read `al_adviseremail` as well as `al_advisername`. Replace the
`fullname` contact fetch and its `contacts.Count != 1` check with:

```csharp
    var (adviser, problem) = ContactByEmail(svc, outcomeCase.GetAttributeValue<string>("al_adviseremail"));
    if (adviser == null)
    {
        Console.Error.WriteLine($"The case's adviser email resolves to nobody: {problem}. " +
            "Set the email on the case, or onboard the contact; the actions then follow on their own.");
        return 1;
    }
```

Delete the `adviserName` blank check (the email check replaces it). Keep the rest, using
`adviser`.

- [ ] **Step 6: Replace `BackfillAdviserEmail` with `BackfillPeopleEmail`**

Delete `BackfillAdviserEmail` and its doc comment. Add:

```csharp
/// <summary>
/// The one-off move to email identity (AD-228), run once per environment after the plug-ins
/// that stop reading names are deployed.
///
/// Reports, for every case:
///   FILL      a blank adviser/para-planner email that the name gives unambiguously (one active
///             contact of that exact name, with an email);
///   NO FILL   a blank email the name cannot give (no contact, two, or no email) - fix in the app;
///   MISMATCH  a stored email whose contact is NAMED differently from the case (an Adam
///             Strumidlo / Strumdlio typo, or a wrong address) - check by hand, nothing written.
/// With --confirm <org> it writes the FILL rows, then re-points every open remediation action
/// to the contact holding the case's adviser email (unassigning where nobody does), exactly
/// as AssignOpenActions does - without a "Remediation assigned" email, because this is a
/// repair, not new work. Completed actions never move. Run reconcileaccess --confirm after.
/// Safe to re-run: filled emails are no longer blank, and actions already right are skipped.
/// </summary>
int BackfillPeopleEmail(string orgUrl, bool confirm)
{
    using var svc = Connect(orgUrl);

    var cases = new List<Entity>();
    var query = new QueryExpression("al_outcomecase")
    {
        ColumnSet = new ColumnSet("al_casereference", "al_advisername", "al_adviseremail", "al_paraplanner", "al_paraplanneremail"),
        PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 },
    };
    while (true)
    {
        var page = svc.RetrieveMultiple(query);
        cases.AddRange(page.Entities);
        if (!page.MoreRecords) break;
        query.PageInfo.PageNumber++;
        query.PageInfo.PagingCookie = page.PagingCookie;
    }

    var byName = new Dictionary<string, (string Email, string Problem)>(StringComparer.OrdinalIgnoreCase);
    (string Email, string Problem) EmailFromName(string name)
    {
        if (byName.TryGetValue(name, out var known)) return known;
        var q = new QueryExpression("contact") { ColumnSet = new ColumnSet("emailaddress1"), TopCount = 2 };
        q.Criteria.AddCondition("fullname", ConditionOperator.Equal, name);
        q.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var m = svc.RetrieveMultiple(q).Entities;
        var e = m.Count == 1 ? (m[0].GetAttributeValue<string>("emailaddress1") ?? string.Empty).Trim() : string.Empty;
        // Declared, not var: a tuple literal holding null has no type of its own.
        (string Email, string Problem) result = m.Count == 0 ? (null, "no active contact of that name")
            : m.Count > 1 ? (null, "two or more active contacts of that name")
            : e.Length == 0 ? (null, "the contact has no email")
            : (e, null);
        byName[name] = result;
        return result;
    }

    var fills = new List<(Guid Id, string Attr, string Email, string Line)>();
    var lines = new List<string>();
    foreach (var c in cases)
    {
        var reference = c.GetAttributeValue<string>("al_casereference") ?? c.Id.ToString("D");
        foreach (var (nameAttr, emailAttr, who) in new[] { ("al_advisername", "al_adviseremail", "adviser"), ("al_paraplanner", "al_paraplanneremail", "para-planner") })
        {
            var name = (c.GetAttributeValue<string>(nameAttr) ?? string.Empty).Trim();
            var email = (c.GetAttributeValue<string>(emailAttr) ?? string.Empty).Trim();
            if (email.Length == 0)
            {
                if (name.Length == 0) continue;
                var (found, problem) = EmailFromName(name);
                if (found != null)
                {
                    fills.Add((c.Id, emailAttr, found, $"FILL      {reference}  {who} '{name}' <- {found}"));
                }
                else
                {
                    lines.Add($"NO FILL   {reference}  {who} '{name}': {problem}");
                }

                continue;
            }

            var (contact, _) = ContactByEmail(svc, email);
            var contactName = contact?.GetAttributeValue<string>("fullname");
            if (contact != null && name.Length > 0 && !string.Equals(contactName?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"MISMATCH  {reference}  {who} '{name}' but {email} belongs to '{contactName}'");
            }
        }
    }

    Console.WriteLine($"{cases.Count} case(s) read. {fills.Count} email(s) to fill.");
    foreach (var f in fills) Console.WriteLine("   " + f.Line);
    foreach (var l in lines) Console.WriteLine("   " + l);

    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run with --confirm <orgUrl> to fill and re-point open actions.");
        return 0;
    }

    foreach (var f in fills)
    {
        svc.Update(new Entity("al_outcomecase", f.Id) { [f.Attr] = f.Email });
    }

    var moved = 0;
    var adviserEmails = cases.ToDictionary(
        c => c.Id,
        c => fills.Where(f => f.Id == c.Id && f.Attr == "al_adviseremail").Select(f => f.Email).FirstOrDefault()
             ?? c.GetAttributeValue<string>("al_adviseremail"));
    var actions = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_remediationaction\"><attribute name=\"al_remediationactionid\"/>" +
        "<attribute name=\"al_outcomecaseid\"/><attribute name=\"al_assignedcontactid\"/><filter>" +
        "<condition attribute=\"al_actionstatus\" operator=\"ne\" value=\"120910602\"/>" +
        "</filter></entity></fetch>")).Entities;
    foreach (var action in actions)
    {
        var caseRef = action.GetAttributeValue<EntityReference>("al_outcomecaseid");
        if (caseRef == null || !adviserEmails.TryGetValue(caseRef.Id, out var adviserEmail)) continue;
        var (adviser, _) = ContactByEmail(svc, adviserEmail);
        var holder = action.GetAttributeValue<EntityReference>("al_assignedcontactid");
        if (adviser == null ? holder == null : holder != null && holder.Id == adviser.Id) continue;
        svc.Update(new Entity("al_remediationaction", action.Id)
        {
            ["al_assignedcontactid"] = adviser?.ToEntityReference(),
        });
        moved++;
    }

    Console.WriteLine($"Filled {fills.Count} email(s); re-pointed {moved} open action(s). Now run: reconcileaccess {orgUrl} --confirm {orgUrl}");
    return 0;
}
```

Add `using System.Linq;` at the top if the file lacks it.

Update the usage header near line 154 with one line per command from **Interfaces**. Replace the stale comment at ~9914-9915 that says the para-planner has no email column with "Both people are identified by email (AD-228)."

- [ ] **Step 7: Build**

Run: `cd plugins/OutcomeTesting.Registration; DOTNET_ROLL_FORWARD=Major dotnet build -c Release 2>&1 | tail -3`

Expected: `Build succeeded.`, 0 errors. Rebuild Debug as well, since the Debug exe is the one
used day to day.

- [ ] **Step 8: Commit**

```bash
git add plugins/OutcomeTesting.Registration/Program.cs
git commit -m "feat(registration): people commands work by email; backfillpeopleemail replaces the name backfill

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The Code App case edit picks a person and writes name and email

**Files:**
- Modify: `app/src/features/cases/caseDetailMapping.ts` (`CaseEditValues` 29-35, `edit:` mapping ~166-172)
- Modify: `app/src/components/form/UserPicker.tsx` (new `onPick` prop)
- Modify: `app/src/features/cases/CaseEditPanel.tsx`:
  - `FieldKind` (60);
  - the "Adviser and paraplanner" section (114-121);
  - `changedFields` (412-427);
  - `onSubmit` (490-516);
  - the `user` render branch (641-647) and the text input branch.
- Modify: `app/src/features/cases/CaseDetailPage.tsx` (the adviser-unmatched notice 197-203)
- Test: `app/src/features/cases/caseEditPeople.test.ts`

**Interfaces:**
- Consumes: `withPersonPairs`, `personRefusals`, `PersonForm` from `casePeople.ts` (Task 3).
- Produces:
  - `CaseEditValues.al_adviseremail: string`
  - `CaseEditValues.al_paraplanneremail: string`
  - `UserPicker` prop `onPick?: (user: DirectoryUser | null) => void`. It is called with the
    chosen directory user, or `null` when the typed text matches nobody.

- [ ] **Step 1: Write the failing test**

The app has no DOM testing library, so the picker's choice logic is tested as pure functions
exported from `UserPicker.tsx`. Create `app/src/features/cases/caseEditPeople.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { pickerOptionText, resolvePicker } from '../../components/form/UserPicker';
import type { DirectoryUser } from '../../hooks/useUserDirectory';

const adam1 = { id: 'c1', name: 'Adam Smith', email: 'adam.smith@example.com', active: true } as DirectoryUser;
const adam2 = { id: 'c2', name: 'Adam Smith', email: 'adam.smith2@example.com', active: true } as DirectoryUser;

describe('the case person picker', () => {
  it('shows a name with its email when the field writes an email too', () => {
    expect(pickerOptionText(adam2, 'name', true)).toBe('Adam Smith — adam.smith2@example.com');
    expect(pickerOptionText(adam2, 'name', false)).toBe('Adam Smith');
  });

  it('tells two people of one name apart by the email in the option', () => {
    expect(resolvePicker([adam1, adam2], 'name', true, 'adam smith — ADAM.SMITH2@example.com ')).toBe(adam2);
  });

  it('resolves nobody for text that matches no option', () => {
    expect(resolvePicker([adam1, adam2], 'name', true, 'Not Onboarded')).toBeNull();
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd app; npx vitest run src/features/cases/caseEditPeople.test.ts`

Expected: FAIL. `pickerOptionText` and `resolvePicker` are not exported.

- [ ] **Step 3: `UserPicker` gains `onPick` and shows the email when picking a name**

Export two pure functions from `UserPicker.tsx`, and use them inside the component in place of
its local `optionText` and `resolve`:

```ts
/** What the person sees for one user, and what they type to choose them. */
export function pickerOptionText(user: DirectoryUser, field: 'name' | 'email' | 'id', withEmail: boolean): string {
  if (field === 'email') return user.email;
  if (field === 'id' || withEmail) return `${user.name} — ${user.email}`;
  return user.name;
}

/** The user whose option text is this (case and spaces ignored), or null. */
export function resolvePicker(
  users: DirectoryUser[],
  field: 'name' | 'email' | 'id',
  withEmail: boolean,
  typed: string,
): DirectoryUser | null {
  const needle = typed.trim().toLowerCase();
  if (needle === '') return null;
  return users.find((user) => pickerOptionText(user, field, withEmail).toLowerCase() === needle) ?? null;
}
```

- Add `onPick?: (user: DirectoryUser | null) => void` to `Props`, with the doc comment: "Called
  with the person chosen, or null when the text matches nobody. A case person field uses it to
  write the email beside the name; with it, a name option shows the email, so two people of
  one name are two options."
- `withEmail` is `onPick !== undefined`. `stored` stays `user.name` for a name field.
- In the input's `onChange`:
  - empty text calls `onChange('')` and `onPick?.(null)`;
  - a match calls `onChange(stored(match))` and `onPick?.(match)`;
  - no match keeps today's behaviour (a name field keeps the typed text, an id field clears)
    and calls `onPick?.(null)`.
- `textFor(held)` for a name field with `onPick` shows the held name as typed: there may be
  two candidates, and the email field beside it shows which one.

- [ ] **Step 4: The edit values carry the emails**

In `caseDetailMapping.ts`:
- add `al_adviseremail: string;` and `al_paraplanneremail: string;` to `CaseEditValues`;
- in `edit:`, add `al_adviseremail: text(record.al_adviseremail) ?? '',` and
  `al_paraplanneremail: text(record.al_paraplanneremail) ?? '',`.

- [ ] **Step 5: The panel shows each email, fills it on a pick, and sends both together**

In `CaseEditPanel.tsx`:
- **Field kinds.** Change `FieldKind` to include `'email'`. Add
  `emailAttr?: keyof CaseEditValues` to `FieldDef`.
- **Section.** The "Adviser and paraplanner" section becomes:

```ts
    fields: [
      { attr: 'al_advisername', label: 'Adviser', kind: 'user', emailAttr: 'al_adviseremail' },
      { attr: 'al_adviseremail', label: 'Adviser email', kind: 'email' },
      { attr: 'al_adviserstatus', label: 'Adviser status', kind: 'choice', options: Al_outcomecasesal_adviserstatus },
      { attr: 'al_paraplanner', label: 'Paraplanner', kind: 'user', emailAttr: 'al_paraplanneremail' },
      { attr: 'al_paraplanneremail', label: 'Paraplanner email', kind: 'email' },
    ],
```

- **`user` render branch.** Pass:

```tsx
            onPick={(user) => {
              if (user && field.emailAttr) setField(field.emailAttr, 'email', user.email);
            }}
```

  A pick fills the email. A typed name that matches nobody leaves the email as it was, for
  the person to type.
- **Text input branch.** Use `type={field.kind === 'date' ? 'date' : field.kind === 'email' ? 'email' : 'text'}`.
- **`changedFields`.** End with
  `return withPersonPairs(changed, form as unknown as PersonForm);`.
- **`onSubmit`.** After `const found = validateSubmit(...)`, use:

```ts
    const refusals = [...found, ...personRefusals(changed, form as unknown as PersonForm)];
    setErrors(refusals);
    if (refusals.length > 0) return;
```

  This replaces the existing `setErrors(found); if (found.length > 0) return;`.

- [ ] **Step 6: The unmatched notice talks about the email**

In `CaseDetailPage.tsx`, the notice becomes:

```tsx
                        Adviser not matched: no single active person holds the email{' '}
                        {state.detail.edit.al_adviseremail || '(none recorded)'}, so no adviser
                        can see this case yet. Correct the adviser&apos;s email on the case, or
                        add or correct the person on the People page.
```

Update the doc of `adviserUnmatched` in `allocationScope.ts`: "the adviser email matched no
single active contact".

- [ ] **Step 7: Run everything**

Run: `cd app; npx vitest run; npx tsc -b`

Expected: all pass; `tsc -b` prints nothing. Fix any test that builds a `CaseEditValues`
literal by adding the two email fields as `''`.

- [ ] **Step 8: Commit**

```bash
git add app/src/components/form/UserPicker.tsx app/src/features/cases
git commit -m "feat(app): editing a case person writes their email with their name

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Workloads and filters are keyed by email

**Files:**
- Modify: `app/src/features/cases/caseWorklistMapping.ts` (`CaseSummary` 40-70, `toSummary` 151-175)
- Modify: `app/src/features/people/peopleDirectory.ts` (positions, keys, `buildDirectory`, `casesForPerson`, `caseloadByName`, renamed to `caseloadByIdentity`)
- Modify: `app/src/features/people/PeoplePage.tsx` (`drillTo` 70-72, `loads` 108-111, `rows` 133-174)
- Modify: `app/src/features/people/PersonCasesPage.tsx` (params, `casesForPerson`, the worklist link)
- Modify: `app/src/app/router.tsx` (line 96: `/people/:role/:name` becomes `/people/:role/:key`)
- Modify: `app/src/features/cases/worklistFilters.ts` (`matchesPerson`, the `adviser` filter, `applyFilters`)
- Modify: `app/src/features/cases/CaseWorklistPage.tsx` (the `applyFilters` call 96, the adviser link 324-327, the scope note 246-249)
- Modify: `app/src/features/dashboard/reportFilters.ts` (the adviser filter at 40; add `adviserOptions`)
- Modify: `app/src/features/dashboard/ReportFilterBar.tsx` (the adviser options, 23 and 66-75)
- Test: `peopleDirectory.test.ts`, `worklistFilters.test.ts`, `reportFilters.test.ts` (add cases; fix fixtures)

**Interfaces:**
- Consumes: `useUserDirectory()` (`app/src/hooks/useUserDirectory.ts`), whose users carry
  `id` and `email`.
- Produces:
  - `CaseSummary` gains `adviserEmail: string | null`, `paraplannerEmail: string | null`,
    `taxCheckerId: string | null` and `aqsCheckerId: string | null`.
  - `type ContactEmails = ReadonlyMap<string, string>`: contact id (lower-case) to email
    (lower-case).
  - `contactEmailsOf(users: { id: string; email: string }[]): ContactEmails`
  - `identityOf(position, contactEmails): string`. Values are `email:<addr>`,
    `contact:<id>` or `name:<name>`, all lower-case.
  - `adviserIdentity(item: CaseSummary): string`
  - `caseloadByIdentity(cases, contactEmails?): Map<string, PersonCaseload>`.
    `PersonCaseload` gains `identity: string`.
  - `casesForPerson(cases, role, identity, contactEmails?)`
  - `applyFilters(cases, filters, contactEmails?)`
  - `adviserOptions(cases): { value: string; label: string }[]`

- [ ] **Step 1: Write the failing tests**

Add to `peopleDirectory.test.ts`. Extend that file's existing `CaseSummary` fixture helper
with the four new fields; call it `summary(...)` below.

```ts
describe('people are keyed by email (two people, one name)', () => {
  const a = summary({ id: '1', adviser: 'Adam Smith', adviserEmail: 'adam.smith@example.com' });
  const b = summary({ id: '2', adviser: 'Adam Smith', adviserEmail: 'adam.smith2@example.com' });

  it('keeps two advisers of one name apart', () => {
    const loads = caseloadByIdentity([a, b]);
    expect(loads.get('email:adam.smith@example.com')?.totalCases).toBe(1);
    expect(loads.get('email:adam.smith2@example.com')?.totalCases).toBe(1);
  });

  it('joins a checker to the same person through the directory email', () => {
    const c = summary({ id: '3', adviser: null, adviserEmail: null, taxChecker: 'Adam Smith', taxCheckerId: 'C1' });
    const loads = caseloadByIdentity([a, c], contactEmailsOf([{ id: 'c1', email: 'Adam.Smith@example.com' }]));
    const load = loads.get('email:adam.smith@example.com');
    expect(load?.totalCases).toBe(2);
    expect(load?.roles).toEqual(['Adviser', 'Checker']);
  });

  it('falls back to the name only for a legacy case with no email', () => {
    const legacy = summary({ id: '4', adviser: 'Old Adviser', adviserEmail: null });
    expect(caseloadByIdentity([legacy]).has('name:old adviser')).toBe(true);
  });

  it('lists one adviser identity\'s cases', () => {
    expect(casesForPerson([a, b], 'Adviser', 'email:adam.smith2@example.com').map((c) => c.id)).toEqual(['2']);
  });
});
```

Add to `worklistFilters.test.ts`:

```ts
it('filters by adviser email, so two advisers of one name stay apart', () => {
  const cases = [
    summary({ id: '1', adviser: 'Adam Smith', adviserEmail: 'adam.smith@example.com' }),
    summary({ id: '2', adviser: 'Adam Smith', adviserEmail: 'adam.smith2@example.com' }),
  ];
  expect(applyFilters(cases, { ...EMPTY_FILTERS, adviser: 'ADAM.SMITH2@example.com' }).map((c) => c.id)).toEqual(['2']);
  expect(applyFilters(cases, { ...EMPTY_FILTERS, person: 'email:adam.smith@example.com' }).map((c) => c.id)).toEqual(['1']);
});
```

Add to `reportFilters.test.ts`:

```ts
it('offers advisers by email, labelled with name and email', () => {
  const cases = [
    summary({ id: '1', adviser: 'Adam Smith', adviserEmail: 'adam.smith@example.com' }),
    summary({ id: '2', adviser: 'Adam Smith', adviserEmail: 'adam.smith2@example.com' }),
  ];
  expect(adviserOptions(cases)).toEqual([
    { value: 'adam.smith@example.com', label: 'Adam Smith (adam.smith@example.com)' },
    { value: 'adam.smith2@example.com', label: 'Adam Smith (adam.smith2@example.com)' },
  ]);
});
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd app; npx vitest run src/features/people src/features/cases/worklistFilters.test.ts src/features/dashboard/reportFilters.test.ts`

Expected: FAIL. `caseloadByIdentity`, `contactEmailsOf` and `adviserOptions` are missing, and
the summaries lack the email fields.

- [ ] **Step 3: `CaseSummary` carries the identities**

In `caseWorklistMapping.ts`, add to `CaseSummary`, beside `adviser`, `paraplanner` and the
checkers:

```ts
  /** The adviser's email: who they are. `adviser` is only the label. */
  adviserEmail: string | null;
  paraplannerEmail: string | null;
  /** The checkers' contact ids, from the access lookups. */
  taxCheckerId: string | null;
  aqsCheckerId: string | null;
```

and to `toSummary`:

```ts
    adviserEmail: record.al_adviseremail ?? null,
    paraplannerEmail: record.al_paraplanneremail ?? null,
    taxCheckerId: record._al_taxcheckercontactid_value ?? null,
    aqsCheckerId: record._al_aqscheckercontactid_value ?? null,
```

- [ ] **Step 4: `peopleDirectory.ts` keys by identity**

- **Positions.** Give each position `email` and `contactId`:
  - Adviser: `item.adviserEmail` and `null`.
  - Paraplanner: `item.paraplannerEmail` and `null`.
  - Checkers: `null`, with `item.taxCheckerId` / `item.aqsCheckerId`. In `checkers()`,
    de-duplicate by contact id when present, by name otherwise.
  - Owner: `null` and `null`. The owner is a Dataverse user, not a case person, and stays
    keyed by name.
- **Add:**

```ts
export type ContactEmails = ReadonlyMap<string, string>;

export function contactEmailsOf(users: { id: string; email: string }[]): ContactEmails {
  return new Map(
    users
      .filter((u) => u.email.trim() !== '')
      .map((u) => [u.id.toLowerCase(), u.email.trim().toLowerCase()] as const),
  );
}

/**
 * Who a position is. The email wherever it is known - stored on the case for the adviser
 * and paraplanner, through the directory for a checker - so two people of one name stay two
 * people and one person in two positions stays one. A name only for a legacy row with nothing
 * better, marked as such so it never merges with an email-keyed person.
 */
export function identityOf(
  position: { name: string | null; email: string | null; contactId: string | null },
  contactEmails: ContactEmails = new Map(),
): string {
  const email = position.email?.trim().toLowerCase();
  if (email) return `email:${email}`;
  const id = position.contactId?.trim().toLowerCase();
  if (id) {
    const viaDirectory = contactEmails.get(id);
    return viaDirectory ? `email:${viaDirectory}` : `contact:${id}`;
  }
  return `name:${(position.name ?? '').trim().toLowerCase()}`;
}

export function adviserIdentity(item: CaseSummary): string {
  return identityOf({ name: item.adviser, email: item.adviserEmail, contactId: null });
}
```

- **Rewrite the three functions by key.**
  - `personKey(role, identity)` returns `` `${role}:${identity}` ``.
  - `buildDirectory(cases, contactEmails = new Map())` uses `identityOf` for the key.
  - `casesForPerson(cases, role, identity, contactEmails = new Map())` matches
    `position.role === role && identityOf(position, contactEmails) === identity.toLowerCase()`.
  - Rename `caseloadByName` to `caseloadByIdentity(cases, contactEmails = new Map())`. It
    keys `loads` and `seen` by `identityOf(position, contactEmails)` and sets
    `identity: key` on each `PersonCaseload` (add `identity: string` to the interface).
- Skip a position whose identity is `name:` with an empty name, as today's code skips a
  blank name.
- Update the module doc at lines 5-12, which says the case carries no email: "Adviser and
  paraplanner are keyed by the email the case stores; checkers by their contact, through the
  directory's email."

- [ ] **Step 5: Wire the pages**

- **`PeoplePage.tsx`**:
  - `const contactEmails = useMemo(() => contactEmailsOf(directory.status === 'ready' ? directory.users : []), [directory]);`
  - `loads = caseloadByIdentity(cases..., contactEmails)`.
  - In `registered`, key each user by `const key = \`email:${user.email.trim().toLowerCase()}\`;`,
    with the same `claimed.add(key)` and `loads.get(key)`.
  - `drillTo` returns `` `/people/${role}/${encodeURIComponent(load!.identity)}` ``.
- **`router.tsx` line 96.** The path becomes `/people/:role/:key`.
- **`PersonCasesPage.tsx`**:
  - `const { role, key = '' } = useParams<{ role: string; key: string }>();`
  - read `useUserDirectory()`;
  - `const contactEmails = useMemo(...)` as above;
  - `casesForPerson(state.cases, personRole, key, contactEmails)`.
  - The page shows the person's display name. Compute it as the `name` of the first matching
    position, or the part after `email:` when there is none. Replace every use of `name` in
    the page text with it.
  - The worklist link becomes `` `/cases?person=${encodeURIComponent(key)}` ``.
- **`worklistFilters.ts`**:
  - `applyFilters(cases, filters, contactEmails: ContactEmails = new Map())`.
  - `matchesPerson(item, person, contactEmails)` returns true when any position's
    `identityOf(position, contactEmails)` equals `person.trim().toLowerCase()`. Export
    `positions` from `peopleDirectory.ts` to reuse it.
  - The adviser filter becomes
    `(item.adviserEmail ?? '').trim().toLowerCase() !== filters.adviser.trim().toLowerCase()`.
- **`CaseWorklistPage.tsx`**:
  - read `useUserDirectory()`;
  - pass `contactEmailsOf(...)` to `applyFilters`;
  - the adviser link becomes `` `/people/Adviser/${encodeURIComponent(adviserIdentity(item))}` ``;
  - the scope note shows the adviser name of the first matching case when one is found, else
    the email.
- **`reportFilters.ts`**:
  - the adviser test becomes
    `(item.adviserEmail ?? '').trim().toLowerCase() !== filters.adviser.trim().toLowerCase()`;
  - add:

```ts
export function adviserOptions(cases: CaseSummary[]): { value: string; label: string }[] {
  const byEmail = new Map<string, string>();
  for (const item of cases) {
    const email = item.adviserEmail?.trim().toLowerCase();
    if (email && !byEmail.has(email)) byEmail.set(email, item.adviser?.trim() || email);
  }
  return [...byEmail.entries()]
    .map(([value, name]) => ({ value, label: name === value ? value : `${name} (${value})` }))
    .sort((a, b) => a.label.localeCompare(b.label));
}
```

- **`ReportFilterBar.tsx`.** Use `adviserOptions(cases)` for the adviser `<select>`:
  `value={option.value}`, with `option.label` as the text.

- [ ] **Step 6: Run everything**

Run: `cd app; npx vitest run; npx tsc -b`

Expected: all pass, and `tsc -b` prints nothing. Every `CaseSummary` literal the compiler names
(`caseExport.test.ts`, `worklistFilters.test.ts`, `reportFilters.test.ts`,
`peopleDirectory.test.ts`) gets the four new fields as `null` in its fixture helper.

- [ ] **Step 7: Commit**

```bash
git add app/src
git commit -m "feat(app): workloads and filters keep two people of one name apart, by email

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: The portal header sends name and email together

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`:
  - the case fetch (~144-148);
  - the header rows (620 and 633);
  - `changedFields()` (~4625-4646).
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html` (the unassigned hint, ~783)
- Test: `app/src/features/reviews/portalPersonEmails.test.ts`

**Interfaces:**
- Consumes: `al_CaseHeaderRequest` accepting `al_adviseremail` and `al_paraplanneremail`
  (Task 2).

- [ ] **Step 1: Write the failing test**

```ts
import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';

/**
 * The portal's case header names each person by name AND email (owner, 2026-10-02: two people
 * can share a name). Read as text: the template is Liquid nothing here compiles.
 */
describe('the portal case header people', () => {
  it('reads both emails with the case', () => {
    expect(reviewTemplate).toContain('<attribute name="al_adviseremail" />');
    expect(reviewTemplate).toContain('<attribute name="al_paraplanneremail" />');
  });

  it('offers an email box beside each name', () => {
    expect(reviewTemplate).toContain('data-ot-hdr="al_adviseremail"');
    expect(reviewTemplate).toContain('data-ot-hdr="al_paraplanneremail"');
    expect(reviewTemplate).toContain('type="email" class="ot-hdr-input" data-ot-hdr="al_adviseremail"');
  });

  it('sends a person\'s name and email together when either changed', () => {
    expect(reviewTemplate).toContain("var PERSON_PAIRS = [['al_advisername', 'al_adviseremail'], ['al_paraplanner', 'al_paraplanneremail']];");
  });

  it('explains an unassigned action by the email, not the name', () => {
    expect(remediation).toContain('is held by no portal contact');
    expect(remediation).not.toContain('is not a portal contact');
  });
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd app; npx vitest run src/features/reviews/portalPersonEmails.test.ts`

Expected: all 4 FAIL.

- [ ] **Step 3: Edit the review template**

- **Fetch.** After `<attribute name="al_advisername" />` (~144), add
  `<attribute name="al_adviseremail" />`. After `<attribute name="al_paraplanner" />`
  (~147), add `<attribute name="al_paraplanneremail" />`.
- **Rows.** After the Adviser name row (620-622), add:

```html
          <tr>
            <td class="lbl">Adviser email</td><td class="val"><input type="email" class="ot-hdr-input" data-ot-hdr="al_adviseremail" value="{{ rv['case.al_adviseremail'] | escape }}" aria-label="Adviser email" /></td>
            <td class="lbl">Paraplanner email</td><td class="val"><input type="email" class="ot-hdr-input" data-ot-hdr="al_paraplanneremail" value="{{ rv['case.al_paraplanneremail'] | escape }}" aria-label="Paraplanner email" /></td>
          </tr>
```

  Use the same `rv['case.…']` alias the neighbouring rows use.
- **`changedFields()`.** Immediately before `return any ? fields : null;`, add:

```js
            // People are identified by email (the server refuses a name sent alone), so a
            // person's name and email travel together whenever either one changed.
            var PERSON_PAIRS = [['al_advisername', 'al_adviseremail'], ['al_paraplanner', 'al_paraplanneremail']];
            for (var p = 0; p < PERSON_PAIRS.length; p++) {
              var pair = PERSON_PAIRS[p];
              if (!(pair[0] in fields) && !(pair[1] in fields)) { continue; }
              for (var q = 0; q < pair.length; q++) {
                var input = document.querySelector('[data-ot-hdr="' + pair[q] + '"]');
                if (input) { fields[pair[q]] = input.value; }
              }
            }
```

- [ ] **Step 4: Edit the remediation hint**

In `OT-Remediation`, replace the `title="…"` text and the visible tail of the unassigned
hint with:

```
title="The adviser email on this case is held by no single portal contact, so nobody can record a response on this action. Correct the adviser's email on the case, or add the person; the open actions then move to them.">Nobody assigned{% if c.al_adviseremail %} &mdash; {{ c.al_adviseremail | escape }} is held by no portal contact{% endif %}
```

`c` comes from the `remcase` fetch, which already reads `al_adviseremail` (~279).

- [ ] **Step 5: Run the portal checks and the suite**

Run: `cd app; npx vitest run`

Expected: all pass, including the existing portal template tests.

Then run `powershell -File powerpages/Check-PortalTemplates.ps1` from the repo root.

Expected: no new failures. The OT AQS Notes Q-GR-03/04 typing failure is a known one,
reported without `-OrgUrl`.

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail powerpages/outcome-testing---outcometesting/web-templates/ot-remediation app/src/features/reviews/portalPersonEmails.test.ts
git commit -m "feat(portal): the case header sends each person's name and email together

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Record the decision, deploy to DEV, backfill and prove

**Files:**
- Modify: `knowledge/decision-log.md` (append AD-228 after the last AD row)
- Create: `docs/deployment/2026-10-04-email-person-identity.md`

**Interfaces:**
- Consumes everything above.

- [ ] **Step 1: Decision log**

Append after the last `| AD-` row, keeping the table's four columns:

```
| AD-228 | **People on a case are identified by email, never by name.** `NotificationOutbox.MatchPerson` resolves by `emailaddress1` only (active, `TopCount 2`); `al_advisername` and `al_paraplanner` are labels. Remediation assignment, adviser access, the pass letter, T&C routing, paraplanner letters and export codes read the stored email. The import rejects a row without AdviserName, AdviserEmail, AssignedBy and ParaplannerEmail; both edit paths refuse a name sent without its email. Open actions follow the adviser email: on an email change, and from `AdviserContactPlugin` on contact Create and Update of emailaddress1/statecode; an email matching nobody unassigns them. `backfillpeopleemail` fills blank emails from unambiguous names once and re-points open actions. | Project owner, 2026-10-02: "This should use email instead of name for matching. The whole system needs to do that as 2 people can have the same name", after PROD case 256497798 named "Adam Strumidlo" while his contact read "Adam Strumdlio" and his remediation was raised unassigned. Owner chose: reject rows without email, one-off backfill with report, email as the only key, manual name + email entry allowed. Spec `docs/superpowers/specs/2026-10-02-email-person-identity-design.md`. Supersedes the name fallback in `CaseAdviser` (2026-09-30) and `MatchPerson` (2026-09-20). | 2026-10-04 |
```

- [ ] **Step 2: Build and push the plug-in assembly to DEV**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet build OutcomeTesting.Plugins -c Release
T=plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.exe
DOTNET_ROLL_FORWARD=Major "$T" pushassembly https://org0b075da8.crm11.dynamics.com 2>&1 | grep -v "^Connecting"
```

Verify by sha256: compare the local Release DLL with `pluginassembly.content` read back from
DEV. Do not trust the byte count; `pushassembly` sends `bin/Release` as it stands.

- [ ] **Step 3: Register the contact step in DEV and add it to the solution**

```bash
DOTNET_ROLL_FORWARD=Major "$T" registerstep https://org0b075da8.crm11.dynamics.com OutcomeTesting.Plugins.AdviserContactPlugin Create contact 40 "" sync
DOTNET_ROLL_FORWARD=Major "$T" registerstep https://org0b075da8.crm11.dynamics.com OutcomeTesting.Plugins.AdviserContactPlugin Update contact 40 "emailaddress1,statecode" sync
```

Then run `addcomponent` (type 92) for both new step ids into `OutcomeTesting`. Confirm with
the solution membership audit that both steps are members and are enabled.

- [ ] **Step 4: Push the two portal templates and the Code App to DEV**

- **Templates.** Before each push, diff DEV's copy against the repo (read
  `powerpagecomponents(<id>)` content). A difference other than this change means DEV holds
  newer work: stop and report it. Then:

```bash
DOTNET_ROLL_FORWARD=Major "$T" pushwebtemplate https://org0b075da8.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html
```

  Push OT Remediation the same way. Its component id is the one
  `powerpages/outcome-testing---outcometesting/.portalconfig` maps to `ot-remediation`.
- **Code App.** Run `cd app; npm run build`. Confirm the new bundle contains
  `al_adviseremail`. Then run `npx pa app push`. If `pa` needs an interactive sign-in, hand the
  command to the owner. Open the `/app/` URL with `sourcetime` that the push prints.

- [ ] **Step 5: Backfill DEV**

```bash
DOTNET_ROLL_FORWARD=Major "$T" backfillpeopleemail https://org0b075da8.crm11.dynamics.com
```

Read the report and record the FILL, NO FILL and MISMATCH counts. Then:

```bash
DOTNET_ROLL_FORWARD=Major "$T" backfillpeopleemail https://org0b075da8.crm11.dynamics.com --confirm https://org0b075da8.crm11.dynamics.com
DOTNET_ROLL_FORWARD=Major "$T" reconcileaccess https://org0b075da8.crm11.dynamics.com --confirm https://org0b075da8.crm11.dynamics.com
```

A `--confirm` command can be refused by the permission gate. If so, hand it to the owner in
PowerShell form.

- [ ] **Step 6: DEV proof**

Each item must be observed in DEV, not inferred:
1. **Import.** Upload a two-row extract in the Code App: one row complete, one row with
   `AdviserEmail` blank. Expected: one case created, and one row rejected with the
   "AdviserEmail is empty" reason.
2. **Misspelled name.** On a DEV case at Awaiting Remediation, set the adviser to
   `Adam Strumidlo` with the email of a DEV contact whose fullname is spelled differently.
   Expected: the open action's `al_assignedcontactid` is that contact.
3. **Contact appears later.** Set a case's adviser email to an address no contact holds.
   Expected: the action is unassigned. Then create a contact with that email. Expected: the
   action is assigned to the new contact without editing the case.
4. **Two people of one name.** Two contacts named the same with different emails each receive
   only their own case's action. The People page shows them as two rows.
5. **Portal.** On the portal review page, change the adviser email. Expected: the header save
   succeeds and the audit line reads "Re-pointed …".

Clean up anything the proof created that is not ordinary DEV test data.

- [ ] **Step 7: Deployment note, then hand TEST and PROD to the owner**

Write `docs/deployment/2026-10-04-email-person-identity.md` in the style of the other
deployment notes. Include:
- what changed;
- the DEV evidence from Step 6;
- the backfill counts;
- the owner's steps for TEST and PROD, as PowerShell:
  1. the managed export from DEV (PROD through `brandpackage <zip> <out.zip> OTIS`) and the
     import, with full paths;
  2. read back the solution version after the import;
  3. confirm the two `AdviserContactPlugin` steps arrived **enabled**. An import with step XML
     and no `--activate-plugins` disables them;
  4. `backfillpeopleemail <org>`, then `--confirm <org>`, then `reconcileaccess --confirm`;
  5. in PROD, check that case 256497798's open action is now held by Adam Strumidlo's contact.
     Remind the owner that he still needs the **AL Portal - Adviser Remediation** web role.

- [ ] **Step 8: Commit**

```bash
git add knowledge/decision-log.md docs/deployment/2026-10-04-email-person-identity.md
git commit -m "docs: AD-228 email identity, DEV deployment and proof

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Run `graphify update .` from the repo root afterwards.
