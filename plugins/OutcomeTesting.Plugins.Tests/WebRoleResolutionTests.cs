using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-090. A web role flagged as the authenticated-users role is granted to every
    /// signed-in contact, so it can never confer application permissions. OD-033 is why
    /// this is not a name check: `Administrators` carried that flag in DEV, and every
    /// contact resolved as Administrators.
    /// </summary>
    public class WebRoleResolutionTests
    {
        private static readonly Guid RoleId = Guid.Parse("44444444-dddd-4ddd-8ddd-444444444444");

        private static FakeOrganizationService WithRole(string name, bool authenticatedUsersRole)
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, name,
                WebRoleRegistry.AuthenticatedAttr, authenticatedUsersRole);
            return svc;
        }

        [Fact]
        public void ExcludesARoleCarryingTheAuthenticatedUsersFlagWhateverItIsCalled()
        {
            var svc = WithRole("Administrators", true);
            Assert.True(WebRoleRegistry.ExcludedFromResolution(svc, "Administrators"));
        }

        [Fact]
        public void KeepsAnOrdinaryBusinessRole()
        {
            var svc = WithRole("AL Portal - Tax Reviewer", false);
            Assert.False(WebRoleRegistry.ExcludedFromResolution(svc, "AL Portal - Tax Reviewer"));
        }

        [Theory]
        [InlineData("Anonymous Users")]
        [InlineData("Authenticated Users")]
        [InlineData("  authenticated users  ")]
        public void ExcludesTheTwoSystemRolesByNameWithoutNeedingTheFlag(string name)
        {
            var svc = WithRole(name, false);
            Assert.True(WebRoleRegistry.ExcludedFromResolution(svc, name));
        }

        [Fact]
        public void KeepsARoleThatCannotBeLookedUpRatherThanSilentlyDroppingIt()
        {
            // A role the query cannot resolve is not evidence that it is auto-granted.
            // Dropping it here would withdraw access on a transient read failure.
            var svc = new FakeOrganizationService();
            Assert.False(WebRoleRegistry.ExcludedFromResolution(svc, "AL Portal - Planner"));
        }

        [Fact]
        public void ExcludesADuplicateNamedRoleRegardlessOfWhichOneIsFlagged_FlaggedSeededFirst()
        {
            // FindByName has no website filter and returns an arbitrary found[0], so a
            // second web role sharing a name with a flagged one — with the flag off — must
            // not make this return false for a name that IS auto-granted. Every row named
            // "Administrators" is checked, not just whichever one a query happens to return
            // first.
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, true);
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.Parse("22222222-2222-4222-8222-222222222222"),
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, false);

            Assert.True(WebRoleRegistry.ExcludedFromResolution(svc, "Administrators"));
        }

        [Fact]
        public void ExcludesADuplicateNamedRoleRegardlessOfWhichOneIsFlagged_FlaggedSeededSecond()
        {
            // Same as above with the seed order reversed, since a Dictionary's enumeration
            // order is what FindByName's found[0] actually depends on.
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.Parse("33333333-3333-4333-8333-333333333333"),
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, false);
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.Parse("44444444-4444-4444-8444-444444444444"),
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, true);

            Assert.True(WebRoleRegistry.ExcludedFromResolution(svc, "Administrators"));
        }

        [Fact]
        public void ExcludesARoleCarryingTheAnonymousUsersFlagWhateverItIsCalled()
        {
            // M9: the anonymous-users flag excludes for the same reason the
            // authenticated-users flag does — it is granted to everyone, not to people a
            // decision put there.
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "Public Access",
                WebRoleRegistry.AuthenticatedAttr, false,
                WebRoleRegistry.AnonymousAttr, true);

            Assert.True(WebRoleRegistry.ExcludedFromResolution(svc, "Public Access"));
        }
    }
}
