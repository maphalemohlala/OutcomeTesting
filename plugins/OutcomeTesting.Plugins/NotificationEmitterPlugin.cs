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
                QueueAllocation(service, context, record.Id);
                return;
            }

            if (string.Equals(record.LogicalName, ActionEntity, StringComparison.OrdinalIgnoreCase))
            {
                QueueRemediationAssigned(service, context, record.Id);
            }
        }

        private static void QueueAllocation(IOrganizationService service, IPluginExecutionContext context, Guid assignmentId)
        {
            var assignment = service.Retrieve(AssignmentEntity, assignmentId,
                new ColumnSet("al_assigneduserid", "al_assignedcontactid", CaseLookup, "al_isactive"));

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

            NotificationOutbox.Queue(
                service,
                context,
                NotificationOutbox.EventAllocation,
                AssignmentEntity,
                assignmentId,
                email,
                "Case " + reference + " has been allocated to you",
                "Case " + reference + " is now assigned to you for checking. Open it in the portal to start the review.");
        }

        private static void QueueRemediationAssigned(IOrganizationService service, IPluginExecutionContext context, Guid actionId)
        {
            var action = service.Retrieve(ActionEntity, actionId,
                new ColumnSet("al_assignedcontactid", CaseLookup, "al_name", "al_duedate"));

            var caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);
            var reference = NotificationOutbox.CaseReference(service, caseRef) ?? "a case";
            var email = NotificationOutbox.ContactEmail(service, action.GetAttributeValue<EntityReference>("al_assignedcontactid"));

            var due = action.GetAttributeValue<DateTime?>("al_duedate");
            var dueText = due.HasValue
                ? " It is due by " + due.Value.ToString("d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + "."
                : string.Empty;

            NotificationOutbox.Queue(
                service,
                context,
                NotificationOutbox.EventRemediationAssigned,
                ActionEntity,
                actionId,
                email,
                "Remediation required on case " + reference,
                "A remediation action has been raised against case " + reference
                    + " and assigned to you (BR-006)." + dueText
                    + " Record your response in the portal.");
        }
    }
}
