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

        /// <summary>Rows fetched per page when paging the contact-side FetchXML below.</summary>
        private const int PageSize = 5000;

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
        ///
        /// Pages via the FetchXML page/count attributes and paging-cookie, following
        /// CommandHelpers.RetrieveAll's own warning: a bare RetrieveMultiple stops at 5000
        /// rows with no error and no signal, which here would report every dropped contact
        /// as associated:false — a fabricated "association missing" that invites an
        /// administrator to act on a fact that is not true. CommandHelpers.RetrieveAll itself
        /// only follows QueryExpression's PagingInfo, not a hand-built FetchXML string, so
        /// the paging is done here instead.
        /// </summary>
        private static List<Entity> ContactsHolding(IOrganizationService service, string roleName)
        {
            var rows = new List<Entity>();
            string cookie = null;
            var page = 1;

            while (true)
            {
                var fetch = BuildContactsFetch(roleName, page, PageSize, cookie);
                var result = service.RetrieveMultiple(new FetchExpression(fetch));
                rows.AddRange(result.Entities);

                if (!result.MoreRecords)
                {
                    return rows;
                }

                cookie = result.PagingCookie;
                page++;
            }
        }

        private static string BuildContactsFetch(string roleName, int page, int count, string pagingCookie)
        {
            var pagingAttrs = " page='" + page + "' count='" + count + "'";
            if (!string.IsNullOrEmpty(pagingCookie))
            {
                // The cookie Dataverse hands back already carries its own quoting; escaping it
                // again is what keeps it well-formed once embedded inside this attribute.
                pagingAttrs += " paging-cookie='" + System.Security.SecurityElement.Escape(pagingCookie) + "'";
            }

            return
                "<fetch" + pagingAttrs + ">" +
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
        }
    }
}
