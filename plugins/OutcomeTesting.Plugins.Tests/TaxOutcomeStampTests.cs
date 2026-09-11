using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A Tax check records its grade on the case (AD-055), so a Tax-only case that passed
    /// does not read as ungraded.
    ///
    /// IO-300004 was the report: Q-TAX-02 answered Pass, the case Closed, and every screen
    /// showing "Not yet graded" because they all read <c>al_outcome</c> - which
    /// <c>FinaliseReview</c> writes on the AQS branch only.
    ///
    /// The grade goes on <c>al_outcomecase.al_taxoutcome</c> rather than into an
    /// <c>al_outcome</c> row, and the second test here is what holds that line. See
    /// <c>SubmitReviewPlugin.StampTaxOutcome</c> for the three reasons.
    /// </summary>
    public class TaxOutcomeStampTests
    {
        private static readonly Guid CaseId = Guid.Parse("d1d1d1d1-0001-4001-8001-d1d1d1d1d1d1");
        private static readonly Guid RouteId = Guid.Parse("d2d2d2d2-0002-4002-8002-d2d2d2d2d2d2");
        private static readonly Guid ReviewId = Guid.Parse("d3d3d3d3-0003-4003-8003-d3d3d3d3d3d3");
        private static readonly Guid QuestionId = Guid.Parse("d4d4d4d4-0004-4004-8004-d4d4d4d4d4d4");
        private static readonly Guid VersionId = Guid.Parse("d5d5d5d5-0005-4005-8005-d5d5d5d5d5d5");
        private static readonly Guid ChecklistVersionId = Guid.Parse("d6d6d6d6-0006-4006-8006-d6d6d6d6d6d6");

        /// <summary>A Tax-only case with its Q-TAX-02 answer recorded and nothing else owed.</summary>
        private static FakeOrganizationService TaxOnlyCase(int answerChoice)
        {
            var svc = new FakeOrganizationService();

            svc.Seed(
                "al_reviewroute", RouteId,
                "al_requirestaxreview", true,
                "al_requiresaqsreview", false);

            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "IO-300004",
                "al_casestatus", new OptionSetValue(CaseLifecycle.ReviewInProgress),
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));

            // No mandatory question versions are seeded, so the checklist owes nothing beyond
            // the Tax outcome answer below; the version itself still has to exist, because a
            // review with none is refused before any of this is reached.
            svc.Seed("al_checklistversion", ChecklistVersionId);

            svc.Seed(
                "al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusInProgress),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistVersionId),
                "al_sequence", 1,
                "statecode", new OptionSetValue(0));

            svc.Seed("al_question", QuestionId, "al_questioncode", "Q-TAX-02");
            svc.Seed(
                "al_questionversion", VersionId,
                "al_questionid", new EntityReference("al_question", QuestionId));

            svc.Seed(
                "al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_questionversionid", new EntityReference("al_questionversion", VersionId),
                "al_answerchoice", new OptionSetValue(answerChoice),
                "statecode", new OptionSetValue(0));

            return svc;
        }

        private static void Submit(FakeOrganizationService svc)
        {
            SubmitReviewPlugin.Submit(
                svc,
                ReviewId,
                "tax-stamp-" + Guid.NewGuid().ToString("N"),
                null,
                Guid.NewGuid(),
                Guid.NewGuid(),
                requireCallerOwnsReview: false,
                details: "tax outcome stamp");
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoiceFail)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        public void A_submitted_tax_check_records_its_grade_on_the_case(int answerChoice)
        {
            var svc = TaxOnlyCase(answerChoice);

            Submit(svc);

            var row = svc.Retrieve("al_outcomecase", CaseId, new ColumnSet("al_taxoutcome"));
            var stamped = row.GetAttributeValue<OptionSetValue>("al_taxoutcome");

            Assert.NotNull(stamped);
            Assert.Equal(answerChoice, stamped.Value);
        }

        [Fact]
        public void The_grade_is_the_answer_itself_not_a_translation_of_it()
        {
            // The column carries al_response.al_answerchoice's own values, so Fail stays Fail.
            // The BR-005 scale has no Fail at all, which is the first of the three reasons
            // this is not an al_outcome row.
            var svc = TaxOnlyCase(ResponseRules.ChoiceFail);

            Submit(svc);

            var stamped = svc.Retrieve("al_outcomecase", CaseId, new ColumnSet("al_taxoutcome"))
                .GetAttributeValue<OptionSetValue>("al_taxoutcome");

            Assert.Equal(ResponseRules.ChoiceFail, stamped.Value);
            Assert.NotEqual(OutcomeRules.OutcomeInsufficient, stamped.Value);
            Assert.NotEqual(OutcomeRules.OutcomePotentialHarm, stamped.Value);
        }

        [Fact]
        public void A_tax_submit_still_writes_no_outcome_row()
        {
            // The regression guard for the other two reasons. HasOutcome is what closes a
            // case after sign-off and counts rows per case with no review filter, and
            // GenerateExportPlugin.ResolveOutcome reads one Outcome per case while AD-075
            // blanks the graded columns for a Tax-only case on purpose. An al_outcome row
            // here would stall closure and fill an export column the contract leaves empty.
            var svc = TaxOnlyCase(ResponseRules.ChoicePass);

            Submit(svc);

            Assert.False(SubmitReviewPlugin.HasOutcome(svc, CaseId));
        }
    }
}
