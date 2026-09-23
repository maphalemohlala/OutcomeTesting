# Role-Scoped Access Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Each person sees only their own work. Tax specialists see only cases allocated to them. AQS reviewers see the AQS queue and their own cases. Advisers and T&C Supervisors see a case only once it is released for remediation. The two new team managers see only their team's cases in the Code App.

**Architecture:** Six access columns on `al_outcomecase` are kept correct by one reconciler (`CaseAccessReconciler`, which applies the pure `CaseAccess` rule). The reconciler runs as SYSTEM from synchronous post-operation steps on case, review and remedial-action writes. Portal table permissions read cases through those columns (Contact scope, plus Account scope for the AQS queue), and child permissions pass access down to reviews, answers, outcomes, remedial actions and sign-offs. In the Code App, cases are shared with two Dataverse owner teams, with cascade to their children. The two manager web roles are mapped to a narrow security role. Allocation is checked on the server by role.

**Tech Stack:** C# net462 plug-ins (xunit, `FakeOrganizationService`), .NET 8 registration console, Power Pages Liquid and YAML (Enhanced data model), React/TypeScript Code App (vitest, `tsc -b`), Playwright portal e2e.

**Spec:** `docs/superpowers/specs/2026-09-23-role-scoped-access-design.md`. Read the "Amendments made while planning" section. It overrides earlier sections where they conflict.

## Global Constraints

- Every environment write targets `Env_AQ_Dev` only. TEST and PROD promotion is the project owner's call, each time.
- Plug-in tests: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests` (PowerShell form).
- Run `dotnet build plugins/OutcomeTesting.Plugins -c Release` immediately before `pushassembly`, and check the pushed byte count matches the Release dll.
- Run `npm run build` in `app/` immediately before `pa app push`. Type-check with `npx tsc -b`, never `tsc --noEmit`.
- Portal: never do a whole-tree `pac pages upload`. Use `pushwebtemplate` for templates and `restoretablepermissions` for permissions. Before pushing a template, download the live copy and diff it against the branch base.
- New portal component ids come from the band `a1000000-0000-4000-8000-0000000000c0` to `...df`. Run `powershell -File powerpages/Check-ComponentIds.ps1` after adding any.
- Hard-code no environment id, email, URL or person's name in source. Teams, the account and web roles are looked up by name.
- Plug-in C# (`OutcomeTesting.Plugins`, net462) must compile as C# 7.3: no `is not`, no target-typed `new()`, no `??=`, no records. The registration tool is net8.0 and may use current C#.
- Plug-in failures use the existing prefixes: `CommandHelpers.PreconditionPrefix`, `ValidationPrefix`, `UnauthorizedPrefix`.
- Commit messages end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- After changing code, run `graphify update .` from the repo root.
- Match the surrounding comment style: comments say *why*, cite the decision or requirement id, and use British spelling.

## Review Focus

1. **Reallocation.** A case moved from checker A to checker B must leave A with no portal read at all. Test: `CaseAccessTests.Reallocation_replaces_the_checker` (Task 2).
2. **A Tax fail with AQS still owed.** Such a case can already have remedial actions and a remediation status while the AQS review is open, and the adviser must not see it. Test: `CaseAccessTests.An_open_review_holds_release_back` (Task 2).
3. **An adviser name that matches two contacts.** The case is not released to either, and the backfill reports it. Test: `CaseAccessReconcilerTests.An_ambiguous_adviser_is_reported_not_guessed` (Task 3).
4. **A route changed while queued**, for example Tax-then-AQS edited to AQS only. The queue and team shares must recompute, and the Tax team keeps the case if a Tax review exists. Test: `CaseAccessTests.A_tax_review_keeps_the_tax_team_after_a_route_change` (Task 2).
5. **Running the reconciler twice.** The second run writes nothing, so the backfill and step re-entry are safe. Test: `CaseAccessReconcilerTests.A_second_run_writes_nothing` (Task 3).

---

### Task 1: Record the decisions

**Files:**
- Modify: `knowledge/decision-log.md` (append AD-218 after AD-217 at the end of the table)
- Modify: `knowledge/requirements-index.md` (new section after the MR table, before "Traceability matrix columns")

**Interfaces:** none. Documentation only.

- [ ] **Step 1: Append AD-218 to the decision log**

Add one row at the end of the AD table:

```markdown
| AD-218 | **Each person sees their own work: role-scoped access replaces OD-022's Global read.** Six access columns on `al_outcomecase` (`al_taxcheckercontactid`, `al_aqscheckercontactid`, `al_aqsqueueaccountid`, `al_aqsqueuedon`, `al_advisercontactid`, `al_tcsupervisorcontactid`) are kept correct by `CaseAccessReconciler`, run as SYSTEM from synchronous post-operation steps on case, review and remedial-action writes. Portal case reads go through those columns (Contact scope; Account scope for the AQS queue); reviews, answers, outcomes, remedial actions and sign-offs are children of the case. In the Code App, cases are shared with the "Outcome Testing - Tax Team" and "Outcome Testing - AQS Team" owner teams with Share and Reparent cascade, and two new web roles, `AL Portal - Tax Team Manager` and `AL Portal - AQS Team Manager`, allocate their own discipline only. New portal permission ids use the band `c0`-`df` (amends AD-059). | Project owner, 2026-09-23, supplying `docs/reference/2026-09-23-access-requirements.md` (AR-01 to AR-04) and answering eight questions recorded in `docs/superpowers/specs/2026-09-23-role-scoped-access-design.md`. **Supersedes OD-022 and AD-056** (every signed-in user reading every case) and AD-083's organisation-wide Home counts; **amends AD-135** ("Depth is Global throughout") for the manager role; **closes OD-029(c)**. A case is released to its adviser only once it has a remedial action, is at Awaiting Remediation or later, and owes no unsubmitted review; Pass cases are never released. Planners keep their emails and lose all system access. Roles and permissions are remapped by the project owner, not by a command (AD-144 forbids an application command conferring platform privileges). **Residual, accepted until that remap:** anyone holding `Outcome Testing App User` still reads every case in the Code App, and checkers must hold it to be allocated work (AD-144). | 2026-09-23 |
```

- [ ] **Step 2: Add the AR band to the requirements index**

After the MR table and its two-line follow-up paragraph, insert:

```markdown
## 23 September 2026 access requirements

Source: `docs/reference/2026-09-23-access-requirements.md`, supplied by the project owner
2026-09-23. Design: `docs/superpowers/specs/2026-09-23-role-scoped-access-design.md` (AD-218).

| ID | Pack item | Traces to | State 2026-09-23 |
|---|---|---|---|
| AR-01 | Tax Team Manager: see, allocate and reallocate all Tax work; monitor workloads | BR-003, FR-004 to FR-006, AD-040, AD-218 | Designed; build plan `docs/superpowers/plans/2026-09-23-role-scoped-access.md` |
| AR-02 | Tax Specialist sees only cases allocated to them; cannot allocate | BR-012, PP-02, NFR-SEC-01, AD-218 | Designed. Code App half depends on the project owner's role remap (AD-218 residual) |
| AR-03 | AQS reviewer sees the AQS queue and their own cases; five labelled groups; Tax-reviewed cases first with age shown | BR-003, BR-004, AD-076, AD-113, AD-218 | Designed |
| AR-04 | Adviser sees only their own cases, only once released for remediation | BR-006, BR-008, BR-012, PP-12, AD-218 | Designed |
```

- [ ] **Step 3: Commit**

```bash
git add knowledge/decision-log.md knowledge/requirements-index.md docs/superpowers/specs/2026-09-23-role-scoped-access-design.md
git commit -m "docs(decisions): AD-218 role-scoped access, and the AR-01..AR-04 band

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: The `CaseAccess` rule

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/CaseAccess.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/CaseAccessTests.cs`

**Interfaces:**
- Consumes: `CaseLifecycle` status constants, `ResponseRules.ReviewTypeTax` (120910200), `ResponseRules.ReviewTypeAqs` (120910201).
- Produces:
  - `sealed class ReviewFact { int ReviewType; Guid? AssignedContactId; bool Submitted; bool Active = true; int Sequence; }`
  - `sealed class CaseAccessInput { int? CaseStatus; bool RouteRequiresTax; bool RouteRequiresAqs; IList<ReviewFact> Reviews; bool HasRemediation; EntityReference ResolvedAdviser; EntityReference ResolvedSupervisor; EntityReference AqsQueueAccount; DateTime? CurrentQueuedOn; DateTime Now; }`
  - `sealed class CaseAccessResult { EntityReference TaxChecker; EntityReference AqsChecker; EntityReference QueueAccount; DateTime? QueuedOn; EntityReference Adviser; EntityReference Supervisor; bool ShareWithTaxTeam; bool ShareWithAqsTeam; }`
  - `static CaseAccessResult CaseAccess.Decide(CaseAccessInput input)`
  - `static bool CaseAccess.InAqsQueue(CaseAccessInput input)`
  - `static bool CaseAccess.IsReleased(CaseAccessInput input)`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Who may see a case, decided from what the case is (AD-218, AR-01 to AR-04). Pure, so
    /// every combination the portal permissions depend on is pinned here rather than found
    /// in DEV.
    /// </summary>
    public class CaseAccessTests
    {
        private static readonly Guid Ada = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
        private static readonly Guid Bo = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000002");
        private static readonly EntityReference Queue = new EntityReference("account", Guid.Parse("bbbbbbbb-0000-4000-8000-000000000001"));
        private static readonly EntityReference Adviser = new EntityReference("contact", Guid.Parse("cccccccc-0000-4000-8000-000000000001"));
        private static readonly EntityReference Supervisor = new EntityReference("contact", Guid.Parse("cccccccc-0000-4000-8000-000000000002"));
        private static readonly DateTime Now = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

        private static ReviewFact Tax(Guid? contact, bool submitted = false, int sequence = 1)
        {
            return new ReviewFact { ReviewType = ResponseRules.ReviewTypeTax, AssignedContactId = contact, Submitted = submitted, Sequence = sequence };
        }

        private static ReviewFact Aqs(Guid? contact, bool submitted = false, int sequence = 2)
        {
            return new ReviewFact { ReviewType = ResponseRules.ReviewTypeAqs, AssignedContactId = contact, Submitted = submitted, Sequence = sequence };
        }

        private static CaseAccessInput Case(int status, bool tax, bool aqs, params ReviewFact[] reviews)
        {
            return new CaseAccessInput
            {
                CaseStatus = status,
                RouteRequiresTax = tax,
                RouteRequiresAqs = aqs,
                Reviews = new List<ReviewFact>(reviews),
                AqsQueueAccount = Queue,
                ResolvedAdviser = Adviser,
                ResolvedSupervisor = Supervisor,
                Now = Now,
            };
        }

        [Fact]
        public void The_tax_checker_is_the_contact_on_the_tax_review()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Assigned, true, true, Tax(Ada)));
            Assert.Equal(Ada, result.TaxChecker.Id);
            Assert.Null(result.AqsChecker);
        }

        [Fact]
        public void Reallocation_replaces_the_checker()
        {
            // The review row carries one contact; reallocating changes it, and the case must
            // follow, so the previous checker's Contact-scoped read goes with it (AR-02).
            var result = CaseAccess.Decide(Case(CaseLifecycle.Assigned, true, false, Tax(Bo)));
            Assert.Equal(Bo, result.TaxChecker.Id);
        }

        [Fact]
        public void An_inactive_review_grants_nothing()
        {
            var review = Tax(Ada);
            review.Active = false;
            var result = CaseAccess.Decide(Case(CaseLifecycle.Assigned, true, false, review));
            Assert.Null(result.TaxChecker);
        }

        [Fact]
        public void An_aqs_only_case_waiting_to_be_taken_is_on_the_queue()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true));
            Assert.Equal(Queue.Id, result.QueueAccount.Id);
            Assert.Equal(Now, result.QueuedOn);
        }

        [Fact]
        public void A_tax_then_aqs_case_is_not_on_the_queue_before_its_tax_check()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, true, true));
            Assert.Null(result.QueueAccount);
            Assert.Null(result.QueuedOn);
        }

        [Fact]
        public void A_tax_then_aqs_case_joins_the_queue_once_its_tax_check_is_submitted()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, true, true, Tax(Ada, submitted: true)));
            Assert.NotNull(result.QueueAccount);
        }

        [Fact]
        public void The_queue_keeps_the_time_the_case_joined_it()
        {
            var input = Case(CaseLifecycle.Queued, false, true);
            input.CurrentQueuedOn = Now.AddDays(-3);
            Assert.Equal(Now.AddDays(-3), CaseAccess.Decide(input).QueuedOn);
        }

        [Fact]
        public void A_case_leaves_the_queue_when_its_aqs_review_is_taken()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true, Aqs(Ada)));
            Assert.Null(result.QueueAccount);
            Assert.Null(result.QueuedOn);
            Assert.Equal(Ada, result.AqsChecker.Id);
        }

        [Fact]
        public void A_case_not_at_queued_is_not_on_the_queue()
        {
            Assert.Null(CaseAccess.Decide(Case(CaseLifecycle.Assigned, false, true)).QueueAccount);
        }

        [Fact]
        public void A_pass_case_is_never_released()
        {
            // Closed is in the release range; a Pass case reaches it with no remediation.
            var input = Case(CaseLifecycle.Closed, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = false;
            var result = CaseAccess.Decide(input);
            Assert.Null(result.Adviser);
            Assert.Null(result.Supervisor);
        }

        [Fact]
        public void A_remediation_case_is_released_to_its_adviser_and_supervisor()
        {
            var input = Case(CaseLifecycle.AwaitingRemediation, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = true;
            var result = CaseAccess.Decide(input);
            Assert.Equal(Adviser.Id, result.Adviser.Id);
            Assert.Equal(Supervisor.Id, result.Supervisor.Id);
        }

        [Fact]
        public void The_adviser_keeps_the_case_after_it_closes()
        {
            var input = Case(CaseLifecycle.Closed, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = true;
            Assert.NotNull(CaseAccess.Decide(input).Adviser);
        }

        [Fact]
        public void An_open_review_holds_release_back()
        {
            // A Tax fail on a Tax-then-AQS case can carry actions while AQS is still owed.
            // AR-04: an adviser never sees a case whose review is still in progress.
            var input = Case(CaseLifecycle.AwaitingRemediation, true, true, Tax(Ada, submitted: true), Aqs(Bo));
            input.HasRemediation = true;
            Assert.Null(CaseAccess.Decide(input).Adviser);
        }

        [Fact]
        public void An_unmatched_adviser_releases_to_nobody()
        {
            var input = Case(CaseLifecycle.AwaitingRemediation, false, true, Aqs(Ada, submitted: true));
            input.HasRemediation = true;
            input.ResolvedAdviser = null;
            Assert.Null(CaseAccess.Decide(input).Adviser);
        }

        [Fact]
        public void The_teams_follow_the_route()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, true, true));
            Assert.True(result.ShareWithTaxTeam);
            Assert.True(result.ShareWithAqsTeam);

            var aqsOnly = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true));
            Assert.False(aqsOnly.ShareWithTaxTeam);
            Assert.True(aqsOnly.ShareWithAqsTeam);
        }

        [Fact]
        public void A_tax_review_keeps_the_tax_team_after_a_route_change()
        {
            // Both managers see a Tax-then-AQS case for its whole life (answer 4c); a route
            // later edited to AQS only still carries a Tax review the Tax team did.
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, true, Tax(Ada, submitted: true)));
            Assert.True(result.ShareWithTaxTeam);
        }

        [Fact]
        public void A_case_with_no_route_shares_with_no_team_and_joins_no_queue()
        {
            var result = CaseAccess.Decide(Case(CaseLifecycle.Queued, false, false));
            Assert.False(result.ShareWithTaxTeam);
            Assert.False(result.ShareWithAqsTeam);
            Assert.Null(result.QueueAccount);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CaseAccessTests`
Expected: build error. `CaseAccess`, `ReviewFact` and `CaseAccessInput` do not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who may see a case, decided from what the case is (AD-218, AR-01 to AR-04).
    ///
    /// Power Pages can narrow a read only through a lookup to the signed-in contact, a lookup
    /// to their account, or a parent chain. So "who may see this case" has to be written ONTO
    /// the case, and this is the one place that decides what is written. The reconciler reads
    /// the case, asks this, and writes the difference.
    ///
    /// Pure, with no service: every rule the portal permissions rest on is a unit test.
    /// </summary>
    public static class CaseAccess
    {
        /// <summary>
        /// The statuses at which a case with remediation is released to its adviser. Closed is
        /// included because the adviser keeps a case after sign-off (answer 2a); a Pass case
        /// reaches Closed too, which is why release also requires a remedial action.
        /// </summary>
        private static readonly int[] ReleaseStatuses =
        {
            CaseLifecycle.AwaitingRemediation,
            CaseLifecycle.RemediationInProgress,
            CaseLifecycle.AwaitingSignoff,
            CaseLifecycle.AwaitingRecheck,
            CaseLifecycle.Closed,
        };

        public static CaseAccessResult Decide(CaseAccessInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var reviews = Active(input);

            var result = new CaseAccessResult
            {
                TaxChecker = CheckerFor(reviews, ResponseRules.ReviewTypeTax),
                AqsChecker = CheckerFor(reviews, ResponseRules.ReviewTypeAqs),

                // Answer 4c: both managers see a Tax-then-AQS case for its whole life, so a
                // team keeps a case its discipline has ever worked, whatever the route says now.
                ShareWithTaxTeam = input.RouteRequiresTax || reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeTax),
                ShareWithAqsTeam = input.RouteRequiresAqs || reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeAqs),
            };

            if (InAqsQueue(input))
            {
                result.QueueAccount = input.AqsQueueAccount;
                result.QueuedOn = input.CurrentQueuedOn ?? input.Now;
            }

            if (IsReleased(input))
            {
                // Never guessed (AD-082): an unmatched adviser leaves the case released to
                // nobody, and the reconciler reports it.
                result.Adviser = input.ResolvedAdviser;
                result.Supervisor = input.ResolvedSupervisor;
            }

            return result;
        }

        /// <summary>
        /// Waiting for an AQS checker: Queued, the route needs AQS, nobody holds the AQS review,
        /// and any Tax check the route needs is submitted (BR-004). The same test the portal's
        /// queue used to make in FetchXML, moved here so the queue can be a permission.
        /// </summary>
        public static bool InAqsQueue(CaseAccessInput input)
        {
            if (input.CaseStatus != CaseLifecycle.Queued || !input.RouteRequiresAqs)
            {
                return false;
            }

            var reviews = Active(input);
            if (reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeAqs && r.AssignedContactId.HasValue))
            {
                return false;
            }

            return !input.RouteRequiresTax
                || reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeTax && r.Submitted);
        }

        /// <summary>
        /// Released to the adviser (AR-04, answer 2): the case has a remedial action, sits in the
        /// remediation range, and owes no unsubmitted review, so no draft finding is ever visible.
        /// </summary>
        public static bool IsReleased(CaseAccessInput input)
        {
            if (!input.HasRemediation || !input.CaseStatus.HasValue)
            {
                return false;
            }

            if (Array.IndexOf(ReleaseStatuses, input.CaseStatus.Value) < 0)
            {
                return false;
            }

            return Active(input).All(r => r.Submitted);
        }

        private static List<ReviewFact> Active(CaseAccessInput input)
        {
            return (input.Reviews ?? new List<ReviewFact>()).Where(r => r != null && r.Active).ToList();
        }

        /// <summary>The contact on the latest review of a discipline, by sequence.</summary>
        private static EntityReference CheckerFor(IList<ReviewFact> reviews, int reviewType)
        {
            var holder = reviews
                .Where(r => r.ReviewType == reviewType && r.AssignedContactId.HasValue)
                .OrderByDescending(r => r.Sequence)
                .FirstOrDefault();

            return holder == null ? null : new EntityReference("contact", holder.AssignedContactId.Value);
        }
    }

    /// <summary>One review instance, as far as access is concerned.</summary>
    public sealed class ReviewFact
    {
        public int ReviewType { get; set; }

        public Guid? AssignedContactId { get; set; }

        public bool Submitted { get; set; }

        public bool Active { get; set; } = true;

        public int Sequence { get; set; }
    }

    public sealed class CaseAccessInput
    {
        public int? CaseStatus { get; set; }

        public bool RouteRequiresTax { get; set; }

        public bool RouteRequiresAqs { get; set; }

        public IList<ReviewFact> Reviews { get; set; } = new List<ReviewFact>();

        public bool HasRemediation { get; set; }

        public EntityReference ResolvedAdviser { get; set; }

        public EntityReference ResolvedSupervisor { get; set; }

        public EntityReference AqsQueueAccount { get; set; }

        public DateTime? CurrentQueuedOn { get; set; }

        public DateTime Now { get; set; }
    }

    public sealed class CaseAccessResult
    {
        public EntityReference TaxChecker { get; set; }

        public EntityReference AqsChecker { get; set; }

        public EntityReference QueueAccount { get; set; }

        public DateTime? QueuedOn { get; set; }

        public EntityReference Adviser { get; set; }

        public EntityReference Supervisor { get; set; }

        public bool ShareWithTaxTeam { get; set; }

        public bool ShareWithAqsTeam { get; set; }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CaseAccessTests`
Expected: all 17 pass.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/CaseAccess.cs plugins/OutcomeTesting.Plugins.Tests/CaseAccessTests.cs
git commit -m "feat(access): the CaseAccess rule, who may see a case (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The reconciler

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/CaseAccessReconciler.cs`
- Modify: `plugins/OutcomeTesting.Plugins.Tests/FakeOrganizationService.cs:659-674` (accept `GrantAccess` and `RevokeAccess` in `Execute`)
- Test: `plugins/OutcomeTesting.Plugins.Tests/CaseAccessReconcilerTests.cs`

**Interfaces:**
- Consumes: `CaseAccess.Decide/IsReleased/InAqsQueue`, `CaseAccessInput`, `ReviewFact` (Task 2); `Remediation.AdviserContact(IOrganizationService, EntityReference)` (existing, `Remediation.cs:815`).
- Produces:
  - `static CaseAccessChange CaseAccessReconciler.Reconcile(IOrganizationService service, Guid caseId, DateTime now)`
  - `sealed class CaseAccessChange { List<string> ChangedColumns; bool Released; bool AdviserUnmatched; bool SupervisorUnmatched; }`
  - Constants: `TaxTeamName = "Outcome Testing - Tax Team"`, `AqsTeamName = "Outcome Testing - AQS Team"`, `AqsQueueAccountName = "Outcome Testing - AQS Team"`, and the column names `TaxCheckerAttr`, `AqsCheckerAttr`, `QueueAccountAttr`, `QueuedOnAttr`, `AdviserAttr`, `SupervisorAttr`.

- [ ] **Step 1: Teach the fake the two sharing messages**

In `FakeOrganizationService.Execute`, before the `NotSupportedException`, add:

```csharp
            // Sharing (AD-218). Recorded in Requests like everything else, so a test asserts
            // on WHO was granted or revoked rather than on a response nobody reads.
            if (request.RequestName == "GrantAccess" || request.RequestName == "RevokeAccess")
            {
                return new OrganizationResponse();
            }
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class CaseAccessReconcilerTests
    {
        private static readonly Guid CaseId = Guid.Parse("dddddddd-0000-4000-8000-000000000001");
        private static readonly Guid RouteId = Guid.Parse("dddddddd-0000-4000-8000-000000000002");
        private static readonly Guid TaxTeam = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
        private static readonly Guid AqsTeam = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");
        private static readonly Guid QueueAccount = Guid.Parse("eeeeeeee-0000-4000-8000-000000000003");
        private static readonly Guid Checker = Guid.Parse("ffffffff-0000-4000-8000-000000000001");
        private static readonly DateTime Now = new DateTime(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

        private static FakeOrganizationService Environment(int status, bool tax, bool aqs)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("team", TaxTeam, "name", CaseAccessReconciler.TaxTeamName);
            svc.Seed("team", AqsTeam, "name", CaseAccessReconciler.AqsTeamName);
            svc.Seed("account", QueueAccount, "name", CaseAccessReconciler.AqsQueueAccountName);
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", tax, "al_requiresaqsreview", aqs);
            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(status),
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId),
                "al_advisername", "Ann Adviser",
                "al_adviseremail", "ann@example.com");
            return svc;
        }

        [Fact]
        public void A_queued_aqs_case_is_put_on_the_queue_and_shared_with_the_aqs_team()
        {
            var svc = Environment(CaseLifecycle.Queued, false, true);

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            var row = svc.Row("al_outcomecase", CaseId);
            Assert.Equal(QueueAccount, row.GetAttributeValue<EntityReference>(CaseAccessReconciler.QueueAccountAttr).Id);
            Assert.Equal(Now, row.GetAttributeValue<DateTime>(CaseAccessReconciler.QueuedOnAttr));
            Assert.Contains(CaseAccessReconciler.QueueAccountAttr, change.ChangedColumns);

            var grants = svc.Requests.OfType<GrantAccessRequest>().ToList();
            Assert.Single(grants);
            Assert.Equal(AqsTeam, grants[0].PrincipalAccess.Principal.Id);

            var revokes = svc.Requests.OfType<RevokeAccessRequest>().ToList();
            Assert.Single(revokes);
            Assert.Equal(TaxTeam, revokes[0].Revokee.Id);
        }

        [Fact]
        public void The_share_lets_a_manager_allocate()
        {
            // al_AssignCase writes the review and changes its owner as the CALLER, so a
            // read-only share would let a manager see work they could not allocate.
            var svc = Environment(CaseLifecycle.Queued, false, true);
            CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            var mask = svc.Requests.OfType<GrantAccessRequest>().Single().PrincipalAccess.AccessMask;
            Assert.True(mask.HasFlag(Microsoft.Crm.Sdk.AccessRights.ReadAccess));
            Assert.True(mask.HasFlag(Microsoft.Crm.Sdk.AccessRights.WriteAccess));
            Assert.True(mask.HasFlag(Microsoft.Crm.Sdk.AccessRights.AssignAccess));
        }

        [Fact]
        public void A_second_run_writes_nothing()
        {
            var svc = Environment(CaseLifecycle.Queued, false, true);
            CaseAccessReconciler.Reconcile(svc, CaseId, Now);
            svc.ClearUpdates();

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now.AddHours(1));

            Assert.Empty(change.ChangedColumns);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void The_allocated_checker_is_written_to_the_case()
        {
            var svc = Environment(CaseLifecycle.Assigned, true, true);
            svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_assignedcontactid", new EntityReference("contact", Checker),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));

            CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            Assert.Equal(Checker, svc.Row("al_outcomecase", CaseId)
                .GetAttributeValue<EntityReference>(CaseAccessReconciler.TaxCheckerAttr).Id);
        }

        [Fact]
        public void A_missing_team_is_refused_rather_than_skipped()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(CaseLifecycle.Queued));

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseAccessReconciler.Reconcile(svc, CaseId, Now));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Contains(CaseAccessReconciler.TaxTeamName, refusal.Message);
        }

        [Fact]
        public void An_ambiguous_adviser_is_reported_not_guessed()
        {
            var svc = Environment(CaseLifecycle.AwaitingRemediation, false, true);
            svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_submittedon", Now.AddDays(-1),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));
            svc.Seed("al_remediationaction", Guid.NewGuid(), "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            svc.Seed("contact", Guid.NewGuid(), "fullname", "Ann Adviser", "statecode", new OptionSetValue(0));
            svc.Seed("contact", Guid.NewGuid(), "fullname", "Ann Adviser", "statecode", new OptionSetValue(0));

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            Assert.True(change.Released);
            Assert.True(change.AdviserUnmatched);
            Assert.Null(svc.Row("al_outcomecase", CaseId).GetAttributeValue<EntityReference>(CaseAccessReconciler.AdviserAttr));
        }

        [Fact]
        public void A_released_case_names_the_supervisor_from_the_adviser_mapping()
        {
            var svc = Environment(CaseLifecycle.AwaitingRemediation, false, true);
            svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_submittedon", Now.AddDays(-1),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));
            svc.Seed("al_remediationaction", Guid.NewGuid(), "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            var adviser = Guid.NewGuid();
            var supervisor = Guid.NewGuid();
            svc.Seed("contact", adviser, "fullname", "Ann Adviser", "statecode", new OptionSetValue(0));
            svc.Seed(
                "al_advisermapping", Guid.NewGuid(),
                "al_adviseremail", "ann@example.com",
                "al_tcmanagerid", new EntityReference("contact", supervisor),
                "statecode", new OptionSetValue(0));

            var change = CaseAccessReconciler.Reconcile(svc, CaseId, Now);

            var row = svc.Row("al_outcomecase", CaseId);
            Assert.Equal(adviser, row.GetAttributeValue<EntityReference>(CaseAccessReconciler.AdviserAttr).Id);
            Assert.Equal(supervisor, row.GetAttributeValue<EntityReference>(CaseAccessReconciler.SupervisorAttr).Id);
            Assert.False(change.AdviserUnmatched);
            Assert.False(change.SupervisorUnmatched);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CaseAccessReconcilerTests`
Expected: build error. `CaseAccessReconciler` does not exist.

- [ ] **Step 4: Write the implementation**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Crm.Sdk;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Makes a case's access columns and team shares match what <see cref="CaseAccess"/>
    /// says they should be (AD-218). Writes only what differs, so it is safe to run on every
    /// write that could change the answer, and safe to run again as a backfill.
    ///
    /// Run as SYSTEM. Portal writes arrive as the site's application user (AD-053), and
    /// sharing needs a privilege no person using the product should hold; the bookkeeping is
    /// derived from a write the calling command has already authorised.
    /// </summary>
    public static class CaseAccessReconciler
    {
        public const string TaxTeamName = "Outcome Testing - Tax Team";
        public const string AqsTeamName = "Outcome Testing - AQS Team";
        public const string AqsQueueAccountName = "Outcome Testing - AQS Team";

        public const string TaxCheckerAttr = "al_taxcheckercontactid";
        public const string AqsCheckerAttr = "al_aqscheckercontactid";
        public const string QueueAccountAttr = "al_aqsqueueaccountid";
        public const string QueuedOnAttr = "al_aqsqueuedon";
        public const string AdviserAttr = "al_advisercontactid";
        public const string SupervisorAttr = "al_tcsupervisorcontactid";

        private const string CaseEntity = "al_outcomecase";

        /// <summary>
        /// Read, write, append, append-to and assign. <c>al_AssignCase</c> writes the review
        /// instance and changes its owner through the caller's own service, so a manager with a
        /// read-only share could see their team's work and not allocate it (spec amendment 7).
        /// </summary>
        public const AccessRights ShareMask =
            AccessRights.ReadAccess | AccessRights.WriteAccess | AccessRights.AppendAccess
            | AccessRights.AppendToAccess | AccessRights.AssignAccess;

        public static CaseAccessChange Reconcile(IOrganizationService service, Guid caseId, DateTime now)
        {
            var teams = new Dictionary<string, EntityReference>
            {
                { TaxTeamName, FindByName(service, "team", TaxTeamName) },
                { AqsTeamName, FindByName(service, "team", AqsTeamName) },
            };

            var outcomeCase = service.Retrieve(
                CaseEntity,
                caseId,
                new ColumnSet(
                    "al_casestatus", "al_reviewrouteid", "al_adviseremail",
                    TaxCheckerAttr, AqsCheckerAttr, QueueAccountAttr, QueuedOnAttr, AdviserAttr, SupervisorAttr));

            var status = outcomeCase.GetAttributeValue<OptionSetValue>("al_casestatus");
            var input = new CaseAccessInput
            {
                CaseStatus = status == null ? (int?)null : status.Value,
                Reviews = ReadReviews(service, caseId),
                HasRemediation = HasRemediation(service, caseId),
                CurrentQueuedOn = outcomeCase.GetAttributeValue<DateTime?>(QueuedOnAttr),
                Now = now,
            };

            var routeRef = outcomeCase.GetAttributeValue<EntityReference>("al_reviewrouteid");
            if (routeRef != null)
            {
                var route = service.Retrieve("al_reviewroute", routeRef.Id, new ColumnSet("al_requirestaxreview", "al_requiresaqsreview"));
                input.RouteRequiresTax = route.GetAttributeValue<bool>("al_requirestaxreview");
                input.RouteRequiresAqs = route.GetAttributeValue<bool>("al_requiresaqsreview");
            }

            // Looked up only when used, so a case that never reaches the queue or remediation
            // costs no extra reads and needs no account to exist.
            if (CaseAccess.InAqsQueue(input))
            {
                input.AqsQueueAccount = FindByName(service, "account", AqsQueueAccountName);
            }

            var released = CaseAccess.IsReleased(input);
            if (released)
            {
                var caseRef = new EntityReference(CaseEntity, caseId);
                input.ResolvedAdviser = Remediation.AdviserContact(service, caseRef);
                input.ResolvedSupervisor = SupervisorFor(service, outcomeCase.GetAttributeValue<string>("al_adviseremail"));
            }

            var decided = CaseAccess.Decide(input);

            var update = new Entity(CaseEntity, caseId);
            var changed = new List<string>();
            SetIfChanged(update, changed, outcomeCase, TaxCheckerAttr, decided.TaxChecker);
            SetIfChanged(update, changed, outcomeCase, AqsCheckerAttr, decided.AqsChecker);
            SetIfChanged(update, changed, outcomeCase, QueueAccountAttr, decided.QueueAccount);
            SetIfChanged(update, changed, outcomeCase, AdviserAttr, decided.Adviser);
            SetIfChanged(update, changed, outcomeCase, SupervisorAttr, decided.Supervisor);

            var queuedOn = outcomeCase.GetAttributeValue<DateTime?>(QueuedOnAttr);
            if (queuedOn != decided.QueuedOn)
            {
                update[QueuedOnAttr] = decided.QueuedOn;
                changed.Add(QueuedOnAttr);
            }

            if (changed.Count > 0)
            {
                service.Update(update);
            }

            var target = new EntityReference(CaseEntity, caseId);
            Share(service, target, teams[TaxTeamName], decided.ShareWithTaxTeam);
            Share(service, target, teams[AqsTeamName], decided.ShareWithAqsTeam);

            return new CaseAccessChange
            {
                ChangedColumns = changed,
                Released = released,
                AdviserUnmatched = released && decided.Adviser == null,
                SupervisorUnmatched = released && decided.Supervisor == null,
            };
        }

        private static List<ReviewFact> ReadReviews(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression("al_reviewinstance")
            {
                ColumnSet = new ColumnSet("al_reviewtype", "al_assignedcontactid", "al_submittedon", "al_sequence", "statecode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);

            var facts = new List<ReviewFact>();
            foreach (var row in CommandHelpers.RetrieveAll(service, query))
            {
                var type = row.GetAttributeValue<OptionSetValue>("al_reviewtype");
                var contact = row.GetAttributeValue<EntityReference>("al_assignedcontactid");
                var state = row.GetAttributeValue<OptionSetValue>("statecode");
                facts.Add(new ReviewFact
                {
                    ReviewType = type == null ? 0 : type.Value,
                    AssignedContactId = contact == null ? (Guid?)null : contact.Id,
                    Submitted = row.GetAttributeValue<DateTime?>("al_submittedon").HasValue,
                    Active = state == null || state.Value == 0,
                    Sequence = row.GetAttributeValue<int>("al_sequence"),
                });
            }

            return facts;
        }

        private static bool HasRemediation(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        /// <summary>
        /// The T&amp;C Manager mapped to this adviser (AD-162), on the same email key the
        /// portal's sign-off gate reads. None or two means nobody: never guessed.
        /// </summary>
        private static EntityReference SupervisorFor(IOrganizationService service, string adviserEmail)
        {
            if (string.IsNullOrWhiteSpace(adviserEmail))
            {
                return null;
            }

            var query = new QueryExpression("al_advisermapping")
            {
                ColumnSet = new ColumnSet("al_tcmanagerid"),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_adviseremail", ConditionOperator.Equal, adviserEmail.Trim());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var rows = service.RetrieveMultiple(query).Entities;
            return rows.Count == 1 ? rows[0].GetAttributeValue<EntityReference>("al_tcmanagerid") : null;
        }

        private static EntityReference FindByName(IOrganizationService service, string entity, string name)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, name);

            var rows = service.RetrieveMultiple(query).Entities;
            if (rows.Count != 1)
            {
                // Refused rather than skipped: a case the reconciler silently could not share
                // or queue is a case nobody can see, and nothing would say why.
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix
                    + "Expected exactly one " + entity + " named \"" + name + "\" and found " + rows.Count
                    + ". Run the registration tool's ensureaccessprincipals verb for this environment.");
            }

            return rows[0].ToEntityReference();
        }

        private static void SetIfChanged(
            Entity update, List<string> changed, Entity current, string attribute, EntityReference wanted)
        {
            var existing = current.GetAttributeValue<EntityReference>(attribute);
            var same = existing == null
                ? wanted == null
                : wanted != null && existing.Id == wanted.Id;
            if (same)
            {
                return;
            }

            update[attribute] = wanted;
            changed.Add(attribute);
        }

        private static void Share(IOrganizationService service, EntityReference target, EntityReference team, bool wanted)
        {
            if (wanted)
            {
                service.Execute(new GrantAccessRequest
                {
                    Target = target,
                    PrincipalAccess = new PrincipalAccess { Principal = team, AccessMask = ShareMask },
                });
                return;
            }

            // Revoking a principal the row was never shared with is a no-op on the platform,
            // which is what lets this be idempotent without first reading the shares.
            service.Execute(new RevokeAccessRequest { Target = target, Revokee = team });
        }
    }

    public sealed class CaseAccessChange
    {
        public List<string> ChangedColumns { get; set; } = new List<string>();

        public bool Released { get; set; }

        public bool AdviserUnmatched { get; set; }

        public bool SupervisorUnmatched { get; set; }
    }
}
```

If `Microsoft.Crm.Sdk.Messages` does not resolve in the plug-in project, check `Microsoft.CrmSdk.CoreAssemblies`. That package ships `Microsoft.Crm.Sdk.Proxy.dll`. Do not add a new package.

If `CommandHelpers.RetrieveAll` does not accept a `QueryExpression` without paging info, read `CommandHelpers.cs` and match what `AssignCasePlugin.ReleasePriorAssignments` passes.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CaseAccess`
Expected: every `CaseAccessTests` and `CaseAccessReconcilerTests` test passes.

- [ ] **Step 6: Run the whole suite**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: no new failures. A test that asserted `Execute` refuses every message may need updating. If one does, update its message list and say so in the commit.

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/CaseAccessReconciler.cs plugins/OutcomeTesting.Plugins.Tests/CaseAccessReconcilerTests.cs plugins/OutcomeTesting.Plugins.Tests/FakeOrganizationService.cs
git commit -m "feat(access): the reconciler that writes a case's access columns and team shares (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: The step plug-in and the backfill command

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/CaseAccessPlugin.cs`
- Create: `plugins/OutcomeTesting.Plugins/ReconcileCaseAccessPlugin.cs`
- Create: `plugins/customapi/al_ReconcileCaseAccess.customapi.json`
- Test: `plugins/OutcomeTesting.Plugins.Tests/CaseAccessPluginTests.cs`

**Interfaces:**
- Consumes: `CaseAccessReconciler.Reconcile` (Task 3), `PermissionHelpers.EnsureAppPermission`, `CommandHelpers.ParseRequiredGuid`.
- Produces:
  - Step type `OutcomeTesting.Plugins.CaseAccessPlugin`, registered in Task 12 on `al_outcomecase` Update, `al_reviewinstance` Create and Update, and `al_remediationaction` Create.
  - Custom API `al_ReconcileCaseAccess`. Input: `TargetId` (string GUID). Outputs: `Changed` (comma-separated column names), `Released` (bool), `AdviserUnmatched` (bool), `SupervisorUnmatched` (bool).
  - `static Guid? CaseAccessPlugin.CaseIdFor(IPluginExecutionContext context, IOrganizationService service)`

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class CaseAccessPluginTests
    {
        private static readonly Guid CaseId = Guid.Parse("dddddddd-0000-4000-8000-000000000011");
        private static readonly Guid ReviewId = Guid.Parse("dddddddd-0000-4000-8000-000000000012");

        [Fact]
        public void A_case_write_reconciles_that_case()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_outcomecase", PrimaryEntityId = CaseId };
            Assert.Equal(CaseId, CaseAccessPlugin.CaseIdFor(context, new FakeOrganizationService()));
        }

        [Fact]
        public void A_review_write_reconciles_its_case_from_the_target()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_reviewinstance", PrimaryEntityId = ReviewId };
            context.InputParameters["Target"] = new Entity("al_reviewinstance", ReviewId)
            {
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
            };
            Assert.Equal(CaseId, CaseAccessPlugin.CaseIdFor(context, new FakeOrganizationService()));
        }

        [Fact]
        public void A_review_update_without_the_case_on_it_reads_the_case_from_the_row()
        {
            // An update carries only the columns that changed, so the lookup is usually absent.
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_reviewinstance", PrimaryEntityId = ReviewId };
            context.InputParameters["Target"] = new Entity("al_reviewinstance", ReviewId);

            Assert.Equal(CaseId, CaseAccessPlugin.CaseIdFor(context, svc));
        }

        [Fact]
        public void A_review_with_no_case_reconciles_nothing()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId);
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_reviewinstance", PrimaryEntityId = ReviewId };
            context.InputParameters["Target"] = new Entity("al_reviewinstance", ReviewId);

            Assert.Null(CaseAccessPlugin.CaseIdFor(context, svc));
        }
    }
}
```

If `FakePluginExecutionContext` has no parameterless constructor with settable `PrimaryEntityName` and `PrimaryEntityId`, read `FakePluginExecutionContext.cs` (lines 15-61). Construct it the way that file allows, and keep the assertions unchanged.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CaseAccessPluginTests`
Expected: build error. `CaseAccessPlugin` does not exist.

- [ ] **Step 3: Write the step plug-in**

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Keeps a case's access columns current (AD-218). Registered synchronous post-operation
    /// on the writes that can change who may see a case: al_outcomecase Update (status, route,
    /// adviser name, adviser email), al_reviewinstance Create and Update (assigned contact,
    /// submitted on, state) and al_remediationaction Create.
    ///
    /// A step rather than a call inside each command, because the status is written by
    /// CaseTransitions.MoveThrough from many commands and a new writer would otherwise be a new
    /// place to forget it. Post-operation and synchronous, so it runs in the writer's
    /// transaction and a failure rolls the writer back.
    ///
    /// Its own case update touches only access columns, which no step filter names, so it
    /// cannot re-trigger itself.
    /// </summary>
    public class CaseAccessPlugin : PluginBase
    {
        public CaseAccessPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CaseAccessPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            // SYSTEM: see CaseAccessReconciler for why the bookkeeping is not the caller's.
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            var caseId = CaseIdFor(localPluginContext.PluginExecutionContext, system);
            if (!caseId.HasValue)
            {
                return;
            }

            CaseAccessReconciler.Reconcile(system, caseId.Value, DateTime.UtcNow);
        }

        /// <summary>The case a write belongs to, or null where it belongs to none.</summary>
        public static Guid? CaseIdFor(IPluginExecutionContext context, IOrganizationService service)
        {
            if (string.Equals(context.PrimaryEntityName, "al_outcomecase", StringComparison.OrdinalIgnoreCase))
            {
                return context.PrimaryEntityId;
            }

            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            var onTarget = target == null ? null : target.GetAttributeValue<EntityReference>("al_outcomecaseid");
            if (onTarget != null)
            {
                return onTarget.Id;
            }

            var row = service.Retrieve(context.PrimaryEntityName, context.PrimaryEntityId, new ColumnSet("al_outcomecaseid"));
            var onRow = row.GetAttributeValue<EntityReference>("al_outcomecaseid");
            return onRow == null ? (Guid?)null : onRow.Id;
        }
    }
}
```

- [ ] **Step 4: Write the backfill command**

```csharp
using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// al_ReconcileCaseAccess: runs the reconciler on one case on demand (AD-218). It is the
    /// backfill for cases that existed before the step was registered, and the repair tool
    /// for a case whose access looks wrong. Gated on permission.manage, because it changes
    /// who can see a case even though every value it writes is derived.
    /// </summary>
    public class ReconcileCaseAccessPlugin : PluginBase
    {
        private const string InTargetId = "TargetId";

        public ReconcileCaseAccessPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ReconcileCaseAccessPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            PermissionHelpers.EnsureAppPermission(
                localPluginContext.PluginUserService, context, "permission.manage", PermissionHelpers.AccessManage);

            var caseId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            var change = CaseAccessReconciler.Reconcile(system, caseId, DateTime.UtcNow);

            context.OutputParameters["Changed"] = string.Join(",", change.ChangedColumns);
            context.OutputParameters["Released"] = change.Released;
            context.OutputParameters["AdviserUnmatched"] = change.AdviserUnmatched;
            context.OutputParameters["SupervisorUnmatched"] = change.SupervisorUnmatched;
        }
    }
}
```

- [ ] **Step 5: Write the contract**

`plugins/customapi/al_ReconcileCaseAccess.customapi.json`:

```json
{
  "$comment": "Contract for the ReconcileCaseAccess command (AD-218). Deployed via the registration console (registerall). Custom API parameter Type codes: 0=Boolean, 10=String.",
  "customApi": {
    "uniquename": "al_ReconcileCaseAccess",
    "name": "al_ReconcileCaseAccess",
    "displayname": "Reconcile Case Access",
    "description": "Brings one case's access columns and team shares into line with its current state (AD-218). The backfill and repair path for role-scoped access.",
    "bindingtype": 0,
    "boundentitylogicalname": null,
    "isfunction": false,
    "isprivate": false,
    "allowedcustomprocessingsteptype": 0,
    "executeprivilegename": null,
    "pluginType": "OutcomeTesting.Plugins.ReconcileCaseAccessPlugin"
  },
  "requestParameters": [
    {
      "uniquename": "TargetId",
      "name": "TargetId",
      "displayname": "Case id",
      "description": "Id of the al_outcomecase to reconcile.",
      "type": 10,
      "isoptional": false
    }
  ],
  "responseProperties": [
    { "uniquename": "Changed", "name": "Changed", "displayname": "Changed", "description": "Comma-separated access columns that were written; empty when nothing changed.", "type": 10 },
    { "uniquename": "Released", "name": "Released", "displayname": "Released", "description": "True when the case is released to its adviser.", "type": 0 },
    { "uniquename": "AdviserUnmatched", "name": "AdviserUnmatched", "displayname": "Adviser unmatched", "description": "True when the case is released but its adviser matched no single contact.", "type": 0 },
    { "uniquename": "SupervisorUnmatched", "name": "SupervisorUnmatched", "displayname": "Supervisor unmatched", "description": "True when the case is released but no single adviser mapping names a supervisor.", "type": 0 }
  ]
}
```

- [ ] **Step 6: Run the tests to verify they pass, including the contract test**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter "FullyQualifiedName~CaseAccessPluginTests|FullyQualifiedName~CustomApiContractTests"`
Expected: all pass. `CustomApiContractTests` confirms the plug-in reads only `TargetId`.

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/CaseAccessPlugin.cs plugins/OutcomeTesting.Plugins/ReconcileCaseAccessPlugin.cs plugins/customapi/al_ReconcileCaseAccess.customapi.json plugins/OutcomeTesting.Plugins.Tests/CaseAccessPluginTests.cs
git commit -m "feat(access): reconcile on every write that can change access, and on demand (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Allocation is scoped by role on the server

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/AllocationScope.cs`
- Modify: `plugins/OutcomeTesting.Plugins/PermissionHelpers.cs:94-103` (extract `AnyMappingExists`)
- Modify: `plugins/OutcomeTesting.Plugins/AssignCasePlugin.cs:23-26` (class comment), `:124-125` (guard after `ResolveReviewInstance`)
- Test: `plugins/OutcomeTesting.Plugins.Tests/AllocationScopeTests.cs`

**Interfaces:**
- Consumes: `PermissionHelpers.GetCallerEmail`, `PermissionHelpers.ResolveRoleCodesForEmail`, `WebRoleRegistry.HasRole`, `WebRoleRegistry.TaxReviewerRole` and `AqsReviewerRole`, `AssignCasePlugin.Assignee`.
- Produces:
  - `static bool AllocationScope.MayAllocate(IEnumerable<string> roleCodes, int reviewType)`
  - `static void AllocationScope.EnsureCallerMayAllocate(IOrganizationService systemService, IPluginExecutionContext context, int reviewType)`
  - `static void AllocationScope.EnsureAssigneeHoldsDiscipline(IOrganizationService service, AssignCasePlugin.Assignee assignee, int reviewType)`
  - `static bool PermissionHelpers.AnyMappingExists(IOrganizationService service)`
  - Constants `AllocationScope.TaxTeamManagerRole = "AL Portal - Tax Team Manager"` and `AqsTeamManagerRole = "AL Portal - AQS Team Manager"`. Task 10 mirrors these names in TypeScript.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>Who may allocate which check (AR-01, AR-02, answer 4a/4b; closes OD-029(c)).</summary>
    public class AllocationScopeTests
    {
        private static readonly Guid ContactId = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222299");

        [Theory]
        [InlineData(AllocationScope.TaxTeamManagerRole, ResponseRules.ReviewTypeTax, true)]
        [InlineData(AllocationScope.TaxTeamManagerRole, ResponseRules.ReviewTypeAqs, false)]
        [InlineData(AllocationScope.AqsTeamManagerRole, ResponseRules.ReviewTypeAqs, true)]
        [InlineData(AllocationScope.AqsTeamManagerRole, ResponseRules.ReviewTypeTax, false)]
        [InlineData("AL Portal - Outcome Testing Manager", ResponseRules.ReviewTypeTax, true)]
        [InlineData("AL Portal - Outcome Testing Manager", ResponseRules.ReviewTypeAqs, true)]
        [InlineData("Administrators", ResponseRules.ReviewTypeAqs, true)]
        [InlineData("AL Portal - T&C Supervisor", ResponseRules.ReviewTypeTax, false)]
        [InlineData("AL Portal - Tax Reviewer", ResponseRules.ReviewTypeTax, false)]
        public void Each_role_allocates_only_its_own_discipline(string role, int reviewType, bool allowed)
        {
            Assert.Equal(allowed, AllocationScope.MayAllocate(new[] { role }, reviewType));
        }

        [Fact]
        public void Role_names_match_whatever_their_case_and_spacing()
        {
            Assert.True(AllocationScope.MayAllocate(new[] { "  al portal - tax team manager " }, ResponseRules.ReviewTypeTax));
        }

        [Fact]
        public void An_unrecognised_discipline_is_allocated_by_nobody_below_the_managers()
        {
            Assert.False(AllocationScope.MayAllocate(new[] { AllocationScope.TaxTeamManagerRole }, 0));
        }

        [Fact]
        public void An_assignee_without_the_reviewer_role_is_refused_by_name()
        {
            var svc = new FakeOrganizationService();
            svc.FetchResults.Enqueue(new EntityCollection());
            var assignee = new AssignCasePlugin.Assignee { ContactId = ContactId, ContactName = "Ada Checker" };

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => AllocationScope.EnsureAssigneeHoldsDiscipline(svc, assignee, ResponseRules.ReviewTypeTax));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, refusal.Message);
            Assert.Contains("Ada Checker", refusal.Message);
            Assert.Contains(WebRoleRegistry.TaxReviewerRole, refusal.Message);
        }

        [Fact]
        public void An_assignee_holding_the_reviewer_role_passes()
        {
            var svc = new FakeOrganizationService();
            var row = new Entity("contact", ContactId);
            row["role.name"] = new AliasedValue("powerpagecomponent", "name", WebRoleRegistry.AqsReviewerRole);
            svc.FetchResults.Enqueue(new EntityCollection(new[] { row }));
            var assignee = new AssignCasePlugin.Assignee { ContactId = ContactId, ContactName = "Ada Checker" };

            AllocationScope.EnsureAssigneeHoldsDiscipline(svc, assignee, ResponseRules.ReviewTypeAqs);
        }
    }
}
```

The `FetchResults` shape copies `ClaimCasePluginTests.Holding` (lines 166-179), which is how `WebRoleRegistry.HasRole` is faked elsewhere.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~AllocationScopeTests`
Expected: build error. `AllocationScope` does not exist.

- [ ] **Step 3: Extract `AnyMappingExists` in `PermissionHelpers`**

Replace the inline `anyMapping` query in `EnsureAppPermission` (currently `var anyMapping = new QueryExpression(MappingEntity) {...}; if (systemService.RetrieveMultiple(anyMapping).Entities.Count == 0) { return; }`) with `if (!AnyMappingExists(systemService)) { return; }`. Keep the comment above it. Then add:

```csharp
        /// <summary>
        /// Whether any role mapping has ever been created, in any state: the bootstrap test
        /// EnsureAppPermission and the allocation scope share, so the two cannot disagree
        /// about whether an environment is still being set up.
        /// </summary>
        public static bool AnyMappingExists(IOrganizationService service)
        {
            var anyMapping = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            return service.RetrieveMultiple(anyMapping).Entities.Count > 0;
        }
```

- [ ] **Step 4: Write `AllocationScope`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who may allocate which check (AD-218; closes OD-029(c)). Each team manager allocates
    /// their own discipline only; Outcome Testing Manager and Administrators allocate either.
    /// Anyone else holding command.assign is refused: denied by default, so a role granted
    /// allocation by accident cannot reach across teams.
    ///
    /// Keyed on web role NAME because that is what al_rolecode and every permission rule
    /// carry (AD-087). The app mirrors this in features/cases/allocationScope.ts.
    /// </summary>
    public static class AllocationScope
    {
        public const string TaxTeamManagerRole = "AL Portal - Tax Team Manager";
        public const string AqsTeamManagerRole = "AL Portal - AQS Team Manager";
        private const string OutcomeTestingManagerRole = "AL Portal - Outcome Testing Manager";
        private const string AdministratorsRole = "Administrators";

        public static bool MayAllocate(IEnumerable<string> roleCodes, int reviewType)
        {
            var roles = new HashSet<string>(
                (roleCodes ?? Enumerable.Empty<string>()).Where(r => r != null).Select(r => r.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (roles.Contains(OutcomeTestingManagerRole) || roles.Contains(AdministratorsRole))
            {
                return true;
            }

            if (reviewType == ResponseRules.ReviewTypeTax)
            {
                return roles.Contains(TaxTeamManagerRole);
            }

            if (reviewType == ResponseRules.ReviewTypeAqs)
            {
                return roles.Contains(AqsTeamManagerRole);
            }

            return false;
        }

        public static void EnsureCallerMayAllocate(
            IOrganizationService systemService, IPluginExecutionContext context, int reviewType)
        {
            // The same bootstrap the permission gate has: before any role is mapped, the first
            // administrator must be able to set the environment up.
            if (!PermissionHelpers.AnyMappingExists(systemService))
            {
                return;
            }

            var roles = PermissionHelpers.ResolveRoleCodesForEmail(
                systemService, PermissionHelpers.GetCallerEmail(systemService, context));
            if (MayAllocate(roles, reviewType))
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.UnauthorizedPrefix
                + "Your role does not allocate " + Discipline(reviewType)
                + " checks. Each team manager allocates their own team's checks.");
        }

        public static void EnsureAssigneeHoldsDiscipline(
            IOrganizationService service, AssignCasePlugin.Assignee assignee, int reviewType)
        {
            string required;
            if (reviewType == ResponseRules.ReviewTypeTax)
            {
                required = WebRoleRegistry.TaxReviewerRole;
            }
            else if (reviewType == ResponseRules.ReviewTypeAqs)
            {
                required = WebRoleRegistry.AqsReviewerRole;
            }
            else
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This check has no recognised discipline, so it cannot be allocated.");
            }

            if (WebRoleRegistry.HasRole(service, assignee.ContactId, required))
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix
                + assignee.ContactName + " does not hold the " + required + " role, so "
                + (reviewType == ResponseRules.ReviewTypeTax ? "a Tax" : "an AQS")
                + " check cannot be allocated to them.");
        }

        private static string Discipline(int reviewType)
        {
            return reviewType == ResponseRules.ReviewTypeTax ? "Tax"
                : reviewType == ResponseRules.ReviewTypeAqs ? "AQS"
                : "these";
        }
    }
}
```

- [ ] **Step 5: Call both checks from `AssignCasePlugin`**

Straight after `var reviewId = review.Id;` (line 125), insert:

```csharp
            // AD-218: each team manager allocates their own discipline, and only to someone
            // holding that discipline's reviewer role. After ResolveReviewInstance because the
            // discipline is the review's; a refusal rolls back any review it opened.
            var reviewType = ReviewTypeOf(review) ?? 0;
            AllocationScope.EnsureCallerMayAllocate(systemService, context, reviewType);
            AllocationScope.EnsureAssigneeHoldsDiscipline(systemService, assignee, reviewType);
```

Replace the class comment's last paragraph (lines 23-26, "Not addressed, and deliberately so: ...") with:

```csharp
    /// Per-team scoping is enforced here since AD-218 (closing OD-029(c)): see
    /// <see cref="AllocationScope"/>.
```

- [ ] **Step 6: Run the tests**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: `AllocationScopeTests` pass. Some existing tests run the whole `AssignCasePlugin.Execute` path: `AssignCasePluginTests`, `AssignCaseReviewTests` and `ClaimAfterRouteChangeTests`. They may now fail in `EnsureAssigneeHoldsDiscipline`, because their assignee holds no web role. For each such test, enqueue the reviewer role for the review's discipline, using the `Holding` shape above, at the point in `FetchResults` order where the role fetch is issued. Do not weaken the check. The bootstrap keeps `EnsureCallerMayAllocate` silent where a test seeds no `al_userrolemapping`. `AppPermissionGateTests` must still pass: the new reads are the gate's own tables.

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/AllocationScope.cs plugins/OutcomeTesting.Plugins/PermissionHelpers.cs plugins/OutcomeTesting.Plugins/AssignCasePlugin.cs plugins/OutcomeTesting.Plugins.Tests/
git commit -m "feat(allocation): each team manager allocates their own discipline, to its reviewers (AD-218, closes OD-029(c))

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Registration verbs for the new environment pieces

**Files:**
- Modify: `plugins/OutcomeTesting.Registration/Program.cs`:
  - the header comment block (document four verbs)
  - the dispatch list after `grantsecurity` (~line 322)
  - `GrantTable` (~line 7580), which gains a `depth` parameter
  - `BuildPermissionJson`, which gains `adx_accountrelationship`
  - new functions `SetCascade`, `EnsureAccessPrincipals`, `GrantTeamSecurity`, `ReconcileAccess`

**Interfaces:**
- Consumes: `Connect`, `ConfirmedFor`, `EnsureRole`, `RootBusinessUnitId`, `AddRoleToSolution`, `GrantTable`.
- Produces these CLI verbs, used in Task 12:
  - `setcascade <orgUrl> <relationshipSchemaName> [--reparent-only] --confirm <orgUrl>`
  - `ensureaccessprincipals <orgUrl> --confirm <orgUrl>`
  - `grantteamsecurity <orgUrl>`
  - `reconcileaccess <orgUrl> [--confirm <orgUrl>]`

The registration tool has no test project. Its check is `dotnet build` plus the read-back each verb performs.

- [ ] **Step 1: Give `GrantTable` a depth**

Add a final optional parameter `PrivilegeDepth depth = PrivilegeDepth.Global` to `GrantTable`. Replace `Depth = PrivilegeDepth.Global` inside it with `Depth = depth`. Existing callers keep Global.

- [ ] **Step 2: Carry the account relationship into permission JSON**

In `BuildPermissionJson`, add `"adx_accountrelationship"` to the key array, after `"adx_appendto"`. Without it, an Account-scoped permission would be written with no relationship and would grant nothing.

- [ ] **Step 3: Add the dispatch lines**

After the `grantsecurity` block:

```csharp
if (args.Length >= 3 && args[0].Equals("setcascade", StringComparison.OrdinalIgnoreCase))
{
    return SetCascade(args);
}

if (args.Length >= 2 && args[0].Equals("ensureaccessprincipals", StringComparison.OrdinalIgnoreCase))
{
    return EnsureAccessPrincipals(args);
}

if (args.Length >= 2 && args[0].Equals("grantteamsecurity", StringComparison.OrdinalIgnoreCase))
{
    return GrantTeamSecurity(args[1]);
}

if (args.Length >= 2 && args[0].Equals("reconcileaccess", StringComparison.OrdinalIgnoreCase))
{
    return ReconcileAccess(args);
}
```

- [ ] **Step 4: Write `SetCascade`**

```csharp
// AD-218: a case shared with a team shares its children too. Share, Unshare and Reparent
// become Cascade; Assign and Delete are left alone, because reassigning a case must never
// reassign a checker's review. --reparent-only is for al_reviewinstance_response, whose
// Share is already Cascade. Reads back, because a successful-looking write is not evidence.
int SetCascade(string[] a)
{
    var orgUrl = a[1];
    if (!ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This changes relationship metadata. Re-run as: setcascade <orgUrl> <relationshipSchemaName> [--reparent-only] --confirm <orgUrl>");
        return 1;
    }

    var name = a[2].Trim();
    var reparentOnly = a.Any(x => x.Equals("--reparent-only", StringComparison.OrdinalIgnoreCase));
    using var svc = Connect(orgUrl);

    var current = (RetrieveRelationshipResponse)svc.Execute(new RetrieveRelationshipRequest { Name = name });
    if (current.RelationshipMetadata is not OneToManyRelationshipMetadata oneToMany)
    {
        Console.Error.WriteLine($"'{name}' is not a one-to-many relationship. Nothing was changed.");
        return 1;
    }

    oneToMany.CascadeConfiguration.Reparent = CascadeType.Cascade;
    if (!reparentOnly)
    {
        oneToMany.CascadeConfiguration.Share = CascadeType.Cascade;
        oneToMany.CascadeConfiguration.Unshare = CascadeType.Cascade;
    }

    svc.Execute(new UpdateRelationshipRequest { Relationship = oneToMany, MergeLabels = true });

    var after = (OneToManyRelationshipMetadata)((RetrieveRelationshipResponse)svc.Execute(
        new RetrieveRelationshipRequest { Name = name })).RelationshipMetadata;
    Console.WriteLine(
        $"{name}: Share={after.CascadeConfiguration.Share} Unshare={after.CascadeConfiguration.Unshare} Reparent={after.CascadeConfiguration.Reparent}");
    return after.CascadeConfiguration.Reparent == CascadeType.Cascade
        && (reparentOnly || after.CascadeConfiguration.Share == CascadeType.Cascade) ? 0 : 1;
}
```

- [ ] **Step 5: Write `EnsureAccessPrincipals`**

```csharp
// AD-218: the two owner teams the reconciler shares cases with, and the account AQS
// reviewers belong to for the queue permission. Data, not solution components: each
// environment makes its own, and the names are what the plug-in looks them up by
// (CaseAccessReconciler.TaxTeamName, AqsTeamName, AqsQueueAccountName). Idempotent.
int EnsureAccessPrincipals(string[] a)
{
    var orgUrl = a[1];
    if (!ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine("This creates teams and an account. Re-run as: ensureaccessprincipals <orgUrl> --confirm <orgUrl>");
        return 1;
    }

    using var svc = Connect(orgUrl);
    var buId = RootBusinessUnitId(svc);

    foreach (var teamName in new[] { "Outcome Testing - Tax Team", "Outcome Testing - AQS Team" })
    {
        var found = svc.RetrieveMultiple(new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("name"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, teamName) } },
        }).Entities;

        if (found.Count > 1)
        {
            Console.Error.WriteLine($"  {found.Count} teams are named '{teamName}'. The reconciler refuses an ambiguous name; remove the duplicate.");
            return 1;
        }

        if (found.Count == 1)
        {
            Console.WriteLine($"  team exists   {teamName} ({found[0].Id:D})");
            continue;
        }

        var id = svc.Create(new Entity("team")
        {
            ["name"] = teamName,
            ["teamtype"] = new OptionSetValue(0), // Owner
            ["businessunitid"] = new EntityReference("businessunit", buId),
        });
        Console.WriteLine($"  team created  {teamName} ({id:D})");
    }

    const string accountName = "Outcome Testing - AQS Team";
    var accounts = svc.RetrieveMultiple(new QueryExpression("account")
    {
        ColumnSet = new ColumnSet("name"),
        Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, accountName) } },
    }).Entities;

    if (accounts.Count > 1)
    {
        Console.Error.WriteLine($"  {accounts.Count} accounts are named '{accountName}'. Remove the duplicate.");
        return 1;
    }

    if (accounts.Count == 1)
    {
        Console.WriteLine($"  account exists {accountName} ({accounts[0].Id:D})");
    }
    else
    {
        var id = svc.Create(new Entity("account") { ["name"] = accountName });
        Console.WriteLine($"  account created {accountName} ({id:D})");
    }

    return 0;
}
```

- [ ] **Step 6: Write `GrantTeamSecurity`**

```csharp
// AD-218: the security role the two team managers are mapped to by the project owner.
// Basic (User) depth on the case tables, so a manager reads what is shared with their
// team and nothing else; Global on configuration and on what the permission gate reads
// (AD-142), which carries no case data. Always paired with Basic User.
int GrantTeamSecurity(string orgUrl)
{
    using var svc = Connect(orgUrl);
    var buId = RootBusinessUnitId(svc);
    var role = EnsureRole(svc, "Outcome Testing Team Manager", buId);

    string[] caseTables =
    {
        "al_outcomecase", "al_reviewinstance", "al_response", "al_remediationaction",
        "al_signoff", "al_outcome", "al_caseassignment",
    };
    foreach (var table in caseTables)
    {
        GrantTable(svc, role, table, read: true, create: true, write: true, append: true, appendTo: true,
            depth: PrivilegeDepth.Basic);
    }

    // al_AssignCase changes the review's owner (AssignCasePlugin.StampReviewInstance).
    GrantTable(svc, role, "al_reviewinstance", read: true, create: true, write: true, append: true, appendTo: true,
        depth: PrivilegeDepth.Basic);
    GrantAssign(svc, role, "al_reviewinstance", PrivilegeDepth.Basic);

    // Create-only and own rows: an audit event that can be edited is not an audit trail.
    GrantTable(svc, role, "al_auditevent", read: true, create: true, append: true, appendTo: true, depth: PrivilegeDepth.Basic);
    GrantTable(svc, role, "al_notification", read: true, create: true, append: true, appendTo: true, depth: PrivilegeDepth.Basic);

    string[] configuration =
    {
        "al_failreason", "al_section", "al_question", "al_questionversion", "al_role",
        "al_checklist", "al_checklistversion", "al_reviewroute", "al_listoption",
        "al_userrolemapping", "al_pagepermission", "role", "powerpagecomponent",
    };
    foreach (var table in configuration)
    {
        GrantTable(svc, role, table, read: true, appendTo: true);
    }

    GrantTable(svc, role, "contact", read: true, appendTo: true);
    GrantTable(svc, role, "systemuser", read: true, appendTo: true);
    GrantTable(svc, role, "mspp_webrole", read: true, appendTo: true);

    AddRoleToSolution(svc, role, "OutcomeTesting");
    Console.WriteLine("Done. Role ready: 'Outcome Testing Team Manager'. Map the two team managers to it alongside Basic User; do not also give them 'Outcome Testing App User', which reads every case.");
    return 0;
}

static void GrantAssign(ServiceClient svc, Guid roleId, string table, PrivilegeDepth depth)
{
    var response = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = table,
        EntityFilters = EntityFilters.Privileges,
    });

    var assign = response.EntityMetadata.Privileges.FirstOrDefault(p => p.PrivilegeType == PrivilegeType.Assign);
    if (assign == null)
    {
        Console.Error.WriteLine($"  SKIPPED assign on {table}: the table carries no Assign privilege.");
        return;
    }

    svc.Execute(new AddPrivilegesRoleRequest
    {
        RoleId = roleId,
        Privileges = new[] { new RolePrivilege { PrivilegeId = assign.PrivilegeId, Depth = depth } },
    });
}
```

Read `GrantTable`'s own body (lines ~7580-7640). If it adds privileges with a request other than `AddPrivilegesRoleRequest`, `GrantAssign` must use the same request.

- [ ] **Step 7: Write `ReconcileAccess`**

```csharp
// AD-218: the backfill. Calls al_ReconcileCaseAccess for every active case, so the server
// decides exactly as the step does. A dry run lists how many cases would be asked; --confirm
// asks them and prints each case whose access changed and every case released to an
// adviser or supervisor nobody could be matched to.
int ReconcileAccess(string[] a)
{
    var orgUrl = a[1];
    var confirm = ConfirmedFor(a, orgUrl);
    using var svc = Connect(orgUrl);

    var cases = new List<Entity>();
    var query = new QueryExpression("al_outcomecase")
    {
        ColumnSet = new ColumnSet("al_casereference"),
        Criteria = { Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) } },
        PageInfo = new PagingInfo { Count = 500, PageNumber = 1 },
    };
    while (true)
    {
        var page = svc.RetrieveMultiple(query);
        cases.AddRange(page.Entities);
        if (!page.MoreRecords) break;
        query.PageInfo.PageNumber++;
        query.PageInfo.PagingCookie = page.PagingCookie;
    }

    Console.WriteLine($"{cases.Count} active case(s).");
    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run as: reconcileaccess <orgUrl> --confirm <orgUrl>");
        return 0;
    }

    int changed = 0, unmatched = 0, failed = 0;
    foreach (var row in cases)
    {
        var reference = row.GetAttributeValue<string>("al_casereference") ?? row.Id.ToString("D");
        try
        {
            var response = svc.Execute(new OrganizationRequest("al_ReconcileCaseAccess") { ["TargetId"] = row.Id.ToString("D") });
            var columns = response.Results.Contains("Changed") ? response.Results["Changed"] as string : null;
            var adviserUnmatched = response.Results.Contains("AdviserUnmatched") && (bool)response.Results["AdviserUnmatched"];
            var supervisorUnmatched = response.Results.Contains("SupervisorUnmatched") && (bool)response.Results["SupervisorUnmatched"];

            if (!string.IsNullOrEmpty(columns))
            {
                changed++;
                Console.WriteLine($"  changed   {reference,-20} {columns}");
            }

            if (adviserUnmatched || supervisorUnmatched)
            {
                unmatched++;
                Console.WriteLine($"  UNMATCHED {reference,-20} {(adviserUnmatched ? "adviser " : "")}{(supervisorUnmatched ? "supervisor" : "")}");
            }
        }
        catch (Exception error)
        {
            failed++;
            Console.Error.WriteLine($"  FAILED    {reference,-20} {error.Message.Split('\n')[0].Trim()}");
        }
    }

    Console.WriteLine($"Done. {changed} changed, {unmatched} released with nobody matched, {failed} failed.");
    return failed == 0 ? 0 : 1;
}
```

- [ ] **Step 8: Document the verbs in the header comment**

Add four entries to the header block, in the existing format:

```csharp
// Cascade sharing: dotnet run -- setcascade <orgUrl> <relationshipSchemaName> [--reparent-only] --confirm <orgUrl>
//   Sets Share, Unshare and Reparent to Cascade on a one-to-many (AD-218), so a case shared
//   with a team shares its reviews, answers, actions, outcome, sign-offs and assignments.
//
// Access principals: dotnet run -- ensureaccessprincipals <orgUrl> --confirm <orgUrl>
//   Creates the Tax and AQS owner teams and the AQS Team account the reconciler looks up by
//   name (AD-218). Idempotent; refuses a duplicated name.
//
// Team manager role: dotnet run -- grantteamsecurity <orgUrl>
//   Creates "Outcome Testing Team Manager": Basic depth on case tables, Global on
//   configuration (AD-218). Mapping people to it is the project owner's.
//
// Backfill access: dotnet run -- reconcileaccess <orgUrl> [--confirm <orgUrl>]
//   Calls al_ReconcileCaseAccess on every active case and reports unmatched advisers.
```

- [ ] **Step 9: Build**

Run: `dotnet build plugins/OutcomeTesting.Registration`
Expected: build succeeds with no new warnings about the added code.

- [ ] **Step 10: Commit**

```bash
git add plugins/OutcomeTesting.Registration/Program.cs
git commit -m "feat(registration): cascade, access principals, the team manager role and the access backfill (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Portal permissions, the additive half

**Files:**
- Create: 25 files under `powerpages/outcome-testing---outcometesting/table-permissions/` (listed below)
- Test: `app/src/app/permissions/portalTablePermissions.test.ts`

**Interfaces:**
- Consumes the lookup relationships Task 12 creates. `addlookupcolumn` names them `<target>_<logicalname>_<entity>`:
  - `contact_al_taxcheckercontactid_al_outcomecase`
  - `contact_al_aqscheckercontactid_al_outcomecase`
  - `contact_al_advisercontactid_al_outcomecase`
  - `contact_al_tcsupervisorcontactid_al_outcomecase`
  - `account_al_aqsqueueaccountid_al_outcomecase`
- Existing child relationships: `al_outcomecase_reviewinstance`, `al_reviewinstance_response`, `al_outcomecase_outcome`, `al_outcomecase_remediationaction`, `al_outcomecase_signoff`.
- Role ids: Tax Reviewer `...0090`, AQS Reviewer `...0091`, Adviser `...0092`, T&C Supervisor `...0093`.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, expect, it } from 'vitest';

/**
 * The portal's case boundary as the table permissions draw it (AD-218, AR-02 to AR-04).
 * Read as YAML text, because nothing else checks these files before they are written to
 * the environment - and a wrong scope here is a leak, not a bug.
 */
const files = import.meta.glob(
  '../../../../powerpages/outcome-testing---outcometesting/table-permissions/*.tablepermission.yml',
  { query: '?raw', import: 'default', eager: true },
) as Record<string, string>;

function field(yaml: string, key: string): string | null {
  const match = new RegExp(`^${key}:\\s*(.*)$`, 'm').exec(yaml);
  return match ? match[1].trim() : null;
}

// \r?\n throughout: a Windows checkout may carry CRLF, and a regex written for \n alone
// would read every role list as empty - which passes a "no Planner" test for the wrong reason.
function roles(yaml: string): string[] {
  const block = /adx_entitypermission_webrole:\r?\n((?:- .*(?:\r?\n|$))*)/.exec(yaml);
  return block
    ? block[1].split(/\r?\n/).filter((line) => line.trim()).map((line) => line.replace(/^- /, '').trim())
    : [];
}

const byName = new Map(Object.values(files).map((yaml) => [field(yaml, 'adx_entityname'), yaml]));

const ROLE = {
  tax: 'a1000000-0000-4000-8000-000000000090',
  aqs: 'a1000000-0000-4000-8000-000000000091',
  adviser: 'a1000000-0000-4000-8000-000000000092',
  supervisor: 'a1000000-0000-4000-8000-000000000093',
};

const PARENTS: [string, string, string, string][] = [
  ['Case - my Tax checks', 'contact_al_taxcheckercontactid_al_outcomecase', '756150001', ROLE.tax],
  ['Case - my AQS checks', 'contact_al_aqscheckercontactid_al_outcomecase', '756150001', ROLE.aqs],
  ['Case - my clients', 'contact_al_advisercontactid_al_outcomecase', '756150001', ROLE.adviser],
  ['Case - advisers I supervise', 'contact_al_tcsupervisorcontactid_al_outcomecase', '756150001', ROLE.supervisor],
];

describe('each role reads cases through its own column', () => {
  it.each(PARENTS)('%s is Contact-scoped, read-only, on its relationship', (name, relationship, scope, role) => {
    const yaml = byName.get(name);
    expect(yaml, name).toBeDefined();
    expect(field(yaml!, 'adx_entitylogicalname')).toBe('al_outcomecase');
    expect(field(yaml!, 'adx_scope')).toBe(scope);
    expect(field(yaml!, 'adx_contactrelationship')).toBe(relationship);
    expect(field(yaml!, 'adx_read')).toBe('true');
    expect(field(yaml!, 'adx_write')).toBe('false');
    expect(roles(yaml!)).toEqual([role]);
  });

  it('the AQS queue is Account-scoped and has no children', () => {
    const yaml = byName.get('Case - AQS queue');
    expect(yaml).toBeDefined();
    expect(field(yaml!, 'adx_scope')).toBe('756150002');
    expect(field(yaml!, 'adx_accountrelationship')).toBe('account_al_aqsqueueaccountid_al_outcomecase');
    expect(roles(yaml!)).toEqual([ROLE.aqs]);

    const id = field(yaml!, 'adx_entitypermissionid');
    const children = Object.values(files).filter((other) => field(other, 'adx_parententitypermission') === id);
    expect(children).toEqual([]);
  });

  it.each(PARENTS)('%s passes read down to the case detail, and nothing more', (name) => {
    const parentId = field(byName.get(name)!, 'adx_entitypermissionid');
    const children = Object.values(files).filter((yaml) => field(yaml, 'adx_parententitypermission') === parentId);
    const tables = children.map((yaml) => field(yaml, 'adx_entitylogicalname')).sort();
    expect(tables).toEqual(['al_outcome', 'al_remediationaction', 'al_reviewinstance', 'al_signoff']);
    for (const child of children) {
      expect(field(child, 'adx_scope')).toBe('756150003');
      expect(field(child, 'adx_write')).toBe('false');
      expect(field(child, 'adx_create')).toBe('false');
    }

    const review = children.find((yaml) => field(yaml, 'adx_entitylogicalname') === 'al_reviewinstance')!;
    const reviewId = field(review, 'adx_entitypermissionid');
    const answers = Object.values(files).filter((yaml) => field(yaml, 'adx_parententitypermission') === reviewId);
    expect(answers.map((yaml) => field(yaml, 'adx_entitylogicalname'))).toEqual(['al_response']);
  });

  it('every new id is in the AD-218 band', () => {
    for (const yaml of Object.values(files)) {
      const name = field(yaml, 'adx_entityname') ?? '';
      if (!name.startsWith('Case - ')) continue;
      expect(field(yaml, 'adx_entitypermissionid')).toMatch(/^a1000000-0000-4000-8000-0000000000[cd][0-9a-f]$/);
    }
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd app; npx vitest run src/app/permissions/portalTablePermissions.test.ts`
Expected: FAIL, with "Case - my Tax checks" undefined.

- [ ] **Step 3: Write the five parent permissions**

Use ids `c0` to `c4`, in this format. `Case-My-Tax-Checks.tablepermission.yml`:

```yaml
# AD-218, AR-02. A Tax specialist reads the cases allocated to them, through the lookup
# CaseAccessReconciler keeps on the case. Reallocation replaces the lookup, so the previous
# checker loses the case. Read-only: answering is Review Instance - assigned to me.
adx_append: false
adx_appendto: false
adx_contactrelationship: contact_al_taxcheckercontactid_al_outcomecase
adx_create: false
adx_delete: false
adx_entitylogicalname: al_outcomecase
adx_entityname: Case - my Tax checks
adx_entitypermission_webrole:
- a1000000-0000-4000-8000-000000000090
adx_entitypermissionid: a1000000-0000-4000-8000-0000000000c0
adx_read: true
adx_scope: 756150001
adx_write: false
```

The other four parents:

| File | adx_entityname | id | relationship key | relationship | role |
|---|---|---|---|---|---|
| `Case-My-AQS-Checks` | Case - my AQS checks | `c1` | adx_contactrelationship | contact_al_aqscheckercontactid_al_outcomecase | `...0091` |
| `Case-AQS-Queue` | Case - AQS queue | `c2` | adx_accountrelationship | account_al_aqsqueueaccountid_al_outcomecase | `...0091` |
| `Case-My-Clients` | Case - my clients | `c3` | adx_contactrelationship | contact_al_advisercontactid_al_outcomecase | `...0092` |
| `Case-Advisers-I-Supervise` | Case - advisers I supervise | `c4` | adx_contactrelationship | contact_al_tcsupervisorcontactid_al_outcomecase | `...0093` |

`Case-AQS-Queue` has `adx_scope: 756150002`. Its comment says: "AR-03. The summary row of a case waiting for AQS, and nothing under it: no child permission, so a reviewer cannot read a queued case's answers until they take it."

- [ ] **Step 4: Write the 20 child permissions**

Each Contact parent gets five children. The review child also has an answer child under it, so that's four direct children plus one grandchild. Ids run `c5` to `d8`. Template, `Case-My-Tax-Checks-Reviews.tablepermission.yml`:

```yaml
adx_append: false
adx_appendto: false
adx_create: false
adx_delete: false
adx_entitylogicalname: al_reviewinstance
adx_entityname: Case - my Tax checks / reviews
adx_entitypermission_webrole:
- a1000000-0000-4000-8000-000000000090
adx_entitypermissionid: a1000000-0000-4000-8000-0000000000c5
adx_parententitypermission: a1000000-0000-4000-8000-0000000000c0
adx_parentrelationship: al_outcomecase_reviewinstance
adx_read: true
adx_scope: 756150003
adx_write: false
```

| Parent (id) | Child suffix | Table | Parent relationship | Its parent id | New id |
|---|---|---|---|---|---|
| my Tax checks (c0) | reviews | al_reviewinstance | al_outcomecase_reviewinstance | c0 | c5 |
| | answers | al_response | al_reviewinstance_response | c5 | c6 |
| | outcome | al_outcome | al_outcomecase_outcome | c0 | c7 |
| | remedial actions | al_remediationaction | al_outcomecase_remediationaction | c0 | c8 |
| | sign-offs | al_signoff | al_outcomecase_signoff | c0 | c9 |
| my AQS checks (c1) | reviews / answers / outcome / remedial actions / sign-offs | same | same | c1, then ca for answers | ca, cb, cc, cd, ce |
| my clients (c3) | same five | same | same | c3, then cf for answers | cf, d0, d1, d2, d3 |
| advisers I supervise (c4) | same five | same | same | c4, then d4 for answers | d4, d5, d6, d7, d8 |

In each row, the answers child's parent is that block's review child: `c5`, `ca`, `cf` or `d4`. Every child lists exactly the one role its parent lists. The `adx_entityname` is `<parent name> / <child suffix>`.

- [ ] **Step 5: Run the test and the id check**

Run: `cd app; npx vitest run src/app/permissions/portalTablePermissions.test.ts`
Expected: PASS.

Run: `powershell -File powerpages/Check-ComponentIds.ps1`
Expected: no collisions.

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/table-permissions app/src/app/permissions/portalTablePermissions.test.ts
git commit -m "feat(portal): read cases through each role's own column (AD-218), added alongside Global read

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Portal permissions, the restricting half, and Planner

**Files:**
- Modify: `table-permissions/Outcome-Case-All-Read.tablepermission.yml`, `Review-Instance-All-Read.tablepermission.yml`, `Response-All-Read.tablepermission.yml`, `Remediation-Action---read-all.tablepermission.yml`, `Outcome-All-Read.tablepermission.yml`, `Remediation-Action---assigned-to-me.tablepermission.yml`
- Modify: `powerpages/outcome-testing---outcometesting/webpagerule.yml` (remove `...0097` from every rule)
- Test: extend `app/src/app/permissions/portalTablePermissions.test.ts`

**Interfaces:** consumes the Task 7 permissions. Produces a site where Global read is held only by Administrators (`c53b2908-1fc1-4470-89cd-6f5b95c17ffe`) and Outcome Testing Manager (`...0095`).

- [ ] **Step 1: Add the failing tests**

Append to `portalTablePermissions.test.ts`:

```ts
const ADMINISTRATORS = 'c53b2908-1fc1-4470-89cd-6f5b95c17ffe';
const OT_MANAGER = 'a1000000-0000-4000-8000-000000000095';
const PLANNER = 'a1000000-0000-4000-8000-000000000097';
const AUTHENTICATED = 'e24b50c5-1443-4725-84c9-70355724547f';

describe('Global read is held by oversight roles only (supersedes OD-022, AD-056)', () => {
  const CASE_DATA = ['al_outcomecase', 'al_reviewinstance', 'al_response', 'al_remediationaction', 'al_outcome', 'al_signoff'];

  it('no Global read on case data reaches a reviewer, adviser, supervisor or planner', () => {
    for (const yaml of Object.values(files)) {
      if (field(yaml, 'adx_scope') !== '756150000') continue;
      if (!CASE_DATA.includes(field(yaml, 'adx_entitylogicalname') ?? '')) continue;
      expect(roles(yaml).sort(), field(yaml, 'adx_entityname') ?? '').toEqual([OT_MANAGER, ADMINISTRATORS].sort());
    }
  });

  it('outcomes are no longer readable by every signed-in user', () => {
    for (const yaml of Object.values(files)) {
      if (field(yaml, 'adx_entitylogicalname') !== 'al_outcome') continue;
      expect(roles(yaml)).not.toContain(AUTHENTICATED);
    }
  });

  it('the Planner role is bound to nothing (answer 3)', () => {
    for (const yaml of Object.values(files)) {
      expect(roles(yaml), field(yaml, 'adx_entityname') ?? '').not.toContain(PLANNER);
    }
  });
});
```

Also import the page rules with `import pageRules from '../../../../powerpages/outcome-testing---outcometesting/webpagerule.yml?raw';` and add:

```ts
  it('no page rule admits the Planner role', () => {
    expect(pageRules).not.toContain(PLANNER);
  });
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd app; npx vitest run src/app/permissions/portalTablePermissions.test.ts`
Expected: FAIL. `Outcome Case - read all` lists `...0090`, and Planner appears in several files.

- [ ] **Step 3: Edit the permissions**

- In `Outcome-Case-All-Read`, `Review-Instance-All-Read`, `Response-All-Read` and `Remediation-Action---read-all`: set `adx_entitypermission_webrole` to exactly `c53b2908-1fc1-4470-89cd-6f5b95c17ffe` and `a1000000-0000-4000-8000-000000000095`. Add a first-line comment: `# AD-218: Global read is oversight only. Supersedes OD-022 and AD-056; each other role reads through its own Case - permission.`
- In `Outcome-All-Read`: replace the `e24b50c5-...` (Authenticated Users) entry with `a1000000-0000-4000-8000-000000000095`, and add the same comment.
- In `Remediation-Action---assigned-to-me`: remove `a1000000-0000-4000-8000-000000000097`.
- In `webpagerule.yml`: delete every `- a1000000-0000-4000-8000-000000000097` line.

- [ ] **Step 4: Run the tests and the id check**

Run: `cd app; npx vitest run src/app/permissions/portalTablePermissions.test.ts; powershell -File ../powerpages/Check-ComponentIds.ps1`
Expected: PASS, and no collisions.

- [ ] **Step 5: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/table-permissions powerpages/outcome-testing---outcometesting/webpagerule.yml app/src/app/permissions/portalTablePermissions.test.ts
git commit -m "feat(portal): Global read for oversight roles only, and Planner bound to nothing (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Portal pages

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html`
- Modify: `web-templates/ot-remediation/OT-Remediation.webtemplate.source.html:77-236` (the list branch)
- Modify: `web-templates/ot-home/OT-Home.webtemplate.source.html:7-9` (the caption comment) and the visible heading over the outcome cards
- Test: `app/src/features/reviews/portalAccessPages.test.ts`

**Interfaces:** consumes the columns `al_aqsqueueaccountid` and `al_aqsqueuedon` on `al_outcomecase`, the route flags on `al_reviewroute`, and the Task 7 permissions.

- [ ] **Step 1: Write the failing tests**

```ts
import { describe, expect, it } from 'vitest';
import reviewList from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html?raw';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';

/** The portal pages under role-scoped access (AD-218, AR-02, AR-03, AR-04). */
describe('the review list', () => {
  it('lists only the signed-in reviewer\'s own reviews, with no "all" option', () => {
    expect(reviewList).toContain('<condition attribute="al_assignedcontactid" operator="eq" value="{{ user.id | xml_escape }}" />');
    expect(reviewList).not.toMatch(/f_mine == '1' %\}<condition attribute="al_assignedcontactid"/);
    expect(reviewList).not.toContain('>All {{ heading | downcase }}</a>');
  });

  it('reads the AQS queue from the queue column, not by joining reviews it cannot read', () => {
    const queue = /\{% fetchxml available %\}([\s\S]*?)\{% endfetchxml %\}/.exec(reviewList)?.[1] ?? '';
    expect(queue).toContain('<condition attribute="al_aqsqueueaccountid" operator="not-null" />');
    expect(queue).not.toContain('al_reviewinstance');
    expect(queue).toContain('<order attribute="al_aqsqueuedon" />');
  });

  it('names the five AQS groups AR-03 lists', () => {
    for (const heading of [
      'Tax review completed – awaiting allocation',
      'New – awaiting allocation',
      'Allocated to me',
      'In progress',
      'Completed',
    ]) {
      expect(reviewList).toContain(heading);
    }
  });

  it('shows both ages in working days', () => {
    expect(reviewList).toContain('Days in OTIS');
    expect(reviewList).toContain('Waiting for AQS');
    expect(reviewList).toContain('data-ot-workdays');
  });
});

describe('the remediation list', () => {
  it('offers the three adviser stages', () => {
    for (const label of ['Remedial action required', 'Awaiting T&amp;C sign-off', 'Closed']) {
      expect(remediation).toContain(label);
    }
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd app; npx vitest run src/features/reviews/portalAccessPages.test.ts`
Expected: FAIL on every assertion.

- [ ] **Step 3: Rewrite the review list's scope and queue**

In `OT-Review-List.webtemplate.source.html`:

1. **Scope.** Replace the comment block and assignments at lines 19-38 (`f_mine`) with:

   ```liquid
   {% comment %}
     Scope (AD-218, AR-02, AR-03). A reviewer's list is their own reviews and nothing else.
     This is presentation; the boundary is the table permission, which since AD-218 gives a
     reviewer read on their own cases and the AQS queue only. So "All" is gone: it would
     have listed exactly the same rows under a label that promised more.
   {% endcomment %}
   ```

   In the `reviews` fetch, replace line 92 with an unconditional `<condition attribute="al_assignedcontactid" operator="eq" value="{{ user.id | xml_escape }}" />`. Wrap the whole page body in `{% if user %} ... {% endif %}`, keeping the anonymous rule. Remove `mine=1&amp;` from `scope_qs`, remove the hidden `mine` input, and delete the `<nav class="ot-scope">` block (lines 312-320). The heading becomes `{{ heading | escape }} allocated to you`.

2. **Queue fetch.** Replace the `available` fetch (lines 165-223) with:

   ```liquid
   {% fetchxml available %}
   <fetch count="50">
     <entity name="al_outcomecase">
       <attribute name="al_outcomecaseid" />
       <attribute name="al_casereference" />
       <attribute name="al_clientname" />
       <attribute name="al_advisername" />
       <attribute name="al_duedate" />
       <attribute name="al_aqsqueuedon" />
       <attribute name="createdon" />
       <filter type="and">
         <condition attribute="statecode" operator="eq" value="0" />
         <condition attribute="al_aqsqueueaccountid" operator="not-null" />
       </filter>
       <link-entity name="al_reviewroute" from="al_reviewrouteid" to="al_reviewrouteid" link-type="outer" alias="route">
         <attribute name="al_requirestaxreview" />
       </link-entity>
       <order attribute="al_aqsqueuedon" />
     </entity>
   </fetch>
   {% endfetchxml %}
   ```

   Replace the comment above it with: "The queue is what CaseAccessReconciler put there (AD-218): the case carries the AQS Team account while it waits, and the Case - AQS queue permission is how a reviewer reads it. Ordered by the time it joined, oldest first (AR-03). No review is joined: the queue permission deliberately has no children, and a queued Tax-then-AQS case is exactly one whose route requires Tax, because the reconciler queues it only once its Tax check is in."

3. **Two queue groups.** Replace the single `Available cases` card (lines 227-294) with two cards drawn from one loop helper. Split `unassigned` into `tax_done` (where `c['route.al_requirestaxreview'] == true`) and `fresh` (otherwise), in that order, with `{% for %}` and `{% capture %}` building id lists. Give each card:
   - The heading: `Tax review completed – awaiting allocation` (a "Tax reviewed" badge on each row, `<span class="ot-badge">Tax reviewed</span>`), or `New – awaiting allocation`.
   - The columns: Case reference, Client, Adviser, Days in OTIS, Waiting for AQS, Due, and the claim button when `can_claim`.
   - The age cells, as `<span data-ot-workdays data-from="{{ c.createdon | date: 'yyyy-MM-dd' }}">—</span>` for Days in OTIS and `<span data-ot-workdays data-from="{{ c.al_aqsqueuedon | date: 'yyyy-MM-dd' }}">—</span>` for Waiting for AQS.
   - The existing empty-state include, with the message suited to the group.

4. **Three AQS "mine" groups.** When `is_aqs_queue` is true, draw the `rows` table three times with a status filter in the loop:
   - `Allocated to me` where `r.al_reviewstatus.value == 120910210`
   - `In progress` where `r.al_reviewstatus.value == 120910211`
   - `Completed` where `r.al_reviewstatus.value == 120910212`

   For the Tax page, keep the single table, captioned "Allocated to you".

5. **Working-day script.** Add one script at the end, copying the mid-day UTC arithmetic from `OT-Remediation`'s `workingDaysBetween` (lines 1360-1377):

   ```html
   <script>
     /* Working-day ages for the AQS queue (AR-03), on the same calendar as the remediation
        clock: Monday to Friday, the first day counts, bank holidays not deducted. */
     (function () {
       'use strict';
       function midday(text) {
         var parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(text || '');
         return parts ? new Date(Date.UTC(+parts[1], +parts[2] - 1, +parts[3], 12, 0, 0)) : null;
       }
       function workingDaysBetween(from, to) {
         if (to.getTime() < from.getTime()) { return 0; }
         var count = 0;
         var cursor = new Date(from.getTime());
         while (cursor.getTime() <= to.getTime()) {
           var day = cursor.getUTCDay();
           if (day >= 1 && day <= 5) { count += 1; }
           cursor.setUTCDate(cursor.getUTCDate() + 1);
         }
         return count;
       }
       var now = new Date();
       var today = new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate(), 12, 0, 0));
       var cells = document.querySelectorAll('[data-ot-workdays]');
       for (var i = 0; i < cells.length; i += 1) {
         var from = midday(cells[i].getAttribute('data-from'));
         if (!from) { continue; }
         var age = workingDaysBetween(from, today);
         cells[i].textContent = age + (age === 1 ? ' working day' : ' working days');
       }
     })();
   </script>
   ```

- [ ] **Step 4: Add the adviser stages to the remediation list**

In the list branch of `OT-Remediation`, add `{% assign f_stage = request.params['stage'] | default: '' %}` and validate it against `open`, `signoff` and `closed`, as the file already does for its other parameters. Add a condition inside `remcases` after the `f_ref` filter:

```liquid
      {% if f_stage == 'open' %}<filter type="or"><condition attribute="al_casestatus" operator="eq" value="120910587" /><condition attribute="al_casestatus" operator="eq" value="120910588" /></filter>{% endif %}
      {% if f_stage == 'signoff' %}<filter type="and"><condition attribute="al_casestatus" operator="eq" value="120910589" /></filter>{% endif %}
      {% if f_stage == 'closed' %}<filter type="or"><condition attribute="al_casestatus" operator="eq" value="120910590" /><condition attribute="al_casestatus" operator="eq" value="120910591" /></filter>{% endif %}
```

Above the search form, add a `<nav class="ot-scope" aria-label="Remediation stage">` with four links: All, `Remedial action required` (`?stage=open`), `Awaiting T&amp;C sign-off` (`?stage=signoff`) and `Closed` (`?stage=closed`). Mark the current one with `aria-current="page"` and `ot-btn--primary`, as the review list's scope links did. Carry `stage` in the pager links and the search form's hidden input.

- [ ] **Step 5: Change the Home caption**

In `OT-Home`, change the comment (lines 7-9) to say the outcome cards count the cases the viewer can see (AD-218, superseding AD-083's organisation-wide count). Change the visible heading over the cards to `Outcomes on your cases`, and any caption reading "all cases" to "cases you can see".

- [ ] **Step 6: Run the tests**

Run: `cd app; npx vitest run src/features/reviews`
Expected: `portalAccessPages.test.ts` passes, and the existing `portal*.test.ts` files still pass.

- [ ] **Step 7: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates app/src/features/reviews/portalAccessPages.test.ts
git commit -m "feat(portal): own reviews only, the five AQS groups with working-day ages, and adviser stages (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Code App roles, allocation scope and the adviser flag

**Files:**
- Modify: `app/src/types/permissions.ts` (`APP_ROLES`, `RESOURCE_KEYS`, `DEFAULT_PERMISSIONS`, `pageResourceForPath`)
- Create: `app/src/features/cases/allocationScope.ts`
- Modify: `app/src/features/cases/CaseEditPanel.tsx` (the checker controls, ~lines 279-350 and 706-790)
- Modify: `app/src/features/cases/caseDetailMapping.ts` (`CaseDetail` and `toDetail`)
- Modify: `app/src/features/cases/CaseDetailPage.tsx:~197` (the notice)
- Test: `app/src/features/cases/allocationScope.test.ts`, `app/src/types/permissions.test.ts` (extend)

**Interfaces:**
- Consumes: `Discipline` from `caseCheckers.ts`, `usePermissions().roles`, `useRoleHolders(roleCode)` and `RoleHolderRecord.email`, `DirectoryUser`.
- Produces:
  - `allocatableDisciplines(roles: readonly string[]): Discipline[]`
  - `checkersFor(candidates: DirectoryUser[], holders: RoleHolderRecord[] | null): DirectoryUser[]`
  - `REVIEWER_ROLE: Record<Discipline, string>`
  - `TAX_TEAM_MANAGER`, `AQS_TEAM_MANAGER`
  - `adviserUnmatched(statusValue: number, adviserContactId: string | null): boolean`
  - the resource key `'page.workload'`

- [ ] **Step 1: Write the failing tests**

`app/src/features/cases/allocationScope.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import {
  AQS_TEAM_MANAGER,
  TAX_TEAM_MANAGER,
  adviserUnmatched,
  allocatableDisciplines,
  checkersFor,
} from './allocationScope';
import type { DirectoryUser } from '../../hooks/useUserDirectory';

/** Mirrors AllocationScope in the plug-in assembly (AD-218). The command is the authority. */
describe('allocatableDisciplines', () => {
  it.each([
    [[TAX_TEAM_MANAGER], ['Tax']],
    [[AQS_TEAM_MANAGER], ['AQS']],
    [['AL Portal - Outcome Testing Manager'], ['Tax', 'AQS']],
    [['Administrators'], ['Tax', 'AQS']],
    [['AL Portal - T&C Supervisor'], []],
    [[TAX_TEAM_MANAGER, AQS_TEAM_MANAGER], ['Tax', 'AQS']],
  ])('%j allocates %j', (roles, expected) => {
    expect(allocatableDisciplines(roles)).toEqual(expected);
  });

  it('matches role names whatever their case and spacing', () => {
    expect(allocatableDisciplines(['  al portal - tax team manager '])).toEqual(['Tax']);
  });
});

describe('checkersFor', () => {
  const person = (email: string): DirectoryUser => ({
    id: email, name: email, email, active: true, createdOn: null, rowVersion: null, staffCode: null,
  });

  it('offers only people holding the discipline\'s reviewer role', () => {
    const holders = [{ email: 'TAX@example.com', name: null, mappingId: null, mappingActive: null, associated: true }];
    expect(checkersFor([person('tax@example.com'), person('aqs@example.com')], holders).map((p) => p.email))
      .toEqual(['tax@example.com']);
  });

  it('offers everyone while the holders are not known, since the server still refuses', () => {
    expect(checkersFor([person('a@example.com')], null)).toHaveLength(1);
  });
});

describe('adviserUnmatched', () => {
  it('flags a case in remediation with nobody to release it to', () => {
    expect(adviserUnmatched(120910587, null)).toBe(true);
  });

  it('does not flag a matched case, a closed case or a case still in review', () => {
    expect(adviserUnmatched(120910587, 'x')).toBe(false);
    expect(adviserUnmatched(120910591, null)).toBe(false);
    expect(adviserUnmatched(120910584, null)).toBe(false);
  });
});
```

Extend `app/src/types/permissions.test.ts`:

```ts
describe('AD-218 roles', () => {
  it('each team manager can open the workload page and allocate', () => {
    for (const role of ['AL Portal - Tax Team Manager', 'AL Portal - AQS Team Manager']) {
      const set = resolvePermissions([role]);
      expect(can(set, 'page.workload')).toBe(true);
      expect(can(set, 'command.assign', 'Edit')).toBe(true);
      expect(can(set, 'page.cases', 'Edit')).toBe(true);
    }
  });

  it('the T&C Supervisor no longer allocates (answer 4a)', () => {
    expect(can(resolvePermissions(['AL Portal - T&C Supervisor']), 'command.assign', 'Edit')).toBe(false);
  });

  it('a Planner reaches nothing in the app (answer 3)', () => {
    const set = resolvePermissions(['AL Portal - Planner']);
    expect(RESOURCE_KEYS.filter((key) => can(set, key))).toEqual([]);
  });

  it('maps /workload to page.workload', () => {
    expect(pageResourceForPath('/workload')).toBe('page.workload');
  });
});
```

Add whichever of `resolvePermissions`, `can`, `pageResourceForPath` and `RESOURCE_KEYS` are missing to that file's imports.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd app; npx vitest run src/features/cases/allocationScope.test.ts src/types/permissions.test.ts`
Expected: FAIL. The module does not exist, and `page.workload` is not a key.

- [ ] **Step 3: Write `allocationScope.ts`**

```ts
import type { Discipline } from './caseCheckers';
import type { DirectoryUser } from '../../hooks/useUserDirectory';
import type { RoleHolderRecord } from '../admin/roleDetail';

/**
 * Who may allocate which check, mirrored from AllocationScope in the plug-in assembly
 * (AD-218; closes OD-029(c)). al_AssignCase is the authority and refuses what this would
 * not offer; the mirror only stops the edit modal proposing a refusal.
 */
export const TAX_TEAM_MANAGER = 'AL Portal - Tax Team Manager';
export const AQS_TEAM_MANAGER = 'AL Portal - AQS Team Manager';
const BOTH = ['AL Portal - Outcome Testing Manager', 'Administrators'];

export const REVIEWER_ROLE: Record<Discipline, string> = {
  Tax: 'AL Portal - Tax Reviewer',
  AQS: 'AL Portal - AQS Reviewer',
};

function normalise(role: string): string {
  return role.trim().toLowerCase();
}

export function allocatableDisciplines(roles: readonly string[]): Discipline[] {
  const held = new Set(roles.map(normalise));
  if (BOTH.some((role) => held.has(normalise(role)))) return ['Tax', 'AQS'];

  const out: Discipline[] = [];
  if (held.has(normalise(TAX_TEAM_MANAGER))) out.push('Tax');
  if (held.has(normalise(AQS_TEAM_MANAGER))) out.push('AQS');
  return out;
}

/**
 * The people a check may go to: holders of the discipline's reviewer role. Everyone while
 * the holders are unknown, because withholding the list would stop a manager working and
 * the server refuses a non-holder either way.
 */
export function checkersFor(candidates: DirectoryUser[], holders: RoleHolderRecord[] | null): DirectoryUser[] {
  if (!holders) return candidates;
  const emails = new Set(holders.map((h) => h.email.trim().toLowerCase()));
  return candidates.filter((person) => emails.has(person.email.trim().toLowerCase()));
}

/** Awaiting Remediation, Remediation In Progress, Awaiting Sign-off, Awaiting Recheck. */
const RELEASE_OPEN = new Set([120910587, 120910588, 120910589, 120910590]);

/**
 * A case in remediation that nobody could be released to (AD-218): the adviser name matched
 * no single contact, so no adviser can see it. Closed is excluded because a Pass case closes
 * without ever being released.
 */
export function adviserUnmatched(statusValue: number, adviserContactId: string | null): boolean {
  return RELEASE_OPEN.has(statusValue) && !adviserContactId;
}
```

- [ ] **Step 4: Update `permissions.ts`**

- Append `'AL Portal - Tax Team Manager'` and `'AL Portal - AQS Team Manager'` to `APP_ROLES`.
- Add `'page.workload'` to `RESOURCE_KEYS`, after `'page.exports'`.
- In `DEFAULT_PERMISSIONS`, delete `{ role: 'AL Portal - T&C Supervisor', resource: 'command.assign', level: 'Edit' }`. Delete the three `AL Portal - Planner` rules and the comment above them (answer 3: Planners keep their emails and have no access to the system), and exclude Planner from the `APP_ROLES.map(...)` spread that grants every role `page.dashboard`, with a comment citing answer 3. Planner stays in `APP_ROLES`: the role still exists, it just grants nothing. Add:

  ```ts
  // AD-218: each team manager sees their team's cases, allocates their own discipline, and
  // watches the team's workload. Their Dataverse security role is what scopes the rows.
  ...(['AL Portal - Tax Team Manager', 'AL Portal - AQS Team Manager'] as const).flatMap((role) => [
    { role, resource: 'page.cases' as ResourceKey, level: 'Edit' as AccessLevel },
    { role, resource: 'page.workload' as ResourceKey, level: 'View' as AccessLevel },
    { role, resource: 'command.assign' as ResourceKey, level: 'Edit' as AccessLevel },
  ]),
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.workload', level: 'View' },
  ```

- In `pageResourceForPath`, add `if (path.startsWith('/workload')) return 'page.workload';`.

- [ ] **Step 5: Use it in `CaseEditPanel`**

Next to `const { can } = usePermissions();` (~line 279), destructure `roles` too. Then:

```tsx
  const inScope = allocatableDisciplines(roles);
  const taxHolders = useRoleHolders(REVIEWER_ROLE.Tax);
  const aqsHolders = useRoleHolders(REVIEWER_ROLE.AQS);
  const holdersFor = (discipline: Discipline) => {
    const state = discipline === 'Tax' ? taxHolders : aqsHolders;
    return state.status === 'ready' ? state.holders : null;
  };
```

In the `owed.map` loop, after the "Submitted" branch and before the `isAllocatable` branch, add:

```tsx
                    // AD-218: another team's check is theirs to allocate. Stated, not offered.
                    if (!inScope.includes(discipline)) {
                      return (
                        <div key={discipline} className="case-edit__field">
                          <span>{discipline} Checker</span>
                          <p className="case-edit__only-check">{review ? held : 'Not open yet'}</p>
                          <small className="case-edit__help">
                            Allocated by the {discipline} team manager.
                          </small>
                        </div>
                      );
                    }
```

In the `<select>`, replace `candidates.map(...)` with `checkersFor(candidates, holdersFor(discipline)).map(...)`. Import `allocatableDisciplines`, `checkersFor` and `REVIEWER_ROLE` from `./allocationScope`, and `useRoleHolders` from `../admin/useRoleHolders`.

- [ ] **Step 6: Flag an unmatched adviser on the case page**

In `caseDetailMapping.ts`, add `adviserUnmatched: boolean;` to `CaseDetail`. In `toDetail`, widen `extra` with `_al_advisercontactid_value?: string`. The generator lags new columns, the same reason `useUserDirectory` widens `al_staffcode`. Then set:

```ts
    adviserUnmatched: adviserUnmatched(record.al_casestatus, extra._al_advisercontactid_value ?? null),
```

In `CaseDetailPage.tsx`, before the `previousCase` section:

```tsx
                    {state.detail.adviserUnmatched ? (
                      <p className="case-detail__notice" role="status">
                        Adviser not matched: no single person is named {state.detail.edit.al_advisername || 'as the adviser'},
                        so no adviser can see this case yet. Correct the adviser name, or the person's name on the People page.
                      </p>
                    ) : null}
```

Fix any test that builds a `CaseDetail` literal by adding `adviserUnmatched: false`.

- [ ] **Step 7: Run tests and type-check**

Run: `cd app; npx vitest run; npx tsc -b`
Expected: all pass, with no type errors. A test that pins the exact `APP_ROLES` list, or the set of resources a role reaches, needs the two roles or `page.workload` added. Update it deliberately and name it in the commit body.

- [ ] **Step 8: Commit**

```bash
git add app/src
git commit -m "feat(app): team manager roles, allocation offered by discipline, and the unmatched-adviser flag (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 11: Team workload page

**Files:**
- Create: `app/src/features/workload/workload.ts`, `workload.test.ts`, `useWorkload.ts`, `WorkloadPage.tsx`, `WorkloadPage.css`
- Modify: `app/src/app/router.tsx` (route), `app/src/app/navigation.ts` ("My work" group)

**Interfaces:**
- Consumes: `allocatableDisciplines` (Task 10), `toReview` and `CaseReview` from `features/cases/reviewRows.ts`, `workingDaysBetween(start: Date, end: Date): number` from `lib/workingDays.ts`, `Al_reviewinstancesService`, `Al_outcomecasesService`.
- Produces:
  - `summarise(reviews: WorkloadReview[], queued: number, now: Date): Workload`
  - `interface WorkloadReview { checker: string | null; status: string; startedOn: string | null; submittedOn: string | null; openedOn: string | null }`
  - `interface Workload { totals: { awaitingAllocation: number; allocated: number; inProgress: number; completedThisMonth: number }; checkers: CheckerLoad[] }`
  - `interface CheckerLoad { checker: string; allocated: number; inProgress: number; completedLast30Days: number; oldestOpenWorkingDays: number | null }`

- [ ] **Step 1: Write the failing tests**

```ts
import { describe, expect, it } from 'vitest';
import { summarise, type WorkloadReview } from './workload';

const NOW = new Date('2026-09-23T12:00:00Z');

function review(checker: string | null, status: string, extra: Partial<WorkloadReview> = {}): WorkloadReview {
  return { checker, status, startedOn: null, submittedOn: null, openedOn: '2026-09-21', ...extra };
}

describe('summarise (AR-01: individual and overall workloads)', () => {
  it('counts each checker\'s allocated, in-progress and recently completed checks', () => {
    const load = summarise([
      review('Ada', 'Assigned'),
      review('Ada', 'Review In Progress'),
      review('Ada', 'Submitted', { submittedOn: '2026-09-10' }),
      review('Bo', 'Submitted', { submittedOn: '2026-07-01' }),
    ], 0, NOW);

    const ada = load.checkers.find((c) => c.checker === 'Ada')!;
    expect(ada).toMatchObject({ allocated: 1, inProgress: 1, completedLast30Days: 1 });
    const bo = load.checkers.find((c) => c.checker === 'Bo')!;
    expect(bo.completedLast30Days).toBe(0);
  });

  it('ages the oldest open check in working days', () => {
    // Monday 21 to Wednesday 23 September is three working days, the first day counting.
    const load = summarise([review('Ada', 'Assigned', { openedOn: '2026-09-21' })], 0, NOW);
    expect(load.checkers[0].oldestOpenWorkingDays).toBe(3);
  });

  it('totals the team, including what is waiting to be allocated', () => {
    const load = summarise([
      review('Ada', 'Assigned'),
      review('Bo', 'Review In Progress'),
      review('Bo', 'Submitted', { submittedOn: '2026-09-02' }),
      review('Bo', 'Submitted', { submittedOn: '2026-08-29' }),
    ], 4, NOW);
    expect(load.totals).toEqual({ awaitingAllocation: 4, allocated: 1, inProgress: 1, completedThisMonth: 1 });
  });

  it('files a check with no holder under "Unassigned" rather than dropping it', () => {
    expect(summarise([review(null, 'Assigned')], 0, NOW).checkers[0].checker).toBe('Unassigned');
  });

  it('lists the busiest checker first', () => {
    const load = summarise([review('Bo', 'Assigned'), review('Ada', 'Assigned'), review('Ada', 'Assigned')], 0, NOW);
    expect(load.checkers.map((c) => c.checker)).toEqual(['Ada', 'Bo']);
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd app; npx vitest run src/features/workload`
Expected: FAIL. The module does not exist.

- [ ] **Step 3: Write `workload.ts`**

```ts
import { workingDaysBetween } from '../../lib/workingDays';

/**
 * A team's workload (AR-01: "monitor individual and overall Tax Specialist workloads").
 * Pure, over the reviews the manager's Dataverse share already limits to their team
 * (AD-218), so this filters nothing by team itself.
 */
export interface WorkloadReview {
  checker: string | null;
  status: string;
  startedOn: string | null;
  submittedOn: string | null;
  /** When the check was opened: the review's created date. */
  openedOn: string | null;
}

export interface CheckerLoad {
  checker: string;
  allocated: number;
  inProgress: number;
  completedLast30Days: number;
  oldestOpenWorkingDays: number | null;
}

export interface Workload {
  totals: { awaitingAllocation: number; allocated: number; inProgress: number; completedThisMonth: number };
  checkers: CheckerLoad[];
}

function day(value: string | null): Date | null {
  if (!value) return null;
  const parsed = new Date(`${value.slice(0, 10)}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

export function summarise(reviews: WorkloadReview[], queued: number, now: Date): Workload {
  const today = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), 12));
  const thirtyDaysAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);
  const monthStart = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1, 12));

  const byChecker = new Map<string, CheckerLoad>();
  const totals = { awaitingAllocation: queued, allocated: 0, inProgress: 0, completedThisMonth: 0 };

  for (const review of reviews) {
    const name = review.checker?.trim() || 'Unassigned';
    const load = byChecker.get(name) ?? {
      checker: name, allocated: 0, inProgress: 0, completedLast30Days: 0, oldestOpenWorkingDays: null,
    };

    if (review.status === 'Submitted') {
      const submitted = day(review.submittedOn);
      if (submitted && submitted >= thirtyDaysAgo) load.completedLast30Days += 1;
      if (submitted && submitted >= monthStart) totals.completedThisMonth += 1;
    } else {
      if (review.status === 'Review In Progress') {
        load.inProgress += 1;
        totals.inProgress += 1;
      } else {
        load.allocated += 1;
        totals.allocated += 1;
      }

      const opened = day(review.openedOn);
      if (opened) {
        const age = workingDaysBetween(opened, today);
        load.oldestOpenWorkingDays = Math.max(load.oldestOpenWorkingDays ?? 0, age);
      }
    }

    byChecker.set(name, load);
  }

  const checkers = [...byChecker.values()].sort(
    (a, b) => b.allocated + b.inProgress - (a.allocated + a.inProgress) || a.checker.localeCompare(b.checker),
  );
  return { totals, checkers };
}
```

Check `workingDaysBetween` in `lib/workingDays.ts` (line 64) counts the first day, as the remediation clock does. If it does not, adjust the expected `3` in the test to what the shared calendar returns. Say why in the commit, rather than writing a second calendar.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd app; npx vitest run src/features/workload`
Expected: PASS.

- [ ] **Step 5: Write the hook and the page**

`useWorkload.ts` reads the reviews for one discipline and the queued cases, both limited by the manager's Dataverse share:

```ts
import { useEffect, useState } from 'react';
import { Al_outcomecasesService, Al_reviewinstancesService } from '../../generated';
import { toReview } from '../cases/reviewRows';
import { logTechnical } from '../../services/errors';
import type { Discipline } from '../cases/caseCheckers';
import type { WorkloadReview } from './workload';

const TYPE: Record<Discipline, number> = { Tax: 120910200, AQS: 120910201 };
const QUEUED = 120910583;

export type WorkloadState =
  | { status: 'loading' }
  | { status: 'unavailable' }
  | { status: 'ready'; reviews: WorkloadReview[]; queued: number };

export function useWorkload(discipline: Discipline): WorkloadState {
  const [state, setState] = useState<WorkloadState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      Al_reviewinstancesService.getAll({ filter: `al_reviewtype eq ${TYPE[discipline]} and statecode eq 0`, top: 5000 }),
      Al_outcomecasesService.getAll({ filter: `al_casestatus eq ${QUEUED} and statecode eq 0`, top: 5000 }),
    ])
      .then(([reviews, cases]) => {
        if (cancelled) return;
        if (!reviews.success || !cases.success) {
          setState({ status: 'unavailable' });
          return;
        }
        setState({
          status: 'ready',
          reviews: reviews.data.map((row) => {
            const r = toReview(row);
            return { checker: r.owner, status: r.status, startedOn: r.startedOn, submittedOn: r.submittedOn, openedOn: row.createdon ?? null };
          }),
          queued: cases.data.length,
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('workload load', error);
        setState({ status: 'unavailable' });
      });
    return () => {
      cancelled = true;
    };
  }, [discipline]);

  return state;
}
```

`queued` counts every queued case the manager can see. For a Tax manager that includes Tax-then-AQS cases waiting on their AQS leg, which the Tax team shares for the case's whole life. It's labelled "Queued cases your team can see", not "awaiting Tax allocation". If the project owner wants the narrower number, it needs the route and each case's reviews, and is a follow-up.

Check that `toReview` returns `status` as the label ('Assigned', 'Review In Progress' or 'Submitted'), not the number. Read `reviewRows.ts` after line 40. If it returns the number, map it with `Al_reviewinstancesal_reviewstatus`.

`WorkloadPage.tsx`:
- If `allocatableDisciplines(roles)` has more than one entry, show a discipline switch. Otherwise show the one discipline.
- `PageIntro` title: "Team workload". Purpose: "Who holds what in your team, and how old the oldest open check is."
- Four tiles for the totals. "Queued" links to `/cases?status=Queued`. Check the worklist's `status` filter value format in `CaseWorklistPage.tsx`'s `applyFilters`, and use the exact value it compares.
- A table: Checker, Allocated, In progress, Completed (last 30 days), Oldest open (working days). Each checker name links to `/cases?person=<encoded name>`.

Follow the markup and CSS conventions of `features/dashboard/DashboardPage.tsx`. Load `skills/ascot-lloyd-design/SKILL.md` before writing the CSS (AGENTS.md rule 11).

- [ ] **Step 6: Route and navigation**

In `router.tsx`, after the `/cases/:caseId` route:

```tsx
        <Route path="/workload" element={<RequirePermission resource="page.workload"><WorkloadPage /></RequirePermission>} />
```

In `navigation.ts`, in "My work", after "Case worklist":

```ts
      { to: '/workload', label: 'Team workload', resource: 'page.workload' },
```

- [ ] **Step 7: Test, type-check, build**

Run: `cd app; npx vitest run; npx tsc -b; npm run build`
Expected: all pass, and the build succeeds. `router.permissions.test.ts` must pass. If it enumerates routes, add `/workload` to its expected list.

- [ ] **Step 8: Commit**

```bash
git add app/src
git commit -m "feat(app): team workload page for the two team managers (AD-218, AR-01)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 12: Deploy to DEV in the lockout-safe order

**Files:**
- Create: `docs/deployment/2026-09-23-role-scoped-access.md` (evidence, in the format of `docs/deployment/2026-09-22-staff-codes-registry.md`)

**Interfaces:** consumes every earlier task. Every command targets `Env_AQ_Dev`. Before Step 1, read the `pac` profile list and the org URL from `docs/deployment/2026-09-22-staff-codes-registry.md`. Always pass the org URL explicitly. Registration tool commands run from `plugins/OutcomeTesting.Registration` as `dotnet run -- <verb> ...`, one process at a time. If a run hangs at "Connecting…", kill the other instance.

- [ ] **Step 1: Everything green locally**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests; cd app; npx vitest run; npx tsc -b`
Expected: all pass. Record the counts in the deployment note.

- [ ] **Step 2: Schema (no behaviour change)**

```
dotnet run -- addlookupcolumn <org> al_outcomecase al_TaxCheckerContactId "Tax checker (access)" contact "AD-218: who may read this case as its Tax checker. Written by CaseAccessReconciler only." --confirm <org>
dotnet run -- addlookupcolumn <org> al_outcomecase al_AqsCheckerContactId "AQS checker (access)" contact "AD-218: who may read this case as its AQS checker." --confirm <org>
dotnet run -- addlookupcolumn <org> al_outcomecase al_AdviserContactId "Adviser (access)" contact "AD-218: set when the case is released for remediation." --confirm <org>
dotnet run -- addlookupcolumn <org> al_outcomecase al_TcSupervisorContactId "T&C Supervisor (access)" contact "AD-218: from the adviser mapping, set on release." --confirm <org>
dotnet run -- addlookupcolumn <org> al_outcomecase al_AqsQueueAccountId "AQS queue (access)" account "AD-218: set while the case waits for an AQS checker." --confirm <org>
dotnet run -- adddatecolumn <org> al_outcomecase al_AqsQueuedOn "Joined the AQS queue" "AD-218: when the case last joined the AQS queue." --behaviour UserLocal --confirm <org>
```

Check the printed relationship names equal the five in Task 7's Interfaces. If `adddatecolumn`'s `--behaviour` choices differ from `UserLocal`, read its usage and choose the date-and-time behaviour.

```
dotnet run -- ensureaccessprincipals <org> --confirm <org>
dotnet run -- setcascade <org> al_outcomecase_reviewinstance --confirm <org>
dotnet run -- setcascade <org> al_outcomecase_remediationaction --confirm <org>
dotnet run -- setcascade <org> al_outcomecase_outcome --confirm <org>
dotnet run -- setcascade <org> al_outcomecase_signoff --confirm <org>
dotnet run -- setcascade <org> al_outcomecase_caseassignment --confirm <org>
dotnet run -- setcascade <org> al_reviewinstance_response --reparent-only --confirm <org>
dotnet run -- grantteamsecurity <org>
```

If Dataverse refuses a cascade change, record the refusal verbatim in the note. Stop and report it: the Code App's team share for that child depends on it.

- [ ] **Step 3: Plug-ins**

```
dotnet build plugins/OutcomeTesting.Plugins -c Release
dotnet run -- registerall <org>
dotnet run -- pushassembly <org> ...
```

Pass `pushassembly` the Release dll path. Confirm the byte count printed matches `bin/Release/net462/OutcomeTesting.Plugins.dll`.

Register the four steps. Use `registerstep`'s header comment for argument order, and pass every filter:

```
dotnet run -- registerstep <org> OutcomeTesting.Plugins.CaseAccessPlugin Update al_outcomecase 40 "al_casestatus,al_reviewrouteid,al_advisername,al_adviseremail" sync
dotnet run -- registerstep <org> OutcomeTesting.Plugins.CaseAccessPlugin Create al_reviewinstance 40 "" sync
dotnet run -- registerstep <org> OutcomeTesting.Plugins.CaseAccessPlugin Update al_reviewinstance 40 "al_assignedcontactid,al_submittedon,statecode" sync
dotnet run -- registerstep <org> OutcomeTesting.Plugins.CaseAccessPlugin Create al_remediationaction 40 "" sync
dotnet run -- registerstep <org> OutcomeTesting.Plugins.CaseAccessGuardPlugin Create al_outcomecase 20 "" sync
dotnet run -- registerstep <org> OutcomeTesting.Plugins.CaseAccessGuardPlugin Update al_outcomecase 20 "al_taxcheckercontactid,al_aqscheckercontactid,al_aqsqueueaccountid,al_aqsqueuedon,al_advisercontactid,al_tcsupervisorcontactid" sync
dotnet run -- verifysteps <org>
```

Expected: 0 missing and 0 disabled. Then stamp the new steps `Enabled` in `src/` as commit `460399b` did for the others.

- [ ] **Step 4: Backfill**

```
dotnet run -- reconcileaccess <org>
dotnet run -- reconcileaccess <org> --confirm <org>
```

Paste the summary and every `UNMATCHED` and `FAILED` line into the note. A failure stops the rollout.

- [ ] **Step 5: AQS reviewers join the AQS Team account, after a check**

List the contacts holding `AL Portal - AQS Reviewer` (`fetch` verb, or `al_GetRoleHolders` via `callapi`). For each, read `parentcustomerid`. If any already has a parent account, stop and ask the project owner. Otherwise set it to the "Outcome Testing - AQS Team" account with the `webapi` verb (`PATCH contacts(<id>)` with `"parentcustomerid_account@odata.bind": "/accounts(<id>)"`). Read each one back.

- [ ] **Step 6: Create the two manager web roles**

```
dotnet run -- callapi <org> al_CreateRole RoleName="AL Portal - Tax Team Manager" ...
dotnet run -- callapi <org> al_CreateRole RoleName="AL Portal - AQS Team Manager" ...
```

Read `plugins/customapi/al_CreateRole.customapi.json` for the exact parameter names. Confirm both appear in the Code App's Security configuration.

- [ ] **Step 7: Additive portal permissions, then verify**

Check out the Task 7 commit's `table-permissions` folder into a scratch copy of the site folder. Run `dotnet run -- restoretablepermissions <org> <scratch site folder>`. This writes the new 25 alongside the untouched Global ones. Everyone still has Global read here.

Verify the Liquid-aggregate risk. Signed in as a reviewer, open My Work and Home. Every aggregate tile renders a number, not "Index was outside the bounds". Record what you saw.

- [ ] **Step 8: Restricting permissions and pages**

Diff the live `OT Review List`, `OT Remediation` and `OT Home` templates against the branch base (`git merge-base HEAD main`). If DEV holds anything newer, stop and merge it first. Then:

```
dotnet run -- restoretablepermissions <org> powerpages/outcome-testing---outcometesting
dotnet run -- pushwebtemplate <org> a1000000-0000-4000-8000-000000000016 powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html
dotnet run -- pushwebtemplate <org> a1000000-0000-4000-8000-000000000019 powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html
dotnet run -- pushwebtemplate <org> a1000000-0000-4000-8000-00000000001a powerpages/outcome-testing---outcometesting/web-templates/ot-home/OT-Home.webtemplate.source.html
```

The page rules (`webpagerule.yml`) have no verb in the table above. Remove the Planner role from each DEV rule the way the last page-rule change was deployed (`git log -- powerpages/outcome-testing---outcometesting/webpagerule.yml`), and record how.

Rollback, if a role loses access it should have: re-add that role to the relevant `*-All-Read` file and rerun `restoretablepermissions`.

- [ ] **Step 9: Code App**

```
cd app; npm run build
pa app push
```

Check the pushed bundle hash matches `dist/`. Open the `/app/` URL with `sourcetime` that the push prints, not the short play URL.

- [ ] **Step 10: Round-trip into `src/`**

Bring the new columns, relationships, cascade changes, Custom API, steps and security role into `src/` the way commit `a47024b` did (AD-013). Run `git show a47024b --stat` and read its message for the commands. Confirm `git diff --stat src/` shows only these components.

- [ ] **Step 11: Verify in DEV**

With the accounts available, check each and record pass, fail or not-run with the reason:
- (a) Signed in as a reviewer with no allocations, `/cases` and the Tax review page show nothing. Opening a known case id at `/case-details?id=` shows the not-found page. `/_api/al_outcomecases(<id>)` is refused.
- (b) Allocate a case to that reviewer from the Code App. It appears for them. Reallocate it. It disappears.
- (c) The AQS page shows the two queue groups, with ages.
- (d) An adviser sees a remediation case of theirs and nothing else.
- (e) As the project owner's Tax Team Manager test account (once mapped), `/workload` shows only Tax work. Allocating an AQS check is refused with the AD-218 message.

Negative checks (a), (d) and (e) need the non-admin test identities the spec names as a prerequisite, and DEV ignores `--as`. Where those don't exist yet, mark the check not-run for that reason. Do not substitute a System Administrator: it bypasses everything under test (AD-135).

- [ ] **Step 12: Write the deployment note and commit**

Write `docs/deployment/2026-09-23-role-scoped-access.md`. Include: target, what was deployed (a table with evidence for each step), backfill output, the Liquid-aggregate finding, verification results with not-run reasons, and the open items: the remap of roles and Dataverse roles by the project owner, the test identities, and TEST promotion. **TEST promotion hazard:** the `CaseAccessPlugin` steps travel with the solution, and the reconciler refuses every case write where the two teams or the AQS Team account are missing. So `ensureaccessprincipals` must run against TEST *before* the solution import activates those steps. Otherwise every case, review and remediation write in TEST fails.

```bash
git add docs/deployment/2026-09-23-role-scoped-access.md src/
git commit -m "docs(deployment): role-scoped access in DEV (AD-218)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

Then run `graphify update .` from the repo root.
