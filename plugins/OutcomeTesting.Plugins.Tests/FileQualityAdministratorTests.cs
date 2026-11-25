using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The administrator who carries a File Quality fail (requirements log row 10): recorded
    /// from the portal and the Code App, never exported, and once named the paraplanner is no
    /// longer derived for the file.
    /// </summary>
    public class FileQualityAdministratorTests
    {
        private static readonly Guid AdminId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");

        private static Entity OutcomeNaming(Guid? administrator)
        {
            var row = new Entity("al_outcome", Guid.NewGuid());
            if (administrator.HasValue)
            {
                row[GenerateExportPlugin.FqAdministratorContactAttr] = new EntityReference("contact", administrator.Value);
            }

            return row;
        }

        [Fact]
        public void A_named_administrator_stops_the_paraplanner_being_derived_for_the_file()
        {
            var row = OutcomeNaming(AdminId);

            Assert.False(GenerateExportPlugin.IsAccountable(
                row, GenerateExportPlugin.FqParaplannerFlag, ResponseRules.ChoiceFail, OutcomeRules.OutcomePass));
            Assert.False(GenerateExportPlugin.IsAccountable(
                row, GenerateExportPlugin.FqAdviserFlag, ResponseRules.ChoiceFail, OutcomeRules.OutcomePass));
        }

        [Fact]
        public void Without_an_administrator_the_paraplanner_is_still_derived()
        {
            Assert.True(GenerateExportPlugin.IsAccountable(
                OutcomeNaming(null), GenerateExportPlugin.FqParaplannerFlag, ResponseRules.ChoiceFail, OutcomeRules.OutcomePass));
        }

        [Fact]
        public void A_ticked_paraplanner_still_exports_beside_the_administrator()
        {
            var row = OutcomeNaming(AdminId);
            row[GenerateExportPlugin.FqParaplannerFlag] = true;

            Assert.True(GenerateExportPlugin.IsAccountable(
                row, GenerateExportPlugin.FqParaplannerFlag, ResponseRules.ChoiceFail, OutcomeRules.OutcomePass));
        }

        [Fact]
        public void The_administrator_leaves_the_advice_quality_derivation_alone()
        {
            Assert.True(GenerateExportPlugin.IsAccountable(
                OutcomeNaming(AdminId), GenerateExportPlugin.AqAdviserFlag, ResponseRules.ChoiceYes, OutcomeRules.OutcomePotentialHarm));
        }

        [Fact]
        public void A_parked_portal_choice_carries_the_administrator_to_the_outcome()
        {
            var review = new Entity("al_reviewinstance", Guid.NewGuid())
            {
                ["al_pendingaccountability"] = "{\"fqAdviser\":false,\"fqParaplanner\":false,\"aqAdviser\":false,"
                    + "\"aqParaplanner\":false,\"fqAdministratorId\":\"" + AdminId.ToString("D") + "\"}",
            };
            var outcome = new Entity("al_outcome");

            Assert.True(AccountabilityRequestPlugin.ApplyParked(new FakeOrganizationService(), review, outcome));

            var admin = outcome.GetAttributeValue<EntityReference>(GenerateExportPlugin.FqAdministratorContactAttr);
            Assert.NotNull(admin);
            Assert.Equal(AdminId, admin.Id);
        }

        [Fact]
        public void A_parked_choice_without_an_administrator_clears_one_left_behind()
        {
            var review = new Entity("al_reviewinstance", Guid.NewGuid())
            {
                ["al_pendingaccountability"] = "{\"fqAdviser\":false,\"fqParaplanner\":true,\"aqAdviser\":false,\"aqParaplanner\":false}",
            };
            var outcome = OutcomeNaming(AdminId);

            AccountabilityRequestPlugin.ApplyParked(new FakeOrganizationService(), review, outcome);

            Assert.True(outcome.Contains(GenerateExportPlugin.FqAdministratorContactAttr));
            Assert.Null(outcome.GetAttributeValue<EntityReference>(GenerateExportPlugin.FqAdministratorContactAttr));
        }

        [Fact]
        public void A_direct_write_of_the_administrator_is_guarded()
        {
            Assert.Contains(GenerateExportPlugin.FqAdministratorContactAttr, OutcomeGuardPlugin.AccountabilityColumns);
        }

        // --- "An administrator", no name (project owner, 2026-11-25) -----------------------

        [Fact]
        public void The_administrator_tick_alone_stops_the_paraplanner_being_derived()
        {
            var row = OutcomeNaming(null);
            row[GenerateExportPlugin.FqAdministratorFlag] = true;

            Assert.False(GenerateExportPlugin.IsAccountable(
                row, GenerateExportPlugin.FqParaplannerFlag, ResponseRules.ChoiceFail, OutcomeRules.OutcomePass));
            Assert.True(GenerateExportPlugin.AdministratorCarriesFile(row));
        }

        [Fact]
        public void A_name_recorded_before_the_tick_still_counts()
        {
            Assert.True(GenerateExportPlugin.AdministratorCarriesFile(OutcomeNaming(AdminId)));
            Assert.False(GenerateExportPlugin.AdministratorCarriesFile(OutcomeNaming(null)));
        }

        [Fact]
        public void A_parked_tick_sets_the_flag_and_names_nobody()
        {
            var review = new Entity("al_reviewinstance", Guid.NewGuid())
            {
                ["al_pendingaccountability"] = "{\"fqAdviser\":false,\"fqParaplanner\":false,\"aqAdviser\":false,"
                    + "\"aqParaplanner\":false,\"fqAdministrator\":true}",
            };
            var outcome = new Entity("al_outcome");

            AccountabilityRequestPlugin.ApplyParked(new FakeOrganizationService(), review, outcome);

            Assert.True(outcome.GetAttributeValue<bool>(GenerateExportPlugin.FqAdministratorFlag));
            Assert.Null(outcome.GetAttributeValue<EntityReference>(GenerateExportPlugin.FqAdministratorContactAttr));
        }

        [Fact]
        public void A_parked_name_from_before_the_tick_sets_the_flag_too()
        {
            var review = new Entity("al_reviewinstance", Guid.NewGuid())
            {
                ["al_pendingaccountability"] = "{\"fqAdviser\":false,\"fqParaplanner\":false,\"aqAdviser\":false,"
                    + "\"aqParaplanner\":false,\"fqAdministratorId\":\"" + AdminId.ToString("D") + "\"}",
            };
            var outcome = new Entity("al_outcome");

            AccountabilityRequestPlugin.ApplyParked(new FakeOrganizationService(), review, outcome);

            Assert.True(outcome.GetAttributeValue<bool>(GenerateExportPlugin.FqAdministratorFlag));
        }

        [Fact]
        public void Unticking_clears_a_kept_name_and_ticking_leaves_it()
        {
            var unticked = new Entity("al_outcome");
            SetFailAccountabilityPlugin.ApplyAdministratorContact(unticked, false, null);
            Assert.True(unticked.Contains(GenerateExportPlugin.FqAdministratorContactAttr));
            Assert.Null(unticked[GenerateExportPlugin.FqAdministratorContactAttr]);

            var ticked = new Entity("al_outcome");
            SetFailAccountabilityPlugin.ApplyAdministratorContact(ticked, true, null);
            Assert.False(ticked.Contains(GenerateExportPlugin.FqAdministratorContactAttr));

            var olderCaller = new Entity("al_outcome");
            SetFailAccountabilityPlugin.ApplyAdministratorContact(olderCaller, true, new EntityReference("contact", AdminId));
            Assert.Equal(AdminId, olderCaller.GetAttributeValue<EntityReference>(GenerateExportPlugin.FqAdministratorContactAttr).Id);
        }

        [Fact]
        public void A_direct_write_of_the_administrator_tick_is_guarded()
        {
            Assert.Contains(GenerateExportPlugin.FqAdministratorFlag, OutcomeGuardPlugin.AccountabilityColumns);
        }
    }
}
