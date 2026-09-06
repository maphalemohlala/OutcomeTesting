using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The read behind the role detail screen. The two reads it performs are a
    /// QueryExpression over the mapping table and a FetchXML over the contact-side
    /// intersect — the intersect cannot be queried from the role side.
    /// </summary>
    public class GetRoleHoldersPluginTests
    {
        private const string Role = "AL Portal - Tax Reviewer";

        private static FakeOrganizationService WithMapping(string email, bool active)
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", email,
                "al_rolecode", Role,
                "statecode", new OptionSetValue(active ? 0 : 1));
            return svc;
        }

        private static void ReturnsAssociatedContacts(FakeOrganizationService svc, params string[] emails)
        {
            var rows = emails.Select(email =>
            {
                var contact = new Entity("contact", Guid.NewGuid());
                contact["emailaddress1"] = email;
                contact["fullname"] = "Person " + email;
                return contact;
            }).ToList();

            svc.FetchResults.Enqueue(new EntityCollection(rows));
        }

        [Fact]
        public void ReportsSomeoneGrantedOnlyInPowerPages()
        {
            var svc = new FakeOrganizationService();
            ReturnsAssociatedContacts(svc, "portal@ascotlloyd.co.uk");

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.Null(holder.MappingId);
            Assert.True(holder.Associated);
        }

        [Fact]
        public void ReportsAWithdrawnMappingThatIsStillAssociated()
        {
            var svc = WithMapping("z@ascotlloyd.co.uk", active: false);
            ReturnsAssociatedContacts(svc, "z@ascotlloyd.co.uk");

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.False(holder.MappingActive);
            Assert.True(holder.Associated);
        }

        [Fact]
        public void ReportsAnActiveMappingWhoseAssociationIsGone()
        {
            var svc = WithMapping("gone@ascotlloyd.co.uk", active: true);
            ReturnsAssociatedContacts(svc);

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.True(holder.MappingActive);
            Assert.False(holder.Associated);
        }

        [Fact]
        public void LeavesOutMappingsForOtherRoles()
        {
            var svc = WithMapping("tax@ascotlloyd.co.uk", active: true);
            svc.Seed(
                "al_userrolemapping",
                Guid.NewGuid(),
                "al_useremail", "planner@ascotlloyd.co.uk",
                "al_rolecode", "AL Portal - Planner",
                "statecode", new OptionSetValue(0));
            ReturnsAssociatedContacts(svc);

            var holder = Assert.Single(GetRoleHoldersPlugin.Read(svc, Role));
            Assert.Equal("tax@ascotlloyd.co.uk", holder.Email);
        }

        [Fact]
        public void AsksForTheRoleByNameOnTheContactSideIntersect()
        {
            var svc = WithMapping("a@ascotlloyd.co.uk", active: true);
            ReturnsAssociatedContacts(svc);

            GetRoleHoldersPlugin.Read(svc, Role);

            var fetch = Assert.Single(svc.FetchXml);
            Assert.Contains(WebRoleRegistry.ContactRelationship, fetch);
            Assert.Contains(Role, fetch);
        }

        [Fact]
        public void EscapesARoleNameCarryingXmlSoTheFetchStaysWellFormed()
        {
            var svc = new FakeOrganizationService();
            ReturnsAssociatedContacts(svc);

            GetRoleHoldersPlugin.Read(svc, "Tax & <Advice>");

            Assert.Contains("Tax &amp; &lt;Advice&gt;", Assert.Single(svc.FetchXml));
        }
    }
}
