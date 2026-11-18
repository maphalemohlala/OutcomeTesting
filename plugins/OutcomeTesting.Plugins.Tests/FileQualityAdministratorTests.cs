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

        [Fact]
        public void The_audit_line_names_the_administrator_or_says_none()
        {
            Assert.Equal("(none)", SetFailAccountabilityPlugin.DescribeAdministrator(null, null));
            Assert.Equal(
                "Ann Admin (" + AdminId.ToString("D") + ")",
                SetFailAccountabilityPlugin.DescribeAdministrator(new EntityReference("contact", AdminId), "Ann Admin"));
        }
    }
}
