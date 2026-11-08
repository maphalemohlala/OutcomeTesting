using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Who the Daily Feed names (client, 2026-11-06): someone for a File Quality Fail, and
    /// someone for Insufficient evidence or Potential harm, and nobody for any other grade.
    /// </summary>
    public class DailyFeedAccountabilityTests
    {
        private const string Fq = GenerateExportPlugin.FqParaplannerFlag;
        private const string Aq = GenerateExportPlugin.AqAdviserFlag;

        private static bool Names(Entity row, string flag, int fileChoice, int outcome)
        {
            return GenerateExportPlugin.IsAccountable(row, flag, fileChoice, outcome);
        }

        [Theory]
        [InlineData(OutcomeRules.OutcomePass)]
        [InlineData(OutcomeRules.OutcomePassWithIssues)]
        public void A_file_fail_names_nobody_for_an_advice_grade_that_is_not_a_fail(int outcome)
        {
            Assert.True(Names(null, Fq, ResponseRules.ChoiceFail, outcome));
            Assert.False(Names(null, Aq, ResponseRules.ChoiceFail, outcome));
        }

        [Theory]
        [InlineData(OutcomeRules.OutcomeInsufficient)]
        [InlineData(OutcomeRules.OutcomePotentialHarm)]
        public void A_file_fail_with_an_advice_fail_names_both(int outcome)
        {
            Assert.True(Names(null, Fq, ResponseRules.ChoiceFail, outcome));
            Assert.True(Names(null, Aq, ResponseRules.ChoiceFail, outcome));
        }

        [Theory]
        [InlineData(OutcomeRules.OutcomeInsufficient)]
        [InlineData(OutcomeRules.OutcomePotentialHarm)]
        public void An_advice_fail_on_a_passed_file_names_nobody_for_the_file(int outcome)
        {
            Assert.False(Names(null, Fq, ResponseRules.ChoicePass, outcome));
            Assert.True(Names(null, Aq, ResponseRules.ChoicePass, outcome));
        }

        [Fact]
        public void A_judgement_on_the_file_does_not_blank_an_advice_fail()
        {
            var row = new Entity("al_outcome") { [GenerateExportPlugin.FqAdviserFlag] = true };

            Assert.True(Names(row, Aq, ResponseRules.ChoiceFail, OutcomeRules.OutcomePotentialHarm));
            Assert.True(Names(row, GenerateExportPlugin.FqAdviserFlag, ResponseRules.ChoiceFail, OutcomeRules.OutcomePotentialHarm));
            Assert.False(Names(row, Fq, ResponseRules.ChoiceFail, OutcomeRules.OutcomePotentialHarm));
        }

        [Fact]
        public void A_judgement_left_behind_by_a_regrade_drops_out()
        {
            var row = new Entity("al_outcome") { [Aq] = true, [Fq] = true };

            Assert.False(Names(row, Aq, ResponseRules.ChoicePass, OutcomeRules.OutcomePassWithIssues));
            Assert.False(Names(row, Fq, ResponseRules.ChoicePass, OutcomeRules.OutcomePassWithIssues));
        }

        [Fact]
        public void A_tax_only_case_has_no_advice_grade_to_name_anyone_for()
        {
            Assert.Null(Outcomes.EffectiveOutcomeLabel(null));
            Assert.False(GenerateExportPlugin.IsAccountable(null, Aq, ResponseRules.ChoiceFail, null));
            Assert.True(GenerateExportPlugin.IsAccountable(null, Fq, ResponseRules.ChoiceFail, null));
        }
    }
}
