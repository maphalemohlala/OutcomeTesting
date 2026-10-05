using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Who the letters come from (owner direction 2026-10-02: "tc.outcometesting@ascotlloyd.co.uk
    /// is a shared mailbox - we need to use it for all communications").
    ///
    /// <para>
    /// A shared mailbox has no licence and no sign-in, so it cannot be the account the drain
    /// step runs as. Dataverse sends from one through a QUEUE carrying its address, whose
    /// mailbox is approved and enabled for server-side email. The address is configuration,
    /// set per environment in <c>al_NotificationSenderAddress</c>, because one Exchange mailbox
    /// can be enabled for server-side sync in only one environment at a time.
    /// </para>
    /// </summary>
    public class NotificationSenderTests
    {
        private const string Shared = "tc.outcometesting@ascotlloyd.co.uk";

        private static readonly Guid NotificationId = Guid.Parse("aaaaaaaa-1111-4111-8111-222222222222");
        private static readonly Guid RunAsId = Guid.Parse("bbbbbbbb-3333-4333-8333-444444444444");
        private static readonly Guid QueueId = Guid.Parse("cccccccc-5555-4555-8555-666666666666");
        private static readonly Guid DefinitionId = Guid.Parse("dddddddd-7777-4777-8777-888888888888");

        private static readonly EntityReference RunAs = new EntityReference("systemuser", RunAsId);

        private static FakeOrganizationService WithRow()
        {
            var service = new FakeOrganizationService();
            service.Seed(NotificationOutbox.NotificationEntity, NotificationId,
                "al_status", new OptionSetValue(NotificationOutbox.StatusPending),
                "al_recipientemail", "para@example.com",
                "al_subject", "Review submitted on case C-1",
                "al_body", "The review on case C-1 has been submitted.");
            return service;
        }

        private static void Variable(FakeOrganizationService service, string value)
        {
            service.Seed(
                "environmentvariabledefinition", DefinitionId,
                "schemaname", NotificationSender.AddressVariable,
                "defaultvalue", null);
            service.Seed(
                "environmentvariablevalue", Guid.NewGuid(),
                "environmentvariabledefinitionid",
                new EntityReference("environmentvariabledefinition", DefinitionId),
                "value", value);
        }

        private static void Queue(FakeOrganizationService service, string address, int state = 0)
        {
            service.Seed("queue", QueueId,
                "emailaddress", address,
                "statecode", new OptionSetValue(state));
        }

        private static NotificationDrain.Result Send(FakeOrganizationService service, EntityReference runAs)
        {
            var row = service.Retrieve(
                NotificationOutbox.NotificationEntity, NotificationId, NotificationDrain.Columns());
            return NotificationDrain.Send(service, row, runAs);
        }

        private static EntityReference From(FakeOrganizationService service)
        {
            var email = service.Creates.Single(c => c.LogicalName == NotificationDrain.EmailEntity);
            return email.GetAttributeValue<EntityCollection>("from").Entities.Single()
                .GetAttributeValue<EntityReference>("partyid");
        }

        private static string FailureReason(FakeOrganizationService service)
        {
            return service.Row(NotificationOutbox.NotificationEntity, NotificationId)
                .GetAttributeValue<string>("al_failurereason");
        }

        [Fact]
        public void Sends_from_the_shared_mailbox_queue_when_the_address_is_set()
        {
            var service = WithRow();
            Variable(service, Shared);
            Queue(service, Shared);

            Assert.Equal(NotificationDrain.Result.Sent, Send(service, RunAs));

            var from = From(service);
            Assert.Equal("queue", from.LogicalName);
            Assert.Equal(QueueId, from.Id);
        }

        [Fact]
        public void Tolerates_spaces_around_the_configured_address()
        {
            var service = WithRow();
            Variable(service, "  " + Shared + " ");
            Queue(service, Shared);

            Assert.Equal(NotificationDrain.Result.Sent, Send(service, RunAs));
            Assert.Equal(QueueId, From(service).Id);
        }

        [Fact]
        public void Needs_no_run_as_account_once_the_queue_is_found()
        {
            var service = WithRow();
            Variable(service, Shared);
            Queue(service, Shared);

            Assert.Equal(NotificationDrain.Result.Sent, Send(service, null));
            Assert.Equal(QueueId, From(service).Id);
        }

        [Fact]
        public void Refuses_rather_than_falling_back_when_no_queue_carries_the_address()
        {
            // "All communications" from the shared mailbox: a letter from the service account
            // instead would be the wrong sender with nothing to show for it. Failed with the
            // reason is visible, and al_DrainNotifications retries it once the queue exists.
            var service = WithRow();
            Variable(service, Shared);

            Assert.Equal(NotificationDrain.Result.Failed, Send(service, RunAs));
            Assert.DoesNotContain(service.Creates, c => c.LogicalName == NotificationDrain.EmailEntity);
            Assert.Empty(service.Requests);
            Assert.Contains(Shared, FailureReason(service));
        }

        [Fact]
        public void Refuses_when_the_queue_is_deactivated()
        {
            var service = WithRow();
            Variable(service, Shared);
            Queue(service, Shared, state: 1);

            Assert.Equal(NotificationDrain.Result.Failed, Send(service, RunAs));
            Assert.Contains(Shared, FailureReason(service));
        }

        [Fact]
        public void Sends_from_the_run_as_account_where_the_address_is_not_set()
        {
            // DEV and TEST until someone sets the variable there: unchanged behaviour.
            var service = WithRow();
            Queue(service, Shared);

            Assert.Equal(NotificationDrain.Result.Sent, Send(service, RunAs));

            var from = From(service);
            Assert.Equal("systemuser", from.LogicalName);
            Assert.Equal(RunAsId, from.Id);
        }
    }
}
