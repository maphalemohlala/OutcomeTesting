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

        // Matches AdoptRoleAssignmentPlugin.Apply's own code construction exactly, so a
        // seeded "existing mapping" is actually reachable by AssignUserRolePlugin.Upsert's
        // query on al_userrolemappingcode rather than silently missed and duplicated.
        private static string MappingCode(string email, string role) =>
            "URM-" + email.ToLowerInvariant() + "-" + role;

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
                    "al_userrolemappingcode", MappingCode(Email, Role),
                    "statecode", new OptionSetValue(mappingActive ? 0 : 1));
            }

            return svc;
        }

        [Fact]
        public void AdoptingAPortalOnlyGrantWritesTheMissingMapping()
        {
            var svc = Environment(withMapping: false, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: true);

            Assert.True(result.Adopted);
            Assert.NotNull(result.MappingId);

            var created = Assert.Single(svc.Creates, row => row.LogicalName == "al_userrolemapping");
            Assert.Equal(Role + " - " + Email, created.GetAttributeValue<string>("al_name"));
            Assert.Equal(MappingCode(Email, Role), created.GetAttributeValue<string>("al_userrolemappingcode"));
        }

        [Fact]
        public void AdoptingAWithdrawnMappingReactivatesItRatherThanWritingASecond()
        {
            var svc = Environment(withMapping: true, mappingActive: false);

            AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: true);

            Assert.DoesNotContain(svc.Creates, row => row.LogicalName == "al_userrolemapping");
            Assert.Contains(svc.Updates, row => row.LogicalName == "al_userrolemapping");
        }

        [Fact]
        public void AdoptingRepairsAMissingAssociation()
        {
            var svc = Environment(withMapping: true, mappingActive: true);

            AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: true);

            Assert.Contains(
                svc.Associations,
                a => a.Item1 == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingRemovesTheAssociation()
        {
            var svc = Environment(withMapping: false, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: false);

            Assert.False(result.Adopted);
            Assert.Contains(
                svc.Disassociations,
                d => d.Item1 == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingAlsoWithdrawsAMappingWhereOneExists()
        {
            var svc = Environment(withMapping: true, mappingActive: true);

            AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: false);

            var update = Assert.Single(svc.Updates, r => r.LogicalName == "al_userrolemapping");
            Assert.Equal(1, update.GetAttributeValue<OptionSetValue>("statecode").Value);
        }

        [Fact]
        public void RevokingDeactivatesAllActiveRowsWhenTwoExistForThePair()
        {
            // A row per (email, role) is only guaranteed unique going forward, once every
            // write goes through the al_userrolemappingcode upsert key. Two active rows for
            // the same pair are a state a pre-existing or out-of-band row can still produce,
            // and PermissionHelpers.GetMappedRoles honours ANY active row — so revoke must
            // not stop at the first one it finds.
            var svc = Environment(withMapping: false, mappingActive: false);
            var first = svc.Seed(
                "al_userrolemapping", Guid.NewGuid(),
                "al_useremail", Email, "al_rolecode", Role, "statecode", new OptionSetValue(0));
            var second = svc.Seed(
                "al_userrolemapping", Guid.NewGuid(),
                "al_useremail", Email, "al_rolecode", Role, "statecode", new OptionSetValue(0));

            AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: false);

            var deactivatedIds = svc.Updates
                .Where(r => r.LogicalName == "al_userrolemapping")
                .Select(r => r.Id)
                .ToList();
            Assert.Contains(first.Id, deactivatedIds);
            Assert.Contains(second.Id, deactivatedIds);
            Assert.Equal(2, deactivatedIds.Count);
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
                () => AdoptRoleAssignmentPlugin.Apply(svc, svc, "nobody@ascotlloyd.co.uk", Role, adopt: true));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
        }

        [Fact]
        public void RefusesARoleThatDoesNotExist()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "A Person");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, "No Such Role", adopt: true));

            Assert.StartsWith(CommandHelpers.NotFoundPrefix, error.Message);
        }

        [Fact]
        public void AdoptingRefusesARoleAutoGrantedToEverySignedInUser()
        {
            // AD-090: mspp_authenticatedusersrole means every signed-in contact already
            // carries this role; adopting it would turn a grant the permission gate
            // deliberately ignores (WebRoleRegistry.ExcludedFromResolution) into one it
            // enforces for everyone, which is what OD-033 found happening with
            // Administrators in DEV.
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "A Person");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, Role,
                WebRoleRegistry.AuthenticatedAttr, true);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: true));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
        }

        [Fact]
        public void RevokingAnAutoGrantedRoleStillSucceedsSoABadRowCanBeCleanedUp()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, "emailaddress1", Email, "fullname", "A Person");
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, Role,
                WebRoleRegistry.AuthenticatedAttr, true);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: false);

            Assert.False(result.Adopted);
        }

        [Fact]
        public void SaysWhatItReconciledSoTheAuditLineIsReadable()
        {
            var svc = Environment(withMapping: true, mappingActive: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: true);

            Assert.Contains("Adopt", result.Details);
            Assert.Contains(Role, result.Details);
            Assert.Contains("Reconciled a role held in two places", result.Details);
        }
    }
}
