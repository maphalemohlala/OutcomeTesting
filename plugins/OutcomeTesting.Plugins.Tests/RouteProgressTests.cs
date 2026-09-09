using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The two questions the sign-off and the AQS submit ask about a case's progress along
    /// its route (BR-004, OD-027, OD-038): is an AQS review still owed, and has the
    /// remediation a Tax check raised been approved.
    /// </summary>
    public class RouteProgressTests
    {
        private static readonly Guid CaseId = Guid.Parse("11111111-2222-4333-8444-555555555555");
        private static readonly Guid RouteId = Guid.Parse("66666666-7777-4888-8999-aaaaaaaaaaaa");
        private static readonly Guid TaxReviewId = Guid.Parse("bbbbbbbb-cccc-4ddd-8eee-ffffffffffff");

        private static FakeOrganizationService Case(bool? routeTax, bool? routeAqs)
        {
            var svc = new FakeOrganizationService();
            var outcomeCase = svc.Seed("al_outcomecase", CaseId);
            if (routeTax.HasValue)
            {
                svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", routeTax.Value, "al_requiresaqsreview", routeAqs ?? false);
                outcomeCase["al_reviewrouteid"] = new EntityReference("al_reviewroute", RouteId);
            }

            return svc;
        }

        private static Guid Review(FakeOrganizationService svc, int reviewType, bool submitted, Guid? id = null)
        {
            var review = svc.Seed(
                "al_reviewinstance",
                id ?? Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(submitted ? ResponseRules.StatusSubmitted : ResponseRules.StatusInProgress),
                "statecode", new OptionSetValue(0));
            if (submitted)
            {
                review["al_submittedon"] = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);
            }

            return review.Id;
        }

        [Fact]
        public void Aqs_is_owed_when_the_route_requires_it_and_none_is_submitted()
        {
            var svc = Case(routeTax: true, routeAqs: true);
            Review(svc, ResponseRules.ReviewTypeTax, submitted: true);

            Assert.True(SubmitReviewPlugin.AqsStillOwed(svc, CaseId));
        }

        [Fact]
        public void Aqs_is_not_owed_once_its_review_is_submitted()
        {
            // The case that used to read wrong: with the AQS review already in, "no
            // unsubmitted AQS instance" was taken as "still to come", which at sign-off would
            // have sent a finished check back to the queue.
            var svc = Case(routeTax: true, routeAqs: true);
            Review(svc, ResponseRules.ReviewTypeTax, submitted: true);
            Review(svc, ResponseRules.ReviewTypeAqs, submitted: true);

            Assert.False(SubmitReviewPlugin.AqsStillOwed(svc, CaseId));
        }

        [Fact]
        public void Aqs_is_not_owed_on_a_tax_only_route()
        {
            var svc = Case(routeTax: true, routeAqs: false);

            Assert.False(SubmitReviewPlugin.AqsStillOwed(svc, CaseId));
        }

        [Fact]
        public void Without_a_route_an_open_aqs_instance_is_the_only_evidence()
        {
            var svc = Case(routeTax: null, routeAqs: null);
            Assert.False(SubmitReviewPlugin.AqsStillOwed(svc, CaseId));

            Review(svc, ResponseRules.ReviewTypeAqs, submitted: false);
            Assert.True(SubmitReviewPlugin.AqsStillOwed(svc, CaseId));
        }

        private static Guid Action(FakeOrganizationService svc, Guid reviewId)
        {
            return svc.Seed(
                "al_remediationaction",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted)).Id;
        }

        private static void Signoff(FakeOrganizationService svc, Guid actionId, int decision, bool active = true)
        {
            svc.Seed(
                "al_signoff",
                Guid.NewGuid(),
                "al_remediationactionid", new EntityReference("al_remediationaction", actionId),
                "al_signoffdecision", new OptionSetValue(decision),
                "statecode", new OptionSetValue(active ? 0 : 1));
        }

        [Fact]
        public void A_tax_remediation_counts_as_approved_only_with_an_approved_signoff()
        {
            var svc = Case(routeTax: true, routeAqs: true);
            Review(svc, ResponseRules.ReviewTypeTax, submitted: true, id: TaxReviewId);
            var action = Action(svc, TaxReviewId);

            Assert.False(SubmitReviewPlugin.RemediationApproved(svc, TaxReviewId));

            Signoff(svc, action, SignoffProgressPlugin.DecisionRejectedValue);
            Assert.False(SubmitReviewPlugin.RemediationApproved(svc, TaxReviewId));

            Signoff(svc, action, SignoffProgressPlugin.DecisionApprovedValue);
            Assert.True(SubmitReviewPlugin.RemediationApproved(svc, TaxReviewId));
        }

        [Fact]
        public void A_review_that_raised_no_action_is_not_approved()
        {
            var svc = Case(routeTax: true, routeAqs: true);
            Review(svc, ResponseRules.ReviewTypeTax, submitted: true, id: TaxReviewId);

            Assert.False(SubmitReviewPlugin.RemediationApproved(svc, TaxReviewId));
        }

        [Fact]
        public void One_approved_action_of_several_does_not_open_the_aqs_gate()
        {
            // A review raises one action per thing the checker marked down (2026-09-10), so
            // approving the first would otherwise let the AQS review start with the rest of
            // the file still unremediated.
            var svc = Case(routeTax: true, routeAqs: true);
            Review(svc, ResponseRules.ReviewTypeTax, submitted: true, id: TaxReviewId);
            var first = Action(svc, TaxReviewId);
            var second = Action(svc, TaxReviewId);

            Signoff(svc, first, SignoffProgressPlugin.DecisionApprovedValue);
            Assert.False(SubmitReviewPlugin.RemediationApproved(svc, TaxReviewId));

            Signoff(svc, second, SignoffProgressPlugin.DecisionApprovedValue);
            Assert.True(SubmitReviewPlugin.RemediationApproved(svc, TaxReviewId));
        }

        [Fact]
        public void An_inactive_signoff_does_not_count()
        {
            var svc = Case(routeTax: true, routeAqs: true);
            Review(svc, ResponseRules.ReviewTypeTax, submitted: true, id: TaxReviewId);
            Signoff(svc, Action(svc, TaxReviewId), SignoffProgressPlugin.DecisionApprovedValue, active: false);

            Assert.False(SubmitReviewPlugin.RemediationApproved(svc, TaxReviewId));
        }
    }
}
