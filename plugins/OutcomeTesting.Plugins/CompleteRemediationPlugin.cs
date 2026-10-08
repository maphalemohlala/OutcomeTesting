using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command CompleteRemediation (AD-003). Registered against the
    /// Custom API message <c>al_CompleteRemediation</c>. An adviser drives their own
    /// remediation action to Completed (BR-006, BR-008, FR-020..FR-023). The command
    /// enforces the caller, the transition guard, optimistic concurrency and
    /// idempotency, and writes an immutable Audit Event (BR-012, NFR-AUD-01).
    /// </summary>
    public class CompleteRemediationPlugin : PluginBase
    {
        // Custom API request parameters.
        private const string InTargetId = "TargetId";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        // Custom API response parameters.
        private const string OutStatus = CommandHelpers.OutStatus;
        private const string OutAuditEventId = CommandHelpers.OutAuditEventId;
        private const string OutConflict = CommandHelpers.OutConflict;

        // al_remediationaction.
        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";
        private const string ReviewLookup = "al_reviewinstanceid";
        private const string ActionCompletedOn = "al_completedon";
        private const string ActionAdviserResponse = "al_adviserresponse";
        // Values live on Remediation, which writes this column when an action is raised.
        private const int StatusOpen = Remediation.StatusOpen;
        private const int StatusInProgress = Remediation.StatusInProgress;
        private const int StatusCompleted = Remediation.StatusCompleted;

        // al_auditevent.
        private const string AuditEntity = "al_auditevent";
        private const int CommandCompleteRemediation = 120910756;

        // Distinct failure prefixes so the client can branch (command-concurrency skill).
        private const string ConflictPrefix = "CONFLICT: ";
        private const string UnauthorizedPrefix = "UNAUTHORIZED: ";
        private const string PreconditionPrefix = CommandHelpers.PreconditionPrefix;

        public CompleteRemediationPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CompleteRemediationPlugin))
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

            var targetId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);

            var result = Complete(
                service,
                targetId,
                idempotencyKey,
                expectedRowVersion,
                context.InitiatingUserId,
                context.CorrelationId,
                requireCallerOwnsAction: true,
                details: null,
                // The sign-off-due routing goes quiet on an unmapped adviser by design, and
                // says why in the trace log instead - but only if it is handed somewhere to
                // write (F41). Nothing passed one, so every explanation it had was discarded
                // and a manager who was never told had no way to find out why.
                trace: message => localPluginContext.Trace(message));

            CommandHelpers.SetResponse(context, result.Status, result.AuditEventId, result.Conflict);
        }

        /// <summary>The outcome of a completion, in the shape the Custom API responds with.</summary>
        public sealed class CompleteResult
        {
            public string Status { get; set; }

            public Guid AuditEventId { get; set; }

            public bool Conflict { get; set; }
        }

        /// <summary>
        /// The whole completion (BR-006, BR-008, FR-020..FR-023), shared by the
        /// <c>al_CompleteRemediation</c> Custom API and the portal's update-triggered path.
        ///
        /// <paramref name="requireCallerOwnsAction"/> is false for the portal, for the
        /// reason AD-053 gives: Power Pages Web API writes arrive under the site's
        /// application user, so the caller is never the adviser and an owner check there
        /// would enforce nothing. The boundary for that path is the Contact-scoped
        /// <c>Remediation Action - assigned to me</c> table permission (AD-069).
        ///
        /// BR-008 needs the adviser to say what they did, so a completion with no recorded
        /// response is refused here rather than in the page — a blank response would reach
        /// the T&amp;C Manager as an action to attest to with nothing to read.
        /// </summary>
        public static CompleteResult Complete(
            IOrganizationService service,
            Guid targetId,
            string idempotencyKey,
            string expectedRowVersion,
            Guid actorId,
            Guid correlationId,
            bool requireCallerOwnsAction,
            string details,
            // Optional so every existing caller is unchanged. Where a caller has a tracing
            // service it passes one, and the sign-off-due routing says why it sent nothing;
            // where it does not, the routing stays silent rather than failing (NFR-OBS-01).
            Action<string> trace = null)
        {
            // Idempotency: a replay with the same key is a success no-op (NFR-REL-01).
            var existingAudit = FindAuditByKey(service, idempotencyKey);
            if (existingAudit != null)
            {
                return new CompleteResult
                {
                    Status = StatusName(StatusCompleted),
                    AuditEventId = existingAudit.Id,
                    Conflict = false,
                };
            }

            var action = CommandHelpers.RetrieveOrNotFound(
                service,
                ActionEntity,
                targetId,
                new ColumnSet(ActionStatus, "ownerid", ActionAdviserResponse, "al_outcomecaseid", ReviewLookup,
                    RemedialActions.ActionAttr, RemedialActions.ActionPerformedAttr),
                "That remediation action no longer exists. Refresh the case and try again.");

            if (requireCallerOwnsAction)
            {
                EnsureCaller(service, actorId, action);
            }

            var status = action.GetAttributeValue<OptionSetValue>(ActionStatus);
            var currentStatus = status != null ? status.Value : -1;

            // Already complete: idempotent success without a second write (BR-007 immutability).
            if (currentStatus == StatusCompleted)
            {
                var replayAudit = WriteAuditEvent(service, targetId, idempotencyKey, actorId, correlationId, details);
                return new CompleteResult
                {
                    Status = StatusName(StatusCompleted),
                    AuditEventId = replayAudit,
                    Conflict = false,
                };
            }

            if (currentStatus != StatusOpen && currentStatus != StatusInProgress)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "A remediation action can only be completed from Open or In progress.");
            }

            var completionRefusal = CompletionRefusal(action);
            if (completionRefusal != null)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + completionRefusal);
            }

            var update = new Entity(ActionEntity, targetId)
            {
                [ActionStatus] = new OptionSetValue(StatusCompleted),
                [ActionCompletedOn] = DateTime.UtcNow,
            };

            // Optimistic concurrency: reject a stale write with a distinct conflict code.
            if (!string.IsNullOrEmpty(expectedRowVersion))
            {
                update.RowVersion = expectedRowVersion;
                var updateRequest = new Microsoft.Xrm.Sdk.Messages.UpdateRequest
                {
                    Target = update,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                };

                try
                {
                    service.Execute(updateRequest);
                }
                catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
                {
                    if (CommandHelpers.IsConcurrencyFault(fault))
                    {
                        throw new InvalidPluginExecutionException(
                            ConflictPrefix + "This remediation action changed since you loaded it. Reload and try again.");
                    }

                    throw;
                }
            }
            else
            {
                service.Update(update);
            }

            AdvanceCase(
                service,
                action.GetAttributeValue<EntityReference>("al_outcomecaseid"),
                action.GetAttributeValue<EntityReference>(ReviewLookup),
                correlationId,
                trace);

            var auditId = WriteAuditEvent(service, targetId, idempotencyKey, actorId, correlationId, details);

            return new CompleteResult
            {
                Status = StatusName(StatusCompleted),
                AuditEventId = auditId,
                Conflict = false,
            };
        }

        /// <summary>
        /// A completed remediation puts the case in front of the T&amp;C Manager: Awaiting
        /// Remediation -> Remediation In Progress -> Awaiting Sign-off, the AD-057 spine
        /// (project-context "Canonical lifecycle", BR-008). Hopped one state at a time, the
        /// way <see cref="OutcomeRules.HopsFor"/> passes through Submitted, so a skipped
        /// state is refused rather than jumped.
        ///
        /// Before this existed nothing moved the case off Awaiting Remediation. The
        /// sign-off's own consequence (<see cref="SignoffProgressPlugin"/>) advances only a
        /// case that is AT Awaiting Sign-off, so an approval found the case one state short
        /// and did nothing — every remediated case stayed parked at Awaiting Remediation with
        /// an approved action against it, and PP-12's "an approval advances the case" never
        /// happened.
        ///
        /// A case that is not at either remediation state is left where it is, matching the
        /// sign-off's guard: the completion is still recorded, and a case a manager has moved
        /// by hand is not dragged back onto the spine.
        /// </summary>
        private static void AdvanceCase(
            IOrganizationService service,
            EntityReference caseRef,
            EntityReference reviewRef,
            Guid correlationId,
            Action<string> trace)
        {
            if (caseRef == null)
            {
                return;
            }

            var current = CaseTransitions.CurrentStatus(service, caseRef.Id);
            if (current != CaseLifecycle.AwaitingRemediation && current != CaseLifecycle.RemediationInProgress)
            {
                return;
            }

            // A review raises one action per thing the checker marked down (2026-09-10), so
            // completing one is no longer completing the remediation. The case moves to
            // Awaiting Sign-off only when nothing on THIS CHECK is still outstanding; until
            // then it sits at Remediation In Progress, which is what the state is for. Without
            // this the first item finished would put the whole case in front of the T&C
            // Manager with the rest untouched.
            if (AnyOutstanding(service, caseRef.Id, reviewRef == null ? (Guid?)null : reviewRef.Id))
            {
                CaseTransitions.MoveThrough(
                    service,
                    caseRef.Id,
                    new[] { CaseLifecycle.RemediationInProgress });
                return;
            }

            // BR-008: the T&C Manager verifies Insufficient evidence and Potential harm. A
            // case carrying neither has nothing for them to decide, and closes here - PROD
            // case 256497798, Pass with issues on both checks, sat at Awaiting Sign-off with
            // every action done and no one asked to act on it.
            if (!SignoffRequired(service, caseRef.Id))
            {
                CaseTransitions.MoveThrough(
                    service,
                    caseRef.Id,
                    new[] { CaseLifecycle.RemediationInProgress, CaseLifecycle.Closed });
                return;
            }

            CaseTransitions.MoveThrough(
                service,
                caseRef.Id,
                new[] { CaseLifecycle.RemediationInProgress, CaseLifecycle.AwaitingSignoff });

            NotifySignoffDue(service, caseRef, correlationId, trace);
        }

        /// <summary>
        /// Whether this case's remediation goes to the T&amp;C Manager, read from the grades
        /// on the case (<see cref="OutcomeRules.SignoffRequired"/>).
        ///
        /// An AQS check still owed always goes to sign-off: approval is what hands such a
        /// case back to the queue (<see cref="SignoffProgressPlugin.MoveCase"/>), and closing
        /// it here would end the case before its AQS check ran.
        /// </summary>
        public static bool SignoffRequired(IOrganizationService service, Guid caseId)
        {
            if (SubmitReviewPlugin.AqsStillOwed(service, caseId))
            {
                return true;
            }

            var outcomeCase = service.Retrieve("al_outcomecase", caseId, new ColumnSet("al_taxoutcome"));
            var tax = outcomeCase.GetAttributeValue<OptionSetValue>("al_taxoutcome");

            var query = new QueryExpression("al_outcome")
            {
                ColumnSet = new ColumnSet("al_initialoutcome"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);

            var aqs = new List<int>();
            foreach (var outcome in service.RetrieveMultiple(query).Entities)
            {
                var initial = outcome.GetAttributeValue<OptionSetValue>("al_initialoutcome");
                if (initial != null)
                {
                    aqs.Add(initial.Value);
                }
            }

            return OutcomeRules.SignoffRequired(tax == null ? (int?)null : tax.Value, aqs);
        }

        /// <summary>
        /// Whether the case may go from Remediation In Progress straight to Closed: nothing
        /// on it needs sign-off and no action on it, on any check, is still open. What
        /// <see cref="CaseStatusGuardPlugin"/> holds a direct write of that edge to.
        /// </summary>
        public static bool MayCloseWithoutSignoff(IOrganizationService service, Guid caseId)
        {
            return !AnyOutstanding(service, caseId, null) && !SignoffRequired(service, caseId);
        }

        /// <summary>
        /// Tells the case's T&amp;C Manager that a sign-off is now waiting on them (Fixes 5,
        /// AD-162).
        ///
        /// <para>
        /// This is the moment the work changes hands. Until 2026-09-20 nothing was sent here
        /// at all: the adviser was told when their remediation was approved or sent back, but
        /// the person who had to do the approving was never told there was anything to
        /// approve. A case reached Awaiting Sign-off and waited for somebody to notice.
        /// </para>
        /// <para>
        /// <b>Routing, not authorisation.</b> Every T&amp;C Manager may still sign off any
        /// case; the mapping only decides who is told. So an unmapped adviser costs a
        /// notification, not the ability to proceed, and the sign-off stays possible for
        /// anyone who goes looking.
        /// </para>
        /// <para>
        /// Never throws. The adviser has just finished their work and the case has already
        /// moved; failing their completion because a configuration row is missing would
        /// punish the wrong person for the wrong thing. The reason goes to the trace log
        /// instead (NFR-OBS-01), which is where an administrator looks when a manager says
        /// they were not told.
        /// </para>
        /// </summary>
        private static void NotifySignoffDue(
            IOrganizationService service,
            EntityReference caseRef,
            Guid correlationId,
            Action<string> trace)
        {
            try
            {
                var routing = TcManagerRouting.ForCase(service, caseRef);
                if (!routing.IsRouted)
                {
                    if (trace != null)
                    {
                        trace("Sign-off due on case " + caseRef.Id.ToString("D")
                            + " was not notified: " + routing.Reason);
                    }

                    return;
                }

                var reference = NotificationOutbox.CaseReference(service, caseRef) ?? "a case";

                // Who it greets and whose work it reports (2026-10-08).
                var manager = routing.Manager == null
                    ? null
                    : service.Retrieve("contact", routing.Manager.Id, new ColumnSet("fullname"))
                        .GetAttributeValue<string>("fullname");
                var adviser = service.Retrieve("al_outcomecase", caseRef.Id, new ColumnSet("al_advisername"))
                    .GetAttributeValue<string>("al_advisername");

                // The case's own remediation page, where the sign-off is made (reported
                // 2026-10-08: the coach had to find the case under "Awaiting T&C sign-off").
                var letter = NotificationTemplates.Render(
                    service,
                    NotificationTemplates.SignoffDue,
                    new Dictionary<string, string>
                    {
                        { NotificationTemplates.TokenReference, reference },
                        {
                            NotificationTemplates.TokenRecipient,
                            string.IsNullOrWhiteSpace(manager) ? "T&C Manager" : manager.Trim()
                        },
                        {
                            NotificationTemplates.TokenAdviser,
                            string.IsNullOrWhiteSpace(adviser) ? "The adviser" : adviser.Trim()
                        },
                        {
                            NotificationTemplates.TokenCaseButton,
                            NotificationTemplates.CaseButton(
                                NotificationOutbox.RemediationLink(service, caseRef),
                                NotificationTemplates.Definition(NotificationTemplates.SignoffDue).ButtonLabel)
                        },
                    });

                NotificationOutbox.Queue(
                    service,
                    correlationId,
                    NotificationOutbox.EventSignoffDue,
                    "al_outcomecase",
                    caseRef.Id,
                    routing.Email,
                    letter.Subject,
                    letter.Body,
                    NotificationTemplates.SignoffDue,
                    SignoffRound(service, caseRef.Id));
            }
            catch (Exception error)
            {
                if (trace != null)
                {
                    trace("Sign-off due notification failed on case " + caseRef.Id.ToString("D")
                        + ": " + error.Message);
                }
            }
        }

        /// <summary>
        /// Which time this case has reached Awaiting Sign-off, as the outbox occurrence of its
        /// "sign-off due" letter: the number of decisions already recorded on its actions.
        ///
        /// <para>
        /// Keyed on the case alone, the letter went once per case for good (reported
        /// 2026-10-08). Work the coach sent back and the adviser redid arrived at Awaiting
        /// Sign-off a second time, found the first row and was dropped as a duplicate, so the
        /// coach never heard it was done; the AQS leg of a Tax-then-AQS case did the same.
        /// </para>
        /// <para>
        /// A new round needs at least one new decision, so the count differs each time, and a
        /// replay of the same round counts the same rows and still collides. Counted through
        /// the actions because a sign-off made on the portal need not carry the case.
        /// </para>
        /// </summary>
        public static string SignoffRound(IOrganizationService service, Guid caseId)
        {
            var actions = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression(),
            };
            actions.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);

            var ids = new List<object>();
            foreach (var action in CommandHelpers.RetrieveAll(service, actions))
            {
                ids.Add(action.Id);
            }

            var decisions = 0;
            if (ids.Count > 0)
            {
                var signoffs = new QueryExpression("al_signoff")
                {
                    ColumnSet = new ColumnSet(false),
                    Criteria = new FilterExpression(),
                };
                signoffs.Criteria.AddCondition("al_remediationactionid", ConditionOperator.In, ids.ToArray());
                decisions = CommandHelpers.RetrieveAll(service, signoffs).Count;
            }

            return "R" + decisions.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Whether the case still carries a remediation action that is not Completed.
        ///
        /// Read from the actions rather than from a count held on the case: the set can grow
        /// (a second review raises its own) and a rejected sign-off reopens one, so a stored
        /// tally would drift out of step with the rows the adviser is actually looking at.
        /// </summary>
        private static bool AnyOutstanding(IOrganizationService service, Guid caseId, Guid? reviewId)
        {
            var query = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            query.Criteria.AddCondition(
                "al_actionstatus", ConditionOperator.NotEqual, Remediation.StatusCompleted);

            // Scoped to the check the finished action belongs to, so the two legs of a
            // Tax-then-AQS route are separate remediations: an action still open on the other
            // leg - a rejection that reopened one, say - is not this check's work and must not
            // hold it at Remediation In Progress. Rows written before the review link existed
            // carry none, and those fall back to the whole case.
            if (reviewId.HasValue)
            {
                query.Criteria.AddCondition(ReviewLookup, ConditionOperator.Equal, reviewId.Value);
            }

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        private static void EnsureCaller(IOrganizationService service, Guid callerId, Entity action)
        {
            var owner = action.GetAttributeValue<EntityReference>("ownerid");

            // The adviser completes their own action. Dataverse security is the primary
            // gate; this rejects a caller acting on an action owned by someone else.
            //
            // al_remediationaction is user-owned, so ownerid may be a systemuser OR a team.
            // Only the systemuser case can be settled by comparing ids: for a team-owned
            // action the caller qualifies if they are a member of that team. Anything else
            // — including an action with no owner at all — is refused rather than allowed,
            // because this runs as the system user and so no Dataverse write privilege of
            // the caller's would catch a wrong answer here.
            if (owner == null)
            {
                throw new InvalidPluginExecutionException(
                    UnauthorizedPrefix + "This remediation action has no owner, so it cannot be completed.");
            }

            if (owner.LogicalName == "systemuser")
            {
                if (owner.Id != callerId)
                {
                    throw new InvalidPluginExecutionException(
                        UnauthorizedPrefix + "Only the adviser who owns this remediation action can complete it.");
                }
                return;
            }

            if (owner.LogicalName == "team")
            {
                if (!CommandHelpers.IsTeamMember(service, owner.Id, callerId))
                {
                    throw new InvalidPluginExecutionException(
                        UnauthorizedPrefix + "Only a member of the team that owns this remediation action can complete it.");
                }
                return;
            }

            throw new InvalidPluginExecutionException(
                UnauthorizedPrefix + "This remediation action has an owner type that cannot be verified.");
        }

        private static Entity FindAuditByKey(IOrganizationService service, string idempotencyKey)
        {
            var query = new QueryExpression(AuditEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_idempotencykey", ConditionOperator.Equal, idempotencyKey);
            // Scoped to this command: an idempotency key is caller-supplied and unique across
            // the whole audit table, so matching the key alone would replay a key first used
            // by a different command as if this one had already run.
            query.Criteria.AddCondition("al_command", ConditionOperator.Equal, CommandCompleteRemediation);

            var result = service.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        private static Guid WriteAuditEvent(
            IOrganizationService service,
            Guid targetId,
            string idempotencyKey,
            Guid actorId,
            Guid correlationId,
            string details)
        {
            var audit = new Entity(AuditEntity)
            {
                ["al_name"] = "CompleteRemediation " + targetId.ToString("D"),
                ["al_command"] = new OptionSetValue(CommandCompleteRemediation),
                ["al_targettable"] = ActionEntity,
                ["al_targetid"] = targetId.ToString("D"),
                ["al_actorid"] = actorId.ToString("D"),
                ["al_idempotencykey"] = idempotencyKey,
                ["al_correlationid"] = correlationId.ToString("D"),
                ["al_occurredon"] = DateTime.UtcNow,
            };

            // Same omission as the other two audit writers had: the id was stamped and the
            // name never was, so the history log showed whichever account the plug-in ran as.
            var actorName = CommandHelpers.ResolveActorName(service, actorId);
            if (!string.IsNullOrEmpty(actorName))
            {
                audit["al_actorname"] = actorName;
            }

            if (!string.IsNullOrEmpty(details))
            {
                audit["al_details"] = details;
            }

            return service.Create(audit);
        }

        /// <summary>
        /// What an action still needs before it can be completed, or null when nothing.
        ///
        /// Two rules, chosen by the row (project owner, 2026-09-29). A row carrying the
        /// checker's remedial action asks the adviser only whether it was performed - Yes or
        /// No, and a No still completes, because the supervisor decides what a No means. A row
        /// raised before the change has no checker's words, so the adviser's own text is still
        /// the thing the supervisor attests to (BR-008) and is still required.
        /// </summary>
        public static string CompletionRefusal(Entity action)
        {
            if (!string.IsNullOrWhiteSpace(action.GetAttributeValue<string>(RemedialActions.ActionAttr)))
            {
                return action.GetAttributeValue<OptionSetValue>(RemedialActions.ActionPerformedAttr) == null
                    ? "Answer 'Action performed' (Yes or No) for this action before marking it complete."
                    : null;
            }

            return string.IsNullOrWhiteSpace(action.GetAttributeValue<string>(ActionAdviserResponse))
                ? "Record what you did about this action before marking it complete."
                : null;
        }

        private static string StatusName(int status)
        {
            switch (status)
            {
                case StatusOpen: return "Open";
                case StatusInProgress: return "In progress";
                case StatusCompleted: return "Completed";
                default: return "Unknown";
            }
        }

    }
}
