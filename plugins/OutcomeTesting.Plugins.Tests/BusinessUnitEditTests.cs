using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A checker corrects the Business Unit from the portal check page (user, 2026-11-26): the
    /// import derives it from Group2, which can leave it blank or wrong.
    /// </summary>
    public class BusinessUnitEditTests
    {
        private static readonly Guid CaseId = Guid.Parse("b0000000-2222-4222-8222-222222222222");
        private static readonly Guid Checker = Guid.Parse("b0000000-3333-4333-8333-333333333333");

        private static FakeOrganizationService Ready()
        {
            var service = new FakeOrganizationService();
            service.Seed("contact", Checker, "fullname", "A Checker");
            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "300000010",
                CaseHeaderRules.BusinessUnitAttribute, new OptionSetValue(ImportRules.BusinessUnitIndependent));
            service.SeedOptionSet("al_outcomecase", CaseHeaderRules.BusinessUnitAttribute,
                ImportRules.BusinessUnitIndependent, "Independent",
                ImportRules.BusinessUnitRestricted, "Restricted");
            service.Seed("al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", Checker),
                "statecode", new OptionSetValue(0));
            return service;
        }

        private static void Send(FakeOrganizationService service, string value)
        {
            var provider = new FakeServiceProvider(service);
            // The portal writes request columns as its application user.
            service.Seed("systemuser", provider.Context.InitiatingUserId, "applicationid", Guid.NewGuid());
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = Checker;
            provider.Context.InputParameters["Target"] = new Entity("contact", Checker)
            {
                [CaseHeaderRequestPlugin.RequestAttr] =
                    "{\"caseId\":\"" + CaseId.ToString("D") + "\",\"fields\":\"{\\\"al_businessunit\\\":\\\"" + value + "\\\"}\"}",
            };

            new CaseHeaderRequestPlugin(null, null).Execute(provider);
        }

        private static OptionSetValue Held(FakeOrganizationService service)
        {
            return service.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>(CaseHeaderRules.BusinessUnitAttribute);
        }

        [Fact]
        public void The_assigned_checker_changes_it_from_the_portal()
        {
            var service = Ready();

            Send(service, ImportRules.BusinessUnitRestricted.ToString());

            Assert.Equal(ImportRules.BusinessUnitRestricted, Held(service).Value);
        }

        [Fact]
        public void The_checker_can_clear_it()
        {
            var service = Ready();

            Send(service, "");

            Assert.Null(Held(service));
        }

        [Fact]
        public void A_value_that_is_not_one_of_its_options_is_refused()
        {
            var service = Ready();

            var error = Assert.Throws<InvalidPluginExecutionException>(() => Send(service, "999"));
            Assert.Contains("Business Unit", error.Message);
            Assert.Equal(ImportRules.BusinessUnitIndependent, Held(service).Value);
        }
    }
}
