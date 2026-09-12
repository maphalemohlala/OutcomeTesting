using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AddSection (AD-003, AD-123). Creates a section under the
    /// checklist version in force, with its questions, in one transaction.
    ///
    /// A new section renders in both front ends without a code change: formBlocks falls
    /// through to a generic block, and the portal template defaults its title and layout
    /// before its dispatch chain. It will not be folded into one of the document's designed
    /// blocks - that mapping is hand-written (AD-098) and stays so.
    /// </summary>
    public class AddSectionPlugin : PluginBase
    {
        private const string SectionEntity = "al_section";
        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const string ChecklistVersionEntity = "al_checklistversion";
        private const int CommandAddSection = 120910796;

        public AddSectionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AddSectionPlugin))
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

            var sectionCode = CommandHelpers.GetRequiredString(context, "SectionCode").Trim();
            var name = CommandHelpers.GetRequiredString(context, "Name").Trim();
            var helpText = CommandHelpers.GetOptionalString(context, "HelpText");
            var ownerRoleArg = CommandHelpers.GetRequiredString(context, "OwnerRole");
            var displayOrderArg = CommandHelpers.GetOptionalString(context, "DisplayOrder");
            var isOptionalArg = CommandHelpers.GetOptionalString(context, "IsOptional");
            var effectiveFromArg = CommandHelpers.GetOptionalString(context, "EffectiveFrom");
            var questionsArg = CommandHelpers.GetOptionalString(context, "Questions");
            var idempotencyKey = CommandHelpers.GetRequiredString(context, "IdempotencyKey");

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandAddSection);
            if (existingAudit != null)
            {
                SetResponse(
                    context,
                    existingAudit.GetAttributeValue<string>("al_targetid"),
                    existingAudit.GetAttributeValue<string>("al_details"),
                    existingAudit.Id);
                return;
            }

            int ownerRole;
            if (!int.TryParse(ownerRoleArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out ownerRole)
                || !IsAssignableOwnerRole(ownerRole))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Owner role must be Tax (120910100), AQS (120910101) or Both (120910105).");
            }

            // Parsed before anything is written, so a bad element refuses the call rather
            // than leaving a section with half its questions.
            var questions = SectionQuestionSpec.ParseMany(questionsArg);

            var today = DateTime.UtcNow.Date;
            var effectiveFrom = AddQuestionPlugin.ParseEffectiveFrom(effectiveFromArg, today);
            var isOptional = !string.IsNullOrWhiteSpace(isOptionalArg)
                && string.Equals(isOptionalArg.Trim(), "true", StringComparison.OrdinalIgnoreCase);

            EnsureSectionCodeIsFree(userService, sectionCode);
            foreach (var spec in questions)
            {
                EnsureQuestionCodeIsFree(userService, spec.Code);
            }

            var checklistVersionId = ResolveChecklistVersion(userService);
            var displayOrder = ResolveDisplayOrder(userService, checklistVersionId, displayOrderArg);

            var section = new Entity(SectionEntity)
            {
                ["al_name"] = name,
                ["al_sectioncode"] = sectionCode,
                ["al_ownerrole"] = new OptionSetValue(ownerRole),
                ["al_displayorder"] = displayOrder,
                ["al_isconditional"] = false,
                ["al_isoptional"] = isOptional,
                ["al_effectivefrom"] = effectiveFrom,
                ["al_checklistversionid"] = new EntityReference(ChecklistVersionEntity, checklistVersionId),
            };
            if (!string.IsNullOrWhiteSpace(helpText))
            {
                section["al_helptext"] = helpText;
            }

            var sectionId = userService.Create(section);

            var createdIds = new List<string>();
            foreach (var spec in questions)
            {
                var questionId = userService.Create(new Entity(QuestionEntity)
                {
                    ["al_name"] = spec.Name.Trim(),
                    ["al_questioncode"] = spec.Code,
                    ["al_displayorder"] = spec.DisplayOrder,
                    ["al_sectionid"] = new EntityReference(SectionEntity, sectionId),
                });

                userService.Create(new Entity(VersionEntity)
                {
                    ["al_name"] = "Question version v1",
                    ["al_questionversioncode"] = "QV-" + questionId.ToString("N") + "-v1",
                    ["al_questiontext"] = spec.Wording.Trim(),
                    ["al_versionnumber"] = 1,
                    ["al_effectivefrom"] = effectiveFrom,
                    ["al_responsetype"] = new OptionSetValue(spec.ResponseType.Value),
                    ["al_ismandatory"] = spec.Mandatory,
                    ["al_displayorder"] = spec.DisplayOrder,
                    ["al_questionid"] = new EntityReference(QuestionEntity, questionId),
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                });

                createdIds.Add(questionId.ToString("D"));
            }

            var questionIds = string.Join(",", createdIds.ToArray());
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandAddSection, "AddSection " + sectionCode,
                SectionEntity, sectionId,
                "Owner role " + ownerRole + ", " + createdIds.Count + " question(s)",
                questionIds, idempotencyKey, context);

            SetResponse(context, sectionId.ToString("D"), questionIds, auditId);
        }

        /// <summary>
        /// Tax, AQS or Both. Adviser, T&amp;C Manager and Manager / Admin are valid owner
        /// roles that no review is ever opened as, so a section owned by one is owed by
        /// nobody - refused rather than created as something invisible.
        /// </summary>
        private static bool IsAssignableOwnerRole(int ownerRole)
        {
            return ownerRole == ResponseRules.OwnerRoleTaxTeam
                || ownerRole == ResponseRules.OwnerRoleAqsChecker
                || ownerRole == SectionRules.OwnerRoleBoth;
        }

        private static void EnsureSectionCodeIsFree(IOrganizationService service, string sectionCode)
        {
            var query = new QueryExpression(SectionEntity)
            {
                ColumnSet = new ColumnSet("al_sectionid"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_sectioncode", ConditionOperator.Equal, sectionCode);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Section code '" + sectionCode + "' is already in use.");
            }
        }

        /// <summary>
        /// Checked for every question before the section is created, not as each is written:
        /// a collision on the third question would otherwise roll back a transaction that
        /// had looked like it was succeeding.
        /// </summary>
        private static void EnsureQuestionCodeIsFree(IOrganizationService service, string questionCode)
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

        /// <summary>
        /// The checklist version in force today, resolved as ClaimCasePlugin resolves it:
        /// the window is applied in memory rather than in the query, because a date-range
        /// condition would put the answer at the mercy of how the platform compares a
        /// date-only column to a UTC timestamp.
        /// </summary>
        private static Guid ResolveChecklistVersion(IOrganizationService service)
        {
            var query = new QueryExpression(ChecklistVersionEntity)
            {
                ColumnSet = new ColumnSet("al_effectivefrom", "al_effectiveto"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) },
                },
                Orders = { new OrderExpression("al_effectivefrom", OrderType.Descending) },
            };

            var today = DateTime.UtcNow.Date;
            foreach (var version in service.RetrieveMultiple(query).Entities)
            {
                var from = version.GetAttributeValue<DateTime?>("al_effectivefrom");
                var to = version.GetAttributeValue<DateTime?>("al_effectiveto");

                if ((!from.HasValue || from.Value.Date <= today) && (!to.HasValue || to.Value.Date >= today))
                {
                    return version.Id;
                }
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "No checklist version is in force, so a section cannot be added (BR-013).");
        }

        private static int ResolveDisplayOrder(
            IOrganizationService service, Guid checklistVersionId, string displayOrderArg)
        {
            int explicitOrder;
            if (!string.IsNullOrWhiteSpace(displayOrderArg)
                && int.TryParse(displayOrderArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out explicitOrder))
            {
                return explicitOrder;
            }

            var query = new QueryExpression(SectionEntity)
            {
                ColumnSet = new ColumnSet("al_displayorder"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_checklistversionid", ConditionOperator.Equal, checklistVersionId);
            query.AddOrder("al_displayorder", OrderType.Descending);

            var highest = service.RetrieveMultiple(query).Entities;
            return highest.Count == 0 ? 1 : highest[0].GetAttributeValue<int>("al_displayorder") + 1;
        }

        private static void SetResponse(
            IPluginExecutionContext context, string sectionId, string questionIds, Guid auditId)
        {
            context.OutputParameters["SectionId"] = sectionId;
            context.OutputParameters["QuestionIds"] = questionIds ?? string.Empty;
            context.OutputParameters["AuditEventId"] = auditId.ToString("D");
            context.OutputParameters["Conflict"] = false;
        }
    }
}
