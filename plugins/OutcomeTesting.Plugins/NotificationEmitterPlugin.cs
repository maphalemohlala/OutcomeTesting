using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Queues the PP-15 notifications that follow a record being created (AD-035, OD-030).
    /// Registered as a synchronous post-operation step on Create of:
    ///
    /// - <c>al_caseassignment</c> -> Allocation
    /// - <c>al_remediationaction</c> -> Remediation assigned
    ///
    /// Hung off the create rather than off the commands, deliberately. Allocation reaches
    /// al_caseassignment by three routes — al_AssignCase (AD-072), the portal self-claim
    /// (AD-076) and a manager writing the row directly — and remediation actions are not
    /// created by any plug-in at all, so a command-side emitter would miss every one of
    /// them. A post-operation step on the table catches the event however it happened,
    /// which is the only version of this that cannot silently under-notify.
    ///
    /// Post-operation, so the row exists and its lookups resolve; synchronous, so the
    /// notification shares the transaction that created the record (the outbox guarantee).
    /// </summary>
    public class NotificationEmitterPlugin : PluginBase
    {
        private const string AssignmentEntity = "al_caseassignment";
        private const string ActionEntity = "al_remediationaction";
        private const string CaseLookup = "al_outcomecaseid";

        public NotificationEmitterPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(NotificationEmitterPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            object target;
            if (!context.InputParameters.TryGetValue("Target", out target))
            {
                return;
            }

            var record = target as Entity;
            if (record == null)
            {
                return;
            }

            // The created row carries only the columns the caller sent, so anything the
            // notification needs is read back rather than assumed present.
            if (string.Equals(record.LogicalName, AssignmentEntity, StringComparison.OrdinalIgnoreCase))
            {
                QueueAllocation(service, context.CorrelationId, record.Id);
                return;
            }

            if (string.Equals(record.LogicalName, ActionEntity, StringComparison.OrdinalIgnoreCase))
            {
                QueueRemediationAssigned(service, context.CorrelationId, record.Id);
            }
        }

        /// <summary>
        /// Tells the allocated checker the case is theirs. Called on the create, and again
        /// by <see cref="AssignCasePlugin.AllocateAssignment"/> when a check is allocated
        /// back to someone who held it before - that reuses their row rather than writing a
        /// second one, so no create fires and the wording would otherwise live twice.
        ///
        /// The outbox code carries <c>al_assignedon</c>, so each allocation of the same row
        /// is its own notification: a reallocation is told, a replay is not.
        /// </summary>
        internal static void QueueAllocation(IOrganizationService service, Guid correlationId, Guid assignmentId)
        {
            var assignment = service.Retrieve(AssignmentEntity, assignmentId,
                new ColumnSet("al_assigneduserid", "al_assignedcontactid", CaseLookup, "al_isactive", "al_assignedon"));

            // A released or inactive row is history, not an allocation to tell anyone about.
            var active = assignment.GetAttributeValue<bool?>("al_isactive");
            if (active.HasValue && !active.Value)
            {
                return;
            }

            var caseRef = assignment.GetAttributeValue<EntityReference>(CaseLookup);
            var reference = NotificationOutbox.CaseReference(service, caseRef) ?? "a case";

            // The assigned Dataverse user is who actually does the work; the contact is the
            // portal identity of the same person on the self-claim path (AD-076).
            var email = NotificationOutbox.UserEmail(service, assignment.GetAttributeValue<EntityReference>("al_assigneduserid"))
                ?? NotificationOutbox.ContactEmail(service, assignment.GetAttributeValue<EntityReference>("al_assignedcontactid"));

            // "Open it in the portal" is an instruction, not a way in: the recipient still
            // has to find the site, sign in and search for the reference the email just gave
            // them. Where the environment has a portal, the email carries the case itself.
            var link = NotificationOutbox.CaseLink(service, caseRef);
            var body = link == null
                ? "Case " + reference + " is now assigned to you for checking. Open it in the portal to start the review."
                : "Case " + reference + " is now assigned to you for checking. Open it to start the review: " + link;

            var assignedOn = assignment.GetAttributeValue<DateTime?>("al_assignedon");

            NotificationOutbox.Queue(
                service,
                correlationId,
                NotificationOutbox.EventAllocation,
                AssignmentEntity,
                assignmentId,
                email,
                "Case " + reference + " has been allocated to you",
                body,
                assignedOn.HasValue
                    ? assignedOn.Value.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture)
                    : null);
        }

        /// <summary>
        /// Tells the assigned adviser a remediation action is theirs. Called on the create,
        /// and again by <see cref="Remediation.AssignUnassignedActions"/> when an action
        /// raised unassigned is later assigned — the adviser hears about it either way, and
        /// the wording lives once.
        /// </summary>
        internal static void QueueRemediationAssigned(IOrganizationService service, Guid correlationId, Guid actionId)
        {
            var action = service.Retrieve(ActionEntity, actionId,
                new ColumnSet("al_assignedcontactid", CaseLookup, "al_name", "al_duedate", "al_reviewinstanceid"));

            // Keyed on the review rather than on the action, so a submission that raises one
            // action per thing the checker marked down (2026-09-10) sends one email and not
            // one per item: the outbox code is derived from the target, so the second and
            // later actions resolve to a code the outbox already holds. A later review on the
            // same case is a different target, so it still tells the adviser.
            //
            // An action with no review behind it falls back to itself, which is the behaviour
            // every action had before.
            var reviewRef = action.GetAttributeValue<EntityReference>("al_reviewinstanceid");
            var targetTable = reviewRef == null ? ActionEntity : reviewRef.LogicalName;
            var targetId = reviewRef == null ? actionId : reviewRef.Id;

            // Asked before the body is built, not after. Everything below costs a round trip
            // apiece - the case, the contact, the grade, the portal domain - and on a review
            // that raised one action per marked-down item every action after the first would
            // pay all of them only for Queue to find the row already there and discard the
            // lot. The code depends on nothing but the action just read, so the cheap question
            // can be asked first.
            if (NotificationOutbox.AlreadyQueued(
                service, NotificationOutbox.EventRemediationAssigned, targetId, null))
            {
                return;
            }

            var caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);

            // One read of the case, not two: the reference for the subject line and the names
            // the letter opens with come back together.
            var caseRow = caseRef == null
                ? null
                : service.Retrieve("al_outcomecase", caseRef.Id,
                    new ColumnSet("al_casereference", "al_advisername", "al_clientname"));

            var reference = (caseRow == null ? null : caseRow.GetAttributeValue<string>("al_casereference"))
                ?? "a case";
            var email = NotificationOutbox.ContactEmail(service, action.GetAttributeValue<EntityReference>("al_assignedcontactid"));

            var due = action.GetAttributeValue<DateTime?>("al_duedate");
            var dueText = due.HasValue
                ? " It is due by " + due.Value.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + "."
                : string.Empty;

            // The letter the grading earned (project owner, 2026-09-10). Null for a grading
            // the supplied copy was not written for - a flagged Pass, or a Tax leg, which
            // records no BR-005 grade at all - and those keep the wording they already had
            // rather than being told they received a grading they did not.
            var grade = NotificationOutbox.InitialOutcome(service, reviewRef);

            var subject = NotificationBodies.RemediationSubject(grade, reference);
            var body = NotificationBodies.Remediation(
                grade,
                caseRow == null ? null : caseRow.GetAttributeValue<string>("al_advisername"),
                caseRow == null ? null : caseRow.GetAttributeValue<string>("al_clientname"),
                NotificationOutbox.CaseLink(service, caseRef),
                dueText);

            if (subject == null || body == null)
            {
                subject = "Remediation required on case " + reference;
                body = "Remediation has been raised against case " + reference
                    + " and assigned to you (BR-006)." + dueText
                    + " Record your response against each item in the portal.";
            }

            NotificationOutbox.Queue(
                service,
                correlationId,
                NotificationOutbox.EventRemediationAssigned,
                targetTable,
                targetId,
                email,
                subject,
                body);
        }

        /// <summary>
        /// Tells the adviser their case was checked and passed, and that nothing is owed
        /// (project owner, 2026-09-10). Called from <see cref="SubmitReviewPlugin"/> on a
        /// submit that closes the case having raised no remediation - the test itself is
        /// <see cref="OutcomeRules.EarnsPassNotification"/>, which is where the reasoning
        /// for it lives.
        ///
        /// Public, unlike the two above, because its caller's success path is not reachable
        /// in a unit test without standing up a whole checklist, and which adviser is told
        /// their case passed is worth holding directly rather than through a fixture.
        ///
        /// Keyed on the case, so a replayed submit finds the row already queued rather than
        /// sending the letter twice.
        /// </summary>
        public static void QueueCasePassed(IOrganizationService service, Guid correlationId, EntityReference caseRef)
        {
            if (caseRef == null)
            {
                return;
            }

            var caseRow = service.Retrieve("al_outcomecase", caseRef.Id,
                new ColumnSet("al_casereference", "al_advisername", "al_clientname"));

            var reference = caseRow.GetAttributeValue<string>("al_casereference");

            // The adviser is named on the case as text; AdviserContact is what turns that
            // into an address, and it declines rather than guess between two of the same
            // name. An unmatched adviser still queues the row - see Queue.
            var email = NotificationOutbox.ContactEmail(service, Remediation.AdviserContact(service, caseRef));

            NotificationOutbox.Queue(
                service,
                correlationId,
                NotificationOutbox.EventCasePassed,
                "al_outcomecase",
                caseRef.Id,
                email,
                NotificationBodies.PassSubject(reference),
                NotificationBodies.Pass(
                    caseRow.GetAttributeValue<string>("al_advisername"),
                    caseRow.GetAttributeValue<string>("al_clientname"),
                    NotificationOutbox.CaseLink(service, caseRef)));
        }
    }
}
