using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The shape IO-SEED-AQS-04 is in on DEV: a case seeded AQS-only, allocated so that an
    /// AQS review instance was opened at sequence 2, then edited so that a Tax check was
    /// required. DeriveRoute re-derived the route to Tax-then-AQS, but nothing reconciled
    /// the review instance already opened under the old route — so the case owes a Tax
    /// check no instance exists for, and its Review progress shows AQS alone (BR-004).
    /// </summary>
    public class RouteChangeOrphanTests
    {
        private static readonly Guid CaseId = Guid.Parse("0a0a0a0a-1b1b-4c4c-8d8d-0e0e0e0e0e0e");
        private static readonly Guid RouteId = Guid.Parse("1a1a1a1a-2b2b-4c4c-8d8d-1e1e1e1e1e1e");
        private static readonly Guid AqsReviewId = Guid.Parse("2a2a2a2a-3b3b-4c4c-8d8d-2e2e2e2e2e2e");
        private static readonly Guid VersionId = Guid.Parse("3a3a3a3a-4b4b-4c4c-8d8d-3e3e3e3e3e3e");

        private static FakeOrganizationService ReroutedCase()
        {
            var svc = new FakeOrganizationService();

            svc.Seed(
                "al_reviewroute", RouteId,
                "al_requirestaxreview", true,
                "al_requiresaqsreview", true);

            svc.Seed(
                "al_outcomecase", CaseId,
                "al_reviewrouteid", new EntityReference("al_reviewroute", RouteId));

            // The orphan: opened under the old AQS-only route, still open, at sequence 2.
            svc.Seed(
                "al_reviewinstance", AqsReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "al_sequence", 2,
                "al_checklistversionid", new EntityReference("al_checklistversion", VersionId),
                "statecode", new OptionSetValue(0));

            return svc;
        }

        [Fact]
        public void The_aqs_submit_is_refused_when_the_route_requires_a_tax_check_that_was_never_created()
        {
            var svc = ReroutedCase();

            var error = Assert.Throws<InvalidPluginExecutionException>(() =>
                SubmitReviewPlugin.Submit(
                    svc,
                    AqsReviewId,
                    "orphan-test-key",
                    null,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    requireCallerOwnsReview: false,
                    details: "reproduction"));

            Assert.Contains("BR-004", error.Message);
        }
    }
}
