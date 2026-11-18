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
        /// The two reviewer web roles (powerpages/.../webrole.yml), which the queue claim
        /// checks against the discipline a case owes. Named here rather than looked up by
        /// id, because the name is what al_rolecode and the permission rules carry.
        /// </summary>
        public const string TaxReviewerRole = "AL Portal - Tax Reviewer";
        public const string AqsReviewerRole = "AL Portal - AQS Reviewer";

        /// <summary>The attesting role (BR-008), which the portal sign-off checks server-side (AD-099).</summary>
        public const string TcSupervisorRole = "AL Portal - T&C Supervisor";

        /// <summary>
        /// What makes a contact an adviser or a paraplanner: the web role they hold (project
        /// owner, 2026-11-17). Only holders of one of these can be named as carrying a fail.
        /// </summary>
        public const string AdviserRole = "AL Portal - Adviser Remediation";
        public const string PlannerRole = "AL Portal - Planner";

        /// <summary>Refuses a named person <see cref="AccountableRefusal"/> rejects; nobody named passes.</summary>
        public static void EnsureAccountable(IOrganizationService service, EntityReference contact)
        {
            EnsureAccountable(service, contact, null);
        }

        /// <summary>
        /// As above, but the person the check already names passes: re-saving the ticks must not
        /// be refused because a role was withdrawn after that person was named.
        /// </summary>
        public static void EnsureAccountable(IOrganizationService service, EntityReference contact, EntityReference alreadyNamed)
        {
            if (contact != null && alreadyNamed != null && contact.Id == alreadyNamed.Id)
            {
                return;
            }

            var refusal = contact == null ? null : AccountableRefusal(service, contact.Id);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }
        }

        /// <summary>
        /// Why this contact cannot be named as accountable for a fail, or null when they can.
        ///
        /// The one copy of the rule, used by al_SetFailAccountability and by the portal's
        /// accountability request. A deactivated contact still passes when they hold the role:
        /// the person may have left since the check, and the fail is still theirs. It reads the
        /// web-role link, which every Outcome Testing role can read (Global on powerpagecomponent).
        /// </summary>
        public static string AccountableRefusal(IOrganizationService service, Guid contactId)
        {
            foreach (var role in RolesForContact(service, contactId))
            {
                if (string.Equals(role, AdviserRole, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(role, PlannerRole, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            // A query rather than a Retrieve: a missing contact answers with no row instead
            // of a fault, which a plug-in may not catch and carry on from.
            var query = new QueryExpression(ContactRegistry.Entity)
            {
                ColumnSet = new ColumnSet(ContactRegistry.FullNameAttr, ContactRegistry.EmailAttr),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("contactid", ConditionOperator.Equal, contactId);
            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return "The person named as accountable could not be found, so they can't be named. "
                    + "Choose someone else and try again.";
            }

            var name = (found[0].GetAttributeValue<string>(ContactRegistry.FullNameAttr) ?? string.Empty).Trim();
            var email = (found[0].GetAttributeValue<string>(ContactRegistry.EmailAttr) ?? string.Empty).Trim();
            var who = name == string.Empty ? (email == string.Empty ? "This person" : email)
                : email == string.Empty ? name : name + " (" + email + ")";

            return who + " can't be named as accountable because they don't hold the Adviser or "
                + "Planner role. An administrator can assign it on the People page.";
        }

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
        /// Whether a contact holds one named web role. The portal plug-ins each gate a
        /// write on this (the claim, the regrade, the sign-off and the Tax team header edit),
        /// and each used to loop over <see cref="RolesForContact"/> itself; one comparison
        /// here means a renamed or trimmed role is handled once, on every gate.
        /// </summary>
        public static bool HasRole(IOrganizationService service, Guid contactId, string roleName)
        {
            foreach (var role in RolesForContact(service, contactId))
            {
                if (string.Equals(role, roleName, StringComparison.OrdinalIgnoreCase))
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
                    "<attribute name='statecode'/>" +
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
                        "<attribute name='statecode'/>" +
                      "</link-entity>" +
                    "</link-entity>" +
                  "</entity>" +
                "</fetch>";

            var names = new List<string>();
            foreach (var row in service.RetrieveMultiple(new FetchExpression(fetch)).Entities)
            {
                // A retired role grants nothing. Retiring withdraws the mappings and rules but
                // leaves the association, so without this a retired role still counted here,
                // and through here on every portal gate and in the caller's resolved roles.
                var state = row.GetAttributeValue<AliasedValue>("role.statecode");
                var stateValue = state == null ? null : state.Value as OptionSetValue;
                if (stateValue != null && stateValue.Value != 0)
                {
                    continue;
                }

                var aliased = row.GetAttributeValue<AliasedValue>("role.name");
                var name = aliased == null ? null : aliased.Value as string;
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name.Trim()))
                {
                    names.Add(name.Trim());
                }
            }

            return names;
        }

        /// <summary>
        /// Whether a contact is already associated with one web role.
        ///
        /// Exists so the association writes can CHECK rather than attempt-and-catch.
        /// Dataverse aborts a plug-in's whole transaction when it swallows a fault from an
        /// OrganizationService call and carries on — "ISV code reduced the open transaction
        /// count" — so the tolerant catch that used to sit in AssignUserRolePlugin turned the
        /// idempotent case into a hard failure. That case is not an edge: adopting a
        /// portal-only grant (AD-089) associates a contact that is ALREADY associated, every
        /// single time, which is what the DEV write-path proof hit at its first adopt.
        ///
        /// Queried on the intersect directly, with no join. mspp_webrole is a typed surface
        /// over powerpagecomponent and will not answer a plain QueryExpression, but the
        /// intersect does answer a flat FetchXML on its two id columns — and
        /// mspp_webroleid and powerpagecomponentid are the same id, which is why the role id
        /// goes into the second condition unchanged.
        /// </summary>
        public static bool IsAssociated(IOrganizationService service, Guid contactId, Guid webRoleId)
        {
            var fetch =
                "<fetch>" +
                  "<entity name='" + ContactRelationship + "'>" +
                    "<attribute name='contactid'/>" +
                    "<filter>" +
                      "<condition attribute='contactid' operator='eq' value='" + contactId.ToString("D") + "'/>" +
                      "<condition attribute='powerpagecomponentid' operator='eq' value='" + webRoleId.ToString("D") + "'/>" +
                    "</filter>" +
                  "</entity>" +
                "</fetch>";

            return service.RetrieveMultiple(new FetchExpression(fetch)).Entities.Count > 0;
        }

        /// <summary>
        /// The contact carrying a work email, or null. Web roles hang off contacts.
        ///
        /// Two contacts can share an email (a rehire beside the leaver's old row), so the pick
        /// is ordered: an active row first, then the oldest. It used to be whichever row the
        /// platform returned first, so the gate could read a deactivated duplicate and refuse
        /// someone the app showed as active. The app reads the same way: one active row is
        /// enough (types/permissions.ts, isDeactivated).
        /// </summary>
        public static Entity FindContactByEmail(IOrganizationService service, string email)
        {
            var query = new QueryExpression(ContactRegistry.Entity)
            {
                ColumnSet = new ColumnSet(
                    ContactRegistry.FullNameAttr, ContactRegistry.EmailAttr, ContactRegistry.StateCodeAttr),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(ContactRegistry.EmailAttr, ConditionOperator.Equal, email);
            query.AddOrder(ContactRegistry.StateCodeAttr, OrderType.Ascending);
            query.AddOrder("createdon", OrderType.Ascending);

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
                    // IsActive reads this. Without it the platform returns no statecode, and
                    // every retired role read as active to every caller of IsActive.
                    "<attribute name='statecode'/>" +
                    "<filter><condition attribute='" + NameAttr + "' operator='eq' value='" +
                      System.Security.SecurityElement.Escape(roleName) + "'/></filter>" +
                  "</entity>" +
                "</fetch>";

            var found = service.RetrieveMultiple(new FetchExpression(fetch)).Entities;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>
        /// Whether a role found by <see cref="FindByName"/> can still be granted. FindByName
        /// keeps returning retired roles, because disassociating one still has to find it.
        /// </summary>
        public static bool IsActive(Entity role)
        {
            var state = role.GetAttributeValue<OptionSetValue>("statecode");
            return state == null || state.Value == 0;
        }

        /// <summary>
        /// True when a web role by this name exists and is retired. A name no web role carries
        /// is not retired: it may be a legacy picklist label, which this registry never held.
        /// </summary>
        public static bool IsRetired(IOrganizationService service, string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                return false;
            }

            var role = FindByName(service, roleName.Trim());
            return role != null && !IsActive(role);
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
