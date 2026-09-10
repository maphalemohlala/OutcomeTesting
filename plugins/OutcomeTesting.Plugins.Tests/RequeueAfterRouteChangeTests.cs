using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// OD-048: an edit that changes a case's route can leave it owing a check nobody can
    /// pick up, because the case is past the queue and only a claim creates a review
    /// instance. The case goes back to the queue for that check, and the assignment it was
    /// holding is released.
    /// </summary>
    public class RequeueAfterRouteChangeTests
    {
        private static readonly Guid CaseId = Guid.Parse("abcd0001-2222-4333-8444-555555555555");
        private static readonly Guid TaxAqsRouteId = Guid.Parse("abcd0002-2222-4333-8444-555555555555");
        private static readonly Guid AqsRouteId = Guid.Parse("abcd0003-2222-4333-8444-555555555555");
        private static readonly Guid AssignmentId = Guid.Parse("abcd0004-2222-4333-8444-555555555555");

        private static FakeOrganizationService World(int caseStatus)
        {
            var svc = new FakeOrganizationService();

            svc.Seed("al_reviewroute", TaxAqsRouteId, "al_requirestaxreview", true, "al_requiresaqsreview", true);
            svc.Seed("al_reviewroute", AqsRouteId, "al_requirestaxreview", false, "al_requiresaqsreview", true);

            svc.Seed(
                "al_outcomecase", CaseId,
                "al_casestatus", new OptionSetValue(caseStatus),
                "al_reviewrouteid", new EntityReference("al_reviewroute", AqsRouteId));

            svc.Seed(
                "al_caseassignment", AssignmentId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_isactive", true);

            return svc;
        }

        private static Entity OpenReview(FakeOrganizationService svc, int reviewType)
        {
            return svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "statecode", new OptionSetValue(0));
        }

        private static Entity SubmittedReview(FakeOrganizationService svc, int reviewType)
        {
            return svc.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_submittedon", new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));
        }

        /// <summary>The `before` image the command holds, and the update carrying the new route.</summary>
        private static Entity Before(FakeOrganizationService svc)
        {
            return svc.Row("al_outcomecase", CaseId);
        }

        private static Entity RouteChangedTo(Guid routeId)
        {
            return new Entity("al_outcomecase", CaseId)
            {
                ["al_reviewrouteid"] = new EntityReference("al_reviewroute", routeId),
            };
        }

        private static int Status(FakeOrganizationService svc)
        {
            return svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_casestatus").Value;
        }

        [Fact]
        public void Returns_an_assigned_case_to_the_queue_when_the_new_route_owes_an_unopened_check()
        {
            // The IO-SEED-AQS-04 shape: claimed as AQS-only, then edited to require Tax.
            var svc = World(CaseLifecycle.Assigned);
            OpenReview(svc, ResponseRules.ReviewTypeAqs);
            var changes = new List<string>();

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), RouteChangedTo(TaxAqsRouteId), changes);

            Assert.True(moved);
            Assert.Equal(CaseLifecycle.Queued, Status(svc));
            Assert.Contains(changes, line => line.Contains("Tax") && line.Contains("OD-048"));
        }

        [Fact]
        public void Releases_the_assignment_it_was_holding()
        {
            // A case in the queue carrying a live assignment is the inconsistency the
            // Tax-to-AQS handoff already avoids. The row survives (AD-037).
            var svc = World(CaseLifecycle.Assigned);
            OpenReview(svc, ResponseRules.ReviewTypeAqs);

            UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), RouteChangedTo(TaxAqsRouteId), new List<string>());

            Assert.False(svc.Row("al_caseassignment", AssignmentId).GetAttributeValue<bool>("al_isactive"));
            Assert.NotNull(svc.Row("al_caseassignment", AssignmentId));
        }

        [Fact]
        public void Moves_a_case_that_was_already_being_reviewed()
        {
            var svc = World(CaseLifecycle.ReviewInProgress);
            OpenReview(svc, ResponseRules.ReviewTypeAqs);

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), RouteChangedTo(TaxAqsRouteId), new List<string>());

            Assert.True(moved);
            Assert.Equal(CaseLifecycle.Queued, Status(svc));
        }

        [Fact]
        public void Leaves_the_checker_alone_when_the_check_now_due_is_already_open()
        {
            // The route changed but what comes next did not: the Tax check is open and
            // being worked, so pulling the case back would take live work off a desk.
            var svc = World(CaseLifecycle.Assigned);
            OpenReview(svc, ResponseRules.ReviewTypeTax);

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), RouteChangedTo(TaxAqsRouteId), new List<string>());

            Assert.False(moved);
            Assert.Equal(CaseLifecycle.Assigned, Status(svc));
            Assert.True(svc.Row("al_caseassignment", AssignmentId).GetAttributeValue<bool>("al_isactive"));
        }

        [Fact]
        public void Leaves_a_case_alone_when_the_route_did_not_change()
        {
            var svc = World(CaseLifecycle.Assigned);
            OpenReview(svc, ResponseRules.ReviewTypeAqs);

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), new Entity("al_outcomecase", CaseId) { ["al_clientname"] = "New name" }, new List<string>());

            Assert.False(moved);
            Assert.Equal(CaseLifecycle.Assigned, Status(svc));
        }

        [Fact]
        public void Leaves_a_case_alone_when_the_caller_set_the_status_themselves()
        {
            // EnsureLifecycleTransition has already passed that move; a second one on top
            // would make the history read as two decisions.
            var svc = World(CaseLifecycle.Assigned);
            OpenReview(svc, ResponseRules.ReviewTypeAqs);

            var update = RouteChangedTo(TaxAqsRouteId);
            update["al_casestatus"] = new OptionSetValue(CaseLifecycle.ReviewInProgress);

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(svc, Before(svc), update, new List<string>());

            Assert.False(moved);
        }

        [Theory]
        [InlineData(CaseLifecycle.Queued)]
        [InlineData(CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.Closed)]
        public void Leaves_a_case_outside_the_two_working_states_where_it_is(int status)
        {
            var svc = World(status);
            OpenReview(svc, ResponseRules.ReviewTypeAqs);

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), RouteChangedTo(TaxAqsRouteId), new List<string>());

            Assert.False(moved);
            Assert.Equal(status, Status(svc));
        }

        [Fact]
        public void Leaves_a_case_alone_when_the_new_route_owes_nothing_further()
        {
            // Tax submitted and AQS submitted: the route requires both and both are in, so
            // there is no check to queue for.
            var svc = World(CaseLifecycle.Assigned);
            SubmittedReview(svc, ResponseRules.ReviewTypeTax);
            SubmittedReview(svc, ResponseRules.ReviewTypeAqs);

            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), RouteChangedTo(TaxAqsRouteId), new List<string>());

            Assert.False(moved);
            Assert.Equal(CaseLifecycle.Assigned, Status(svc));
        }

        [Fact]
        public void A_submitted_tax_check_means_the_aqs_leg_is_what_is_due()
        {
            // Tax is in but the AQS instance has never been opened, so the case owes AQS
            // and belongs back in the queue for it.
            var svc = World(CaseLifecycle.Assigned);
            SubmittedReview(svc, ResponseRules.ReviewTypeTax);

            var changes = new List<string>();
            var moved = UpdateCaseDetailsPlugin.RequeueAfterRouteChange(
                svc, Before(svc), RouteChangedTo(TaxAqsRouteId), changes);

            Assert.True(moved);
            Assert.Contains(changes, line => line.Contains("AQS"));
        }
    }
}
