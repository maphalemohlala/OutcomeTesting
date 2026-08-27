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

            // Bootstrap: before any mapping exists, allow so the first assignment can seed.
            // Dataverse create privilege on al_userrolemapping still gates who reaches here.
            var anyMapping = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            anyMapping.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            if (systemService.RetrieveMultiple(anyMapping).Entities.Count == 0)
            {
                return;
            }

            var email = GetCallerEmail(systemService, context);
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

        private static List<int> GetActiveRoles(IOrganizationService service, string email)
        {
            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet("al_approle"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("al_useremail", ConditionOperator.Equal, email);

            var roles = new List<int>();
            foreach (var entity in service.RetrieveMultiple(query).Entities)
            {
                var role = entity.GetAttributeValue<OptionSetValue>("al_approle");
                if (role != null)
                {
                    roles.Add(role.Value);
                }
            }
            return roles;
        }

        private static int MaxLevel(IOrganizationService service, string resourceKey, List<int> roles)
        {
            var roleValues = new object[roles.Count];
            for (var i = 0; i < roles.Count; i++)
            {
                roleValues[i] = roles[i];
            }

            var query = new QueryExpression(PermissionEntity)
            {
                ColumnSet = new ColumnSet("al_accesslevel"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("al_resourcekey", ConditionOperator.Equal, resourceKey);
            query.Criteria.AddCondition("al_approle", ConditionOperator.In, roleValues);

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
