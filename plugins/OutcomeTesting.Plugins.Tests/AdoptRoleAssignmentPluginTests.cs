using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Reconciling a role held in two places (AD-089). Adopt converges on granted, Revoke
    /// converges on not granted, and each has to work from every inconsistent starting
    /// state rather than assuming one.
    /// </summary>
    public class AdoptRoleAssignmentPluginTests
    {
        private const string Email = "person@ascotlloyd.co.uk";
        private const string Role = "AL Portal - Tax Reviewer";

        private static readonly Guid ContactId = Guid.Parse("66666666-ffff-4fff-8fff-666666666666");
        private static readonly Guid RoleId = Guid.Parse("77777777-aaaa-4aaa-8aaa-777777777777");

        private static FakeOrganizationService Environment(bool withMapping, bool mappingActive)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "A Person");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, Role,
                WebRoleRegistry.AuthenticatedAttr, false);

            if (withMapping)
            {
                svc.Seed(
                    "al_userrolemapping",
                    Guid.NewGuid(),
                    "al_useremail", Email,
                    "al_rolecode", Role,
                    "statecode", new OptionSetValue(mappingActive ? 0 : 1));
            }

            return svc;
        }

        [Fact]
        public void AdoptingAPortalOnlyGrantWritesTheMissingMapping()
        {
            var svc = Environment(withMapping: false, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.True(result.Adopted);
            Assert.NotNull(result.MappingId);
            Assert.Contains(svc.Creates, row => row.LogicalName == "al_userrolemapping");
        }

        [Fact]
        public void AdoptingAWithdrawnMappingReactivatesItRatherThanWritingASecond()
        {
            var svc = Environment(withMapping: true, mappingActive: false);

            AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.DoesNotContain(svc.Creates, row => row.LogicalName == "al_userrolemapping");
            Assert.Contains(svc.Updates, row => row.LogicalName == "al_userrolemapping");
        }

        [Fact]
        public void AdoptingRepairsAMissingAssociation()
        {
            var svc = Environment(withMapping: true, mappingActive: true);

            AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.Contains(
                svc.Associations,
                a => a.Item1 == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingRemovesTheAssociation()
        {
            var svc = Environment(withMapping: false, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: false);

            Assert.False(result.Adopted);
            Assert.Contains(
                svc.Disassociations,
                d => d.Item1 == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingAlsoWithdrawsAMappingWhereOneExists()
        {
            var svc = Environment(withMapping: true, mappingActive: true);

            AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: false);

            var update = Assert.Single(svc.Updates.Where(r => r.LogicalName == "al_userrolemapping"));
            Assert.Equal(1, update.GetAttributeValue<OptionSetValue>("statecode").Value);
        }

        [Fact]
        public void RefusesAPersonWithNoContactRatherThanWritingAMappingNothingGrants()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, Role,
                WebRoleRegistry.AuthenticatedAttr, false);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AdoptRoleAssignmentPlugin.Apply(svc, "nobody@ascotlloyd.co.uk", Role, adopt: true));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
        }

        [Fact]
        public void RefusesARoleThatDoesNotExist()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "A Person");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AdoptRoleAssignmentPlugin.Apply(svc, Email, "No Such Role", adopt: true));

            Assert.StartsWith(CommandHelpers.NotFoundPrefix, error.Message);
        }

        [Fact]
        public void SaysWhatItReconciledSoTheAuditLineIsReadable()
        {
            var svc = Environment(withMapping: true, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, Email, Role, adopt: true);

            Assert.Contains("Adopt", result.Details);
            Assert.Contains(Role, result.Details);
        }
    }
}
