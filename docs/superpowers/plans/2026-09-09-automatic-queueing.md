# Automatic Queueing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A case that has a review route and sits at Imported or Ready for Allocation moves itself into Queued, so the shared queue offers fresh work without a manual status edit.

**Architecture:** One pure rule class in the plug-in assembly (`CaseQueueing`) decides whether a case should be queued and walks it there through the existing `CaseTransitions.MoveThrough`, one hop at a time, so AD-057's transition checks still apply. The rule fires in two commands: `ImportCasesPlugin` derives the route from the file's "Tax check required" answer and queues each new case; `UpdateCaseDetailsPlugin` queues a case once it has a route, unless the caller set a status in the same call. A registration-tool verb backfills cases that predate the rule by invoking the case-edit command, so the audit trail comes from the command itself.

**Tech Stack:** C# 7.3 plug-ins targeting net462 (`plugins/OutcomeTesting.Plugins`), xUnit tests with `FakeOrganizationService` (`plugins/OutcomeTesting.Plugins.Tests`), the net8.0 registration tool (`plugins/OutcomeTesting.Registration`).

**Spec:** The design agreed in chat on 2026-09-09 (this plan restates it in full; there is no separate spec file). Decision to be recorded as AD-093 in `knowledge/decision-log.md`.

## Global Constraints

- Plug-in code is C# 7.3 on net462: no `is not`, no target-typed `new()`, no switch expressions, no nullable reference annotations.
- Status changes on `al_outcomecase` go only through `CaseTransitions.MoveThrough`; never write `al_casestatus` directly (AD-057).
- Status values: Imported `120910580`, Ready for Allocation `120910582`, Queued `120910583` (`CaseLifecycle` constants).
- Route codes: `ROUTE-TAX-AQS` for Tax check required = Yes (`120910560`), `ROUTE-AQS` for No (`120910561`), derived by `UpdateCaseDetailsPlugin.DeriveRoute` (BR-004).
- Tests run with `DOTNET_ROLL_FORWARD=Major` set: `dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj`. The suite stood at 513 passing before this plan.
- The registration tool needs `DOTNET_ROLL_FORWARD=Major` too. Its sign-in can take several minutes; use a 600000 ms timeout and never run two of it at once.
- Commit only the files each task names. The working tree carries unrelated uncommitted work from earlier today; leave it alone.
- Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

### Task 1: The queueing rule

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/CaseQueueing.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/CaseQueueingTests.cs`

**Interfaces:**
- Consumes: `CaseLifecycle.Imported`, `CaseLifecycle.ReadyForAllocation`, `CaseLifecycle.Queued`, `CaseLifecycle.NameOf(int)`, `CaseTransitions.MoveThrough(IOrganizationService, Guid, IEnumerable<int>)`.
- Produces:
  - `public static bool CaseQueueing.ShouldQueue(int? status, bool hasRoute)`
  - `public static int[] CaseQueueing.HopsToQueue(int? status)`
  - `public static bool CaseQueueing.QueueIfRouted(IOrganizationService service, Guid caseId, int? status, bool hasRoute, List<string> changes)` — walks the case and appends one change line; returns whether it moved.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-093: a routed case at Imported or Ready for Allocation belongs in the queue.
    /// Before this rule nothing moved a case into Queued except a hand-back from a Tax
    /// check or a sign-off, so the AD-076 self-service queue never offered fresh work.
    /// </summary>
    public class CaseQueueingTests
    {
        private const string CaseEntity = "al_outcomecase";
        private const string CaseStatus = "al_casestatus";
        private static readonly Guid CaseId = new Guid("11111111-1111-4111-8111-111111111111");

        private static FakeOrganizationService WithCaseAt(int status)
        {
            var service = new FakeOrganizationService();
            service.Seed(CaseEntity, CaseId, CaseStatus, new OptionSetValue(status));
            return service;
        }

        private static int? StatusOf(FakeOrganizationService service)
        {
            var value = service.Row(CaseEntity, CaseId).GetAttributeValue<OptionSetValue>(CaseStatus);
            return value != null ? value.Value : (int?)null;
        }

        [Theory]
        [InlineData(CaseLifecycle.Imported, true, true)]
        [InlineData(CaseLifecycle.ReadyForAllocation, true, true)]
        [InlineData(CaseLifecycle.Imported, false, false)]
        [InlineData(CaseLifecycle.ReadyForAllocation, false, false)]
        [InlineData(CaseLifecycle.ValidationFailed, true, false)]
        [InlineData(CaseLifecycle.Queued, true, false)]
        [InlineData(CaseLifecycle.Assigned, true, false)]
        [InlineData(CaseLifecycle.Closed, true, false)]
        public void Queues_only_a_routed_case_that_has_not_reached_the_queue(int status, bool hasRoute, bool expected)
        {
            Assert.Equal(expected, CaseQueueing.ShouldQueue(status, hasRoute));
        }

        [Fact]
        public void A_case_with_no_status_is_not_queued()
        {
            Assert.False(CaseQueueing.ShouldQueue(null, true));
        }

        [Fact]
        public void Walks_imported_through_ready_for_allocation()
        {
            // Skipping Ready for Allocation is exactly what AD-057 forbids.
            Assert.Equal(
                new[] { CaseLifecycle.ReadyForAllocation, CaseLifecycle.Queued },
                CaseQueueing.HopsToQueue(CaseLifecycle.Imported));
            Assert.Equal(new[] { CaseLifecycle.Queued }, CaseQueueing.HopsToQueue(CaseLifecycle.ReadyForAllocation));
            Assert.Empty(CaseQueueing.HopsToQueue(CaseLifecycle.Queued));
            Assert.Empty(CaseQueueing.HopsToQueue(null));
        }

        [Fact]
        public void Moves_a_routed_imported_case_to_queued_and_records_the_hop()
        {
            var service = WithCaseAt(CaseLifecycle.Imported);
            var changes = new List<string>();

            var moved = CaseQueueing.QueueIfRouted(service, CaseId, CaseLifecycle.Imported, true, changes);

            Assert.True(moved);
            Assert.Equal(CaseLifecycle.Queued, StatusOf(service));
            Assert.Equal(2, service.Updates.Count);
            Assert.Equal("Status Imported -> Queued (queued automatically: route set, AD-093)", Assert.Single(changes));
        }

        [Fact]
        public void Leaves_an_unrouted_case_where_it_is()
        {
            var service = WithCaseAt(CaseLifecycle.ReadyForAllocation);
            var changes = new List<string>();

            var moved = CaseQueueing.QueueIfRouted(service, CaseId, CaseLifecycle.ReadyForAllocation, false, changes);

            Assert.False(moved);
            Assert.Equal(CaseLifecycle.ReadyForAllocation, StatusOf(service));
            Assert.Empty(service.Updates);
            Assert.Empty(changes);
        }

        [Fact]
        public void Leaves_a_case_already_past_the_queue_alone()
        {
            var service = WithCaseAt(CaseLifecycle.Assigned);
            var changes = new List<string>();

            Assert.False(CaseQueueing.QueueIfRouted(service, CaseId, CaseLifecycle.Assigned, true, changes));
            Assert.Empty(service.Updates);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~CaseQueueingTests"`
Expected: build error `The name 'CaseQueueing' does not exist in the current context`.

- [ ] **Step 3: Write the rule**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// AD-093: a case that has a review route and sits at Imported or Ready for Allocation
    /// belongs in the shared queue. Nothing else in the solution moves a case into Queued
    /// except a hand-back from a Tax check or a sign-off, so without this the AD-076
    /// self-service queue only ever showed returned work and every fresh case waited for a
    /// team lead to edit its status by hand.
    ///
    /// The rule is about the state the case is in, not the field that changed: an edit to
    /// any detail of a routed case still at Ready for Allocation queues it. Hops go through
    /// <see cref="CaseTransitions.MoveThrough(IOrganizationService, Guid, IEnumerable{int})"/>
    /// so a skipped state is refused the same way as anywhere else (AD-057).
    /// </summary>
    public static class CaseQueueing
    {
        private static readonly int[] NoHops = new int[0];

        /// <summary>Whether a case in this state, with or without a route, should be queued.</summary>
        public static bool ShouldQueue(int? status, bool hasRoute)
        {
            if (!hasRoute || !status.HasValue)
            {
                return false;
            }

            return status.Value == CaseLifecycle.Imported || status.Value == CaseLifecycle.ReadyForAllocation;
        }

        /// <summary>The hops from this status to Queued, in order; empty when there are none.</summary>
        public static int[] HopsToQueue(int? status)
        {
            if (!status.HasValue)
            {
                return NoHops;
            }

            switch (status.Value)
            {
                case CaseLifecycle.Imported:
                    return new[] { CaseLifecycle.ReadyForAllocation, CaseLifecycle.Queued };
                case CaseLifecycle.ReadyForAllocation:
                    return new[] { CaseLifecycle.Queued };
                default:
                    return NoHops;
            }
        }

        /// <summary>
        /// Queues the case when <see cref="ShouldQueue"/> says so, appending one line to
        /// <paramref name="changes"/> in the same shape the case-edit command writes for a
        /// status change, so the case history reads the same whether a person or the rule
        /// moved it. Returns whether the case moved.
        /// </summary>
        public static bool QueueIfRouted(
            IOrganizationService service, Guid caseId, int? status, bool hasRoute, List<string> changes)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            if (!ShouldQueue(status, hasRoute))
            {
                return false;
            }

            CaseTransitions.MoveThrough(service, caseId, HopsToQueue(status));

            changes.Add("Status " + CaseLifecycle.NameOf(status.Value) + " -> " + CaseLifecycle.NameOf(CaseLifecycle.Queued)
                + " (queued automatically: route set, AD-093)");
            return true;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~CaseQueueingTests"`
Expected: 13 passed (8 theory cases + 5 facts). If `CaseLifecycle.NameOf` renders a label other than `Imported` or `Queued`, read `CaseLifecycle.cs` line 44 area and match the test string to the label table rather than changing the label.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/CaseQueueing.cs plugins/OutcomeTesting.Plugins.Tests/CaseQueueingTests.cs
git commit -m "feat(lifecycle): CaseQueueing, the AD-093 rule that a routed case belongs in the queue

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Import derives the route and queues the case

> **Superseded in part by the final-review fix wave (commit 40633f8):** `CreateRoutedCase` now
> takes `(IOrganizationService userService, Func<string, Guid?> findRoute, Entity record)`,
> resolves each route code at most once per import through a memoised resolver built before
> the loop, reads the status off the record, and returns a `RoutedCaseResult` (created id,
> whether queued, the queueing error if any) instead of throwing after the create. The
> signatures and tests below are the plan as executed, kept for the record.

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/ImportCasesPlugin.cs:150-165` (the per-row create inside `foreach (var row in parsed.Valid)`)
- Test: `plugins/OutcomeTesting.Plugins.Tests/DeriveRouteTests.cs` (one new fact)
- Test: `plugins/OutcomeTesting.Plugins.Tests/ImportCasesPluginTests.cs` (two new facts)

**Interfaces:**
- Consumes: `CaseQueueing.QueueIfRouted(IOrganizationService, Guid, int?, bool, List<string>)` from Task 1; `UpdateCaseDetailsPlugin.DeriveRoute(IOrganizationService service, Entity before, Entity update, List<string> changes)` (existing, public static).
- Produces: `public static Guid ImportCasesPlugin.CreateRoutedCase(IOrganizationService userService, IOrganizationService systemService, Entity record)` — derives the route onto `record`, creates it, queues it when routed, returns the new id.

- [ ] **Step 1: Write the failing tests**

Add to `DeriveRouteTests`, inside the class after the existing facts:

```csharp
        [Fact]
        public void Derives_a_route_for_a_new_record_with_no_before_values()
        {
            // The import creates cases with the file's "Tax check required" answer already on
            // the record and nothing before it. An empty before entity must read as "the
            // answer changed", or every imported case would stay unrouted (AD-093).
            var svc = Routes();
            var record = Case(No, null);
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.DeriveRoute(svc, new Entity("al_outcomecase"), record, changes);

            Assert.Equal(AqsOnly, record.GetAttributeValue<EntityReference>("al_reviewrouteid").Id);
            Assert.Single(changes);
        }
```

Add to `ImportCasesPluginTests`, inside the class after the existing facts:

```csharp
        private static readonly Guid AqsOnlyRoute = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private const int TaxCheckRequiredNo = 120910561;

        private static FakeOrganizationService WithRoutes()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_reviewroute", AqsOnlyRoute, "al_routecode", "ROUTE-AQS");
            service.Seed("al_reviewroute", Guid.Parse("11111111-1111-4111-8111-111111111111"), "al_routecode", "ROUTE-TAX-AQS");
            return service;
        }

        private static Entity ImportedRecord(int? taxCheckRequired)
        {
            var record = new Entity("al_outcomecase");
            record["al_casereference"] = "IO-1";
            record["al_casestatus"] = new OptionSetValue(ImportRules.CaseStatusImported);
            if (taxCheckRequired.HasValue)
            {
                record["al_taxcheckrequired"] = new OptionSetValue(taxCheckRequired.Value);
            }

            return record;
        }

        [Fact]
        public void A_row_that_answers_tax_check_required_lands_in_the_queue_with_its_route()
        {
            // AD-093: the queue is the landing place for routed work, so an import that
            // carries the answer needs no edit before a checker can pick the case up.
            var service = WithRoutes();

            var caseId = ImportCasesPlugin.CreateRoutedCase(service, service, ImportedRecord(TaxCheckRequiredNo));

            var row = service.Row("al_outcomecase", caseId);
            Assert.Equal(AqsOnlyRoute, row.GetAttributeValue<EntityReference>("al_reviewrouteid").Id);
            Assert.Equal(CaseLifecycle.Queued, row.GetAttributeValue<OptionSetValue>("al_casestatus").Value);
        }

        [Fact]
        public void A_row_without_the_answer_stays_imported_and_unrouted()
        {
            // It shows on the dashboard as "Awaiting a route" until someone answers, and the
            // case-edit command queues it then.
            var service = WithRoutes();

            var caseId = ImportCasesPlugin.CreateRoutedCase(service, service, ImportedRecord(null));

            var row = service.Row("al_outcomecase", caseId);
            Assert.False(row.Contains("al_reviewrouteid"));
            Assert.Equal(CaseLifecycle.Imported, row.GetAttributeValue<OptionSetValue>("al_casestatus").Value);
            Assert.Empty(service.Updates);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~ImportCasesPluginTests|FullyQualifiedName~DeriveRouteTests"`
Expected: build error `'ImportCasesPlugin' does not contain a definition for 'CreateRoutedCase'`. (The DeriveRoute fact is expected to pass once the build succeeds; it pins behaviour the import now relies on.)

- [ ] **Step 3: Add `CreateRoutedCase` and call it from the import loop**

In `ImportCasesPlugin.cs`, replace the two lines

```csharp
                record["al_casestatus"] = new OptionSetValue(ImportRules.CaseStatusImported);

                try
                {
                    userService.Create(record);
                    imported++;
```

with

```csharp
                record["al_casestatus"] = new OptionSetValue(ImportRules.CaseStatusImported);

                try
                {
                    CreateRoutedCase(userService, systemService, record);
                    imported++;
```

Then add this method to the class, next to `FindExistingReferences`:

```csharp
        /// <summary>
        /// Creates one imported case and, when the file answered "Tax check required",
        /// derives its route (BR-004) and queues it (AD-093). The route is stamped before the
        /// create so the row never exists unrouted; the hops run after it because the
        /// lifecycle walker needs a row to move. A file that gives no answer leaves the case
        /// at Imported, where the dashboard shows it as awaiting a route.
        ///
        /// Inside the caller's per-row try: a missing route configuration surfaces as that
        /// row failing with the precondition message DeriveRoute already writes, not as a
        /// case created in a state the queue will never offer.
        /// </summary>
        public static Guid CreateRoutedCase(IOrganizationService userService, IOrganizationService systemService, Entity record)
        {
            if (userService == null)
            {
                throw new ArgumentNullException(nameof(userService));
            }

            if (systemService == null)
            {
                throw new ArgumentNullException(nameof(systemService));
            }

            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var changes = new List<string>();
            UpdateCaseDetailsPlugin.DeriveRoute(systemService, new Entity(CaseEntity), record, changes);

            var caseId = userService.Create(record);

            CaseQueueing.QueueIfRouted(
                userService,
                caseId,
                ImportRules.CaseStatusImported,
                record.Contains("al_reviewrouteid"),
                changes);

            return caseId;
        }
```

If `CaseEntity` is not already a constant in `ImportCasesPlugin`, use the literal `"al_outcomecase"`. Check the file's `using` lines include `System.Collections.Generic`; add it if not.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~ImportCasesPluginTests|FullyQualifiedName~DeriveRouteTests"`
Expected: all pass, including the three new facts.

- [ ] **Step 5: Run the whole suite**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj`
Expected: 0 failed. Note the passed count for the deploy note.

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/ImportCasesPlugin.cs plugins/OutcomeTesting.Plugins.Tests/ImportCasesPluginTests.cs plugins/OutcomeTesting.Plugins.Tests/DeriveRouteTests.cs
git commit -m "feat(import): derive the route from the file and queue the case on create (AD-093)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The case-edit command queues a routed case

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs:184-200` (between the update block and the adviser-name block)
- Test: `plugins/OutcomeTesting.Plugins.Tests/UpdateCaseDetailsQueueingTests.cs` (new file)

**Interfaces:**
- Consumes: `CaseQueueing.QueueIfRouted` from Task 1.
- Produces: `public static bool UpdateCaseDetailsPlugin.QueueAfterEdit(IOrganizationService service, Entity before, Entity update, List<string> changes)` — the post-save hook, pure enough to test with the fake.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-093 applied by the case-edit command: once a case has a route and is still short of
    /// the queue, saving it queues it. The rule reads the state the save leaves behind, so
    /// it does not matter whether the route arrived in this call, was derived in this call,
    /// or was already there. A status the caller set in the same call is theirs to set.
    /// </summary>
    public class UpdateCaseDetailsQueueingTests
    {
        private const string CaseEntity = "al_outcomecase";
        private const string StatusAttr = "al_casestatus";
        private const string RouteAttr = "al_reviewrouteid";
        private static readonly Guid CaseId = new Guid("11111111-1111-4111-8111-111111111111");
        private static readonly Guid AqsOnly = new Guid("22222222-2222-4222-8222-222222222222");

        private static FakeOrganizationService WithCase(int status, Guid? route)
        {
            var service = new FakeOrganizationService();
            var row = service.Seed(CaseEntity, CaseId, StatusAttr, new OptionSetValue(status));
            if (route.HasValue)
            {
                row[RouteAttr] = new EntityReference("al_reviewroute", route.Value);
            }

            return service;
        }

        private static Entity Before(FakeOrganizationService service)
        {
            return service.Row(CaseEntity, CaseId);
        }

        private static int StatusOf(FakeOrganizationService service)
        {
            return service.Row(CaseEntity, CaseId).GetAttributeValue<OptionSetValue>(StatusAttr).Value;
        }

        [Fact]
        public void Setting_a_route_on_an_imported_case_queues_it()
        {
            var service = WithCase(CaseLifecycle.Imported, null);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update[RouteAttr] = new EntityReference("al_reviewroute", AqsOnly);
            var changes = new List<string>();

            var moved = UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes);

            Assert.True(moved);
            Assert.Equal(CaseLifecycle.Queued, StatusOf(service));
            Assert.Contains(changes, c => c.StartsWith("Status Imported -> Queued", StringComparison.Ordinal));
        }

        [Fact]
        public void Editing_any_detail_of_a_routed_case_at_ready_for_allocation_queues_it()
        {
            // The rule is about the state, not the field that changed.
            var service = WithCase(CaseLifecycle.ReadyForAllocation, AqsOnly);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update["al_priority"] = new OptionSetValue(120910540);
            var changes = new List<string>();

            Assert.True(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Equal(CaseLifecycle.Queued, StatusOf(service));
        }

        [Fact]
        public void A_status_the_caller_set_is_left_alone()
        {
            // The caller moved Imported -> Ready for Allocation themselves; the rule does not
            // pile a second move on top of a deliberate one in the same call.
            var service = WithCase(CaseLifecycle.ReadyForAllocation, AqsOnly);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update[StatusAttr] = new OptionSetValue(CaseLifecycle.ReadyForAllocation);
            var changes = new List<string>();

            Assert.False(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Empty(service.Updates);
            Assert.Empty(changes);
        }

        [Fact]
        public void An_unrouted_case_is_not_queued()
        {
            var service = WithCase(CaseLifecycle.ReadyForAllocation, null);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update["al_priority"] = new OptionSetValue(120910540);
            var changes = new List<string>();

            Assert.False(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Equal(CaseLifecycle.ReadyForAllocation, StatusOf(service));
        }

        [Fact]
        public void A_case_past_the_queue_is_not_touched()
        {
            var service = WithCase(CaseLifecycle.Assigned, AqsOnly);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update["al_priority"] = new OptionSetValue(120910540);
            var changes = new List<string>();

            Assert.False(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Empty(service.Updates);
        }
    }
}
```

Note on `Seed`: it returns the seeded `Entity` (see `FakeOrganizationService.cs:58`). If it does not in this version, replace `var row = service.Seed(...)` with `service.Seed(...); var row = service.Row(CaseEntity, CaseId);`.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~UpdateCaseDetailsQueueingTests"`
Expected: build error `'UpdateCaseDetailsPlugin' does not contain a definition for 'QueueAfterEdit'`.

- [ ] **Step 3: Add `QueueAfterEdit` and call it after the save**

In `UpdateCaseDetailsPlugin.cs`, immediately after the `if (string.IsNullOrEmpty(expectedRowVersion)) { ... } else { ... }` block that performs the update (ends around line 184), and before the comment `// A corrected adviser name reaches the remediation actions...`, insert:

```csharp
            // AD-093: a routed case still short of the queue is queued by the save. Runs after
            // the update so the walk starts from the status the caller's own change left.
            QueueAfterEdit(userService, before, update, changes);
```

Then add this method next to `EnsureLifecycleTransition`:

```csharp
        /// <summary>
        /// AD-093 on the case-edit command. The rule reads the state the save leaves behind:
        /// the route after this call (set explicitly, derived by DeriveRoute, or already on
        /// the case) and the status before it. A status the caller set in the same call is
        /// theirs: EnsureLifecycleTransition has already checked it, and a second move on top
        /// of a deliberate one would make the history read as two decisions.
        /// </summary>
        public static bool QueueAfterEdit(IOrganizationService service, Entity before, Entity update, List<string> changes)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            if (update.Contains(StatusAttr))
            {
                return false;
            }

            var routeAfter = update.Contains(RouteAttr)
                ? update.GetAttributeValue<EntityReference>(RouteAttr)
                : before.GetAttributeValue<EntityReference>(RouteAttr);
            var status = before.GetAttributeValue<OptionSetValue>(StatusAttr);

            return CaseQueueing.QueueIfRouted(
                service,
                before.Id,
                status != null ? status.Value : (int?)null,
                routeAfter != null,
                changes);
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~UpdateCaseDetailsQueueingTests"`
Expected: 5 passed.

- [ ] **Step 5: Run the whole suite**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj`
Expected: 0 failed. Note the passed count.

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs plugins/OutcomeTesting.Plugins.Tests/UpdateCaseDetailsQueueingTests.cs
git commit -m "feat(cases): the case-edit command queues a routed case (AD-093)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Backfill verb in the registration tool

**Files:**
- Modify: `plugins/OutcomeTesting.Registration/Program.cs` — verb dispatch after the `pushwebtemplate` branch (around line 343), and a new `QueueRoutedCases` function near `Fetch` (around line 440).

**Interfaces:**
- Consumes: `Connect(string orgUrl)` (existing), the `al_UpdateCaseDetails` custom API with request parameters `TargetId`, `IdempotencyKey`, `RouteId`, `Reason` (strings), and the Task 3 behaviour that an explicit `RouteId` on a routed case at Imported or Ready for Allocation queues it and writes the audit event.
- Produces: verb `queueroutedcases <orgUrl> [--confirm]`. Without `--confirm` it lists candidates only.

- [ ] **Step 1: Add the dispatch branch**

After

```csharp
if (args.Length >= 4 && args[0].Equals("pushwebtemplate", StringComparison.OrdinalIgnoreCase))
{
    return PushWebTemplate(args[1], args[2], args[3]);
}
```

add

```csharp
if (args.Length >= 2 && args[0].Equals("queueroutedcases", StringComparison.OrdinalIgnoreCase))
{
    return QueueRoutedCases(args[1], args.Length > 2 && args[2].Equals("--confirm", StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Add the function**

Place it directly after `int Fetch(string orgUrl, string fetchXmlOrFile)`:

```csharp
// AD-093 backfill. Cases created before the rule sit at Imported or Ready for Allocation with
// a route and no way into the queue except a hand edit. Each one is re-saved through
// al_UpdateCaseDetails with its own route, which the plug-in turns into the Queued hops and an
// audit event, so the trail is the command's, not this tool's. Idempotent: the key is the case
// id, so a re-run replays the original result.
int QueueRoutedCases(string orgUrl, bool confirm)
{
    using var svc = Connect(orgUrl);

    var candidates = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_outcomecase\">" +
        "<attribute name=\"al_outcomecaseid\"/><attribute name=\"al_casereference\"/>" +
        "<attribute name=\"al_casestatus\"/><attribute name=\"al_reviewrouteid\"/>" +
        "<filter type=\"and\">" +
        "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\"/>" +
        "<condition attribute=\"al_reviewrouteid\" operator=\"not-null\"/>" +
        "<condition attribute=\"al_casestatus\" operator=\"in\">" +
        "<value>" + CaseStatusImported + "</value><value>" + CaseStatusReadyForAllocation + "</value>" +
        "</condition></filter><order attribute=\"al_casereference\"/></entity></fetch>")).Entities;

    Console.WriteLine($"{candidates.Count} routed case(s) short of the queue.");
    foreach (var c in candidates)
    {
        var route = c.GetAttributeValue<EntityReference>("al_reviewrouteid");
        Console.WriteLine($"   {c.GetAttributeValue<string>("al_casereference")}  status {c.GetAttributeValue<OptionSetValue>("al_casestatus").Value}  route {route.Name}");
    }

    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run with --confirm to queue them.");
        return 0;
    }

    var queued = 0;
    foreach (var c in candidates)
    {
        var reference = c.GetAttributeValue<string>("al_casereference") ?? c.Id.ToString("D");
        try
        {
            svc.Execute(new OrganizationRequest("al_UpdateCaseDetails")
            {
                ["TargetId"] = c.Id.ToString("D"),
                ["IdempotencyKey"] = "QUEUE-ROUTED-" + c.Id.ToString("N"),
                ["RouteId"] = c.GetAttributeValue<EntityReference>("al_reviewrouteid").Id.ToString("D"),
                ["Reason"] = "Queued automatically: route already set (AD-093 backfill)",
            });

            var after = svc.Retrieve("al_outcomecase", c.Id, new ColumnSet("al_casestatus"))
                .GetAttributeValue<OptionSetValue>("al_casestatus");
            var isQueued = after != null && after.Value == CaseStatusQueued;
            Console.WriteLine($"   {reference}: {(isQueued ? "Queued" : "NOT queued, status " + (after?.Value.ToString() ?? "(none)"))}");
            if (isQueued) queued++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"   {reference} FAILED: {ex.Message}");
        }
    }

    Console.WriteLine($"Done: {queued} of {candidates.Count} queued.");
    return queued == candidates.Count ? 0 : 1;
}
```

Add these two constants next to the existing `const int CaseStatusQueued = 120910583;` (line 165):

```csharp
const int CaseStatusImported = 120910580;
const int CaseStatusReadyForAllocation = 120910582;
```

- [ ] **Step 3: Build the tool**

Run: `DOTNET_ROLL_FORWARD=Major dotnet build plugins/OutcomeTesting.Registration/OutcomeTesting.Registration.csproj`
Expected: `Build succeeded` with 0 errors. If `FetchExpression` or `ColumnSet` is unresolved, add `using Microsoft.Xrm.Sdk.Query;` at the top of `Program.cs`; both are already used by `Fetch`, so this is unlikely.

- [ ] **Step 4: Dry-run against DEV**

Run (timeout 600000 ms): `DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll queueroutedcases https://org0b075da8.crm11.dynamics.com`
Expected: lists `IO-000124` (status 120910580) and `IO-000125` (status 120910582), both on the AQS only route, then `Dry run.` Do not pass `--confirm` yet; the plug-in assembly with Task 3 is not live until Task 5.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Registration/Program.cs
git commit -m "feat(registration): queueroutedcases, the AD-093 backfill through al_UpdateCaseDetails

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: Deploy to DEV, backfill, verify, and record

**Files:**
- Modify: `knowledge/decision-log.md` (append AD-093 after AD-092)
- Create: `docs/deployment/2026-09-09-automatic-queueing.md`

**Interfaces:**
- Consumes: the `pushassembly <orgUrl>` verb (pushes the built plug-in assembly), the `queueroutedcases <orgUrl> --confirm` verb from Task 4, the `fetch <orgUrl> <fetchxml>` verb.

- [ ] **Step 1: Build the plug-in assembly**

Run: `DOTNET_ROLL_FORWARD=Major dotnet build plugins/OutcomeTesting.Plugins/OutcomeTesting.Plugins.csproj -c Debug`
Expected: `Build succeeded`. Check `docs/deployment/2026-09-09-question-version-rule.md` under "What ran" for the exact `pushassembly` invocation used this morning and use the same configuration it pushed.

- [ ] **Step 2: Push the assembly**

Run (timeout 600000 ms): `DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll pushassembly https://org0b075da8.crm11.dynamics.com`
Expected: `Done` with a byte count and a modified timestamp. Record both. No step registrations change: both `ImportCasesPlugin` and `UpdateCaseDetailsPlugin` are existing Custom API plug-ins.

- [ ] **Step 3: Run the backfill**

Run (timeout 600000 ms): `DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll queueroutedcases https://org0b075da8.crm11.dynamics.com --confirm`
Expected: `IO-000124: Queued`, `IO-000125: Queued`, `Done: 2 of 2 queued.` If either reports a permission precondition, stop and report: the service account `svc.automate.aq` holds System Administrator, which the gate treats as break-glass, so a refusal means something else changed.

- [ ] **Step 4: Verify the queue sees them**

Run the same FetchXML the portal's AQS queue uses, reduced to the case reference:

```
DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll fetch https://org0b075da8.crm11.dynamics.com '<fetch><entity name="al_outcomecase"><attribute name="al_casereference"/><attribute name="al_casestatus"/><filter><condition attribute="statecode" operator="eq" value="0"/><condition attribute="al_casestatus" operator="eq" value="120910583"/></filter><link-entity name="al_reviewroute" from="al_reviewrouteid" to="al_reviewrouteid" alias="route"><filter><condition attribute="al_requiresaqsreview" operator="eq" value="1"/></filter></link-entity></entity></fetch>'
```

Expected: both `IO-000124` and `IO-000125` in the rows. Also confirm the audit trail: fetch `al_auditevent` rows whose `al_details` contains `AD-093` and expect two, each carrying `Status ... -> Queued (queued automatically: route set, AD-093)`.

- [ ] **Step 5: Record the decision**

Append to `knowledge/decision-log.md` directly after the AD-092 row, matching the table's columns (id, decision, rationale, date):

```markdown
| AD-093 | **A case that has a review route and sits at Imported or Ready for Allocation is queued automatically**, Imported -> Ready for Allocation -> Queued through `CaseTransitions.MoveThrough`. Applied by `ImportCasesPlugin` (which now derives the route from the file's "Tax check required" answer via `DeriveRoute` and queues the case on create) and by `UpdateCaseDetailsPlugin` after every save that leaves a routed case short of the queue, unless the caller set a status in the same call. The rule is state-based: editing any detail of a routed case at Ready for Allocation queues it. Pure rule in `CaseQueueing`, tested. One-off `queueroutedcases` verb re-saves pre-existing cases through the command so their audit events are the command's. | Nothing moved a case into Queued except a hand-back from a Tax check or a sign-off, so the AD-076 self-service queue never offered fresh work and AD-040's "a case sits in the queue" was only true after a lead edited the status by hand, twice for an imported case. Found on 2026-09-09 when two AQS-only DEV cases with no reviewer (IO-000124 at Imported, IO-000125 at Ready for Allocation) did not appear in the queue. Project owner direction: automatic rather than an explicit "send to queue" command. Ready for Allocation stays in the lifecycle as the hop the walk passes through, so AD-057's no-skipping rule and the app's `CASE_STATUS_TRANSITIONS` mirror are unchanged. | 2026-09-09 |
```

- [ ] **Step 6: Write the deploy note**

Create `docs/deployment/2026-09-09-automatic-queueing.md`:

```markdown
# Automatic queueing of routed cases — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). All reads and writes through
`plugins/OutcomeTesting.Registration`; `pac` remains token-revoked.

## The report

AQS-only cases with no reviewer did not appear in the shared queue.

## What was wrong

The queue lists cases at Queued on a route that requires AQS. IO-000124 sat at Imported and
IO-000125 at Ready for Allocation, both routed AQS only. Nothing in the solution moved a case
into Queued except a hand-back from a Tax check or a sign-off; allocation walked straight past
it to Assigned. So the AD-076 self-service queue only ever showed returned work.

## The fix (AD-093)

A routed case at Imported or Ready for Allocation is queued, hop by hop through
`CaseTransitions.MoveThrough`:

| Where | Change |
|---|---|
| `CaseQueueing` | the rule, pure, tested (`CaseQueueingTests`) |
| `ImportCasesPlugin.CreateRoutedCase` | derives the route from "Tax check required" via `DeriveRoute`, creates the case, queues it |
| `UpdateCaseDetailsPlugin.QueueAfterEdit` | after every save, queues a routed case still short of the queue; a status set by the caller wins |
| `queueroutedcases` verb | one-off: re-saves pre-existing routed cases through `al_UpdateCaseDetails` so the plug-in queues them and writes the audit event |

Tests: plug-ins <count> passed.

## What ran

| Step | Command | Result |
|---|---|---|
| Assembly push | `pushassembly https://org0b075da8.crm11.dynamics.com` | <bytes> bytes, modified <timestamp> |
| Backfill dry run | `queueroutedcases https://org0b075da8.crm11.dynamics.com` | IO-000124, IO-000125 listed |
| Backfill | `queueroutedcases https://org0b075da8.crm11.dynamics.com --confirm` | 2 of 2 queued |
| Queue check | portal AQS queue FetchXML | both cases returned |
| Audit check | `al_auditevent` with details containing `AD-093` | 2 rows |

## Not changed

Portal queue query, app worklist and its status filter, `CaseLifecycle` and the app's
`CASE_STATUS_TRANSITIONS`, allocation, claiming.

## Sign-off

| Step | Run by | Date | Outcome |
|---|---|---|---|
| Rule, import and edit changes, tests | Delivery (automated) | 2026-09-09 | Pass — <count> tests |
| Assembly push | Delivery (automated) | 2026-09-09 | Pass |
| Backfill of IO-000124 and IO-000125 | Delivery (automated) | 2026-09-09 | Pass — both Queued, audited |
```

Replace every `<count>`, `<bytes>` and `<timestamp>` with the values from Steps 1 to 4 of this task and from Task 3 Step 5. A note with a placeholder left in it is not finished.

- [ ] **Step 7: Commit**

```bash
git add knowledge/decision-log.md docs/deployment/2026-09-09-automatic-queueing.md
git commit -m "docs: AD-093 automatic queueing, and the DEV deploy and backfill record

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

Note: `knowledge/decision-log.md` already has unrelated uncommitted edits from earlier today. Stage only the AD-093 hunk with `git add -p knowledge/decision-log.md` if those edits are still uncommitted; otherwise `git add` the file.
