using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-093: a routed case at Imported or Ready for Allocation belongs in the queue.
    /// Before this rule nothing moved a case into Queued except a hand-back from a Tax
    /// check or a sign-off, so the AD-076 self-service queue never offered fresh work.
    /// </summary>
    public class CaseQueueingTests
    {
        private const string CaseEntity = "al_outcomecase";
        private const string CaseStatus = "al_casestatus";
        private static readonly Guid CaseId = new Guid("11111111-1111-4111-8111-111111111111");

        private static FakeOrganizationService WithCaseAt(int status)
        {
            var service = new FakeOrganizationService();
            service.Seed(CaseEntity, CaseId, CaseStatus, new OptionSetValue(status));
            return service;
        }

        private static int? StatusOf(FakeOrganizationService service)
        {
            var value = service.Row(CaseEntity, CaseId).GetAttributeValue<OptionSetValue>(CaseStatus);
            return value != null ? value.Value : (int?)null;
        }

        [Theory]
        [InlineData(CaseLifecycle.Imported, true, true)]
        [InlineData(CaseLifecycle.ReadyForAllocation, true, true)]
        [InlineData(CaseLifecycle.Imported, false, false)]
        [InlineData(CaseLifecycle.ReadyForAllocation, false, false)]
        [InlineData(CaseLifecycle.ValidationFailed, true, false)]
        [InlineData(CaseLifecycle.Queued, true, false)]
        [InlineData(CaseLifecycle.Assigned, true, false)]
        [InlineData(CaseLifecycle.Closed, true, false)]
        public void Queues_only_a_routed_case_that_has_not_reached_the_queue(int status, bool hasRoute, bool expected)
        {
            Assert.Equal(expected, CaseQueueing.ShouldQueue(status, hasRoute));
        }

        [Fact]
        public void A_case_with_no_status_is_not_queued()
        {
            Assert.False(CaseQueueing.ShouldQueue(null, true));
        }

        [Fact]
        public void Walks_imported_through_ready_for_allocation()
        {
            // Skipping Ready for Allocation is exactly what AD-057 forbids.
            Assert.Equal(
                new[] { CaseLifecycle.ReadyForAllocation, CaseLifecycle.Queued },
                CaseQueueing.HopsToQueue(CaseLifecycle.Imported));
            Assert.Equal(new[] { CaseLifecycle.Queued }, CaseQueueing.HopsToQueue(CaseLifecycle.ReadyForAllocation));
            Assert.Empty(CaseQueueing.HopsToQueue(CaseLifecycle.Queued));
            Assert.Empty(CaseQueueing.HopsToQueue(null));
        }

        [Fact]
        public void Moves_a_routed_imported_case_to_queued_and_records_the_hop()
        {
            var service = WithCaseAt(CaseLifecycle.Imported);
            var changes = new List<string>();

            var moved = CaseQueueing.QueueIfRouted(service, CaseId, CaseLifecycle.Imported, true, changes);

            Assert.True(moved);
            Assert.Equal(CaseLifecycle.Queued, StatusOf(service));
            Assert.Equal(2, service.Updates.Count);
            Assert.Equal("Status Imported -> Queued (queued automatically: route set, AD-093)", Assert.Single(changes));
        }

        [Fact]
        public void Leaves_an_unrouted_case_where_it_is()
        {
            var service = WithCaseAt(CaseLifecycle.ReadyForAllocation);
            var changes = new List<string>();

            var moved = CaseQueueing.QueueIfRouted(service, CaseId, CaseLifecycle.ReadyForAllocation, false, changes);

            Assert.False(moved);
            Assert.Equal(CaseLifecycle.ReadyForAllocation, StatusOf(service));
            Assert.Empty(service.Updates);
            Assert.Empty(changes);
        }

        [Fact]
        public void Leaves_a_case_already_past_the_queue_alone()
        {
            var service = WithCaseAt(CaseLifecycle.Assigned);
            var changes = new List<string>();

            Assert.False(CaseQueueing.QueueIfRouted(service, CaseId, CaseLifecycle.Assigned, true, changes));
            Assert.Empty(service.Updates);
        }
    }
}
