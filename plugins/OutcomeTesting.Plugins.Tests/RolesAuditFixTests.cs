using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The role and permission defects found by the 2026-11-16 audit, one regression each.
    ///
    /// Most go through the plug-in's own Execute, because the defects sat in the commands'
    /// wiring rather than in a helper: which row was written, which was read, whether a
    /// check ran at all.
    /// </summary>
    public class RolesAuditFixTests
    {
        private const string AdminEmail = "admin@ascotlloyd.co.uk";
        private const string OtherEmail = "other@ascotlloyd.co.uk";
        private const string AdminRole = "AL Portal - Portal Administrator";
        private const int Adviser = 120910762;

        private static readonly Guid CallerId = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000001");
        private static readonly Guid AdminContact = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000002");
        private static readonly Guid AdminMapping = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000003");
        private static readonly Guid AdminRule = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000004");
        private static readonly Guid AdminRoleId = Guid.Parse("f1f1f1f1-0000-4000-8000-000000000005");

        // ---- worlds -------------------------------------------------------------------

        /// <summary>
        /// A caller who passes the gate for permission.manage at Manage, and is its only
        /// holder: an active contact, an active mapping to the administrator web role, and
        /// the one rule granting it.
        /// </summary>
        private static FakeOrganizationService OneAdministrator()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", AdminEmail);
            Contact(svc, AdminContact, AdminEmail, 0);
            svc.Seed("al_userrolemapping", AdminMapping,
                "al_useremail", AdminEmail, "al_rolecode", AdminRole, "statecode", new OptionSetValue(0));
            svc.Seed(WebRoleRegistry.RoleEntity, AdminRoleId,
                WebRoleRegistry.NameAttr, AdminRole, WebRoleRegistry.AuthenticatedAttr, false,
                "statecode", new OptionSetValue(0));
            Rule(svc, AdminRule, AdminRole, "permission.manage", PermissionHelpers.AccessManage);
            Joins(svc, 6);
            return svc;
        }

        private static void Contact(FakeOrganizationService svc, Guid id, string email, int state)
        {
            svc.Seed("contact", id, "emailaddress1", email, "fullname", email, "statecode", new OptionSetValue(state));
        }

        private static void Rule(FakeOrganizationService svc, Guid id, string roleCode, string resource, int level, int? appRole = null)
        {
            var row = svc.Seed("al_pagepermission", id,
                "al_resourcekey", resource,
                "al_accesslevel", new OptionSetValue(level),
                "statecode", new OptionSetValue(0));
            if (roleCode != null)
            {
                row["al_rolecode"] = roleCode;
            }

            if (appRole.HasValue)
            {
                row["al_approle"] = new OptionSetValue(appRole.Value);
            }
        }

        /// <summary>Empty answers for the joined contact-to-web-role fetches the fake cannot run.</summary>
        private static void Joins(FakeOrganizationService svc, int count)
        {
            for (var i = 0; i < count; i++)
            {
                svc.FetchResults.Enqueue(new EntityCollection());
            }
        }

        private static FakeServiceProvider Provider(FakeOrganizationService svc, params object[] inputs)
        {
            var provider = new FakeServiceProvider(svc);
            provider.Context.UserId = CallerId;
            provider.Context.InitiatingUserId = CallerId;
            for (var i = 0; i + 1 < inputs.Length; i += 2)
            {
                provider.Context.InputParameters[(string)inputs[i]] = inputs[i + 1];
            }

            return provider;
        }

        private static string Refusal(Action act)
        {
            return Assert.Throws<InvalidPluginExecutionException>(act).Message;
        }

        private static IPluginExecutionContext Caller()
        {
            return new FakePluginExecutionContext { UserId = CallerId, InitiatingUserId = CallerId };
        }

        private const string LockoutRefusal =
            "PRECONDITION: That would leave nobody able to manage security configuration. Give that access to someone else first.";

        // ---- 1. the legacy Adviser picklist default --------------------------------------

        /// <summary>
        /// The live shape: every rule carries a role code AND al_approle's schema default,
        /// Adviser. A picklist-only Adviser mapping matched every one of them, permission.manage
        /// included. The app reads the same rule as the role code alone
        /// (loadPermissions.fix.test.ts holds the same case on that side).
        /// </summary>
        [Fact]
        public void A_picklist_Adviser_does_not_match_a_rule_that_names_a_role_code()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", OtherEmail);
            Contact(svc, Guid.NewGuid(), OtherEmail, 0);
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", OtherEmail, "al_approle", new OptionSetValue(Adviser), "statecode", new OptionSetValue(0));
            Rule(svc, Guid.NewGuid(), "Administrators", "permission.manage", PermissionHelpers.AccessManage, Adviser);
            Joins(svc, 2);

            Assert.Equal(
                "UNAUTHORIZED: Your role does not grant the required access for this action (permission.manage).",
                Refusal(() => PermissionHelpers.EnsureAppPermission(svc, Caller(), "permission.manage", PermissionHelpers.AccessManage)));
        }

        [Fact]
        public void A_picklist_Adviser_still_matches_a_rule_that_names_only_the_picklist()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", OtherEmail);
            Contact(svc, Guid.NewGuid(), OtherEmail, 0);
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", OtherEmail, "al_approle", new OptionSetValue(Adviser), "statecode", new OptionSetValue(0));
            Rule(svc, Guid.NewGuid(), null, "page.dashboard", PermissionHelpers.AccessView, Adviser);
            Joins(svc, 2);

            PermissionHelpers.EnsureAppPermission(svc, Caller(), "page.dashboard", PermissionHelpers.AccessView);
        }

        [Fact]
        public void A_rule_is_matched_on_its_role_code_whatever_its_case()
        {
            // Dataverse compared the codes without regard to case inside the query; the match
            // now runs in memory and has to keep doing so.
            var svc = OneAdministrator();
            svc.Row("al_pagepermission", AdminRule)["al_rolecode"] = AdminRole.ToUpperInvariant();

            PermissionHelpers.EnsureAppPermission(svc, Caller(), "permission.manage", PermissionHelpers.AccessManage);
        }

        [Fact]
        public void SetPagePermission_clears_the_picklist_when_it_writes_a_role_code()
        {
            var svc = new FakeOrganizationService(); // no mapping yet: the bootstrap admits the caller
            svc.Seed("systemuser", CallerId, "internalemailaddress", AdminEmail);
            svc.Seed(WebRoleRegistry.RoleEntity, AdminRoleId, WebRoleRegistry.NameAttr, AdminRole);

            new SetPagePermissionPlugin(null, null).Execute(Provider(svc,
                "RoleCode", AdminRole, "ResourceKey", "page.cases", "AccessLevel", "View", "IdempotencyKey", "k1"));

            var written = Assert.Single(svc.Creates, c => c.LogicalName == "al_pagepermission");
            Assert.True(written.Contains("al_approle"));
            Assert.Null(written["al_approle"]);
        }

        // ---- 2. duplicate contacts for one email -----------------------------------------

        [Fact]
        public void A_deactivated_duplicate_does_not_decide_for_a_person_with_an_active_row()
        {
            var svc = new FakeOrganizationService();
            Contact(svc, Guid.NewGuid(), OtherEmail, 1); // seeded first, so an unordered pick took it
            var active = Guid.NewGuid();
            Contact(svc, active, OtherEmail, 0);

            Assert.True(PermissionHelpers.IsRegisteredActive(svc, OtherEmail));
            Assert.Equal(active, WebRoleRegistry.FindContactByEmail(svc, OtherEmail).Id);
        }

        [Fact]
        public void Deactivating_a_person_deactivates_every_contact_holding_their_email()
        {
            var svc = OneAdministrator();
            var first = Guid.NewGuid();
            var duplicate = Guid.NewGuid();
            Contact(svc, first, OtherEmail, 0);
            Contact(svc, duplicate, OtherEmail, 0);

            new SetUserActivePlugin(null, null).Execute(Provider(svc,
                "UserId", first.ToString("D"), "Active", false, "IdempotencyKey", "k2"));

            Assert.False(ContactRegistry.IsActive(svc.Row("contact", first)));
            Assert.False(ContactRegistry.IsActive(svc.Row("contact", duplicate)));
            Assert.False(PermissionHelpers.IsRegisteredActive(svc, OtherEmail));
        }

        [Fact]
        public void Registering_an_email_already_registered_is_refused_and_renames_nobody()
        {
            var svc = OneAdministrator();
            var existing = Guid.NewGuid();
            Contact(svc, existing, OtherEmail, 0);

            var message = Refusal(() => new CreateUserPlugin(null, null).Execute(Provider(svc,
                "FullName", "Somebody Else", "WorkEmail", "  " + OtherEmail + " ", "IdempotencyKey", "k3")));

            Assert.Equal(
                "PRECONDITION: Someone is already registered with this work email. Edit their details instead of adding them again.",
                message);
            Assert.Equal(OtherEmail, svc.Row("contact", existing).GetAttributeValue<string>("fullname"));
            Assert.DoesNotContain(svc.Creates, c => c.LogicalName == "contact");
        }

        // ---- 3. a retired role coming back -----------------------------------------------

        [Fact]
        public void A_retired_web_role_held_by_a_contact_is_not_one_of_their_roles()
        {
            var svc = new FakeOrganizationService();
            var retired = new Entity("contact", Guid.NewGuid());
            retired["role.name"] = new AliasedValue("powerpagecomponent", "name", "Old Role");
            retired["role.statecode"] = new AliasedValue("powerpagecomponent", "statecode", new OptionSetValue(1));
            var live = new Entity("contact", Guid.NewGuid());
            live["role.name"] = new AliasedValue("powerpagecomponent", "name", "Live Role");
            live["role.statecode"] = new AliasedValue("powerpagecomponent", "statecode", new OptionSetValue(0));
            svc.FetchResults.Enqueue(new EntityCollection(new[] { retired, live }));

            Assert.Equal(new[] { "Live Role" }, WebRoleRegistry.RolesForContact(svc, Guid.NewGuid()).ToArray());
            Assert.Contains("<attribute name='statecode'/>", svc.FetchXml.Last());
        }

        [Fact]
        public void The_role_lookup_asks_for_the_state_IsActive_reads()
        {
            // Without it the platform returns no statecode and every retired role reads as
            // active; the fake hands back the whole row, which is why no test saw it.
            var svc = new FakeOrganizationService();
            WebRoleRegistry.FindByName(svc, AdminRole);
            Assert.Contains("<attribute name='statecode'/>", svc.FetchXml.Single());
        }

        [Fact]
        public void A_rule_for_a_retired_role_cannot_be_restored()
        {
            var svc = OneAdministrator();
            svc.Seed(WebRoleRegistry.RoleEntity, Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "Old Role", "statecode", new OptionSetValue(1));
            var rule = Guid.NewGuid();
            Rule(svc, rule, "Old Role", "page.cases", PermissionHelpers.AccessEdit);
            svc.Row("al_pagepermission", rule)["statecode"] = new OptionSetValue(1);

            Assert.Equal(
                "PRECONDITION: Old Role is retired, so its permission rule cannot be restored.",
                Refusal(() => new SetPermissionRuleActivePlugin(null, null).Execute(Provider(svc,
                    "PermissionId", rule.ToString("D"), "Active", true, "IdempotencyKey", "k4"))));
            Assert.Equal(1, svc.Row("al_pagepermission", rule).GetAttributeValue<OptionSetValue>("statecode").Value);
        }

        [Fact]
        public void An_assignment_to_a_retired_role_cannot_be_restored()
        {
            var svc = OneAdministrator();
            svc.Seed(WebRoleRegistry.RoleEntity, Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "Old Role", "statecode", new OptionSetValue(1));
            var mapping = Guid.NewGuid();
            svc.Seed("al_userrolemapping", mapping,
                "al_useremail", OtherEmail, "al_rolecode", "Old Role", "statecode", new OptionSetValue(1));

            Assert.Equal(
                "PRECONDITION: Old Role is retired, so this assignment cannot be restored.",
                Refusal(() => new SetRoleAssignmentActivePlugin(null, null).Execute(Provider(svc,
                    "MappingId", mapping.ToString("D"), "Active", true, "IdempotencyKey", "k5"))));
            Assert.Empty(svc.Associations);
        }

        [Fact]
        public void Creating_a_role_under_a_retired_name_is_refused()
        {
            var svc = OneAdministrator();
            svc.Seed(WebRoleRegistry.RoleEntity, Guid.NewGuid(),
                WebRoleRegistry.NameAttr, "Old Role", "statecode", new OptionSetValue(1));

            Assert.Equal(
                "PRECONDITION: A retired role already has this name. Restore it instead of creating it again.",
                Refusal(() => new CreateRolePlugin(null, null).Execute(Provider(svc,
                    "RoleName", "Old Role", "IdempotencyKey", "k6"))));
        }

        // ---- 4. locking every administrator out ------------------------------------------

        [Fact]
        public void An_administrator_cannot_deactivate_themselves()
        {
            var svc = OneAdministrator();

            Assert.Equal(
                "PRECONDITION: You cannot deactivate your own account. Ask another administrator to do it.",
                Refusal(() => new SetUserActivePlugin(null, null).Execute(Provider(svc,
                    "UserId", AdminContact.ToString("D"), "Active", false, "IdempotencyKey", "k7"))));
            Assert.True(ContactRegistry.IsActive(svc.Row("contact", AdminContact)));
        }

        [Fact]
        public void The_last_administrator_cannot_withdraw_their_own_assignment()
        {
            var svc = OneAdministrator();

            Assert.Equal(LockoutRefusal, Refusal(() => new SetRoleAssignmentActivePlugin(null, null).Execute(Provider(svc,
                "MappingId", AdminMapping.ToString("D"), "Active", false, "IdempotencyKey", "k8"))));
        }

        [Fact]
        public void An_administrator_can_withdraw_their_own_assignment_while_another_remains()
        {
            var svc = OneAdministrator();
            Contact(svc, Guid.NewGuid(), OtherEmail, 0);
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", OtherEmail, "al_rolecode", AdminRole, "statecode", new OptionSetValue(0));

            new SetRoleAssignmentActivePlugin(null, null).Execute(Provider(svc,
                "MappingId", AdminMapping.ToString("D"), "Active", false, "IdempotencyKey", "k9"));

            Assert.False(CommandHelpers.IsActive(svc.Row("al_userrolemapping", AdminMapping)));
        }

        [Fact]
        public void A_deactivated_holder_does_not_count_as_someone_who_can_still_manage()
        {
            var svc = OneAdministrator();
            Contact(svc, Guid.NewGuid(), OtherEmail, 1);
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", OtherEmail, "al_rolecode", AdminRole, "statecode", new OptionSetValue(0));

            Assert.Equal(LockoutRefusal, Refusal(() => new SetRoleAssignmentActivePlugin(null, null).Execute(Provider(svc,
                "MappingId", AdminMapping.ToString("D"), "Active", false, "IdempotencyKey", "k10"))));
        }

        [Fact]
        public void The_last_permission_manage_rule_cannot_be_withdrawn()
        {
            var svc = OneAdministrator();

            Assert.Equal(LockoutRefusal, Refusal(() => new SetPermissionRuleActivePlugin(null, null).Execute(Provider(svc,
                "PermissionId", AdminRule.ToString("D"), "Active", false, "IdempotencyKey", "k11"))));
        }

        [Fact]
        public void The_last_permission_manage_rule_cannot_be_lowered()
        {
            var svc = OneAdministrator();
            svc.Row("al_pagepermission", AdminRule)["al_pagepermissioncode"] = "PP-" + AdminRole + "-permission.manage";

            Assert.Equal(LockoutRefusal, Refusal(() => new SetPagePermissionPlugin(null, null).Execute(Provider(svc,
                "RoleCode", AdminRole, "ResourceKey", "permission.manage", "AccessLevel", "Edit", "IdempotencyKey", "k12"))));
        }

        [Fact]
        public void The_role_every_administrator_holds_cannot_be_retired()
        {
            var svc = OneAdministrator();

            Assert.Equal(LockoutRefusal, Refusal(() => new UpdateRolePlugin(null, null).Execute(Provider(svc,
                "RoleId", AdminRoleId.ToString("D"), "Active", false, "IdempotencyKey", "k13"))));
        }

        [Fact]
        public void Revoking_the_last_holder_is_refused()
        {
            var svc = OneAdministrator();

            Assert.Equal(LockoutRefusal, Refusal(() => new AdoptRoleAssignmentPlugin(null, null).Execute(Provider(svc,
                "UserEmail", AdminEmail, "RoleCode", AdminRole, "Decision", "Revoke", "IdempotencyKey", "k14"))));
        }

        // ---- 6. a deactivated person's own roles -----------------------------------------

        [Fact]
        public void GetMyRoles_answers_nothing_for_a_deactivated_person()
        {
            var svc = OneAdministrator();
            svc.Row("contact", AdminContact)["statecode"] = new OptionSetValue(1);
            var provider = Provider(svc);

            new GetMyRolesPlugin(null, null).Execute(provider);

            Assert.Equal("[]", provider.Context.OutputParameters["RoleCodes"]);
        }

        [Fact]
        public void GetMyRoles_still_answers_for_an_active_person()
        {
            var svc = OneAdministrator();
            var provider = Provider(svc);

            new GetMyRolesPlugin(null, null).Execute(provider);

            Assert.Equal("[\"" + AdminRole + "\"]", provider.Context.OutputParameters["RoleCodes"]);
        }

        // ---- 7. withdrawing one of two rows for the same grant ---------------------------

        [Fact]
        public void Withdrawing_one_of_two_active_rows_keeps_the_web_role()
        {
            var svc = OneAdministrator();
            var otherContact = Guid.NewGuid();
            Contact(svc, otherContact, OtherEmail, 0);
            var planner = Guid.NewGuid();
            svc.Seed(WebRoleRegistry.RoleEntity, planner, WebRoleRegistry.NameAttr, "AL Portal - Planner");
            svc.SeedWebRoleAssociation(otherContact, planner);
            var first = Guid.NewGuid();
            svc.Seed("al_userrolemapping", first,
                "al_useremail", OtherEmail, "al_rolecode", "AL Portal - Planner", "statecode", new OptionSetValue(0));
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", OtherEmail, "al_rolecode", "AL Portal - Planner", "statecode", new OptionSetValue(0));

            new SetRoleAssignmentActivePlugin(null, null).Execute(Provider(svc,
                "MappingId", first.ToString("D"), "Active", false, "IdempotencyKey", "k15"));

            Assert.False(CommandHelpers.IsActive(svc.Row("al_userrolemapping", first)));
            Assert.Equal(0, svc.DisassociateAttempts);
        }

        [Fact]
        public void Withdrawing_the_only_row_still_removes_the_web_role()
        {
            var svc = OneAdministrator();
            var otherContact = Guid.NewGuid();
            Contact(svc, otherContact, OtherEmail, 0);
            var planner = Guid.NewGuid();
            svc.Seed(WebRoleRegistry.RoleEntity, planner, WebRoleRegistry.NameAttr, "AL Portal - Planner");
            svc.SeedWebRoleAssociation(otherContact, planner);
            var only = Guid.NewGuid();
            svc.Seed("al_userrolemapping", only,
                "al_useremail", OtherEmail, "al_rolecode", "AL Portal - Planner", "statecode", new OptionSetValue(0));

            new SetRoleAssignmentActivePlugin(null, null).Execute(Provider(svc,
                "MappingId", only.ToString("D"), "Active", false, "IdempotencyKey", "k16"));

            Assert.Single(svc.Disassociations);
        }

        // ---- 8. an adviser as their own T&C Manager --------------------------------------

        [Fact]
        public void An_adviser_cannot_be_mapped_as_their_own_TC_Manager()
        {
            var svc = new FakeOrganizationService();
            var self = Guid.NewGuid();
            Contact(svc, self, "ann@example.com", 0);
            var mapping = Guid.NewGuid();
            svc.Seed("al_advisermapping", mapping,
                "al_adviseremail", " ANN@example.com ",
                "al_tcmanagerid", new EntityReference("contact", self),
                "statecode", new OptionSetValue(0));
            var context = new FakePluginExecutionContext { MessageName = "Create", PrimaryEntityId = mapping };
            context.InputParameters["Target"] = new Entity("al_advisermapping", mapping);

            Assert.Equal(
                "PRECONDITION: An adviser cannot be their own T&C Manager. Choose someone else to sign off their cases.",
                Refusal(() => AdviserMappingPlugin.Apply(context, svc)));
        }

        [Fact]
        public void A_withdrawn_self_mapping_can_still_be_saved()
        {
            var svc = new FakeOrganizationService();
            var self = Guid.NewGuid();
            Contact(svc, self, "ann@example.com", 0);
            var mapping = Guid.NewGuid();
            svc.Seed("al_advisermapping", mapping,
                "al_adviseremail", "ann@example.com",
                "al_tcmanagerid", new EntityReference("contact", self),
                "statecode", new OptionSetValue(1));
            var context = new FakePluginExecutionContext { MessageName = "Update", PrimaryEntityId = mapping };
            context.InputParameters["Target"] = new Entity("al_advisermapping", mapping) { ["statecode"] = new OptionSetValue(1) };

            AdviserMappingPlugin.RefuseSelfManagement(context, svc);
        }
    }
}
