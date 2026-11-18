using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Only someone holding the Adviser or Planner web role can be named as carrying a fail
    /// (project owner, 2026-11-17: what makes a contact an adviser or paraplanner is the roles
    /// assigned to them). Both entry points, al_SetFailAccountability and the portal's
    /// accountability request, go through WebRoleRegistry.AccountableRefusal.
    /// </summary>
    public class AccountableRoleFixTests
    {
        private static readonly Guid Named = Guid.Parse("bbbbbbbb-3333-4333-8333-000000000001");
        private static readonly Guid CaseId = Guid.Parse("bbbbbbbb-3333-4333-8333-000000000002");
        private static readonly Guid Checker = Guid.Parse("bbbbbbbb-3333-4333-8333-000000000003");
        private static readonly Guid ReviewId = Guid.Parse("bbbbbbbb-3333-4333-8333-000000000004");
        private static readonly Guid OutcomeId = Guid.Parse("bbbbbbbb-3333-4333-8333-000000000005");

        /// <summary>The joined contact-to-web-role fetch RolesForContact issues, answered.</summary>
        private static void Holds(FakeOrganizationService service, params string[] roles)
        {
            var rows = new List<Entity>();
            foreach (var role in roles)
            {
                var row = new Entity("contact", Named);
                row["role.name"] = new AliasedValue("powerpagecomponent", "name", role);
                row["role.statecode"] = new AliasedValue("powerpagecomponent", "statecode", new OptionSetValue(0));
                rows.Add(row);
            }

            service.FetchResults.Enqueue(new EntityCollection(rows));
        }

        private static FakeOrganizationService WithNamedPerson(int state = 0)
        {
            var service = new FakeOrganizationService();
            service.Seed("contact", Named,
                "fullname", "Jane Smith", "emailaddress1", "jane.smith@example.com",
                "statecode", new OptionSetValue(state));
            return service;
        }

        // ---- the shared rule ---------------------------------------------------------------

        [Theory]
        [InlineData(WebRoleRegistry.AdviserRole)]
        [InlineData(WebRoleRegistry.PlannerRole)]
        public void A_holder_of_either_role_can_be_named(string role)
        {
            var service = WithNamedPerson();
            Holds(service, WebRoleRegistry.TaxReviewerRole, role);

            Assert.Null(WebRoleRegistry.AccountableRefusal(service, Named));
        }

        [Fact]
        public void The_role_names_are_the_portal_web_roles()
        {
            Assert.Equal("AL Portal - Adviser Remediation", WebRoleRegistry.AdviserRole);
            Assert.Equal("AL Portal - Planner", WebRoleRegistry.PlannerRole);
        }

        [Fact]
        public void A_contact_holding_neither_role_is_refused_by_name_and_email()
        {
            var service = WithNamedPerson();
            Holds(service, WebRoleRegistry.AqsReviewerRole);

            Assert.Equal(
                "Jane Smith (jane.smith@example.com) can't be named as accountable because they don't hold "
                + "the Adviser or Planner role. An administrator can assign it on the People page.",
                WebRoleRegistry.AccountableRefusal(service, Named));
        }

        [Fact]
        public void A_contact_with_no_roles_at_all_is_refused()
        {
            var service = WithNamedPerson();
            Holds(service);

            Assert.Contains("don't hold the Adviser or Planner role", WebRoleRegistry.AccountableRefusal(service, Named));
        }

        [Fact]
        public void A_deactivated_contact_who_still_holds_the_role_can_be_named()
        {
            // They may have left since the check; the fail is still theirs.
            var service = WithNamedPerson(state: 1);
            Holds(service, WebRoleRegistry.AdviserRole);

            Assert.Null(WebRoleRegistry.AccountableRefusal(service, Named));
        }

        [Fact]
        public void A_retired_role_does_not_count()
        {
            var service = WithNamedPerson();
            var row = new Entity("contact", Named);
            row["role.name"] = new AliasedValue("powerpagecomponent", "name", WebRoleRegistry.PlannerRole);
            row["role.statecode"] = new AliasedValue("powerpagecomponent", "statecode", new OptionSetValue(1));
            service.FetchResults.Enqueue(new EntityCollection(new List<Entity> { row }));

            Assert.NotNull(WebRoleRegistry.AccountableRefusal(service, Named));
        }

        [Fact]
        public void A_contact_that_does_not_exist_is_refused_without_a_fault()
        {
            var service = new FakeOrganizationService();
            Holds(service);

            Assert.Contains("could not be found", WebRoleRegistry.AccountableRefusal(service, Named));
        }

        // ---- the command ---------------------------------------------------------------

        private static Exception RecordThroughCommand(FakeOrganizationService service, out FakeServiceProvider provider)
        {
            return RecordThroughCommand(service, out provider, false);
        }

        private static Exception RecordThroughCommand(FakeOrganizationService service, out FakeServiceProvider provider, bool alreadyNamed)
        {
            provider = new FakeServiceProvider(service);
            var email = "manager@example.com";
            service.Seed("systemuser", provider.Context.InitiatingUserId, "internalemailaddress", email);
            service.Seed("systemuser", provider.Context.UserId, "internalemailaddress", email);
            service.Seed("al_userrolemapping", Guid.NewGuid(),
                "al_useremail", email, "al_rolecode", "Case Editor", "statecode", new OptionSetValue(0));
            service.Seed("al_pagepermission", Guid.NewGuid(),
                "al_resourcekey", "page.cases", "al_rolecode", "Case Editor",
                "al_accesslevel", new OptionSetValue(PermissionHelpers.AccessEdit),
                "statecode", new OptionSetValue(0));
            service.Seed("al_outcomecase", CaseId, "al_casereference", "300000011");
            service.Seed("al_outcome", OutcomeId,
                Outcomes.InitialOutcomeAttr, new OptionSetValue(OutcomeRules.OutcomeInsufficient),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            if (alreadyNamed)
            {
                service.Row("al_outcome", OutcomeId)["al_aqaccountablecontactid"] = new EntityReference("contact", Named);
            }

            provider.Context.MessageName = "al_SetFailAccountability";
            provider.Context.InputParameters["TargetId"] = OutcomeId.ToString("D");
            provider.Context.InputParameters["IdempotencyKey"] = "role-test-" + Guid.NewGuid().ToString("N");
            provider.Context.InputParameters["FqAdviser"] = false;
            provider.Context.InputParameters["FqParaplanner"] = false;
            provider.Context.InputParameters["AqAdviser"] = true;
            provider.Context.InputParameters["AqParaplanner"] = false;
            provider.Context.InputParameters["AqContactId"] = Named.ToString("D");

            try
            {
                new SetFailAccountabilityPlugin(null, null).Execute(provider);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        [Fact]
        public void The_command_refuses_a_named_person_without_the_role_and_writes_nothing()
        {
            var service = WithNamedPerson();
            Holds(service, WebRoleRegistry.TaxReviewerRole);

            var ex = RecordThroughCommand(service, out _);

            Assert.IsType<InvalidPluginExecutionException>(ex);
            Assert.StartsWith(CommandHelpers.PreconditionPrefix, ex.Message, StringComparison.Ordinal);
            Assert.Contains("Jane Smith (jane.smith@example.com) can't be named", ex.Message);
            Assert.False(service.Row("al_outcome", OutcomeId).Contains("al_aqaccountablecontactid"));
        }

        [Fact]
        public void The_command_records_a_named_person_who_holds_the_role()
        {
            var service = WithNamedPerson(state: 1);
            Holds(service, WebRoleRegistry.PlannerRole);

            var ex = RecordThroughCommand(service, out var provider);

            Assert.Null(ex);
            Assert.Equal("Recorded", provider.Context.OutputParameters["Status"]);
            Assert.Equal(Named, service.Row("al_outcome", OutcomeId)
                .GetAttributeValue<EntityReference>("al_aqaccountablecontactid").Id);
        }

        [Fact]
        public void Re_saving_a_person_already_named_is_not_refused_after_their_role_was_withdrawn()
        {
            var service = WithNamedPerson();
            Holds(service, WebRoleRegistry.TaxReviewerRole);

            var ex = RecordThroughCommand(service, out var provider, alreadyNamed: true);

            Assert.Null(ex);
            Assert.Equal("Recorded", provider.Context.OutputParameters["Status"]);
        }

        // ---- the portal request (Park and Apply) -------------------------------------------

        private static FakeOrganizationService PortalCase(FakeOrganizationService service)
        {
            service.Seed("contact", Checker, "fullname", "Casey Checker");
            service.Seed("al_outcomecase", CaseId, "al_casereference", "300000010");
            service.Seed(
                "al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", Checker),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "statecode", new OptionSetValue(0));
            return service;
        }

        private static void Request(FakeOrganizationService service, string json)
        {
            var provider = new FakeServiceProvider(service);
            service.Seed("systemuser", provider.Context.InitiatingUserId, "applicationid", Guid.NewGuid());
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = Checker;
            provider.Context.InputParameters["Target"] = new Entity("contact", Checker)
            {
                [AccountabilityRequestPlugin.RequestAttr] = json,
            };

            new AccountabilityRequestPlugin(null, null).Execute(provider);
        }

        private static string ParkJson()
        {
            return "{\"reviewId\":\"" + ReviewId.ToString("D") + "\",\"aqAdviser\":true,"
                + "\"aqContactId\":\"" + Named.ToString("D") + "\"}";
        }

        [Fact]
        public void Parking_a_person_without_the_role_is_refused_and_nothing_is_parked()
        {
            var service = PortalCase(WithNamedPerson());
            Holds(service, WebRoleRegistry.AqsReviewerRole);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => Request(service, ParkJson()));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, ex.Message, StringComparison.Ordinal);
            Assert.Contains("Jane Smith (jane.smith@example.com) can't be named", ex.Message);
            Assert.True(string.IsNullOrEmpty(
                service.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>("al_pendingaccountability")));
        }

        [Fact]
        public void Re_parking_a_person_already_parked_survives_their_role_being_withdrawn()
        {
            var service = PortalCase(WithNamedPerson());
            Holds(service, WebRoleRegistry.AqsReviewerRole);
            service.Row("al_reviewinstance", ReviewId)["al_pendingaccountability"] = ParkJson();

            Request(service, ParkJson().Replace("\"aqAdviser\":true", "\"aqAdviser\":true,\"fqAdviser\":true"));

            Assert.Contains("fqAdviser", service.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>("al_pendingaccountability"));
        }

        [Fact]
        public void Parking_a_role_holder_is_accepted()
        {
            var service = PortalCase(WithNamedPerson());
            Holds(service, WebRoleRegistry.AdviserRole);

            Request(service, ParkJson());

            Assert.Contains(Named.ToString("D"),
                service.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>("al_pendingaccountability"));
        }

        [Fact]
        public void Parking_with_nobody_named_reads_no_roles()
        {
            // The case's own adviser or paraplanner is identified by the case's email, not by role.
            var service = PortalCase(new FakeOrganizationService());

            Request(service, "{\"reviewId\":\"" + ReviewId.ToString("D") + "\",\"aqAdviser\":true}");

            Assert.Empty(service.FetchXml);
        }

        [Fact]
        public void Applying_to_an_outcome_refuses_a_person_without_the_role_and_writes_nothing()
        {
            var service = PortalCase(WithNamedPerson());
            service.Seed("al_outcome", OutcomeId,
                Outcomes.InitialOutcomeAttr, new OptionSetValue(OutcomeRules.OutcomeInsufficient),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            Holds(service);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => Request(service,
                "{\"outcomeId\":\"" + OutcomeId.ToString("D") + "\",\"aqAdviser\":true,"
                + "\"aqContactId\":\"" + Named.ToString("D") + "\"}"));

            Assert.Contains("don't hold the Adviser or Planner role", ex.Message);
            Assert.False(service.Row("al_outcome", OutcomeId).Contains("al_aqaccountablecontactid"));
        }
    }
}
