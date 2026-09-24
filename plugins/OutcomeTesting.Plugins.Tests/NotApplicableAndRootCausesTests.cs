using System.Linq;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The two response types added on 2026-09-24 (project owner): N/A on the CRP rows and
    /// on the E4 concessions row, and several primary root causes rather than one.
    /// </summary>
    public class NotApplicableAndRootCausesTests
    {
        [Fact]
        public void The_na_scale_is_pass_fail_insufficient_then_na()
        {
            Assert.Equal(
                new[] { 120910300, 120910301, 120910302, 120910307 },
                ResponseRules.PermittedChoices(ResponseRules.TypePassFailInsufficientNa).ToArray());
            Assert.Equal(
                ResponseRules.AnswerColumn.Choice,
                ResponseRules.ColumnFor(ResponseRules.TypePassFailInsufficientNa));
        }

        [Fact]
        public void Root_causes_are_several_ticks_from_the_nine_causes()
        {
            Assert.Equal(
                ResponseRules.AnswerColumn.Choices,
                ResponseRules.ColumnFor(ResponseRules.TypeMultiSelectRootCause));
            Assert.Equal(
                new[]
                {
                    120910320, 120910321, 120910322, 120910323, 120910324,
                    120910325, 120910326, 120910327, 120910328,
                },
                ResponseRules.PermittedChoices(ResponseRules.TypeMultiSelectRootCause).ToArray());

            Assert.Null(ResponseRules.ValidateAnswer(
                ResponseRules.TypeMultiSelectRootCause, false, false, null, new[] { 120910320, 120910326 }));
        }

        [Fact]
        public void A_tax_reason_is_not_a_root_cause_and_a_root_cause_is_not_a_tax_reason()
        {
            Assert.NotNull(ResponseRules.ValidateAnswer(
                ResponseRules.TypeMultiSelectRootCause, false, false, null, new[] { 120910340 }));
            Assert.NotNull(ResponseRules.ValidateAnswer(
                ResponseRules.TypeMultiSelect, false, false, null, new[] { 120910320 }));
        }

        [Fact]
        public void Root_causes_are_not_a_single_choice()
        {
            Assert.NotNull(ResponseRules.ValidateAnswer(
                ResponseRules.TypeMultiSelectRootCause, false, false, 120910320, null));
        }

        [Fact]
        public void Na_is_not_a_failure_but_a_fail_on_the_na_scale_is()
        {
            Assert.True(Remediation.IsRemediableScale(ResponseRules.TypePassFailInsufficientNa));
            Assert.False(Remediation.IsNonPassAnswer(ResponseRules.TypePassFailInsufficientNa, ResponseRules.ChoiceNa));
            Assert.False(Remediation.IsNonPassAnswer(ResponseRules.TypePassFailInsufficientNa, ResponseRules.ChoicePass));
            Assert.True(Remediation.IsNonPassAnswer(ResponseRules.TypePassFailInsufficientNa, ResponseRules.ChoiceFail));
            Assert.True(Remediation.IsNonPassAnswer(ResponseRules.TypePassFailInsufficientNa, ResponseRules.ChoiceInsufficient));
        }

        [Fact]
        public void Na_neither_restricts_the_grade_nor_counts_as_a_fail()
        {
            Assert.False(ChecklistGating.IsInsufficient(ResponseRules.ChoiceNa));
            Assert.False(ChecklistGating.IsNoOrFail(ResponseRules.ChoiceNa));
        }

        [Fact]
        public void Both_root_cause_types_are_recognised_as_the_root_cause()
        {
            Assert.True(GradingRules.IsRootCauseResponseType(120910003));
            Assert.True(GradingRules.IsRootCauseResponseType(ResponseRules.TypeMultiSelectRootCause));
            Assert.False(GradingRules.IsRootCauseResponseType(ResponseRules.TypeMultiSelect));
        }
    }
}
