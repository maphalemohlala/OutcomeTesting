using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// How many reads the gate spends on a caller who holds one role both ways, by mapping
    /// and by web role, which is how al_AssignUserRole leaves a person. It was eight: the
    /// contact was looked up twice and the role's web role row twice.
    /// </summary>
    public class RoleResolutionReadsTests
    {
        private const string Email = "person@ascotlloyd.co.uk";
        private static readonly Guid CallerId = Guid.Parse("abcdef00-0000-4000-8000-000000000002");

        [Fact]
        public void The_gate_reads_the_contact_once_and_each_role_name_once()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", Email);
            svc.Seed("contact", Guid.NewGuid(), "emailaddress1", Email, "statecode", new OptionSetValue(0));
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", Email, "al_rolecode", "Compliance", "statecode", new OptionSetValue(0));
            svc.Seed(WebRoleRegistry.RoleEntity, Guid.NewGuid(), WebRoleRegistry.NameAttr, "Compliance");
            svc.Seed("al_pagepermission", Guid.NewGuid(),
                "al_resourcekey", "cases.edit", "al_rolecode", "Compliance",
                "al_accesslevel", new OptionSetValue(PermissionHelpers.AccessEdit), "statecode", new OptionSetValue(0));

            var held = new Entity("contact", Guid.NewGuid());
            held["role.name"] = new AliasedValue("powerpagecomponent", "name", "Compliance");
            svc.FetchResults.Enqueue(new EntityCollection(new[] { held }));

            PermissionHelpers.EnsureAppPermission(
                svc,
                new FakePluginExecutionContext { UserId = CallerId, InitiatingUserId = CallerId },
                "cases.edit",
                PermissionHelpers.AccessEdit);

            // Any mapping, the contact, the mappings, the web role row, the contact's web
            // roles, the permission rules.
            Assert.Equal(6, svc.RetrieveMultipleCount);
        }
    }
}
