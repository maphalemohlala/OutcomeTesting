using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The AQS grading consistency check counts only what the checker can see: a fail point
    /// retired from the library is dropped before the check, and an answer left on a question
    /// version since superseded is not read. Driven through the real Submit, Tax then AQS.
    /// </summary>
    public class SubmitReviewGradingFixTests
    {
        private static readonly Guid CaseId = Guid.Parse("f1f1f1f1-0001-4001-8001-f1f1f1f1f1f1");
        private static readonly Guid RouteId = Guid.Parse("f2f2f2f2-0002-4002-8002-f2f2f2f2f2f2");
        private static readonly Guid TaxReviewId = Guid.Parse("f3f3f3f3-0003-4003-8003-f3f3f3f3f3f3");
        private static readonly Guid AqsReviewId = Guid.Parse("f4f4f4f4-0004-4004-8004-f4f4f4f4f4f4");
        private static readonly Guid ChecklistVersionId = Guid.Parse("f5f5f5f5-0005-4005-8005-f5f5f5f5f5f5");

        /// <summary>A Tax-then-AQS case whose AQS check is a clean Pass on every answer it shows.</summary>
        private static FakeOrganizationService CleanCase()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", true, "al_requiresaqsreview", true);
            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "GR-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.ReviewInProgress),
                "al_adviseremail", "adviser@example.invalid",
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));
            svc.Seed("al_checklistversion", ChecklistVersionId);

            Review(svc, TaxReviewId, ResponseRules.ReviewTypeTax, 1);
            Review(svc, AqsReviewId, ResponseRules.ReviewTypeAqs, 2);
            Answer(svc, TaxReviewId, "Q-TAX-02", ResponseRules.ChoicePass, null);
            Answer(svc, AqsReviewId, GradingRules.GradeQuestionCode, ResponseRules.ChoicePass, null);
            Answer(svc, AqsReviewId, "Q-FQ-01", ResponseRules.ChoicePass, null);
            Answer(svc, AqsReviewId, "Q-FQ-03", ResponseRules.ChoiceNo, null);
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

        private static Guid Answer(FakeOrganizationService svc, Guid reviewId, string questionCode, int choice, DateTime? retiredOn)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var responseId = Guid.NewGuid();
            svc.Seed("al_question", questionId, "al_questioncode", questionCode);
            var version = svc.Seed("al_questionversion", versionId, "al_questionid", new EntityReference("al_question", questionId));
            if (retiredOn.HasValue)
            {
                version["al_effectiveto"] = retiredOn.Value;
            }

            svc.Seed(
                "al_response", responseId,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "al_answerchoice", new OptionSetValue(choice),
                "statecode", new OptionSetValue(0));
            return responseId;
        }

        private static void SubmitBoth(FakeOrganizationService svc)
        {
            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);
        }

        private static void Submit(FakeOrganizationService svc, Guid reviewId)
        {
            SubmitReviewPlugin.Submit(
                svc, reviewId, "gr-" + Guid.NewGuid().ToString("N"), null,
                Guid.NewGuid(), Guid.NewGuid(), requireCallerOwnsReview: false, details: "grading fix");
        }

        private static int AqsStatus(FakeOrganizationService svc)
        {
            return svc.Row("al_reviewinstance", AqsReviewId).GetAttributeValue<OptionSetValue>("al_reviewstatus").Value;
        }

        [Fact]
        public void A_clean_check_submits()
        {
            var svc = CleanCase();

            SubmitBoth(svc);

            Assert.Equal(ResponseRules.StatusSubmitted, AqsStatus(svc));
        }

        [Fact]
        public void A_retired_fail_point_ticked_before_it_was_retired_does_not_demand_a_file_quality_fail()
        {
            var svc = CleanCase();
            var response = Answer(svc, AqsReviewId, "Q-SUIT-99", ResponseRules.ChoiceNa, null);
            var retired = Guid.NewGuid();
            svc.Seed("al_failreason", retired, "al_name", "Retired point", "al_displayorder", 1, "statecode", new OptionSetValue(1));
            svc.Seed("al_al_failreason_al_response", Guid.NewGuid(), "al_responseid", response, "al_failreasonid", retired);

            SubmitBoth(svc);

            Assert.Equal(ResponseRules.StatusSubmitted, AqsStatus(svc));
        }

        [Fact]
        public void A_live_fail_point_still_demands_a_file_quality_fail()
        {
            var svc = CleanCase();
            var response = Answer(svc, AqsReviewId, "Q-SUIT-99", ResponseRules.ChoiceNa, null);
            var live = Guid.NewGuid();
            svc.Seed("al_failreason", live, "al_name", "Live point", "al_displayorder", 1, "statecode", new OptionSetValue(0));
            svc.Seed("al_al_failreason_al_response", Guid.NewGuid(), "al_responseid", response, "al_failreasonid", live);

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            var error = Assert.Throws<InvalidPluginExecutionException>(() => Submit(svc, AqsReviewId));

            Assert.Contains("must be Fail", error.Message);
        }

        [Fact]
        public void An_answer_left_on_a_superseded_question_version_is_not_counted()
        {
            var svc = CleanCase();
            // The old version, answered No and retired since; its successor answered Yes.
            Answer(svc, AqsReviewId, "Q-AML-01", ResponseRules.ChoiceNo, DateTime.UtcNow.AddDays(-2));
            Answer(svc, AqsReviewId, "Q-AML-01", ResponseRules.ChoiceYes, null);

            SubmitBoth(svc);

            Assert.Equal(ResponseRules.StatusSubmitted, AqsStatus(svc));
        }

        [Fact]
        public void A_consumer_duty_outcome_does_not_restrict_the_grade()
        {
            // Record-only (project owner, 2026-11-24): Potential harm on a Consumer Duty outcome
            // beside a Pass grade still submits, where the same answer on a suitability point is refused.
            var svc = CleanCase();
            var response = Answer(svc, AqsReviewId, "Q-CD-01", ResponseRules.ChoicePotentialHarm, null);
            Retype(svc, response, ResponseRules.TypeOutcomeScale);

            SubmitBoth(svc);

            Assert.Equal(ResponseRules.StatusSubmitted, AqsStatus(svc));
        }

        [Fact]
        public void The_same_harm_on_a_suitability_point_is_still_refused_beside_a_pass_grade()
        {
            var svc = CleanCase();
            var response = Answer(svc, AqsReviewId, "Q-E1-01", ResponseRules.ChoiceFail, null);
            Retype(svc, response, ResponseRules.TypePassFailInsufficientNa);

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Assert.Throws<InvalidPluginExecutionException>(() => Submit(svc, AqsReviewId));
        }

        private static void Retype(FakeOrganizationService svc, Guid responseId, int responseType)
        {
            var versionId = svc.Row("al_response", responseId).GetAttributeValue<EntityReference>("al_questionversionid").Id;
            svc.Row("al_questionversion", versionId)["al_responsetype"] = new OptionSetValue(responseType);
        }
    }
}
