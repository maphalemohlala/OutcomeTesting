using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A moved question keeps the answer an open review already gave it (project owner,
    /// 2026-11-17). The move gives the question a new code and version in the target section,
    /// so the answer on the old version used to be left behind and the checker had to answer
    /// again, or was refused at submit. Driven through the real Move, Save and Submit.
    /// </summary>
    public class MovedAnswerCarryFixTests
    {
        private static readonly Guid CaseId = Guid.Parse("c1c1c1c1-0001-4001-8001-c1c1c1c1c1c1");
        private static readonly Guid RouteId = Guid.Parse("c2c2c2c2-0002-4002-8002-c2c2c2c2c2c2");
        private static readonly Guid TaxReviewId = Guid.Parse("c3c3c3c3-0003-4003-8003-c3c3c3c3c3c3");
        private static readonly Guid AqsReviewId = Guid.Parse("c4c4c4c4-0004-4004-8004-c4c4c4c4c4c4");
        private static readonly Guid ChecklistVersionId = Guid.Parse("c5c5c5c5-0005-4005-8005-c5c5c5c5c5c5");
        private static readonly Guid TaxFrom = Guid.Parse("c6c6c6c6-0006-4006-8006-c6c6c6c6c6c6");
        private static readonly Guid TaxTo = Guid.Parse("c7c7c7c7-0007-4007-8007-c7c7c7c7c7c7");
        private static readonly Guid AqsTo = Guid.Parse("c8c8c8c8-0008-4008-8008-c8c8c8c8c8c8");
        private static readonly Guid MovedQuestion = Guid.Parse("c9c9c9c9-0009-4009-8009-c9c9c9c9c9c9");
        private static readonly Guid OldVersion = Guid.Parse("cacacaca-000a-400a-800a-cacacacacaca");
        private static readonly Guid OldAnswer = Guid.Parse("cbcbcbcb-000b-400b-800b-cbcbcbcbcbcb");
        private static readonly Guid LivePoint = Guid.Parse("cccccccc-000c-400c-800c-cccccccccccc");
        private static readonly Guid RetiredPoint = Guid.Parse("cdcdcdcd-000d-400d-800d-cdcdcdcdcdcd");
        private const string NewCode = "Q-E2-09";

        private static DateTime Today
        {
            get { return DateTime.UtcNow.Date; }
        }

        /// <summary>
        /// A Tax-then-AQS case. The Tax review answered the mandatory Q-E1-01 Pass, with one
        /// live and one retired fail point ticked.
        /// </summary>
        private static FakeOrganizationService Case()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", true, "al_requiresaqsreview", true);
            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "MV-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.ReviewInProgress),
                "al_adviseremail", "adviser@example.invalid",
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));
            svc.Seed("al_checklistversion", ChecklistVersionId);
            Review(svc, TaxReviewId, ResponseRules.ReviewTypeTax, 1);
            Review(svc, AqsReviewId, ResponseRules.ReviewTypeAqs, 2);

            Section(svc, TaxFrom, ResponseRules.OwnerRoleTaxTeam, "Tax from");
            Section(svc, TaxTo, ResponseRules.OwnerRoleTaxTeam, "Tax to");
            Section(svc, AqsTo, ResponseRules.OwnerRoleAqsChecker, "AQS to");

            svc.Seed("al_question", MovedQuestion,
                "al_questioncode", "Q-E1-01", "al_name", "Moved", "al_displayorder", 1,
                "al_sectionid", new EntityReference("al_section", TaxFrom));
            svc.Seed("al_questionversion", OldVersion,
                "al_questionid", new EntityReference("al_question", MovedQuestion),
                "al_versionnumber", 1, "al_questiontext", "Was the advice suitable?",
                "al_effectivefrom", Today.AddDays(-30), "al_ismandatory", true, "al_displayorder", 1,
                "al_responsetype", new OptionSetValue(ResponseRules.TypePassFail));
            svc.Seed("al_response", OldAnswer,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", TaxReviewId),
                "al_questionversionid", new EntityReference("al_questionversion", OldVersion),
                "al_answerchoice", new OptionSetValue(ResponseRules.ChoicePass));

            svc.Seed("al_failreason", LivePoint, "al_name", "Live point", "statecode", new OptionSetValue(0));
            svc.Seed("al_failreason", RetiredPoint, "al_name", "Retired point", "statecode", new OptionSetValue(1));
            svc.Seed("al_al_failreason_al_response", Guid.NewGuid(), "al_responseid", OldAnswer, "al_failreasonid", LivePoint);
            svc.Seed("al_al_failreason_al_response", Guid.NewGuid(), "al_responseid", OldAnswer, "al_failreasonid", RetiredPoint);

            // The Tax outcome the submit's finalising reads.
            var outcomeQuestion = Guid.NewGuid();
            var outcomeVersion = Guid.NewGuid();
            svc.Seed("al_question", outcomeQuestion, "al_questioncode", "Q-TAX-02");
            svc.Seed("al_questionversion", outcomeVersion, "al_questionid", new EntityReference("al_question", outcomeQuestion));
            svc.Seed("al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", TaxReviewId),
                "al_questionversionid", new EntityReference("al_questionversion", outcomeVersion),
                "al_answerchoice", new OptionSetValue(ResponseRules.ChoicePass));
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

        private static void Section(FakeOrganizationService svc, Guid id, int ownerRole, string name)
        {
            svc.Seed("al_section", id,
                "al_name", name,
                "al_ownerrole", new OptionSetValue(ownerRole),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistVersionId));
        }

        private static void Move(FakeOrganizationService svc, Guid toSection, DateTime from)
        {
            var provider = new FakeServiceProvider(svc);
            provider.Context.InputParameters["QuestionId"] = MovedQuestion.ToString("D");
            provider.Context.InputParameters["TargetSectionId"] = toSection.ToString("D");
            provider.Context.InputParameters["NewQuestionCode"] = NewCode;
            provider.Context.InputParameters["Reason"] = "Belongs elsewhere";
            provider.Context.InputParameters["EffectiveFrom"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            provider.Context.InputParameters["IdempotencyKey"] = Guid.NewGuid().ToString("D");
            new MoveQuestionPlugin(null, null).Execute(provider);
        }

        private static Entity NewVersion(FakeOrganizationService svc)
        {
            var question = svc.All("al_question").Single(q => q.GetAttributeValue<string>("al_questioncode") == NewCode);
            return svc.All("al_questionversion")
                .Single(v => v.GetAttributeValue<EntityReference>("al_questionid").Id == question.Id);
        }

        /// <summary>The forward-dated move's day arrives: the old version ends today and the new one starts.</summary>
        private static void Arrive(FakeOrganizationService svc)
        {
            svc.Row("al_questionversion", OldVersion)["al_effectiveto"] = Today;
            NewVersion(svc)["al_effectivefrom"] = Today;
        }

        private static Entity[] AnswersOn(FakeOrganizationService svc, Guid reviewId, Guid versionId)
        {
            return svc.All("al_response")
                .Where(r => r.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id == reviewId
                    && r.GetAttributeValue<EntityReference>("al_questionversionid").Id == versionId)
                .ToArray();
        }

        private static void SubmitTax(FakeOrganizationService svc)
        {
            SubmitReviewPlugin.Submit(
                svc, TaxReviewId, "mv-" + Guid.NewGuid().ToString("N"), null,
                Guid.NewGuid(), Guid.NewGuid(), requireCallerOwnsReview: false, details: "move carry");
        }

        private static int Status(FakeOrganizationService svc, Guid reviewId)
        {
            return svc.Row("al_reviewinstance", reviewId).GetAttributeValue<OptionSetValue>("al_reviewstatus").Value;
        }

        [Fact]
        public void A_move_dated_today_carries_the_answer_and_its_live_fail_points_at_once()
        {
            var svc = Case();

            Move(svc, TaxTo, Today);

            var carried = Assert.Single(AnswersOn(svc, TaxReviewId, NewVersion(svc).Id));
            Assert.Equal(ResponseRules.ChoicePass, carried.GetAttributeValue<OptionSetValue>("al_answerchoice").Value);
            var link = Assert.Single(svc.Associations, a => a.TargetId == carried.Id);
            Assert.Equal(LivePoint, link.RelatedId);
        }

        [Fact]
        public void The_carried_answer_is_one_the_response_guard_accepts()
        {
            // The fake runs no plug-in steps. On the platform the guard fires on the carry's
            // Create, and a refusal there would fail the whole move.
            var svc = Case();
            Move(svc, TaxTo, Today);
            var carried = Assert.Single(AnswersOn(svc, TaxReviewId, NewVersion(svc).Id));

            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = "Create";
            provider.Context.InputParameters["Target"] = carried;
            new ResponseGuardPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void A_forward_dated_move_carries_nothing_before_its_day_and_the_answer_at_submit_after_it()
        {
            var svc = Case();

            Move(svc, TaxTo, Today.AddDays(10));
            Assert.Empty(AnswersOn(svc, TaxReviewId, NewVersion(svc).Id));

            Arrive(svc);
            SubmitTax(svc);

            Assert.Equal(ResponseRules.StatusSubmitted, Status(svc, TaxReviewId));
            var carried = Assert.Single(AnswersOn(svc, TaxReviewId, NewVersion(svc).Id));
            Assert.Equal(ResponseRules.ChoicePass, carried.GetAttributeValue<OptionSetValue>("al_answerchoice").Value);
        }

        [Fact]
        public void A_forward_dated_move_carries_the_answer_on_the_next_save_after_its_day()
        {
            var svc = Case();
            var otherQuestion = Guid.NewGuid();
            var otherVersion = Guid.NewGuid();
            svc.Seed("al_question", otherQuestion, "al_questioncode", "Q-E1-02",
                "al_sectionid", new EntityReference("al_section", TaxFrom));
            svc.Seed("al_questionversion", otherVersion, "al_questionid", new EntityReference("al_question", otherQuestion),
                "al_versionnumber", 1, "al_responsetype", new OptionSetValue(ResponseRules.TypeText));
            Move(svc, TaxTo, Today.AddDays(10));
            Arrive(svc);

            AnswerWriter.Save(svc, TaxReviewId, new AnswerRequestPayload
            {
                QuestionVersionId = otherVersion.ToString("D"),
                AnswerText = "Noted",
            });

            Assert.Single(AnswersOn(svc, TaxReviewId, NewVersion(svc).Id));
        }

        [Fact]
        public void An_answer_the_checker_already_gave_on_the_new_question_is_never_overwritten()
        {
            var svc = Case();
            Move(svc, TaxTo, Today.AddDays(10));
            var newVersion = NewVersion(svc).Id;
            svc.Seed("al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", TaxReviewId),
                "al_questionversionid", new EntityReference("al_questionversion", newVersion),
                "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceFail));
            Arrive(svc);

            Assert.Equal(0, AnswerWriter.CarryMovedAnswers(svc, TaxReviewId));

            var kept = Assert.Single(AnswersOn(svc, TaxReviewId, newVersion));
            Assert.Equal(ResponseRules.ChoiceFail, kept.GetAttributeValue<OptionSetValue>("al_answerchoice").Value);
        }

        [Fact]
        public void A_move_into_the_other_discipline_carries_nothing()
        {
            var svc = Case();

            Move(svc, AqsTo, Today);

            Assert.DoesNotContain(svc.All("al_response"), r =>
                r.GetAttributeValue<EntityReference>("al_questionversionid").Id == NewVersion(svc).Id);
            Assert.Equal(0, AnswerWriter.CarryMovedAnswers(svc, TaxReviewId));
        }

        [Fact]
        public void A_target_on_another_answer_scale_is_left_unanswered_and_named_at_submit()
        {
            var svc = Case();
            Move(svc, TaxTo, Today.AddDays(10));
            NewVersion(svc)["al_responsetype"] = new OptionSetValue(ResponseRules.TypeYesNo);
            Arrive(svc);

            var refusal = Assert.Throws<InvalidPluginExecutionException>(() => SubmitTax(svc));

            Assert.Contains(NewCode, refusal.Message);
            Assert.Empty(AnswersOn(svc, TaxReviewId, NewVersion(svc).Id));
        }

        [Fact]
        public void A_submitted_review_is_never_touched()
        {
            var svc = Case();
            svc.Row("al_reviewinstance", TaxReviewId)["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusSubmitted);

            Move(svc, TaxTo, Today);

            Assert.Empty(AnswersOn(svc, TaxReviewId, NewVersion(svc).Id));
            Assert.Equal(0, AnswerWriter.CarryMovedAnswers(svc, TaxReviewId));
        }
    }
}
