using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command GenerateExport (AD-003, AD-039, AD-034 manual only). Registered
    /// against the Custom API <c>al_GenerateExport</c>. Snapshots each Closed case into an
    /// al_exportrecord in the AD-039 20-column Trail Light shape, so the delivered values
    /// are preserved for reconciliation (BR-012). Enforces the caller holds Edit on
    /// <c>export.generate</c>, is idempotent per batch, and writes an Audit Event.
    /// </summary>
    public class GenerateExportPlugin : PluginBase
    {
        private const string InBatchId = "BatchId";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutBatchId = "BatchId";
        private const string OutRowCount = "RowCount";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string BatchEntity = "al_exportbatch";
        private const string RecordEntity = "al_exportrecord";
        private const string CaseEntity = "al_outcomecase";
        private const string OutcomeEntity = "al_outcome";
        private const int CaseStatusClosed = 120910591;
        private const int BatchStatusGenerated = 120910771;
        private const int CommandGenerateExport = 120910775;

        public GenerateExportPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(GenerateExportPlugin))
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

            var batchId = CommandHelpers.ParseRequiredGuid(context, InBatchId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, "export.generate", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey);
            if (existingAudit != null)
            {
                var priorCount = existingAudit.GetAttributeValue<string>("al_details");
                SetResponse(context, batchId.ToString("D"), priorCount ?? "0", "Generated", existingAudit.Id, false);
                return;
            }

            var batch = userService.Retrieve(BatchEntity, batchId, new ColumnSet("al_exportbatchcode"));
            var batchCode = batch.GetAttributeValue<string>("al_exportbatchcode");
            var batchRef = new EntityReference(BatchEntity, batchId);

            var cases = new QueryExpression(CaseEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_casereference", "al_advisername", "al_advisercode", "al_paraplanner", "al_paraplannercode",
                    "al_casetype", "al_productsolutiontype", "al_checkdate", "al_clientname", "al_preorpostcheck"),
                Criteria = new FilterExpression(),
            };
            cases.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            cases.Criteria.AddCondition("al_casestatus", ConditionOperator.Equal, CaseStatusClosed);

            var rows = 0;
            foreach (var outcomeCase in userService.RetrieveMultiple(cases).Entities)
            {
                var caseRef = outcomeCase.GetAttributeValue<string>("al_casereference") ?? outcomeCase.Id.ToString("D");
                var adviceGrade = ResolveAdviceGrade(userService, outcomeCase.Id);
                var code = "EXR-" + batchCode + "-" + caseRef;

                var record = new Entity(RecordEntity)
                {
                    ["al_name"] = "Export " + caseRef,
                    ["al_exportrecordcode"] = code,
                    ["al_exportbatchid"] = batchRef,
                    ["al_outcomecaseid"] = new EntityReference(CaseEntity, outcomeCase.Id),
                    ["al_advisername"] = outcomeCase.GetAttributeValue<string>("al_advisername"),
                    ["al_advisercode"] = outcomeCase.GetAttributeValue<string>("al_advisercode"),
                    ["al_paraplannername"] = outcomeCase.GetAttributeValue<string>("al_paraplanner"),
                    ["al_paraplannercode"] = outcomeCase.GetAttributeValue<string>("al_paraplannercode"),
                    ["al_casetype"] = Formatted(outcomeCase, "al_casetype"),
                    ["al_productsolutiontype"] = Formatted(outcomeCase, "al_productsolutiontype"),
                    ["al_clientname"] = outcomeCase.GetAttributeValue<string>("al_clientname"),
                    ["al_preorpostcheck"] = Formatted(outcomeCase, "al_preorpostcheck"),
                    ["al_advicequalitygrade"] = adviceGrade,
                    ["al_separator"] = string.Empty,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };

                var checkDate = outcomeCase.GetAttributeValue<DateTime?>("al_checkdate");
                if (checkDate.HasValue)
                {
                    record["al_checkdate"] = checkDate.Value;
                }

                AssignUserRolePlugin.Upsert(userService, RecordEntity, "al_exportrecordcode", code, record);
                rows++;
            }

            var update = new Entity(BatchEntity, batchId)
            {
                ["al_batchstatus"] = new OptionSetValue(BatchStatusGenerated),
                ["al_rowcount"] = rows,
                ["al_generatedon"] = DateTime.UtcNow,
            };
            userService.Update(update);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandGenerateExport, "GenerateExport " + batchCode, BatchEntity, batchId,
                null, rows.ToString(), idempotencyKey, context);

            SetResponse(context, batchId.ToString("D"), rows.ToString(), "Generated", auditId, false);
        }

        private static string Formatted(Entity entity, string attribute)
        {
            return entity.FormattedValues.ContainsKey(attribute) ? entity.FormattedValues[attribute] : null;
        }

        // AD-039 col 15 Advice Quality grade = final outcome, or initial when not yet finalised (BR-007).
        private static string ResolveAdviceGrade(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(OutcomeEntity)
            {
                ColumnSet = new ColumnSet("al_finaloutcome", "al_initialoutcome"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return null;
            }

            var outcome = found[0];
            if (outcome.FormattedValues.ContainsKey("al_finaloutcome"))
            {
                return outcome.FormattedValues["al_finaloutcome"];
            }
            return outcome.FormattedValues.ContainsKey("al_initialoutcome") ? outcome.FormattedValues["al_initialoutcome"] : null;
        }

        private static void SetResponse(IPluginExecutionContext context, string batchId, string rowCount, string status, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutBatchId] = batchId;
            context.OutputParameters[OutRowCount] = rowCount;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
