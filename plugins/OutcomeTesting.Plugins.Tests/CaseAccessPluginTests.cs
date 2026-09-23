using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class CaseAccessPluginTests
    {
        private static readonly Guid CaseId = Guid.Parse("dddddddd-0000-4000-8000-000000000011");
        private static readonly Guid ReviewId = Guid.Parse("dddddddd-0000-4000-8000-000000000012");

        [Fact]
        public void A_case_write_reconciles_that_case()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_outcomecase", PrimaryEntityId = CaseId };
            Assert.Equal(CaseId, CaseAccessPlugin.CaseIdFor(context, new FakeOrganizationService()));
        }

        [Fact]
        public void A_review_write_reconciles_its_case_from_the_target()
        {
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_reviewinstance", PrimaryEntityId = ReviewId };
            context.InputParameters["Target"] = new Entity("al_reviewinstance", ReviewId)
            {
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
            };
            Assert.Equal(CaseId, CaseAccessPlugin.CaseIdFor(context, new FakeOrganizationService()));
        }

        [Fact]
        public void A_review_update_without_the_case_on_it_reads_the_case_from_the_row()
        {
            // An update carries only the columns that changed, so the lookup is usually absent.
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_reviewinstance", PrimaryEntityId = ReviewId };
            context.InputParameters["Target"] = new Entity("al_reviewinstance", ReviewId);

            Assert.Equal(CaseId, CaseAccessPlugin.CaseIdFor(context, svc));
        }

        [Fact]
        public void A_review_with_no_case_reconciles_nothing()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewinstance", ReviewId);
            var context = new FakePluginExecutionContext { PrimaryEntityName = "al_reviewinstance", PrimaryEntityId = ReviewId };
            context.InputParameters["Target"] = new Entity("al_reviewinstance", ReviewId);

            Assert.Null(CaseAccessPlugin.CaseIdFor(context, svc));
        }
    }
}
