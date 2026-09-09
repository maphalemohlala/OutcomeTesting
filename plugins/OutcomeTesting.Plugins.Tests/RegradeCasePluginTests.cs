using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The recheck step (AD-057 lifecycle "Awaiting Recheck/Regrade -> Closed"): setting the
    /// final outcome is what closes a remediated case. Nothing else moved a case off Awaiting
    /// Recheck, so every remediated case sat there, invisible to the Closed-only export.
    /// </summary>
    public class RegradeCasePluginTests
    {
        private static readonly Guid CaseId = Guid.Parse("99999999-aaaa-4aaa-8aaa-999999999999");

        private static FakeOrganizationService CaseAt(int status)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(status));
            return svc;
        }

        private static int CaseStatus(FakeOrganizationService svc)
        {
            return svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_casestatus").Value;
        }

        private static EntityReference Case()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        [Fact]
        public void Closes_a_case_awaiting_recheck()
        {
            var svc = CaseAt(CaseLifecycle.AwaitingRecheck);

            RegradeCasePlugin.CloseAfterRecheck(svc, Case());

            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));
        }

        [Fact]
        public void Leaves_a_closed_case_closed()
        {
            // The AD-031 privileged correction of a closed outcome: the grade changes, the
            // case does not reopen.
            var svc = CaseAt(CaseLifecycle.Closed);

            RegradeCasePlugin.CloseAfterRecheck(svc, Case());

            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));
            Assert.Empty(svc.Updates);
        }

        [Theory]
        [InlineData(CaseLifecycle.Submitted)]
        [InlineData(CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.AwaitingSignoff)]
        public void Leaves_a_case_elsewhere_in_the_lifecycle_where_it_is(int status)
        {
            var svc = CaseAt(status);

            RegradeCasePlugin.CloseAfterRecheck(svc, Case());

            Assert.Equal(status, CaseStatus(svc));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void An_outcome_with_no_case_is_a_no_op()
        {
            var svc = new FakeOrganizationService();

            RegradeCasePlugin.CloseAfterRecheck(svc, null);

            Assert.Empty(svc.Updates);
        }
    }
}
