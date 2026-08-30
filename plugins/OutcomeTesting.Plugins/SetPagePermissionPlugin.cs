using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SetPagePermission (AD-003, AD-041). Registered against the
    /// Custom API <c>al_SetPagePermission</c>. An Administrator sets the access level a
    /// role has on a resource key (page or capability). Enforces the caller holds Manage on
    /// <c>permission.manage</c>, upserts al_pagepermission on its business code (idempotent,
    /// NFR-REL-01) and writes an immutable Audit Event (BR-012, NFR-AUD-01).
    /// </summary>
    public class SetPagePermissionPlugin : PluginBase
    {
        private const string InAppRole = "AppRole";
        private const string InRoleCode = "RoleCode";
        private const string InResourceKey = "ResourceKey";
        private const string InAccessLevel = "AccessLevel";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutPermissionId = "PermissionId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string PermissionEntity = "al_pagepermission";
        private const int CommandSetPagePermission = 120910774;

        public SetPagePermissionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SetPagePermissionPlugin))
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

            var roleLabel = CommandHelpers.GetOptionalString(context, InAppRole);
            var roleCode = CommandHelpers.GetOptionalString(context, InRoleCode);
            var resourceKey = CommandHelpers.GetRequiredString(context, InResourceKey).Trim();
            var levelLabel = CommandHelpers.GetRequiredString(context, InAccessLevel);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var levelValue = PermissionHelpers.ParseLevel(levelLabel);

            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandSetPagePermission);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), levelLabel, existingAudit.Id, false);
                return;
            }

            Entity permission;
            string code;
            if (!string.IsNullOrWhiteSpace(roleCode))
            {
                // Custom role (AD-044): identified by its al_role business code, not the picklist.
                var normalizedCode = roleCode.Trim();
                if (!AssignUserRolePlugin.CustomRoleExists(systemService, normalizedCode))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "The role code does not match an active role.");
                }
                code = "PP-" + normalizedCode + "-" + resourceKey;
                permission = new Entity(PermissionEntity)
                {
                    ["al_name"] = (normalizedCode + " / " + resourceKey),
                    ["al_rolecode"] = normalizedCode,
                    ["al_resourcekey"] = resourceKey,
                    ["al_accesslevel"] = new OptionSetValue(levelValue),
                    ["al_pagepermissioncode"] = code,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };
            }
            else
            {
                if (string.IsNullOrWhiteSpace(roleLabel))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "Provide a built-in role or a custom role code.");
                }
                var roleValue = PermissionHelpers.ParseRole(roleLabel);
                code = "PP-" + roleValue + "-" + resourceKey;
                permission = new Entity(PermissionEntity)
                {
                    ["al_name"] = (roleLabel + " / " + resourceKey),
                    ["al_approle"] = new OptionSetValue(roleValue),
                    ["al_resourcekey"] = resourceKey,
                    ["al_accesslevel"] = new OptionSetValue(levelValue),
                    ["al_pagepermissioncode"] = code,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };
            }

            var permissionId = AssignUserRolePlugin.Upsert(userService, PermissionEntity, "al_pagepermissioncode", code, permission);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandSetPagePermission, "SetPagePermission " + code, PermissionEntity, permissionId,
                levelLabel, resourceKey, idempotencyKey, context);

            SetResponse(context, permissionId.ToString("D"), levelLabel, auditId, false);
        }

        private static void SetResponse(IPluginExecutionContext context, string permissionId, string status, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutPermissionId] = permissionId;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
