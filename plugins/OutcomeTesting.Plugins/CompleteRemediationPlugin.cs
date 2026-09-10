using System;
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
                details: null);

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
            string details)
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

            var action = service.Retrieve(
                ActionEntity,
                targetId,
                new ColumnSet(ActionStatus, "ownerid", ActionAdviserResponse, "al_outcomecaseid", ReviewLookup));

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

            if (string.IsNullOrWhiteSpace(action.GetAttributeValue<string>(ActionAdviserResponse)))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "Record what you did about this action before marking it complete.");
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
                action.GetAttributeValue<EntityReference>(ReviewLookup));

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
            IOrganizationService service, EntityReference caseRef, EntityReference reviewRef)
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

            CaseTransitions.MoveThrough(
                service,
                caseRef.Id,
                new[] { CaseLifecycle.RemediationInProgress, CaseLifecycle.AwaitingSignoff });
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
