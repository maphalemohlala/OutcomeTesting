using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// BR-007 keeps the initial and final outcomes side by side so both survive a regrade,
    /// so every reader has to resolve which one is in force. These pin that precedence,
    /// which three call sites previously spelled out inline.
    /// </summary>
    public class OutcomesTests
    {
        private static Entity Outcome(int? initial, int? final)
        {
            var e = new Entity("al_outcome");
            if (initial.HasValue)
            {
                e[Outcomes.InitialOutcomeAttr] = new OptionSetValue(initial.Value);
            }

            if (final.HasValue)
            {
                e[Outcomes.FinalOutcomeAttr] = new OptionSetValue(final.Value);
            }

            return e;
        }

        [Fact]
        public void Uses_the_initial_outcome_until_one_is_finalised()
        {
            Assert.Equal(
                OutcomeRules.OutcomePotentialHarm,
                Outcomes.EffectiveOutcome(Outcome(OutcomeRules.OutcomePotentialHarm, null)));
        }

        [Fact]
        public void Prefers_the_final_outcome_once_a_regrade_has_recorded_one()
        {
            // OD-007/AD-031: a regrade is the T&C Manager's decision and supersedes the
            // checker's grade. Reading the initial one here would report a superseded grade
            // to Trail Light.
            Assert.Equal(
                OutcomeRules.OutcomePass,
                Outcomes.EffectiveOutcome(Outcome(OutcomeRules.OutcomePotentialHarm, OutcomeRules.FinalOutcomePass)));
        }

        [Fact]
        public void Reports_no_outcome_rather_than_a_pass_when_the_row_carries_neither()
        {
            // A missing outcome is "nobody has graded this yet", not "it passed". Callers
            // gate on HasValue precisely so an ungraded case is not silently cleared.
            Assert.False(Outcomes.EffectiveOutcome(Outcome(null, null)).HasValue);
        }

        [Fact]
        public void Reports_no_outcome_for_a_missing_row()
        {
            Assert.False(Outcomes.EffectiveOutcome(null).HasValue);
            Assert.Null(Outcomes.EffectiveOutcomeLabel(null));
        }

        [Fact]
        public void Resolves_the_label_with_the_same_precedence_as_the_value()
        {
            // AD-039 column 15 reports the grade as text; the label and the value must never
            // disagree about which outcome is in force.
            var e = Outcome(OutcomeRules.OutcomePotentialHarm, OutcomeRules.OutcomePass);
            e.FormattedValues[Outcomes.InitialOutcomeAttr] = "Potential harm";
            e.FormattedValues[Outcomes.FinalOutcomeAttr] = "Pass";

            Assert.Equal("Pass", Outcomes.EffectiveOutcomeLabel(e));
        }

        [Fact]
        public void Falls_back_to_the_initial_label_when_nothing_is_finalised()
        {
            var e = Outcome(OutcomeRules.OutcomePotentialHarm, null);
            e.FormattedValues[Outcomes.InitialOutcomeAttr] = "Potential harm";

            Assert.Equal("Potential harm", Outcomes.EffectiveOutcomeLabel(e));
        }

        [Theory]
        [InlineData(OutcomeRules.FinalOutcomePass, OutcomeRules.OutcomePass)]
        [InlineData(OutcomeRules.FinalOutcomePassWithIssues, OutcomeRules.OutcomePassWithIssues)]
        [InlineData(OutcomeRules.FinalOutcomeInsufficient, OutcomeRules.OutcomeInsufficient)]
        [InlineData(OutcomeRules.FinalOutcomePotentialHarm, OutcomeRules.OutcomePotentialHarm)]
        public void Resolves_a_final_grade_onto_the_common_grade_scale(int final, int expected)
        {
            // al_initialoutcome and al_finaloutcome are separate option sets - 1209107_0_x
            // is the grade a check gave, 1209107_1_x the grade it ended on. Every caller
            // compares this result against the OutcomeRules grade constants, which are the
            // 1209107_0_x band, so returning a final value raw measured it against the wrong
            // scale and matched nothing. That read a regraded Pass as a non-pass.
            Assert.Equal(expected, Outcomes.EffectiveOutcome(Outcome(OutcomeRules.OutcomeInsufficient, final)));
        }

        [Fact]
        public void Leaves_a_value_that_is_not_on_the_final_scale_alone()
        {
            // An unrecognised value is not quietly mapped to the most favourable grade;
            // TryGradeFromAnswer refuses to default for the same reason.
            Assert.Equal(999, Outcomes.EffectiveOutcome(Outcome(OutcomeRules.OutcomeInsufficient, 999)));
        }
    }
}
