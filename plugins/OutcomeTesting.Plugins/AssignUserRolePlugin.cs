using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AssignUserRole (AD-003, AD-041). Registered against the Custom
    /// API <c>al_AssignUserRole</c>. An Administrator maps a person (work email, AD-010) to
    /// an application role. Enforces the caller holds Manage on <c>permission.manage</c>,
    /// upserts al_userrolemapping on its business code (idempotent, NFR-REL-01) and writes
    /// an immutable Audit Event (BR-012, NFR-AUD-01).
    /// </summary>
    public class AssignUserRolePlugin : PluginBase
    {
        private const string InUserEmail = "UserEmail";
        private const string InAppRole = "AppRole";
        private const string InRoleCode = "RoleCode";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutMappingId = "MappingId";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string MappingEntity = "al_userrolemapping";
        private const int CommandAssignUserRole = 120910773;

        public AssignUserRolePlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AssignUserRolePlugin))
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

            var email = CommandHelpers.GetRequiredString(context, InUserEmail).Trim();
            var roleLabel = CommandHelpers.GetOptionalString(context, InAppRole);
            var roleCode = CommandHelpers.GetOptionalString(context, InRoleCode);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandAssignUserRole);
            if (existingAudit != null)
            {
                SetResponse(context, existingAudit.GetAttributeValue<string>("al_targetid"), "Assigned", existingAudit.Id, false);
                return;
            }

            Entity mapping;
            string code;
            string auditRole;
            Entity webRole = null;
            if (!string.IsNullOrWhiteSpace(roleCode))
            {
                // Custom role (AD-044): identified by its al_role business code, not the picklist.
                var normalizedCode = roleCode.Trim();

                // A role code names EITHER a web role (the registry, AD-041) or an AD-044
                // al_role. Web roles are checked first because they are the source the app
                // now offers; al_role stays valid so nothing already assigned breaks.
                webRole = WebRoleRegistry.FindByName(systemService, normalizedCode);
                if (webRole == null && !CustomRoleExists(systemService, normalizedCode))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "The role code does not match an active role.");
                }
                code = "URM-" + email.ToLowerInvariant() + "-" + normalizedCode;
                mapping = new Entity(MappingEntity)
                {
                    ["al_name"] = (normalizedCode + " - " + email),
                    ["al_useremail"] = email,
                    ["al_rolecode"] = normalizedCode,
                    // Explicitly null: al_approle carries a schema default, so leaving it
                    // out writes a picklist role nobody asked for onto a code-based
                    // assignment, and anything reading the picklist would honour it.
                    ["al_approle"] = null,
                    ["al_userrolemappingcode"] = code,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };
                auditRole = normalizedCode;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(roleLabel))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "Provide a built-in role or a custom role code.");
                }
                var roleValue = PermissionHelpers.ParseRole(roleLabel);
                code = "URM-" + email.ToLowerInvariant() + "-" + roleValue;
                mapping = new Entity(MappingEntity)
                {
                    ["al_name"] = (roleLabel + " - " + email),
                    ["al_useremail"] = email,
                    ["al_approle"] = new OptionSetValue(roleValue),
                    ["al_userrolemappingcode"] = code,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };
                auditRole = roleLabel;
            }

            var mappingId = Upsert(userService, MappingEntity, "al_userrolemappingcode", code, mapping);

            // The mapping row is the mirror; the association is the assignment. Doing both
            // is what stops the app and the portal disagreeing about who holds a role.
            // Deliberately after the mirror exists: an association with no row behind it is
            // invisible to every screen that reads the mapping table.
            if (webRole != null)
            {
                AssociateWebRole(systemService, email, webRole.Id);
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandAssignUserRole, "AssignUserRole " + code, MappingEntity, mappingId,
                auditRole, email, idempotencyKey, context);

            SetResponse(context, mappingId.ToString("D"), "Assigned", auditId, false);
        }

        /// <summary>
        /// Associates the contact behind a work email with a web role, tolerating the case
        /// where the association already exists.
        ///
        /// Contacts and web roles are the two halves AD-010 already keys on one email, so a
        /// person with no contact cannot hold a web role — that is reported rather than
        /// silently skipped, because the caller believes they just granted access.
        /// </summary>
        internal static void AssociateWebRole(IOrganizationService service, string email, Guid webRoleId)
        {
            var contact = WebRoleRegistry.FindContactByEmail(service, email);
            if (contact == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "No contact has the work email " + email + ", so the web role cannot be granted to them.");
            }

            try
            {
                service.Associate(
                    ContactRegistry.Entity,
                    contact.Id,
                    new Relationship(WebRoleRegistry.ContactRelationship),
                    new EntityReferenceCollection
                    {
                        new EntityReference(WebRoleRegistry.RoleEntity, webRoleId),
                    });
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                // Already associated. Assigning a role someone already holds is the
                // idempotent outcome this command promises (NFR-REL-01), not a failure.
            }
        }

        /// <summary>
        /// Withdraws the association behind a mapping row, so deactivating an assignment in
        /// the app actually removes the portal role rather than only greying out a mirror.
        /// </summary>
        internal static void DisassociateWebRole(IOrganizationService service, string email, string roleName)
        {
            var contact = WebRoleRegistry.FindContactByEmail(service, email);
            var webRole = WebRoleRegistry.FindByName(service, roleName);
            if (contact == null || webRole == null)
            {
                return;
            }

            try
            {
                service.Disassociate(
                    ContactRegistry.Entity,
                    contact.Id,
                    new Relationship(WebRoleRegistry.ContactRelationship),
                    new EntityReferenceCollection
                    {
                        new EntityReference(WebRoleRegistry.RoleEntity, webRole.Id),
                    });
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                // Not associated. Withdrawing a role someone does not hold is already the
                // state the caller asked for.
            }
        }

        /// <summary>True when an al_role exists with the given business code.</summary>
        internal static bool CustomRoleExists(IOrganizationService service, string roleCode)
        {
            var query = new QueryExpression("al_role")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_rolecode", ConditionOperator.Equal, roleCode);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        internal static Guid Upsert(IOrganizationService service, string entity, string codeAttr, string code, Entity values)
        {
            var query = new QueryExpression(entity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(codeAttr, ConditionOperator.Equal, code);
            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count > 0)
            {
                values.Id = found[0].Id;
                values.LogicalName = entity;
                var update = new Entity(entity, found[0].Id);
                foreach (var attr in values.Attributes)
                {
                    if (attr.Key != codeAttr)
                    {
                        update[attr.Key] = attr.Value;
                    }
                }
                service.Update(update);
                return found[0].Id;
            }

            return service.Create(values);
        }

        private static void SetResponse(IPluginExecutionContext context, string mappingId, string status, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutMappingId] = mappingId;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
