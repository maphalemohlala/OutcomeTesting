using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// An outcome must agree with the test points recorded under it (project owner,
    /// 2026-09-22).
    ///
    /// <para>
    /// This supersedes SuitabilityGradeTests, which pinned the same rule scoped to the
    /// Suitability core checks (item 10, 2026-09-19). Three things changed on 2026-09-22:
    /// Insufficient evidence now counts "at any section not just the File quality AML and CRA
    /// section", a No or a Fail anywhere takes Pass off as well, and the Pass it takes is the
    /// file quality outcome's as much as the grade's.
    /// </para>
    /// <para>
    /// Three server-side halves, as before, because all three are still needed: the outcome
    /// must not be SAVEABLE (ResponseGuardPlugin), one already saved must be CLEARED when the
    /// condition arrives (ResponseProgressPlugin), and the command that COMPLETES the review
    /// must refuse one that slipped past both (SubmitReviewPlugin) - which is what catches
    /// answers written before the rule existed, since deploying a guard cannot reach back and
    /// re-guard rows already saved.
    /// </para>
    /// <para>
    /// No TypeScript mirror, and for the reason the superseded file already gave: the Code
    /// App's review page is a read-only document (OD-007), so there is no control there for a
    /// client-side copy of these rules to restrict. The portal carries the rendering half, in
    /// OT Review Detail and OT Answer Options. That is an affordance; this is the boundary
    /// (NFR-SEC-01).
    /// </para>
    /// </summary>
    public class ChecklistGatingTests
    {
        private static readonly Guid ReviewId = Guid.Parse("22222222-2222-4222-8222-222222222222");

        private static readonly Guid GradeQuestionId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        private static readonly Guid GradeVersionId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        private static readonly Guid GradeResponseId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");

        private static readonly Guid E3VersionId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        private static readonly Guid CdVersionId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc");
        private static readonly Guid AmlVersionId = Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd");
        private static readonly Guid SecondAmlVersionId = Guid.Parse("dddddddd-2222-4222-8222-222222222222");
        private static readonly Guid TaxOutcomeVersionId = Guid.Parse("bbbbbbbb-1111-4111-8111-111111111111");

        /// <summary>The checklist version this review was issued, which scopes its sections.</summary>
        private static readonly Guid ChecklistVersionId = Guid.Parse("99999999-1111-4111-8111-111111111111");

        /// <summary>A later version, whose sections this review must NOT be counted against.</summary>
        private static readonly Guid OtherChecklistVersionId = Guid.Parse("99999999-2222-4222-8222-222222222222");

        private static readonly Guid FqQuestionId = Guid.Parse("eeeeeeee-1111-4111-8111-111111111111");
        private static readonly Guid FqVersionId = Guid.Parse("eeeeeeee-2222-4222-8222-222222222222");
        private static readonly Guid FqResponseId = Guid.Parse("eeeeeeee-3333-4333-8333-333333333333");

        private static readonly Guid RemedialVersionId = Guid.Parse("ffffffff-1111-4111-8111-111111111111");

        // ================================================================== the pure rules

        [Theory]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void Insufficient_evidence_leaves_the_two_grades_that_can_still_stand(int grade)
        {
            Assert.Null(ChecklistGating.GradeRefusal(grade, true, false));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        public void Insufficient_evidence_takes_pass_and_pass_with_issues_off_the_grade(int grade)
        {
            var refusal = ChecklistGating.GradeRefusal(grade, true, false);

            Assert.NotNull(refusal);
            Assert.Contains("Insufficient evidence or Potential harm", refusal);
        }

        [Fact]
        public void A_no_or_fail_takes_only_pass_off_the_grade()
        {
            // The weaker of the two conditions, and deliberately so: a Fail on a test point
            // says the file did not pass, not that the evidence for judging it was missing.
            // Pass with issues is exactly the grade for that, so it stays available.
            Assert.NotNull(ChecklistGating.GradeRefusal(ResponseRules.ChoicePass, false, true));
            Assert.Null(ChecklistGating.GradeRefusal(ResponseRules.ChoicePassWithIssues, false, true));
            Assert.Null(ChecklistGating.GradeRefusal(ResponseRules.ChoicePotentialHarm, false, true));
        }

        [Fact]
        public void The_stricter_refusal_is_the_one_reported_when_both_apply()
        {
            // A form carrying both an Insufficient and a Fail refuses Pass with issues too.
            // Reporting the No-or-Fail message instead would send the checker to change the
            // grade to Pass with issues, which is also refused.
            var refusal = ChecklistGating.GradeRefusal(ResponseRules.ChoicePassWithIssues, true, true);

            Assert.NotNull(refusal);
            Assert.Contains("Insufficient evidence or Potential harm", refusal);
        }

        [Fact]
        public void An_unanswered_grade_is_never_refused()
        {
            // Clearing the grade is what this rule DOES to an invalid one. Refusing the
            // cleared state would leave the review with no way back.
            Assert.Null(ChecklistGating.GradeRefusal(null, true, true));
        }

        [Fact]
        public void A_no_or_fail_takes_pass_off_the_file_quality_outcome()
        {
            Assert.NotNull(ChecklistGating.FileQualityRefusal(ResponseRules.ChoicePass, true));
            Assert.Null(ChecklistGating.FileQualityRefusal(ResponseRules.ChoiceFail, true));
            Assert.Null(ChecklistGating.FileQualityRefusal(ResponseRules.ChoicePass, false));
            Assert.Null(ChecklistGating.FileQualityRefusal(null, true));
        }

        [Theory]
        [InlineData("Q-FQ-01", true)]
        [InlineData("Q-FQTAX-01", true)]
        [InlineData("Q-FQ-03", true)]
        [InlineData("Q-FQTAX-03", true)]
        [InlineData("Q-GR-01", true)]
        [InlineData("q-gr-01", true)]       // codes are matched without regard to case
        [InlineData(" Q-FQ-03 ", true)]     // and with the whitespace of a hand-typed code
        [InlineData("Q-E3-01", false)]
        [InlineData("Q-AML-01", false)]
        [InlineData("Q-E2-LENS", false)]    // a No here IS a finding
        [InlineData("Q-TAX-02", false)]     // the Tax check outcome is a finding about the file
        [InlineData("Q-GR-02", false)]      // the root cause is not one of the five
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Knows_which_questions_are_outcomes_rather_than_test_points(string code, bool expected)
        {
            Assert.Equal(expected, ChecklistGating.IsOutcomeQuestion(code));
        }

        [Fact]
        public void The_tax_fail_points_are_locked_by_a_pass_on_the_tax_check_outcome()
        {
            // Q-TAX-02, the Tax CHECK outcome, and not Q-FQTAX-01, the tax FILE QUALITY
            // outcome, which is where this rule was first written (project owner, 2026-09-23:
            // "it is done on the File quality Outcome instead of the File quality - Tax check
            // section"). The two are different questions on different sections.
            Assert.True(ChecklistGating.FailPointsLocked(true, ResponseRules.ChoicePass, false));
            Assert.False(ChecklistGating.FailPointsLocked(true, ResponseRules.ChoiceFail, false));

            // Pass with issues is not a Pass. The Tax scale carries three values (AD-055) and
            // only the clean one says there is nothing to give reasons for.
            Assert.False(
                ChecklistGating.FailPointsLocked(true, ResponseRules.ChoicePassWithIssues, false));

            // Unanswered is not locked: the reasons are often what the checker works out
            // first, and a form that opens locked reads as a page that failed to load.
            Assert.False(ChecklistGating.FailPointsLocked(true, null, false));

            // The AML and CRA section is AQS-owned and AD-020 keeps it off the Tax form, so
            // whatever it says cannot reach this rule.
            Assert.True(ChecklistGating.FailPointsLocked(true, ResponseRules.ChoicePass, true));
        }

        [Fact]
        public void The_aqs_fail_points_lock_only_when_every_aml_and_cra_point_reads_yes()
        {
            // Reversed from the rule of 2026-09-22, which locked the block until a finding
            // arrived and so greeted a checker with it shut (project owner, 2026-09-23: "if
            // all the responses are yes, then lock it. Otherwise open it").
            Assert.True(ChecklistGating.FailPointsLocked(false, null, true));
            Assert.False(ChecklistGating.FailPointsLocked(false, null, false));

            // The AQS file quality outcome has no say here at all - it is not even passed in.
            // A half-answered section is "otherwise", and ChecklistQueries is what decides
            // that; the rule itself is handed the answer.
            Assert.False(ChecklistGating.FailPointsLocked(false, ResponseRules.ChoicePass, false));
        }

        [Fact]
        public void Remedial_action_opens_on_the_answer_the_outcome_implies()
        {
            Assert.Equal(ResponseRules.ChoiceNo, ChecklistGating.RemedialActionDefault(ResponseRules.ChoicePass));
            Assert.Equal(ResponseRules.ChoiceYes, ChecklistGating.RemedialActionDefault(ResponseRules.ChoiceFail));

            // Nothing is defaulted from an outcome nobody chose, or from one off this scale.
            Assert.Null(ChecklistGating.RemedialActionDefault(null));
            Assert.Null(ChecklistGating.RemedialActionDefault(ResponseRules.ChoiceInsufficient));
        }

        [Fact]
        public void Remedial_action_refuses_the_answer_the_outcome_took_away()
        {
            // No longer a default the checker can overrule (project owner, 2026-09-23: "the
            // user can still select yes manually, it needs to be disabled"). What the outcome
            // implies is now the only answer the question accepts.
            var onPass = ChecklistGating.RemedialActionRefusal(
                ResponseRules.ChoicePass, ResponseRules.ChoiceYes);

            Assert.NotNull(onPass);
            Assert.Contains("Pass", onPass);

            var onFail = ChecklistGating.RemedialActionRefusal(
                ResponseRules.ChoiceFail, ResponseRules.ChoiceNo);

            Assert.NotNull(onFail);
            Assert.Contains("Fail", onFail);
        }

        [Fact]
        public void Remedial_action_accepts_the_answer_the_outcome_implies()
        {
            Assert.Null(ChecklistGating.RemedialActionRefusal(
                ResponseRules.ChoicePass, ResponseRules.ChoiceNo));
            Assert.Null(ChecklistGating.RemedialActionRefusal(
                ResponseRules.ChoiceFail, ResponseRules.ChoiceYes));
        }

        [Fact]
        public void Remedial_action_is_free_while_the_outcome_says_nothing()
        {
            // An unanswered outcome locks neither answer. Refusing here would leave a checker
            // who works bottom-up unable to record anything at all, and there is no outcome
            // for the answer to contradict.
            Assert.Null(ChecklistGating.RemedialActionRefusal(null, ResponseRules.ChoiceYes));
            Assert.Null(ChecklistGating.RemedialActionRefusal(null, ResponseRules.ChoiceNo));

            // Nor does a value off the Pass / Fail scale, which this question cannot carry.
            Assert.Null(ChecklistGating.RemedialActionRefusal(
                ResponseRules.ChoiceInsufficient, ResponseRules.ChoiceYes));
        }

        // ================================================================== the read

        [Fact]
        public void Sees_insufficient_evidence_on_a_suitability_core_check()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            var facts = ChecklistQueries.ReadGatingFacts(service, ReviewId);

            Assert.True(facts.InsufficientAnywhere);
            Assert.False(facts.NoOrFailAnywhere);
        }

        [Fact]
        public void Sees_insufficient_evidence_outside_the_core_checks_too()
        {
            // THE widening. The Consumer Duty overlay offers Insufficient evidence, and before
            // 2026-09-22 it was filtered out because it is not a Suitability core check.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), CdVersionId, ResponseRules.ChoiceInsufficient);

            Assert.True(ChecklistQueries.ReadGatingFacts(service, ReviewId).InsufficientAnywhere);
        }

        [Fact]
        public void Sees_a_fail_on_a_test_point()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceFail);

            var facts = ChecklistQueries.ReadGatingFacts(service, ReviewId);

            Assert.True(facts.NoOrFailAnywhere);
            Assert.False(facts.InsufficientAnywhere);
        }

        [Fact]
        public void Does_not_count_a_no_on_remedial_action_required()
        {
            // The rule that would otherwise eat itself: choosing Pass on the file quality
            // outcome defaults Remedial action required? to No, and a scan counting every No
            // would then grey out the Pass the checker had just chosen.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), RemedialVersionId, ResponseRules.ChoiceNo);

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).NoOrFailAnywhere);
        }

        [Fact]
        public void Does_not_count_the_file_quality_outcomes_own_fail()
        {
            // The value being gated cannot be part of what gates it, or the rule would only
            // ever confirm itself.
            var service = Checklist();
            SeedAnswer(service, FqResponseId, FqVersionId, ResponseRules.ChoiceFail);

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).NoOrFailAnywhere);
        }

        [Fact]
        public void Sees_a_finding_on_the_aml_and_cra_section()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceNo);

            var facts = ChecklistQueries.ReadGatingFacts(service, ReviewId);

            // A No is not a Yes, so the section is not clean and the fail points stay open.
            Assert.False(facts.AmlCraAllYes);
            // And it is a No on a test point, so it takes Pass off as well.
            Assert.True(facts.NoOrFailAnywhere);
        }

        [Fact]
        public void An_aml_and_cra_section_answered_yes_throughout_is_clean()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceYes);

            Assert.True(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);
        }

        [Fact]
        public void An_unanswered_aml_and_cra_point_leaves_the_section_unclean()
        {
            // The half-answered form (project owner, 2026-09-23: lock only when all of them
            // are Yes). Two points, one answered Yes, one blank - the blank one is why this
            // is counted against the questions in force rather than against the answers
            // recorded, which on their own would read as all Yes.
            var service = Checklist();
            Question(
                service, ChecklistGating.AmlCraSectionCode, "Q-AML-02", SecondAmlVersionId, 120910009);
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceYes);

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);

            SeedAnswer(service, Guid.NewGuid(), SecondAmlVersionId, ResponseRules.ChoiceYes);

            Assert.True(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);
        }

        [Fact]
        public void Counts_only_the_aml_and_cra_questions_of_this_reviews_own_checklist_version()
        {
            // The version gap. al_section carries al_checklistversionid and the rest of the
            // solution scopes by it - ResponseGuardPlugin compares the review's against the
            // section's before it will accept an answer at all. Counting every S-AMLCRA
            // question in the environment instead means a LATER version that adds a sixth
            // point puts a code in the expected set that a review on the older version can
            // never answer, so "all Yes" becomes unreachable and the AQS lock silently stops
            // working. It fails open, which is the safe direction and exactly why nothing
            // would report it.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceYes);

            Assert.True(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);

            // A second checklist version issues its own S-AMLCRA with an extra point. The
            // review under test was issued the first version and has never seen this question.
            Question(
                service,
                ChecklistGating.AmlCraSectionCode,
                "Q-AML-06",
                Guid.Parse("dddddddd-6666-4666-8666-666666666666"),
                120910009,
                checklistVersionId: OtherChecklistVersionId);

            Assert.True(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);
        }

        [Fact]
        public void An_added_point_on_this_reviews_own_version_is_still_owed_an_answer()
        {
            // The other side of the same rule, so the scoping cannot be "read nothing".
            // A question added to the review's OWN version is counted and leaves the section
            // unclean until it is answered.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceYes);
            Question(
                service,
                ChecklistGating.AmlCraSectionCode,
                "Q-AML-07",
                Guid.Parse("dddddddd-7777-4777-8777-777777777777"),
                120910009,
                checklistVersionId: ChecklistVersionId);

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);
        }

        [Fact]
        public void An_insufficient_evidence_on_aml_and_cra_leaves_the_section_unclean()
        {
            // The scale the section was retyped to (project owner, 2026-09-23: "we've changed
            // the options to yes, no, and insufficient evidence"). Written against the answer
            // being a Yes rather than against a list of the values that are not, so the rule
            // holds on whichever scale AD-123 leaves the section on.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceInsufficient);

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);
        }

        [Fact]
        public void A_cleared_aml_and_cra_answer_leaves_the_section_unclean()
        {
            // A row that exists with no choice on it is an unanswered question, not a Yes.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, null);

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);
        }

        [Fact]
        public void An_answer_elsewhere_does_not_clean_the_aml_and_cra_section()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoicePass);

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).AmlCraAllYes);
        }

        [Fact]
        public void Reads_the_tax_check_outcome_and_the_file_quality_outcome()
        {
            // The two answers the fail points and Remedial action required? are gated on.
            // Both are outcome questions, so neither counts towards the findings.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), TaxOutcomeVersionId, ResponseRules.ChoicePass);
            SeedAnswer(service, FqResponseId, FqVersionId, ResponseRules.ChoiceFail);

            var facts = ChecklistQueries.ReadGatingFacts(service, ReviewId);

            Assert.Equal(ResponseRules.ChoicePass, facts.TaxCheckOutcome);
            Assert.Equal(ResponseRules.ChoiceFail, facts.FileQualityOutcome);
            Assert.False(facts.NoOrFailAnywhere);
        }

        [Fact]
        public void Reports_no_outcomes_on_a_review_that_has_not_recorded_them()
        {
            var facts = ChecklistQueries.ReadGatingFacts(Checklist(), ReviewId);

            Assert.Null(facts.TaxCheckOutcome);
            Assert.Null(facts.FileQualityOutcome);
        }

        [Fact]
        public void A_passing_form_reports_nothing()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoicePass);
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceYes);

            var facts = ChecklistQueries.ReadGatingFacts(service, ReviewId);

            Assert.False(facts.InsufficientAnywhere);
            Assert.False(facts.NoOrFailAnywhere);
            Assert.True(facts.AmlCraAllYes);
        }

        [Fact]
        public void Does_not_see_another_reviews_answers()
        {
            var service = Checklist();
            var other = Guid.Parse("dddddddd-1111-4111-8111-111111111111");
            service.Seed(
                "al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", other),
                "al_questionversionid", new EntityReference("al_questionversion", E3VersionId),
                "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceInsufficient));

            Assert.False(ChecklistQueries.ReadGatingFacts(service, ReviewId).InsufficientAnywhere);
        }

        // ================================================================== the clearing

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        public void An_insufficient_answer_clears_a_grade_that_can_no_longer_stand(int grade)
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, grade);

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(
                service, AnswerOn(service, E3VersionId, ResponseRules.ChoiceInsufficient), null, ReviewId);

            var update = Assert.Single(service.Updates);
            Assert.Equal(GradeResponseId, update.Id);
            Assert.True(update.Contains("al_answerchoice"));
            Assert.Null(update["al_answerchoice"]);
        }

        [Fact]
        public void A_fail_clears_a_passing_grade_and_a_passing_file_quality_outcome_together()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);
            SeedAnswer(service, FqResponseId, FqVersionId, ResponseRules.ChoicePass);

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(
                service, AnswerOn(service, E3VersionId, ResponseRules.ChoiceFail), null, ReviewId);

            var cleared = service.Updates.Select(u => u.Id).ToList();
            Assert.Contains(GradeResponseId, cleared);
            Assert.Contains(FqResponseId, cleared);
        }

        [Fact]
        public void A_fail_leaves_a_grade_of_pass_with_issues_alone()
        {
            // A No or a Fail takes only Pass. Clearing more than the rule refuses would be
            // destroying an answer nothing disallows.
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePassWithIssues);

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(
                service, AnswerOn(service, E3VersionId, ResponseRules.ChoiceFail), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void A_grade_that_still_stands_is_left_alone(int grade)
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, grade);

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(
                service, AnswerOn(service, E3VersionId, ResponseRules.ChoiceInsufficient), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void An_answer_of_pass_clears_nothing()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(
                service, AnswerOn(service, E3VersionId, ResponseRules.ChoicePass), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void A_no_on_remedial_action_required_clears_nothing()
        {
            // The same self-defeating case as the read above, at the other half of the rule:
            // the default the Pass put there must not turn round and clear that Pass.
            var service = Checklist();
            SeedAnswer(service, FqResponseId, FqVersionId, ResponseRules.ChoicePass);

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(
                service, AnswerOn(service, RemedialVersionId, ResponseRules.ChoiceNo), null, ReviewId);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void A_write_that_does_not_touch_the_choice_column_reads_nothing()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            var note = new Entity("al_response", Guid.NewGuid()) { ["al_answertext"] = "Research evidenced." };

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(service, note, null, ReviewId);

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

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(service, cleared, null, ReviewId);

            Assert.Empty(service.Updates);
            Assert.Equal(0, service.RetrieveCount);
        }

        [Fact]
        public void Reads_the_question_from_the_pre_image_when_the_update_carries_only_the_answer()
        {
            var service = Checklist();
            SeedAnswer(service, GradeResponseId, GradeVersionId, ResponseRules.ChoicePass);

            var targetId = Guid.NewGuid();
            SeedAnswer(service, targetId, E3VersionId, ResponseRules.ChoiceInsufficient);

            var target = new Entity("al_response", targetId)
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoiceInsufficient),
            };
            var pre = new Entity("al_response", target.Id)
            {
                ["al_questionversionid"] = new EntityReference("al_questionversion", E3VersionId),
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", ReviewId),
            };

            ResponseProgressPlugin.ClearOutcomesContradictedByAnswer(service, target, pre, ReviewId);

            Assert.Equal(GradeResponseId, Assert.Single(service.Updates).Id);
        }

        // ================================================================== the guard

        [Theory]
        [InlineData(ResponseRules.ChoicePass)]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        public void Refuses_to_save_a_grade_that_contradicts_the_evidence(int grade)
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                    service, Write(grade), GradingRules.GradeResponseType, GradeVersion(), ReviewId));

            Assert.StartsWith("PRECONDITION: ", error.Message);
            Assert.Contains("Insufficient evidence or Potential harm", error.Message);
        }

        [Fact]
        public void Refuses_to_save_a_passing_grade_against_a_fail()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceFail);

            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                    service,
                    Write(ResponseRules.ChoicePass),
                    GradingRules.GradeResponseType,
                    GradeVersion(),
                    ReviewId));

            Assert.Contains("cannot be Pass", error.Message);
        }

        [Fact]
        public void Refuses_to_save_a_passing_file_quality_outcome_against_a_no()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceNo);

            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                    service,
                    Write(ResponseRules.ChoicePass),
                    ResponseRules.TypePassFail,
                    FqVersion(),
                    ReviewId));

            Assert.Contains("file quality outcome cannot be Pass", error.Message);
        }

        [Fact]
        public void Saves_a_failing_file_quality_outcome_against_the_same_no()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), AmlVersionId, ResponseRules.ChoiceNo);

            ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                service, Write(ResponseRules.ChoiceFail), ResponseRules.TypePassFail, FqVersion(), ReviewId);
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void Saves_a_grade_the_evidence_still_allows(int grade)
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                service, Write(grade), GradingRules.GradeResponseType, GradeVersion(), ReviewId);
        }

        [Fact]
        public void Saves_a_pass_when_the_form_carries_no_findings()
        {
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoicePass);

            ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                service,
                Write(ResponseRules.ChoicePass),
                GradingRules.GradeResponseType,
                GradeVersion(),
                ReviewId);
        }

        [Fact]
        public void Reads_nothing_for_an_answer_on_neither_gated_scale()
        {
            // A suitability tick is a Pass on a different scale. It must not cost a lookup,
            // and it must certainly not be refused.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);
            var before = service.RetrieveCount;

            ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                service, Write(ResponseRules.ChoicePass), 120910006, GradeVersion(), ReviewId);

            Assert.Equal(before, service.RetrieveCount);
        }

        [Fact]
        public void Reads_nothing_for_an_answer_no_rule_could_refuse()
        {
            // Only Pass and Pass with issues are ever refused, so a Fail must not pay for the
            // read of the rest of the review - and this runs inside the transaction holding
            // the checker's save open.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);
            var before = service.RetrieveCount;

            ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                service,
                Write(ResponseRules.ChoiceFail),
                ResponseRules.TypePassFail,
                FqVersion(),
                ReviewId);

            Assert.Equal(before, service.RetrieveCount);
        }

        [Fact]
        public void A_question_on_a_gated_scale_that_is_not_a_gated_question_is_not_refused()
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

            ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                service,
                Write(ResponseRules.ChoicePass),
                GradingRules.GradeResponseType,
                impostorVersion,
                ReviewId);
        }

        [Fact]
        public void A_write_clearing_an_outcome_is_never_refused()
        {
            // This is the write ResponseProgressPlugin issues when a finding arrives. Refusing
            // it would leave the review with an invalid outcome and no way to shed it.
            var service = Checklist();
            SeedAnswer(service, Guid.NewGuid(), E3VersionId, ResponseRules.ChoiceInsufficient);

            var cleared = new Entity("al_response", GradeResponseId) { ["al_answerchoice"] = null };

            ResponseGuardPlugin.EnsureOutcomeAgreesWithForm(
                service, cleared, GradingRules.GradeResponseType, GradeVersion(), ReviewId);
        }

        // ================================================================== the fixture

        /// <summary>
        /// A review wired response -> version -> question -> section exactly as the seed wires
        /// it, so every query walks a real chain: one Suitability core check, one Consumer Duty
        /// question, one AML and CRA point, the file quality outcome, Remedial action required?
        /// and the grade.
        /// </summary>
        private static FakeOrganizationService Checklist()
        {
            var service = new FakeOrganizationService();

            // The review itself, which until 2026-09-23 this fixture never seeded because
            // nothing read it. ReadGatingFacts now does: the AML and CRA count is scoped to
            // the version this review was issued, the way ResponseGuardPlugin already scopes
            // whether a section may be answered at all.
            service.Seed(
                "al_reviewinstance", ReviewId,
                "al_checklistversionid",
                new EntityReference("al_checklistversion", ChecklistVersionId));

            Question(service, "S-E3", "Q-E3-01", E3VersionId, 120910006);
            Question(service, "S-CD", "Q-CD-01", CdVersionId, 120910009);
            Question(service, ChecklistGating.AmlCraSectionCode, "Q-AML-01", AmlVersionId, 120910008);
            Question(
                service, "S-TAX", ChecklistGating.TaxCheckOutcomeQuestionCode,
                TaxOutcomeVersionId, 120910006);
            Question(
                service, "S-FQOUT", ChecklistGating.RemedialActionQuestionCode,
                RemedialVersionId, 120910007);

            // The two the guard is handed a version of by name, so their ids are fixed.
            //
            // Each hangs off the section it is actually seeded with. Until 2026-09-23 both
            // pointed at a section id that was never seeded, so the response -> version ->
            // question -> section join could not be walked and the row never came back at
            // all - which made the two tests below that expect these answers to be IGNORED
            // pass for the wrong reason, and left the outcome reads with nothing to find.
            var fqSectionId = Guid.NewGuid();
            service.Seed("al_section", fqSectionId, "al_sectioncode", "S-FQOUT");
            service.Seed(
                "al_question", FqQuestionId,
                "al_questioncode", FileQuality.QuestionCode,
                "al_sectionid", new EntityReference("al_section", fqSectionId));
            service.Seed(
                "al_questionversion", FqVersionId,
                "al_responsetype", new OptionSetValue(ResponseRules.TypePassFail),
                "al_questionid", new EntityReference("al_question", FqQuestionId));

            var gradeSectionId = Guid.NewGuid();
            service.Seed("al_section", gradeSectionId, "al_sectioncode", "S-GRADE");
            service.Seed(
                "al_question", GradeQuestionId,
                "al_questioncode", GradingRules.GradeQuestionCode,
                "al_sectionid", new EntityReference("al_section", gradeSectionId));
            service.Seed(
                "al_questionversion", GradeVersionId,
                "al_responsetype", new OptionSetValue(GradingRules.GradeResponseType),
                "al_questionid", new EntityReference("al_question", GradeQuestionId));

            return service;
        }

        private static void Question(
            FakeOrganizationService service,
            string sectionCode,
            string questionCode,
            Guid versionId,
            int responseType,
            Guid? checklistVersionId = null)
        {
            var sectionId = Guid.NewGuid();
            var questionId = Guid.NewGuid();

            // Defaults to the version the review under test was issued, so every existing
            // caller keeps the behaviour it had; a test that wants a DIFFERENT version's
            // section - the one this review must not be counted against - passes it.
            service.Seed(
                "al_section", sectionId,
                "al_sectioncode", sectionCode,
                "al_checklistversionid",
                new EntityReference(
                    "al_checklistversion", checklistVersionId ?? ChecklistVersionId));
            service.Seed(
                "al_question", questionId,
                "al_questioncode", questionCode,
                "al_sectionid", new EntityReference("al_section", sectionId));
            service.Seed(
                "al_questionversion", versionId,
                "al_responsetype", new OptionSetValue(responseType),
                "al_questionid", new EntityReference("al_question", questionId));
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

        /// <summary>
        /// The answer that has just been saved, as the clearing path is handed it.
        ///
        /// SEEDED as well as returned. ResponseProgressPlugin is registered post-operation
        /// (stage 40), so by the time it runs the row is committed and the review's own read
        /// of its answers includes it. A target that existed only as an in-flight entity would
        /// make this fixture describe a pipeline stage the step is not registered at, and
        /// every rule keyed on "what does the rest of the form say" would be tested against a
        /// form missing the answer that triggered it.
        /// </summary>
        private static Entity AnswerOn(FakeOrganizationService service, Guid versionId, int choice)
        {
            var id = Guid.NewGuid();
            SeedAnswer(service, id, versionId, choice);

            return new Entity("al_response", id)
            {
                ["al_answerchoice"] = new OptionSetValue(choice),
                ["al_questionversionid"] = new EntityReference("al_questionversion", versionId),
            };
        }

        private static Entity Write(int choice)
        {
            return new Entity("al_response", Guid.NewGuid())
            {
                ["al_answerchoice"] = new OptionSetValue(choice),
            };
        }

        /// <summary>The grade question version, as the guard has it in hand.</summary>
        private static Entity GradeVersion()
        {
            return new Entity("al_questionversion", GradeVersionId)
            {
                ["al_questionid"] = new EntityReference("al_question", GradeQuestionId),
            };
        }

        /// <summary>The file quality outcome's version, likewise.</summary>
        private static Entity FqVersion()
        {
            return new Entity("al_questionversion", FqVersionId)
            {
                ["al_questionid"] = new EntityReference("al_question", FqQuestionId),
            };
        }
    }
}
