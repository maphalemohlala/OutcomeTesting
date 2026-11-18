using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command CreateUser (AD-003, AD-041, AD-044). Registered against the
    /// Custom API <c>al_CreateUser</c>. An Administrator adds a person to the application
    /// user registry, keyed on work email (AD-010). Enforces the caller holds Manage on
    /// <c>permission.manage</c>, refuses an email already registered, replays a repeated
    /// idempotency key (NFR-REL-01) and writes an immutable Audit Event (BR-012, NFR-AUD-01).
    ///
    /// The registry is <c>contact</c>; see <see cref="ContactRegistry"/> for why. The
    /// display name is written as firstname/lastname because fullname is calculated.
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
            //
            // Looked up as SYSTEM, not as the caller: a row the caller cannot see is still the
            // person, and missing it created a duplicate contact for one email. Nothing is
            // written to an existing row, so the wider read cannot overwrite anything.
            var lookup = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            var existing = WebRoleRegistry.FindContactByEmail(lookup, workEmail);
            if (existing != null && !ContactRegistry.IsActive(existing))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "A deactivated account already exists for this work email. Reactivate it instead of registering it again.");
            }

            // An active person is not registered again. This used to rename them to whatever
            // the form said and audit it as "Created"; a name change is al_UpdateUser's job.
            if (existing != null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Someone is already registered with this work email. Edit their details instead of adding them again.");
            }

            // statecode is left alone: a new contact is created active, and setting it
            // here would make this command a reactivation path, which it must not be.
            var contact = new Entity(ContactRegistry.Entity)
            {
                [ContactRegistry.EmailAttr] = workEmail,
            };
            ContactRegistry.SetName(contact, fullName);

            // A person made here has to be able to sign in to the portal, and Power Pages
            // only writes this for contacts it creates itself. Never on an existing row:
            // rotating a stamp signs the person out everywhere.
            ContactRegistry.SetSecurityStamp(contact);

            var userId = userService.Create(contact);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandCreateUser, "CreateUser " + workEmail, ContactRegistry.Entity, userId,
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
