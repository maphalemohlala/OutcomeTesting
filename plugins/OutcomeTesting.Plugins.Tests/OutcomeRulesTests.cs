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
        [InlineData(ResponseRules.ChoicePass, false)]
        [InlineData(ResponseRules.ChoiceFail, true)]
        [InlineData(ResponseRules.ChoiceInsufficient, true)]
        public void Accepts_every_value_on_the_tax_scale(int answer, bool expected)
        {
            bool requiresRemediation;
            Assert.True(OutcomeRules.TryTaxResultRequiresRemediation(answer, out requiresRemediation));
            Assert.Equal(expected, requiresRemediation);
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        [InlineData(ResponseRules.ChoiceYes)]
        [InlineData(999)]
        public void Refuses_a_tax_result_the_AD_055_scale_does_not_contain(int answer)
        {
            // Never treats an unscaled value as a pass: that closes the case terminally,
            // which is the same failure TryGradeFromAnswer refuses on the AQS side.
            bool requiresRemediation;
            Assert.False(OutcomeRules.TryTaxResultRequiresRemediation(answer, out requiresRemediation));
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

        [Fact]
        public void Queues_a_passed_tax_check_for_aqs_allocation()
        {
            // BR-003, AD-040: allocation is manual, so the handoff is real work to
            // allocate rather than a case parked with nobody working it.
            Assert.Equal(
                CaseLifecycle.Queued,
                OutcomeRules.NextCaseStatusForTax(ResponseRules.ChoicePass, true));
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceFail)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        public void Sends_a_failed_tax_check_to_remediation_even_when_aqs_is_still_to_come(int answer)
        {
            // OD-027: only a passed Tax check hands off to AQS. A non-pass enters
            // remediation whatever the route, so an AQS pass cannot later close the case
            // with the Tax failure unaddressed (BR-006).
            Assert.Equal(
                CaseLifecycle.AwaitingRemediation,
                OutcomeRules.NextCaseStatusForTax(answer, true));
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

        [Theory]
        [InlineData(CaseLifecycle.Assigned, CaseLifecycle.Closed)]
        [InlineData(CaseLifecycle.Assigned, CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.ReviewInProgress, CaseLifecycle.Closed)]
        [InlineData(CaseLifecycle.ReviewInProgress, CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.Assigned, CaseLifecycle.Queued)]
        [InlineData(CaseLifecycle.ReviewInProgress, CaseLifecycle.Queued)]
        public void Every_hop_a_submit_produces_is_one_the_lifecycle_permits(int from, int final)
        {
            // The chain, not just its endpoints: skipping a state is exactly what AD-057
            // exists to prevent, so each hop must be legal from the one before it.
            var current = from;
            foreach (var hop in OutcomeRules.HopsFor(from, final))
            {
                Assert.True(
                    CaseLifecycle.IsAllowed(current, hop),
                    "Refused hop " + current + " -> " + hop);
                current = hop;
            }

            Assert.Equal(final, current);
        }

        [Fact]
        public void Sends_a_tax_handoff_straight_to_the_queue_without_submitting_the_case()
        {
            // The case is not submitted when only its Tax review is (BR-004).
            Assert.DoesNotContain(
                CaseLifecycle.Submitted,
                OutcomeRules.HopsFor(CaseLifecycle.ReviewInProgress, CaseLifecycle.Queued));
        }
    }
}
