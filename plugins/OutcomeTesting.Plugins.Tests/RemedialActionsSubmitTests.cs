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

        private static Guid Answer(FakeOrganizationService svc, Guid reviewId, string questionCode, int choice)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var responseId = Guid.NewGuid();
            svc.Seed("al_question", questionId, "al_questioncode", questionCode);
            svc.Seed("al_questionversion", versionId, "al_questionid", new EntityReference("al_question", questionId));
            svc.Seed(
                "al_response", responseId,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "al_answerchoice", new OptionSetValue(choice),
                "statecode", new OptionSetValue(0));
            return responseId;
        }

        /// <summary>
        /// A File Quality fail point ticked on the given review's answer to a question. The
        /// fail points are read across every answer on a review (AD-096), so any answer the
        /// review holds carries one.
        /// </summary>
        private static void Tick(FakeOrganizationService svc, Guid reviewId, Guid reasonId)
        {
            var query = new QueryExpression("al_response") { ColumnSet = new ColumnSet(false), Criteria = new FilterExpression() };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            var response = svc.RetrieveMultiple(query).Entities.First();
            svc.Seed("al_al_failreason_al_response", Guid.NewGuid(), "al_responseid", response.Id, "al_failreasonid", reasonId);
        }

        private static Guid FailReason(FakeOrganizationService svc, string name)
        {
            var id = Guid.NewGuid();
            svc.Seed("al_failreason", id, "al_name", name, "al_displayorder", 1);
            return id;
        }

        /// <summary>Parks a whole map: item, words, item, words.</summary>
        private static void ParkMap(FakeOrganizationService svc, Guid reviewId, params string[] pairs)
        {
            var entries = new System.Collections.Generic.List<string>();
            for (var i = 0; i < pairs.Length; i += 2)
            {
                entries.Add("{\"item\":\"" + pairs[i] + "\",\"text\":\"" + pairs[i + 1] + "\"}");
            }

            svc.Row("al_reviewinstance", reviewId)[RemedialActions.PendingAttr] = "[" + string.Join(",", entries) + "]";
        }

        private static string[] WordsOn(FakeOrganizationService svc)
        {
            return ActionsOn(svc)
                .Select(a => a.GetAttributeValue<string>(RemedialActions.ActionAttr))
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToArray();
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

        [Fact]
        public void A_tax_fail_with_nothing_itemised_keeps_its_words_beside_the_aqs_items()
        {
            // The Tax checker failed the tax outcome without ticking a fail point, so they were
            // made to write the overall action. The AQS leg itemised one. Before 2026-09-29's
            // fix the overall words reached an action only when the COMBINED list was empty,
            // so here they reached none.
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePassWithIssues);
            var reason = FailReason(svc, "Record Keeping - TOB not provided or out of date");
            Park(svc, TaxReviewId, "Correct the tax wrapper.");
            Tick(svc, AqsReviewId, reason);
            ParkMap(svc, AqsReviewId, "Record Keeping - TOB not provided or out of date", "Issue the current TOB.");

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);

            Assert.Equal(new[] { "Correct the tax wrapper.", "Issue the current TOB." }, WordsOn(svc));
        }

        [Fact]
        public void An_aqs_grade_with_nothing_itemised_keeps_its_words_beside_the_tax_items()
        {
            // The reverse: the AQS grade owes remediation with no fail point behind it, while
            // the deferred Tax leg itemised one.
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePassWithIssues);
            var reason = FailReason(svc, "Tax - wrapper not suitable");
            Tick(svc, TaxReviewId, reason);
            ParkMap(svc, TaxReviewId, "Tax - wrapper not suitable", "Correct the tax wrapper.");
            Park(svc, AqsReviewId, "Rewrite the suitability report.");

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);

            Assert.Equal(new[] { "Correct the tax wrapper.", "Rewrite the suitability report." }, WordsOn(svc));
        }

        [Fact]
        public void Overall_words_from_a_leg_that_owes_nothing_are_not_raised()
        {
            // The AQS checker wrote an overall action while the grade owed one, then graded
            // Pass. Those words answer a result that no longer stands; only the Tax fail is owed.
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePass);
            Park(svc, TaxReviewId, "Correct the tax wrapper.");
            Park(svc, AqsReviewId, "Words for a grade that was changed.");

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);

            Assert.Equal(new[] { "Correct the tax wrapper." }, WordsOn(svc));
            Assert.Null(svc.Row("al_reviewinstance", AqsReviewId).GetAttributeValue<string>(RemedialActions.PendingAttr));
        }

        [Fact]
        public void The_same_fail_point_on_both_checks_carries_each_checkers_own_words()
        {
            // Both checkers ticked the same fail point, so both legs produce the same item text.
            // Each action must carry the words of the checker who marked it on THAT check -
            // aligning against the combined list would hand both actions one checker's words.
            var svc = Case(ResponseRules.ChoiceFail, ResponseRules.ChoicePassWithIssues);
            var reason = FailReason(svc, "Record Keeping - TOB not provided or out of date");
            Tick(svc, TaxReviewId, reason);
            Tick(svc, AqsReviewId, reason);
            ParkMap(svc, TaxReviewId, "Record Keeping - TOB not provided or out of date", "Tax: reissue the TOB.");
            ParkMap(svc, AqsReviewId, "Record Keeping - TOB not provided or out of date", "AQS: evidence the TOB.");

            Submit(svc, TaxReviewId);
            CaseTransitions.MoveThrough(svc, CaseId, CaseLifecycle.Assigned);
            Submit(svc, AqsReviewId);

            Assert.Equal(new[] { "AQS: evidence the TOB.", "Tax: reissue the TOB." }, WordsOn(svc));
        }
    }
}
