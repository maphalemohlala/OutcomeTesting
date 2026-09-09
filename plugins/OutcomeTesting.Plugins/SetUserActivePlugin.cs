using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command SetUserActive (AD-003, AD-041, AD-037/OD-010). Registered against
    /// the Custom API message <c>al_SetUserActive</c>. Deactivation is the sanctioned
    /// alternative to hard deletion for retained data (OD-010): the registry row and its
    /// history are preserved and only the state is flipped. The registry is <c>contact</c>
    /// (see <see cref="ContactRegistry"/>), so the flag is statecode/statuscode rather than
    /// a bespoke boolean. Enforces the AD-041
    /// <c>permission.manage</c> Manage permission and writes an immutable Audit Event
    /// (BR-012, NFR-AUD-01). The write runs as the initiating user so Dataverse privilege
    /// remains the platform gate.
    /// </summary>
    public class SetUserActivePlugin : PluginBase
    {
        private const string InUserId = "UserId";
        private const string InActive = "Active";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutUserId = "UserId";
        private const string OutActive = "Active";
        private const string OutAuditEventId = "AuditEventId";

        private const int CommandSetUserActive = 120910787;

        public SetUserActivePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SetUserActivePlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService; // caller privileges gate the write
            var systemService = localPluginContext.PluginUserService;   // permission read + audit

            var userId = CommandHelpers.ParseRequiredGuid(context, InUserId);
            var active = GetRequiredBool(context, InActive);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            // Permission check before the idempotency lookup (matching AssignUserRolePlugin):
            // otherwise an unauthorised caller could probe whether a key exists and receive a
            // real audit id back for work they were never allowed to trigger.
            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandSetUserActive);
            if (existingAudit != null)
            {
                SetResponse(context, userId.ToString("D"), active, existingAudit.Id);
                return;
            }

            var before = userService.Retrieve(
                ContactRegistry.Entity,
                userId,
                new ColumnSet(ContactRegistry.StateCodeAttr, ContactRegistry.EmailAttr));
            var workEmail = before.GetAttributeValue<string>(ContactRegistry.EmailAttr);
            var wasActive = ContactRegistry.IsActive(before);

            // statuscode travels with statecode: setting the state alone leaves the reason
            // code pointing at the state the row just left, which reads as a half-applied
            // change wherever the reason is displayed.
            var update = new Entity(ContactRegistry.Entity, userId)
            {
                [ContactRegistry.StateCodeAttr] = new OptionSetValue(
                    active ? ContactRegistry.StateActive : ContactRegistry.StateInactive),
                [ContactRegistry.StatusCodeAttr] = new OptionSetValue(
                    active ? ContactRegistry.StatusActive : ContactRegistry.StatusInactive),
            };
            userService.Update(update);

            var details = "Active " + wasActive + " -> " + active;
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandSetUserActive, (active ? "ReactivateUser " : "DeactivateUser ") + workEmail,
                ContactRegistry.Entity, userId, null, details, idempotencyKey, context);

            SetResponse(context, userId.ToString("D"), active, auditId);
        }

        private static bool GetRequiredBool(IPluginExecutionContext context, string name)
        {
            object value;
            if (context.InputParameters.TryGetValue(name, out value) && value is bool)
            {
                return (bool)value;
            }

            throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + name + " is required.");
        }

        private static void SetResponse(IPluginExecutionContext context, string userId, bool active, Guid auditId)
        {
            context.OutputParameters[OutUserId] = userId;
            context.OutputParameters[OutActive] = active;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
        }
    }
}
