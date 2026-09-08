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

        /// <param name="associated">
        /// Whether the contact is already associated with the web role in Power Pages. This
        /// is not decoration: it is one of the two sources AD-089 reconciles, and every
        /// scenario worth testing is a particular combination of it and the mapping row.
        /// Defaulted to false only so the tests that seed it read as the exception.
        /// </param>
        private static FakeOrganizationService Environment(
            bool withMapping, bool mappingActive, bool associated = false)
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

            if (associated)
            {
                svc.SeedWebRoleAssociation(ContactId, RoleId);
            }

            return svc;
        }

        [Fact]
        public void AdoptingAPortalOnlyGrantWritesTheMissingMapping()
        {
            // Associated, because that is what "portal-only" means: the grant exists in
            // Power Pages and nowhere else. Seeding it without the association tested a
            // state AD-089 never has to reconcile.
            var svc = Environment(withMapping: false, mappingActive: false, associated: true);

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
            // "Withdrawn in app, still granted" — a withdrawn mapping whose association
            // survived, which is the second state the role detail screen offers Adopt for.
            var svc = Environment(withMapping: true, mappingActive: false, associated: true);

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
                a => a.Relationship == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingRemovesTheAssociation()
        {
            var svc = Environment(withMapping: false, mappingActive: false, associated: true);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: false);

            Assert.False(result.Adopted);
            Assert.Contains(
                svc.Disassociations,
                d => d.Relationship == WebRoleRegistry.ContactRelationship);
        }

        [Fact]
        public void RevokingAlsoWithdrawsAMappingWhereOneExists()
        {
            var svc = Environment(withMapping: true, mappingActive: true, associated: true);

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
            var svc = Environment(withMapping: false, mappingActive: false, associated: true);
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
            svc.SeedWebRoleAssociation(ContactId, RoleId);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: false);

            Assert.False(result.Adopted);
        }

        [Fact]
        public void AdoptingAPortalOnlyGrantDoesNotReAssociateWhatIsAlreadyAssociated()
        {
            // The failure the DEV write-path proof hit at its first adopt. The association
            // is always already there when a portal-only grant is adopted, so a plug-in that
            // calls Associate anyway takes the duplicate-key fault — and a plug-in that
            // catches that fault and carries on has its whole transaction aborted by the
            // platform. Neither is acceptable, so the write has to be skipped.
            var svc = Environment(withMapping: false, mappingActive: false, associated: true);

            AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: true);

            Assert.Equal(0, svc.AssociateAttempts);
        }

        [Fact]
        public void RevokingAGrantThatIsAlreadyGoneDoesNotCallDisassociate()
        {
            // The other side, where the platform turns out to be forgiving: removing a pair
            // that is not associated succeeds rather than faulting (observed against DEV,
            // 2026-09-07). So this pins a smaller claim than its Associate counterpart —
            // revoking an "assigned in app, association missing" row does not spend a write
            // it has no need for — but it is what keeps the tolerant catch from coming back.
            var svc = Environment(withMapping: true, mappingActive: true, associated: false);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: false);

            Assert.False(result.Adopted);
            Assert.Equal(0, svc.DisassociateAttempts);
        }

        [Fact]
        public void SaysWhatItReconciledSoTheAuditLineIsReadable()
        {
            var svc = Environment(withMapping: true, mappingActive: false, associated: true);

            var result = AdoptRoleAssignmentPlugin.Apply(svc, svc, Email, Role, adopt: true);

            Assert.Contains("Adopt", result.Details);
            Assert.Contains(Role, result.Details);
            Assert.Contains("Reconciled a role held in two places", result.Details);
        }
    }
}
