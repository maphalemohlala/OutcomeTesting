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

        // Who signed, read back from the row SignoffRequestPlugin stamped. Named there so
        // the writer and the reader cannot drift apart on the column name.
        private const string SignedByIdAttr = SignoffRequestPlugin.SignedByIdAttr;
        private const string SignedByNameAttr = SignoffRequestPlugin.SignedByNameAttr;
        private const string ActionLookup = "al_remediationactionid";
        private const string CaseLookup = "al_outcomecaseid";
        private const string ReviewLookup = "al_reviewinstanceid";
        private const string FinalOutcomeAttr = "al_finaloutcome";

        private const int StatusInProgress = Remediation.StatusInProgress;
        public const int DecisionApprovedValue = 120910720;
        public const int DecisionRejectedValue = 120910721;

        private const int CommandSignOffRemediation = 120910757;

        public SignoffProgressPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SignoffProgressPlugin))
        {
        }

        /// <summary>
        /// The Audit Event key for one sign-off row - its own id, so each decision on an
        /// action is recorded once and a second decision is not mistaken for a replay.
        /// </summary>
        public static string ReplayKeyFor(Guid signoffId)
        {
            return "signoff-" + signoffId.ToString("N");
        }

        /// <summary>
        /// The created row's id. The post-operation target carries it; the pipeline's "id"
        /// output is the fallback; and a row with neither falls back to the action, so the
        /// guard still holds rather than lapsing.
        /// </summary>
        private static Guid SignoffId(IPluginExecutionContext context, Entity signoff, EntityReference actionRef)
        {
            if (signoff.Id != Guid.Empty)
            {
                return signoff.Id;
            }

            object created;
            if (context.OutputParameters.TryGetValue("id", out created)
                && created is Guid && (Guid)created != Guid.Empty)
            {
                return (Guid)created;
            }

            return actionRef.Id;
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

            // A replay writes no second Audit Event (NFR-REL-01). Keyed on the sign-off row,
            // not the action: an action that is rejected and later approved is genuinely two
            // decisions (SignoffGuardPlugin.AlreadySettled lets the second one in), and a key
            // derived from the action found the rejection's event and returned here before
            // the approval could move the case, record the outcome or write its own event -
            // the IO-300003 deadlock moved one plug-in along. A platform retry of the same
            // create carries the same row id, so the replay guard still holds.
            var idempotencyKey = ReplayKeyFor(SignoffId(context, signoff, actionRef));
            if (CommandHelpers.FindAuditByKey(service, idempotencyKey, CommandSignOffRemediation) != null)
            {
                return;
            }

            if (decision.Value == DecisionRejectedValue)
            {
                service.Update(ReopenedAction(actionRef.Id, DateTime.UtcNow));
            }

            var reviewId = ResolveReview(service, actionRef);
            var caseId = ResolveCase(service, signoff, actionRef);
            if (caseId.HasValue)
            {
                MoveCase(service, caseId.Value, decision.Value, reviewId);
                RecordFinalOutcome(service, context, signoff, caseId.Value, reviewId);
            }

            // One Audit Event per decision. A create raised by the al_SignOffRemediation
            // command is audited by that command, under the caller's own idempotency key;
            // writing a second event here recorded the same decision twice (BR-012 wants an
            // immutable record, not a duplicated one). The consequences above still run for
            // both doors — that is the point of them living here.
            if (!CommandHelpers.IsWithinMessage(context, SignOffRemediationPlugin.MessageName))
            {
                // The signatory, not the caller. A portal write reaches Dataverse as the
                // site's application user (AD-053), so leaving WriteAuditEvent to fall back
                // to context.InitiatingUserId recorded every portal sign-off against
                // "# PowerPages Data Runtime PROD" - the one question an audit trail exists
                // to answer, unanswered. SignoffRequestPlugin stamps the contact on the row
                // for exactly this, and CompleteRequestPlugin has always named its contact
                // the same way. Falls back to the caller where the row names no signatory,
                // which is every sign-off written before those columns existed.
                var signedByName = signoff.GetAttributeValue<string>(SignedByNameAttr);
                var actorId = SignatoryId(signoff);

                CommandHelpers.WriteAuditEvent(
                    service,
                    CommandSignOffRemediation,
                    "SignOffRemediation " + actionRef.Id.ToString("D"),
                    ActionEntity,
                    actionRef.Id,
                    signoff.GetAttributeValue<string>(NotesAttr),
                    DescribeSignoff(decision.Value, signedByName),
                    idempotencyKey,
                    context,
                    actorId,
                    signedByName);
            }

            QueueSignoffNotification(service, context, signoff, actionRef, decision.Value);

            // Read AFTER MoveCase and RecordFinalOutcome, so this asks where the case actually
            // ended up rather than predicting it.
            if (caseId.HasValue && ParkedAtRecheck(service, caseId.Value, decision.Value))
            {
                QueueRecheckDue(service, context, signoff, caseId.Value);
            }
        }

        /// <summary>
        /// Whether this approval has left the case waiting at the recheck with nothing to
        /// finish it (project owner, 2026-09-14).
        ///
        /// "Leave for a separate regrade" is a legitimate choice on the sign-off panel, and
        /// the case is meant to wait at Awaiting Recheck when it is made. What was wrong is
        /// that it waited <b>silently</b>: <see cref="RecordFinalOutcome"/> returns at its
        /// first gate when the sign-off carries no grade, so nothing closed the case and
        /// nothing told anyone it was theirs to close. Case 254398988 sat there from 17:35Z
        /// until the project owner noticed it the next morning, and the export collects Closed
        /// cases only, so it had not reached Trail Light either.
        ///
        /// Asked of the case as it now stands rather than of the sign-off row, which is what
        /// makes this right in every branch <see cref="MoveCase"/> has: a graded approval has
        /// already Closed the case, a Tax leg that still owes AQS has gone back to Queued, and
        /// a Tax-only case with no Outcome was closed without a recheck. Only the parked case
        /// is still sitting at Awaiting Recheck by the time this runs.
        /// </summary>
        public static bool ParkedAtRecheck(IOrganizationService service, Guid caseId, int decision)
        {
            if (decision != DecisionApprovedValue)
            {
                return false;
            }

            return CaseTransitions.CurrentStatus(service, caseId) == CaseLifecycle.AwaitingRecheck;
        }

        /// <summary>The notice's body. Public so its wording is pinned by a test.</summary>
        public static string RecheckDueBody(string reference)
        {
            return "The remediation on case " + reference + " is approved and the case is now at "
                + "Awaiting Recheck. It is waiting for you to record the final outcome, which is "
                + "what closes it - until then it stays open and does not reach the export. "
                + "Open the case's remediation page and use Record the final outcome.";
        }

        /// <summary>
        /// Tells the signatory that the case they just approved is now theirs to close.
        ///
        /// Sent to the person who signed off rather than to the adviser: recording the final
        /// outcome is the T&amp;C Supervisor's step (RegradeRequestPlugin checks that role), and
        /// the adviser's own notification already tells them the case moved on to recheck.
        ///
        /// Keyed on the case rather than the sign-off, because a check signs off one action at
        /// a time and every one of them would otherwise queue this - the supervisor wants one
        /// reminder that a case is waiting, not eighteen. The alternate key collapses them.
        ///
        /// A sign-off row that names no signatory still queues the row, with no recipient: the
        /// drain refuses to send without an address, and a row a person can see in the outbox
        /// is better than the silence this exists to end.
        /// </summary>
        private static void QueueRecheckDue(
            IOrganizationService service,
            IPluginExecutionContext context,
            Entity signoff,
            Guid caseId)
        {
            var caseRef = new EntityReference("al_outcomecase", caseId);
            var reference = NotificationOutbox.CaseReference(service, caseRef) ?? "a case";

            var signatory = SignatoryId(signoff);
            var email = signatory.HasValue
                ? NotificationOutbox.ContactEmail(service, new EntityReference("contact", signatory.Value))
                : null;

            NotificationOutbox.Queue(
                service,
                context,
                NotificationOutbox.EventRecheckDue,
                "al_outcomecase",
                caseId,
                email,
                "Case " + reference + " is waiting for its final outcome",
                RecheckDueBody(reference));
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

            // What approval now means depends on whether the supervisor graded as they
            // approved (project owner, 2026-09-11). Where they did, the case is finished and
            // saying it "moved on to recheck" would send the adviser looking for a step that
            // is not coming; where they did not, it is still waiting on one.
            var finalOutcome = signoff.GetAttributeValue<OptionSetValue>(FinalOutcomeAttr);

            var body = approved
                ? finalOutcome == null
                    ? "Your remediation on case " + reference + " has been approved and the case has moved on to recheck."
                    : "Your remediation on case " + reference + " has been approved, and the case is now closed with a final outcome of "
                        + RegradeCasePlugin.FinalOutcomeLabel(finalOutcome.Value) + "."
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
        /// An action counts as decided once an <b>approved</b> sign-off exists against it.
        /// It used to count any row, "because the guard refuses a second one" - and it did,
        /// until 2026-09-12, when SignoffGuardPlugin.AlreadySettled started letting a rejected
        /// action be decided again once reworked. From then a reworked action still carrying
        /// its old rejection row read as decided here, so approving the other actions on the
        /// check moved the case on with that one never approved. A rejection is not a
        /// decision that ends anything: it returns the case to Awaiting Remediation at once,
        /// which is the whole check going back, and the action comes back for a real one.
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
            signoffs.Criteria.AddCondition(DecisionAttr, ConditionOperator.Equal, DecisionApprovedValue);

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
        /// <summary>
        /// Writes the final outcome the supervisor recorded as they approved, which closes
        /// the case (project owner, 2026-09-11, settling OD-041).
        ///
        /// Approving used to leave the case at Awaiting Recheck for a separate regrade, and
        /// nothing prompted anyone to do it - so a remediated case sat there, still showing
        /// the grade it was given before the adviser put anything right, and never reached
        /// Closed. The export collects Closed cases only, as
        /// <see cref="RegradeCasePlugin.CloseAfterRecheck"/> records, so it never reached
        /// Trail Light either.
        ///
        /// <b>Run after <see cref="MoveCase"/> and gated on the status it left behind.</b> A
        /// check raises one action per thing marked down and the page signs them off one by
        /// one, each carrying the same grade; only the last one clears
        /// <see cref="AnyAwaitingSignoff"/> and reaches Awaiting Recheck. Asking the case
        /// where it is now is therefore what makes this happen once, without this having to
        /// count sign-offs a second time.
        ///
        /// The regrade itself is <see cref="RegradeCasePlugin.Regrade"/> - the same method
        /// the Code App's al_RegradeCase and the portal's regrade panel call (AD-111), so a
        /// grade set here is written, audited and closed exactly as one set there is.
        /// </summary>
        private static void RecordFinalOutcome(
            IOrganizationService service,
            IPluginExecutionContext context,
            Entity signoff,
            Guid caseId,
            Guid? reviewId)
        {
            var finalOutcome = signoff.GetAttributeValue<OptionSetValue>(FinalOutcomeAttr);
            if (finalOutcome == null)
            {
                return;
            }

            // Anything else means this was not the sign-off that finished the check: an
            // earlier one of the set, a rejection, or a Tax leg handing back to the queue.
            if (CaseTransitions.CurrentStatus(service, caseId) != CaseLifecycle.AwaitingRecheck)
            {
                return;
            }

            var outcomeId = OutcomeFor(service, caseId, reviewId);
            if (outcomeId == Guid.Empty)
            {
                // No graded outcome to override - a remediated Tax-only case has no
                // al_outcome row at all (AD-055). MoveCase has already closed that one.
                return;
            }

            RegradeCasePlugin.Regrade(
                service,
                service,
                outcomeId,
                RegradeCasePlugin.FinalOutcomeLabel(finalOutcome.Value),
                signoff.GetAttributeValue<string>(NotesAttr),
                null,
                "signoff-regrade-" + outcomeId.ToString("N"),
                context);
        }

        /// <summary>
        /// The Outcome the check being signed off recorded: the one for this review where the
        /// sign-off knows which review it was, and the case's otherwise.
        ///
        /// Scoped to the review because a Tax-then-AQS case can carry an Outcome per leg, and
        /// regrading whichever came back first would put the AQS supervisor's grade on the
        /// Tax check (the reason NotificationOutbox.InitialOutcome reads by review too).
        /// </summary>
        private static Guid OutcomeFor(IOrganizationService service, Guid caseId, Guid? reviewId)
        {
            var query = new QueryExpression("al_outcome")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };

            if (reviewId.HasValue)
            {
                query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId.Value);
            }
            else
            {
                query.Criteria.AddCondition(CaseLookup, ConditionOperator.Equal, caseId);
            }

            var rows = service.RetrieveMultiple(query).Entities;
            return rows.Count == 0 ? Guid.Empty : rows[0].Id;
        }

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

        /// <summary>
        /// The audit event's details line: the decision and who made it.
        ///
        /// The sign-off's own GUID used to close this sentence ("Sign-off 3a8e654a-52af-..."),
        /// which told a reader nothing they could act on - the event already carries the
        /// target id in its own column, and a person reading a case history wants the name.
        /// Naming the signatory is also what makes this line legible on the rows where the
        /// actor columns are the fallback rather than the contact.
        ///
        /// Falls back to the bare decision where the row names no signatory, which is every
        /// sign-off written before those columns existed. Deliberately not the GUID: an
        /// unnamed signatory reads better as unnamed than as sixteen bytes of hex.
        /// </summary>
        /// <summary>
        /// The contact who signed, from the row, or null where it names none.
        ///
        /// Null rather than Guid.Empty so the caller falls through to WriteAuditEvent's own
        /// default - the initiating user - which is the right answer for every sign-off
        /// written before the signatory columns existed. A malformed id is treated as absent
        /// for the same reason: an audit event attributed to nobody is worse than one
        /// attributed to the caller, and refusing the sign-off outright over a bad string
        /// would lose a decision the supervisor has already made.
        /// </summary>
        public static Guid? SignatoryId(Entity signoff)
        {
            if (signoff == null)
            {
                return null;
            }

            Guid parsed;
            return Guid.TryParse(signoff.GetAttributeValue<string>(SignedByIdAttr), out parsed)
                && parsed != Guid.Empty
                ? parsed
                : (Guid?)null;
        }

        public static string DescribeSignoff(int decision, string signedByName)
        {
            var text = "Signed off from the portal: " + DescribeDecision(decision);
            return string.IsNullOrWhiteSpace(signedByName)
                ? text + "."
                : text + " by " + signedByName.Trim() + ".";
        }
    }
}
