using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

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
        private const string ActiveAttr = "al_isactive";
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

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandCreateUser);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), "Created", existingAudit.Id, false);
                return;
            }

            // Registering someone must never be a way to bring a leaver back. The upsert is
            // keyed on work email, so re-adding a deactivated person's address would rewrite
            // their row active again under a new display name — restoring the role mappings
            // that deactivation withdrew (OD-010), audited only as "Created". Reactivation
            // is its own command, al_SetUserActive, and its own Audit Event.
            var existing = FindByWorkEmail(userService, workEmail);
            if (existing != null && !(existing.GetAttributeValue<bool?>(ActiveAttr) ?? true))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "A deactivated account already exists for this work email. Reactivate it instead of registering it again.");
            }

            Guid userId;
            if (existing != null)
            {
                // Already registered and active: this is the idempotent re-run. Only the
                // display name is refreshed — the active state is not this command's to set.
                userId = existing.Id;
                userService.Update(new Entity(UserEntity, userId) { ["al_name"] = fullName });
            }
            else
            {
                userId = userService.Create(new Entity(UserEntity)
                {
                    ["al_name"] = fullName,
                    ["al_workemail"] = workEmail,
                    [ActiveAttr] = true,
                });
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandCreateUser, "CreateUser " + workEmail, UserEntity, userId,
                fullName, workEmail, idempotencyKey, context);

            SetResponse(context, userId.ToString("D"), "Created", auditId, false);
        }

        /// <summary>The al_user registry row for a work email, or null. Read as the caller,
        /// so a row they cannot see is not silently overwritten on their behalf.</summary>
        private static Entity FindByWorkEmail(IOrganizationService service, string workEmail)
        {
            var query = new QueryExpression(UserEntity)
            {
                ColumnSet = new ColumnSet(ActiveAttr),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_workemail", ConditionOperator.Equal, workEmail);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0] : null;
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
