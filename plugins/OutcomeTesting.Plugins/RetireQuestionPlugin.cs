using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command RetireQuestion (AD-003, AD-122). Stamps al_effectiveto on the
    /// question's current version and creates no successor, which is the whole difference
    /// from RetireAndSucceedQuestion.
    ///
    /// The version stays Active: statecode is never consulted (AD-091), so every answer
    /// already recorded against it keeps resolving. The question simply stops being asked.
    /// </summary>
    public class RetireQuestionPlugin : PluginBase
    {
        private const string InQuestionId = "QuestionId";
        private const string InEffectiveTo = "EffectiveTo";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutQuestionId = "QuestionId";
        private const string OutRetiredVersionId = "RetiredVersionId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const int CommandRetireQuestion = 120910794;

        public RetireQuestionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RetireQuestionPlugin))
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

            var questionId = CommandHelpers.ParseRequiredGuid(context, InQuestionId);
            var effectiveToArg = CommandHelpers.GetOptionalString(context, InEffectiveTo);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandRetireQuestion);
            if (existingAudit != null)
            {
                SetResponse(
                    context, questionId.ToString("D"),
                    existingAudit.GetAttributeValue<string>("al_targetid"), existingAudit.Id);
                return;
            }

            var question = userService.Retrieve(
                QuestionEntity, questionId, new ColumnSet("al_questioncode", "al_name"));
            var questionCode = question.GetAttributeValue<string>("al_questioncode");

            // Some questions are read by code, not only by reviewers. Retiring one does not
            // degrade the checklist, it stops the system producing outcomes (AD-122).
            var protectedReason = ChecklistGuards.ProtectedReason(questionCode);
            if (protectedReason != null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + protectedReason +
                    " It cannot be retired without a code change.");
            }

            var effectiveTo = ParseEffectiveTo(effectiveToArg, DateTime.UtcNow.Date);
            var current = CurrentVersion(userService, questionId);
            if (current == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "That question has no version to retire.");
            }

            userService.Update(new Entity(VersionEntity, current.Id) { ["al_effectiveto"] = effectiveTo });

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandRetireQuestion, "RetireQuestion " + questionCode,
                VersionEntity, current.Id, reason,
                effectiveTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                idempotencyKey, context);

            SetResponse(context, questionId.ToString("D"), current.Id.ToString("D"), auditId);
        }

        /// <summary>
        /// The day the question stops being asked. Absent is today; later is allowed so a
        /// change can be announced ahead of time; earlier is refused, because a question
        /// that was in force when a review answered it must stay so.
        /// </summary>
        public static DateTime ParseEffectiveTo(string value, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return today;
            }

            DateTime parsed;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective to must be a date in the form yyyy-MM-dd.");
            }

            var day = parsed.Date;
            if (day < today.Date)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective to cannot be in the past, because a question that was in force when a review answered it must stay so.");
            }

            return day;
        }

        private static Entity CurrentVersion(IOrganizationService service, Guid questionId)
        {
            var query = new QueryExpression(VersionEntity)
            {
                ColumnSet = new ColumnSet("al_versionnumber", "al_effectiveto"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questionid", ConditionOperator.Equal, questionId);
            query.AddOrder("al_versionnumber", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0] : null;
        }

        private static void SetResponse(
            IPluginExecutionContext context, string questionId, string versionId, Guid auditId)
        {
            context.OutputParameters[OutQuestionId] = questionId;
            context.OutputParameters[OutRetiredVersionId] = versionId;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = false;
        }
    }
}
