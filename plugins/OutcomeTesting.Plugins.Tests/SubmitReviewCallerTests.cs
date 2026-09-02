using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The owner gate on <see cref="SubmitReviewPlugin.Submit"/>.
    ///
    /// One submission implementation now serves two entry points that authorise
    /// differently: the Custom API knows who the caller is, and the portal path does not,
    /// because Power Pages Web API writes arrive under the site's application user
    /// (AD-053). The flag that expresses that is the only thing separating "the assigned
    /// checker submitted this" from "anyone with write privilege did", so it is worth
    /// holding in place with a test rather than a comment.
    /// </summary>
    public class SubmitReviewCallerTests
    {
        private static readonly Guid ReviewId = Guid.Parse("cccccccc-1111-4111-8111-111111111111");
        private static readonly Guid OwnerId = Guid.Parse("dddddddd-2222-4222-8222-222222222222");
        private static readonly Guid SomeoneElse = Guid.Parse("eeeeeeee-3333-4333-8333-333333333333");

        private const int StatusAssigned = 120910210;
        private const int ReviewTypeTax = 120910201;

        /// <summary>
        /// A review owned by someone else, with no checklist version. The missing version is
        /// deliberate: it is the first refusal *after* the owner gate, so a test that reaches
        /// it has proved the gate was skipped rather than merely that nothing threw.
        /// </summary>
        private static FakeOrganizationService Review()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_reviewinstance",
                ReviewId,
                "al_reviewstatus", new OptionSetValue(StatusAssigned),
                "al_reviewtype", new OptionSetValue(ReviewTypeTax),
                "ownerid", new EntityReference("systemuser", OwnerId),
                "al_sequence", 1);
            return svc;
        }

        private static InvalidPluginExecutionException SubmitExpectingFailure(bool requireCallerOwnsReview)
        {
            return Assert.Throws<InvalidPluginExecutionException>(() =>
                SubmitReviewPlugin.Submit(
                    Review(),
                    ReviewId,
                    "key-" + Guid.NewGuid().ToString("N"),
                    expectedRowVersion: null,
                    actorId: SomeoneElse,
                    correlationId: Guid.NewGuid(),
                    requireCallerOwnsReview: requireCallerOwnsReview,
                    details: null));
        }

        [Fact]
        public void Custom_api_path_refuses_a_caller_who_does_not_own_the_review()
        {
            var ex = SubmitExpectingFailure(requireCallerOwnsReview: true);

            Assert.Contains("UNAUTHORIZED:", ex.Message);
            Assert.Contains("assigned to this review", ex.Message);
        }

        [Fact]
        public void Portal_path_skips_the_owner_gate_but_still_runs_the_business_rules()
        {
            var ex = SubmitExpectingFailure(requireCallerOwnsReview: false);

            Assert.DoesNotContain("UNAUTHORIZED:", ex.Message);
            Assert.Contains("PRECONDITION:", ex.Message);
            Assert.Contains("checklist version", ex.Message);
        }

        [Fact]
        public void Owner_path_still_accepts_the_owner_themselves()
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                SubmitReviewPlugin.Submit(
                    Review(),
                    ReviewId,
                    "key-" + Guid.NewGuid().ToString("N"),
                    expectedRowVersion: null,
                    actorId: OwnerId,
                    correlationId: Guid.NewGuid(),
                    requireCallerOwnsReview: true,
                    details: null));

            Assert.DoesNotContain("UNAUTHORIZED:", ex.Message);
            Assert.Contains("checklist version", ex.Message);
        }

        [Fact]
        public void A_review_with_no_owner_is_refused_on_the_owner_path()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_reviewinstance",
                ReviewId,
                "al_reviewstatus", new OptionSetValue(StatusAssigned),
                "al_reviewtype", new OptionSetValue(ReviewTypeTax));

            var ex = Assert.Throws<InvalidPluginExecutionException>(() =>
                SubmitReviewPlugin.Submit(
                    svc,
                    ReviewId,
                    "key-" + Guid.NewGuid().ToString("N"),
                    expectedRowVersion: null,
                    actorId: OwnerId,
                    correlationId: Guid.NewGuid(),
                    requireCallerOwnsReview: true,
                    details: null));

            Assert.Contains("UNAUTHORIZED:", ex.Message);
            Assert.Contains("no owner", ex.Message);
        }
    }
}
