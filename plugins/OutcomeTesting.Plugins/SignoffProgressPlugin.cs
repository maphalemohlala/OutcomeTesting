using System;
using System.Collections.Generic;
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
        private const string ReviewLookup = "al_reviewinstanceid";

        private const int StatusInProgress = Remediation.StatusInProgress;
        public const int DecisionApprovedValue = 120910720;
        public const int DecisionRejectedValue = 120910721;

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

            var caseId = ResolveCase(service, signoff, actionRef);
            if (caseId.HasValue)
            {
                MoveCase(service, caseId.Value, decision.Value, ResolveReview(service, actionRef));
            }

            // One Audit Event per decision. A create raised by the al_SignOffRemediation
            // command is audited by that command, under the caller's own idempotency key;
            // writing a second event here recorded the same decision twice (BR-012 wants an
            // immutable record, not a duplicated one). The consequences above still run for
            // both doors — that is the point of them living here.
            if (!CommandHelpers.IsWithinMessage(context, SignOffRemediationPlugin.MessageName))
            {
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
            }

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
        /// Whether an action on this check is still waiting for a decision.
        ///
        /// An action counts as decided once a sign-off row exists against it - approved or
        /// rejected - because the guard refuses a second one, so the row is the decision. A
        /// rejection never reaches this test: it returns the case to Awaiting Remediation
        /// immediately, which is the whole check going back.
        ///
        /// Scoped to the review instance when the action carries one. Rows written before
        /// that link existed carry none, and gating those on nothing would restore the
        /// behaviour this exists to stop, so the case is the scope instead.
        /// </summary>
        public static bool AnyAwaitingSignoff(IOrganizationService service, Guid caseId, Guid? reviewId)
        {
            var actions = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression(),
            };
            actions.Criteria.AddCondition(CaseLookup, ConditionOperator.Equal, caseId);
            if (reviewId.HasValue)
            {
                actions.Criteria.AddCondition(ReviewLookup, ConditionOperator.Equal, reviewId.Value);
            }

            var undecided = new HashSet<Guid>();
            foreach (var action in service.RetrieveMultiple(actions).Entities)
            {
                undecided.Add(action.Id);
            }

            if (undecided.Count == 0)
            {
                return false;
            }

            // Matched on the actions themselves rather than on the case, because a sign-off
            // does not always carry the case lookup - ResolveCase above exists for that.
            var keys = new object[undecided.Count];
            var next = 0;
            foreach (var id in undecided)
            {
                keys[next++] = id;
            }

            var signoffs = new QueryExpression(SignoffEntity)
            {
                ColumnSet = new ColumnSet(ActionLookup),
                Criteria = new FilterExpression(),
            };
            signoffs.Criteria.AddCondition(ActionLookup, ConditionOperator.In, keys);

            foreach (var signoff in service.RetrieveMultiple(signoffs).Entities)
            {
                var decided = signoff.GetAttributeValue<EntityReference>(ActionLookup);
                if (decided != null)
                {
                    undecided.Remove(decided.Id);
                }
            }

            return undecided.Count > 0;
        }

        /// <summary>The check the signed-off action belongs to, or null where it carries none.</summary>
        private static Guid? ResolveReview(IOrganizationService service, EntityReference actionRef)
        {
            var action = service.Retrieve(ActionEntity, actionRef.Id, new ColumnSet(ReviewLookup));
            var review = action.GetAttributeValue<EntityReference>(ReviewLookup);
            return review == null ? (Guid?)null : review.Id;
        }

        /// <summary>The case the sign-off is about: from the sign-off itself, else from its action.</summary>
        private static Guid? ResolveCase(IOrganizationService service, Entity signoff, EntityReference actionRef)
        {
            var caseRef = signoff.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef == null)
            {
                var action = service.Retrieve(ActionEntity, actionRef.Id, new ColumnSet(CaseLookup));
                caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);
            }

            return caseRef == null ? (Guid?)null : caseRef.Id;
        }

        /// <summary>
        /// Where the decision leaves the case (AD-057, BR-008). A rejection returns it to
        /// Awaiting Remediation alongside the reopened action — the "returns with notes"
        /// step, which used to move the action and leave the case reading as though the
        /// T&amp;C Manager still held it. An approval goes one of three ways:
        ///
        /// - back to <b>Queued</b> when the route still owes an AQS review (OD-038, project
        ///   owner direction 2026-09-09: the case has to go through remediation before it can
        ///   reach AQS). The AQS checker then picks it up from the shared queue, and the AQS
        ///   submit accepts it because the Tax remediation is approved;
        /// - on to <b>Awaiting Recheck</b> where the case carries an Outcome, so the T&amp;C
        ///   Manager can set the final outcome (al_RegradeCase), which closes it;
        /// - through recheck to <b>Closed</b> where the case carries no Outcome at all — a
        ///   remediated Tax-only case (AD-055) has no final outcome to set, so a recheck
        ///   would wait for a decision nothing can record.
        ///
        /// Only a case AT Awaiting Sign-off is moved. The hops are checked against the
        /// lifecycle rather than assumed, so a case that is not where it expects is left
        /// alone instead of jumping.
        /// </summary>
        public static void MoveCase(IOrganizationService service, Guid caseId, int decision, Guid? reviewId = null)
        {
            var current = CaseTransitions.CurrentStatus(service, caseId);
            if (!current.HasValue || current.Value != CaseLifecycle.AwaitingSignoff)
            {
                return;
            }

            switch (decision)
            {
                case DecisionRejectedValue:
                    CaseTransitions.MoveThrough(service, caseId, CaseLifecycle.AwaitingRemediation);
                    return;

                case DecisionApprovedValue:
                    // The check's remediation is signed off as a whole, not action by action.
                    // A review raises one action per thing the checker marked down, so an AQS
                    // check can carry eighteen; without this the first approval moved the case
                    // and the other seventeen sign-offs found it already moved and did nothing.
                    // The completion side has the same gate one step earlier
                    // (CompleteRemediationPlugin.AnyOutstanding).
                    //
                    // Scoped to the check, so the two legs of a Tax-then-AQS route are separate
                    // remediations: a Tax action still to be decided is not this check's work.
                    if (AnyAwaitingSignoff(service, caseId, reviewId))
                    {
                        return;
                    }

                    if (SubmitReviewPlugin.AqsStillOwed(service, caseId))
                    {
                        CaseTransitions.MoveThrough(service, caseId, CaseLifecycle.Queued);
                        return;
                    }

                    CaseTransitions.MoveThrough(service, caseId, CaseLifecycle.AwaitingRecheck);
                    if (!SubmitReviewPlugin.HasOutcome(service, caseId))
                    {
                        CaseTransitions.MoveThrough(service, caseId, CaseLifecycle.Closed);
                    }

                    return;

                default:
                    return;
            }
        }

        private static string DescribeDecision(int decision)
        {
            return decision == DecisionApprovedValue ? "Approved" : "Rejected";
        }
    }
}
