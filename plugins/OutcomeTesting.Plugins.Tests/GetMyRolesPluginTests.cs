using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What the client is told it holds. This has to be the SAME union the server enforces
    /// with, because the whole defect AD-089 closes is the client deriving a different
    /// answer from the mapping table alone.
    /// </summary>
    public class GetMyRolesPluginTests
    {
        private const string Email = "person@ascotlloyd.co.uk";

        private static FakeOrganizationService WithContact(Guid contactId)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", contactId, "emailaddress1", Email, "fullname", "A Person");
            return svc;
        }

        private static void ReturnsWebRoles(FakeOrganizationService svc, params string[] names)
        {
            var rows = new List<Entity>();
            foreach (var name in names)
            {
                var row = new Entity("contact", Guid.NewGuid());
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
        }

        [Fact]
        public void UnionsTheMappingTableWithTheWebRoleAssociations()
        {
            var contactId = Guid.NewGuid();
            var svc = WithContact(contactId);
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_rolecode", "AL Portal - Planner",
                "statecode", new OptionSetValue(0));
            ReturnsWebRoles(svc, "AL Portal - Tax Reviewer");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "AL Portal - Tax Reviewer",
                WebRoleRegistry.AuthenticatedAttr, false);

            var codes = PermissionHelpers.ResolveRoleCodesForEmail(svc, Email);

            Assert.Contains("AL Portal - Planner", codes);
            Assert.Contains("AL Portal - Tax Reviewer", codes);
        }

        [Fact]
        public void LeavesOutARoleAutoGrantedToEveryone()
        {
            var svc = WithContact(Guid.NewGuid());
            ReturnsWebRoles(svc, "Administrators");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, true);

            Assert.DoesNotContain("Administrators", PermissionHelpers.ResolveRoleCodesForEmail(svc, Email));
        }

        [Fact]
        public void LeavesOutAMappingRowForARoleAutoGrantedToEveryone()
        {
            // AD-090: the web-role loop already skips a flagged role (the test above), but
            // GetMappedRoles reads al_rolecode verbatim, and nothing stopped a mapping row
            // from naming a flagged role — the app's own "Assign a role" modal offers it
            // under a name that is not one of the two picklist system roles it filters by,
            // and a restored row (al_SetRoleAssignmentActive) never passes through that
            // filter at all. This has to be excluded at the resolver, not only at the
            // writers, for the rule to hold for every writer present and future.
            // No contact seeded: this exercises GetMappedRoles in isolation (it runs before
            // the web-role/contact lookup), so nothing here should touch the FetchResults
            // queue that a joined web-role fetch would need.
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_rolecode", "Administrators",
                "statecode", new OptionSetValue(0));
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, true);

            Assert.DoesNotContain("Administrators", PermissionHelpers.ResolveRoleCodesForEmail(svc, Email));
        }

        [Fact]
        public void TranslatesAPicklistOnlyMappingToItsLabel()
        {
            // AD-089: a person whose only active mapping carries the legacy al_approle
            // picklist, with no al_rolecode, used to resolve to an empty array here — read by
            // the client as "resolved and holds nothing" — while the gate (MaxLevel) still
            // honoured the picklist and accepted their writes. The client needs the same
            // label ParseRole accepts so a permission rule written against it still matches.
            var svc = WithContact(Guid.NewGuid());
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_approle", new OptionSetValue(120910760),
                "statecode", new OptionSetValue(0));
            ReturnsWebRoles(svc);

            var codes = PermissionHelpers.ResolveRoleCodesForEmail(svc, Email);

            Assert.Contains("Tax Checker", codes);
        }

        [Fact]
        public void LeavesOutAWithdrawnMapping()
        {
            var svc = WithContact(Guid.NewGuid());
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", Email,
                "al_rolecode", "AL Portal - Planner",
                "statecode", new OptionSetValue(1));
            ReturnsWebRoles(svc);

            Assert.Empty(PermissionHelpers.ResolveRoleCodesForEmail(svc, Email));
        }

        [Fact]
        public void EmitsAJsonArrayOfStrings()
        {
            Assert.Equal("[]", GetMyRolesPlugin.ToJson(new List<string>()));
            Assert.Equal("[\"A\",\"B\"]", GetMyRolesPlugin.ToJson(new List<string> { "A", "B" }));
        }
    }
}
