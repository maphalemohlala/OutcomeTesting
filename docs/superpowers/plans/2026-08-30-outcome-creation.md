# Outcome Creation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Submitting an AQS review creates the initial Outcome and moves the case to the state its grade demands, so the remediation loop that already exists becomes reachable.

**Architecture:** All decision logic lives in a pure static `OutcomeRules` class with no Dataverse dependency, unit-tested without a fake organisation service — the same pattern as `ResponseRules` and `CaseLifecycle`. `SubmitReviewPlugin` stays thin wiring over it, creating the Outcome and transitioning the case inside the existing submit transaction so a review can never be Submitted without its Outcome. A separate `al_SetFailAccountability` command records the AD-039 fail attribution afterwards, and `al_GenerateExport` refuses to export a non-pass that records none.

**Tech Stack:** Dataverse plug-ins (C#, net462, Microsoft.CrmSdk.CoreAssemblies 9.0.2), xUnit, PAC CLI 2.11.2.

**Spec:** `docs/superpowers/specs/2026-08-30-outcome-creation-design.md`

## Global Constraints

- Never invent a business rule. Cite the requirement ID from `knowledge/requirements-index.md`, and name the blocking OD ID from `knowledge/decision-log.md` rather than assuming a resolution.
- Business logic lives in shared server-side commands and is never duplicated per front end (AD-003).
- Security is enforced in Dataverse permissions, never only in UI code (NFR-SEC-01).
- Do not hardcode secrets, tenant/environment IDs, URLs, connection IDs, email addresses or group IDs.
- Entity XML is authored by importing a first draft, then replacing the local file with the version Dataverse emits on export (AD-013). Hand-written entity XML is rejected for subtle reasons.
- Every business code column is declared with `MaxLength` 100 (AD-026).
- Refusal messages use the established prefixes the portal branches on: `PRECONDITION:`, `UNAUTHORIZED:`, `CONFLICT:`, `VALIDATION:` (PP-16). Technical detail stays in the Audit Event, never on screen (NFR-OBS-01).
- Reuse the option-set constants already on `ResponseRules`. Do not redeclare `ChoicePass`, `ChoiceFail`, `ChoiceInsufficient`, `ChoicePassWithIssues`, `ChoicePotentialHarm`, `ReviewTypeTax`, `ReviewTypeAqs`, `OwnerRoleTaxTeam` or `OwnerRoleAqsChecker`.
- Target framework is net462 and the language level is old: no target-typed `new`, no records, no switch expressions. Match the surrounding style — explicit types and `out` parameters.

## File Structure

| File | Responsibility |
|---|---|
| `plugins/OutcomeTesting.Plugins/OutcomeRules.cs` | **New.** Pure grade mapping, remediation predicates, discipline mapping, case-status transitions. No Dataverse types. |
| `plugins/OutcomeTesting.Plugins.Tests/OutcomeRulesTests.cs` | **New.** Unit tests for the above. |
| `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs` | **Modify.** Discipline-scoped mandatory gate, Tax-before-AQS precondition, Outcome creation, case transition. |
| `plugins/OutcomeTesting.Plugins/SetFailAccountabilityPlugin.cs` | **New.** The `al_SetFailAccountability` command. |
| `plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs` | **Modify.** Populate AD-039 columns 11-14 and 17-20; refuse a non-pass with no accountability. |
| `src/Entities/al_Outcome/Entity.xml` | **Modify.** Four accountability flags. |
| `data/route-seed/` | **New.** Three `al_ReviewRoute` rows. |
| `plugins/customapi/al_SetFailAccountability.customapi.json` | **New.** Command contract. |

---

### Task 1: `OutcomeRules`, and the lifecycle edge it needs

The whole decision surface of this feature, as pure functions. Everything later tasks do is wiring over this, so it is built and proven first.

**`CaseLifecycle` is missing an edge this design needs.** Today `Assigned` and `Review In Progress` can only move to `Submitted` or `No Check Required`. The Tax handoff in spec §2.2 moves the case to `Queued` when a discipline finishes and another is still required, and the guard would refuse it. That edge is added here, and AD-057 amended to describe it — the table was written before this design existed and genuinely did not cover a two-discipline case.

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/OutcomeRules.cs`
- Create: `plugins/OutcomeTesting.Plugins.Tests/OutcomeRulesTests.cs`
- Modify: `plugins/OutcomeTesting.Plugins/CaseLifecycle.cs:89-91`
- Modify: `plugins/OutcomeTesting.Plugins.Tests/CaseLifecycleTests.cs`
- Modify: `knowledge/decision-log.md` (amend AD-057)

**Interfaces:**
- Consumes: `ResponseRules.ChoicePass` `120910300`, `ChoiceFail` `120910301`, `ChoiceInsufficient` `120910302`, `ChoicePassWithIssues` `120910303`, `ChoicePotentialHarm` `120910304`, `ReviewTypeTax` `120910200`, `ReviewTypeAqs` `120910201`, `OwnerRoleTaxTeam` `120910100`, `OwnerRoleAqsChecker` `120910101`. `CaseLifecycle.Queued` `120910583`, `AwaitingRemediation` `120910587`, `Closed` `120910591`.
- Produces:
  - `bool OutcomeRules.TryGradeFromAnswer(int answerChoice, out int outcome)`
  - `bool OutcomeRules.RequiresRemediation(int outcome)`
  - `bool OutcomeRules.TaxResultRequiresRemediation(int answerChoice)`
  - `bool OutcomeRules.TryOwnerRoleForReviewType(int reviewType, out int ownerRole)`
  - `int OutcomeRules.NextCaseStatusForAqs(int outcome)`
  - `int OutcomeRules.NextCaseStatusForTax(int answerChoice, bool aqsStillToCome)`
  - Constants `OutcomePass` `120910700`, `OutcomePassWithIssues` `120910701`, `OutcomeInsufficient` `120910702`, `OutcomePotentialHarm` `120910703`

- [ ] **Step 1: Add the failing lifecycle test**

In `plugins/OutcomeTesting.Plugins.Tests/CaseLifecycleTests.cs`, add:

```csharp
        [Theory]
        [InlineData(CaseLifecycle.Assigned)]
        [InlineData(CaseLifecycle.ReviewInProgress)]
        public void Returns_a_case_to_the_queue_when_another_discipline_is_still_required(int from)
        {
            // BR-004: Tax and AQS run sequentially. When Tax submits on a Tax-then-AQS
            // route the case goes back to the shared queue for the AQS checker to be
            // assigned (BR-003, AD-040), so this is not a backwards move — it is the
            // handoff between two disciplines on one case.
            Assert.True(CaseLifecycle.IsAllowed(from, CaseLifecycle.Queued));
        }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd plugins/OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: FAIL — the two new cases assert True and get False.

- [ ] **Step 3: Add the edge**

In `plugins/OutcomeTesting.Plugins/CaseLifecycle.cs`, change two rows of the `Allowed` table:

```csharp
            { Assigned, new[] { ReviewInProgress, Queued, NoCheckRequired } },
            { ReviewInProgress, new[] { Submitted, Queued, NoCheckRequired } },
```

And extend the `<summary>` on `Allowed` with a paragraph describing it:

```
        /// A case returns to Queued from Assigned or Review In Progress when one
        /// discipline has finished and another is still required — the Tax-then-AQS
        /// handoff (BR-004). Allocation is manual (BR-003, AD-040), so the case goes back
        /// to the shared queue for a manager to assign the AQS checker rather than moving
        /// straight to a named person. This is a handoff, not a backwards step.
```

Then amend AD-057 in `knowledge/decision-log.md` to record the edge and why, so the log describes the table that exists.

- [ ] **Step 4: Run it to verify it passes**

Run: `cd plugins/OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: PASS. `Refuses_walking_the_lifecycle_backwards` still passes — it asserts `Assigned -> Queued` is refused, so **that case must be removed from it**, since the edge now exists deliberately. Remove only that one `InlineData`, leaving `Submitted -> Review In Progress` and `Awaiting Recheck -> Awaiting Remediation`.

- [ ] **Step 5: Write the failing `OutcomeRules` tests**

Create `plugins/OutcomeTesting.Plugins.Tests/OutcomeRulesTests.cs`:

```csharp
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// BR-005 grading, BR-006 remediation, BR-004 sequencing and the AD-057 lifecycle
    /// transitions a submit may produce. Every case names the rule it comes from.
    /// </summary>
    public class OutcomeRulesTests
    {
        [Theory]
        [InlineData(ResponseRules.ChoicePass, OutcomeRules.OutcomePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues, OutcomeRules.OutcomePassWithIssues)]
        [InlineData(ResponseRules.ChoiceInsufficient, OutcomeRules.OutcomeInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm, OutcomeRules.OutcomePotentialHarm)]
        public void Maps_every_Q_GR_01_answer_to_its_BR_005_outcome(int answer, int expected)
        {
            int outcome;
            Assert.True(OutcomeRules.TryGradeFromAnswer(answer, out outcome));
            Assert.Equal(expected, outcome);
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceFail)]
        [InlineData(ResponseRules.ChoiceYes)]
        [InlineData(ResponseRules.ChoiceNa)]
        [InlineData(999)]
        public void Refuses_an_answer_the_grade_scale_does_not_contain(int answer)
        {
            // Never defaults to Pass: a grade the model does not recognise must not
            // silently become the most favourable one.
            int outcome;
            Assert.False(OutcomeRules.TryGradeFromAnswer(answer, out outcome));
        }

        [Theory]
        [InlineData(OutcomeRules.OutcomePassWithIssues)]
        [InlineData(OutcomeRules.OutcomeInsufficient)]
        [InlineData(OutcomeRules.OutcomePotentialHarm)]
        public void Requires_remediation_for_every_non_pass(int outcome)
        {
            Assert.True(OutcomeRules.RequiresRemediation(outcome));
        }

        [Fact]
        public void Does_not_require_remediation_for_a_pass()
        {
            Assert.False(OutcomeRules.RequiresRemediation(OutcomeRules.OutcomePass));
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceFail, true)]
        [InlineData(ResponseRules.ChoiceInsufficient, true)]
        [InlineData(ResponseRules.ChoicePass, false)]
        public void Sends_a_non_pass_tax_check_to_remediation(int answer, bool expected)
        {
            // "A completed Tax check with a non-pass result enters remediation"
            // — project-context.md, confirmed workflow step 5.
            Assert.Equal(expected, OutcomeRules.TaxResultRequiresRemediation(answer));
        }

        [Theory]
        [InlineData(ResponseRules.ReviewTypeTax, ResponseRules.OwnerRoleTaxTeam)]
        [InlineData(ResponseRules.ReviewTypeAqs, ResponseRules.OwnerRoleAqsChecker)]
        public void Maps_a_review_discipline_to_the_sections_it_owns(int reviewType, int expected)
        {
            int ownerRole;
            Assert.True(OutcomeRules.TryOwnerRoleForReviewType(reviewType, out ownerRole));
            Assert.Equal(expected, ownerRole);
        }

        [Fact]
        public void Refuses_a_review_type_that_owns_no_sections()
        {
            // An unrecognised discipline is a configuration fault, not a review with
            // nothing to answer.
            int ownerRole;
            Assert.False(OutcomeRules.TryOwnerRoleForReviewType(999, out ownerRole));
        }

        [Fact]
        public void Closes_a_case_on_an_aqs_pass()
        {
            Assert.Equal(CaseLifecycle.Closed, OutcomeRules.NextCaseStatusForAqs(OutcomeRules.OutcomePass));
        }

        [Theory]
        [InlineData(OutcomeRules.OutcomePassWithIssues)]
        [InlineData(OutcomeRules.OutcomeInsufficient)]
        [InlineData(OutcomeRules.OutcomePotentialHarm)]
        public void Sends_a_non_pass_aqs_case_to_remediation(int outcome)
        {
            Assert.Equal(CaseLifecycle.AwaitingRemediation, OutcomeRules.NextCaseStatusForAqs(outcome));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoiceFail)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        public void Queues_a_case_for_aqs_allocation_whatever_the_tax_result(int answer)
        {
            // BR-003, AD-040: allocation is manual, so the handoff is real work to
            // allocate rather than a case parked with nobody working it.
            Assert.Equal(CaseLifecycle.Queued, OutcomeRules.NextCaseStatusForTax(answer, true));
        }

        [Fact]
        public void Closes_a_tax_only_case_that_passed()
        {
            Assert.Equal(CaseLifecycle.Closed, OutcomeRules.NextCaseStatusForTax(ResponseRules.ChoicePass, false));
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceFail)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        public void Sends_a_tax_only_non_pass_to_remediation(int answer)
        {
            Assert.Equal(CaseLifecycle.AwaitingRemediation, OutcomeRules.NextCaseStatusForTax(answer, false));
        }

        [Theory]
        [InlineData(OutcomeRules.OutcomePass)]
        [InlineData(OutcomeRules.OutcomePassWithIssues)]
        [InlineData(OutcomeRules.OutcomeInsufficient)]
        [InlineData(OutcomeRules.OutcomePotentialHarm)]
        public void Only_ever_returns_a_transition_the_lifecycle_permits_from_Submitted(int outcome)
        {
            // Stops the two tables drifting apart: every status a submit can produce
            // must be reachable from Submitted per AD-057.
            var next = OutcomeRules.NextCaseStatusForAqs(outcome);
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.Submitted, next));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass, false)]
        [InlineData(ResponseRules.ChoiceFail, false)]
        public void Tax_finalisation_is_also_reachable_from_Submitted(int answer, bool aqsStillToCome)
        {
            var next = OutcomeRules.NextCaseStatusForTax(answer, aqsStillToCome);
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.Submitted, next));
        }

        [Fact]
        public void Tax_handoff_to_the_queue_is_reachable_from_Review_In_Progress()
        {
            // The case is still being reviewed when Tax submits on a two-stage route.
            var next = OutcomeRules.NextCaseStatusForTax(ResponseRules.ChoicePass, true);
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.ReviewInProgress, next));
        }
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail**

Run: `cd plugins/OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: FAIL — `error CS0103: The name 'OutcomeRules' does not exist in the current context`

- [ ] **Step 7: Write the implementation**

Create `plugins/OutcomeTesting.Plugins/OutcomeRules.cs`:

```csharp
namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Pure decision logic for grading a submitted review (BR-004 to BR-007). Deliberately
    /// free of Dataverse types so it can be unit-tested without a fake organisation
    /// service; SubmitReviewPlugin is thin wiring over this. Mirrors ResponseRules and
    /// CaseLifecycle.
    ///
    /// Option-set values that already exist on ResponseRules are reused rather than
    /// redeclared, so an answer value has one definition in the assembly.
    /// </summary>
    public static class OutcomeRules
    {
        // al_outcome.al_initialoutcome / al_finaloutcome — the BR-005 four-value scale.
        public const int OutcomePass = 120910700;
        public const int OutcomePassWithIssues = 120910701;
        public const int OutcomeInsufficient = 120910702;
        public const int OutcomePotentialHarm = 120910703;

        /// <summary>
        /// Maps the Q-GR-01 "Advice Quality Grade" answer to the outcome it records.
        ///
        /// Returns false rather than a default for any value outside the grade scale.
        /// Defaulting would turn an unrecognised answer into a Pass, which is the most
        /// favourable outcome and the one least likely to be noticed.
        /// </summary>
        public static bool TryGradeFromAnswer(int answerChoice, out int outcome)
        {
            switch (answerChoice)
            {
                case ResponseRules.ChoicePass:
                    outcome = OutcomePass;
                    return true;
                case ResponseRules.ChoicePassWithIssues:
                    outcome = OutcomePassWithIssues;
                    return true;
                case ResponseRules.ChoiceInsufficient:
                    outcome = OutcomeInsufficient;
                    return true;
                case ResponseRules.ChoicePotentialHarm:
                    outcome = OutcomePotentialHarm;
                    return true;
                default:
                    outcome = 0;
                    return false;
            }
        }

        /// <summary>BR-006: every non-pass outcome requires remediation.</summary>
        public static bool RequiresRemediation(int outcome)
        {
            return outcome != OutcomePass;
        }

        /// <summary>
        /// Whether a Q-TAX-02 result sends the case to remediation. The Tax scale is
        /// PassFailInsufficient (AD-055), and a completed Tax check with a non-pass result
        /// enters remediation rather than being returned or cancelled (AD-006).
        /// </summary>
        public static bool TaxResultRequiresRemediation(int answerChoice)
        {
            return answerChoice == ResponseRules.ChoiceFail
                || answerChoice == ResponseRules.ChoiceInsufficient;
        }

        /// <summary>
        /// The al_section.al_ownerrole a review of this discipline is answerable for
        /// (AD-020). Returns false for a review type the model does not define, so the
        /// caller refuses rather than submitting against an empty mandatory set.
        /// </summary>
        public static bool TryOwnerRoleForReviewType(int reviewType, out int ownerRole)
        {
            switch (reviewType)
            {
                case ResponseRules.ReviewTypeTax:
                    ownerRole = ResponseRules.OwnerRoleTaxTeam;
                    return true;
                case ResponseRules.ReviewTypeAqs:
                    ownerRole = ResponseRules.OwnerRoleAqsChecker;
                    return true;
                default:
                    ownerRole = 0;
                    return false;
            }
        }

        /// <summary>Where an AQS submit leaves the case: closed on a Pass, awaiting
        /// remediation on anything else (BR-006).</summary>
        public static int NextCaseStatusForAqs(int outcome)
        {
            return RequiresRemediation(outcome)
                ? CaseLifecycle.AwaitingRemediation
                : CaseLifecycle.Closed;
        }

        /// <summary>
        /// Where a Tax submit leaves the case. When AQS is still to come the case returns
        /// to the shared queue for manual allocation (BR-003, AD-040) whatever the Tax
        /// result — the Tax outcome does not finalise a case that has not had its advice
        /// quality check. Otherwise the Tax result finalises it.
        /// </summary>
        public static int NextCaseStatusForTax(int answerChoice, bool aqsStillToCome)
        {
            if (aqsStillToCome)
            {
                return CaseLifecycle.Queued;
            }

            return TaxResultRequiresRemediation(answerChoice)
                ? CaseLifecycle.AwaitingRemediation
                : CaseLifecycle.Closed;
        }
    }
}
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `cd plugins/OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: PASS — all previous tests still green (65 before this task).

- [ ] **Step 9: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/OutcomeRules.cs plugins/OutcomeTesting.Plugins.Tests/OutcomeRulesTests.cs plugins/OutcomeTesting.Plugins/CaseLifecycle.cs plugins/OutcomeTesting.Plugins.Tests/CaseLifecycleTests.cs knowledge/decision-log.md
git commit -m "feat(plugins): pure grading rules, and the tax-to-AQS handoff edge (AD-057)"
```

---

### Task 2: Schema delta — accountability flags and route seed rows

**Files:**
- Modify: `src/Entities/al_Outcome/Entity.xml`
- Create: `data/route-seed/data.xml`, `data/route-seed/data_schema.xml`, `data/route-seed/[Content_Types].xml`

**Interfaces:**
- Produces: `al_outcome.al_fqadviseraccountable`, `al_fqparaplanneraccountable`, `al_aqadviseraccountable`, `al_aqparaplanneraccountable` — all Two Options, default No. Three `al_reviewroute` rows keyed `ROUTE-TAX`, `ROUTE-AQS`, `ROUTE-TAX-AQS`.

- [ ] **Step 1: Add the four attributes to the entity XML**

In `src/Entities/al_Outcome/Entity.xml`, inside `<attributes>`, add four attributes modelled exactly on an existing Two Options attribute already in this solution (`al_role/Entity.xml` `al_isactive` is the closest). Each needs `<Type>bit</Type>`, `<RequiredLevel>none</RequiredLevel>`, and an `<optionset>` with `0 No` / `1 Yes` and a default of `0`.

Do not hand-craft the final shape. Per AD-013 this is a first draft to be replaced by what Dataverse emits.

- [ ] **Step 2: Pack and import the solution, then export and replace the file**

Follow AD-011 and AD-013: `pac solution pack` the `src` root, `pac solution import` it unmanaged (which merges rather than replaces), then export the solution and replace `src/Entities/al_Outcome/Entity.xml` with the version Dataverse emits.

Verify the syntax against `pac solution pack --help` for CLI 2.11.2 before running; do not assume flags.

Expected: import succeeds, and the re-exported file contains all four attributes with `<Type>bit</Type>`.

- [ ] **Step 3: Create the route seed package**

Create `data/route-seed/data.xml`, copying the package shape from `data/roles-seed/`:

```xml
<entities>
  <entity name="al_reviewroute" displayname="Review Route">
    <records>
      <record id="cccc0000-0000-4c00-8000-000000000001">
        <field name="al_name" value="Tax only" />
        <field name="al_routecode" value="ROUTE-TAX" />
        <field name="al_requirestaxreview" value="1" />
        <field name="al_requiresaqsreview" value="0" />
        <field name="al_displayorder" value="1" />
      </record>
      <record id="cccc0000-0000-4c00-8000-000000000002">
        <field name="al_name" value="AQS only" />
        <field name="al_routecode" value="ROUTE-AQS" />
        <field name="al_requirestaxreview" value="0" />
        <field name="al_requiresaqsreview" value="1" />
        <field name="al_displayorder" value="2" />
      </record>
      <record id="cccc0000-0000-4c00-8000-000000000003">
        <field name="al_name" value="Tax then AQS" />
        <field name="al_routecode" value="ROUTE-TAX-AQS" />
        <field name="al_requirestaxreview" value="1" />
        <field name="al_requiresaqsreview" value="1" />
        <field name="al_displayorder" value="3" />
      </record>
    </records>
  </entity>
</entities>
```

Copy `data_schema.xml` and `[Content_Types].xml` from `data/roles-seed/`, changing the entity name and fields to match. The schema file must declare `al_routecode` as the `primaryKey` field so the import is idempotent on the alternate key (AD-014, NFR-REL-01).

- [ ] **Step 4: Import the seed and verify exactly three rows**

Run the import the same way the other seed packages are imported, then verify:

```bash
pac org fetch --xmlFile <a fetch selecting al_routecode from al_reviewroute>
```

Expected: exactly three rows, `ROUTE-TAX`, `ROUTE-AQS`, `ROUTE-TAX-AQS`. Run the import a second time and confirm it is still three — that proves idempotency.

- [ ] **Step 5: Commit**

```bash
git add src/Entities/al_Outcome/Entity.xml data/route-seed
git commit -m "feat(schema): fail accountability flags and review route seed rows"
```

---

### Task 3: Scope the mandatory gate to the review's discipline

Fixes the defect that makes a Tax review impossible to submit. Nothing after this task can be tested end to end until it lands.

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs:155-232` (`EnsureMandatoryQuestionsAnswered`)

**Interfaces:**
- Consumes: `OutcomeRules.TryOwnerRoleForReviewType` from Task 1.

- [ ] **Step 1: Change the method to take the review type and filter on owner role**

The current query links `al_questionversion` → `al_question` → `al_section` and filters the section on `al_checklistversionid` only. Add the owner-role condition to that same `sectionLink`, and take the review type from the review the caller already loaded.

Change the signature from:

```csharp
private static void EnsureMandatoryQuestionsAnswered(
    IOrganizationService service,
    Guid targetId,
    Entity review)
```

Keep that signature — the review entity is already passed — and add at the top of the method body, immediately after the `checklistVersion` null check:

```csharp
            // Mandatory questions are scoped to the sections this discipline owns
            // (AD-020). Without this the gate demands all 42 mandatory answers whatever
            // the review type, so a Tax review is held by AQS questions its reviewer
            // cannot see, and an AQS review is held by the two Tax questions. The portal
            // already filters sections by owner role (AD-056); this is the server half.
            var reviewType = review.GetAttributeValue<OptionSetValue>(ReviewType);
            int ownerRole;
            if (reviewType == null || !OutcomeRules.TryOwnerRoleForReviewType(reviewType.Value, out ownerRole))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This review has no recognised discipline, so the questions it must answer cannot be determined.");
            }
```

Then add one condition to the existing `sectionLink`:

```csharp
            sectionLink.LinkCriteria.AddCondition(
                "al_ownerrole", ConditionOperator.Equal, ownerRole);
```

- [ ] **Step 2: Add the review type constant and include it in the retrieve**

Add next to the other review constants near line 35:

```csharp
        private const string ReviewType = "al_reviewtype";
```

Find the `service.Retrieve(ReviewEntity, targetId, new ColumnSet(...))` call in `ExecuteDataversePlugin` and add `ReviewType` to its `ColumnSet` so `review` actually carries it.

- [ ] **Step 3: Build**

Run: `cd plugins/OutcomeTesting.Plugins && dotnet build --nologo -v q`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run the full test suite**

Run: `cd plugins/OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: PASS, no regressions.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs
git commit -m "fix(plugins): scope the submit gate to the review's own sections"
```

---

### Task 4: Refuse an AQS submit while Tax is outstanding

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs`

**Interfaces:**
- Produces: `private static void EnsureTaxPrecedesAqs(IOrganizationService service, Entity review)`

- [ ] **Step 1: Add the precondition method**

Add to `SubmitReviewPlugin`, next to the other private helpers:

```csharp
        /// <summary>
        /// Tax and AQS run sequentially when both are required (BR-004). Without this an
        /// AQS review could be graded and its case closed while the Tax check was still
        /// open, producing an Outcome for a case whose Tax check never happened.
        ///
        /// Sibling instances are read from the case rather than trusted from al_sequence,
        /// because sequence is data a caller can set and the invariant has to hold anyway.
        /// A Tax submit is never refused here: Tax is always first.
        /// </summary>
        private static void EnsureTaxPrecedesAqs(IOrganizationService service, Entity review)
        {
            var reviewType = review.GetAttributeValue<OptionSetValue>(ReviewType);
            if (reviewType == null || reviewType.Value != ResponseRules.ReviewTypeAqs)
            {
                return;
            }

            var caseRef = review.GetAttributeValue<EntityReference>(ReviewOutcomeCase);
            if (caseRef == null)
            {
                return;
            }

            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ReviewOutcomeCase, ConditionOperator.Equal, caseRef.Id);
            query.Criteria.AddCondition(ReviewType, ConditionOperator.Equal, ResponseRules.ReviewTypeTax);
            query.Criteria.AddCondition(ReviewStatus, ConditionOperator.NotEqual, StatusSubmitted);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "The Tax check on this case has not been submitted yet. Tax must be completed before the AQS review (BR-004).");
            }
        }
```

- [ ] **Step 2: Add the case lookup constant and include it in the retrieve**

Add next to the other review constants:

```csharp
        private const string ReviewOutcomeCase = "al_outcomecaseid";
```

Add `ReviewOutcomeCase` to the `ColumnSet` of the review retrieve in `ExecuteDataversePlugin`.

- [ ] **Step 3: Call it before the status update**

In `ExecuteDataversePlugin`, immediately after the existing `EnsureMandatoryQuestionsAnswered(service, targetId, review);` line:

```csharp
            EnsureTaxPrecedesAqs(service, review);
```

- [ ] **Step 4: Build and test**

Run: `cd plugins/OutcomeTesting.Plugins && dotnet build --nologo -v q` then `cd ../OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: `Build succeeded. 0 Error(s)`, tests PASS.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs
git commit -m "fix(plugins): require the tax check before the AQS review (BR-004)"
```

---

### Task 5: Create the Outcome and move the case

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs`

**Interfaces:**
- Consumes: `OutcomeRules.TryGradeFromAnswer`, `NextCaseStatusForAqs`, `NextCaseStatusForTax` from Task 1. `CaseLifecycle.IsAllowed`, `CaseLifecycle.DescribeRefusal`.
- Produces: `private static void FinaliseReview(IOrganizationService service, Entity review, Guid targetId)`, `private static bool AqsStillToCome(IOrganizationService service, Guid caseId)`, `private static void MoveCaseThrough(IOrganizationService service, Guid caseId, int nextStatus)`, `private static int? AnswerChoiceFor(IOrganizationService service, Guid reviewId, string questionCode)`

- [ ] **Step 1: Add the answer-resolution helper**

This is the same traversal `GenerateExportPlugin.ResolveFileQualityGrade` already uses — Response → QuestionVersion → Question, matched on the business code so a retired-and-succeeded question keeps working (BR-013, AD-004).

```csharp
        /// <summary>
        /// The al_answerchoice this review recorded for a question, by business code.
        /// Returns null when the question was not answered.
        /// </summary>
        private static int? AnswerChoiceFor(IOrganizationService service, Guid reviewId, string questionCode)
        {
            var query = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet("al_answerchoice"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

            var version = query.AddLink(QuestionVersionEntity, "al_questionversionid", "al_questionversionid");
            var question = version.AddLink("al_question", "al_questionid", "al_questionid");
            question.LinkCriteria.AddCondition("al_questioncode", ConditionOperator.Equal, questionCode);

            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return null;
            }

            var choice = found[0].GetAttributeValue<OptionSetValue>("al_answerchoice");
            return choice == null ? (int?)null : choice.Value;
        }
```

- [ ] **Step 2: Add the "is AQS still to come" helper**

```csharp
        /// <summary>
        /// Whether an AQS review is still owed on this case. The route decides when it is
        /// set; where it is null — which is every case created before the route seed
        /// existed — fall back to the review instances that actually exist, so a Tax
        /// submit on a case with no AQS instance finalises as Tax-only rather than
        /// stalling in the queue forever.
        /// </summary>
        private static bool AqsStillToCome(IOrganizationService service, Guid caseId)
        {
            var routeRef = null as EntityReference;
            var outcomeCase = service.Retrieve(CaseEntity, caseId, new ColumnSet("al_reviewrouteid"));
            routeRef = outcomeCase.GetAttributeValue<EntityReference>("al_reviewrouteid");

            if (routeRef != null)
            {
                var route = service.Retrieve("al_reviewroute", routeRef.Id, new ColumnSet("al_requiresaqsreview"));
                if (!(route.GetAttributeValue<bool?>("al_requiresaqsreview") ?? false))
                {
                    return false;
                }
            }

            // Either the route requires AQS, or there is no route to ask. Both are
            // answered the same way: is there an AQS instance that has not been submitted?
            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ReviewOutcomeCase, ConditionOperator.Equal, caseId);
            query.Criteria.AddCondition(ReviewType, ConditionOperator.Equal, ResponseRules.ReviewTypeAqs);
            query.Criteria.AddCondition(ReviewStatus, ConditionOperator.NotEqual, StatusSubmitted);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                return true;
            }

            // No unsubmitted AQS instance. If the route demanded one it has not been
            // created yet, so it is still to come; with no route, this is Tax-only.
            return routeRef != null;
        }
```

Add the constant `private const string CaseEntity = "al_outcomecase";` next to the other entity names.

- [ ] **Step 3: Add the finalisation method**

```csharp
        /// <summary>
        /// Records what the submission produced: for AQS the initial Outcome (BR-005,
        /// BR-007), and for both disciplines the case's next status (AD-057). Runs inside
        /// the submit transaction, so a review can never be Submitted without its Outcome.
        ///
        /// A Tax review creates no Outcome: al_Outcome.al_initialoutcome carries only the
        /// BR-005 four-value AQS scale, and the Tax result is the three-value
        /// PassFailInsufficient scale of Q-TAX-02 (AD-055). The Tax grade stays on its
        /// response, and AD-039's export contract has no Tax column.
        /// </summary>
        private static void FinaliseReview(IOrganizationService service, Entity review, Guid targetId)
        {
            var caseRef = review.GetAttributeValue<EntityReference>(ReviewOutcomeCase);
            if (caseRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This review is not linked to a case, so its outcome cannot be recorded.");
            }

            var reviewType = review.GetAttributeValue<OptionSetValue>(ReviewType).Value;
            int nextStatus;

            if (reviewType == ResponseRules.ReviewTypeAqs)
            {
                var answer = AnswerChoiceFor(service, targetId, GradeQuestionCode);
                if (!answer.HasValue)
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The advice quality grade has not been recorded, so this review cannot be submitted.");
                }

                int outcomeValue;
                if (!OutcomeRules.TryGradeFromAnswer(answer.Value, out outcomeValue))
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The advice quality grade holds a value this solution does not recognise (" + answer.Value + ").");
                }

                CreateOutcome(service, review, targetId, caseRef, outcomeValue);
                // Through Submitted, then on: the lifecycle has no edge from Review In
                // Progress straight to Awaiting Remediation or Closed, and it should not —
                // the case really is submitted before it is triaged (AD-057).
                MoveCaseThrough(service, caseRef.Id, CaseLifecycle.Submitted);
                nextStatus = OutcomeRules.NextCaseStatusForAqs(outcomeValue);
            }
            else
            {
                var answer = AnswerChoiceFor(service, targetId, TaxOutcomeQuestionCode);
                if (!answer.HasValue)
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The tax check outcome has not been recorded, so this review cannot be submitted.");
                }

                var aqsStillToCome = AqsStillToCome(service, caseRef.Id);
                nextStatus = OutcomeRules.NextCaseStatusForTax(answer.Value, aqsStillToCome);

                // A Tax handoff goes straight to the queue — the case is not submitted,
                // only its Tax review is. A Tax-only case is submitted, then triaged.
                if (!aqsStillToCome)
                {
                    MoveCaseThrough(service, caseRef.Id, CaseLifecycle.Submitted);
                }
            }

            MoveCaseThrough(service, caseRef.Id, nextStatus);
        }
```

Add the constants:

```csharp
        private const string GradeQuestionCode = "Q-GR-01";
        private const string TaxOutcomeQuestionCode = "Q-TAX-02";
        private const string OutcomeEntity = "al_outcome";
```

- [ ] **Step 4: Add the Outcome creation and case move helpers**

```csharp
        /// <summary>
        /// Writes the initial Outcome. The code is derived from the case reference and the
        /// review sequence, so a replay upserts the same row on the al_outcomecode
        /// alternate key rather than creating a second Outcome (NFR-REL-01).
        /// Only the initial columns are written: the final outcome is the regrade path's
        /// to set, and BR-007 requires both to be preserved separately.
        /// </summary>
        private static void CreateOutcome(
            IOrganizationService service, Entity review, Guid reviewId, EntityReference caseRef, int outcomeValue)
        {
            var outcomeCase = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet("al_casereference"));
            var caseReference = outcomeCase.GetAttributeValue<string>("al_casereference") ?? caseRef.Id.ToString("D");
            var sequence = review.GetAttributeValue<int?>("al_sequence") ?? 1;
            var code = "OUT-" + caseReference + "-" + sequence;

            var outcome = new Entity(OutcomeEntity)
            {
                ["al_name"] = "Outcome " + caseReference,
                ["al_outcomecode"] = code,
                ["al_outcomecaseid"] = caseRef,
                ["al_reviewinstanceid"] = new EntityReference(ReviewEntity, reviewId),
                ["al_initialoutcome"] = new OptionSetValue(outcomeValue),
            };

            AssignUserRolePlugin.Upsert(service, OutcomeEntity, "al_outcomecode", code, outcome);
        }

        /// <summary>
        /// Moves the case one hop, refusing any transition the lifecycle does not describe
        /// (AD-057). Called once per hop rather than jumping, because the lifecycle is a
        /// sequence and skipping a state is exactly what AD-057 exists to prevent. A hop
        /// that is already satisfied is a no-op, so a re-run is harmless.
        /// </summary>
        private static void MoveCaseThrough(IOrganizationService service, Guid caseId, int nextStatus)
        {
            var outcomeCase = service.Retrieve(CaseEntity, caseId, new ColumnSet("al_casestatus"));
            var current = outcomeCase.GetAttributeValue<OptionSetValue>("al_casestatus");
            int? from = current != null ? current.Value : (int?)null;

            if (from.HasValue && from.Value == nextStatus)
            {
                return;
            }

            if (!CaseLifecycle.IsAllowed(from, nextStatus))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + CaseLifecycle.DescribeRefusal(from, nextStatus));
            }

            service.Update(new Entity(CaseEntity, caseId)
            {
                ["al_casestatus"] = new OptionSetValue(nextStatus),
            });
        }
```

- [ ] **Step 5: Call it, and add `al_sequence` to the retrieve**

In `ExecuteDataversePlugin`, immediately after the review status update block and before `var auditId = WriteAuditEvent(...)`:

```csharp
            FinaliseReview(service, review, targetId);
```

Add `"al_sequence"` to the `ColumnSet` of the review retrieve.

- [ ] **Step 6: Build and test**

Run: `cd plugins/OutcomeTesting.Plugins && dotnet build --nologo -v q` then `cd ../OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: `Build succeeded. 0 Error(s)`, tests PASS.

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs
git commit -m "feat(plugins): create the initial outcome and move the case on submit"
```

---

### Task 6: `al_SetFailAccountability`

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/SetFailAccountabilityPlugin.cs`
- Create: `plugins/customapi/al_SetFailAccountability.customapi.json`

**Interfaces:**
- Consumes: `CommandHelpers.GetRequiredString`, `ParseRequiredGuid`, `FindAuditByKey(service, key, command)`, `WriteAuditEvent(service, command, name, targetTable, targetId, reason, details, idempotencyKey, context)`, `PermissionHelpers.EnsureAppPermission(systemService, context, resourceKey, requiredLevel)`, `PermissionHelpers.AccessEdit`.
- Produces: Custom API `al_SetFailAccountability`, request parameters `TargetId`, `FqAdviser`, `FqParaplanner`, `AqAdviser`, `AqParaplanner`, `IdempotencyKey`; response `Status`, `AuditEventId`.

- [ ] **Step 1: Write the plug-in**

Create `plugins/OutcomeTesting.Plugins/SetFailAccountabilityPlugin.cs`, modelled on `SetUserActivePlugin` for shape:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SetFailAccountability (AD-003, OD-024). Records who is
    /// accountable for a File Quality or Advice Quality fail on an Outcome, which is the
    /// judgement AD-039's export columns 11-14 and 17-20 report and which nothing in the
    /// model captured before.
    ///
    /// Set after submission rather than during the review, because the Outcome does not
    /// exist until the review is submitted. al_GenerateExport refuses a non-pass Outcome
    /// that records no accountability, so the step cannot be silently skipped.
    /// </summary>
    public class SetFailAccountabilityPlugin : PluginBase
    {
        private const string InTargetId = "TargetId";
        private const string InFqAdviser = "FqAdviser";
        private const string InFqParaplanner = "FqParaplanner";
        private const string InAqAdviser = "AqAdviser";
        private const string InAqParaplanner = "AqParaplanner";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";

        private const string OutcomeEntity = "al_outcome";
        private const int CommandSetFailAccountability = 120910788;

        public SetFailAccountabilityPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SetFailAccountabilityPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var targetId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var fqAdviser = GetBool(context, InFqAdviser);
            var fqParaplanner = GetBool(context, InFqParaplanner);
            var aqAdviser = GetBool(context, InAqAdviser);
            var aqParaplanner = GetBool(context, InAqParaplanner);

            PermissionHelpers.EnsureAppPermission(systemService, context, "page.cases", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandSetFailAccountability);
            if (existingAudit != null)
            {
                SetResponse(context, "Recorded", existingAudit.Id);
                return;
            }

            var outcome = userService.Retrieve(OutcomeEntity, targetId, new ColumnSet("al_initialoutcome", "al_finaloutcome"));

            // Accountability describes a fail. Recording it against a Pass would put a
            // name in a Trail Light column that AD-039 only ever fills for a fail.
            var effective = outcome.GetAttributeValue<OptionSetValue>("al_finaloutcome")
                ?? outcome.GetAttributeValue<OptionSetValue>("al_initialoutcome");
            if (effective == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This case has no outcome recorded, so there is no fail to attribute.");
            }
            if (!OutcomeRules.RequiresRemediation(effective.Value))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "This case passed, so there is no fail to attribute.");
            }

            userService.Update(new Entity(OutcomeEntity, targetId)
            {
                ["al_fqadviseraccountable"] = fqAdviser,
                ["al_fqparaplanneraccountable"] = fqParaplanner,
                ["al_aqadviseraccountable"] = aqAdviser,
                ["al_aqparaplanneraccountable"] = aqParaplanner,
            });

            var details = "FQ adviser " + fqAdviser + ", FQ paraplanner " + fqParaplanner
                + ", AQ adviser " + aqAdviser + ", AQ paraplanner " + aqParaplanner;

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandSetFailAccountability, "SetFailAccountability", OutcomeEntity, targetId,
                null, details, idempotencyKey, context);

            SetResponse(context, "Recorded", auditId);
        }

        private static bool GetBool(IPluginExecutionContext context, string name)
        {
            object value;
            if (context.InputParameters.TryGetValue(name, out value) && value is bool)
            {
                return (bool)value;
            }

            throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + name + " is required.");
        }

        private static void SetResponse(IPluginExecutionContext context, string status, Guid auditId)
        {
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
```

Before building, confirm `120910788` is free in the `al_auditevent.al_command` option set by reading `src/Entities/al_AuditEvent/Entity.xml`. If it is taken, use the next free value and say so in the commit message.

- [ ] **Step 2: Write the contract**

Create `plugins/customapi/al_SetFailAccountability.customapi.json`, matching the shape of `plugins/customapi/al_CreateRole.customapi.json`. `bindingtype` 0, `isfunction` false, `isprivate` false, `pluginType` `OutcomeTesting.Plugins.SetFailAccountabilityPlugin`. Request parameters: `TargetId` type 10 required, `FqAdviser` / `FqParaplanner` / `AqAdviser` / `AqParaplanner` type 0 required, `IdempotencyKey` type 10 required. Response properties: `Status` type 10, `AuditEventId` type 10. Type codes are `0=Boolean, 10=String`.

- [ ] **Step 3: Build and test**

Run: `cd plugins/OutcomeTesting.Plugins && dotnet build --nologo -v q` then `cd ../OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: `Build succeeded. 0 Error(s)`, tests PASS.

- [ ] **Step 4: Register the command**

Push the assembly and create the Custom API from the contract, the way the other commands are registered:

```bash
pac plugin push --pluginId 7b51d0d1-f5a1-f111-b8dd-e4fade069307 --pluginFile plugins/OutcomeTesting.Plugins/bin/Debug/net462/OutcomeTesting.Plugins.dll --type Assembly
```

Then create the plug-in type and Custom API. `pac plugin push` does not create plug-in types for classes the environment has not seen (AD-052), so the type row must be created explicitly — `plugins/deploy/Register-ResponseGuard.ps1` has the `Resolve-PluginType` function that does this.

Verify with a fetch on `customapi` filtered to `uniquename eq 'al_SetFailAccountability'`.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/SetFailAccountabilityPlugin.cs plugins/customapi/al_SetFailAccountability.customapi.json
git commit -m "feat(plugins): record fail accountability on an outcome (OD-024)"
```

---

### Task 7: Export the accountability columns

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs`

**Interfaces:**
- Consumes: `OutcomeRules.RequiresRemediation`. The four flags from Task 2.

- [ ] **Step 1: Extend `ResolveAdviceGrade` to return the flags too**

`ResolveAdviceGrade` already retrieves the Outcome for a case. Change its `ColumnSet` to include the four flags and the raw outcome value, and return the entity rather than the formatted string, so the caller has both the grade and the accountability without a second read. Rename it `ResolveOutcome` and have the caller take `FormattedValues` from it as before.

- [ ] **Step 2: Refuse a non-pass with no accountability**

Inside the per-case loop, after the Outcome is resolved:

```csharp
                // A non-pass with no accountability recorded would export four blank
                // accountability pairs, which reads as "nobody is responsible" rather
                // than "nobody has said yet". Refusing here keeps an incomplete row out
                // of a delivered Trail Light file (AD-039, OD-024).
                if (outcomeRow != null)
                {
                    var effective = outcomeRow.GetAttributeValue<OptionSetValue>("al_finaloutcome")
                        ?? outcomeRow.GetAttributeValue<OptionSetValue>("al_initialoutcome");
                    var anyAccountability =
                        (outcomeRow.GetAttributeValue<bool?>("al_fqadviseraccountable") ?? false)
                        || (outcomeRow.GetAttributeValue<bool?>("al_fqparaplanneraccountable") ?? false)
                        || (outcomeRow.GetAttributeValue<bool?>("al_aqadviseraccountable") ?? false)
                        || (outcomeRow.GetAttributeValue<bool?>("al_aqparaplanneraccountable") ?? false);

                    if (effective != null && OutcomeRules.RequiresRemediation(effective.Value) && !anyAccountability)
                    {
                        throw new InvalidPluginExecutionException(
                            CommandHelpers.PreconditionPrefix
                            + "Case " + caseRef + " has a non-pass outcome with no fail accountability recorded. "
                            + "Record accountability before generating the export.");
                    }
                }
```

- [ ] **Step 3: Populate the eight columns**

In the `record` initialiser, add:

```csharp
                    ["al_fqfailadvisername"] = FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisername"),
                    ["al_fqfailadvisercode"] = FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisercode"),
                    ["al_fqfailparaplannername"] = FlaggedText(outcomeRow, "al_fqparaplanneraccountable", outcomeCase, "al_paraplanner"),
                    ["al_fqfailparaplannercode"] = FlaggedText(outcomeRow, "al_fqparaplanneraccountable", outcomeCase, "al_paraplannercode"),
                    ["al_aqfailadvisername"] = FlaggedText(outcomeRow, "al_aqadviseraccountable", outcomeCase, "al_advisername"),
                    ["al_aqfailadvisercode"] = FlaggedText(outcomeRow, "al_aqadviseraccountable", outcomeCase, "al_advisercode"),
                    ["al_aqfailparaplannername"] = FlaggedText(outcomeRow, "al_aqparaplanneraccountable", outcomeCase, "al_paraplanner"),
                    ["al_aqfailparaplannercode"] = FlaggedText(outcomeRow, "al_aqparaplanneraccountable", outcomeCase, "al_paraplannercode"),
```

And the helper:

```csharp
        /// <summary>
        /// The case value when the Outcome flags that person accountable, otherwise empty.
        /// AD-039 attributes a fail to the adviser and/or the paraplanner, so a pair whose
        /// flag is false is written empty rather than filled in.
        /// </summary>
        private static string FlaggedText(Entity outcomeRow, string flag, Entity outcomeCase, string caseAttribute)
        {
            if (outcomeRow == null || !(outcomeRow.GetAttributeValue<bool?>(flag) ?? false))
            {
                return string.Empty;
            }

            return outcomeCase.GetAttributeValue<string>(caseAttribute) ?? string.Empty;
        }
```

Add `"al_paraplanner"` and `"al_paraplannercode"` to the case query's `ColumnSet` if they are not already there.

- [ ] **Step 4: Build and test**

Run: `cd plugins/OutcomeTesting.Plugins && dotnet build --nologo -v q` then `cd ../OutcomeTesting.Plugins.Tests && dotnet test --nologo`
Expected: `Build succeeded. 0 Error(s)`, tests PASS.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs
git commit -m "feat(plugins): populate the AD-039 fail accountability columns"
```

---

## Traceability

| Requirement | Task |
|---|---|
| BR-004 Tax precedes AQS | 4 |
| BR-005 four outcomes | 1, 5 |
| BR-006 non-pass requires remediation | 1, 5 |
| BR-007 initial outcome preserved | 5 |
| AD-020 section ownership | 1, 3 |
| AD-039 export contract | 2, 6, 7 |
| AD-055 Tax three-value scale | 1 |
| AD-057 lifecycle transitions | 1, 5 |
| OD-024 fail accountability | 2, 6, 7 |
| PP-07, PP-08 questionnaire submit | 3 |
| NFR-REL-01 idempotency | 2, 5, 6 |

## Verification in DEV

After Task 7, run the whole path against a case seeded for the purpose — never a live client case, per the guard added for audit finding I7.

**Deployment is a hard sequence, not a checklist to run in any order.** `al_GenerateExport` refuses a non-pass Outcome that records no accountability, and only `al_SetFailAccountability` can record it. Push the assembly before the schema and route seed are in, or register the command before the assembly exists, and the export starts refusing batches nobody can satisfy — a real support hole if run out of order in DEV. Deploy in exactly this order:

1. Import the Task 2 solution (the AD-013 export-and-replace round trip: pack, import unmanaged, export, replace `src/Entities/al_Outcome/Entity.xml` with what Dataverse emits).
2. Import the route seed package, then import it a second time to prove idempotency (Task 2, Step 4).
3. `pac plugin push` the assembly.
4. Create the plug-in type (`Resolve-PluginType`, since `pac plugin push` does not create one for a class the environment has not seen — AD-052).
5. Create the Custom API `al_SetFailAccountability` from its contract.
6. Only then run the scenarios below.

1. A Tax-only case: submit, confirm no Outcome, confirm the case closed on a Pass.
2. A Tax-then-AQS case: submit Tax, confirm the case is `Queued`; attempt the AQS submit before Tax and confirm it is refused. Then, before submitting AQS for real: a manager must move the case from `Queued` to `Assigned` — allocation is manual (BR-003, AD-040), and the Tax submit only queues the case for allocation, it does not assign the AQS checker. Skip that hand-off and the AQS submit is refused for a second, unrelated reason (the review is not yet in a submittable state), which reads as a bug if you are not expecting it.
3. An AQS Pass: confirm one Outcome with `al_initialoutcome` Pass, case `Closed`.
4. An AQS Potential harm: confirm the Outcome and case `Awaiting Remediation`.
5. Generate an export with a non-pass and no accountability: confirm refusal naming the case; record accountability and confirm it then succeeds with the correct pairs populated and the others blank.

## Deliberately not in this plan

- **Remediation Action generation.** Sub-project 2. This plan stops at the Outcome and the case status.
- **Notifications.** Sub-project 3; `al_Notification` does not exist.
- **The SubmitReview cloud flow.** Creatable only in the maker portal.
- **A portal or Code App control for the accountability flags.** The command is server-side; the UI surface is a later slice.
- **S-CRP conditional rendering.** Blocked by OD-016.
