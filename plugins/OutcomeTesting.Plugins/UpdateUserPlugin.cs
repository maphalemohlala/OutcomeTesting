using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command UpdateUser (AD-003, AD-041). Registered against the Custom API
    /// message <c>al_UpdateUser</c>. An administrator amends an existing application user's
    /// display name. The registry is <c>contact</c> (see <see cref="ContactRegistry"/>) and
    /// the name is written as firstname/lastname because fullname is calculated.
    /// Work email is the stable identifier (AD-010) and is not
    /// changed here. Enforces the AD-041 <c>permission.manage</c> Manage permission, applies
    /// optimistic concurrency and idempotency, and writes an immutable Audit Event
    /// (BR-012, NFR-AUD-01). The write runs as the initiating user so Dataverse privilege
    /// remains the platform gate.
    /// </summary>
    public class UpdateUserPlugin : PluginBase
    {
        private const string InUserId = "UserId";
        private const string InFullName = "FullName";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutUserId = "UserId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const int CommandUpdateUser = 120910786;

        public UpdateUserPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(UpdateUserPlugin))
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
            var fullName = CommandHelpers.GetRequiredString(context, InFullName).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);

            // Permission check before the idempotency lookup (matching AssignUserRolePlugin):
            // otherwise an unauthorised caller could probe whether a key exists and receive a
            // real audit id back for work they were never allowed to trigger.
            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandUpdateUser);
            if (existingAudit != null)
            {
                SetResponse(context, userId.ToString("D"), existingAudit.Id, false);
                return;
            }

            var before = userService.Retrieve(
                ContactRegistry.Entity,
                userId,
                new Microsoft.Xrm.Sdk.Query.ColumnSet(
                    ContactRegistry.FullNameAttr,
                    ContactRegistry.FirstNameAttr,
                    ContactRegistry.LastNameAttr,
                    ContactRegistry.EmailAttr));
            var previousName = ContactRegistry.NameOf(before);
            var workEmail = before.GetAttributeValue<string>(ContactRegistry.EmailAttr);

            var update = new Entity(ContactRegistry.Entity, userId);
            ContactRegistry.SetName(update, fullName);

            if (string.IsNullOrEmpty(expectedRowVersion))
            {
                userService.Update(update);
            }
            else
            {
                update.RowVersion = expectedRowVersion;
                try
                {
                    userService.Execute(new Microsoft.Xrm.Sdk.Messages.UpdateRequest
                    {
                        Target = update,
                        ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                    });
                }
                catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
                {
                    if (CommandHelpers.IsConcurrencyFault(fault))
                    {
                        SetResponse(context, userId.ToString("D"), Guid.Empty, true);
                        throw new InvalidPluginExecutionException(
                            CommandHelpers.ConflictPrefix + "This person was changed by someone else. Refresh and try again.");
                    }

                    throw;
                }
            }

            var details = "Name " + (previousName ?? string.Empty) + " -> " + fullName;
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandUpdateUser, "UpdateUser " + workEmail, ContactRegistry.Entity, userId,
                null, details, idempotencyKey, context);

            SetResponse(context, userId.ToString("D"), auditId, false);
        }

        private static void SetResponse(IPluginExecutionContext context, string userId, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutUserId] = userId;
            context.OutputParameters[OutAuditEventId] = auditId == Guid.Empty ? null : auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
