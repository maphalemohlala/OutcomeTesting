using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The PP-15 drain (AD-035, OD-030). The rules worth a test here are the ones that decide
    /// whether a person gets an email, a second email, or a wrong one: what the drain refuses
    /// to send, what it records when a send fails, and that a delivered row is never re-sent.
    /// </summary>
    public class NotificationDrainTests
    {
        private static readonly Guid NotificationId = Guid.Parse("aaaaaaaa-1111-4111-8111-222222222222");
        private static readonly Guid SenderId = Guid.Parse("bbbbbbbb-3333-4333-8333-444444444444");

        private static readonly EntityReference Sender = new EntityReference("systemuser", SenderId);

        private static FakeOrganizationService WithRow(
            int status = NotificationOutbox.StatusPending, string recipient = "para@example.com")
        {
            var service = new FakeOrganizationService();
            service.Seed(NotificationOutbox.NotificationEntity, NotificationId,
                "al_status", new OptionSetValue(status),
                "al_recipientemail", recipient,
                "al_subject", "Review submitted on case C-1",
                "al_body", "The review on case C-1 has been submitted.");
            return service;
        }

        private static Entity Row(FakeOrganizationService service)
        {
            return service.Retrieve(NotificationOutbox.NotificationEntity, NotificationId, NotificationDrain.Columns());
        }

        [Fact]
        public void Sends_a_pending_row_and_stamps_it_sent()
        {
            var service = WithRow();

            var result = NotificationDrain.Send(service, Row(service), Sender);

            Assert.Equal(NotificationDrain.Result.Sent, result);
            Assert.Single(service.Requests, r => r.RequestName == NotificationDrain.SendEmailMessage);

            var stored = service.Row(NotificationOutbox.NotificationEntity, NotificationId);
            Assert.Equal(NotificationOutbox.StatusSent, stored.GetAttributeValue<OptionSetValue>("al_status").Value);
            Assert.NotNull(stored.GetAttributeValue<DateTime?>("al_senton"));
        }

        [Fact]
        public void Issues_the_send_rather_than_leaving_a_draft()
        {
            // Creating the email row is not sending it. A drain that stopped at Create would
            // stamp every row Sent while the outbox quietly filled with unsent drafts.
            var service = WithRow();

            NotificationDrain.Send(service, Row(service), Sender);

            var send = Assert.Single(service.Requests);
            Assert.True((bool)send["IssueSend"]);
            Assert.Equal(
                service.Creates.Single(c => c.LogicalName == NotificationDrain.EmailEntity).Id,
                (Guid)send["EmailId"]);
        }

        [Fact]
        public void Addresses_the_email_from_the_sending_account_to_the_recipient()
        {
            var service = WithRow();

            NotificationDrain.Send(service, Row(service), Sender);

            var email = service.Creates.Single(c => c.LogicalName == NotificationDrain.EmailEntity);
            Assert.Equal("Review submitted on case C-1", email.GetAttributeValue<string>("subject"));

            var to = email.GetAttributeValue<EntityCollection>("to").Entities.Single();
            Assert.Equal("para@example.com", to.GetAttributeValue<string>("addressused"));

            var from = email.GetAttributeValue<EntityCollection>("from").Entities.Single();
            Assert.Equal(SenderId, from.GetAttributeValue<EntityReference>("partyid").Id);
        }

        [Fact]
        public void Refuses_a_row_with_no_recipient_and_says_so_on_the_row()
        {
            // OD-030(ii): a para-planner who could not be resolved leaves the row unrouted.
            // Nothing is sent, and the reason is on the record where a person can act on it
            // rather than buried in a trace log.
            var service = WithRow(recipient: null);

            var result = NotificationDrain.Send(service, Row(service), Sender);

            Assert.Equal(NotificationDrain.Result.Failed, result);
            Assert.Empty(service.Requests);
            Assert.DoesNotContain(service.Creates, c => c.LogicalName == NotificationDrain.EmailEntity);

            var stored = service.Row(NotificationOutbox.NotificationEntity, NotificationId);
            Assert.Equal(NotificationOutbox.StatusFailed, stored.GetAttributeValue<OptionSetValue>("al_status").Value);
            Assert.Contains("No recipient", stored.GetAttributeValue<string>("al_failurereason"));
        }

        [Fact]
        public void Refuses_a_row_with_a_blank_recipient()
        {
            // Whitespace is not an address. Dataverse would accept the activity party and
            // the email would vanish.
            var service = WithRow(recipient: "   ");

            Assert.Equal(NotificationDrain.Result.Failed, NotificationDrain.Send(service, Row(service), Sender));
            Assert.Empty(service.Requests);
        }

        [Fact]
        public void Refuses_when_no_sending_account_was_resolved()
        {
            var service = WithRow();

            Assert.Equal(NotificationDrain.Result.Failed, NotificationDrain.Send(service, Row(service), null));

            var stored = service.Row(NotificationOutbox.NotificationEntity, NotificationId);
            Assert.Contains("No sending account", stored.GetAttributeValue<string>("al_failurereason"));
        }

        [Fact]
        public void Records_why_a_send_failed_instead_of_going_quiet()
        {
            // The unapproved-mailbox case OD-030 warns about: the row must not stay Pending,
            // because Pending is indistinguishable from "not drained yet".
            var service = WithRow();
            service.ExecuteThrows = new InvalidOperationException("The mailbox is not approved for sending.");

            var result = NotificationDrain.Send(service, Row(service), Sender);

            Assert.Equal(NotificationDrain.Result.Failed, result);
            var stored = service.Row(NotificationOutbox.NotificationEntity, NotificationId);
            Assert.Equal(NotificationOutbox.StatusFailed, stored.GetAttributeValue<OptionSetValue>("al_status").Value);
            Assert.Contains("not approved", stored.GetAttributeValue<string>("al_failurereason"));
        }

        [Fact]
        public void Unwraps_a_nested_failure_so_the_reason_is_the_useful_one()
        {
            var service = WithRow();
            service.ExecuteThrows = new InvalidOperationException(
                "An unexpected error occurred.",
                new InvalidOperationException("No email address for the sender."));

            NotificationDrain.Send(service, Row(service), Sender);

            var reason = service.Row(NotificationOutbox.NotificationEntity, NotificationId)
                .GetAttributeValue<string>("al_failurereason");
            Assert.Contains("No email address for the sender.", reason);
        }

        [Fact]
        public void Never_re_sends_a_delivered_row()
        {
            // The duplicate-send guard. The alternate key stops a second row being queued;
            // this stops the one row being sent twice.
            var service = WithRow(NotificationOutbox.StatusSent);

            var result = NotificationDrain.Send(service, Row(service), Sender);

            Assert.Equal(NotificationDrain.Result.Skipped, result);
            Assert.Empty(service.Requests);
            Assert.Empty(service.Updates);
        }

        [Fact]
        public void Retries_a_failed_row()
        {
            // Without this an approved mailbox arriving later would send nothing that had
            // already failed against the unapproved one.
            var service = WithRow(NotificationOutbox.StatusFailed);

            Assert.Equal(NotificationDrain.Result.Sent, NotificationDrain.Send(service, Row(service), Sender));
        }

        [Fact]
        public void Clears_the_failure_reason_when_a_retry_succeeds()
        {
            var service = WithRow(NotificationOutbox.StatusFailed);
            service.Row(NotificationOutbox.NotificationEntity, NotificationId)["al_failurereason"] = "was broken";

            NotificationDrain.Send(service, Row(service), Sender);

            Assert.Null(service.Row(NotificationOutbox.NotificationEntity, NotificationId)
                .GetAttributeValue<string>("al_failurereason"));
        }

        [Theory]
        [InlineData(NotificationOutbox.StatusPending, true)]
        [InlineData(NotificationOutbox.StatusFailed, true)]
        [InlineData(NotificationOutbox.StatusSent, false)]
        public void Owns_pending_and_failed_rows_only(int status, bool drainable)
        {
            var row = new Entity(NotificationOutbox.NotificationEntity, NotificationId)
            {
                ["al_status"] = new OptionSetValue(status),
            };

            Assert.Equal(drainable, NotificationDrain.IsDrainable(row));
        }

        [Fact]
        public void Treats_a_row_with_no_status_as_not_drainable()
        {
            var row = new Entity(NotificationOutbox.NotificationEntity, NotificationId);

            Assert.False(NotificationDrain.IsDrainable(row));
        }
    }
}
