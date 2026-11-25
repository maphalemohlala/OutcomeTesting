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

        [Theory]
        // A fail regraded to a pass: the daily file reports the initial fail and names someone;
        // the quarterly file reports the pass and names nobody.
        [InlineData(OutcomeRules.OutcomePass, OutcomeRules.OutcomePotentialHarm, true, false)]
        [InlineData(OutcomeRules.OutcomePassWithIssues, OutcomeRules.OutcomeInsufficient, true, false)]
        // A pass with issues regraded to a fail: only the quarterly file reports the fail.
        [InlineData(OutcomeRules.OutcomeInsufficient, OutcomeRules.OutcomePassWithIssues, false, true)]
        // Neither grade failed: nobody, in either file.
        [InlineData(OutcomeRules.OutcomePass, OutcomeRules.OutcomePassWithIssues, false, false)]
        public void Each_file_names_people_against_the_grade_it_reports(
            int effective, int initial, bool namedDaily, bool namedQuarterly)
        {
            var daily = GenerateExportPlugin.FileOutcome(true, effective, initial);
            var quarterly = GenerateExportPlugin.FileOutcome(false, effective, initial);
            Assert.Equal(namedDaily, Names(null, Aq, ResponseRules.ChoicePass, daily.Value));
            Assert.Equal(namedQuarterly, Names(null, Aq, ResponseRules.ChoicePass, quarterly.Value));
        }

        [Fact]
        public void The_daily_file_falls_back_to_the_grade_in_force_when_none_was_first_recorded()
        {
            Assert.Equal(OutcomeRules.OutcomePass, GenerateExportPlugin.FileOutcome(true, OutcomeRules.OutcomePass, null));
        }

        [Theory]
        [InlineData(ResponseRules.ChoiceFail, OutcomeRules.OutcomePass, null, true)]
        [InlineData(ResponseRules.ChoicePass, OutcomeRules.OutcomePassWithIssues, null, true)]
        [InlineData(ResponseRules.ChoicePass, OutcomeRules.OutcomeInsufficient, null, true)]
        [InlineData(ResponseRules.ChoicePass, OutcomeRules.OutcomePotentialHarm, null, true)]
        [InlineData(ResponseRules.ChoicePass, OutcomeRules.OutcomePass, null, false)]
        // A Tax-only case: no advice grade, the Tax verdict decides. The Tax "Pass with issues" is
        // stored as ChoiceInsufficient (120910302), as case 991106001 in DEV holds it.
        [InlineData(ResponseRules.ChoicePass, null, ResponseRules.ChoiceFail, true)]
        [InlineData(ResponseRules.ChoicePass, null, ResponseRules.ChoiceInsufficient, true)]
        [InlineData(ResponseRules.ChoicePass, null, ResponseRules.ChoicePass, false)]
        public void The_daily_file_holds_any_type_of_failure_and_no_clean_pass(
            int fileChoice, int? advice, int? tax, bool belongs)
        {
            Assert.Equal(belongs, GenerateExportPlugin.BelongsInDailyFile(fileChoice, advice, tax));
        }

        [Fact]
        public void The_batch_is_named_for_its_file_and_range()
        {
            var oct1 = new System.DateTime(2026, 10, 1);
            var dec31 = new System.DateTime(2026, 12, 31);
            Assert.Equal("Daily file, checked 1 Oct 2026", GenerateExportPlugin.FileName(true, oct1, oct1));
            Assert.Equal("Quarterly file, checked 1 Oct 2026 to 31 Dec 2026", GenerateExportPlugin.FileName(false, oct1, dec31));
            Assert.Equal("Quarterly file, every case", GenerateExportPlugin.FileName(false, null, null));
            Assert.Equal("Daily file, checked on or after 1 Oct 2026", GenerateExportPlugin.FileName(true, oct1, null));
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
