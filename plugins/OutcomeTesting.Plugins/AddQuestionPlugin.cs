using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AddQuestion (AD-003, AD-122). Creates an al_question and its
    /// first al_questionversion in one transaction, in the section named.
    ///
    /// The question is added to the checklist version already in force rather than by
    /// reissuing the checklist: effective dating is what protects history, because every
    /// read path is date-scoped and a submitted review reads as of its submission day
    /// (AD-091). A mandatory question in force today is owed by every unsubmitted review of
    /// that discipline at its next submit, which is what EffectiveFrom is for.
    /// </summary>
    public class AddQuestionPlugin : PluginBase
    {
        private const string InSectionId = "SectionId";
        private const string InQuestionCode = "QuestionCode";
        private const string InName = "Name";
        private const string InWording = "Wording";
        private const string InResponseType = "ResponseType";
        private const string InMandatory = "Mandatory";
        private const string InDisplayOrder = "DisplayOrder";
        private const string InEffectiveFrom = "EffectiveFrom";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutQuestionId = "QuestionId";
        private const string OutVersionId = "VersionId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const int CommandAddQuestion = 120910793;

        public AddQuestionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AddQuestionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var sectionId = CommandHelpers.ParseRequiredGuid(context, InSectionId);
            var questionCode = CommandHelpers.GetRequiredString(context, InQuestionCode).Trim();
            var name = CommandHelpers.GetRequiredString(context, InName).Trim();
            var wording = CommandHelpers.GetRequiredString(context, InWording).Trim();
            var responseTypeArg = CommandHelpers.GetRequiredString(context, InResponseType);
            var mandatoryArg = CommandHelpers.GetOptionalString(context, InMandatory);
            var displayOrderArg = CommandHelpers.GetOptionalString(context, InDisplayOrder);
            var effectiveFromArg = CommandHelpers.GetOptionalString(context, InEffectiveFrom);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandAddQuestion);
            if (existingAudit != null)
            {
                SetResponse(
                    context,
                    existingAudit.GetAttributeValue<string>("al_details"),
                    existingAudit.GetAttributeValue<string>("al_targetid"),
                    existingAudit.Id);
                return;
            }

            int responseType;
            if (!int.TryParse(responseTypeArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out responseType))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Response type must be a numeric option value.");
            }

            var effectiveFrom = ParseEffectiveFrom(effectiveFromArg, DateTime.UtcNow.Date);
            var mandatory = string.IsNullOrWhiteSpace(mandatoryArg) || ReadBool(mandatoryArg);

            // The section must exist and still be in force: adding a question to a section
            // that has been dated out creates content no reviewer will ever see.
            var section = userService.Retrieve(
                "al_section", sectionId, new ColumnSet("al_effectivefrom", "al_effectiveto", "al_name"));
            if (!SectionRules.IsSectionEffective(
                section.GetAttributeValue<DateTime?>("al_effectivefrom"),
                section.GetAttributeValue<DateTime?>("al_effectiveto"),
                DateTime.UtcNow))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "That section is no longer part of the checklist, so a question cannot be added to it.");
            }

            EnsureCodeIsFree(userService, questionCode);

            var displayOrder = ResolveDisplayOrder(userService, sectionId, displayOrderArg);

            var question = new Entity(QuestionEntity)
            {
                ["al_name"] = name,
                ["al_questioncode"] = questionCode,
                ["al_displayorder"] = displayOrder,
                ["al_sectionid"] = new EntityReference("al_section", sectionId),
            };
            var questionId = userService.Create(question);

            var code = "QV-" + questionId.ToString("N") + "-v1";
            var version = new Entity(VersionEntity)
            {
                ["al_name"] = "Question version v1",
                ["al_questionversioncode"] = code,
                ["al_questiontext"] = wording,
                ["al_versionnumber"] = 1,
                ["al_effectivefrom"] = effectiveFrom,
                ["al_responsetype"] = new OptionSetValue(responseType),
                ["al_ismandatory"] = mandatory,
                ["al_displayorder"] = displayOrder,
                ["al_questionid"] = new EntityReference(QuestionEntity, questionId),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };
            var versionId = userService.Create(version);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandAddQuestion, "AddQuestion " + questionCode, QuestionEntity, questionId,
                "Added to section " + section.GetAttributeValue<string>("al_name"),
                versionId.ToString("D"), idempotencyKey, context);

            SetResponse(context, versionId.ToString("D"), questionId.ToString("D"), auditId);
        }

        /// <summary>
        /// The day this question starts being asked. Absent is today; a future day lets an
        /// administrator leave in-flight reviews on the set they started with; the past is
        /// refused, because a question cannot retrospectively have been owed by a review
        /// that has already been answered.
        ///
        /// Public and static so it can be tested directly — the assembly is signed and
        /// carries no InternalsVisibleTo.
        /// </summary>
        public static DateTime ParseEffectiveFrom(string value, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return today;
            }

            DateTime parsed;
            if (!DateTime.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective from must be a date in the form yyyy-MM-dd.");
            }

            // Read as a day, not a moment: these columns are date-only, and comparing one
            // against a timestamp is what AD-091 was raised to stop.
            var day = parsed.Date;
            if (day < today.Date)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective from cannot be in the past, because a question cannot have been owed by a review already answered.");
            }

            return day;
        }

        private static bool ReadBool(string value)
        {
            bool parsed;
            if (!bool.TryParse(value, out parsed))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Mandatory must be 'true' or 'false'.");
            }

            return parsed;
        }

        /// <summary>
        /// al_questioncode carries the al_questioncodekey alternate key, so a duplicate
        /// collides at the platform. Caught here so the caller reads a sentence about the
        /// code rather than a key-violation stack.
        /// </summary>
        private static void EnsureCodeIsFree(IOrganizationService service, string questionCode)
        {
            var query = new QueryExpression(QuestionEntity)
            {
                ColumnSet = new ColumnSet("al_questionid"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questioncode", ConditionOperator.Equal, questionCode);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Question code '" + questionCode + "' is already in use.");
            }
        }

        /// <summary>Explicit order where given, otherwise after the highest in the section.</summary>
        private static int ResolveDisplayOrder(
            IOrganizationService service, Guid sectionId, string displayOrderArg)
        {
            int explicitOrder;
            if (!string.IsNullOrWhiteSpace(displayOrderArg)
                && int.TryParse(displayOrderArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out explicitOrder))
            {
                return explicitOrder;
            }

            var query = new QueryExpression(QuestionEntity)
            {
                ColumnSet = new ColumnSet("al_displayorder"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_sectionid", ConditionOperator.Equal, sectionId);
            query.AddOrder("al_displayorder", OrderType.Descending);

            var highest = service.RetrieveMultiple(query).Entities;
            return highest.Count == 0 ? 1 : highest[0].GetAttributeValue<int>("al_displayorder") + 1;
        }

        private static void SetResponse(
            IPluginExecutionContext context, string versionId, string questionId, Guid auditId)
        {
            context.OutputParameters[OutQuestionId] = questionId;
            context.OutputParameters[OutVersionId] = versionId;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = false;
        }
    }
}
