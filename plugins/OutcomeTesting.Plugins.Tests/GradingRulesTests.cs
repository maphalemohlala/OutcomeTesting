using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The conditional primary root cause (item 3, 2026-09-19). Pure rules, so every grade
    /// the scale offers can be asked about directly rather than through a submission.
    ///
    /// Mirrored by app/src/features/reviews/gradingRules.test.ts, which asks the same
    /// questions of the TypeScript copy: a change made to one and not the other shows up as
    /// two suites disagreeing rather than as a rule that holds on one surface only.
    /// </summary>
    public class GradingRulesTests
    {
        [Fact]
        public void A_pass_owes_no_root_cause()
        {
            // The rule's whole purpose: a passing file has nothing to explain, and Q-GR-02
            // is seeded mandatory, so without this every Pass review was held at submission
            // by a question whose only honest answer was none of the nine.
            Assert.False(GradingRules.RootCauseRequired(ResponseRules.ChoicePass));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void Every_other_grade_owes_one(int grade)
        {
            Assert.True(GradingRules.RootCauseRequired(grade));
        }

        [Fact]
        public void A_grade_outside_the_scale_owes_one()
        {
            // An unrecognised value is not a pass. Demanding the root cause is the safe
            // direction, and it is the same direction OutcomeRules.TryGradeFromAnswer takes
            // when it refuses to read an unknown grade as the most favourable one.
            Assert.True(GradingRules.RootCauseRequired(999));
        }

        [Fact]
        public void An_ungraded_review_owes_none_yet()
        {
            // Not an excuse: the grade is mandatory in its own right, so an ungraded review
            // is still refused. This only keeps Q-GR-02 out of that refusal, because until
            // the grade is answered nobody can say whether it is owed at all.
            Assert.False(GradingRules.RootCauseRequired(null));
        }

        [Fact]
        public void A_pass_clears_a_root_cause_already_recorded()
        {
            Assert.True(GradingRules.RootCauseCleared(ResponseRules.ChoicePass));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        [InlineData(999)]
        public void Any_other_grade_leaves_it_alone(int grade)
        {
            Assert.False(GradingRules.RootCauseCleared(grade));
        }

        [Fact]
        public void An_ungraded_review_clears_nothing()
        {
            // The case the two rules are deliberately not each other's negation for. The
            // grade and the root cause sit one above the other and nothing makes a checker
            // answer them in order; picking the cause first must not see it wiped on the way
            // past by a grade that has not been given yet.
            Assert.False(GradingRules.RootCauseCleared(null));
            Assert.False(GradingRules.RootCauseRequired(null));
        }

        [Fact]
        public void The_two_question_codes_are_the_ones_the_checklist_seeds()
        {
            // Both are read by name in compiled C#, so this is the AD-122 contract written
            // down where a rename would break a test rather than a review.
            Assert.Equal("Q-GR-01", GradingRules.GradeQuestionCode);
            Assert.Equal("Q-GR-02", GradingRules.RootCauseQuestionCode);
        }

        [Fact]
        public void Both_grading_questions_are_protected_from_retirement()
        {
            // Retiring Q-GR-02 now leaves every non-pass review with no cause to record and
            // the trend MI with an empty column, which is the ChecklistGuards test.
            Assert.NotNull(ChecklistGuards.ProtectedReason(GradingRules.GradeQuestionCode));
            Assert.NotNull(ChecklistGuards.ProtectedReason(GradingRules.RootCauseQuestionCode));
        }
    }
}
