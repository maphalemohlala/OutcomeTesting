using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The two rules that decide whether a portal self-claim is allowed and what it is a
    /// claim for (AD-076). Both are the only thing standing between "pick up free work"
    /// and "take work someone else already holds", because authorization on this path
    /// cannot be a caller check — a Power Pages write reaches Dataverse as the site's
    /// application user (AD-053).
    /// </summary>
    public class ClaimCasePluginTests
    {
        private static readonly Guid CaseId = Guid.Parse("cccccccc-3333-4333-8333-333333333333");
        private static readonly Guid RouteId = Guid.Parse("dddddddd-4444-4444-8444-444444444444");

        private static Entity Case(int? status, Guid? route)
        {
            var outcomeCase = new Entity("al_outcomecase", CaseId);
            if (status.HasValue)
            {
                outcomeCase["al_casestatus"] = new OptionSetValue(status.Value);
            }

            if (route.HasValue)
            {
                outcomeCase["al_reviewrouteid"] = new EntityReference("al_reviewroute", route.Value);
            }

            return outcomeCase;
        }

        private static FakeOrganizationService Route(bool tax, bool aqs)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", tax, "al_requiresaqsreview", aqs);
            return svc;
        }

        [Fact]
        public void Allows_a_queued_case_to_be_picked_up()
        {
            ClaimCasePlugin.EnsureQueued(Case(CaseLifecycle.Queued, RouteId));
        }

        [Theory]
        [InlineData(CaseLifecycle.Imported)]
        [InlineData(CaseLifecycle.ReadyForAllocation)]
        [InlineData(CaseLifecycle.ReviewInProgress)]
        [InlineData(CaseLifecycle.Closed)]
        public void Refuses_a_case_that_is_not_in_the_queue(int status)
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.EnsureQueued(Case(status, RouteId)));

            Assert.Contains("PRECONDITION:", ex.Message);
            Assert.Contains(CaseLifecycle.NameOf(status), ex.Message);
        }

        [Fact]
        public void Refuses_a_case_that_is_already_assigned()
        {
            // CaseLifecycle allows Assigned -> Assigned, because re-stating a status is not
            // a transition. Without this rule the second claimant would pass the lifecycle
            // check and take a case off the first.
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.EnsureQueued(Case(CaseLifecycle.Assigned, RouteId)));

            Assert.Contains("already", ex.Message);
        }

        [Fact]
        public void Refuses_a_case_with_no_status_recorded()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.EnsureQueued(Case(null, RouteId)));
        }

        [Fact]
        public void Opens_the_tax_check_first_on_a_tax_then_aqs_route()
        {
            // BR-004: Tax precedes AQS, and the route decides it — not the page the checker
            // happened to be on when they pressed the button.
            Assert.Equal(
                ResponseRules.ReviewTypeTax,
                ClaimCasePlugin.NextDiscipline(Route(true, true), Case(CaseLifecycle.Queued, RouteId)));
        }

        [Fact]
        public void Opens_the_tax_check_on_a_tax_only_route()
        {
            Assert.Equal(
                ResponseRules.ReviewTypeTax,
                ClaimCasePlugin.NextDiscipline(Route(true, false), Case(CaseLifecycle.Queued, RouteId)));
        }

        [Fact]
        public void Opens_the_aqs_check_on_an_aqs_only_route()
        {
            Assert.Equal(
                ResponseRules.ReviewTypeAqs,
                ClaimCasePlugin.NextDiscipline(Route(false, true), Case(CaseLifecycle.Queued, RouteId)));
        }

        [Fact]
        public void Refuses_a_case_carrying_no_route()
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.NextDiscipline(Route(true, true), Case(CaseLifecycle.Queued, null)));

            Assert.Contains("no review route", ex.Message);
        }

        [Fact]
        public void Refuses_a_route_that_requires_neither_discipline()
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.NextDiscipline(Route(false, false), Case(CaseLifecycle.Queued, RouteId)));

            Assert.Contains("nothing to pick up", ex.Message);
        }
    }
}
