using System;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command UpdateRole (AD-003, AD-041, AD-044). Registered against the Custom
    /// API message <c>al_UpdateRole</c>. An administrator renames, re-describes or retires a
    /// role in the extensible registry (al_role).
    ///
    /// <c>al_rolecode</c> is deliberately NOT editable: it is the stable business key that
    /// al_userrolemapping and al_pagepermission reference, so renaming a role must leave every
    /// existing assignment and rule intact.
    ///
    /// Retiring a role CASCADES: neither the client permission reader nor
    /// <see cref="PermissionHelpers"/> consults the role registry when resolving access, so
    /// deactivating the al_role row alone would look like a revocation while still granting
    /// everything. The command therefore also deactivates the assignments and permission rules
    /// that reference the code, and records the counts on the Audit Event.
    ///
    /// Enforces the AD-041 <c>permission.manage</c> Manage permission, applies optimistic
    /// concurrency and idempotency, and writes an immutable Audit Event (BR-012, NFR-AUD-01).
    /// </summary>
    public class UpdateRolePlugin : PluginBase
    {
        private const string InRoleId = "RoleId";
        private const string InRoleName = "RoleName";
        private const string InDescription = "Description";
        private const string InActive = "Active";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutRoleId = "RoleId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string RoleEntity = "al_role";
        private const string MappingEntity = "al_userrolemapping";
        private const string PermissionEntity = "al_pagepermission";

        private const int CommandUpdateRole = 120910790;

        public UpdateRolePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(UpdateRolePlugin))
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

            var roleId = CommandHelpers.ParseRequiredGuid(context, InRoleId);
            var roleName = CommandHelpers.GetOptionalString(context, InRoleName);
            var description = CommandHelpers.GetOptionalString(context, InDescription);
            var active = CommandHelpers.GetOptionalBool(context, InActive);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            if (string.IsNullOrWhiteSpace(roleName) && description == null && !active.HasValue)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Change the role name, description or active state.");
            }

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandUpdateRole);
            if (existingAudit != null)
            {
                SetResponse(context, roleId.ToString("D"), existingAudit.Id, false);
                return;
            }

            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            Entity before;
            try
            {
                before = userService.Retrieve(
                    RoleEntity, roleId, new ColumnSet("al_name", "al_description", "al_rolecode", "al_isactive"));
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.NotFoundPrefix + "That role no longer exists. Refresh and try again.");
            }

            var previousName = before.GetAttributeValue<string>("al_name");
            var previousDescription = before.GetAttributeValue<string>("al_description");
            var wasActive = before.GetAttributeValue<bool?>("al_isactive") ?? true;
            var roleCode = before.GetAttributeValue<string>("al_rolecode");

            var update = new Entity(RoleEntity, roleId);
            var details = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(roleName) && roleName.Trim() != previousName)
            {
                update["al_name"] = roleName.Trim();
                Append(details, "Name " + (previousName ?? string.Empty) + " -> " + roleName.Trim());
            }

            if (description != null && description.Trim() != (previousDescription ?? string.Empty))
            {
                update["al_description"] = description.Trim().Length == 0 ? null : description.Trim();
                Append(details, "Description changed");
            }

            if (active.HasValue && active.Value != wasActive)
            {
                update["al_isactive"] = active.Value;
                Append(details, "Active " + wasActive + " -> " + active.Value);
            }

            if (update.Attributes.Count > 0)
            {
                ApplyUpdate(userService, context, update, roleId, expectedRowVersion);
            }

            // Retiring the role must also stop it granting access; see the class remarks.
            if (active.HasValue && !active.Value && !string.IsNullOrWhiteSpace(roleCode))
            {
                var mappings = DeactivateByRoleCode(userService, MappingEntity, "al_userrolemappingid", roleCode);
                var rules = DeactivateByRoleCode(userService, PermissionEntity, "al_pagepermissionid", roleCode);
                Append(details, "Cascaded to " + mappings + " assignment(s) and " + rules + " permission rule(s)");
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandUpdateRole, "UpdateRole " + (roleCode ?? roleId.ToString("D")),
                RoleEntity, roleId, null, details.ToString(), idempotencyKey, context);

            SetResponse(context, roleId.ToString("D"), auditId, false);
        }

        private void ApplyUpdate(
            IOrganizationService service,
            IPluginExecutionContext context,
            Entity update,
            Guid roleId,
            string expectedRowVersion)
        {
            if (string.IsNullOrEmpty(expectedRowVersion))
            {
                service.Update(update);
                return;
            }

            update.RowVersion = expectedRowVersion;
            try
            {
                service.Execute(new Microsoft.Xrm.Sdk.Messages.UpdateRequest
                {
                    Target = update,
                    ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                });
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
            {
                if (CommandHelpers.IsConcurrencyFault(fault))
                {
                    SetResponse(context, roleId.ToString("D"), Guid.Empty, true);
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ConflictPrefix + "This role was changed by someone else. Refresh and try again.");
                }

                throw;
            }
        }

        /// <summary>Deactivates every active row of <paramref name="entity"/> holding the role code.</summary>
        private static int DeactivateByRoleCode(
            IOrganizationService service, string entity, string idAttribute, string roleCode)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_rolecode", ConditionOperator.Equal, roleCode);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var rows = service.RetrieveMultiple(query).Entities;
            foreach (var row in rows)
            {
                CommandHelpers.SetState(service, entity, row.Id, false);
            }

            return rows.Count;
        }

        private static void Append(StringBuilder builder, string line)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(line);
        }

        private static void SetResponse(IPluginExecutionContext context, string roleId, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutRoleId] = roleId;
            context.OutputParameters[OutAuditEventId] = auditId == Guid.Empty ? null : auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
