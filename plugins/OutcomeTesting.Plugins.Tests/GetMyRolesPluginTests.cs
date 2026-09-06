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
