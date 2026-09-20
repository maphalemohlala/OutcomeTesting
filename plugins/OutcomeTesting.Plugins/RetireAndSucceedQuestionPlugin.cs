using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command RetireAndSucceedQuestion (AD-003, AD-004, FR-030/FR-031).
    /// Registered against the Custom API <c>al_RetireAndSucceedQuestion</c>. Published
    /// checklist content is immutable, so an edit is modelled as retiring the current
    /// al_questionversion (setting its effective-to date) and creating a successor version
    /// with the new wording. Submitted reviews keep referencing the frozen version (BR-013).
    /// Enforces the caller holds Edit on <c>question.retire</c> and writes an Audit Event.
    /// </summary>
    public class RetireAndSucceedQuestionPlugin : PluginBase
    {
        private const string InQuestionId = "QuestionId";
        private const string InNewWording = "NewWording";
        private const string InResponseType = "ResponseType";
        private const string InMandatory = "Mandatory";
        private const string InDisplayOrder = "DisplayOrder";
        private const string InEffectiveFrom = "EffectiveFrom";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutNewVersionId = "NewVersionId";
        private const string OutVersionNumber = "VersionNumber";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string VersionEntity = "al_questionversion";
        private const int CommandRetireAndSucceed = 120910776;

        public RetireAndSucceedQuestionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RetireAndSucceedQuestionPlugin))
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
            var newWording = CommandHelpers.GetRequiredString(context, InNewWording);
            var responseTypeOverride = CommandHelpers.GetOptionalString(context, InResponseType);
            var mandatoryOverride = CommandHelpers.GetOptionalString(context, InMandatory);
            var displayOrderOverride = CommandHelpers.GetOptionalString(context, InDisplayOrder);
            var effectiveFromArg = CommandHelpers.GetOptionalString(context, InEffectiveFrom);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandRetireAndSucceed);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), existingAudit.GetAttributeValue<string>("al_details"), existingAudit.Id, false);
                return;
            }

            var current = ChecklistQueries.CurrentVersionOf(
                userService, questionId, "al_versionnumber", "al_responsetype", "al_ismandatory", "al_displayorder");
            if (current == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "The question has no current version to succeed.");
            }

            /*
             * The day the new wording starts being asked (F31).
             *
             * This command hard-coded today, alone among the seven. AD-122 accepts that a
             * mandatory question in force today is owed by every unsubmitted review of that
             * discipline at its next submit, and names the effective-from date as the control
             * for it - "which is why every command exposes the effective-from date". This one
             * did not, and it is the command that changes a question's WORDING, which is the
             * most ordinary edit there is. So the one mitigation the decision relies on was
             * missing exactly where it was argued to apply.
             *
             * Absent is still today, so every existing caller behaves as it did.
             *
             * The retirement and the successor take the SAME day, as they always have: on the
             * changeover day the successor alone is current, which is the contract
             * versionEffective and ResponseRules.IsVersionEffective are both written against.
             * Dating them apart would leave a gap with no version in force.
             */
            var today = AddQuestionPlugin.ParseEffectiveFrom(
                effectiveFromArg, DateTime.UtcNow.Date);
            var currentNumber = current.GetAttributeValue<int>("al_versionnumber");
            var newNumber = currentNumber + 1;

            // Retire the current version by dating it out; its content stays frozen (BR-013).
            userService.Update(new Entity(VersionEntity, current.Id) { ["al_effectiveto"] = today });

            var code = "QV-" + questionId.ToString("N") + "-v" + newNumber;
            var successor = new Entity(VersionEntity)
            {
                ["al_name"] = "Question version v" + newNumber,
                ["al_questionversioncode"] = code,
                ["al_questiontext"] = newWording,
                ["al_versionnumber"] = newNumber,
                ["al_effectivefrom"] = today,
                ["al_questionid"] = new EntityReference("al_question", questionId),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };

            // Carry forward the frozen answer shape and ordering (AD-015, AD-019),
            // unless the editor supplied an override for this new version.
            if (!string.IsNullOrWhiteSpace(responseTypeOverride) && int.TryParse(responseTypeOverride, out var responseTypeValue))
            {
                successor["al_responsetype"] = new OptionSetValue(responseTypeValue);
            }
            else
            {
                var responseType = current.GetAttributeValue<OptionSetValue>("al_responsetype");
                if (responseType != null)
                {
                    successor["al_responsetype"] = new OptionSetValue(responseType.Value);
                }
            }
            if (!string.IsNullOrWhiteSpace(mandatoryOverride) && bool.TryParse(mandatoryOverride, out var mandatoryValue))
            {
                successor["al_ismandatory"] = mandatoryValue;
            }
            else if (current.Contains("al_ismandatory"))
            {
                successor["al_ismandatory"] = current.GetAttributeValue<bool>("al_ismandatory");
            }
            // Display order is frozen on the version (AD-015), so reordering a question is a
            // new version rather than an in-place update - the same shape as the wording,
            // response type and mandatory flag above.
            int displayOrderValue;
            if (!string.IsNullOrWhiteSpace(displayOrderOverride)
                && int.TryParse(displayOrderOverride, out displayOrderValue))
            {
                successor["al_displayorder"] = displayOrderValue;
            }
            else if (current.Contains("al_displayorder"))
            {
                successor["al_displayorder"] = current.GetAttributeValue<int>("al_displayorder");
            }

            var newVersionId = userService.Create(successor);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandRetireAndSucceed, "RetireAndSucceedQuestion " + code, VersionEntity, newVersionId,
                "Superseded v" + currentNumber, newNumber.ToString(), idempotencyKey, context);

            SetResponse(context, newVersionId.ToString("D"), newNumber.ToString(), auditId, false);
        }

        private static void SetResponse(IPluginExecutionContext context, string newVersionId, string versionNumber, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutNewVersionId] = newVersionId;
            context.OutputParameters[OutVersionNumber] = versionNumber;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
