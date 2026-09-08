using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// OD-010/OD-037. Deactivation is the sanctioned alternative to deleting a leaver, so it
    /// has to be what actually withdraws their access.
    ///
    /// It did not. `SetUserActivePlugin` has written the **contact's** statecode since AD-085,
    /// while `PermissionHelpers` still asked `al_user` — a table migrated to zero rows the same
    /// day. And the "no row means permitted" rule, which exists so a Dataverse user the registry
    /// has not caught up with is not locked out, turned that empty table into a blanket yes: a
    /// deactivated person kept full command access and nothing anywhere said so.
    ///
    /// That is the shape worth pinning. A registry read that is permissive on absence cannot be
    /// pointed at a table nobody writes any more, because the failure is silent in the direction
    /// that grants access.
    /// </summary>
    public class RegisteredActiveTests
    {
        private const string Email = "leaver@ascotlloyd.co.uk";
        private static readonly Guid ContactId = Guid.Parse("77777777-cccc-4ccc-8ccc-777777777777");

        private static FakeOrganizationService WithContact(int? stateCode)
        {
            var svc = new FakeOrganizationService();
            if (stateCode.HasValue)
            {
                svc.Seed(
                    ContactRegistry.Entity,
                    ContactId,
                    ContactRegistry.EmailAttr, Email,
                    ContactRegistry.StateCodeAttr, new OptionSetValue(stateCode.Value));
            }

            return svc;
        }

        [Fact]
        public void ADeactivatedContactIsNotRegisteredActive()
        {
            var svc = WithContact(ContactRegistry.StateInactive);
            Assert.False(PermissionHelpers.IsRegisteredActive(svc, Email));
        }

        [Fact]
        public void AnActiveContactIsRegisteredActive()
        {
            var svc = WithContact(ContactRegistry.StateActive);
            Assert.True(PermissionHelpers.IsRegisteredActive(svc, Email));
        }

        /// <summary>
        /// The permissive rule, kept deliberately: a Dataverse user can legitimately predate
        /// their contact row, and refusing here would lock out anyone the registry has not
        /// caught up with. Only an explicit deactivation withdraws access.
        /// </summary>
        [Fact]
        public void NoContactRowAtAllStaysPermitted()
        {
            var svc = new FakeOrganizationService();
            Assert.True(PermissionHelpers.IsRegisteredActive(svc, Email));
        }

        [Fact]
        public void AContactCarryingNoStateAtAllStaysPermitted()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(ContactRegistry.Entity, ContactId, ContactRegistry.EmailAttr, Email);
            Assert.True(PermissionHelpers.IsRegisteredActive(svc, Email));
        }

        /// <summary>
        /// The regression this file exists for. Rows in the retired registry must not decide
        /// anything: a person deactivated in `al_user` but active as a contact is active, and
        /// the reverse — the case that was actually broken — is refused.
        /// </summary>
        [Fact]
        public void TheRetiredAlUserRegistryDoesNotDecide()
        {
            var svc = WithContact(ContactRegistry.StateInactive);
            svc.Seed(
                "al_user",
                Guid.Parse("88888888-aaaa-4aaa-8aaa-888888888888"),
                "al_workemail", Email,
                "al_isactive", true);

            Assert.False(PermissionHelpers.IsRegisteredActive(svc, Email));
        }
    }

    /// <summary>
    /// AD-087/OD-037. The registry is the Power Pages web roles, and a role code is checked
    /// against them alone.
    ///
    /// Two things are pinned here, and the second is the one that will look wrong later.
    /// `al_SetPagePermission` used to validate against `al_role` **only**, and no `al_role` row
    /// carries an `AL Portal - *` code, so configuring a permission for any role the app offers
    /// was refused — hidden because the 52 web role rules in DEV were written directly by
    /// `seedwebroles` rather than through the command. Seeding a result is how a broken path
    /// stops being walked.
    ///
    /// The legacy `al_role` fallback that briefly replaced it is **deliberately gone**. It could
    /// only ever have matched a code nothing carries: every `al_rolecode` in `al_pagepermission`
    /// and `al_userrolemapping` is a web role name, verified against DEV. Dropping it is what
    /// lets the table be deleted, because a read against a table that has gone faults and
    /// Dataverse cannot see a plug-in's RetrieveMultiple as a dependency.
    /// </summary>
    public class RoleCodeExistsTests
    {
        private const string WebRoleName = "AL Portal - Tax Reviewer";
        private const string LegacyCode = "ROLE-ADMINISTRATOR";

        private static FakeOrganizationService Seeded()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                WebRoleRegistry.RoleEntity,
                Guid.Parse("11111111-bbbb-4bbb-8bbb-111111111111"),
                WebRoleRegistry.NameAttr, WebRoleName,
                WebRoleRegistry.AuthenticatedAttr, false);
            return svc;
        }

        [Fact]
        public void AcceptsAWebRoleName()
        {
            Assert.True(AssignUserRolePlugin.RoleCodeExists(Seeded(), WebRoleName));
        }

        [Fact]
        public void RefusesACodeThatIsNotAWebRole()
        {
            Assert.False(AssignUserRolePlugin.RoleCodeExists(Seeded(), "ROLE-NOT-A-THING"));
        }

        /// <summary>
        /// The retired vocabulary is not consulted, and this must keep failing to match even
        /// with a row present — otherwise the check is still reachable and `al_role` cannot go.
        /// </summary>
        [Fact]
        public void DoesNotConsultTheRetiredAlRoleTable()
        {
            var svc = Seeded();
            svc.Seed(
                "al_role",
                Guid.Parse("22222222-bbbb-4bbb-8bbb-222222222222"),
                "al_rolecode", LegacyCode,
                "statecode", new OptionSetValue(0));

            Assert.False(AssignUserRolePlugin.RoleCodeExists(svc, LegacyCode));
        }
    }
}
