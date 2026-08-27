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
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), existingAudit.GetAttributeValue<string>("al_details"), existingAudit.Id, false);
                return;
            }

            var current = GetCurrentVersion(userService, questionId);
            if (current == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "The question has no current version to succeed.");
            }

            var today = DateTime.UtcNow.Date;
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
            if (current.Contains("al_displayorder"))
            {
                successor["al_displayorder"] = current.GetAttributeValue<int>("al_displayorder");
            }

            var newVersionId = userService.Create(successor);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandRetireAndSucceed, "RetireAndSucceedQuestion " + code, VersionEntity, newVersionId,
                "Superseded v" + currentNumber, newNumber.ToString(), idempotencyKey, context);

            SetResponse(context, newVersionId.ToString("D"), newNumber.ToString(), auditId, false);
        }

        private static Entity GetCurrentVersion(IOrganizationService service, Guid questionId)
        {
            var query = new QueryExpression(VersionEntity)
            {
                ColumnSet = new ColumnSet("al_versionnumber", "al_responsetype", "al_ismandatory", "al_displayorder"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questionid", ConditionOperator.Equal, questionId);
            query.AddOrder("al_versionnumber", OrderType.Descending);
            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0] : null;
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
