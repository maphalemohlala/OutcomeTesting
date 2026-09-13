using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command MoveQuestion (AD-003, AD-122). Retires the question in its old
    /// section and creates it in the target, carrying the same wording, response type and
    /// mandatory flag.
    ///
    /// Not a lookup update. al_sectionid lives on al_question and is not versioned, so
    /// changing it in place re-files every answer ever recorded - for every submitted
    /// review at once - under a section they were never answered in.
    ///
    /// Both writes happen in one plug-in execution and therefore one transaction, so a move
    /// cannot half-complete and leave a question retired in one section and absent from the
    /// other.
    ///
    /// A move between sections owned by different roles changes which discipline owes the
    /// question. That is the point of the action and is not guarded; the caller states it.
    /// </summary>
    public class MoveQuestionPlugin : PluginBase
    {
        private const string InQuestionId = "QuestionId";
        private const string InTargetSectionId = "TargetSectionId";
        private const string InNewQuestionCode = "NewQuestionCode";
        private const string InEffectiveFrom = "EffectiveFrom";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutRetiredVersionId = "RetiredVersionId";
        private const string OutNewQuestionId = "NewQuestionId";
        private const string OutNewVersionId = "NewVersionId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string SectionEntity = "al_section";
        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const int CommandMoveQuestion = 120910795;

        public MoveQuestionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(MoveQuestionPlugin))
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
            var targetSectionId = CommandHelpers.ParseRequiredGuid(context, InTargetSectionId);
            var newCode = CommandHelpers.GetRequiredString(context, InNewQuestionCode).Trim();
            var effectiveFromArg = CommandHelpers.GetOptionalString(context, InEffectiveFrom);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandMoveQuestion);
            if (existingAudit != null)
            {
                SetResponse(
                    context,
                    string.Empty,
                    existingAudit.GetAttributeValue<string>("al_targetid"),
                    existingAudit.GetAttributeValue<string>("al_details"),
                    existingAudit.Id);
                return;
            }

            var question = userService.Retrieve(
                QuestionEntity, questionId,
                new ColumnSet("al_questioncode", "al_name", "al_sectionid", "al_displayorder"));
            var questionCode = question.GetAttributeValue<string>("al_questioncode");
            var fromSection = question.GetAttributeValue<EntityReference>("al_sectionid");

            var refusal = RefusalFor(
                questionCode, fromSection == null ? Guid.Empty : fromSection.Id, targetSectionId);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }

            var target = userService.Retrieve(
                SectionEntity, targetSectionId,
                new ColumnSet("al_effectivefrom", "al_effectiveto", "al_name"));
            if (!SectionRules.IsSectionEffective(
                target.GetAttributeValue<DateTime?>("al_effectivefrom"),
                target.GetAttributeValue<DateTime?>("al_effectiveto"),
                DateTime.UtcNow))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "The target section is no longer part of the checklist.");
            }

            ChecklistQueries.EnsureQuestionCodeIsFree(userService, newCode);

            var today = DateTime.UtcNow.Date;
            var effectiveFrom = AddQuestionPlugin.ParseEffectiveFrom(effectiveFromArg, today);

            // Retire where it was. The version stays Active so its answers keep resolving.
            // One read carries everything the new version copies forward.
            var current = ChecklistQueries.CurrentVersionOf(
                userService, questionId,
                "al_versionnumber", "al_effectiveto", "al_questiontext", "al_responsetype", "al_ismandatory", "al_displayorder");
            if (current == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "That question has no version to move.");
            }

            var retiredRefusal = RetiredRefusal(current.GetAttributeValue<DateTime?>("al_effectiveto"), today);
            if (retiredRefusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + retiredRefusal);
            }

            // Dated out on the day the new version starts, not today. A move announced ahead
            // of time used to leave the question asked in neither section until that day: the
            // old version ended today and the new one had not begun. The window is out of
            // force from the start of effective-to (ResponseRules.IsVersionEffective), so
            // ending the old on the day the new begins hands over with neither a gap nor a
            // day on which both sections ask it.
            userService.Update(new Entity(VersionEntity, current.Id) { ["al_effectiveto"] = effectiveFrom });

            // Create it where it is going, carrying the frozen answer shape forward.
            var newQuestionId = userService.Create(new Entity(QuestionEntity)
            {
                ["al_name"] = question.GetAttributeValue<string>("al_name"),
                ["al_questioncode"] = newCode,
                ["al_displayorder"] = question.GetAttributeValue<int>("al_displayorder"),
                ["al_sectionid"] = new EntityReference(SectionEntity, targetSectionId),
            });

            var newVersion = new Entity(VersionEntity)
            {
                ["al_name"] = "Question version v1",
                ["al_questionversioncode"] = "QV-" + newQuestionId.ToString("N") + "-v1",
                ["al_questiontext"] = current.GetAttributeValue<string>("al_questiontext"),
                ["al_versionnumber"] = 1,
                ["al_effectivefrom"] = effectiveFrom,
                ["al_ismandatory"] = current.GetAttributeValue<bool>("al_ismandatory"),
                ["al_displayorder"] = current.GetAttributeValue<int>("al_displayorder"),
                ["al_questionid"] = new EntityReference(QuestionEntity, newQuestionId),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };

            var responseType = current.GetAttributeValue<OptionSetValue>("al_responsetype");
            if (responseType != null)
            {
                newVersion["al_responsetype"] = new OptionSetValue(responseType.Value);
            }

            var newVersionId = userService.Create(newVersion);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandMoveQuestion,
                "MoveQuestion " + questionCode + " -> " + newCode,
                QuestionEntity, newQuestionId,
                reason + " (moved to " + target.GetAttributeValue<string>("al_name") + ")",
                newVersionId.ToString("D"), idempotencyKey, context);

            SetResponse(
                context, current.Id.ToString("D"), newQuestionId.ToString("D"),
                newVersionId.ToString("D"), auditId);
        }

        /// <summary>
        /// Why this question cannot move, or null when it can. A protected code is refused
        /// because a move retires the original just as surely as a retire does; a move to
        /// the section it is already in is refused because it would burn the old code -
        /// codes are unique and never freed - for no change.
        /// </summary>
        public static string RefusalFor(string questionCode, Guid fromSectionId, Guid toSectionId)
        {
            var protectedReason = ChecklistGuards.ProtectedReason(questionCode);
            if (protectedReason != null)
            {
                return protectedReason + " It cannot be moved without a code change.";
            }

            if (fromSectionId != Guid.Empty && fromSectionId == toSectionId)
            {
                return "That question is already in the section you are moving it to.";
            }

            return null;
        }

        /// <summary>
        /// Why a question already out of force cannot move, or null while it is live or
        /// dated out only in the future. Moving a retired question would date its version
        /// out again - later than it was - and so bring it back into force for the gap,
        /// changing what every review submitted in between owed.
        /// </summary>
        public static string RetiredRefusal(DateTime? currentEffectiveTo, DateTime today)
        {
            if (currentEffectiveTo.HasValue && currentEffectiveTo.Value.Date <= today.Date)
            {
                return "That question is already retired, so it cannot be moved. Add it to the target section as a new question instead.";
            }

            return null;
        }

        private static void SetResponse(
            IPluginExecutionContext context,
            string retiredVersionId,
            string newQuestionId,
            string newVersionId,
            Guid auditId)
        {
            context.OutputParameters[OutRetiredVersionId] = retiredVersionId;
            context.OutputParameters[OutNewQuestionId] = newQuestionId;
            context.OutputParameters[OutNewVersionId] = newVersionId;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = false;
        }
    }
}
