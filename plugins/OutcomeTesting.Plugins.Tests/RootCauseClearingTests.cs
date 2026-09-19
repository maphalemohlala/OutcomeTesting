using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The primary root cause is let go when the grade returns to Pass (item 3, 2026-09-19).
    ///
    /// ResponseProgressPlugin owns this because it is already the post-operation step on
    /// al_response, so it sees the write whatever made it - the portal through AnswerWriter,
    /// or a direct Dataverse write that never touches AnswerWriter at all. Hiding the
    /// question in the two front ends was never enough on its own: a value left in the table
    /// still exports, and still reads as a live root cause on a case that passed.
    /// </summary>
    public class RootCauseClearingTests
    {
        private static readonly Guid ReviewId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid OtherReviewId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        private static readonly Guid GradeQuestionId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        private static readonly Guid GradeVersionId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        private static readonly Guid RootCauseQuestionId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        private static readonly Guid RootCauseVersionId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        private static readonly Guid RootCauseResponseId = Guid.Parse("88888888-8888-4888-8888-888888888888");

        /// <summary>FactFind quality, the first of the nine causes.</summary>
        private const int FactFindQuality = 120910320;

        /// <summary>
        /// A checklist holding the two grading questions, wired question version -> question
        /// the way the seed wires them, so the plug-in walks a real chain rather than a
        /// shortcut a test invented.
        /// </summary>
        private static FakeOrganizationService Checklist()
        {
            var service = new FakeOrganizationService();

            service.Seed("al_question", GradeQuestionId, "al_questioncode", "Q-GR-01");
            service.Seed(
                "al_questionversion",
                GradeVersionId,
                "al_responsetype", new OptionSetValue(GradingRules.GradeResponseType),
                "al_questionid", new EntityReference("al_question", GradeQuestionId));

            service.Seed("al_question", RootCauseQuestionId, "al_questioncode", "Q-GR-02");
            service.Seed(
                "al_questionversion",
                RootCauseVersionId,
                "al_responsetype", new OptionSetValue(GradingRules.RootCauseResponseType),
                "al_questionid", new EntityReference("al_question", RootCauseQuestionId));

            return service;
        }

        /// <summary>A recorded root cause on a review, as the checker left it.</summary>
        private static void SeedRootCause(
            FakeOrganizationService service, Guid responseId, Guid reviewId, int? cause)
        {
            var row = service.Seed(
                "al_response",
                responseId,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", RootCauseVersionId));

            if (cause.HasValue)
            {
                row["al_answerchoice"] = new OptionSetValue(cause.Value);
            }
        }

        /// <summary>The grade answer as it arrives on the Update, with no pre-image needed.</summary>
        private static Entity GradeAnswer(int grade)
        {
            return new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(grade),
                ["al_questionversionid"] = new EntityReference("al_questionversion", GradeVersionId),
            };
        }

        [Fact]
        public void A_grade_of_pass_clears_a_root_cause_already_recorded()
        {
            // The case the rule exists for: graded Potential harm, a cause chosen, then the
            // grade corrected to Pass. Without this the cause stays in the table.
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);

            ResponseProgressPlugin.ClearRootCauseOnPass(
                service, GradeAnswer(ResponseRules.ChoicePass), null, ReviewId);

            var update = Assert.Single(service.Updates);
            Assert.Equal("al_response", update.LogicalName);
            Assert.Equal(RootCauseResponseId, update.Id);
            Assert.True(update.Contains("al_answerchoice"));
            Assert.Null(update["al_answerchoice"]);
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void Any_other_grade_leaves_the_root_cause_where_it_is(int grade)
        {
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);

            ResponseProgressPlugin.ClearRootCauseOnPass(service, GradeAnswer(grade), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void A_suitability_pass_tick_clears_nothing()
        {
            // Pass is also the suitability grid's Pass, so most ticks on a checklist reach
            // the first test. The response type is what takes them no further, and this is
            // the test that would fail if that filter were dropped: forty-odd ticks would
            // each wipe the checker's root cause.
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);

            var suitabilityVersionId = Guid.Parse("99999999-9999-4999-8999-999999999999");
            service.Seed(
                "al_questionversion",
                suitabilityVersionId,
                "al_responsetype", new OptionSetValue(120910006),
                "al_questionid", new EntityReference("al_question", Guid.NewGuid()));

            var tick = new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass),
                ["al_questionversionid"] =
                    new EntityReference("al_questionversion", suitabilityVersionId),
            };

            ResponseProgressPlugin.ClearRootCauseOnPass(service, tick, null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void A_question_on_the_grade_scale_that_is_not_the_grade_clears_nothing()
        {
            // The response type is a convention AD-055 arranged, not a rule: checklist
            // administration could give the grade scale to a question added tomorrow. The
            // question code is the AD-122 contract, so it is what actually decides.
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);

            var impostorQuestionId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
            var impostorVersionId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
            service.Seed("al_question", impostorQuestionId, "al_questioncode", "Q-NEW-01");
            service.Seed(
                "al_questionversion",
                impostorVersionId,
                "al_responsetype", new OptionSetValue(GradingRules.GradeResponseType),
                "al_questionid", new EntityReference("al_question", impostorQuestionId));

            var answer = new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass),
                ["al_questionversionid"] = new EntityReference("al_questionversion", impostorVersionId),
            };

            ResponseProgressPlugin.ClearRootCauseOnPass(service, answer, null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void Reads_the_question_from_the_pre_image_when_the_update_carries_only_the_answer()
        {
            // An Update Target holds changed columns only, so a checker changing their grade
            // sends al_answerchoice and nothing else. Without the pre-image the plug-in
            // cannot tell which question was answered, and would clear nothing at all.
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);

            var target = new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass),
            };
            var pre = new Entity("al_response", target.Id)
            {
                ["al_questionversionid"] = new EntityReference("al_questionversion", GradeVersionId),
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", ReviewId),
            };

            ResponseProgressPlugin.ClearRootCauseOnPass(service, target, pre, ReviewId);

            Assert.Equal(RootCauseResponseId, Assert.Single(service.Updates).Id);
        }

        [Fact]
        public void A_root_cause_row_already_empty_is_not_written_again()
        {
            // An Update that changes no value still stamps modified-on and still fires every
            // step registered on the table, so a grade saved twice would otherwise churn the
            // row and the audit trail with it.
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, null);

            ResponseProgressPlugin.ClearRootCauseOnPass(
                service, GradeAnswer(ResponseRules.ChoicePass), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void Clears_only_the_root_cause_on_the_review_being_graded()
        {
            // Every review on a case answers the same question version, so a query that
            // forgot the review would clear the root cause on a colleague's review of the
            // same case.
            var service = Checklist();
            var otherResponseId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);
            SeedRootCause(service, otherResponseId, OtherReviewId, FactFindQuality);

            ResponseProgressPlugin.ClearRootCauseOnPass(
                service, GradeAnswer(ResponseRules.ChoicePass), null, ReviewId);

            Assert.Equal(RootCauseResponseId, Assert.Single(service.Updates).Id);
            Assert.NotNull(
                service.Row("al_response", otherResponseId).GetAttributeValue<OptionSetValue>("al_answerchoice"));
        }

        [Fact]
        public void An_answer_carrying_no_choice_at_all_is_left_alone()
        {
            // This is what stops the clearing Update recursing: the row it writes holds no
            // choice, so the second pass through this plug-in stops at the first test.
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);

            var cleared = new Entity("al_response", RootCauseResponseId)
            {
                ["al_answerchoice"] = null,
            };

            ResponseProgressPlugin.ClearRootCauseOnPass(service, cleared, null, ReviewId);

            Assert.Empty(service.Updates);
            Assert.Equal(0, service.RetrieveCount);
        }

        [Fact]
        public void A_write_that_does_not_touch_the_choice_column_reads_nothing()
        {
            // A saved case note must not cost two retrieves on its way past a rule that
            // cannot apply to it.
            var service = Checklist();
            SeedRootCause(service, RootCauseResponseId, ReviewId, FactFindQuality);

            var note = new Entity("al_response", Guid.NewGuid())
            {
                ["al_answertext"] = "The adviser evidenced the research.",
            };

            ResponseProgressPlugin.ClearRootCauseOnPass(service, note, null, ReviewId);

            Assert.Empty(service.Updates);
            Assert.Equal(0, service.RetrieveCount);
        }
    }
}
