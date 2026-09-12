using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command RetireSection (AD-003, AD-123). Stamps al_effectiveto on the
    /// section, which is what removes its questions from the form and from the submit gate.
    ///
    /// The questions' own versions are deliberately left alone: the answers they hold keep
    /// resolving (AD-091), and dating them out as well would be a second, redundant edit to
    /// history that an un-retire could never undo cleanly.
    /// </summary>
    public class RetireSectionPlugin : PluginBase
    {
        private const string InSectionId = "SectionId";
        private const string InEffectiveTo = "EffectiveTo";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string SectionEntity = "al_section";
        private const string QuestionEntity = "al_question";
        private const int CommandRetireSection = 120910797;

        public RetireSectionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RetireSectionPlugin))
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
            var effectiveToArg = CommandHelpers.GetOptionalString(context, InEffectiveTo);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandRetireSection);
            if (existingAudit != null)
            {
                SetResponse(context, sectionId.ToString("D"), existingAudit.Id);
                return;
            }

            var section = userService.Retrieve(
                SectionEntity, sectionId, new ColumnSet("al_sectioncode", "al_name"));

            // Retiring the section removes its questions from the form and the gate, so a
            // load-bearing code inside it is refused exactly as retiring that question
            // directly would be (AD-122).
            var refusal = ProtectedCodeIn(QuestionCodesIn(userService, sectionId));
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }

            var effectiveTo = RetireQuestionPlugin.ParseEffectiveTo(effectiveToArg, DateTime.UtcNow.Date);

            userService.Update(new Entity(SectionEntity, sectionId) { ["al_effectiveto"] = effectiveTo });

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandRetireSection,
                "RetireSection " + section.GetAttributeValue<string>("al_sectioncode"),
                SectionEntity, sectionId, reason,
                effectiveTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                idempotencyKey, context);

            SetResponse(context, sectionId.ToString("D"), auditId);
        }

        /// <summary>
        /// The refusal for the first load-bearing code this section holds, or null when it
        /// holds none. A null code is skipped rather than thrown on: a guard that fails on
        /// the way to refusing is no guard.
        /// </summary>
        public static string ProtectedCodeIn(IEnumerable<string> questionCodes)
        {
            foreach (var code in questionCodes)
            {
                var reason = ChecklistGuards.ProtectedReason(code);
                if (reason != null)
                {
                    return reason +
                        " Retiring the section that holds it would take it out of the form, so the section cannot be retired.";
                }
            }

            return null;
        }

        /// <summary>Every question code in the section, for the guard above.</summary>
        public static IEnumerable<string> QuestionCodesIn(IOrganizationService service, Guid sectionId)
        {
            var query = new QueryExpression(QuestionEntity)
            {
                ColumnSet = new ColumnSet("al_questioncode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_sectionid", ConditionOperator.Equal, sectionId);

            var codes = new List<string>();
            foreach (var row in service.RetrieveMultiple(query).Entities)
            {
                codes.Add(row.GetAttributeValue<string>("al_questioncode"));
            }

            return codes;
        }

        private static void SetResponse(IPluginExecutionContext context, string sectionId, Guid auditId)
        {
            context.OutputParameters["SectionId"] = sectionId;
            context.OutputParameters["AuditEventId"] = auditId.ToString("D");
            context.OutputParameters["Conflict"] = false;
        }
    }
}
