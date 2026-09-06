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
    }
}
