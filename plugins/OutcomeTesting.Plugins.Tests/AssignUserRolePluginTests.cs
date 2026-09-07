using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// al_AssignUserRole (AD-003, AD-090). Full plug-in execution needs a
    /// IPluginExecutionContext this codebase has no fake for, so — matching how
    /// AdoptRoleAssignmentPlugin.Apply and GetRoleHoldersPlugin.Read are tested — the guard
    /// is extracted into its own static method and exercised directly.
    /// </summary>
    public class AssignUserRolePluginTests
    {
        private static readonly Guid RoleId = Guid.Parse("88888888-bbbb-4bbb-8bbb-888888888888");

        [Fact]
        public void RefusesARoleAutoGrantedToEverySignedInUser()
        {
            // AD-090: writing a mapping row for a role carrying mspp_authenticatedusersrole
            // would look successful while PermissionHelpers.GetMappedRoles silently never
            // resolves it (Task 1's filter there). Refusing here at write time gives a clear
            // reason instead. OD-033 found Administrators carrying this flag in DEV.
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, "Administrators",
                WebRoleRegistry.AuthenticatedAttr, true);

            var error = Assert.Throws<Microsoft.Xrm.Sdk.InvalidPluginExecutionException>(
                () => AssignUserRolePlugin.RefuseIfAutoGranted(svc, "Administrators"));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
        }

        [Fact]
        public void AllowsAnOrdinaryBusinessRole()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                RoleId,
                WebRoleRegistry.NameAttr, "AL Portal - Tax Reviewer",
                WebRoleRegistry.AuthenticatedAttr, false);

            // Does not throw.
            AssignUserRolePlugin.RefuseIfAutoGranted(svc, "AL Portal - Tax Reviewer");
        }

        [Fact]
        public void AllowsARoleCodeThatIsNotAWebRoleAtAll()
        {
            // An AD-044 al_role code never appears in mspp_webrole, so ExcludedFromResolution
            // resolves it as "cannot be looked up" and does not exclude it — the guard must
            // not refuse a custom role just because it found no web role by that name.
            var svc = new FakeOrganizationService();

            AssignUserRolePlugin.RefuseIfAutoGranted(svc, "CUSTOM-ROLE-CODE");
        }
    }
}
