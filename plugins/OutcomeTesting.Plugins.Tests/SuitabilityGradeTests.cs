using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Insufficient evidence on a Suitability core check takes Pass and Pass with issues off
    /// the advice quality grade (item 10, 2026-09-19).
    ///
    /// Three server-side halves, because the batch asked for all three: the grade must not be
    /// SAVEABLE (ResponseGuardPlugin), a grade already saved must be CLEARED when the
    /// condition arrives (ResponseProgressPlugin), and the command that COMPLETES the review
    /// must refuse one that slipped past both (SubmitReviewPlugin).
    ///
    /// No TypeScript mirror, unlike the root cause rule. The Code App's review page is a
    /// read-only document (OD-007) - grading is a portal write path - so there is no grade
    /// control in the app for a client-side copy of this rule to restrict. The portal carries
    /// the rendering half, in OT Review Detail and OT Answer Options.
    /// </summary>
    public class SuitabilityGradeTests
    {
        private static readonly Guid ReviewId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid GradeQuestionId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        private static readonly Guid GradeVersionId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        private static readonly Guid GradeResponseId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");

        private static readonly Guid E3SectionId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        private static readonly Guid E3QuestionId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        private static readonly Guid E3VersionId = Guid.Parse("88888888-8888-4888-8888-888888888888");

        private static readonly Guid CdSectionId = Guid.Parse("99999999-9999-4999-8999-999999999999");
        private static readonly Guid CdQuestionId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb");
        private static readonly Guid CdVersionId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");

        // ------------------------------------------------------------------ the pure rule

        [Theory]
        [InlineData("S-E1", true)]
        [InlineData("S-E5", true)]
        [InlineData("S-E6", true)]          // an S-E6 added through checklist administration
        [InlineData("s-e2", true)]          // codes are matched without regard to case
        [InlineData(" S-E3 ", true)]        // and with the whitespace of a hand-typed code
        [InlineData("S-CRP", false)]        // a separate block, though it shares the scale
        [InlineData("S-CD", false)]         // likewise the Consumer Duty overlay
        [InlineData("S-EXTRA", false)]      // begins with S-E and is not one of them
        [InlineData("S-E", false)]          // the bare prefix names no section
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Recognises_a_suitability_section_by_its_code(string code, bool expected)
        {
            Assert.Equal(expected, GradingRules.IsSuitabilitySection(code));
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void Leaves_the_two_grades_that_can_still_stand(int grade)
        {
            Assert.Null(GradingRules.SuitabilityGradeRefusal(grade, true));
            Assert.True(GradingRules.GradeAllowedWithInsufficientEvidence(grade));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        public void Refuses_a_grade_that_contradicts_the_evidence(int grade)
        {
            var refusal = GradingRules.SuitabilityGradeRefusal(grade, true);

            Assert.Equal(
                "A Suitability core check has been answered Insufficient evidence, so the "
                + "advice quality grade can only be Insufficient evidence or Potential harm.",
                refusal);
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        public void Allows_every_grade_when_no_core_check_is_insufficient(int grade)
        {
            Assert.Null(GradingRules.SuitabilityGradeRefusal(grade, false));
        }

        [Fact]
        public void An_absent_grade_is_not_refused()
        {
            // Clearing the grade is what this rule does to an invalid one already saved.
            // Refusing the cleared state would leave the review with no way back.
            Assert.Null(GradingRules.SuitabilityGradeRefusal(null, true));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass, true)]
        [InlineData(ResponseRules.ChoicePassWithIssues, true)]
        [InlineData(ResponseRules.ChoiceInsufficient, false)]
        [InlineData(ResponseRules.ChoicePotentialHarm, false)]
        public void Clears_only_the_two_grades_that_can_no_longer_stand(int grade, bool cleared)
        {
            Assert.Equal(cleared, GradingRules.GradeClearedBySuitability(grade));
        }

        [Fact]
        public void Clears_nothing_when_there_is_no_grade_to_clear()
        {
            Assert.False(GradingRules.GradeClearedBySuitability(null));
        }

        // ------------------------------------------------------------------ the checklist

        /// <summary>
        /// A review with a grading question, one Suitability core check (E3) and one Consumer
        /// Duty question, wired response -> version -> question -> section as the seed wires
        /// them, so every query walks a real chain.
        /// </summary>
        private static FakeOrganizationService Checklist()
        {
            var service = new FakeOrganizationService();

            service.Seed("al_section", E3SectionId, "al_sectioncode", "S-E3");
            service.Seed(
                "al_question", E3QuestionId,
                "al_questioncode", "Q-E3-01",
                "al_sectionid", new EntityReference("al_section", E3SectionId));
            service.Seed(
                "al_questionversion", E3VersionId,
                "al_responsetype", new OptionSetValue(120910006),
                "al_questionid", new EntityReference("al_question", E3QuestionId));

            service.Seed("al_section", CdSectionId, "al_sectioncode", "S-CD");
            service.Seed(
                "al_question", CdQuestionId,
                "al_questioncode", "Q-CD-01",
                "al_sectionid", new EntityReference("al_section", CdSectionId));
            service.Seed(
                "al_questionversion", CdVersionId,
                "al_responsetype", new OptionSetValue(120910009),
                "al_questionid", new EntityReference("al_question", CdQuestionId));

            service.Seed("al_section", Guid.NewGuid(), "al_sectioncode", "S-GRADE");
            service.Seed("al_question", GradeQuestionId, "al_questioncode", "Q-GR-01");
            service.Seed(
                "al_questionversion", GradeVersionId,
                "al_responsetype", new OptionSetValue(GradingRules.GradeResponseType),
                "al_questionid", new EntityReference("al_question", GradeQuestionId));

            return service;
        }

        private static void SeedAnswer(
            FakeOrganizationService service, Guid id, Guid versionId, int? choice)
        {
            var row = service.Seed(
                "al_response", id,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId));

            if (choice.HasValue)
            {
                row["al_answerchoice"] = new OptionSetValue(choice.Value);
            }
        }

        // ------------------------------------------------------------------ the read

        [Fact]
        public void Sees_an_insufficient_answer_on_a_core_check()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            Assert.True(ChecklistQueries.HasSuitabilityInsufficient(service, ReviewId));
        }

        [Fact]
        public void Does_not_see_a_core_check_that_merely_failed()
        {
            // Fail is a non-pass and it is not this rule's condition. The batch named
            // Insufficient Evidence, which is a statement about the evidence on the file
            // rather than about the advice.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceFail);

            Assert.False(ChecklistQueries.HasSuitabilityInsufficient(service, ReviewId));
        }

        [Fact]
        public void Does_not_see_an_insufficient_answer_outside_the_core_checks()
        {
            // The Consumer Duty overlay offers Insufficient evidence too. Only the
            // Suitability core checks trigger this rule.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), CdVersionId, ResponseRules.ChoiceInsufficient);

            Assert.False(ChecklistQueries.HasSuitabilityInsufficient(service, ReviewId));
        }

        [Fact]
        public void Does_not_see_another_reviews_insufficient_answer()
        {
            var service = Checklist();
            var other = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
            service.Seed(
                "al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", other),
                "al_questionversionid", new EntityReference("al_questionversion", E3VersionId),
                "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceInsufficient));

            Assert.False(ChecklistQueries.HasSuitabilityInsufficient(service, ReviewId));
        }

        // ------------------------------------------------------------------ the clearing

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        public void An_insufficient_core_check_clears_a_grade_that_can_no_longer_stand(int grade)
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, grade);

            ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient(
                service, InsufficientOn(E3VersionId), null, ReviewId);

            var update = Assert.Single(service.Updates);
            Assert.Equal(GradeResponseId, update.Id);
            Assert.True(update.Contains("al_answerchoice"));
            Assert.Null(update["al_answerchoice"]);
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void A_grade_that_still_stands_is_left_alone(int grade)
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, grade);

            ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient(
                service, InsufficientOn(E3VersionId), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void An_insufficient_answer_outside_the_core_checks_clears_nothing()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient(
                service, InsufficientOn(CdVersionId), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void A_core_check_answered_pass_clears_nothing()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            var tick = new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass),
                ["al_questionversionid"] = new EntityReference("al_questionversion", E3VersionId),
            };

            ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient(service, tick, null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void A_write_that_does_not_touch_the_choice_column_reads_nothing()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            var note = new Entity("al_response", Guid.NewGuid())
            {
                ["al_answertext"] = "Research evidenced.",
            };

            ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient(service, note, null, ReviewId);

            Assert.Empty(service.Updates);
            Assert.Equal(0, service.RetrieveCount);
        }

        [Fact]
        public void The_clearing_update_does_not_recurse()
        {
            // The row the clearing writes holds no choice at all, so a second pass through
            // this plug-in stops at the first test.
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            var cleared = new Entity("al_response", GradeResponseId) { ["al_answerchoice"] = null };

            ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient(service, cleared, null, ReviewId);

            Assert.Empty(service.Updates);
            Assert.Equal(0, service.RetrieveCount);
        }

        [Fact]
        public void Reads_the_question_from_the_pre_image_when_the_update_carries_only_the_answer()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            var target = new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoiceInsufficient),
            };
            var pre = new Entity("al_response", target.Id)
            {
                ["al_questionversionid"] = new EntityReference("al_questionversion", E3VersionId),
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", ReviewId),
            };

            ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient(service, target, pre, ReviewId);

            Assert.Equal(GradeResponseId, Assert.Single(service.Updates).Id);
        }

        // ------------------------------------------------------------------ the guard

        /// <summary>The grade question version, as the guard has it in hand.</summary>
        private static Entity GradeVersion()
        {
            return new Entity("al_questionversion", GradeVersionId)
            {
                ["al_questionid"] = new EntityReference("al_question", GradeQuestionId),
            };
        }

        private static Entity GradeWrite(int grade)
        {
            return new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(grade),
            };
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        public void Refuses_to_save_a_grade_that_contradicts_the_evidence(int grade)
        {
            // The "must not be saveable" half. The page stops offering these two, and this is
            // what stops a PATCH made by hand or a stale tab writing one anyway.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                ResponseGuardPlugin.EnsureGradeAgreesWithSuitability(
                    service,
                    GradeWrite(grade),
                    GradingRules.GradeResponseType,
                    GradeVersion(),
                    ReviewId));

            Assert.StartsWith("PRECONDITION: ", error.Message);
            Assert.Contains("Insufficient evidence or Potential harm", error.Message);
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void Saves_a_grade_the_evidence_still_allows(int grade)
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            ResponseGuardPlugin.EnsureGradeAgreesWithSuitability(
                service, GradeWrite(grade), GradingRules.GradeResponseType, GradeVersion(), ReviewId);
        }

        [Fact]
        public void Saves_a_pass_when_no_core_check_is_insufficient()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoicePass);

            ResponseGuardPlugin.EnsureGradeAgreesWithSuitability(
                service,
                GradeWrite(ResponseRules.ChoicePass),
                GradingRules.GradeResponseType,
                GradeVersion(),
                ReviewId);
        }

        [Fact]
        public void Reads_nothing_for_an_answer_that_is_not_on_the_grade_scale()
        {
            // A suitability tick is a Pass on a different scale. It must not cost a lookup,
            // and it must certainly not be refused.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);
            var before = service.RetrieveCount;

            ResponseGuardPlugin.EnsureGradeAgreesWithSuitability(
                service, GradeWrite(ResponseRules.ChoicePass), 120910006, GradeVersion(), ReviewId);

            Assert.Equal(before, service.RetrieveCount);
        }

        [Fact]
        public void A_question_on_the_grade_scale_that_is_not_the_grade_is_not_refused()
        {
            // The response type is a convention AD-055 arranged; the question code is the
            // AD-122 contract, and it is what actually decides.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            var impostorQuestionId = Guid.Parse("eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee");
            service.Seed("al_question", impostorQuestionId, "al_questioncode", "Q-NEW-01");
            var impostorVersion = new Entity("al_questionversion", Guid.NewGuid())
            {
                ["al_questionid"] = new EntityReference("al_question", impostorQuestionId),
            };

            ResponseGuardPlugin.EnsureGradeAgreesWithSuitability(
                service,
                GradeWrite(ResponseRules.ChoicePass),
                GradingRules.GradeResponseType,
                impostorVersion,
                ReviewId);
        }

        [Fact]
        public void A_write_clearing_the_grade_is_never_refused()
        {
            // This is the write ResponseProgressPlugin issues when a core check turns
            // Insufficient. Refusing it would leave the review with an invalid grade and no
            // way to shed it.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            var cleared = new Entity("al_response", GradeResponseId) { ["al_answerchoice"] = null };

            ResponseGuardPlugin.EnsureGradeAgreesWithSuitability(
                service, cleared, GradingRules.GradeResponseType, GradeVersion(), ReviewId);
        }

        private static Entity InsufficientOn(Guid versionId)
        {
            return new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoiceInsufficient),
                ["al_questionversionid"] = new EntityReference("al_questionversion", versionId),
            };
        }
    }
}
