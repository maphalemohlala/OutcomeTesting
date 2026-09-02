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
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        // al_remediationaction.
        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";
        private const string ActionCompletedOn = "al_completedon";
        private const string ActionAdviserResponse = "al_adviserresponse";
        private const int StatusOpen = 120910600;
        private const int StatusInProgress = 120910601;
        private const int StatusCompleted = 120910602;

        // al_auditevent.
        private const string AuditEntity = "al_auditevent";
        private const int CommandCompleteRemediation = 120910756;

        // Distinct failure prefixes so the client can branch (command-concurrency skill).
        private const string ConflictPrefix = "CONFLICT: ";
        private const string UnauthorizedPrefix = "UNAUTHORIZED: ";
        private const string PreconditionPrefix = "PRECONDITION: ";

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

            var targetId = ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = GetRequiredString(context, InIdempotencyKey);
            var expectedRowVersion = GetOptionalString(context, InExpectedRowVersion);

            var result = Complete(
                service,
                targetId,
                idempotencyKey,
                expectedRowVersion,
                context.InitiatingUserId,
                context.CorrelationId,
                requireCallerOwnsAction: true,
                details: null);

            SetResponse(context, result.Status, result.AuditEventId, result.Conflict);
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
                new ColumnSet(ActionStatus, "ownerid", ActionAdviserResponse));

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
                    if (IsConcurrencyFault(fault))
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

            var auditId = WriteAuditEvent(service, targetId, idempotencyKey, actorId, correlationId, details);

            return new CompleteResult
            {
                Status = StatusName(StatusCompleted),
                AuditEventId = auditId,
                Conflict = false,
            };
        }

        private static bool IsConcurrencyFault(System.ServiceModel.FaultException<OrganizationServiceFault> fault)
        {
            // ConcurrencyVersionMismatch (0x80060892); fall back to message text in case
            // the exact code varies by platform build.
            if (fault.Detail != null && fault.Detail.ErrorCode == unchecked((int)0x80060892))
            {
                return true;
            }

            var message = fault.Message ?? string.Empty;
            return message.IndexOf("row version", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("concurrency", StringComparison.OrdinalIgnoreCase) >= 0;
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
                if (!IsTeamMember(service, owner.Id, callerId))
                {
                    throw new InvalidPluginExecutionException(
                        UnauthorizedPrefix + "Only a member of the team that owns this remediation action can complete it.");
                }
                return;
            }

            throw new InvalidPluginExecutionException(
                UnauthorizedPrefix + "This remediation action has an owner type that cannot be verified.");
        }

        /// <summary>True when <paramref name="userId"/> belongs to the given team.</summary>
        private static bool IsTeamMember(IOrganizationService service, Guid teamId, Guid userId)
        {
            var query = new QueryExpression("teammembership")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
            query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);

            return service.RetrieveMultiple(query).Entities.Count > 0;
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

            if (!string.IsNullOrEmpty(details))
            {
                audit["al_details"] = details;
            }

            return service.Create(audit);
        }

        private static void SetResponse(IPluginExecutionContext context, string status, Guid auditEventId, bool conflict)
        {
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditEventId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
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

        private static Guid ParseRequiredGuid(IPluginExecutionContext context, string name)
        {
            var raw = GetRequiredString(context, name);
            Guid value;
            if (!Guid.TryParse(raw, out value) || value == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + name + " must be a valid record id.");
            }

            return value;
        }

        private static string GetRequiredString(IPluginExecutionContext context, string name)
        {
            var value = GetOptionalString(context, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + name + " is required.");
            }

            return value;
        }

        private static string GetOptionalString(IPluginExecutionContext context, string name)
        {
            object value;
            if (context.InputParameters.TryGetValue(name, out value) && value is string)
            {
                return (string)value;
            }

            return null;
        }
    }
}
