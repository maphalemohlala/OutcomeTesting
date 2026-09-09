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
        private const string ReviewType = "al_reviewtype";
        private const string ReviewOutcomeCase = "al_outcomecaseid";
        private const int StatusAssigned = 120910210;
        private const int StatusInProgress = 120910211;
        private const int StatusSubmitted = 120910212;

        // al_outcomecase and its route.
        private const string CaseEntity = "al_outcomecase";
        private const string CaseStatus = "al_casestatus";
        private const string RouteEntity = "al_reviewroute";
        private const string CaseReviewRoute = "al_reviewrouteid";
        private const string RouteRequiresTax = "al_requirestaxreview";
        private const string RouteRequiresAqs = "al_requiresaqsreview";

        // Checklist chain and responses.
        private const string QuestionVersionEntity = "al_questionversion";
        private const string ResponseEntity = "al_response";

        // al_outcome and the questions that drive it.
        private const string OutcomeEntity = "al_outcome";
        private const string GradeQuestionCode = "Q-GR-01";
        private const string TaxOutcomeQuestionCode = "Q-TAX-02";

        // The checklist's own remediation trigger, per discipline (V8: "Remedial action
        // required?", a mandatory Yes/No in each File Quality section), and the fail
        // observation beside it that gives the adviser something to work from. Both are
        // matched on question code, so a question retired and succeeded under BR-013 keeps
        // working.
        private const string AqsRemedialQuestionCode = "Q-FQ-03";
        private const string AqsObservationQuestionCode = "Q-FQ-02";
        private const string TaxRemedialQuestionCode = "Q-FQTAX-03";
        private const string TaxObservationQuestionCode = "Q-FQTAX-02";

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

            var result = Submit(
                service,
                targetId,
                idempotencyKey,
                expectedRowVersion,
                context.InitiatingUserId,
                context.CorrelationId,
                requireCallerOwnsReview: true,
                details: null);

            SetResponse(context, result.Status, result.AuditEventId, result.Conflict);
        }

        /// <summary>The outcome of a submission, in the shape the Custom API responds with.</summary>
        public sealed class SubmitResult
        {
            public string Status { get; set; }

            public Guid AuditEventId { get; set; }

            public bool Conflict { get; set; }
        }

        /// <summary>
        /// The whole submission (FR-016, FR-017, PP-07, PP-08, PP-11), shared by the
        /// <c>al_SubmitReview</c> Custom API and the portal's update-triggered path so the
        /// two entry points cannot enforce different rules. Extracted for the same reason
        /// <see cref="CaseTransitions"/> was: two callers writing the same state is two
        /// places for it to be enforced differently.
        ///
        /// <paramref name="requireCallerOwnsReview"/> is false for the portal. Power Pages
        /// Web API writes arrive under the site's application user, so the caller identity
        /// is never the checker and an owner check there would look like security while
        /// enforcing nothing (AD-053). The boundary for that path is the Contact-scoped
        /// table permission, which is what let the write happen at all.
        /// </summary>
        public static SubmitResult Submit(
            IOrganizationService service,
            Guid targetId,
            string idempotencyKey,
            string expectedRowVersion,
            Guid actorId,
            Guid correlationId,
            bool requireCallerOwnsReview,
            string details,
            string actorName = null)
        {
            // Idempotency: a replay with the same key is a success no-op (NFR-REL-01).
            var existingAudit = FindAuditByKey(service, idempotencyKey);
            if (existingAudit != null)
            {
                return new SubmitResult
                {
                    Status = StatusName(StatusSubmitted),
                    AuditEventId = existingAudit.Id,
                    Conflict = false,
                };
            }

            var review = service.Retrieve(
                ReviewEntity,
                targetId,
                new ColumnSet(ReviewStatus, ReviewChecklistVersion, ReviewType, ReviewOutcomeCase, "ownerid", "al_sequence"));

            if (requireCallerOwnsReview)
            {
                EnsureCaller(service, actorId, review);
            }

            var status = review.GetAttributeValue<OptionSetValue>(ReviewStatus);
            var currentStatus = status != null ? status.Value : -1;

            // Already submitted: idempotent success. The submitted review is immutable,
            // so no second write is made and al_submittedon keeps its original value
            // (BR-007, PP-11). Reopening is a privileged T&C Manager command (AD-031).
            if (currentStatus == StatusSubmitted)
            {
                var replayAudit = WriteAuditEvent(service, targetId, idempotencyKey, actorId, correlationId, details, actorName);
                return new SubmitResult
                {
                    Status = StatusName(StatusSubmitted),
                    AuditEventId = replayAudit,
                    Conflict = false,
                };
            }

            if (currentStatus != StatusAssigned && currentStatus != StatusInProgress)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "A review can only be submitted from Assigned or Review In Progress.");
            }

            EnsureMandatoryQuestionsAnswered(service, targetId, review);
            EnsureTaxPrecedesAqs(service, review);

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

            FinaliseReview(service, review, targetId);

            var auditId = WriteAuditEvent(service, targetId, idempotencyKey, actorId, correlationId, details, actorName);

            QueueSubmittedNotification(service, correlationId, review, targetId);

            return new SubmitResult
            {
                Status = StatusName(StatusSubmitted),
                AuditEventId = auditId,
                Conflict = false,
            };
        }

        /// <summary>
        /// Queues the PP-15 "Review submitted" event (AD-035), in the same transaction as
        /// the submission so the two cannot disagree.
        ///
        /// The recipient is the para-planner named on the case (OD-030(ii), project owner
        /// direction 2026-09-04). That is what BR-009 asks for: the para-planner who
        /// supported the case hears that its check is finished, by email, without being
        /// given a licence, a web role or any operational access to the case itself.
        ///
        /// The case names the para-planner but carries no address for them, so
        /// <see cref="NotificationOutbox.ParaplannerEmail"/> resolves the name to a Contact
        /// and returns null unless the match is unambiguous. A null recipient still queues
        /// the row — the event happened and the outbox says so — and the drain then marks it
        /// Failed with the reason, which puts an unresolvable para-planner in front of a
        /// person instead of sending a client's advice outcome to a guessed address.
        /// </summary>
        private static void QueueSubmittedNotification(
            IOrganizationService service,
            Guid correlationId,
            Entity review,
            Guid reviewId)
        {
            var caseRef = review.GetAttributeValue<EntityReference>("al_outcomecaseid");
            var reference = NotificationOutbox.CaseReference(service, caseRef) ?? "a case";

            NotificationOutbox.Queue(
                service,
                correlationId,
                NotificationOutbox.EventReviewSubmitted,
                ReviewEntity,
                reviewId,
                NotificationOutbox.ParaplannerEmail(service, caseRef),
                "Review submitted on case " + reference,
                "The review on case " + reference + " has been submitted and is locked to further edits (FR-017).");
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

            // Mandatory questions are scoped to the sections this discipline owns
            // (AD-020). Without this the gate demands all 42 mandatory answers whatever
            // the review type, so a Tax review is held by AQS questions its reviewer
            // cannot see, and an AQS review is held by the two Tax questions. The portal
            // already filters sections by owner role (AD-056); this is the server half.
            var reviewType = review.GetAttributeValue<OptionSetValue>(ReviewType);
            int ownerRole;
            if (reviewType == null || !OutcomeRules.TryOwnerRoleForReviewType(reviewType.Value, out ownerRole))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This review has no recognised discipline, so the questions it must answer cannot be determined.");
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
            sectionLink.LinkCriteria.AddCondition(
                "al_ownerrole", ConditionOperator.Equal, ownerRole);

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

        /// <summary>
        /// Tax and AQS run sequentially when both are required (BR-004). Without this an
        /// AQS review could be graded and its case closed while the Tax check was still
        /// open, producing an Outcome for a case whose Tax check never happened.
        ///
        /// Sibling instances are read from the case rather than trusted from al_sequence,
        /// because sequence is data a caller can set and the invariant has to hold anyway.
        /// A Tax submit is never refused here: Tax is always first.
        /// </summary>
        private static void EnsureTaxPrecedesAqs(IOrganizationService service, Entity review)
        {
            var reviewType = review.GetAttributeValue<OptionSetValue>(ReviewType);
            if (reviewType == null || reviewType.Value != ResponseRules.ReviewTypeAqs)
            {
                return;
            }

            var caseRef = review.GetAttributeValue<EntityReference>(ReviewOutcomeCase);
            if (caseRef == null)
            {
                return;
            }

            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ReviewOutcomeCase, ConditionOperator.Equal, caseRef.Id);
            query.Criteria.AddCondition(ReviewType, ConditionOperator.Equal, ResponseRules.ReviewTypeTax);
            query.Criteria.AddCondition(ReviewStatus, ConditionOperator.NotEqual, StatusSubmitted);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "The Tax check on this case has not been submitted yet. Tax must be completed before the AQS review (BR-004).");
            }

            // No unsubmitted Tax instance. That is either "Tax has been submitted" or
            // "Tax was never created", and only the route tells them apart. The
            // difference matters: a route that requires Tax with no Tax instance at all
            // means the check never happened, which is precisely what BR-004 exists to
            // prevent. Where the case carries no route — every case created before the
            // route seed existed — fall back to the instances rather than refusing on
            // absent data, so an AQS-only case still submits.
            bool taxRequired;
            if (TryRouteRequires(service, caseRef.Id, RouteRequiresTax, out taxRequired) && taxRequired)
            {
                var anyTax = new QueryExpression(ReviewEntity)
                {
                    ColumnSet = new ColumnSet(false),
                    TopCount = 1,
                    Criteria = new FilterExpression(),
                };
                anyTax.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                anyTax.Criteria.AddCondition(ReviewOutcomeCase, ConditionOperator.Equal, caseRef.Id);
                anyTax.Criteria.AddCondition(ReviewType, ConditionOperator.Equal, ResponseRules.ReviewTypeTax);

                if (service.RetrieveMultiple(anyTax).Entities.Count == 0)
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "This case requires a Tax check and none has been created yet. Tax must be completed before the AQS review (BR-004).");
                }
            }

            // OD-027, as OD-038 now completes it: a Tax check that did not pass sends the
            // case to remediation, and the AQS review follows once that remediation has been
            // APPROVED — through remediation, not instead of it. So an AQS submit is refused
            // while the Tax non-pass is unremediated, and allowed once the action the Tax
            // check raised carries an approved sign-off. No submitted Tax review, or one with
            // no answer recorded, is not refused here — the checks above already cover the
            // cases that matter, and refusing on absent data would break AQS-only and
            // legacy routes.
            var submittedTax = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            submittedTax.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            submittedTax.Criteria.AddCondition(ReviewOutcomeCase, ConditionOperator.Equal, caseRef.Id);
            submittedTax.Criteria.AddCondition(ReviewType, ConditionOperator.Equal, ResponseRules.ReviewTypeTax);
            submittedTax.Criteria.AddCondition(ReviewStatus, ConditionOperator.Equal, StatusSubmitted);

            // A case can carry more than one submitted Tax review — a recheck or a regrade
            // adds another. With TopCount 1 and no order Dataverse may return any of them,
            // so which tax result gates the AQS submit would be arbitrary. The latest
            // submission is the one in force; modifiedon breaks a tie and covers rows
            // predating al_submittedon.
            submittedTax.AddOrder(ReviewSubmittedOn, OrderType.Descending);
            submittedTax.AddOrder("modifiedon", OrderType.Descending);

            var submittedTaxEntities = service.RetrieveMultiple(submittedTax).Entities;
            if (submittedTaxEntities.Count > 0)
            {
                var taxReviewId = submittedTaxEntities[0].Id;
                var taxAnswer = AnswerChoiceFor(service, taxReviewId, TaxOutcomeQuestionCode);
                if (taxAnswer.HasValue
                    && OutcomeRules.TaxResultRequiresRemediation(taxAnswer.Value)
                    && !RemediationApproved(service, taxReviewId))
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The tax check on this case did not pass and its remediation has not been signed off, so the case cannot proceed to an AQS review yet (OD-027, OD-038).");
                }
            }
        }

        /// <summary>
        /// Whether the remediation action a review raised carries an approved sign-off. This
        /// is what lets an AQS review follow a Tax non-pass (OD-038): the case went through
        /// remediation, the T&amp;C Manager approved it, and only then did it return to the
        /// queue. An action that was never raised, is still open, or was only ever rejected
        /// does not count.
        /// </summary>
        public static bool RemediationApproved(IOrganizationService service, Guid reviewId)
        {
            var actions = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression(),
            };
            actions.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

            foreach (var action in service.RetrieveMultiple(actions).Entities)
            {
                var signoffs = new QueryExpression("al_signoff")
                {
                    ColumnSet = new ColumnSet(false),
                    TopCount = 1,
                    Criteria = new FilterExpression(),
                };
                signoffs.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                signoffs.Criteria.AddCondition("al_remediationactionid", ConditionOperator.Equal, action.Id);
                signoffs.Criteria.AddCondition("al_signoffdecision", ConditionOperator.Equal, SignoffProgressPlugin.DecisionApprovedValue);

                if (service.RetrieveMultiple(signoffs).Entities.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the case's route demands a discipline. Returns false when the case
        /// carries no route at all — every case created before the route seed existed —
        /// so the caller falls back to the review instances that actually exist rather
        /// than refusing on absent data.
        /// </summary>
        private static bool TryRouteRequires(
            IOrganizationService service, Guid caseId, string routeAttribute, out bool required)
        {
            required = false;

            var outcomeCase = service.Retrieve(CaseEntity, caseId, new ColumnSet(CaseReviewRoute));
            var routeRef = outcomeCase.GetAttributeValue<EntityReference>(CaseReviewRoute);
            if (routeRef == null)
            {
                return false;
            }

            var route = service.Retrieve(RouteEntity, routeRef.Id, new ColumnSet(routeAttribute));
            required = route.GetAttributeValue<bool?>(routeAttribute) ?? false;
            return true;
        }

        /// <summary>
        /// The response this review recorded for a question, by business code, carrying
        /// <paramref name="columns"/>. Returns null when the question was not answered.
        ///
        /// Matched on question CODE, so a question retired and succeeded under BR-013 /
        /// AD-004 can leave this review holding an answer against more than one version of
        /// the same question. The latest answer is the one that grades the review; without
        /// an order Dataverse could return the superseded one.
        /// </summary>
        private static Entity AnswerFor(
            IOrganizationService service, Guid reviewId, string questionCode, params string[] columns)
        {
            var query = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet(columns),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

            var version = query.AddLink(QuestionVersionEntity, "al_questionversionid", "al_questionversionid");
            var question = version.AddLink("al_question", "al_questionid", "al_questionid");
            question.LinkCriteria.AddCondition("al_questioncode", ConditionOperator.Equal, questionCode);
            query.AddOrder("modifiedon", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>
        /// The al_answerchoice this review recorded for a question, by business code.
        /// Returns null when the question was not answered.
        /// </summary>
        private static int? AnswerChoiceFor(IOrganizationService service, Guid reviewId, string questionCode)
        {
            var response = AnswerFor(service, reviewId, questionCode, "al_answerchoice");
            if (response == null)
            {
                return null;
            }

            var choice = response.GetAttributeValue<OptionSetValue>("al_answerchoice");
            return choice == null ? (int?)null : choice.Value;
        }

        /// <summary>
        /// Whether an AQS review is still owed on this case. The route decides when it is
        /// set; where it is null — which is every case created before the route seed
        /// existed — fall back to the review instances that actually exist, so a Tax
        /// submit on a case with no AQS instance finalises as Tax-only rather than
        /// stalling in the queue forever.
        /// </summary>
        private static bool AqsStillToCome(IOrganizationService service, Guid caseId)
        {
            return AqsStillOwed(service, caseId);
        }

        /// <summary>
        /// Whether the case still owes an AQS review: the route requires one and none has
        /// been submitted. Where the case carries no route — every case created before the
        /// route seed existed — an unsubmitted AQS instance is the only evidence, so that is
        /// what decides.
        ///
        /// Asked at a Tax submit (does the case go to the queue or finalise?) and at an
        /// approved sign-off (does the case go back to the queue for AQS, or on to recheck?
        /// — OD-038). The second caller is why "no unsubmitted instance" is not read as
        /// "still to come": at sign-off the AQS review may already be in, and sending the
        /// case back to the queue then would offer a finished check for a second checker.
        /// </summary>
        public static bool AqsStillOwed(IOrganizationService service, Guid caseId)
        {
            bool aqsRequired;
            var hasRoute = TryRouteRequires(service, caseId, RouteRequiresAqs, out aqsRequired);

            if (hasRoute)
            {
                return aqsRequired && !HasSubmittedReview(service, caseId, ResponseRules.ReviewTypeAqs);
            }

            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ReviewOutcomeCase, ConditionOperator.Equal, caseId);
            query.Criteria.AddCondition(ReviewType, ConditionOperator.Equal, ResponseRules.ReviewTypeAqs);
            query.Criteria.AddCondition(ReviewStatus, ConditionOperator.NotEqual, StatusSubmitted);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        private static bool HasSubmittedReview(IOrganizationService service, Guid caseId, int reviewType)
        {
            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ReviewOutcomeCase, ConditionOperator.Equal, caseId);
            query.Criteria.AddCondition(ReviewType, ConditionOperator.Equal, reviewType);
            query.Criteria.AddCondition(ReviewStatus, ConditionOperator.Equal, StatusSubmitted);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        /// <summary>
        /// Whether the case carries an Outcome at all. A Tax check records none (AD-055), so a
        /// remediated Tax-only case has no final outcome to set at recheck.
        /// </summary>
        public static bool HasOutcome(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(OutcomeEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        /// <summary>
        /// Records what the submission produced: for AQS the initial Outcome (BR-005,
        /// BR-007), and for both disciplines the case's next status (AD-057). Runs inside
        /// the submit transaction, so a review can never be Submitted without its Outcome.
        ///
        /// A Tax review creates no Outcome: al_Outcome.al_initialoutcome carries only the
        /// BR-005 four-value AQS scale, and the Tax result is the three-value
        /// PassFailInsufficient scale of Q-TAX-02 (AD-055). The Tax grade stays on its
        /// response, and AD-039's export contract has no Tax column.
        /// </summary>
        private static void FinaliseReview(IOrganizationService service, Entity review, Guid targetId)
        {
            var caseRef = review.GetAttributeValue<EntityReference>(ReviewOutcomeCase);
            if (caseRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This review is not linked to a case, so its outcome cannot be recorded.");
            }

            var reviewType = review.GetAttributeValue<OptionSetValue>(ReviewType).Value;
            var isAqs = reviewType == ResponseRules.ReviewTypeAqs;
            int nextStatus;

            // Read once. The Outcome's code and the remediation action's are both built from
            // this pair, so resolving it here is what keeps OUT- and REM- pointing at the same
            // submission on a replay - and saves reading the case twice in one transaction.
            var outcomeCase = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet("al_casereference"));
            var caseReference = outcomeCase.GetAttributeValue<string>("al_casereference") ?? caseRef.Id.ToString("D");
            var sequence = review.GetAttributeValue<int?>("al_sequence") ?? 1;

            // The checklist's own trigger, read for both disciplines. Mandatory in each, so
            // a submitted review has always answered it; RemedialActionFlagged still treats
            // an absent answer as No rather than inventing a remediation.
            var remedialFlagged = OutcomeRules.RemedialActionFlagged(
                AnswerChoiceFor(
                    service,
                    targetId,
                    isAqs ? AqsRemedialQuestionCode : TaxRemedialQuestionCode));

            bool requiresRemediation;
            string remediationReason;

            if (isAqs)
            {
                var answer = AnswerChoiceFor(service, targetId, GradeQuestionCode);
                if (!answer.HasValue)
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The advice quality grade has not been recorded, so this review cannot be submitted.");
                }

                int outcomeValue;
                if (!OutcomeRules.TryGradeFromAnswer(answer.Value, out outcomeValue))
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The advice quality grade holds a value this solution does not recognise (" + answer.Value + ").");
                }

                CreateOutcome(service, targetId, caseRef, caseReference, sequence, outcomeValue);
                nextStatus = OutcomeRules.NextCaseStatusForAqs(outcomeValue, remedialFlagged);
                requiresRemediation = OutcomeRules.RequiresRemediation(outcomeValue, remedialFlagged);
                remediationReason = new OptionLabels(service).Label(OutcomeEntity, "al_initialoutcome", outcomeValue);
            }
            else
            {
                var answer = AnswerChoiceFor(service, targetId, TaxOutcomeQuestionCode);
                if (!answer.HasValue)
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The tax check outcome has not been recorded, so this review cannot be submitted.");
                }

                bool taxRequiresRemediation;
                if (!OutcomeRules.TryTaxResultRequiresRemediation(answer.Value, out taxRequiresRemediation))
                {
                    throw new InvalidPluginExecutionException(
                        PreconditionPrefix + "The tax check outcome holds a value this solution does not recognise (" + answer.Value + ").");
                }

                var aqsStillToCome = AqsStillToCome(service, caseRef.Id);
                nextStatus = OutcomeRules.NextCaseStatusForTax(answer.Value, aqsStillToCome, remedialFlagged);
                requiresRemediation = taxRequiresRemediation || remedialFlagged;
                remediationReason = "Tax check: "
                    + new OptionLabels(service).Label(ResponseEntity, "al_answerchoice", answer.Value);
            }

            // BR-006's other half. The case status alone has always said remediation was
            // owed; the action is what an adviser can actually be given, and what the
            // response, the completion, the sign-off and the BR-010 clock all hang off.
            // Inside the submit transaction, so a case can never reach Awaiting Remediation
            // with nothing on the worklist to explain why - which is exactly the state the
            // environment was in before this existed.
            if (requiresRemediation)
            {
                RaiseRemediation(
                    service,
                    targetId,
                    caseRef,
                    caseReference,
                    sequence,
                    remediationReason,
                    isAqs ? AqsObservationQuestionCode : TaxObservationQuestionCode);
            }

            // OutcomeRules.HopsFor is the single description of the route a submit takes:
            // open the case if it was never opened, through Submitted unless this is the Tax
            // handoff to the queue, then on to the destination. It was previously a tested
            // pure function that nothing called, with the plug-in walking the same route by
            // hand — two descriptions that agreed only for as long as both were edited
            // together. CaseTransitions checks each hop against AD-057 in turn.
            CaseTransitions.MoveThrough(
                service,
                caseRef.Id,
                OutcomeRules.HopsFor(CaseTransitions.CurrentStatus(service, caseRef.Id), nextStatus));
        }

        /// <summary>
        /// Writes the initial Outcome. The code is derived from the case reference and the
        /// review sequence, so a replay upserts the same row on the al_outcomecode
        /// alternate key rather than creating a second Outcome (NFR-REL-01).
        /// Only the initial columns are written: the final outcome is the regrade path's
        /// to set, and BR-007 requires both to be preserved separately.
        /// </summary>
        private static void CreateOutcome(
            IOrganizationService service,
            Guid reviewId,
            EntityReference caseRef,
            string caseReference,
            int sequence,
            int outcomeValue)
        {
            var code = "OUT-" + caseReference + "-" + sequence;

            var outcome = new Entity(OutcomeEntity)
            {
                ["al_name"] = "Outcome " + caseReference,
                ["al_outcomecode"] = code,
                ["al_outcomecaseid"] = caseRef,
                ["al_reviewinstanceid"] = new EntityReference(ReviewEntity, reviewId),
                ["al_initialoutcome"] = new OptionSetValue(outcomeValue),
            };

            AssignUserRolePlugin.Upsert(service, OutcomeEntity, "al_outcomecode", code, outcome);
        }

        /// <summary>
        /// Raises the BR-006 remediation action for this submission.
        ///
        /// The observation is the checker's own words from the discipline's fail
        /// observation question, which AD-019 leaves optional - so the description
        /// <see cref="Remediation.Describe"/> builds has to stand without it.
        ///
        /// The action is keyed on the case reference and the review's sequence, so a
        /// replayed submit finds the row it already raised instead of raising a second one,
        /// and an adviser part-way through their response is never sent back to Open.
        /// </summary>
        private static void RaiseRemediation(
            IOrganizationService service,
            Guid reviewId,
            EntityReference caseRef,
            string caseReference,
            int sequence,
            string reason,
            string observationQuestionCode)
        {
            Remediation.Raise(
                service,
                caseRef,
                caseReference,
                reviewId,
                sequence,
                reason,
                AnswerTextFor(service, reviewId, observationQuestionCode),
                Remediation.AdviserContact(service, caseRef),
                DateTime.UtcNow);
        }

        /// <summary>
        /// The al_answertext this review recorded for a question, by business code.
        /// Returns null when the question was not answered.
        /// </summary>
        private static string AnswerTextFor(IOrganizationService service, Guid reviewId, string questionCode)
        {
            var response = AnswerFor(service, reviewId, questionCode, "al_answertext");
            return response == null ? null : response.GetAttributeValue<string>("al_answertext");
        }

        /// <summary>
        /// True when the response holds a value in any typed answer column (AD-023).
        ///
        /// al_answerchoices is a multi-select choice column, so it comes back as an
        /// OptionSetValueCollection. Reading it as a string threw
        /// InvalidCastException before any business rule ran, which made every review
        /// carrying an answered multi-select question impossible to submit — in the V8
        /// checklist that is every Tax review, because Q-TAX-01 is mandatory and
        /// multi-select. ResponseGuardPlugin always read the column at its real type;
        /// this is the same read.
        /// </summary>
        public static bool HasAnswer(Entity response)
        {
            var text = response.GetAttributeValue<string>("al_answertext");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var choices = response.GetAttributeValue<OptionSetValueCollection>("al_answerchoices");
            if (choices != null && choices.Count > 0)
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

        private static void EnsureCaller(IOrganizationService service, Guid callerId, Entity review)
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
                if (owner.Id != callerId)
                {
                    throw new InvalidPluginExecutionException(
                        UnauthorizedPrefix + "Only the checker assigned to this review can submit it.");
                }
                return;
            }

            if (owner.LogicalName == "team")
            {
                if (!IsTeamMember(service, owner.Id, callerId))
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
            Guid actorId,
            Guid correlationId,
            string details,
            string actorName)
        {
            var audit = new Entity(AuditEntity)
            {
                ["al_name"] = "SubmitReview " + targetId.ToString("D"),
                ["al_command"] = new OptionSetValue(CommandSubmitReview),
                ["al_targettable"] = ReviewEntity,
                ["al_targetid"] = targetId.ToString("D"),
                ["al_actorid"] = actorId.ToString("D"),
                ["al_idempotencykey"] = idempotencyKey,
                ["al_correlationid"] = correlationId.ToString("D"),
                ["al_occurredon"] = DateTime.UtcNow,
            };

            // Same reason CommandHelpers.WriteAuditEvent does it: nothing wrote this column,
            // so "who submitted this review?" was answered by createdbyname - the portal's
            // application user on every portal submit, which is every submit there is.
            var resolved = string.IsNullOrWhiteSpace(actorName)
                ? CommandHelpers.ResolveActorName(service, actorId)
                : actorName.Trim();

            if (!string.IsNullOrEmpty(resolved))
            {
                audit["al_actorname"] = resolved;
            }

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
