using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side enforcement of the application RBAC model (AD-041). Resolves the
    /// caller's app roles from their Power Pages web roles AND from al_userrolemapping
    /// (keyed on work email, AD-010), then the effective access level from
    /// al_pagepermission, and refuses an action the caller is not granted.
    ///
    /// The two sources are unioned rather than one replacing the other. Web roles are the
    /// authority — assignment associates a contact with a role for real — but the mapping
    /// table mirrors the same facts and still carries any older assignment. Reading only
    /// the web roles would withdraw access from anyone the mirror had but the intersect did
    /// not; reading only the mirror would make the web roles decorative. This is the authoritative gate the client permission reader only
    /// mirrors. Reads use the system service so the check does not depend on the caller's
    /// own read privileges; the command's write still runs as the caller so Dataverse
    /// create/write privilege remains the primary platform gate.
    /// </summary>
    public static class PermissionHelpers
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

        /// <summary>
        /// The work email of the caller (AD-010). Public so al_GetMyRoles reports on the
        /// SAME person the gate authorises, resolved the SAME way — one implementation
        /// rather than two that can disagree about who is calling.
        /// </summary>
        public static string GetCallerEmail(IOrganizationService service, IPluginExecutionContext context)
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
        /// True when the caller has a contact registry row that is active, or no row at
        /// all. "No row" stays permissive on purpose: a Dataverse user can legitimately
        /// predate their contact, and refusing there would lock out anyone the registry has
        /// not caught up with. Only an explicit deactivation withdraws access.
        ///
        /// This read used to ask <c>al_user</c>, and that was a live defect rather than
        /// tidiness owed. <see cref="SetUserActivePlugin"/> has written the **contact's**
        /// statecode since AD-085, while this asked a table migrated to zero rows the same
        /// day — and the permissive rule turned an empty table into a blanket yes, so a
        /// deactivated person kept full command access and nothing said so. OD-010 makes
        /// deactivation the sanctioned alternative to deleting a leaver, which is exactly
        /// the case it failed open on.
        ///
        /// Public so it can be driven directly by a test, matching WebRoleRegistry: the
        /// defect was invisible from EnsureAppPermission, which needs a plug-in context.
        /// </summary>
        public static bool IsRegisteredActive(IOrganizationService service, string email)
        {
            var query = new QueryExpression(ContactRegistry.Entity)
            {
                ColumnSet = new ColumnSet(ContactRegistry.StateCodeAttr),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(ContactRegistry.EmailAttr, ConditionOperator.Equal, email);

            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return true;
            }

            return ContactRegistry.IsActive(found[0]);
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

        /// <summary>
        /// The role codes a person holds, as the gate resolves them (AD-089).
        ///
        /// Public so al_GetMyRoles can hand the client the SAME answer the server enforces
        /// with. The client used to derive its own from the mapping table alone, which meant
        /// a portal-side assignment authorised writes the UI would not offer.
        ///
        /// Built-in al_approle values are not returned: they have no code to match a
        /// permission rule on, and AD-087 stopped offering the picklist. A caller holding
        /// only a picklist role still resolves server-side through GetActiveRoles,
        /// unchanged.
        /// </summary>
        public static List<string> ResolveRoleCodesForEmail(IOrganizationService service, string email)
        {
            var roles = GetActiveRoles(service, (email ?? string.Empty).Trim());

            // A person whose only active mapping carries the legacy al_approle picklist (no
            // al_rolecode) has to show up here too. RoleCodes alone used to be returned, so
            // such a person resolved to an empty array and the client — now treating "empty"
            // as "resolved and holds nothing" rather than "unconfigured" — showed them "No
            // access" everywhere, while the server (which reads AppRoles separately in
            // MaxLevel) still accepted their writes. Translating the option value to its
            // label lets the client match the same permission rules the gate does.
            var codes = new List<string>(roles.RoleCodes);
            foreach (var appRole in roles.AppRoles)
            {
                var label = AppRoleLabel(appRole);
                if (label != null && !codes.Contains(label))
                {
                    codes.Add(label);
                }
            }

            return codes;
        }

        /// <summary>
        /// The caller's roles: their web roles, plus whatever the mapping table holds.
        ///
        /// A web role contributes its NAME as a role code, which is the same shape
        /// al_pagepermission.al_rolecode matches on, so a rule written against a web role
        /// and a rule written against an AD-044 custom role are indistinguishable to the
        /// gate — as they should be.
        /// </summary>
        private static CallerRoles GetActiveRoles(IOrganizationService service, string email)
        {
            var roles = GetMappedRoles(service, email);

            var contact = WebRoleRegistry.FindContactByEmail(service, email);
            if (contact != null)
            {
                foreach (var webRole in WebRoleRegistry.RolesForContact(service, contact.Id))
                {
                    if (WebRoleRegistry.ExcludedFromResolution(service, webRole))
                    {
                        continue;
                    }

                    if (!roles.RoleCodes.Contains(webRole))
                    {
                        roles.RoleCodes.Add(webRole);
                    }
                }
            }

            return roles;
        }

        private static CallerRoles GetMappedRoles(IOrganizationService service, string email)
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
                // al_rolecode outranks al_approle (AD-044), which is also how the client
                // reads a mapping. The order used to be reversed, and that was not
                // harmless: al_approle carries a schema default, so every row written with
                // only a role code came back ALSO carrying a picklist value, and the
                // picklist won. Every web role assignment resolved as that default rather
                // than as the role it named.
                var code = entity.GetAttributeValue<string>("al_rolecode");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    var trimmed = code.Trim();

                    // AD-090: a mapping row is the OTHER half of the union GetActiveRoles
                    // forms (the web-role loop below already skips an excluded role), and
                    // nothing stopped a mapping row from being written for a role flagged
                    // auto-granted-to-everyone — al_AssignUserRole's own picklist name filter
                    // does not catch a flagged role under another name, and a restored row
                    // never passed through that filter at all. Filtering HERE, at the
                    // resolver both write paths feed, is what makes AD-090 true for every
                    // writer that exists today and every one added later, rather than only
                    // for the ones a reviewer remembered to guard individually.
                    if (WebRoleRegistry.ExcludedFromResolution(service, trimmed))
                    {
                        continue;
                    }

                    roles.RoleCodes.Add(trimmed);
                    continue;
                }

                var role = entity.GetAttributeValue<OptionSetValue>("al_approle");
                if (role != null)
                {
                    roles.AppRoles.Add(role.Value);
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

        /// <summary>
        /// The inverse of <see cref="ParseRole"/>: an al_approle option value back to its
        /// label. Exists so a legacy picklist-only assignment resolves in the client the
        /// same way it resolves in the gate (AD-089) — ResolveRoleCodesForEmail has to hand
        /// the client something to match a permission rule written against the label, and
        /// the option value alone carries no name. Kept to the same six labels ParseRole
        /// accepts; "T&C Manager" is returned rather than the "T and C Manager" alias since
        /// that is the one form both sides of the map agree on.
        /// </summary>
        public static string AppRoleLabel(int value)
        {
            switch (value)
            {
                case 120910760: return "Tax Checker";
                case 120910761: return "AQS Checker";
                case 120910762: return "Adviser";
                case 120910763: return "T&C Manager";
                case 120910764: return "Outcome Testing Manager";
                case 120910765: return "Administrator";
                default: return null;
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
