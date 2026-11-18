using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What the role resolver answers, pinned input by input before its reads were reduced,
    /// so the reduction can be shown to change none of it. Every test here passed against the
    /// resolver as it stood and passes unchanged against it now.
    ///
    /// Not pinned here, because this fake cannot say it: how Dataverse compares an email or a
    /// web role name inside a query. The platform compares text without regard to case and the
    /// fake compares it exactly, so a test of that would pin the fake and not the product. The
    /// resolver still leaves those comparisons to the platform, query for query.
    /// </summary>
    public class RoleResolutionCharacterisationTests
    {
        private const string Email = "person@ascotlloyd.co.uk";
        private const string Resource = "cases.edit";
        private static readonly Guid CallerId = Guid.Parse("abcdef00-0000-4000-8000-000000000001");

        private static FakeOrganizationService World(bool contact = true, int contactState = 0)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId, "internalemailaddress", Email);
            if (contact)
            {
                svc.Seed("contact", Guid.NewGuid(),
                    "emailaddress1", Email, "fullname", "A Person", "statecode", new OptionSetValue(contactState));
            }

            return svc;
        }

        private static void Mapped(FakeOrganizationService svc, string roleCode, int state = 0)
        {
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", Email, "al_rolecode", roleCode, "statecode", new OptionSetValue(state));
        }

        private static void MappedPicklist(FakeOrganizationService svc, int appRole)
        {
            svc.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", Email, "al_approle", new OptionSetValue(appRole), "statecode", new OptionSetValue(0));
        }

        /// <summary>The contact's web roles, as the joined fetch returns them.</summary>
        private static void WebRoles(FakeOrganizationService svc, params string[] names)
        {
            var rows = new List<Entity>();
            foreach (var name in names)
            {
                var row = new Entity("contact", Guid.NewGuid());
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", name);
                rows.Add(row);
            }

            svc.FetchResults.Enqueue(new EntityCollection(rows));
        }

        private static void RoleRow(FakeOrganizationService svc, string name, bool authenticated = false, bool anonymous = false)
        {
            svc.Seed(WebRoleRegistry.RoleEntity, Guid.NewGuid(),
                WebRoleRegistry.NameAttr, name,
                WebRoleRegistry.AuthenticatedAttr, authenticated,
                WebRoleRegistry.AnonymousAttr, anonymous);
        }

        private static void Rule(FakeOrganizationService svc, string roleCode, int level)
        {
            svc.Seed("al_pagepermission", Guid.NewGuid(),
                "al_resourcekey", Resource, "al_rolecode", roleCode,
                "al_accesslevel", new OptionSetValue(level), "statecode", new OptionSetValue(0));
        }

        private static IPluginExecutionContext Caller()
        {
            return new FakePluginExecutionContext { UserId = CallerId, InitiatingUserId = CallerId };
        }

        private static string Refusal(FakeOrganizationService svc, int level = PermissionHelpers.AccessEdit)
        {
            return Assert.Throws<InvalidPluginExecutionException>(
                () => PermissionHelpers.EnsureAppPermission(svc, Caller(), Resource, level)).Message;
        }

        [Fact]
        public void Mapped_roles_come_first_then_web_roles_and_a_role_held_both_ways_is_listed_once()
        {
            var svc = World();
            Mapped(svc, "Compliance");
            Mapped(svc, "Planner");
            WebRoles(svc, "AL Portal - Tax Reviewer", "Compliance");
            RoleRow(svc, "AL Portal - Tax Reviewer");
            RoleRow(svc, "Compliance");

            Assert.Equal(
                new[] { "Compliance", "Planner", "AL Portal - Tax Reviewer" },
                PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
        }

        [Fact]
        public void A_role_flagged_for_everyone_is_left_out_whichever_way_it_is_held()
        {
            var svc = World();
            Mapped(svc, "Everyone");
            Mapped(svc, "Planner");
            WebRoles(svc, "Open", "Everyone", "AL Portal - Tax Reviewer");
            RoleRow(svc, "Everyone", authenticated: true);
            RoleRow(svc, "Open", anonymous: true);
            RoleRow(svc, "Planner");

            Assert.Equal(
                new[] { "Planner", "AL Portal - Tax Reviewer" },
                PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
        }

        [Fact]
        public void The_two_system_roles_are_left_out_by_name_however_they_are_spelt()
        {
            var svc = World();
            Mapped(svc, " authenticated users ");
            WebRoles(svc, "ANONYMOUS USERS", "Authenticated Users");

            Assert.Empty(PermissionHelpers.ResolveRoleCodesForEmail(svc, Email));
        }

        [Fact]
        public void A_name_two_sites_share_is_left_out_when_either_row_is_flagged()
        {
            var svc = World();
            Mapped(svc, "Shared");
            Mapped(svc, "Twice");
            WebRoles(svc);
            RoleRow(svc, "Shared");
            RoleRow(svc, "Shared", authenticated: true);
            RoleRow(svc, "Twice");
            RoleRow(svc, "Twice");

            Assert.Equal(new[] { "Twice" }, PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
        }

        [Fact]
        public void A_role_code_no_web_role_carries_is_kept()
        {
            var svc = World();
            Mapped(svc, "Compliance");
            WebRoles(svc, "Not A Seeded Role");

            Assert.Equal(
                new[] { "Compliance", "Not A Seeded Role" },
                PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
        }

        [Fact]
        public void With_no_contact_only_the_mapping_table_answers_and_no_web_role_join_is_issued()
        {
            var svc = World(contact: false);
            Mapped(svc, "Compliance");
            WebRoles(svc, "Would Be Wrong");

            Assert.Equal(new[] { "Compliance" }, PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
            Assert.Single(svc.FetchResults);
        }

        [Fact]
        public void A_role_code_is_trimmed_and_a_web_role_differing_only_in_case_is_a_second_entry()
        {
            var svc = World();
            Mapped(svc, "  compliance  ");
            WebRoles(svc, "Compliance", " Padded ");

            Assert.Equal(
                new[] { "compliance", "Compliance", "Padded" },
                PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
        }

        [Fact]
        public void The_email_is_trimmed_before_it_is_looked_up()
        {
            var svc = World();
            Mapped(svc, "Compliance");
            WebRoles(svc, "AL Portal - Tax Reviewer");

            Assert.Equal(
                new[] { "Compliance", "AL Portal - Tax Reviewer" },
                PermissionHelpers.ResolveRoleCodesForEmail(svc, "  " + Email + " ").ToArray());
        }

        [Fact]
        public void A_picklist_mapping_gives_its_label_once_and_a_withdrawn_mapping_gives_nothing()
        {
            var svc = World();
            MappedPicklist(svc, 120910760);
            Mapped(svc, "Tax Checker");
            Mapped(svc, "Gone", state: 1);
            WebRoles(svc);

            Assert.Equal(new[] { "Tax Checker" }, PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
        }

        [Fact]
        public void The_resolver_itself_does_not_ask_whether_the_contact_is_active()
        {
            var svc = World(contactState: 1);
            Mapped(svc, "Compliance");
            WebRoles(svc, "AL Portal - Tax Reviewer");

            Assert.Equal(
                new[] { "Compliance", "AL Portal - Tax Reviewer" },
                PermissionHelpers.ResolveRoleCodesForEmail(svc, Email).ToArray());
        }

        [Fact]
        public void The_gate_admits_a_caller_whose_mapped_role_or_web_role_grants_the_level()
        {
            var mapped = World();
            Mapped(mapped, "Compliance");
            WebRoles(mapped);
            Rule(mapped, "Compliance", PermissionHelpers.AccessEdit);
            PermissionHelpers.EnsureAppPermission(mapped, Caller(), Resource, PermissionHelpers.AccessEdit);

            var web = World();
            Mapped(web, "Unrelated");
            WebRoles(web, "AL Portal - Tax Reviewer");
            Rule(web, "AL Portal - Tax Reviewer", PermissionHelpers.AccessManage);
            PermissionHelpers.EnsureAppPermission(web, Caller(), Resource, PermissionHelpers.AccessEdit);
        }

        [Fact]
        public void The_gate_admits_a_caller_with_no_contact_on_their_mapping_alone()
        {
            var svc = World(contact: false);
            Mapped(svc, "Compliance");
            Rule(svc, "Compliance", PermissionHelpers.AccessEdit);

            PermissionHelpers.EnsureAppPermission(svc, Caller(), Resource, PermissionHelpers.AccessEdit);
        }

        [Fact]
        public void The_gate_refuses_a_deactivated_contact_before_it_reads_any_role()
        {
            var svc = World(contactState: 1);
            Mapped(svc, "Compliance");
            WebRoles(svc, "AL Portal - Tax Reviewer");
            Rule(svc, "Compliance", PermissionHelpers.AccessManage);

            Assert.Equal(
                "UNAUTHORIZED: Your application account is deactivated. Ask an administrator to reactivate it.",
                Refusal(svc));
            Assert.Single(svc.FetchResults);
        }

        [Fact]
        public void The_gate_refuses_a_caller_whose_only_role_is_flagged_for_everyone()
        {
            var svc = World();
            Mapped(svc, "Everyone");
            WebRoles(svc, "Everyone");
            RoleRow(svc, "Everyone", authenticated: true);
            Rule(svc, "Everyone", PermissionHelpers.AccessManage);

            Assert.Equal(
                "UNAUTHORIZED: You have no application role assigned. Ask an administrator to assign one in Security configuration.",
                Refusal(svc));
        }

        [Fact]
        public void The_gate_refuses_a_level_the_callers_roles_do_not_reach()
        {
            var svc = World();
            Mapped(svc, "Compliance");
            WebRoles(svc);
            Rule(svc, "Compliance", PermissionHelpers.AccessView);

            Assert.Equal(
                "UNAUTHORIZED: Your role does not grant the required access for this action (cases.edit).",
                Refusal(svc));
        }

        [Fact]
        public void The_gate_refuses_a_caller_with_no_work_email()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("systemuser", CallerId);
            Mapped(svc, "Compliance");

            Assert.Equal(
                "UNAUTHORIZED: Your account has no work email, which is required to resolve your role.",
                Refusal(svc));
        }

        [Fact]
        public void The_gate_lets_anyone_through_while_no_mapping_has_ever_been_made()
        {
            PermissionHelpers.EnsureAppPermission(
                new FakeOrganizationService(), Caller(), Resource, PermissionHelpers.AccessManage);
        }
    }
}
