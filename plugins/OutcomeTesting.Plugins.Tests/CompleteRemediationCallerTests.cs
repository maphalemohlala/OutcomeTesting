using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The owner gate and the mandatory-response rule on
    /// <see cref="CompleteRemediationPlugin.Complete"/>.
    ///
    /// The response rule matters more than it looks. BR-008 has the T&amp;C Manager attest to
    /// what the adviser did, so an action that reaches them Completed with nothing written
    /// in it is an attestation with nothing to read. Refusing it server-side is what stops
    /// the loop being closed on an empty answer.
    /// </summary>
    public class CompleteRemediationCallerTests
    {
        private static readonly Guid ActionId = Guid.Parse("11111111-aaaa-4aaa-8aaa-111111111111");
        private static readonly Guid OwnerId = Guid.Parse("22222222-bbbb-4bbb-8bbb-222222222222");
        private static readonly Guid SomeoneElse = Guid.Parse("33333333-cccc-4ccc-8ccc-333333333333");

        private const int StatusOpen = 120910600;
        private const int StatusCompleted = 120910602;

        private static FakeOrganizationService Action(string response, int status = StatusOpen)
        {
            var svc = new FakeOrganizationService();
            var row = svc.Seed(
                "al_remediationaction",
                ActionId,
                "al_actionstatus", new OptionSetValue(status),
                "ownerid", new EntityReference("systemuser", OwnerId));

            if (response != null)
            {
                row["al_adviserresponse"] = response;
            }

            return svc;
        }

        private static CompleteRemediationPlugin.CompleteResult Complete(
            FakeOrganizationService svc,
            Guid actorId,
            bool requireCallerOwnsAction)
        {
            return CompleteRemediationPlugin.Complete(
                svc,
                ActionId,
                "key-" + Guid.NewGuid().ToString("N"),
                expectedRowVersion: null,
                actorId: actorId,
                correlationId: Guid.NewGuid(),
                requireCallerOwnsAction: requireCallerOwnsAction,
                details: null);
        }

        [Fact]
        public void Custom_api_path_refuses_a_caller_who_does_not_own_the_action()
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => Complete(Action("Rebuilt the suitability report."), SomeoneElse, true));

            Assert.Contains("UNAUTHORIZED:", ex.Message);
        }

        [Fact]
        public void Portal_path_skips_the_owner_gate()
        {
            var result = Complete(Action("Rebuilt the suitability report."), SomeoneElse, false);

            Assert.Equal("Completed", result.Status);
        }

        [Fact]
        public void A_completion_with_no_recorded_response_is_refused()
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => Complete(Action(null), OwnerId, true));

            Assert.Contains("PRECONDITION:", ex.Message);
            Assert.Contains("Record what you did", ex.Message);
        }

        [Fact]
        public void A_whitespace_only_response_does_not_count_as_an_answer()
        {
            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => Complete(Action("   "), OwnerId, true));

            Assert.Contains("PRECONDITION:", ex.Message);
        }

        [Fact]
        public void The_owner_completes_their_own_action()
        {
            var svc = Action("Rebuilt the suitability report.");

            var result = Complete(svc, OwnerId, true);

            Assert.Equal("Completed", result.Status);
            Assert.False(result.Conflict);
        }

        /// <summary>BR-007: a completed action is not written a second time.</summary>
        [Fact]
        public void An_already_completed_action_is_an_idempotent_success()
        {
            var svc = Action("Rebuilt the suitability report.", StatusCompleted);

            var result = Complete(svc, OwnerId, true);

            Assert.Equal("Completed", result.Status);
            Assert.Empty(svc.Updates);
        }
    }
}
