using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side read GetRoleHolders (AD-003, AD-089). Registered against the Custom API
    /// message <c>al_GetRoleHolders</c>.
    ///
    /// This exists because the browser cannot perform the second read at all: the
    /// contact-to-web-role intersect has no many-to-many on mspp_webrole, so it is
    /// reachable only from the contact side and only by FetchXML, and the generated client
    /// has no $expand. Without this the app can show the mapping table and call it the
    /// truth, which is the state AD-089 closes.
    ///
    /// Reads only. It writes nothing and therefore audits nothing — there is no decision
    /// here to record. Runs as the plug-in user because it reports on everyone holding the
    /// role, not on the caller.
    /// </summary>
    public class GetRoleHoldersPlugin : PluginBase
    {
        private const string InRoleCode = "RoleCode";
        private const string OutHolders = "Holders";

        private const string MappingEntity = "al_userrolemapping";

        public GetRoleHoldersPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(GetRoleHoldersPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var systemService = localPluginContext.PluginUserService;

            var roleCode = CommandHelpers.GetRequiredString(context, InRoleCode);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "permission.manage", PermissionHelpers.AccessView);

            context.OutputParameters[OutHolders] = RoleHolders.ToJson(Read(systemService, roleCode));
        }

        /// <summary>
        /// The two reads and the join. Public and static so the disagreement cases are
        /// testable without standing up a plug-in context.
        /// </summary>
        public static List<RoleHolder> Read(IOrganizationService service, string roleCode)
        {
            var trimmed = (roleCode ?? string.Empty).Trim();

            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet("al_useremail", "al_rolecode", "statecode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_rolecode", ConditionOperator.Equal, trimmed);
            var mappings = CommandHelpers.RetrieveAll(service, query);

            return RoleHolders.Merge(mappings, ContactsHolding(service, trimmed));
        }

        /// <summary>
        /// Contacts associated with the role. Starts at contact because the intersect hangs
        /// off contact, not off the role — the same reason WebRoleRegistry.RolesForContact
        /// does, in the opposite direction.
        /// </summary>
        private static List<Entity> ContactsHolding(IOrganizationService service, string roleName)
        {
            var fetch =
                "<fetch>" +
                  "<entity name='contact'>" +
                    "<attribute name='contactid'/>" +
                    "<attribute name='emailaddress1'/>" +
                    "<attribute name='fullname'/>" +
                    "<link-entity name='" + WebRoleRegistry.ContactRelationship + "' from='contactid' to='contactid' intersect='true'>" +
                      "<link-entity name='powerpagecomponent' from='powerpagecomponentid' to='powerpagecomponentid' alias='role'>" +
                        "<filter><condition attribute='name' operator='eq' value='" +
                          System.Security.SecurityElement.Escape(roleName) + "'/></filter>" +
                      "</link-entity>" +
                    "</link-entity>" +
                  "</entity>" +
                "</fetch>";

            var rows = new List<Entity>();
            foreach (var row in service.RetrieveMultiple(new FetchExpression(fetch)).Entities)
            {
                rows.Add(row);
            }

            return rows;
        }
    }
}
