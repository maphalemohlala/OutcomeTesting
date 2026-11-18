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

        private const int FailureReasonLength = NotificationOutbox.FailureReasonLength;

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
                NotificationOutbox.AttachmentNameAttr,
                NotificationOutbox.AttachmentBodyAttr,
                "al_subject",
                "al_body",
                "al_event",
                "al_targettable",
                "al_targetid",
                "al_senton");
        }

        /// <summary>
        /// How long a claim holds a row against another drain. A drain that claimed a row and
        /// then died leaves it claimed; after this it can be drained again.
        /// </summary>
        public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Sends one notification and stamps the outcome on the row.
        ///
        /// <paramref name="sender"/> is the account the plug-in is registered to run as. The
        /// email leaves from the shared mailbox's queue where <c>al_NotificationSenderAddress</c>
        /// is set, and from this account only where it is not (<see cref="NotificationSender"/>).
        /// Server-side email requires the sending mailbox to be approved and tested on the
        /// environment; where it is not, the send throws and the row lands at <c>Failed</c>
        /// saying so, which is the visible version of that gap.
        /// </summary>
        public static Result Send(IOrganizationService service, Entity notification, EntityReference sender)
        {
            return Send(service, notification, sender, false);
        }

        /// <summary>
        /// As <see cref="Send(IOrganizationService, Entity, EntityReference)"/>.
        /// <paramref name="insideTransaction"/> is true for al_DrainNotifications, which runs
        /// synchronously: there a Dataverse fault cannot be recorded on the row and carried
        /// past - the platform rolls back the whole batch, the Failed stamp with it, once a
        /// plug-in has absorbed one - so it stops the batch with the row and the reason instead
        /// of ending in the platform's own unexplained abort.
        /// </summary>
        public static Result Send(
            IOrganizationService service, Entity notification, EntityReference sender, bool insideTransaction)
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
                // A Failed row with nobody to send to already says why (an unreached
                // recipient, a letter too long to hold); that reason is the useful one and is
                // kept, rather than written over with this one on every drain.
                if (Status(notification) == NotificationOutbox.StatusFailed)
                {
                    return Result.Skipped;
                }

                // The OD-030 gap made visible rather than guessed at. An event whose
                // recipient could not be resolved is a routing question for a person, so it
                // is parked where a person can see it — not sent to a plausible address.
                return Fail(service, notification.Id,
                    "No recipient address. The event was queued with nobody to send it to, "
                    + "so nothing was sent.");
            }

            // The async step and al_DrainNotifications can both reach a Pending row; only the
            // one that claims it sends it.
            if (!Claim(service, notification.Id))
            {
                return Result.Skipped;
            }

            EntityReference from;
            try
            {
                string problem;
                from = NotificationSender.Resolve(service, sender, out problem);
                if (from == null)
                {
                    return Fail(service, notification.Id, problem);
                }
            }
            catch (Exception error)
            {
                StopInsideTransaction(insideTransaction, notification.Id, error);

                // A queue the run-as account cannot read is a configuration fault like any
                // other, and belongs on the row rather than in an async retry.
                return Fail(service, notification.Id, Describe(error));
            }

            try
            {
                var email = Compose(notification, recipient, from);

                // Markup letters go out in the house frame (2026-10-08); plain ones as written.
                email["description"] = NotificationFrame.Wrap(
                    email.GetAttributeValue<string>("description"), ProductName.Read(service));

                var emailId = service.Create(email);

                Attach(service, notification, emailId, insideTransaction);

                var send = new OrganizationRequest(SendEmailMessage);
                send["EmailId"] = emailId;
                send["IssueSend"] = true;
                send["TrackingToken"] = string.Empty;
                service.Execute(send);
            }
            catch (Exception error)
            {
                StopInsideTransaction(insideTransaction, notification.Id, error);

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
            var status = Status(notification);
            if (status != NotificationOutbox.StatusPending && status != NotificationOutbox.StatusFailed)
            {
                return false;
            }

            // Claimed by a drain that is sending it now; see Claim.
            var claimed = notification.GetAttributeValue<DateTime?>("al_senton");
            return !claimed.HasValue || claimed.Value < DateTime.UtcNow - ClaimTimeout;
        }

        private static int? Status(Entity notification)
        {
            var status = notification.GetAttributeValue<OptionSetValue>("al_status");
            return status == null ? (int?)null : status.Value;
        }

        /// <summary>
        /// Marks the row as being sent, or false where another drain got there first.
        ///
        /// <para>
        /// The row is read again and stamped with <c>al_senton</c> only if it has not changed
        /// since that read (its row version), so of two drains holding the same Pending row one
        /// claims it and the other finds it claimed and leaves it. Without this the async step
        /// and al_DrainNotifications could each send the same letter. A send that fails clears
        /// the stamp (<see cref="Fail"/>), and a stamp left by a drain that died lapses after
        /// <see cref="ClaimTimeout"/>.
        /// </para>
        /// <para>
        /// A row read without a version (only the test fake does that) is stamped plainly, as
        /// AssignCasePlugin does with a missing version.
        /// </para>
        /// </summary>
        private static bool Claim(IOrganizationService service, Guid notificationId)
        {
            var current = service.Retrieve(
                NotificationOutbox.NotificationEntity, notificationId, new ColumnSet("al_status", "al_senton"));
            if (!IsDrainable(current))
            {
                return false;
            }

            var claim = new Entity(NotificationOutbox.NotificationEntity, notificationId)
            {
                ["al_senton"] = DateTime.UtcNow,
            };

            if (string.IsNullOrEmpty(current.RowVersion))
            {
                service.Update(claim);
                return true;
            }

            claim.RowVersion = current.RowVersion;
            try
            {
                service.Execute(new Microsoft.Xrm.Sdk.Messages.UpdateRequest
                {
                    Target = claim,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                });
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
            {
                // Lost the race in the moment between the read and the stamp. Outside a
                // transaction (the async step) that is simply the other drain's row; inside
                // al_DrainNotifications's transaction the platform will roll the batch back
                // whatever this returns, and nothing it held is sent, so a re-run is safe.
                if (CommandHelpers.IsConcurrencyFault(fault))
                {
                    return false;
                }

                throw;
            }

            return true;
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

        /// <summary>
        /// Attaches the document the outbox row carries, if it carries one (Change 2, AD-164).
        ///
        /// <para>
        /// Created against the email BEFORE SendEmail, because an attachment added after the
        /// send is an attachment nobody receives.
        /// </para>
        /// <para>
        /// <b>A failed attachment does not fail the send.</b> The letter is the thing that
        /// has to arrive; a document that cannot be attached leaves the recipient with the
        /// words and without the summary, which is a worse email but not a missing one.
        /// Throwing here would mark the row Failed and re-drain it, sending the letter twice
        /// to fix a document.
        /// </para>
        /// </summary>
        private static void Attach(IOrganizationService service, Entity notification, Guid emailId, bool insideTransaction)
        {
            var body = notification.GetAttributeValue<string>(NotificationOutbox.AttachmentBodyAttr);
            var name = notification.GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr);

            // Several files may ride one row (a Tax check, an AQS check, the remediation),
            // joined in both columns in the same order.
            foreach (var attachment in NotificationOutbox.UnpackAttachments(name, body))
            {
                try
                {
                    service.Create(new Entity("activitymimeattachment")
                    {
                        ["objectid"] = new EntityReference(EmailEntity, emailId),
                        ["objecttypecode"] = EmailEntity,
                        ["subject"] = attachment.Key,
                        ["filename"] = attachment.Key,
                        ["mimetype"] = "application/pdf",
                        ["body"] = attachment.Value,
                    });
                }
                catch (Exception error)
                {
                    // Deliberately swallowed; see the summary. One file that cannot be
                    // attached does not cost the others - except inside a transaction, where
                    // a swallowed fault would cost the whole batch.
                    StopInsideTransaction(insideTransaction, notification.Id, error);
                }
            }
        }

        /// <summary>See <see cref="Send(IOrganizationService, Entity, EntityReference, bool)"/>.</summary>
        private static void StopInsideTransaction(bool insideTransaction, Guid notificationId, Exception error)
        {
            if (!insideTransaction)
            {
                return;
            }

            // Already stopped by an inner step (an attachment).
            if (error is InvalidPluginExecutionException)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }

            if (error is System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                throw new InvalidPluginExecutionException(
                    "Notification " + notificationId.ToString("D") + " could not be sent: " + Describe(error)
                    + " The batch was stopped there; run it again once that is fixed.",
                    error);
            }
        }

        private static Result Fail(IOrganizationService service, Guid notificationId, string reason)
        {
            service.Update(new Entity(NotificationOutbox.NotificationEntity, notificationId)
            {
                ["al_status"] = new OptionSetValue(NotificationOutbox.StatusFailed),
                ["al_failurereason"] = CommandHelpers.Truncate(reason, FailureReasonLength),

                // Releases the claim, so the row is retried rather than held.
                ["al_senton"] = null,
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

    }
}
