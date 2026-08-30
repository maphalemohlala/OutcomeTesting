using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-057 on a real case row. CaseLifecycle decides whether a transition is legal;
    /// CaseTransitions is what actually moves the case, and it is the single enforcement
    /// point for both SubmitReview and UpdateCaseDetails. These run it against a fake
    /// IOrganizationService, which is the first time anything behind a service call in this
    /// assembly has been covered.
    /// </summary>
    public class CaseTransitionsTests
    {
        private const string CaseEntity = "al_outcomecase";
        private const string CaseStatus = "al_casestatus";

        private static readonly Guid CaseId = new Guid("11111111-1111-4111-8111-111111111111");

        private static FakeOrganizationService WithCaseAt(int? status)
        {
            var service = new FakeOrganizationService();
            if (status.HasValue)
            {
                service.Seed(CaseEntity, CaseId, CaseStatus, new OptionSetValue(status.Value));
            }
            else
            {
                service.Seed(CaseEntity, CaseId);
            }

            return service;
        }

        private static int? StatusOf(FakeOrganizationService service)
        {
            var value = service.Row(CaseEntity, CaseId).GetAttributeValue<OptionSetValue>(CaseStatus);
            return value != null ? value.Value : (int?)null;
        }

        [Fact]
        public void Moves_the_case_and_writes_the_new_status()
        {
            var service = WithCaseAt(CaseLifecycle.Assigned);

            CaseTransitions.MoveThrough(service, CaseId, CaseLifecycle.ReviewInProgress);

            Assert.Equal(CaseLifecycle.ReviewInProgress, StatusOf(service));
            Assert.Single(service.Updates);
        }

        [Fact]
        public void A_hop_already_satisfied_writes_nothing()
        {
            // Re-running a submit must not churn the row. The no-op is what makes the
            // sequence safe to replay.
            var service = WithCaseAt(CaseLifecycle.Submitted);

            CaseTransitions.MoveThrough(service, CaseId, CaseLifecycle.Submitted);

            Assert.Empty(service.Updates);
        }

        [Fact]
        public void Refuses_a_transition_the_lifecycle_does_not_describe()
        {
            // The jump AD-057 exists to prevent: Imported straight to Closed would skip
            // validation, allocation, review and remediation, and the export filters on
            // Closed.
            var service = WithCaseAt(CaseLifecycle.Imported);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseTransitions.MoveThrough(service, CaseId, CaseLifecycle.Closed));

            Assert.StartsWith("PRECONDITION: ", ex.Message);
            Assert.Empty(service.Updates);
            Assert.Equal(CaseLifecycle.Imported, StatusOf(service));
        }

        [Fact]
        public void Refuses_to_move_a_closed_case()
        {
            // Closed is terminal; reopening is a privileged correction (OD-007, AD-031).
            var service = WithCaseAt(CaseLifecycle.Closed);

            Assert.Throws<InvalidPluginExecutionException>(
                () => CaseTransitions.MoveThrough(service, CaseId, CaseLifecycle.Submitted));
        }

        [Fact]
        public void Walks_a_sequence_one_hop_at_a_time()
        {
            // The whole point of taking a sequence: each hop is checked against the status
            // the previous hop left behind, so the chain cannot skip a state.
            var service = WithCaseAt(CaseLifecycle.Assigned);

            CaseTransitions.MoveThrough(
                service,
                CaseId,
                OutcomeRules.HopsFor(CaseLifecycle.Assigned, CaseLifecycle.Closed));

            Assert.Equal(
                new[] { CaseLifecycle.ReviewInProgress, CaseLifecycle.Submitted, CaseLifecycle.Closed },
                service.Updates
                    .Select(u => u.GetAttributeValue<OptionSetValue>(CaseStatus).Value)
                    .ToArray());
            Assert.Equal(CaseLifecycle.Closed, StatusOf(service));
        }

        [Fact]
        public void A_refused_hop_leaves_the_earlier_hops_applied_and_stops()
        {
            // Honest about partial application: the hops before the refusal have already
            // been written. The submit runs inside the platform transaction, so the throw is
            // what rolls them back - not the helper.
            var service = WithCaseAt(CaseLifecycle.Assigned);

            Assert.Throws<InvalidPluginExecutionException>(
                () => CaseTransitions.MoveThrough(
                    service,
                    CaseId,
                    new[] { CaseLifecycle.ReviewInProgress, CaseLifecycle.Closed }));

            Assert.Equal(CaseLifecycle.ReviewInProgress, StatusOf(service));
            Assert.Single(service.Updates);
        }

        [Fact]
        public void Reports_the_current_status_and_null_when_it_was_never_set()
        {
            Assert.Equal(
                CaseLifecycle.Queued,
                CaseTransitions.CurrentStatus(WithCaseAt(CaseLifecycle.Queued), CaseId));

            Assert.Null(CaseTransitions.CurrentStatus(WithCaseAt(null), CaseId));
        }

        [Fact]
        public void A_tax_handoff_reaches_the_queue_without_passing_through_submitted()
        {
            // BR-004: the case is not submitted when only its Tax review is. Driven through
            // the same HopsFor the plug-in uses.
            var service = WithCaseAt(CaseLifecycle.ReviewInProgress);

            CaseTransitions.MoveThrough(
                service,
                CaseId,
                OutcomeRules.HopsFor(CaseLifecycle.ReviewInProgress, CaseLifecycle.Queued));

            Assert.DoesNotContain(
                CaseLifecycle.Submitted,
                service.Updates.Select(u => u.GetAttributeValue<OptionSetValue>(CaseStatus).Value));
            Assert.Equal(CaseLifecycle.Queued, StatusOf(service));
        }

        [Fact]
        public void EnsureAllowed_refuses_without_touching_the_row()
        {
            // The guard UpdateCaseDetails uses: it validates a caller-supplied status before
            // the update is applied, so nothing is written either way.
            Assert.Throws<InvalidPluginExecutionException>(
                () => CaseTransitions.EnsureAllowed(CaseLifecycle.Imported, CaseLifecycle.Closed));

            CaseTransitions.EnsureAllowed(CaseLifecycle.Assigned, CaseLifecycle.ReviewInProgress);
        }
    }
}
