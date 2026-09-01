using System;
using System.Linq;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Rules for a checklist answer (AD-023, AD-053). These are the repository's first
    /// plug-in tests: ResponseRules is deliberately free of Dataverse types so the rules
    /// can be verified without a fake organisation service.
    /// </summary>
    public class ResponseRulesTests
    {
        [Theory]
        [InlineData(120910000, ResponseRules.AnswerColumn.Text)]
        [InlineData(120910001, ResponseRules.AnswerColumn.Text)]
        [InlineData(120910002, ResponseRules.AnswerColumn.Date)]
        [InlineData(120910003, ResponseRules.AnswerColumn.Choice)]
        [InlineData(120910004, ResponseRules.AnswerColumn.Choices)]
        [InlineData(120910005, ResponseRules.AnswerColumn.Choice)]
        [InlineData(120910010, ResponseRules.AnswerColumn.Choice)]
        public void ColumnFor_maps_each_response_type_to_its_typed_column(int type, ResponseRules.AnswerColumn expected)
        {
            Assert.Equal(expected, ResponseRules.ColumnFor(type));
        }

        [Fact]
        public void ColumnFor_rejects_an_unknown_response_type()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ResponseRules.ColumnFor(999));
        }

        [Fact]
        public void PermittedChoices_for_grade_is_the_four_BR005_outcomes()
        {
            Assert.Equal(
                new[] { 120910300, 120910303, 120910302, 120910304 },
                ResponseRules.PermittedChoices(120910010).ToArray());
        }

        [Fact]
        public void PermittedChoices_for_yes_no_na_excludes_insufficient_evidence()
        {
            var permitted = ResponseRules.PermittedChoices(120910008);
            Assert.Contains(120910307, permitted);
            Assert.DoesNotContain(120910302, permitted);
        }

        [Fact]
        public void PermittedChoices_for_single_select_is_the_root_cause_block()
        {
            var permitted = ResponseRules.PermittedChoices(120910003);
            Assert.Equal(9, permitted.Count);
            Assert.Contains(120910320, permitted);
            Assert.Contains(120910328, permitted);
        }

        [Fact]
        public void ValidateAnswer_accepts_a_permitted_choice()
        {
            Assert.Null(ResponseRules.ValidateAnswer(120910005, false, false, 120910301, new int[0]));
        }

        [Fact]
        public void ValidateAnswer_rejects_a_choice_outside_the_scale()
        {
            // Potential harm belongs to Grade, not Pass / Fail.
            var failure = ResponseRules.ValidateAnswer(120910005, false, false, 120910304, new int[0]);
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_allows_a_note_alongside_a_choice()
        {
            // al_answertext doubles as evidence held with a structured answer.
            // useReviewDetail.ts noteOf() already reads the record this way.
            Assert.Null(ResponseRules.ValidateAnswer(120910005, true, false, 120910301, new int[0]));
        }

        [Fact]
        public void ValidateAnswer_rejects_an_answer_in_the_wrong_column()
        {
            var failure = ResponseRules.ValidateAnswer(120910000, false, true, null, new int[0]);
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_rejects_a_choice_on_a_text_question()
        {
            var failure = ResponseRules.ValidateAnswer(120910000, true, false, 120910300, new int[0]);
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_accepts_an_empty_answer_so_a_draft_can_be_cleared()
        {
            Assert.Null(ResponseRules.ValidateAnswer(120910005, false, false, null, new int[0]));
        }

        [Fact]
        public void ValidateAnswer_rejects_a_multi_select_value_outside_the_tax_reason_list()
        {
            var failure = ResponseRules.ValidateAnswer(120910004, false, false, null, new[] { 120910340, 120910300 });
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_accepts_every_tax_reason_at_once()
        {
            var all = new[] { 120910340, 120910341, 120910342, 120910343, 120910344 };
            Assert.Null(ResponseRules.ValidateAnswer(120910004, false, false, null, all));
        }

        [Theory]
        [InlineData(120910200, 120910100)]
        [InlineData(120910201, 120910101)]
        public void OwnerRoleFor_maps_review_type_to_the_section_owner_role(int reviewType, int expected)
        {
            Assert.Equal(expected, ResponseRules.OwnerRoleFor(reviewType));
        }

        [Theory]
        [InlineData(120910301, true)]
        [InlineData(120910302, true)]
        [InlineData(120910304, true)]
        [InlineData(120910300, false)]
        [InlineData(120910303, false)]
        public void IsNonPass_covers_fail_insufficient_and_potential_harm(int choice, bool expected)
        {
            Assert.Equal(expected, ResponseRules.IsNonPass(choice));
        }

        [Fact]
        public void BuildResponseCode_is_stable_and_fits_the_code_column()
        {
            var review = new Guid("11111111-1111-4111-8111-111111111111");
            var question = new Guid("22222222-2222-4222-8222-222222222222");
            var code = ResponseRules.BuildResponseCode(review, question);

            Assert.Equal(code, ResponseRules.BuildResponseCode(review, question));
            Assert.True(code.Length <= 100);
            Assert.Contains("|", code);
        }
    }
}
