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
        private const string PreconditionPrefix = CommandHelpers.PreconditionPrefix;
        private const string ConflictPrefix = CommandHelpers.ConflictPrefix;

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
                    GuardWrite(service, context, IsGradeAmendment(localPluginContext.OrgSvcFactory.CreateOrganizationService(null), context));
                    return;
                case "Associate":
                case "Disassociate":
                    GuardRelationship(service, context);
                    return;
                default:
                    return;
            }
        }

        /// <summary>
        /// al_AmendOutcome changing a submitted File Quality answer (requirements log row 39): an
        /// Update from SYSTEM - which no app user or portal write can be - that changes the answer
        /// choice and none of the answer's other content or links. Only the submitted lock and the
        /// retired-version rule step aside for it; the answer must still fit the question.
        /// A deny-list, not "the choice alone": at pre-operation the platform has already added
        /// modifiedon and modifiedby to the Target (DEV, 2026-11-17).
        /// </summary>
        public static bool IsGradeAmendment(IOrganizationService systemService, IPluginExecutionContext context)
        {
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            return target != null
                && string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase)
                && target.Contains("al_answerchoice")
                && !AmendmentLeavesAlone.Any(target.Contains)
                && WriteProvenance.IsSystemWrite(systemService, context);
        }

        private static readonly string[] AmendmentLeavesAlone =
        {
            "al_answertext", "al_answerchoices", "al_answerdate", "al_answerrichtext",
            "al_reviewinstanceid", "al_questionversionid", "al_responsecode", "al_name",
        };

        private static void GuardWrite(IOrganizationService service, IPluginExecutionContext context, bool amendment = false)
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

            if (!amendment)
            {
                EnsureNotSubmitted(review);
            }

            // One linked read where version, question and section are all there, which is
            // every answer on a healthy checklist. A broken chain returns no row, and the
            // chained reads below then find the break and give it the refusal it always had.
            Entity section;
            var questionVersion = VersionWithSection(service, questionVersionRef.Id, out section)
                ?? service.Retrieve(
                    "al_questionversion",
                    questionVersionRef.Id,
                    new ColumnSet(VersionColumns));

            var responseType = questionVersion.GetAttributeValue<OptionSetValue>("al_responsetype");
            if (responseType == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question has no response type, so it cannot be answered.");
            }

            // A retired version keeps the answers it already holds (BR-013) but takes no new
            // ones: the page renders the version in force, and an answer written against a
            // superseded one would be a second answer to the same question that nothing
            // reads back consistently.
            if (!amendment && !ResponseRules.IsVersionEffective(
                questionVersion.GetAttributeValue<DateTime?>("al_effectivefrom"),
                questionVersion.GetAttributeValue<DateTime?>("al_effectiveto"),
                DateTime.UtcNow))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question has been replaced by a newer version, so this version can no longer be answered.");
            }

            EnsureSectionBelongsToReview(service, questionVersion, review, section);
            SanitiseRichText(target);
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

        private static readonly string[] VersionColumns =
            { "al_responsetype", "al_questionid", "al_effectivefrom", "al_effectiveto" };

        /// <summary>The section columns the guard reads; AnswerWriter checks a carry against the same ones.</summary>
        internal static readonly string[] SectionColumns =
            { "al_ownerrole", "al_checklistversionid", "al_effectivefrom", "al_effectiveto" };

        /// <summary>
        /// The question version and, through its question, the section it sits in, in one
        /// read. Null when any link of the chain is missing.
        /// </summary>
        private static Entity VersionWithSection(IOrganizationService service, Guid versionId, out Entity section)
        {
            section = null;

            var query = new QueryExpression("al_questionversion")
            {
                ColumnSet = new ColumnSet(VersionColumns),
                TopCount = 1,
            };
            query.Criteria.AddCondition("al_questionversionid", ConditionOperator.Equal, versionId);

            var questionLink = query.AddLink("al_question", "al_questionid", "al_questionid");
            var sectionLink = questionLink.AddLink("al_section", "al_sectionid", "al_sectionid");
            sectionLink.EntityAlias = "s";
            sectionLink.Columns = new ColumnSet(SectionColumns);

            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return null;
            }

            section = new Entity("al_section");
            foreach (var column in SectionColumns)
            {
                var aliased = found[0].GetAttributeValue<AliasedValue>("s." + column);
                if (aliased != null)
                {
                    section[column] = aliased.Value;
                }
            }

            return found[0];
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
            Entity review,
            Entity section)
        {
            if (section == null)
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

                section = service.Retrieve("al_section", sectionRef.Id, new ColumnSet(SectionColumns));
            }

            var reviewType = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            if (reviewType == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This review has no recognised discipline.");
            }

            var sectionRefusal = SectionRefusal(section, reviewType.Value, DateTime.UtcNow);
            if (sectionRefusal != null)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + sectionRefusal);
            }

            var issued = review.GetAttributeValue<EntityReference>("al_checklistversionid");
            var sectionVersion = section.GetAttributeValue<EntityReference>("al_checklistversionid");
            if (issued == null || sectionVersion == null || issued.Id != sectionVersion.Id)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question is not part of the checklist version issued to this review.");
            }
        }

        /// <summary>
        /// Why this section cannot be answered by this review, or null when it can
        /// (AD-123). Public and static so it can be tested with a plain Entity, exactly as
        /// RemediationResponseGuardPlugin.Refusal is. The assembly is signed and carries no
        /// InternalsVisibleTo, so internal would not be reachable from the tests.
        ///
        /// Owner role is membership rather than equality: a Both section is answered by the
        /// Tax review and the AQS review alike, each writing its own responses. Equality
        /// here is what would make a Both section render in both front ends and then refuse
        /// every answer typed into it.
        /// </summary>
        public static string SectionRefusal(Entity section, int reviewType, DateTime asOf)
        {
            var ownerRole = section.GetAttributeValue<OptionSetValue>("al_ownerrole");
            if (ownerRole == null || !SectionRules.OwnerRoleServes(ownerRole.Value, reviewType))
            {
                return "This question belongs to another discipline's section.";
            }

            // A retired section takes no new answers, while the answers it already holds
            // keep resolving (AD-091).
            if (!SectionRules.IsSectionEffective(
                section.GetAttributeValue<DateTime?>("al_effectivefrom"),
                section.GetAttributeValue<DateTime?>("al_effectiveto"),
                asOf))
            {
                return "This section is no longer part of the checklist, so it cannot be answered.";
            }

            return null;
        }

        /// <summary>
        /// Replaces the rich-text answer on the Target with its allow-listed form, so the
        /// row that reaches the database is clean whatever wrote it (item 7, 2026-09-19).
        ///
        /// AnswerWriter already sanitises, and the portal's editor sanitises before it
        /// sends. Neither is the guarantee. This step is pre-operation on al_response
        /// itself, so it is the last thing to touch the row before it is stored, and it
        /// therefore covers the path those two do not: a PATCH straight at an existing
        /// al_response from a browser that holds write permission on the table. That path
        /// reaches here without passing AnswerWriter at all, and until this ran the shape
        /// check would have waved a script through - it asks whether the answer is markup,
        /// not what the markup says.
        ///
        /// Mutating the Target in a pre-operation step is how a plug-in changes what gets
        /// written, and is the same mechanism al_ResponseCodeKey is stamped by below.
        /// </summary>
        private static void SanitiseRichText(Entity target)
        {
            if (target == null || !target.Contains("al_answerrichtext"))
            {
                return;
            }

            target["al_answerrichtext"] = HtmlSanitiser.Clean(
                target.GetAttributeValue<string>("al_answerrichtext"));
        }

        private static void EnsureAnswerShape(Entity target, Entity pre, int responseType)
        {
            var hasText = !string.IsNullOrWhiteSpace(Resolve<string>(target, pre, "al_answertext"));

            // HasText, not IsNullOrWhiteSpace: markup is never whitespace even when it says
            // nothing, so "<p><br></p>" would otherwise count as an answer.
            var hasRichText = HtmlSanitiser.HasText(Resolve<string>(target, pre, "al_answerrichtext"));

            var dateValue = Resolve<object>(target, pre, "al_answerdate");
            var hasDate = dateValue is DateTime;

            var choiceValue = Resolve<OptionSetValue>(target, pre, "al_answerchoice");
            int? choice = choiceValue == null ? (int?)null : choiceValue.Value;

            var choicesValue = Resolve<OptionSetValueCollection>(target, pre, "al_answerchoices");
            var choices = choicesValue == null
                ? new int[0]
                : choicesValue.Select(value => value.Value).ToArray();

            var failure = ResponseRules.ValidateAnswer(
                responseType, hasText, hasDate, choice, choices, hasRichText);
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

            // Each distinct review is checked once. Associating several fail reasons against
            // answers on one review used to re-read that same review row once per response,
            // in a synchronous pre-operation step on the user's critical path - and the
            // answer cannot differ between two responses on the same review.
            var checkedReviews = new HashSet<Guid>();
            foreach (var responseId in CollectResponseIds(context))
            {
                var response = service.Retrieve(
                    ResponseEntity, responseId, new ColumnSet("al_reviewinstanceid"));
                var reviewRef = response.GetAttributeValue<EntityReference>("al_reviewinstanceid");
                if (reviewRef == null || !checkedReviews.Add(reviewRef.Id))
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
