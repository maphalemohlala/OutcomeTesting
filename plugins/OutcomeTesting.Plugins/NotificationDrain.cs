using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Drains one <c>al_notification</c> row: builds an email, hands it to Dataverse
    /// server-side email, and records what happened on the row (PP-15, AD-035, OD-030).
    ///
    /// Delivery is Dataverse server-side email with no Power Automate, per the 2026-09-03
    /// direction that closed OD-030 gap (b), so the solution still carries no connector
    /// dependency anywhere after AD-073.
    ///
    /// **The transactional split is the point.** <see cref="NotificationOutbox"/> writes the
    /// row synchronously, inside the state change's transaction. This class runs afterwards,
    /// outside it. That is what makes a retry safe: the row cannot exist without the state
    /// change, the send cannot happen without the row, and a send that fails leaves a
    /// <c>Failed</c> row carrying its reason rather than rolling back a completed review.
    ///
    /// **Every terminal state is written, including failure.** A drain that swallowed errors
    /// would produce exactly the failure OD-030 warns about — the outbox looking healthy
    /// while nothing arrives. A row that cannot be sent ends at <c>Failed</c> with
    /// <c>al_failurereason</c> populated, which is visible in a view and retryable, rather
    /// than resting at <c>Pending</c> forever where nothing distinguishes it from a row the
    /// drain has not reached yet.
    ///
    /// The SendEmail message is issued untyped rather than through
    /// <c>Microsoft.Crm.Sdk.Messages.SendEmailRequest</c>. The plug-in assembly references
    /// only the core SDK today, and the three parameters this needs are part of the message
    /// contract, so an untyped request keeps the dependency list where it is.
    /// </summary>
    public static class NotificationDrain
    {
        public const string EmailEntity = "email";
        public const string ActivityPartyEntity = "activityparty";
        public const string SendEmailMessage = "SendEmail";

        /// <summary>activityparty.participationtypemask: 1 = Sender, 2 = To Recipient.</summary>
        private const int PartySender = 1;
        private const int PartyToRecipient = 2;

        private const int FailureReasonLength = 2000;

        /// <summary>What the drain did to one row, so callers can report rather than guess.</summary>
        public enum Result
        {
            /// <summary>Sent, and the row is stamped <c>Sent</c> with <c>al_senton</c>.</summary>
            Sent,

            /// <summary>Not a Pending or Failed row — already delivered, so left alone.</summary>
            Skipped,

            /// <summary>Could not be sent; the row is stamped <c>Failed</c> with a reason.</summary>
            Failed,
        }

        /// <summary>
        /// Columns the drain reads. Named once so the async step, the batch retry and the
        /// tests cannot disagree about what a drainable row looks like.
        /// </summary>
        public static ColumnSet Columns()
        {
            return new ColumnSet(
                "al_status",
                "al_recipientemail",
                "al_subject",
                "al_body",
                "al_event",
                "al_targettable",
                "al_targetid");
        }

        /// <summary>
        /// Sends one notification and stamps the outcome on the row.
        ///
        /// <paramref name="sender"/> is the mailbox the email leaves from. It is the account
        /// the plug-in is registered to run as — the service account — never an address in
        /// code (AGENTS.md rule 7). Server-side email requires that mailbox to be approved
        /// and tested on the environment; where it is not, the send throws and the row lands
        /// at <c>Failed</c> saying so, which is the visible version of that gap.
        /// </summary>
        public static Result Send(IOrganizationService service, Entity notification, EntityReference sender)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (notification == null)
            {
                throw new ArgumentNullException(nameof(notification));
            }

            if (!IsDrainable(notification))
            {
                return Result.Skipped;
            }

            var recipient = notification.GetAttributeValue<string>("al_recipientemail");
            if (string.IsNullOrWhiteSpace(recipient))
            {
                // The OD-030 gap made visible rather than guessed at. An event whose
                // recipient could not be resolved is a routing question for a person, so it
                // is parked where a person can see it — not sent to a plausible address.
                return Fail(service, notification.Id,
                    "No recipient address. The event was queued with nobody to send it to, "
                    + "so nothing was sent (OD-030).");
            }

            if (sender == null)
            {
                return Fail(service, notification.Id,
                    "No sending account. The drain step must be registered to run as the "
                    + "account whose mailbox is approved for server-side email.");
            }

            try
            {
                var emailId = service.Create(Compose(notification, recipient, sender));

                var send = new OrganizationRequest(SendEmailMessage);
                send["EmailId"] = emailId;
                send["IssueSend"] = true;
                send["TrackingToken"] = string.Empty;
                service.Execute(send);
            }
            catch (Exception error)
            {
                // Broad on purpose. The row is the only record that this event needed an
                // email, and losing the reason to an unexpected exception type would leave
                // a Pending row that no one can explain. The reason is written, and the
                // async platform retry (or al_DrainNotifications) picks it up again.
                return Fail(service, notification.Id, Describe(error));
            }

            service.Update(new Entity(NotificationOutbox.NotificationEntity, notification.Id)
            {
                ["al_status"] = new OptionSetValue(NotificationOutbox.StatusSent),
                ["al_senton"] = DateTime.UtcNow,
                ["al_failurereason"] = null,
            });

            return Result.Sent;
        }

        /// <summary>
        /// True when the row is one the drain owns. <c>Sent</c> is deliberately excluded:
        /// re-draining a delivered row would send a second email, which is the one failure
        /// the outbox exists to prevent. <c>Failed</c> is included, because a failure that
        /// could not be retried would make an approved mailbox arriving later useless.
        /// </summary>
        public static bool IsDrainable(Entity notification)
        {
            var status = notification.GetAttributeValue<OptionSetValue>("al_status");
            if (status == null)
            {
                return false;
            }

            return status.Value == NotificationOutbox.StatusPending
                || status.Value == NotificationOutbox.StatusFailed;
        }

        /// <summary>
        /// Builds the email activity. Plain text in <c>description</c>: the bodies are short
        /// operational sentences, and HTML would only add an escaping problem to a message
        /// that carries a case reference and a person's name.
        ///
        /// No <c>regardingobjectid</c>. The outbox points at whatever table raised the event
        /// (<c>al_targettable</c>), and setting a regarding object would require activities
        /// enabled on every one of them — a schema change to five tables in exchange for a
        /// link the email body already spells out.
        /// </summary>
        public static Entity Compose(Entity notification, string recipient, EntityReference sender)
        {
            var from = new Entity(ActivityPartyEntity);
            from["partyid"] = sender;
            from["participationtypemask"] = new OptionSetValue(PartySender);

            // addressused rather than a party lookup, because the recipient is an address the
            // outbox resolved at queue time — a para-planner who is deliberately not a
            // portal or Dataverse user still has to be reachable (BR-009).
            var to = new Entity(ActivityPartyEntity);
            to["addressused"] = recipient;
            to["participationtypemask"] = new OptionSetValue(PartyToRecipient);

            return new Entity(EmailEntity)
            {
                ["subject"] = notification.GetAttributeValue<string>("al_subject") ?? string.Empty,
                ["description"] = notification.GetAttributeValue<string>("al_body") ?? string.Empty,
                ["from"] = new EntityCollection(new List<Entity> { from }) { EntityName = ActivityPartyEntity },
                ["to"] = new EntityCollection(new List<Entity> { to }) { EntityName = ActivityPartyEntity },
            };
        }

        private static Result Fail(IOrganizationService service, Guid notificationId, string reason)
        {
            service.Update(new Entity(NotificationOutbox.NotificationEntity, notificationId)
            {
                ["al_status"] = new OptionSetValue(NotificationOutbox.StatusFailed),
                ["al_failurereason"] = Truncate(reason, FailureReasonLength),
            });

            return Result.Failed;
        }

        /// <summary>
        /// The message a person reading the outbox needs, innermost first — Dataverse wraps
        /// a mailbox refusal several layers deep and the outer message says only that
        /// something failed. Never surfaced to an end user; this column is operational
        /// (PP-16, NFR-OBS-01).
        /// </summary>
        private static string Describe(Exception error)
        {
            var innermost = error;
            while (innermost.InnerException != null)
            {
                innermost = innermost.InnerException;
            }

            return ReferenceEquals(innermost, error)
                ? error.Message
                : innermost.Message + " (" + error.Message + ")";
        }

        private static string Truncate(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length > length ? value.Substring(0, length) : value;
        }
    }
}
