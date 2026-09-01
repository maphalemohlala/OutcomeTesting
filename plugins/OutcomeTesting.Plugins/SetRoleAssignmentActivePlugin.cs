using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SetRoleAssignmentActive (AD-003, AD-041). Registered against the
    /// Custom API message <c>al_SetRoleAssignmentActive</c>. An administrator withdraws (or
    /// restores) a person's application role by flipping the state of the al_userrolemapping
    /// row. Deactivation is the sanctioned alternative to deletion for retained data
    /// (AD-037/OD-010): the mapping and its history survive, so the audit trail still shows
    /// who once held the role. Changing someone's role is a withdraw followed by a fresh
    /// al_AssignUserRole, because the mapping's business code embeds the role.
    ///
    /// Enforces the AD-041 <c>permission.manage</c> Manage permission and writes an
    /// immutable Audit Event (BR-012, NFR-AUD-01). The write runs as the initiating user so
    /// Dataverse privilege remains the platform gate.
    /// </summary>
    public class SetRoleAssignmentActivePlugin : PluginBase
    {
        private const string InMappingId = "MappingId";
        private const string InActive = "Active";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutMappingId = "MappingId";
        private const string OutActive = "Active";
        private const string OutAuditEventId = "AuditEventId";

        private const string MappingEntity = "al_userrolemapping";

        private const int CommandSetRoleAssignmentActive = 120910788;

        public SetRoleAssignmentActivePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SetRoleAssignmentActivePlugin))
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

            var mappingId = CommandHelpers.ParseRequiredGuid(context, InMappingId);
            var active = CommandHelpers.GetRequiredBool(context, InActive);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey);
            if (existingAudit != null)
            {
                SetResponse(context, mappingId.ToString("D"), active, existingAudit.Id);
                return;
            }

            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            Entity before;
            try
            {
                before = userService.Retrieve(MappingEntity, mappingId, new ColumnSet("al_useremail", "statecode"));
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "That role assignment no longer exists. Refresh and try again.");
            }

            var email = before.GetAttributeValue<string>("al_useremail");
            var wasActive = CommandHelpers.IsActive(before);

            CommandHelpers.SetState(userService, MappingEntity, mappingId, active);

            var details = "Active " + wasActive + " -> " + active;
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandSetRoleAssignmentActive,
                (active ? "RestoreRoleAssignment " : "WithdrawRoleAssignment ") + email,
                MappingEntity, mappingId, null, details, idempotencyKey, context);

            SetResponse(context, mappingId.ToString("D"), active, auditId);
        }

        private static void SetResponse(IPluginExecutionContext context, string mappingId, bool active, Guid auditId)
        {
            context.OutputParameters[OutMappingId] = mappingId;
            context.OutputParameters[OutActive] = active;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
