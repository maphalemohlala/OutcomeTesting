using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>The client's checklist decisions of 2026-11-06, as pure rules.</summary>
    public class UatDecisionsTests
    {
        private const int Pass = ResponseRules.ChoicePass;
        private const int Issues = ResponseRules.ChoicePassWithIssues;
        private const int Harm = ResponseRules.ChoiceFail;
        private const int Insufficient = ResponseRules.ChoiceInsufficient;
        private const int Grade_Harm = ResponseRules.ChoicePotentialHarm;
        private const int Na = ResponseRules.ChoiceNa;
        private const int Yes = ResponseRules.ChoiceYes;
        private const int No = ResponseRules.ChoiceNo;

        [Fact]
        public void All_pass_or_na_allows_only_a_pass_with_no_remedial_action()
        {
            var expected = GradingRules.For(new[] { Pass, Na }, new[] { Yes, Na }, false);

            Assert.Equal(new[] { Pass }, expected.Grades);
            Assert.Equal(Pass, expected.FileQuality);
            Assert.Null(GradingRules.Refusal(expected, Pass, No, Pass));
            Assert.NotNull(GradingRules.Refusal(expected, Pass, Yes, Pass));
            Assert.NotNull(GradingRules.Refusal(expected, Pass, No, Issues));
        }

        [Fact]
        public void Pass_with_issues_on_a_check_means_pass_with_issues_and_a_remedial_action()
        {
            var expected = GradingRules.For(new[] { Pass, Issues }, new[] { Yes }, false);

            Assert.Equal(new[] { Issues }, expected.Grades);
            Assert.Null(GradingRules.Refusal(expected, Pass, Yes, Issues));
            Assert.NotNull(GradingRules.Refusal(expected, Pass, No, Issues));
        }

        [Theory]
        [InlineData(Harm)]
        [InlineData(Insufficient)]
        public void Harm_or_insufficient_evidence_allows_only_those_two_grades(int answer)
        {
            var expected = GradingRules.For(new[] { Pass, Issues, answer }, new[] { Yes }, false);

            Assert.Equal(new[] { Insufficient, Grade_Harm }, expected.Grades);
            Assert.NotNull(GradingRules.Refusal(expected, Pass, Yes, Issues));
            Assert.Null(GradingRules.Refusal(expected, Pass, Yes, Grade_Harm));
        }

        [Fact]
        public void A_no_or_a_fail_point_makes_file_quality_a_fail_and_owes_an_action()
        {
            Assert.Equal(ResponseRules.ChoiceFail, GradingRules.For(new[] { Pass }, new[] { Yes, No }, false).FileQuality);

            var ticked = GradingRules.For(new[] { Pass }, new[] { Yes }, true);
            Assert.Equal(ResponseRules.ChoiceFail, ticked.FileQuality);
            Assert.Equal(new[] { Pass, Issues }, ticked.Grades);
            Assert.NotNull(GradingRules.Refusal(ticked, ResponseRules.ChoicePass, Yes, Pass));
            Assert.NotNull(GradingRules.Refusal(ticked, ResponseRules.ChoiceFail, No, Pass));
            Assert.Null(GradingRules.Refusal(ticked, ResponseRules.ChoiceFail, Yes, Pass));
        }

        [Fact]
        public void Pass_with_issues_on_a_suitability_check_is_something_to_put_right()
        {
            Assert.True(Remediation.IsNonPassAnswer(ResponseRules.TypePassFailInsufficientNa, Issues));
            Assert.False(Remediation.IsNonPassAnswer(ResponseRules.TypePassFailInsufficientNa, Pass));
            Assert.False(Remediation.IsNonPassAnswer(ResponseRules.TypePassFailInsufficientNa, Na));
        }

        [Fact]
        public void Further_actions_on_an_item_each_keep_their_own_words_in_order()
        {
            var map = new Dictionary<string, string>
            {
                { "Item A", "First" },
                { "Item A" + RemedialActions.ExtraMark + "3", "Third" },
                { "Item A" + RemedialActions.ExtraMark + "2", "Second" },
                { "Item B", "Other" },
                { "Item AB" + RemedialActions.ExtraMark + "2", "Not this item" },
            };

            var expanded = RemedialActions.Expand(new[] { "Item A", "Item B" }, map);

            Assert.Equal(new[] { "Item A", "Item A", "Item A", "Item B" }, expanded.Select(p => p.Key));
            Assert.Equal(new[] { "First", "Second", "Third", "Other" }, expanded.Select(p => p.Value));
        }

        [Fact]
        public void A_tax_category_answered_yes_carries_its_comments_and_refuses_without_them()
        {
            var choices = new Dictionary<string, int> { { "Q-TAX-05", Yes }, { "Q-TAX-07", No }, { "Q-TAX-09", Yes } };
            var comments = new Dictionary<string, string> { { "Q-TAX-06", " Rerun the CGT figures. " } };

            var raised = TaxCategories.Raised(
                code => choices.ContainsKey(code) ? choices[code] : (int?)null,
                code => comments.ContainsKey(code) ? comments[code] : null);

            Assert.Equal(new[] { "Calculations", "Omissions" }, raised.Keys);
            Assert.Equal("Rerun the CGT figures.", raised["Calculations"]);
            Assert.Contains("Omissions", TaxCategories.Refusal(raised));
            raised["Omissions"] = "Add the trust deed.";
            Assert.Null(TaxCategories.Refusal(raised));
        }

        [Fact]
        public void The_client_contact_answer_travels_in_the_parked_words()
        {
            var map = RemedialActions.Parse("[{\"item\":\"__clientcontact__\",\"text\":\"120910795\"}]");

            Assert.Equal(120910795, RemedialActions.ClientContact(map));
            Assert.Null(RemedialActions.ClientContact(new Dictionary<string, string>()));
        }
    }
}
