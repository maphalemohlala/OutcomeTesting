using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Synchronous pre-operation guard on al_response Create and Update, and on
    /// Associate/Disassociate of al_failreason_response (AD-053).
    ///
    /// This is the PP-11 submission lock and the AD-023 answer-shape rule enforced below
    /// the Portals Web API, so neither a hand-edited URL nor a direct PATCH can bypass
    /// them (NFR-SEC-01).
    ///
    /// It deliberately performs no authorization. Power Pages Web API writes reach
    /// Dataverse under the site's application user, so InitiatingUserId is not the
    /// checker; treating it as one would be a security hole rather than a check. The
    /// caller gate is the Contact-scoped table permission on al_reviewinstance with
    /// al_response as its child permission (AD-047).
    /// </summary>
    public class ResponseGuardPlugin : PluginBase
    {
        private const string PreconditionPrefix = "PRECONDITION: ";
        private const string ConflictPrefix = "CONFLICT: ";

        private const string ResponseEntity = "al_response";
        private const string ReviewEntity = "al_reviewinstance";
        private const string FailReasonRelationship = "al_failreason_response";

        public ResponseGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ResponseGuardPlugin))
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

            switch (context.MessageName)
            {
                case "Create":
                case "Update":
                    GuardWrite(service, context);
                    return;
                case "Associate":
                case "Disassociate":
                    GuardRelationship(service, context);
                    return;
                default:
                    return;
            }
        }

        private static void GuardWrite(IOrganizationService service, IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("Target"))
            {
                return;
            }

            var target = context.InputParameters["Target"] as Entity;
            if (target == null || target.LogicalName != ResponseEntity)
            {
                return;
            }

            // On Update the Target carries only changed columns, so the review and question
            // links come from the pre-image registered by Register-ResponseGuard.ps1.
            var pre = context.PreEntityImages.Values.FirstOrDefault();

            var reviewRef = target.GetAttributeValue<EntityReference>("al_reviewinstanceid")
                ?? (pre == null ? null : pre.GetAttributeValue<EntityReference>("al_reviewinstanceid"));
            var questionVersionRef = target.GetAttributeValue<EntityReference>("al_questionversionid")
                ?? (pre == null ? null : pre.GetAttributeValue<EntityReference>("al_questionversionid"));

            if (reviewRef == null || questionVersionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "An answer must belong to a review and a question.");
            }

            var review = service.Retrieve(
                ReviewEntity,
                reviewRef.Id,
                new ColumnSet("al_reviewstatus", "al_reviewtype", "al_checklistversionid"));

            EnsureNotSubmitted(review);

            var questionVersion = service.Retrieve(
                "al_questionversion",
                questionVersionRef.Id,
                new ColumnSet("al_responsetype", "al_questionid"));

            var responseType = questionVersion.GetAttributeValue<OptionSetValue>("al_responsetype");
            if (responseType == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question has no response type, so it cannot be answered.");
            }

            EnsureSectionBelongsToReview(service, questionVersion, review);
            EnsureAnswerShape(target, pre, responseType.Value);

            // Stamped server-side, never accepted from the client: al_ResponseCodeKey is what
            // makes a replayed create collide instead of writing a rival answer.
            if (context.MessageName == "Create")
            {
                target["al_responsecode"] = ResponseRules.BuildResponseCode(reviewRef.Id, questionVersionRef.Id);
                if (!target.Contains("al_name"))
                {
                    target["al_name"] = questionVersionRef.Name ?? "Answer";
                }
            }
        }

        private static void EnsureNotSubmitted(Entity review)
        {
            var status = review.GetAttributeValue<OptionSetValue>("al_reviewstatus");
            if (status != null && status.Value == ResponseRules.StatusSubmitted)
            {
                throw new InvalidPluginExecutionException(
                    ConflictPrefix + "This review has been submitted and can no longer be changed.");
            }
        }

        /// <summary>
        /// AD-020 section ownership, and the AD-023 rule that a review answers only the
        /// checklist version issued to it. Both are structural: a Tax review cannot hold an
        /// AQS section's answer even if a request is crafted by hand (PP-08).
        /// </summary>
        private static void EnsureSectionBelongsToReview(
            IOrganizationService service,
            Entity questionVersion,
            Entity review)
        {
            var questionRef = questionVersion.GetAttributeValue<EntityReference>("al_questionid");
            if (questionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question is not attached to a section.");
            }

            var question = service.Retrieve("al_question", questionRef.Id, new ColumnSet("al_sectionid"));
            var sectionRef = question.GetAttributeValue<EntityReference>("al_sectionid");
            if (sectionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question is not attached to a section.");
            }

            var section = service.Retrieve(
                "al_section",
                sectionRef.Id,
                new ColumnSet("al_ownerrole", "al_checklistversionid"));

            var reviewType = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            var ownerRole = section.GetAttributeValue<OptionSetValue>("al_ownerrole");
            if (reviewType == null || ownerRole == null
                || ownerRole.Value != ResponseRules.OwnerRoleFor(reviewType.Value))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question belongs to another discipline's section.");
            }

            var issued = review.GetAttributeValue<EntityReference>("al_checklistversionid");
            var sectionVersion = section.GetAttributeValue<EntityReference>("al_checklistversionid");
            if (issued == null || sectionVersion == null || issued.Id != sectionVersion.Id)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question is not part of the checklist version issued to this review.");
            }
        }

        private static void EnsureAnswerShape(Entity target, Entity pre, int responseType)
        {
            var hasText = !string.IsNullOrWhiteSpace(Resolve<string>(target, pre, "al_answertext"));

            var dateValue = Resolve<object>(target, pre, "al_answerdate");
            var hasDate = dateValue is DateTime;

            var choiceValue = Resolve<OptionSetValue>(target, pre, "al_answerchoice");
            int? choice = choiceValue == null ? (int?)null : choiceValue.Value;

            var choicesValue = Resolve<OptionSetValueCollection>(target, pre, "al_answerchoices");
            var choices = choicesValue == null
                ? new int[0]
                : choicesValue.Select(value => value.Value).ToArray();

            var failure = ResponseRules.ValidateAnswer(responseType, hasText, hasDate, choice, choices);
            if (failure != null)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + failure);
            }
        }

        /// <summary>
        /// The effective value after this write: the Target when it carries the column,
        /// otherwise the pre-image. A Target that explicitly sets a column to null clears it,
        /// which is why Contains is checked before falling back.
        /// </summary>
        private static T Resolve<T>(Entity target, Entity pre, string attribute)
        {
            if (target.Contains(attribute))
            {
                return target.GetAttributeValue<T>(attribute);
            }
            return pre == null ? default(T) : pre.GetAttributeValue<T>(attribute);
        }

        /// <summary>
        /// A fail reason may only be attached to or removed from an answer on a review that
        /// is still open (FR-013, PP-11).
        /// </summary>
        private static void GuardRelationship(IOrganizationService service, IPluginExecutionContext context)
        {
            if (!context.InputParameters.Contains("Relationship"))
            {
                return;
            }

            var relationship = context.InputParameters["Relationship"] as Relationship;
            if (relationship == null
                || !string.Equals(relationship.SchemaName, FailReasonRelationship, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var responseId in CollectResponseIds(context))
            {
                var response = service.Retrieve(
                    ResponseEntity, responseId, new ColumnSet("al_reviewinstanceid"));
                var reviewRef = response.GetAttributeValue<EntityReference>("al_reviewinstanceid");
                if (reviewRef == null)
                {
                    continue;
                }

                var review = service.Retrieve(
                    ReviewEntity, reviewRef.Id, new ColumnSet("al_reviewstatus"));
                EnsureNotSubmitted(review);
            }
        }

        /// <summary>
        /// Either end of the N:N may be the Target, so both are inspected rather than
        /// assuming the caller associated from the response side.
        /// </summary>
        private static IEnumerable<Guid> CollectResponseIds(IPluginExecutionContext context)
        {
            var ids = new List<Guid>();

            if (context.InputParameters.Contains("Target"))
            {
                var target = context.InputParameters["Target"] as EntityReference;
                if (target != null && target.LogicalName == ResponseEntity)
                {
                    ids.Add(target.Id);
                }
            }

            if (context.InputParameters.Contains("RelatedEntities"))
            {
                var related = context.InputParameters["RelatedEntities"] as EntityReferenceCollection;
                if (related != null)
                {
                    ids.AddRange(related.Where(r => r.LogicalName == ResponseEntity).Select(r => r.Id));
                }
            }

            return ids.Distinct();
        }
    }
}
