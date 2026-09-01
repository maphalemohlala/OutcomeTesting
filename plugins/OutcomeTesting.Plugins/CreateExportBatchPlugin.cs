using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command CreateExportBatch (AD-003, AD-039, AD-034 manual only).
    /// Registered against the Custom API <c>al_CreateExportBatch</c>. Opens one manual
    /// Trail Light export run in Draft. Enforces the caller holds Edit on
    /// <c>export.generate</c>, upserts al_exportbatch on its code (idempotent) and writes
    /// an immutable Audit Event.
    /// </summary>
    public class CreateExportBatchPlugin : PluginBase
    {
        private const string InName = "Name";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutBatchId = "BatchId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string BatchEntity = "al_exportbatch";
        private const int BatchStatusDraft = 120910770;
        private const int CommandCreateExportBatch = 120910759;

        public CreateExportBatchPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CreateExportBatchPlugin))
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

            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var name = CommandHelpers.GetOptionalString(context, InName);

            PermissionHelpers.EnsureAppPermission(systemService, context, "export.generate", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandCreateExportBatch);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), "Draft", existingAudit.Id, false);
                return;
            }

            var code = "EXB-" + idempotencyKey;
            var batch = new Entity(BatchEntity)
            {
                ["al_name"] = string.IsNullOrWhiteSpace(name) ? ("Trail Light export " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")) : name,
                ["al_exportbatchcode"] = code,
                ["al_batchstatus"] = new OptionSetValue(BatchStatusDraft),
                ["al_rowcount"] = 0,
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };
            var batchId = AssignUserRolePlugin.Upsert(userService, BatchEntity, "al_exportbatchcode", code, batch);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandCreateExportBatch, "CreateExportBatch " + code, BatchEntity, batchId,
                null, name, idempotencyKey, context);

            SetResponse(context, batchId.ToString("D"), "Draft", auditId, false);
        }

        private static void SetResponse(IPluginExecutionContext context, string batchId, string status, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutBatchId] = batchId;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
