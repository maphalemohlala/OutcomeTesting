using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The consequences of a sign-off (BR-008, FR-023). Registered as a synchronous
    /// post-operation step on Create of <c>al_signoff</c>, so it runs only once the
    /// sign-off has actually been written.
    ///
    /// Split from <see cref="SignoffGuardPlugin"/> for the reason AD-053 split the response
    /// guard from the progress plug-in: validation must run before the row exists and
    /// consequences must run after it, and one step cannot be both.
    ///
    /// A rejection reopens the action so the adviser reworks it. An approval leaves the
    /// action Completed and moves the case on through the AD-057 lifecycle. Either way an
    /// immutable Audit Event records who decided what (BR-012, NFR-AUD-01).
    /// </summary>
    public class SignoffProgressPlugin : PluginBase
    {
        private const string SignoffEntity = "al_signoff";
        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";
        private const string ClockStartedOnAttr = "al_clockstartedon";
        private const string DecisionAttr = "al_signoffdecision";
        private const string NotesAttr = "al_notes";
        private const string ActionLookup = "al_remediationactionid";
        private const string CaseLookup = "al_outcomecaseid";

        private const int StatusInProgress = 120910601;
        private const int DecisionApprovedValue = 120910720;
        private const int DecisionRejectedValue = 120910721;

        private const int CommandSignOffRemediation = 120910757;

        public SignoffProgressPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SignoffProgressPlugin))
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

            var signoff = target as Entity;
            if (signoff == null || !string.Equals(signoff.LogicalName, SignoffEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var decision = signoff.GetAttributeValue<OptionSetValue>(DecisionAttr);
            var actionRef = signoff.GetAttributeValue<EntityReference>(ActionLookup);
            if (decision == null || actionRef == null)
            {
                return;
            }

            // A replay writes no second Audit Event: the key is derived from the action, and
            // the guard has already refused a second sign-off on the same one (NFR-REL-01).
            var idempotencyKey = "signoff-" + actionRef.Id.ToString("N");
            if (CommandHelpers.FindAuditByKey(service, idempotencyKey, CommandSignOffRemediation) != null)
            {
                return;
            }

            if (decision.Value == DecisionRejectedValue)
            {
                service.Update(ReopenedAction(actionRef.Id, DateTime.UtcNow));
            }
            else if (decision.Value == DecisionApprovedValue)
            {
                AdvanceCase(service, signoff, actionRef);
            }

            CommandHelpers.WriteAuditEvent(
                service,
                CommandSignOffRemediation,
                "SignOffRemediation " + actionRef.Id.ToString("D"),
                ActionEntity,
                actionRef.Id,
                signoff.GetAttributeValue<string>(NotesAttr),
                "Signed off from the portal: " + DescribeDecision(decision.Value) + ". Sign-off " + signoff.Id.ToString("D"),
                idempotencyKey,
                context);

            QueueSignoffNotification(service, context, signoff, actionRef, decision.Value);
        }

        /// <summary>
        /// Tells the adviser what happened to their remediation (PP-15, AD-035). Queued in
        /// the same transaction as the decision, so a sign-off that rolls back takes its
        /// notification with it and one that commits cannot be silently unannounced.
        ///
        /// Keyed on the sign-off rather than the action, because an action that goes round
        /// twice is genuinely two decisions and the adviser needs to hear about both — key
        /// it on the action and the second rejection would collide with the first and never
        /// be sent.
        /// </summary>
        private static void QueueSignoffNotification(
            IOrganizationService service,
            IPluginExecutionContext context,
            Entity signoff,
            EntityReference actionRef,
            int decision)
        {
            var approved = decision == DecisionApprovedValue;
            var action = service.Retrieve(ActionEntity, actionRef.Id,
                new ColumnSet("al_assignedcontactid", CaseLookup));

            var caseRef = signoff.GetAttributeValue<EntityReference>(CaseLookup)
                ?? action.GetAttributeValue<EntityReference>(CaseLookup);
            var reference = NotificationOutbox.CaseReference(service, caseRef) ?? "a case";
            var email = NotificationOutbox.ContactEmail(service, action.GetAttributeValue<EntityReference>("al_assignedcontactid"));
            var notes = signoff.GetAttributeValue<string>(NotesAttr);

            var body = approved
                ? "Your remediation on case " + reference + " has been approved and the case has moved on to recheck."
                : "Your remediation on case " + reference + " has been sent back for further work. "
                    + "The ten-working-day clock has restarted from today (OD-018).";

            if (!string.IsNullOrWhiteSpace(notes))
            {
                body += " Notes: " + notes;
            }

            NotificationOutbox.Queue(
                service,
                context,
                approved ? NotificationOutbox.EventSignoffApproved : NotificationOutbox.EventSignoffRejected,
                SignoffEntity,
                signoff.Id,
                email,
                (approved ? "Remediation approved on case " : "Remediation sent back on case ") + reference,
                body);
        }

        /// <summary>
        /// The action as a rejection leaves it: back with the adviser, and on a fresh
        /// BR-010 clock (OD-018). The reset is what makes the previous period a period
        /// rather than part of one long age — <c>createdon</c> keeps the original start, so
        /// createdon-to-clockStartedOn is the timer that just ended and clockStartedOn-to-now
        /// is the one now running. A case that goes round twice therefore reads as two
        /// timers, and the ten-working-day threshold applies to the current one.
        ///
        /// The status is set explicitly rather than left alone because a rejected action
        /// has already been through Completed, and rework has to be visible as in progress.
        /// </summary>
        public static Entity ReopenedAction(Guid actionId, DateTime now)
        {
            return new Entity(ActionEntity, actionId)
            {
                [ActionStatus] = new OptionSetValue(StatusInProgress),
                [ClockStartedOnAttr] = now,
            };
        }

        /// <summary>
        /// An approved remediation sends the case on to recheck. The hop is checked against
        /// AD-057 rather than assumed, so a case that is not where the lifecycle expects is
        /// refused instead of jumping.
        /// </summary>
        private static void AdvanceCase(IOrganizationService service, Entity signoff, EntityReference actionRef)
        {
            var caseRef = signoff.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef == null)
            {
                var action = service.Retrieve(ActionEntity, actionRef.Id, new ColumnSet(CaseLookup));
                caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);
            }

            if (caseRef == null)
            {
                return;
            }

            var current = CaseTransitions.CurrentStatus(service, caseRef.Id);
            if (current.HasValue && current.Value == CaseLifecycle.AwaitingSignoff)
            {
                CaseTransitions.MoveThrough(service, caseRef.Id, CaseLifecycle.AwaitingRecheck);
            }
        }

        private static string DescribeDecision(int decision)
        {
            return decision == DecisionApprovedValue ? "Approved" : "Rejected";
        }
    }
}
