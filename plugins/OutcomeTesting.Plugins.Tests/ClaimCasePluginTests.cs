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

        private static void SeedSubmittedReview(FakeOrganizationService svc, int reviewType)
        {
            svc.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_submittedon", new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));
        }

        [Fact]
        public void Opens_the_aqs_check_once_the_tax_check_has_been_submitted()
        {
            // The BR-004 handoff. A passed Tax submit returns the case to Queued with its Tax
            // review submitted and no AQS instance yet. Reading the route alone would open Tax
            // again, and the deterministic al_reviewinstancecode then collides on the
            // alternate key - so the AQS leg could never be picked up from the queue.
            var svc = Route(true, true);
            SeedSubmittedReview(svc, ResponseRules.ReviewTypeTax);

            Assert.Equal(
                ResponseRules.ReviewTypeAqs,
                ClaimCasePlugin.NextDiscipline(svc, Case(CaseLifecycle.Queued, RouteId)));
        }

        [Fact]
        public void Refuses_a_case_whose_every_required_check_has_been_submitted()
        {
            var svc = Route(true, true);
            SeedSubmittedReview(svc, ResponseRules.ReviewTypeTax);
            SeedSubmittedReview(svc, ResponseRules.ReviewTypeAqs);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.NextDiscipline(svc, Case(CaseLifecycle.Queued, RouteId)));

            Assert.Contains("already been submitted", ex.Message);
        }

        [Fact]
        public void An_inactive_submitted_review_does_not_count_as_the_check_being_done()
        {
            var svc = Route(true, true);
            svc.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_submittedon", new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(1));

            Assert.Equal(
                ResponseRules.ReviewTypeTax,
                ClaimCasePlugin.NextDiscipline(svc, Case(CaseLifecycle.Queued, RouteId)));
        }

        private static readonly Guid ContactId = Guid.Parse("eeeeeeee-5555-4555-8555-555555555555");

        private static FakeOrganizationService Holding(params string[] roleNames)
        {
            var svc = new FakeOrganizationService();
            var rows = new System.Collections.Generic.List<Entity>();
            foreach (var name in roleNames)
            {
                var row = new Entity("contact", ContactId);
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
            return svc;
        }

        [Fact]
        public void A_tax_reviewer_may_pick_up_a_tax_check()
        {
            ClaimCasePlugin.EnsureDisciplineRole(Holding(WebRoleRegistry.TaxReviewerRole), ContactId, ResponseRules.ReviewTypeTax);
        }

        [Fact]
        public void An_aqs_reviewer_may_not_pick_up_a_tax_check()
        {
            // The queue pages filter by discipline, but a page is presentation: both reviewer
            // roles are bound to the same claim permission.
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.EnsureDisciplineRole(Holding(WebRoleRegistry.AqsReviewerRole), ContactId, ResponseRules.ReviewTypeTax));

            Assert.Contains(WebRoleRegistry.TaxReviewerRole, ex.Message);
        }

        [Fact]
        public void A_checker_holding_both_roles_may_pick_up_either()
        {
            ClaimCasePlugin.EnsureDisciplineRole(
                Holding(WebRoleRegistry.TaxReviewerRole, WebRoleRegistry.AqsReviewerRole), ContactId, ResponseRules.ReviewTypeAqs);
        }

        [Fact]
        public void A_contact_with_no_reviewer_role_is_refused()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => ClaimCasePlugin.EnsureDisciplineRole(Holding("AL Portal - Planner"), ContactId, ResponseRules.ReviewTypeAqs));
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
