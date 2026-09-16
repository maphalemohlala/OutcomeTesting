using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// SetFailAccountabilityPlugin.RefusalFor — whether there is a fail on this case to
    /// attribute at all.
    ///
    /// The command exists to override the pair the export derives (project owner,
    /// 2026-09-16). It guarded on the advice quality outcome alone, which refused every
    /// case that failed only on file quality — five of the seven closed cases in DEV, and
    /// exactly the population the derived pair names the paraplanner for.
    /// </summary>
    public class SetFailAccountabilityPluginTests
    {
        [Fact]
        public void A_non_pass_outcome_is_attributable()
        {
            Assert.Null(SetFailAccountabilityPlugin.RefusalFor(OutcomeRules.OutcomePotentialHarm, false));
        }

        [Fact]
        public void A_file_quality_fail_is_attributable_even_though_the_advice_passed()
        {
            // The case the old guard turned away. The file failed; somebody carries that,
            // and the derived paraplanner may not be who.
            Assert.Null(SetFailAccountabilityPlugin.RefusalFor(OutcomeRules.OutcomePass, true));
        }

        [Fact]
        public void A_file_quality_fail_is_attributable_with_no_advice_grade_at_all()
        {
            // A Tax leg records no advice quality grade. The file quality answer is still
            // an answer, and still a fail.
            Assert.Null(SetFailAccountabilityPlugin.RefusalFor(null, true));
        }

        [Fact]
        public void A_clean_case_is_refused()
        {
            var refusal = SetFailAccountabilityPlugin.RefusalFor(OutcomeRules.OutcomePass, false);

            Assert.NotNull(refusal);
            Assert.Contains("passed", refusal);
        }

        [Fact]
        public void A_case_with_nothing_recorded_is_refused()
        {
            // Not the same refusal as a pass: nothing has been graded, so there is no fail
            // to attribute rather than a fail that did not happen.
            var refusal = SetFailAccountabilityPlugin.RefusalFor(null, false);

            Assert.NotNull(refusal);
            Assert.Contains("no outcome recorded", refusal);
        }
    }
}
