using System;
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

        // The Power Pages site, for the links the bodies carry. See CaseLink.
        private const string SiteEntity = "powerpagesite";
        private const string SiteDomainAttr = "primarydomainname";

        // Option values from the al_notification_event set created with the table.
        public const int EventAllocation = 120910800;
        public const int EventReviewSubmitted = 120910801;
        public const int EventRemediationAssigned = 120910802;
        public const int EventSignoffApproved = 120910803;
        public const int EventSignoffRejected = 120910804;

        // Added 2026-09-10 for the adviser's "Case check - Pass" letter. Additive, as the
        // header above records: an option value appended to al_notification_event costs
        // nothing to the rows already carrying the first five.
        public const int EventCasePassed = 120910805;

        // Added 2026-09-14. The supervisor approved a remediation and left the grade for a
        // separate regrade, so the case is parked at Awaiting Recheck waiting for them to
        // record the final outcome. Additive in the same way as the value above.
        public const int EventRecheckDue = 120910806;

        // Added 2026-09-20 for Fixes 5. The adviser finished every action on the case, so it
        // is now the T&C Manager's turn and nothing told them. Additive in the same way as
        // the two values above: appending an option value costs nothing to rows already
        // carrying the earlier ones.
        public const int EventSignoffDue = 120910807;

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
            return CodeFor(eventValue, targetId, null);
        }

        /// <summary>
        /// As <see cref="CodeFor(int, Guid)"/>, with an occurrence appended.
        ///
        /// Some events happen to the same target more than once and must be told each
        /// time. An allocation is one: a check moved away from someone and back is a fresh
        /// allocation to them, and the row it writes is the same row, so a code keyed on
        /// the target alone would treat the second as already queued and say nothing
        /// (project owner direction, 2026-09-10 - "they need to get the email each time
        /// even if it's a reallocation"). Passing the moment it was allocated makes each
        /// occurrence its own outbox row, while a replay of the same one still collides.
        /// </summary>
        public static string CodeFor(int eventValue, Guid targetId, string occurrence)
        {
            var code = EventName(eventValue).Replace(" ", string.Empty).ToUpperInvariant()
                + "-" + targetId.ToString("N").ToUpperInvariant();

            return string.IsNullOrWhiteSpace(occurrence)
                ? code
                : code + "-" + occurrence.Trim().ToUpperInvariant();
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
                case EventCasePassed: return "Case passed";
                case EventRecheckDue: return "Recheck due";
                case EventSignoffDue: return "Sign-off due";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Whether this is an event the solution actually raises.
        ///
        /// Keyed off <see cref="EventName"/> so the two cannot drift: an event added there and
        /// forgotten here would let a template be attached to something nothing ever fires.
        /// </summary>
        public static bool IsKnownEvent(int eventValue)
        {
            return EventName(eventValue) != "Unknown";
        }

        /// <summary>Every event a template can be attached to, in the order they occur.</summary>
        public static int[] KnownEvents()
        {
            return new[]
            {
                EventAllocation,
                EventReviewSubmitted,
                EventRemediationAssigned,
                EventSignoffApproved,
                EventSignoffRejected,
                EventCasePassed,
                EventRecheckDue,
                EventSignoffDue,
            };
        }

        /// <summary>
        /// Writes one outbox row. Returns the new row's id, or <see cref="Guid.Empty"/> when
        /// an identical event was already queued for the same target.
        ///
        /// An already-queued event is not an error: it is the expected outcome of a retry, and
        /// it means the outbox is already correct, so it returns rather than throwing. Every
        /// other failure is allowed to propagate: the row belongs to the same transaction as
        /// the state change, and silently dropping it would produce exactly the failure OD-030
        /// warns about — the system looking correct while nothing arrives.
        /// </summary>
        public static Guid Queue(
            IOrganizationService service,
            IPluginExecutionContext context,
            int eventValue,
            string targetTable,
            Guid targetId,
            string recipientEmail,
            string subject,
            string body,
            string templateCode)
        {
            return Queue(
                service, context.CorrelationId, eventValue, targetTable, targetId,
                recipientEmail, subject, body, templateCode);
        }

        /// <summary>
        /// As <see cref="Queue(IOrganizationService, IPluginExecutionContext, int, string, Guid, string, string, string)"/>,
        /// for callers that hold the correlation id but not the context.
        /// </summary>
        /// <summary>The column holding the attached document's file name.</summary>
        public const string AttachmentNameAttr = "al_attachmentname";

        /// <summary>The column holding the attached document, base64 encoded.</summary>
        public const string AttachmentBodyAttr = "al_attachmentbody";

        /// <summary>
        /// Queues a notification and attaches a case summary to it (Change 2, AD-164).
        ///
        /// <para>
        /// The document is built HERE, at queue time, so it is a snapshot of the case at the
        /// moment of the event rather than at send time. A re-drain then sends the same
        /// document under the same letter, which a document generated at send time could not
        /// promise.
        /// </para>
        /// <para>
        /// <b>A document that cannot be built costs the attachment, never the letter.</b>
        /// This runs inside the transaction that caused the notification, so throwing here
        /// would roll back a checker's submit because a PDF could not be drawn. The letter
        /// goes either way and the row simply carries no attachment.
        /// </para>
        /// </summary>
        public static Guid QueueWithCompletedCheck(
            IOrganizationService service,
            Guid correlationId,
            int eventValue,
            string targetTable,
            Guid targetId,
            string recipientEmail,
            string subject,
            string body,
            string templateCode,
            EntityReference caseRef)
        {
            var id = Queue(
                service, correlationId, eventValue, targetTable, targetId,
                recipientEmail, subject, body, templateCode);

            // Guid.Empty means the event was already queued, so the attachment is already on
            // the row that exists and writing it again would replace a sent document.
            if (id == Guid.Empty || caseRef == null)
            {
                return id;
            }

            AttachCompletedCheck(service, id, caseRef);
            return id;
        }

        public static Guid Queue(
            IOrganizationService service,
            Guid correlationId,
            int eventValue,
            string targetTable,
            Guid targetId,
            string recipientEmail,
            string subject,
            string body,
            /*
             * The letter's template code, and required on purpose (F33).
             *
             * It used to default to null, and not one of the seven places that queue a
             * built-in letter passed it - so `SettingsFor(service, null)` found no row every
             * time and the chosen recipient below was dead for all twelve of them. The unit
             * tests all passed, because every one of them supplied the code by hand.
             *
             * An optional argument that silently disables a feature when it is left out is
             * not an argument anybody should have to remember. Required means the compiler
             * asks the ninth call site the question.
             */
            string templateCode,
            string occurrence = null)
        {
            var code = CodeFor(eventValue, targetId, occurrence);

            // Who this goes to may have been chosen as data (AD-168). The code's own routing
            // is the default and stays the answer unless somebody has actually picked
            // somebody else; an override that resolved to nobody is ignored rather than
            // allowed to empty a recipient the caller had worked out correctly.
            var settings = NotificationTemplateRows.SettingsFor(service, templateCode);
            recipientEmail = Chosen(service, settings, targetTable, targetId) ?? recipientEmail;
            var row = new Entity(NotificationEntity)
            {
                ["al_name"] = CommandHelpers.Truncate(EventName(eventValue) + ": " + subject, 200),
                ["al_notificationcode"] = code,
                ["al_event"] = new OptionSetValue(eventValue),
                ["al_status"] = new OptionSetValue(StatusPending),
                ["al_targettable"] = targetTable,
                ["al_targetid"] = targetId.ToString("D"),
                ["al_subject"] = CommandHelpers.Truncate(subject, 400),
                ["al_body"] = CommandHelpers.Truncate(body, 4000),
                ["al_queuedon"] = DateTime.UtcNow,
                ["al_correlationid"] = correlationId.ToString("D"),
            };

            // Left empty when the record does not name one. Recipient routing per event is
            // not settled for every event (OD-030), and an invented address is worse than a
            // row a person has to look at: the drain refuses to send without one.
            if (!string.IsNullOrWhiteSpace(recipientEmail))
            {
                row["al_recipientemail"] = CommandHelpers.Truncate(recipientEmail, 200);
            }

            // Read before writing, because a duplicate must never reach the platform as a
            // fault. This used to let the create fail on the alternate key and swallow the
            // fault, and that is the one thing a plug-in may not do: the platform aborts the
            // whole transaction of a plug-in that absorbs an OrganizationService fault and
            // carries on - "ISV code reduced the open transaction count" - which is the rule
            // OptionLabels already records for the metadata read.
            //
            // It was harmless while a transaction only ever queued one notification: the
            // duplicate arose on a replayed submit, which is a fresh transaction, and losing
            // that one was the point. From 2026-09-10 a review raises one action per thing
            // the checker marked down, NotificationEmitterPlugin fires on each create, and
            // the code is keyed on the review - so the second create in the same transaction
            // collided, the catch swallowed it, and the platform failed the whole submit.
            // Reassigning several actions from one case edit died the same way.
            //
            // A read of a row this transaction created is visible to it, so this closes both.
            if (Exists(service, code))
            {
                return Guid.Empty;
            }

            var created = service.Create(row);

            // The wording asked for the completed check (AD-171). The para-planner's letter
            // attaches one by its own call regardless; this is what lets any other letter.
            if (settings.WantsCompletedCheck)
            {
                AttachCompletedCheck(
                    service, created, NotificationRecipients.CaseOf(service, targetTable, targetId));
            }

            // The letters an administrator attached to this event, sent in addition to this
            // one. Deliberately AFTER the built-in is created: an extra letter is worth
            // having and is never worth losing the real one over, and a failure here would
            // otherwise take the notification that the system itself depends on with it.
            QueueCustom(service, correlationId, eventValue, targetTable, targetId);

            return created;
        }

        /// <summary>
        /// The address a stored template chooses for this letter, or null where none is
        /// chosen and the caller's own routing stands.
        /// </summary>
        private static string Chosen(
            IOrganizationService service,
            NotificationTemplateRows.TemplateSettings settings,
            string targetTable,
            Guid targetId)
        {
            if (settings == null || !settings.Kind.HasValue)
            {
                return null;
            }

            var choice = settings;
            var caseRef = NotificationRecipients.CaseOf(service, targetTable, targetId);
            var email = NotificationRecipients.EmailFor(
                service,
                choice.Kind.Value,
                caseRef,
                NotificationRecipients.ReviewOf(targetTable, targetId),
                choice.Contact);

            return string.IsNullOrWhiteSpace(email) ? null : email;
        }

        /// <summary>
        /// Queues one letter per template an administrator has attached to this event.
        ///
        /// <para>
        /// Each gets a notification code of its own - the event, the target and the template's
        /// code - so two custom letters at one event do not collide with each other or with
        /// the built-in, and a replay still finds them already queued.
        /// </para>
        /// <para>
        /// Never throws. These are letters somebody added to a system that worked without
        /// them; the transaction that raised the event must not fail because one of them
        /// could not be built.
        /// </para>
        /// </summary>
        private static void QueueCustom(
            IOrganizationService service,
            Guid correlationId,
            int eventValue,
            string targetTable,
            Guid targetId)
        {
            try
            {
                var templates = NotificationTemplateRows.CustomFor(service, eventValue);
                if (templates.Count == 0)
                {
                    return;
                }

                var caseRef = NotificationRecipients.CaseOf(service, targetTable, targetId);
                var reviewRef = NotificationRecipients.ReviewOf(targetTable, targetId);
                var tokens = NotificationTemplateRows.CaseTokens(service, caseRef);

                foreach (var template in templates)
                {
                    var email = NotificationRecipients.EmailFor(
                        service, template.RecipientKind, caseRef, reviewRef, template.RecipientContact);

                    if (string.IsNullOrWhiteSpace(email))
                    {
                        // Nobody to send to. Queuing it unaddressed would put a row in the
                        // outbox that can never drain and that nothing is watching.
                        continue;
                    }

                    var customCode = CodeFor(eventValue, targetId, template.Code);
                    if (Exists(service, customCode))
                    {
                        continue;
                    }


                    var customSubject = NotificationTemplates.Substitute(
                        template.Subject, tokens, escape: false);
                    var customBody = NotificationTemplates.Substitute(
                        template.Body, tokens, escape: true);

                    var customRow = new Entity(NotificationEntity)
                    {
                        ["al_name"] = CommandHelpers.Truncate(
                            EventName(eventValue) + ": " + customSubject, 200),
                        ["al_notificationcode"] = customCode,
                        ["al_event"] = new OptionSetValue(eventValue),
                        ["al_status"] = new OptionSetValue(StatusPending),
                        ["al_targettable"] = targetTable,
                        ["al_targetid"] = targetId.ToString("D"),
                        ["al_recipientemail"] = CommandHelpers.Truncate(email, 200),
                        ["al_subject"] = CommandHelpers.Truncate(customSubject, 400),
                        ["al_body"] = CommandHelpers.Truncate(customBody, 4000),
                        ["al_queuedon"] = DateTime.UtcNow,
                        ["al_correlationid"] = correlationId.ToString("D"),
                    };

                    var customId = service.Create(customRow);

                    if (template.WantsCompletedCheck)
                    {
                        AttachCompletedCheck(service, customId, caseRef);
                    }
                }
            }
            catch (Exception)
            {
                // See the summary. An extra letter never costs the event that raised it.
            }
        }

        /// <summary>
        /// Puts the completed check on a queued letter.
        ///
        /// <para>
        /// Never throws. The letter is already queued by the time this runs, and a document
        /// that could not be drawn must not take it back off - the same rule the para-planner's
        /// attachment has followed since AD-164.
        /// </para>
        /// </summary>
        private static void AttachCompletedCheck(
            IOrganizationService service, Guid notificationId, EntityReference caseRef)
        {
            if (notificationId == Guid.Empty || caseRef == null)
            {
                return;
            }

            try
            {
                var pdf = CompletedCheckPdf.Build(service, caseRef);
                if (pdf == null || pdf.Length == 0)
                {
                    return;
                }

                service.Update(new Entity(NotificationEntity, notificationId)
                {
                    [AttachmentNameAttr] = CompletedCheckPdf.FileName(CaseReference(service, caseRef)),
                    [AttachmentBodyAttr] = Convert.ToBase64String(pdf),
                });
            }
            catch (Exception)
            {
                // Deliberately swallowed; see the summary.
            }
        }

        /// <summary>
        /// True when this event is already queued for this target.
        ///
        /// The same question <see cref="Queue"/> asks itself, offered to callers that would
        /// otherwise do expensive work to build a notification the outbox already holds. It
        /// is an optimisation and not a guarantee: <see cref="Queue"/> still asks again, so a
        /// caller that skips this is correct, just slower.
        /// </summary>
        public static bool AlreadyQueued(
            IOrganizationService service, int eventValue, Guid targetId, string occurrence)
        {
            return Exists(service, CodeFor(eventValue, targetId, occurrence));
        }

        /// <summary>
        /// True when the outbox already holds this code.
        ///
        /// Deliberately not a try/catch around the create: see <see cref="Queue"/>. A race
        /// between two transactions can still lose to the alternate key, and that fault is
        /// left to propagate rather than be absorbed - a refused write the caller can see
        /// beats a transaction the platform kills for hiding one.
        /// </summary>
        private static bool Exists(IOrganizationService service, string code)
        {
            var query = new QueryExpression(NotificationEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_notificationcode", ConditionOperator.Equal, code);

            return service.RetrieveMultiple(query).Entities.Count > 0;
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

            var row = service.Retrieve(
                "al_outcomecase",
                outcomeCase.Id,
                new ColumnSet(ImportRules.ParaplannerEmailAttribute, "al_paraplanner"));

            var stored = row.GetAttributeValue<string>(ImportRules.ParaplannerEmailAttribute);
            var name = row.GetAttributeValue<string>("al_paraplanner");

            var match = MatchParaplanner(service, stored, name);
            if (match.IsMatch)
            {
                return match.Email;
            }

            // The address the extract carried, even when no contact answers to it. This is
            // the AD-168 judgement the adviser's letter already makes - a lost letter is
            // worse than one sent to an address the directory does not happen to hold - and
            // until 2026-09-21 the para-planner could not make it, because a name that
            // matched nobody left nothing to fall back to.
            return string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
        }

        /// <summary>Why a para-planner name did or did not reach somebody.</summary>
        public enum PersonMatchKind
        {
            /// <summary>The row named nobody.</summary>
            NoName,

            /// <summary>No active contact carries that name.</summary>
            NoContact,

            /// <summary>Two or more do, so no one of them can be chosen.</summary>
            Ambiguous,

            /// <summary>Exactly one does, and they have no work email.</summary>
            NoEmail,

            /// <summary>Exactly one active contact, with an email.</summary>
            Matched,
        }

        /// <summary>The outcome of resolving a para-planner name, with words for a report.</summary>
        public sealed class PersonMatch
        {
            /// <summary>What happened.</summary>
            public PersonMatchKind Kind { get; set; }

            /// <summary>The work email, set only when <see cref="IsMatch"/>.</summary>
            public string Email { get; set; }

            /// <summary>
            /// The contact the value resolved to, set only when <see cref="IsMatch"/>.
            ///
            /// This is the "link" part: a person field holds text, and this says which record
            /// that text turned out to be, so a caller can point at the person rather than
            /// repeat their name.
            /// </summary>
            public EntityReference Contact { get; set; }

            /// <summary>
            /// The matched contact's staff code, set only when <see cref="IsMatch"/>.
            ///
            /// Null on every failure kind by construction: an ambiguous name resolved to
            /// nobody, so there is no code to report. That is the point - a code guessed
            /// from the first of two Sam Joneses would attribute a fail to the wrong person
            /// on a file that leaves this system.
            /// </summary>
            public string StaffCode { get; set; }

            /// <summary>One sentence for the import report, naming the value that failed.</summary>
            public string Reason { get; set; }

            /// <summary>True only for an unambiguous, reachable match.</summary>
            public bool IsMatch
            {
                get { return Kind == PersonMatchKind.Matched; }
            }
        }

        /// <summary>
        /// Resolves a para-planner name to a reachable Contact, and says why when it cannot.
        ///
        /// <para>
        /// This is the one place that decides. <see cref="ParaplannerEmail"/> is a thin
        /// wrapper for the send path, and the import calls it directly so a name that will
        /// never reach anyone is reported on the day of the upload rather than surfacing
        /// weeks later as a Failed notification nobody is watching (audit finding 7).
        /// </para>
        /// <para>
        /// The four failures were one null until 2026-09-20. They are separated because a
        /// report saying "unmatched" tells an administrator nothing they can act on, while
        /// "two active contacts are named Sam Jones" and "Sam Jones has no work email" are
        /// different jobs for different people.
        /// </para>
        /// <para>
        /// <b>Deliberately stricter than before in one case.</b> The old query filtered on
        /// emailaddress1 being present, so two contacts of one name where only one had an
        /// email resolved to that one and sent. It now reads Ambiguous and sends nothing.
        /// That is the existing rule applied honestly - a missing email address is not
        /// evidence about which Sam Jones the case means, and the whole reason this matching
        /// fails loudly is that sending a client's advice outcome to the wrong para-planner
        /// is a data-protection incident where an unrouted row is an operational one.
        /// </para>
        /// </summary>
        public static PersonMatch MatchParaplanner(
            IOrganizationService service, string email, string name)
        {
            // Both, from 2026-09-21. The para-planner USED to have no email column on the
            // case - "so the name is all there is" is what this comment said - and that was
            // the only reason they were matched by name while the adviser was matched by
            // address. The extract carries ParaplannerEmail, ImportRules maps it, and
            // MatchPerson has read email first and name second since 2026-09-20.
            return MatchPerson(service, email, name, "para-planner");
        }

        /// <summary>
        /// The adviser, from <c>al_adviseremail</c> where it is set and <c>al_advisername</c>
        /// where it is not (project owner, 2026-09-20).
        /// </summary>
        public static PersonMatch MatchAdviser(
            IOrganizationService service, string email, string name)
        {
            return MatchPerson(service, email, name, "adviser");
        }

        /// <summary>
        /// Resolves a person field to exactly one active contact.
        ///
        /// <para>
        /// <b>Email first, name second</b> (project owner, 2026-09-20). An address identifies
        /// somebody; a display name describes them. Two people share a name far more often
        /// than they share a mailbox, so where a field carries both, the email decides and the
        /// name is only consulted when there is no email to go on.
        /// </para>
        /// <para>
        /// <b>Two rows are fetched, never one.</b> The second row is what proves the match was
        /// unambiguous - <c>TopCount 1</c> would return the first of two J Smiths and look
        /// certain. The same applies to an email, which is unique by convention rather than by
        /// constraint: nothing in Dataverse stops two contacts carrying one address.
        /// </para>
        /// <para>
        /// Every failure carries a sentence naming the value that failed, because these are
        /// read by a person looking at an import report rather than by code.
        /// </para>
        /// </summary>
        public static PersonMatch MatchPerson(
            IOrganizationService service, string email, string name, string role)
        {
            var label = string.IsNullOrWhiteSpace(role) ? "person" : role;
            var byEmail = (email ?? string.Empty).Trim();
            var byName = (name ?? string.Empty).Trim();

            if (byEmail.Length == 0 && byName.Length == 0)
            {
                return new PersonMatch
                {
                    Kind = PersonMatchKind.NoName,
                    Reason = "The row names no " + label
                        + ", so nothing can be sent about this case.",
                };
            }

            var attribute = byEmail.Length > 0 ? "emailaddress1" : "fullname";
            var value = byEmail.Length > 0 ? byEmail : byName;

            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("emailaddress1", ContactRegistry.StaffCodeAttr),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var matches = service.RetrieveMultiple(query).Entities;

            if (matches.Count == 0)
            {
                return new PersonMatch
                {
                    Kind = PersonMatchKind.NoContact,
                    Reason = byEmail.Length > 0
                        ? "No active contact holds the " + label + " email \"" + value + "\"."
                        : "No active contact is named \"" + value + "\".",
                };
            }

            if (matches.Count > 1)
            {
                return new PersonMatch
                {
                    Kind = PersonMatchKind.Ambiguous,
                    Reason = byEmail.Length > 0
                        ? "Two or more active contacts hold the email \"" + value
                            + "\", so no notification can be addressed."
                        : "Two or more active contacts are named \"" + value
                            + "\", so no notification can be addressed.",
                };
            }

            var found = matches[0].GetAttributeValue<string>("emailaddress1");
            if (string.IsNullOrWhiteSpace(found))
            {
                return new PersonMatch
                {
                    Kind = PersonMatchKind.NoEmail,
                    Reason = "The contact named \"" + value + "\" has no work email.",
                };
            }

            return new PersonMatch
            {
                Kind = PersonMatchKind.Matched,
                Email = found,
                Contact = matches[0].ToEntityReference(),
                StaffCode = matches[0].GetAttributeValue<string>(ContactRegistry.StaffCodeAttr),
            };
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

        /// <summary>
        /// The BR-005 grade a review recorded, or null when it recorded none.
        ///
        /// Read from the al_outcome row for that review rather than for the case, which is
        /// what keeps a two-leg case honest: a Tax leg records no grade, and reading "the
        /// case's outcome" on a Tax-then-AQS case would hand the Tax remediation whichever
        /// leg's row came back first.
        /// </summary>
        public static int? InitialOutcome(IOrganizationService service, EntityReference review)
        {
            if (review == null)
            {
                return null;
            }

            var query = new QueryExpression("al_outcome")
            {
                ColumnSet = new ColumnSet("al_initialoutcome"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, review.Id);

            var rows = service.RetrieveMultiple(query).Entities;
            if (rows.Count == 0)
            {
                return null;
            }

            var grade = rows[0].GetAttributeValue<OptionSetValue>("al_initialoutcome");
            return grade == null ? (int?)null : grade.Value;
        }

        /// <summary>
        /// A portal link to one case, or null when this environment has no site to link into.
        ///
        /// The domain is read from the Power Pages site row rather than from configuration.
        /// Power Pages already records it per environment, so the link is right in DEV, TEST
        /// and PROD with nothing to set — and, more to the point, a solution promoted from
        /// DEV cannot carry DEV's URL into PROD, which is the failure a copied setting has
        /// and the one that would send a checker to the wrong environment's case.
        ///
        /// Null rather than a bare path when there is no site or no domain. An email that
        /// says "/case-details?id=..." is worse than one that says nothing: it looks like a
        /// link, and the reader spends their time working out why it does not work.
        /// </summary>
        public static string CaseLink(IOrganizationService service, EntityReference outcomeCase)
        {
            // Delegated to PortalSite, which knows that the site row cannot answer this on any
            // environment but DEV: powerpagesite is a managed solution component, so it carries
            // DEV's domain wherever the solution is imported. Every letter sent from TEST
            // linked into DEV until 2026-09-22 for exactly that reason.
            return outcomeCase == null ? null : PortalSite.CaseLink(service, outcomeCase.Id);
        }
    }
}
