using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SubmitReview (AD-003). Registered against the Custom API
    /// message <c>al_SubmitReview</c>. A checker submits their own Tax or AQS review
    /// instance (FR-010..FR-017, PP-07, PP-08). The command enforces the caller, the
    /// transition guard, every mandatory question, optimistic concurrency and
    /// idempotency, and writes an immutable Audit Event (BR-012, NFR-AUD-01).
    ///
    /// Submission is the point the review becomes read-only (PP-11). The lock is
    /// enforced here rather than in the portal, so it cannot be bypassed through the
    /// Portals Web API or a hand-edited URL.
    /// </summary>
    public class SubmitReviewPlugin : PluginBase
    {
        // Custom API request parameters.
        private const string InTargetId = "TargetId";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        // Custom API response parameters.
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        // al_reviewinstance.
        private const string ReviewEntity = "al_reviewinstance";
        private const string ReviewStatus = "al_reviewstatus";
        private const string ReviewSubmittedOn = "al_submittedon";
        private const string ReviewChecklistVersion = "al_checklistversionid";
        private const int StatusAssigned = 120910210;
        private const int StatusInProgress = 120910211;
        private const int StatusSubmitted = 120910212;

        // Checklist chain and responses.
        private const string QuestionVersionEntity = "al_questionversion";
        private const string ResponseEntity = "al_response";

        // al_auditevent.
        private const string AuditEntity = "al_auditevent";
        private const int CommandSubmitReview = 120910754;

        // Distinct failure prefixes so the client can branch (command-concurrency skill).
        private const string ConflictPrefix = "CONFLICT: ";
        private const string UnauthorizedPrefix = "UNAUTHORIZED: ";
        private const string PreconditionPrefix = "PRECONDITION: ";

        public SubmitReviewPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SubmitReviewPlugin))
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

            // Idempotency: a replay with the same key is a success no-op (NFR-REL-01).
            var existingAudit = FindAuditByKey(service, idempotencyKey);
            if (existingAudit != null)
            {
                SetResponse(context, StatusName(StatusSubmitted), existingAudit.Id, false);
                return;
            }

            var review = service.Retrieve(
                ReviewEntity,
                targetId,
                new ColumnSet(ReviewStatus, ReviewChecklistVersion, "ownerid"));

            EnsureCaller(service, context, review);

            var status = review.GetAttributeValue<OptionSetValue>(ReviewStatus);
            var currentStatus = status != null ? status.Value : -1;

            // Already submitted: idempotent success. The submitted review is immutable,
            // so no second write is made and al_submittedon keeps its original value
            // (BR-007, PP-11). Reopening is a privileged T&C Manager command (AD-031).
            if (currentStatus == StatusSubmitted)
            {
                var replayAudit = WriteAuditEvent(service, targetId, idempotencyKey, context);
                SetResponse(context, StatusName(StatusSubmitted), replayAudit, false);
                return;
            }

            if (currentStatus != StatusAssigned && currentStatus != StatusInProgress)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "A review can only be submitted from Assigned or Review In Progress.");
            }

            EnsureMandatoryQuestionsAnswered(service, targetId, review);

            var update = new Entity(ReviewEntity, targetId)
            {
                [ReviewStatus] = new OptionSetValue(StatusSubmitted),
                [ReviewSubmittedOn] = DateTime.UtcNow,
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
                            ConflictPrefix + "This review changed since you loaded it. Reload and try again.");
                    }

                    throw;
                }
            }
            else
            {
                service.Update(update);
            }

            var auditId = WriteAuditEvent(service, targetId, idempotencyKey, context);
            SetResponse(context, StatusName(StatusSubmitted), auditId, false);
        }

        /// <summary>
        /// Every mandatory question on the checklist version issued to this review must
        /// carry an answer before submission (PP-07, PP-08). Mandatory questions are read
        /// from the version stamped on the review, never from the current question record,
        /// so a historic review is judged against the rules that applied to it
        /// (BR-013, FR-030, FR-031).
        /// </summary>
        private static void EnsureMandatoryQuestionsAnswered(
            IOrganizationService service,
            Guid targetId,
            Entity review)
        {
            var checklistVersion = review.GetAttributeValue<EntityReference>(ReviewChecklistVersion);
            if (checklistVersion == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This review has no checklist version issued, so it cannot be submitted.");
            }

            // Mandatory question versions belonging to this checklist version, reached
            // through Question -> Section, which is the single-parent chain in the model.
            var mandatoryQuery = new QueryExpression(QuestionVersionEntity)
            {
                ColumnSet = new ColumnSet("al_questionversionid"),
                Criteria = new FilterExpression(),
            };
            mandatoryQuery.Criteria.AddCondition("al_ismandatory", ConditionOperator.Equal, true);

            var questionLink = mandatoryQuery.AddLink("al_question", "al_questionid", "al_questionid");
            questionLink.EntityAlias = "q";
            var sectionLink = questionLink.AddLink("al_section", "al_sectionid", "al_sectionid");
            sectionLink.EntityAlias = "s";
            sectionLink.LinkCriteria.AddCondition(
                "al_checklistversionid", ConditionOperator.Equal, checklistVersion.Id);

            var mandatory = service.RetrieveMultiple(mandatoryQuery);
            if (mandatory.Entities.Count == 0)
            {
                return;
            }

            // Answered question versions for this review. A response counts as answered
            // only when one of the typed answer columns actually holds a value (AD-023);
            // an empty response row is not an answer.
            var answered = new HashSet<Guid>();
            var responseQuery = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_questionversionid", "al_answertext", "al_answerchoice",
                    "al_answerchoices", "al_answerdate"),
                Criteria = new FilterExpression(),
            };
            responseQuery.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, targetId);

            foreach (var response in service.RetrieveMultiple(responseQuery).Entities)
            {
                var questionVersion = response.GetAttributeValue<EntityReference>("al_questionversionid");
                if (questionVersion == null)
                {
                    continue;
                }

                if (HasAnswer(response))
                {
                    answered.Add(questionVersion.Id);
                }
            }

            var missing = 0;
            foreach (var questionVersion in mandatory.Entities)
            {
                if (!answered.Contains(questionVersion.Id))
                {
                    missing++;
                }
            }

            if (missing > 0)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "Complete all questions marked Required before submitting. "
                    + missing + " of " + mandatory.Entities.Count + " required questions are unanswered.");
            }
        }

        /// <summary>True when the response holds a value in any typed answer column.</summary>
        private static bool HasAnswer(Entity response)
        {
            var text = response.GetAttributeValue<string>("al_answertext");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var choices = response.GetAttributeValue<string>("al_answerchoices");
            if (!string.IsNullOrWhiteSpace(choices))
            {
                return true;
            }

            if (response.GetAttributeValue<OptionSetValue>("al_answerchoice") != null)
            {
                return true;
            }

            return response.Contains("al_answerdate")
                && response.GetAttributeValue<DateTime?>("al_answerdate").HasValue;
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

        private static void EnsureCaller(IOrganizationService service, IPluginExecutionContext context, Entity review)
        {
            var owner = review.GetAttributeValue<EntityReference>("ownerid");

            // The assigned checker submits their own review. Dataverse security is the
            // primary gate; this rejects a caller acting on a review owned by someone
            // else. al_reviewinstance is user-owned (AD-024), so ownerid may be a
            // systemuser or a team. Anything else, including no owner at all, is refused
            // rather than allowed, because this runs as the system user and so no write
            // privilege of the caller's would catch a wrong answer here.
            if (owner == null)
            {
                throw new InvalidPluginExecutionException(
                    UnauthorizedPrefix + "This review has no owner, so it cannot be submitted.");
            }

            if (owner.LogicalName == "systemuser")
            {
                if (owner.Id != context.InitiatingUserId)
                {
                    throw new InvalidPluginExecutionException(
                        UnauthorizedPrefix + "Only the checker assigned to this review can submit it.");
                }
                return;
            }

            if (owner.LogicalName == "team")
            {
                if (!IsTeamMember(service, owner.Id, context.InitiatingUserId))
                {
                    throw new InvalidPluginExecutionException(
                        UnauthorizedPrefix + "Only a member of the team that owns this review can submit it.");
                }
                return;
            }

            throw new InvalidPluginExecutionException(
                UnauthorizedPrefix + "This review has an owner type that cannot be verified.");
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
            // Scoped to this command: an idempotency key is caller-supplied and unique
            // across the whole audit table, so matching the key alone would replay a key
            // first used by a different command as if this one had already run.
            query.Criteria.AddCondition("al_command", ConditionOperator.Equal, CommandSubmitReview);

            var result = service.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0] : null;
        }

        private static Guid WriteAuditEvent(
            IOrganizationService service,
            Guid targetId,
            string idempotencyKey,
            IPluginExecutionContext context)
        {
            var audit = new Entity(AuditEntity)
            {
                ["al_name"] = "SubmitReview " + targetId.ToString("D"),
                ["al_command"] = new OptionSetValue(CommandSubmitReview),
                ["al_targettable"] = ReviewEntity,
                ["al_targetid"] = targetId.ToString("D"),
                ["al_actorid"] = context.InitiatingUserId.ToString("D"),
                ["al_idempotencykey"] = idempotencyKey,
                ["al_correlationid"] = context.CorrelationId.ToString("D"),
                ["al_occurredon"] = DateTime.UtcNow,
            };

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
                case StatusAssigned: return "Assigned";
                case StatusInProgress: return "Review In Progress";
                case StatusSubmitted: return "Submitted";
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
