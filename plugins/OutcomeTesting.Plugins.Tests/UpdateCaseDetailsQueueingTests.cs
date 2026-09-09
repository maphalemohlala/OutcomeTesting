using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-093 applied by the case-edit command: once a case has a route and is still short of
    /// the queue, saving it queues it. The rule reads the state the save leaves behind, so
    /// it does not matter whether the route arrived in this call, was derived in this call,
    /// or was already there. A status the caller set in the same call is theirs to set.
    /// </summary>
    public class UpdateCaseDetailsQueueingTests
    {
        private const string CaseEntity = "al_outcomecase";
        private const string StatusAttr = "al_casestatus";
        private const string RouteAttr = "al_reviewrouteid";
        private static readonly Guid CaseId = new Guid("11111111-1111-4111-8111-111111111111");
        private static readonly Guid AqsOnly = new Guid("22222222-2222-4222-8222-222222222222");

        private static FakeOrganizationService WithCase(int status, Guid? route)
        {
            var service = new FakeOrganizationService();
            var row = service.Seed(CaseEntity, CaseId, StatusAttr, new OptionSetValue(status));
            if (route.HasValue)
            {
                row[RouteAttr] = new EntityReference("al_reviewroute", route.Value);
            }

            return service;
        }

        private static Entity Before(FakeOrganizationService service)
        {
            return service.Row(CaseEntity, CaseId);
        }

        private static int StatusOf(FakeOrganizationService service)
        {
            return service.Row(CaseEntity, CaseId).GetAttributeValue<OptionSetValue>(StatusAttr).Value;
        }

        [Fact]
        public void Setting_a_route_on_an_imported_case_queues_it()
        {
            var service = WithCase(CaseLifecycle.Imported, null);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update[RouteAttr] = new EntityReference("al_reviewroute", AqsOnly);
            var changes = new List<string>();

            var moved = UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes);

            Assert.True(moved);
            Assert.Equal(CaseLifecycle.Queued, StatusOf(service));
            Assert.Contains(changes, c => c.StartsWith("Status Imported -> Queued", StringComparison.Ordinal));
        }

        [Fact]
        public void Editing_any_detail_of_a_routed_case_at_ready_for_allocation_queues_it()
        {
            // The rule is about the state, not the field that changed.
            var service = WithCase(CaseLifecycle.ReadyForAllocation, AqsOnly);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update["al_priority"] = new OptionSetValue(120910540);
            var changes = new List<string>();

            Assert.True(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Equal(CaseLifecycle.Queued, StatusOf(service));
        }

        [Fact]
        public void A_status_the_caller_set_is_left_alone()
        {
            // The caller moved Imported -> Ready for Allocation themselves; the rule does not
            // pile a second move on top of a deliberate one in the same call.
            var service = WithCase(CaseLifecycle.ReadyForAllocation, AqsOnly);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update[StatusAttr] = new OptionSetValue(CaseLifecycle.ReadyForAllocation);
            var changes = new List<string>();

            Assert.False(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Empty(service.Updates);
            Assert.Empty(changes);
        }

        [Fact]
        public void An_unrouted_case_is_not_queued()
        {
            var service = WithCase(CaseLifecycle.ReadyForAllocation, null);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update["al_priority"] = new OptionSetValue(120910540);
            var changes = new List<string>();

            Assert.False(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Equal(CaseLifecycle.ReadyForAllocation, StatusOf(service));
        }

        [Fact]
        public void A_case_past_the_queue_is_not_touched()
        {
            var service = WithCase(CaseLifecycle.Assigned, AqsOnly);
            var before = Before(service);
            var update = new Entity(CaseEntity, CaseId);
            update["al_priority"] = new OptionSetValue(120910540);
            var changes = new List<string>();

            Assert.False(UpdateCaseDetailsPlugin.QueueAfterEdit(service, before, update, changes));
            Assert.Empty(service.Updates);
        }
    }
}
