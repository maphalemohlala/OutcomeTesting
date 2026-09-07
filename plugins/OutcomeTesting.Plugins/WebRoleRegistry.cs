using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Power Pages web roles read as the application role vocabulary (AD-041, AD-044).
    ///
    /// This environment runs the enhanced portal data model, so the table is
    /// <c>mspp_webrole</c> — a typed surface over <c>powerpagecomponent</c> — and not
    /// <c>adx_webrole</c>. The contact link is the intersect
    /// <c>powerpagecomponent_mspp_webrole_contact</c>, reached from the CONTACT side:
    /// mspp_webrole carries no many-to-many of its own, which is why a query starting at
    /// the role finds nothing.
    ///
    /// A web role travels through the existing <c>al_rolecode</c> text column, which
    /// AD-044 already reserved for custom roles and which outranks the al_approle
    /// picklist. Nothing in the schema changes to support this.
    /// </summary>
    public static class WebRoleRegistry
    {
        public const string RoleEntity = "mspp_webrole";
        public const string NameAttr = "mspp_name";
        public const string DescriptionAttr = "mspp_description";
        public const string WebsiteAttr = "mspp_websiteid";
        public const string AuthenticatedAttr = "mspp_authenticatedusersrole";
        public const string AnonymousAttr = "mspp_anonymoususersrole";
        public const string ContactRelationship = "powerpagecomponent_mspp_webrole_contact";

        /// <summary>
        /// Roles that exist to make Power Pages work rather than to describe a job. Offering
        /// them as application roles would invite granting business access to "everyone who
        /// is signed in", which is not a decision any requirement makes.
        /// </summary>
        private static readonly HashSet<string> SystemRoles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Anonymous Users",
                "Authenticated Users",
            };

        public static bool IsSystemRole(string roleName)
        {
            return roleName != null && SystemRoles.Contains(roleName.Trim());
        }

        /// <summary>
        /// Whether a web role must be ignored when resolving what a caller may do (AD-090).
        ///
        /// Two reasons, and the second is the one that matters. `Anonymous Users` and
        /// `Authenticated Users` are Power Pages plumbing and excluded by name. Beyond them, ANY
        /// role carrying <c>mspp_authenticatedusersrole</c> OR <c>mspp_anonymoususersrole</c> is
        /// auto-granted (to every signed-in contact, or to everyone at all) rather than to
        /// people a decision put there, so treating it as an application role grants that role
        /// to everyone — which is exactly what OD-033 found `Administrators` doing in DEV.
        ///
        /// Deliberately NOT FindByName: mspp_name carries no website filter and FindByName
        /// returns an arbitrary found[0], so a second web role sharing a name with a flagged
        /// one — with the flag off — would make this return false for a role that IS
        /// auto-granted under that name. Every row matching the name is checked, and ANY of
        /// them carrying either flag is enough to exclude.
        ///
        /// A role that cannot be looked up at all is NOT excluded. Absence of evidence that a
        /// role is auto-granted is not evidence that it is, and excluding on a failed read
        /// would withdraw access from everyone holding it.
        /// </summary>
        public static bool ExcludedFromResolution(IOrganizationService service, string roleName)
        {
            if (IsSystemRole(roleName))
            {
                return true;
            }

            var trimmed = (roleName ?? string.Empty).Trim();
            var matches = FindAllByName(service, trimmed);
            if (matches.Count == 0)
            {
                return false;
            }

            foreach (var role in matches)
            {
                if ((role.GetAttributeValue<bool?>(AuthenticatedAttr) ?? false) ||
                    (role.GetAttributeValue<bool?>(AnonymousAttr) ?? false))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Every web role row named <paramref name="roleName"/>, not just an arbitrary one.
        /// Backs <see cref="ExcludedFromResolution"/> only; <see cref="FindByName"/> keeps its
        /// found[0] behaviour for association targets, which is a separate pre-existing
        /// concern this does not touch.
        /// </summary>
        private static List<Entity> FindAllByName(IOrganizationService service, string roleName)
        {
            // No `top` attribute: mspp_webrole ignores it and returns nothing.
            var fetch =
                "<fetch>" +
                  "<entity name='" + RoleEntity + "'>" +
                    "<attribute name='" + NameAttr + "'/>" +
                    "<attribute name='" + AuthenticatedAttr + "'/>" +
                    "<attribute name='" + AnonymousAttr + "'/>" +
                    "<attribute name='" + WebsiteAttr + "'/>" +
                    "<filter><condition attribute='" + NameAttr + "' operator='eq' value='" +
                      System.Security.SecurityElement.Escape(roleName) + "'/></filter>" +
                  "</entity>" +
                "</fetch>";

            return new List<Entity>(service.RetrieveMultiple(new FetchExpression(fetch)).Entities);
        }

        /// <summary>
        /// The web role names associated with a contact.
        ///
        /// FetchXML rather than QueryExpression: mspp_webrole does not answer a plain
        /// QueryExpression in this environment, and the join has to start at the contact
        /// because the intersect hangs off contact, not off the role.
        /// </summary>
        public static List<string> RolesForContact(IOrganizationService service, Guid contactId)
        {
            var fetch =
                "<fetch>" +
                  "<entity name='contact'>" +
                    "<attribute name='contactid'/>" +
                    "<filter><condition attribute='contactid' operator='eq' value='" + contactId.ToString("D") + "'/></filter>" +
                    "<link-entity name='" + ContactRelationship + "' from='contactid' to='contactid' intersect='true'>" +
                      "<link-entity name='powerpagecomponent' from='powerpagecomponentid' to='powerpagecomponentid' alias='role'>" +
                        "<attribute name='name'/>" +
                      "</link-entity>" +
                    "</link-entity>" +
                  "</entity>" +
                "</fetch>";

            var names = new List<string>();
            foreach (var row in service.RetrieveMultiple(new FetchExpression(fetch)).Entities)
            {
                var aliased = row.GetAttributeValue<AliasedValue>("role.name");
                var name = aliased == null ? null : aliased.Value as string;
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name.Trim()))
                {
                    names.Add(name.Trim());
                }
            }

            return names;
        }

        /// <summary>The contact carrying a work email, or null. Web roles hang off contacts.</summary>
        public static Entity FindContactByEmail(IOrganizationService service, string email)
        {
            var query = new QueryExpression(ContactRegistry.Entity)
            {
                ColumnSet = new ColumnSet(ContactRegistry.FullNameAttr, ContactRegistry.EmailAttr),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(ContactRegistry.EmailAttr, ConditionOperator.Equal, email);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>
        /// A web role by name. Matched on name because that is what al_rolecode carries and
        /// what the permission rules reference.
        /// </summary>
        public static Entity FindByName(IOrganizationService service, string roleName)
        {
            // No `top` attribute: mspp_webrole ignores it and returns nothing.
            var fetch =
                "<fetch>" +
                  "<entity name='" + RoleEntity + "'>" +
                    "<attribute name='" + NameAttr + "'/>" +
                    "<attribute name='" + AuthenticatedAttr + "'/>" +
                    "<attribute name='" + WebsiteAttr + "'/>" +
                    "<filter><condition attribute='" + NameAttr + "' operator='eq' value='" +
                      System.Security.SecurityElement.Escape(roleName) + "'/></filter>" +
                  "</entity>" +
                "</fetch>";

            var found = service.RetrieveMultiple(new FetchExpression(fetch)).Entities;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>The website every web role belongs to, taken from any existing role.</summary>
        public static EntityReference AnyWebsite(IOrganizationService service)
        {
            var fetch =
                "<fetch><entity name='" + RoleEntity + "'>" +
                  "<attribute name='" + WebsiteAttr + "'/>" +
                "</entity></fetch>";

            foreach (var role in service.RetrieveMultiple(new FetchExpression(fetch)).Entities)
            {
                var website = role.GetAttributeValue<EntityReference>(WebsiteAttr);
                if (website != null)
                {
                    return website;
                }
            }

            return null;
        }
    }
}
