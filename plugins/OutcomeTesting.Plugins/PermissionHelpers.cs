using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side enforcement of the application RBAC model (AD-041). Resolves the
    /// caller's app roles from al_userrolemapping (keyed on work email, AD-010) and the
    /// effective access level from al_pagepermission, then refuses an action the caller is
    /// not granted. This is the authoritative gate the client permission reader only
    /// mirrors. Reads use the system service so the check does not depend on the caller's
    /// own read privileges; the command's write still runs as the caller so Dataverse
    /// create/write privilege remains the primary platform gate.
    /// </summary>
    internal static class PermissionHelpers
    {
        public const int AccessNone = 120910766;
        public const int AccessView = 120910767;
        public const int AccessEdit = 120910768;
        public const int AccessManage = 120910769;

        private const string MappingEntity = "al_userrolemapping";
        private const string PermissionEntity = "al_pagepermission";
        private const string UserEntity = "al_user";
        private const string ActiveAttr = "al_isactive";

        public static void EnsureAppPermission(
            IOrganizationService systemService,
            IPluginExecutionContext context,
            string resourceKey,
            int requiredLevel)
        {
            // Break-glass: a Dataverse System Administrator can always manage access, so
            // assigning roles can never permanently lock everyone out of configuration.
            if (IsSystemAdministrator(systemService, context))
            {
                return;
            }

            // Bootstrap: before any mapping has ever been created, allow so the first
            // assignment can seed. Dataverse create privilege on al_userrolemapping still
            // gates who reaches here.
            //
            // This deliberately counts mappings in ANY state. Counting only active ones
            // would re-open the gate for everyone whenever the last mapping is deactivated
            // or a migration lands them inactive — a table that has rows but none active is
            // a configuration to enforce, not a system waiting to be seeded.
            var anyMapping = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            if (systemService.RetrieveMultiple(anyMapping).Entities.Count == 0)
            {
                return;
            }

            var email = GetCallerEmail(systemService, context);

            // Deactivation (OD-010) is the sanctioned alternative to deleting a leaver, so
            // it has to be what actually withdraws their access. The mappings are left in
            // place deliberately — they are history, and reactivation restores the person
            // to the roles they had — so the registry row is what decides, not the mapping.
            if (!IsRegisteredActive(systemService, email))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.UnauthorizedPrefix + "Your application account is deactivated. Ask an administrator to reactivate it.");
            }

            var roles = GetActiveRoles(systemService, email);
            if (roles.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.UnauthorizedPrefix +
                    "You have no application role assigned. Ask an administrator to assign one in Security configuration.");
            }

            if (MaxLevel(systemService, resourceKey, roles) < requiredLevel)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.UnauthorizedPrefix +
                    "Your role does not grant the required access for this action (" + resourceKey + ").");
            }
        }

        private static bool IsSystemAdministrator(IOrganizationService service, IPluginExecutionContext context)
        {
            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, "System Administrator");
            var userLink = query.AddLink("systemuserroles", "roleid", "roleid");
            var systemUserLink = userLink.AddLink("systemuser", "systemuserid", "systemuserid");
            systemUserLink.LinkCriteria.AddCondition("systemuserid", ConditionOperator.Equal, context.InitiatingUserId);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        private static string GetCallerEmail(IOrganizationService service, IPluginExecutionContext context)
        {
            var user = service.Retrieve("systemuser", context.InitiatingUserId, new ColumnSet("internalemailaddress"));
            var email = user.GetAttributeValue<string>("internalemailaddress");
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.UnauthorizedPrefix +
                    "Your account has no work email, which is required to resolve your role (AD-010).");
            }
            return email;
        }

        /// <summary>
        /// True when the caller has an al_user registry row that is active, or no row at
        /// all. "No row" stays permissive on purpose: al_user is an application registry
        /// that a Dataverse user can legitimately predate, and refusing there would lock
        /// out anyone the registry has not caught up with. Only an explicit deactivation
        /// withdraws access.
        /// </summary>
        private static bool IsRegisteredActive(IOrganizationService service, string email)
        {
            var query = new QueryExpression(UserEntity)
            {
                ColumnSet = new ColumnSet(ActiveAttr),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_workemail", ConditionOperator.Equal, email);

            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return true;
            }

            return found[0].GetAttributeValue<bool?>(ActiveAttr) ?? true;
        }

        /// <summary>
        /// The caller's roles. A mapping identifies its role EITHER by the built-in
        /// al_approle picklist OR by a custom role's al_rolecode (AD-044) — the assign path
        /// writes one or the other, never both — so both have to be carried and matched.
        /// </summary>
        private sealed class CallerRoles
        {
            public readonly List<int> AppRoles = new List<int>();
            public readonly List<string> RoleCodes = new List<string>();

            public int Count
            {
                get { return AppRoles.Count + RoleCodes.Count; }
            }
        }

        private static CallerRoles GetActiveRoles(IOrganizationService service, string email)
        {
            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet("al_approle", "al_rolecode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("al_useremail", ConditionOperator.Equal, email);

            var roles = new CallerRoles();
            foreach (var entity in service.RetrieveMultiple(query).Entities)
            {
                var role = entity.GetAttributeValue<OptionSetValue>("al_approle");
                if (role != null)
                {
                    roles.AppRoles.Add(role.Value);
                    continue;
                }

                var code = entity.GetAttributeValue<string>("al_rolecode");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    roles.RoleCodes.Add(code.Trim());
                }
            }
            return roles;
        }

        private static int MaxLevel(IOrganizationService service, string resourceKey, CallerRoles roles)
        {
            var query = new QueryExpression(PermissionEntity)
            {
                ColumnSet = new ColumnSet("al_accesslevel"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("al_resourcekey", ConditionOperator.Equal, resourceKey);

            // A permission rule names its role the same two ways a mapping does, so a rule
            // matches if EITHER identifier is one the caller holds.
            var roleFilter = new FilterExpression(LogicalOperator.Or);
            if (roles.AppRoles.Count > 0)
            {
                var appRoleValues = new object[roles.AppRoles.Count];
                for (var i = 0; i < roles.AppRoles.Count; i++)
                {
                    appRoleValues[i] = roles.AppRoles[i];
                }
                roleFilter.AddCondition("al_approle", ConditionOperator.In, appRoleValues);
            }
            if (roles.RoleCodes.Count > 0)
            {
                var roleCodeValues = new object[roles.RoleCodes.Count];
                for (var i = 0; i < roles.RoleCodes.Count; i++)
                {
                    roleCodeValues[i] = roles.RoleCodes[i];
                }
                roleFilter.AddCondition("al_rolecode", ConditionOperator.In, roleCodeValues);
            }
            query.Criteria.AddFilter(roleFilter);

            var max = AccessNone;
            foreach (var entity in service.RetrieveMultiple(query).Entities)
            {
                var level = entity.GetAttributeValue<OptionSetValue>("al_accesslevel");
                if (level != null && level.Value > max)
                {
                    max = level.Value;
                }
            }
            return max;
        }

        /// <summary>Maps an app-role label to its al_approle option value (AD-041).</summary>
        public static int ParseRole(string label)
        {
            switch ((label ?? string.Empty).Trim())
            {
                case "Tax Checker": return 120910760;
                case "AQS Checker": return 120910761;
                case "Adviser": return 120910762;
                case "T&C Manager":
                case "T and C Manager": return 120910763;
                case "Outcome Testing Manager": return 120910764;
                case "Administrator": return 120910765;
                default:
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "Unknown app role '" + label + "'.");
            }
        }

        /// <summary>Maps an access-level label to its al_accesslevel option value (AD-041).</summary>
        public static int ParseLevel(string label)
        {
            switch ((label ?? string.Empty).Trim())
            {
                case "None": return AccessNone;
                case "View": return AccessView;
                case "Edit": return AccessEdit;
                case "Manage": return AccessManage;
                default:
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "Unknown access level '" + label + "'.");
            }
        }
    }
}
