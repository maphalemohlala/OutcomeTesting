using System;
using System.Globalization;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The PP-15 notification outbox (AD-035, OD-030).
    ///
    /// A row is written into <c>al_notification</c> in the same transaction as the state
    /// change that caused it, and drained separately. That is what makes a retry safe and a
    /// duplicate send impossible: if the state change rolls back, so does the notification,
    /// and if it commits, the notification is committed with it — there is no window where
    /// one exists without the other.
    ///
    /// The event vocabulary is the FIVE events AD-035 names. PP-15 says nine; the other four
    /// are not enumerated in any requirement, knowledge file or design document (OD-030 gap
    /// (a)), so they are not invented here. Adding option values later is additive.
    ///
    /// Delivery is Dataverse server-side email with no Power Automate (OD-030, 2026-09-03).
    /// The drain is <see cref="NotificationDrain"/>, reached from the asynchronous
    /// <see cref="NotificationDrainPlugin"/> step and from the al_DrainNotifications command.
    /// It sends from the account the step is registered to run as, so server-side email still
    /// needs an approved, tested mailbox on the environment — where there is none, the step
    /// is simply not registered, rows rest at Pending, and nothing claims an email was sent
    /// that was not.
    /// </summary>
    public static class NotificationOutbox
    {
        public const string NotificationEntity = "al_notification";

        // Option values from the al_notification_event set created with the table.
        public const int EventAllocation = 120910800;
        public const int EventReviewSubmitted = 120910801;
        public const int EventRemediationAssigned = 120910802;
        public const int EventSignoffApproved = 120910803;
        public const int EventSignoffRejected = 120910804;

        public const int StatusPending = 120910810;
        public const int StatusSent = 120910811;
        public const int StatusFailed = 120910812;

        /// <summary>
        /// The code is deterministic per event and target, and the table's alternate key.
        /// A command that runs twice for the same intent therefore collides here rather
        /// than queueing a second email — the duplicate-proofing is the key, not a
        /// after-the-fact scan for near-identical rows.
        /// </summary>
        public static string CodeFor(int eventValue, Guid targetId)
        {
            return EventName(eventValue).Replace(" ", string.Empty).ToUpperInvariant()
                + "-" + targetId.ToString("N").ToUpperInvariant();
        }

        public static string EventName(int eventValue)
        {
            switch (eventValue)
            {
                case EventAllocation: return "Allocation";
                case EventReviewSubmitted: return "Review submitted";
                case EventRemediationAssigned: return "Remediation assigned";
                case EventSignoffApproved: return "Sign-off approved";
                case EventSignoffRejected: return "Sign-off rejected";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Writes one outbox row. Returns the new row's id, or <see cref="Guid.Empty"/> when
        /// an identical event was already queued for the same target.
        ///
        /// A duplicate-key collision is swallowed because it is the expected outcome of a
        /// retry and means the outbox is already correct. Every other failure is allowed to
        /// propagate: the row belongs to the same transaction as the state change, and
        /// silently dropping it would produce exactly the failure OD-030 warns about — the
        /// system looking correct while nothing arrives.
        /// </summary>
        public static Guid Queue(
            IOrganizationService service,
            IPluginExecutionContext context,
            int eventValue,
            string targetTable,
            Guid targetId,
            string recipientEmail,
            string subject,
            string body)
        {
            return Queue(service, context.CorrelationId, eventValue, targetTable, targetId, recipientEmail, subject, body);
        }

        /// <summary>
        /// As <see cref="Queue(IOrganizationService, IPluginExecutionContext, int, string, Guid, string, string, string)"/>,
        /// for callers that hold the correlation id but not the context.
        /// </summary>
        public static Guid Queue(
            IOrganizationService service,
            Guid correlationId,
            int eventValue,
            string targetTable,
            Guid targetId,
            string recipientEmail,
            string subject,
            string body)
        {
            var code = CodeFor(eventValue, targetId);
            var row = new Entity(NotificationEntity)
            {
                ["al_name"] = Truncate(EventName(eventValue) + ": " + subject, 200),
                ["al_notificationcode"] = code,
                ["al_event"] = new OptionSetValue(eventValue),
                ["al_status"] = new OptionSetValue(StatusPending),
                ["al_targettable"] = targetTable,
                ["al_targetid"] = targetId.ToString("D"),
                ["al_subject"] = Truncate(subject, 400),
                ["al_body"] = Truncate(body, 4000),
                ["al_queuedon"] = DateTime.UtcNow,
                ["al_correlationid"] = correlationId.ToString("D"),
            };

            // Left empty when the record does not name one. Recipient routing per event is
            // not settled for every event (OD-030), and an invented address is worse than a
            // row a person has to look at: the drain refuses to send without one.
            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                row["al_recipientemail"] = Truncate(recipientEmail, 200);
            }

            try
            {
                return service.Create(row);
            }
            catch (FaultException<OrganizationServiceFault> fault)
            {
                if (IsDuplicateKey(fault))
                {
                    return Guid.Empty;
                }

                throw;
            }
        }

        /// <summary>
        /// True when the create failed because the alternate key already holds this code.
        /// DuplicateRecord (0x80040237) and the alternate-key variant both mean "already
        /// queued", which is a success for an outbox.
        /// </summary>
        public static bool IsDuplicateKey(FaultException<OrganizationServiceFault> fault)
        {
            if (fault.Detail != null)
            {
                var code = fault.Detail.ErrorCode;
                if (code == unchecked((int)0x80040237) || code == unchecked((int)0x80060892))
                {
                    return true;
                }
            }

            var message = fault.Message ?? string.Empty;
            return message.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Work email of a contact (AD-010), or null when there is none to reach.</summary>
        public static string ContactEmail(IOrganizationService service, EntityReference contact)
        {
            if (contact == null)
            {
                return null;
            }

            var row = service.Retrieve("contact", contact.Id, new ColumnSet("emailaddress1"));
            return row.GetAttributeValue<string>("emailaddress1");
        }

        /// <summary>Work email of a Dataverse user, or null when there is none to reach.</summary>
        public static string UserEmail(IOrganizationService service, EntityReference user)
        {
            if (user == null)
            {
                return null;
            }

            var row = service.Retrieve("systemuser", user.Id, new ColumnSet("internalemailaddress"));
            return row.GetAttributeValue<string>("internalemailaddress");
        }

        /// <summary>
        /// The email of the para-planner named on a case, or null when it cannot be resolved
        /// to exactly one person (BR-009, OD-030(ii)).
        ///
        /// The case carries <c>al_paraplanner</c> as a <b>name</b> and
        /// <c>al_paraplannercode</c> as an Intelligent Office identifier — neither is an
        /// address, and Contact is unmodified in this solution, so the code has nothing to
        /// match on. The name is therefore matched against <c>contact.fullname</c>, which is
        /// the only link between the two that exists in the model today.
        ///
        /// <b>Matching by name is weak, so it is made to fail loudly rather than
        /// approximately.</b> Two people sharing a name, a contact with no work email, or no
        /// contact at all all return null, and the outbox row is queued with no recipient —
        /// the drain then marks it Failed saying so. Sending a client's advice outcome to the
        /// wrong para-planner because two of them are called J Smith is a data-protection
        /// incident; a Failed row is an operational one. Only an unambiguous match sends.
        ///
        /// BR-009 is satisfied precisely because this resolves a Contact and not a system
        /// user: the para-planner is reachable by email without holding a Dataverse licence,
        /// a web role or any operational access to the case.
        /// </summary>
        public static string ParaplannerEmail(IOrganizationService service, EntityReference outcomeCase)
        {
            if (outcomeCase == null)
            {
                return null;
            }

            var row = service.Retrieve("al_outcomecase", outcomeCase.Id, new ColumnSet("al_paraplanner"));
            var name = row.GetAttributeValue<string>("al_paraplanner");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("emailaddress1"),
                // Two, not one: the second row is what proves the match was unambiguous.
                // TopCount 1 would return the first of two J Smiths and look certain.
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("fullname", ConditionOperator.Equal, name.Trim());
            query.Criteria.AddCondition("emailaddress1", ConditionOperator.NotNull);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var matches = service.RetrieveMultiple(query).Entities;
            return matches.Count == 1 ? matches[0].GetAttributeValue<string>("emailaddress1") : null;
        }

        /// <summary>The case reference a person recognises, for the subject line.</summary>
        public static string CaseReference(IOrganizationService service, EntityReference outcomeCase)
        {
            if (outcomeCase == null)
            {
                return null;
            }

            var row = service.Retrieve("al_outcomecase", outcomeCase.Id, new ColumnSet("al_casereference"));
            return row.GetAttributeValue<string>("al_casereference");
        }

        private static string Truncate(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length > length ? value.Substring(0, length) : value;
        }

        /// <summary>Formats a count for a notification body without culture surprises.</summary>
        public static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
