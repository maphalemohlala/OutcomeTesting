using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Approving a remediation records the final outcome and closes the case, in one action
    /// (project owner, 2026-09-11, settling OD-041).
    ///
    /// Before this, approving left the case at Awaiting Recheck waiting for a separate
    /// regrade, and nothing prompted anyone to do it. IO-SEED-TXA-01 was the report: its
    /// remediation was done and approved, and it still showed Awaiting Recheck and the
    /// Insufficient evidence grade the AQS check gave before the adviser put anything right.
    /// The export collects Closed cases only, so a case parked there never reached Trail
    /// Light either.
    /// </summary>
    public class SignoffFinalOutcomeTests
    {
        private static readonly Guid CaseId = Guid.Parse("c1c1c1c1-0001-4001-8001-c1c1c1c1c1c1");
        private static readonly Guid ReviewId = Guid.Parse("c2c2c2c2-0002-4002-8002-c2c2c2c2c2c2");
        private static readonly Guid OutcomeId = Guid.Parse("c3c3c3c3-0003-4003-8003-c3c3c3c3c3c3");

        /// <summary>A case at Awaiting Recheck, graded Insufficient evidence by its AQS check.</summary>
        private static FakeOrganizationService AwaitingRecheck()
        {
            var svc = new FakeOrganizationService();

            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casereference", "IO-SEED-TXA-01",
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingRecheck));

            svc.Seed(
                "al_outcome", OutcomeId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomeInsufficient));

            return svc;
        }

        private static Entity Outcome(FakeOrganizationService svc)
        {
            return svc.Retrieve(
                "al_outcome", OutcomeId, new ColumnSet("al_finaloutcome", "al_initialoutcome", "al_regradereason"));
        }

        [Fact]
        public void The_grade_the_supervisor_recorded_becomes_the_final_outcome()
        {
            var svc = AwaitingRecheck();

            RegradeCasePlugin.Regrade(
                svc,
                svc,
                OutcomeId,
                RegradeCasePlugin.FinalOutcomeLabel(OutcomeRules.FinalOutcomePass),
                "Adviser evidenced the suitability rationale in full.",
                null,
                "signoff-regrade-" + OutcomeId.ToString("N"),
                new FakePluginExecutionContext());

            var outcome = Outcome(svc);

            Assert.Equal(
                OutcomeRules.FinalOutcomePass,
                outcome.GetAttributeValue<OptionSetValue>("al_finaloutcome").Value);
        }

        [Fact]
        public void The_initial_grade_survives_the_regrade()
        {
            // BR-007. The grade the check gave is what the file was assessed as, and a
            // remediation that put it right does not rewrite that history.
            var svc = AwaitingRecheck();

            RegradeCasePlugin.Regrade(
                svc, svc, OutcomeId,
                RegradeCasePlugin.FinalOutcomeLabel(OutcomeRules.FinalOutcomePass),
                "Rework accepted.", null, "signoff-regrade-" + OutcomeId.ToString("N"),
                new FakePluginExecutionContext());

            Assert.Equal(
                OutcomeRules.OutcomeInsufficient,
                Outcome(svc).GetAttributeValue<OptionSetValue>("al_initialoutcome").Value);
        }

        [Fact]
        public void Recording_the_final_outcome_closes_the_case()
        {
            // The whole point: one action, and the case is finished. CloseAfterRecheck only
            // moves a case that is AT Awaiting Recheck, which is where MoveCase leaves it.
            var svc = AwaitingRecheck();

            RegradeCasePlugin.Regrade(
                svc, svc, OutcomeId,
                RegradeCasePlugin.FinalOutcomeLabel(OutcomeRules.FinalOutcomePassWithIssues),
                "Rework accepted, minor gaps remain.", null,
                "signoff-regrade-" + OutcomeId.ToString("N"),
                new FakePluginExecutionContext());

            Assert.Equal(CaseLifecycle.Closed, CaseTransitions.CurrentStatus(svc, CaseId));
        }

        [Fact]
        public void The_supervisors_words_are_kept_as_the_regrade_reason()
        {
            // AD-031 wants a reason on the record, not just on the audit event.
            var svc = AwaitingRecheck();

            RegradeCasePlugin.Regrade(
                svc, svc, OutcomeId,
                RegradeCasePlugin.FinalOutcomeLabel(OutcomeRules.FinalOutcomePass),
                "Adviser evidenced the suitability rationale in full.", null,
                "signoff-regrade-" + OutcomeId.ToString("N"),
                new FakePluginExecutionContext());

            Assert.Equal(
                "Adviser evidenced the suitability rationale in full.",
                Outcome(svc).GetAttributeValue<string>("al_regradereason"));
        }

        [Fact]
        public void A_replayed_sign_off_does_not_regrade_twice()
        {
            // The page signs off one action at a time and each carries the same grade, so the
            // key has to be the outcome rather than the sign-off. A second run finds the
            // audit event and returns the first regrade.
            var svc = AwaitingRecheck();
            var key = "signoff-regrade-" + OutcomeId.ToString("N");

            RegradeCasePlugin.Regrade(
                svc, svc, OutcomeId, RegradeCasePlugin.FinalOutcomeLabel(OutcomeRules.FinalOutcomePass),
                "Rework accepted.", null, key, new FakePluginExecutionContext());

            var second = RegradeCasePlugin.Regrade(
                svc, svc, OutcomeId, RegradeCasePlugin.FinalOutcomeLabel(OutcomeRules.FinalOutcomePotentialHarm),
                "Different grade on a replay.", null, key, new FakePluginExecutionContext());

            Assert.False(second.Conflict);
            Assert.Equal(
                OutcomeRules.FinalOutcomePass,
                Outcome(svc).GetAttributeValue<OptionSetValue>("al_finaloutcome").Value);
        }

        [Theory]
        [InlineData(OutcomeRules.FinalOutcomePass, true)]
        [InlineData(OutcomeRules.FinalOutcomePassWithIssues, true)]
        [InlineData(OutcomeRules.FinalOutcomeInsufficient, true)]
        [InlineData(OutcomeRules.FinalOutcomePotentialHarm, true)]
        [InlineData(OutcomeRules.OutcomePass, false)]
        [InlineData(0, false)]
        public void Only_the_four_br005_final_values_are_a_final_outcome(int value, bool expected)
        {
            // The guard refuses anything else before the sign-off is written. The initial
            // band (1209107_0_x) is a different option set and is not one of these.
            Assert.Equal(expected, OutcomeRules.IsFinalOutcome(value));
        }
    }
}
