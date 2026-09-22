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
        private const string InStaffCode = "StaffCode";
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
            var staffCode = CommandHelpers.GetOptionalString(context, InStaffCode);

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
                    ContactRegistry.EmailAttr,
                    // Read so the audit line can say what the code CHANGED FROM. Without it
                    // a code-only edit produced "Name Jane Adviser -> Jane Adviser", which
                    // reads as a no-op on a permission-gated administrative write that feeds
                    // a positional file an external system consumes (2026-09-22 review).
                    ContactRegistry.StaffCodeAttr));
            var previousName = ContactRegistry.NameOf(before);
            var previousStaffCode = before.GetAttributeValue<string>(ContactRegistry.StaffCodeAttr);
            var workEmail = before.GetAttributeValue<string>(ContactRegistry.EmailAttr);

            var update = new Entity(ContactRegistry.Entity, userId);
            ContactRegistry.SetName(update, fullName);

            // Absent leaves the stored code alone; an explicitly empty string clears it.
            // The distinction matters because the People page sends only what it edited,
            // and a save of somebody's NAME must not silently wipe their code.
            if (staffCode != null)
            {
                update[ContactRegistry.StaffCodeAttr] = staffCode.Trim().Length == 0
                    ? null
                    : staffCode.Trim();
            }

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

            // Appended only where the code was actually part of this edit AND actually
            // moved. An absent StaffCode means the page did not touch it, and a value equal
            // to the stored one is a name-only save that happened to resend it - saying
            // "Employee code EMP1 -> EMP1" on either would be the same no-op line the name
            // clause already was. AD-041 is why this command exists; the audit event is the
            // only record that a code on a Trail Light file was changed by hand and by whom.
            var newStaffCode = staffCode == null ? null : staffCode.Trim();
            if (newStaffCode != null
                && !string.Equals(newStaffCode, previousStaffCode ?? string.Empty, StringComparison.Ordinal))
            {
                details += "; Employee code "
                    + Shown(previousStaffCode) + " -> " + Shown(newStaffCode);
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandUpdateUser, "UpdateUser " + workEmail, ContactRegistry.Entity, userId,
                null, details, idempotencyKey, context);

            SetResponse(context, userId.ToString("D"), auditId, false);
        }

        /// <summary>
        /// A code as the audit line should read it. An empty side is written "(none)" rather
        /// than left blank: "Employee code  -> EMP1" and "Employee code EMP1 -> " are the two
        /// transitions a reader most needs to tell apart, and a trailing blank reads like a
        /// truncated line rather than a cleared value. The case history renders al_details
        /// verbatim, so the words here are the words somebody sees.
        /// </summary>
        private static string Shown(string staffCode)
        {
            return string.IsNullOrWhiteSpace(staffCode) ? "(none)" : staffCode.Trim();
        }

        private static void SetResponse(IPluginExecutionContext context, string userId, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutUserId] = userId;
            context.OutputParameters[OutAuditEventId] = auditId == Guid.Empty ? null : auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
