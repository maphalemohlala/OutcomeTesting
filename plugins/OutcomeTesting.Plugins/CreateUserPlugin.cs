using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command CreateUser (AD-003, AD-041, AD-044). Registered against the
    /// Custom API <c>al_CreateUser</c>. An Administrator adds a person to the application
    /// user registry (al_user), keyed on work email (AD-010). Enforces the caller holds
    /// Manage on <c>permission.manage</c>, upserts on the work email (idempotent,
    /// NFR-REL-01) and writes an immutable Audit Event (BR-012, NFR-AUD-01).
    /// </summary>
    public class CreateUserPlugin : PluginBase
    {
        private const string InFullName = "FullName";
        private const string InWorkEmail = "WorkEmail";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutUserId = "UserId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string UserEntity = "al_user";
        private const int CommandCreateUser = 120910785;

        public CreateUserPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CreateUserPlugin))
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

            var fullName = CommandHelpers.GetRequiredString(context, InFullName).Trim();
            var workEmail = CommandHelpers.GetRequiredString(context, InWorkEmail).Trim().ToLowerInvariant();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            if (workEmail.IndexOf('@') <= 0 || workEmail.EndsWith("@", StringComparison.Ordinal))
            {
                throw new InvalidPluginExecutionException("Enter a valid work email address.");
            }

            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), "Created", existingAudit.Id, false);
                return;
            }

            var user = new Entity(UserEntity)
            {
                ["al_name"] = fullName,
                ["al_workemail"] = workEmail,
                ["al_isactive"] = true,
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };

            var userId = AssignUserRolePlugin.Upsert(userService, UserEntity, "al_workemail", workEmail, user);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandCreateUser, "CreateUser " + workEmail, UserEntity, userId,
                fullName, workEmail, idempotencyKey, context);

            SetResponse(context, userId.ToString("D"), "Created", auditId, false);
        }

        private static void SetResponse(IPluginExecutionContext context, string userId, string status, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutUserId] = userId;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
