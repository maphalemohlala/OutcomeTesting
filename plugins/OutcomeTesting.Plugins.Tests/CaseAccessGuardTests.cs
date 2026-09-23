using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The access columns are the portal's case boundary (AD-218), so nobody writes them but
    /// the reconciler. A client write arrives at pipeline depth 1; the reconciler's arrives
    /// from inside a plug-in, deeper.
    /// </summary>
    public class CaseAccessGuardTests
    {
        private static Entity Target(string attribute)
        {
            return new Entity("al_outcomecase", Guid.NewGuid())
            {
                [attribute] = new EntityReference("contact", Guid.NewGuid()),
            };
        }

        [Theory]
        [InlineData(CaseAccessReconciler.TaxCheckerAttr)]
        [InlineData(CaseAccessReconciler.AqsCheckerAttr)]
        [InlineData(CaseAccessReconciler.QueueAccountAttr)]
        [InlineData(CaseAccessReconciler.AdviserAttr)]
        [InlineData(CaseAccessReconciler.SupervisorAttr)]
        public void A_direct_write_to_an_access_column_is_refused(string attribute)
        {
            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseAccessGuardPlugin.EnsureNotWrittenDirectly(Target(attribute), 1));

            Assert.StartsWith(CommandHelpers.UnauthorizedPrefix, refusal.Message);
        }

        [Fact]
        public void A_direct_write_to_the_queue_date_is_refused()
        {
            var target = new Entity("al_outcomecase", Guid.NewGuid()) { [CaseAccessReconciler.QueuedOnAttr] = DateTime.UtcNow };
            Assert.Throws<InvalidPluginExecutionException>(() => CaseAccessGuardPlugin.EnsureNotWrittenDirectly(target, 1));
        }

        [Fact]
        public void The_reconciler_writing_from_inside_a_plugin_is_allowed()
        {
            CaseAccessGuardPlugin.EnsureNotWrittenDirectly(Target(CaseAccessReconciler.AdviserAttr), 2);
        }

        [Fact]
        public void A_direct_write_to_other_columns_is_untouched()
        {
            var target = new Entity("al_outcomecase", Guid.NewGuid()) { ["al_clientname"] = "A client" };
            CaseAccessGuardPlugin.EnsureNotWrittenDirectly(target, 1);
        }
    }
}
