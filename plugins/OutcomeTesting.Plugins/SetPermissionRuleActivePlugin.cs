using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SetPermissionRuleActive (AD-003, AD-041). Registered against the
    /// Custom API message <c>al_SetPermissionRuleActive</c>. An administrator withdraws (or
    /// restores) an al_pagepermission override rule. Withdrawing a rule does not deny the
    /// resource -- it drops the override so the role falls back to the code default in
    /// DEFAULT_PERMISSIONS. To deny explicitly, set the rule's level to None instead.
    ///
    /// Deactivation preserves the row and its history (AD-037/OD-010). Enforces the AD-041
    /// <c>permission.manage</c> Manage permission and writes an immutable Audit Event
    /// (BR-012, NFR-AUD-01).
    /// </summary>
    public class SetPermissionRuleActivePlugin : PluginBase
    {
        private const string InPermissionId = "PermissionId";
        private const string InActive = "Active";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutPermissionId = "PermissionId";
        private const string OutActive = "Active";
        private const string OutAuditEventId = "AuditEventId";

        private const string PermissionEntity = "al_pagepermission";

        private const int CommandSetPermissionRuleActive = 120910789;

        public SetPermissionRuleActivePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SetPermissionRuleActivePlugin))
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

            var permissionId = CommandHelpers.ParseRequiredGuid(context, InPermissionId);
            var active = CommandHelpers.GetRequiredBool(context, InActive);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey);
            if (existingAudit != null)
            {
                SetResponse(context, permissionId.ToString("D"), active, existingAudit.Id);
                return;
            }

            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            Entity before;
            try
            {
                before = userService.Retrieve(PermissionEntity, permissionId, new ColumnSet("al_resourcekey", "statecode"));
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "That permission rule no longer exists. Refresh and try again.");
            }

            var resourceKey = before.GetAttributeValue<string>("al_resourcekey");
            var wasActive = CommandHelpers.IsActive(before);

            CommandHelpers.SetState(userService, PermissionEntity, permissionId, active);

            var details = "Active " + wasActive + " -> " + active;
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandSetPermissionRuleActive,
                (active ? "RestorePermissionRule " : "WithdrawPermissionRule ") + resourceKey,
                PermissionEntity, permissionId, null, details, idempotencyKey, context);

            SetResponse(context, permissionId.ToString("D"), active, auditId);
        }

        private static void SetResponse(IPluginExecutionContext context, string permissionId, bool active, Guid auditId)
        {
            context.OutputParameters[OutPermissionId] = permissionId;
            context.OutputParameters[OutActive] = active;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
