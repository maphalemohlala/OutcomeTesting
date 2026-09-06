using System;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command CreateRole (AD-003, AD-041, AD-044). Registered against the
    /// Custom API <c>al_CreateRole</c>. An Administrator adds a role to the extensible role
    /// registry, which is the Power Pages web roles (see <see cref="WebRoleRegistry"/>).
    /// Enforces the caller holds Manage on <c>permission.manage</c>,
    /// upserts the role on its business code (idempotent, NFR-REL-01) and writes an
    /// immutable Audit Event (BR-012, NFR-AUD-01).
    /// </summary>
    public class CreateRolePlugin : PluginBase
    {
        private const string InRoleName = "RoleName";
        private const string InDescription = "Description";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutRoleId = "RoleId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string RoleEntity = "al_role";
        private const int CommandCreateRole = 120910784;

        public CreateRolePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CreateRolePlugin))
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

            var roleName = CommandHelpers.GetRequiredString(context, InRoleName).Trim();
            var description = CommandHelpers.GetOptionalString(context, InDescription);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandCreateRole);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), "Created", existingAudit.Id, false);
                return;
            }

            // The role name IS the code. Permission rules and role assignments both match
            // on al_rolecode, and a web role has no separate business key, so deriving a
            // slug would put a second identifier in play that nothing else references.
            var existing = WebRoleRegistry.FindByName(systemService, roleName);
            Guid roleId;
            if (existing != null)
            {
                // Same name, same role: the idempotent re-run. Only the description moves.
                roleId = existing.Id;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    userService.Update(new Entity(WebRoleRegistry.RoleEntity, roleId)
                    {
                        [WebRoleRegistry.DescriptionAttr] = description.Trim(),
                    });
                }
            }
            else
            {
                var website = WebRoleRegistry.AnyWebsite(systemService);
                if (website == null)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "No Power Pages website was found, so a web role cannot be created.");
                }

                var role = new Entity(WebRoleRegistry.RoleEntity)
                {
                    [WebRoleRegistry.NameAttr] = roleName,
                    [WebRoleRegistry.WebsiteAttr] = website,
                    [WebRoleRegistry.AuthenticatedAttr] = false,
                    [WebRoleRegistry.AnonymousAttr] = false,
                };
                if (!string.IsNullOrWhiteSpace(description))
                {
                    role[WebRoleRegistry.DescriptionAttr] = description.Trim();
                }

                roleId = userService.Create(role);
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandCreateRole, "CreateRole " + roleName, WebRoleRegistry.RoleEntity, roleId,
                roleName, description, idempotencyKey, context);

            SetResponse(context, roleId.ToString("D"), "Created", auditId, false);
        }

        /// <summary>Uppercase, hyphen-separated stable code fragment from a role name.</summary>
        private static string Slug(string value)
        {
            var sb = new StringBuilder();
            var lastHyphen = false;
            foreach (var ch in value.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastHyphen = false;
                }
                else if (!lastHyphen)
                {
                    sb.Append('-');
                    lastHyphen = true;
                }
            }
            return sb.ToString().Trim('-');
        }

        private static void SetResponse(IPluginExecutionContext context, string roleId, string status, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutRoleId] = roleId;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
