using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Who carries a fail is parked on the check being worked, and only that check's own
    /// checker may park it. Being assigned to another open check on the same case used to be
    /// enough, so an AQS checker could set the judgement on someone else's Tax check.
    /// </summary>
    public class AccountabilityRequestOwnReviewTests
    {
        private static readonly Guid CaseId = Guid.Parse("aaaaaaaa-1111-4111-8111-000000000001");
        private static readonly Guid TaxChecker = Guid.Parse("aaaaaaaa-1111-4111-8111-000000000002");
        private static readonly Guid AqsChecker = Guid.Parse("aaaaaaaa-1111-4111-8111-000000000003");
        private static readonly Guid TaxReview = Guid.Parse("aaaaaaaa-1111-4111-8111-000000000004");

        private static FakeOrganizationService Case()
        {
            var service = new FakeOrganizationService();
            service.Seed("contact", TaxChecker, "fullname", "Tax Checker");
            service.Seed("contact", AqsChecker, "fullname", "AQS Checker");
            service.Seed("al_outcomecase", CaseId, "al_casereference", "300000009");
            service.Seed(
                "al_reviewinstance", TaxReview,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", TaxChecker),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "statecode", new OptionSetValue(0));
            service.Seed(
                "al_reviewinstance", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_assignedcontactid", new EntityReference("contact", AqsChecker),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "statecode", new OptionSetValue(0));
            return service;
        }

        private static void Park(FakeOrganizationService service, Guid caller)
        {
            var provider = new FakeServiceProvider(service);
            // The portal writes request columns as its application user.
            service.Seed("systemuser", provider.Context.InitiatingUserId, "applicationid", Guid.NewGuid());
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "contact";
            provider.Context.PrimaryEntityId = caller;
            provider.Context.InputParameters["Target"] = new Entity("contact", caller)
            {
                [AccountabilityRequestPlugin.RequestAttr] =
                    "{\"reviewId\":\"" + TaxReview.ToString("D") + "\",\"fqParaplanner\":true}",
            };

            new AccountabilityRequestPlugin(null, null).Execute(provider);
        }

        [Fact]
        public void The_checks_own_checker_parks_the_judgement()
        {
            var service = Case();

            Park(service, TaxChecker);

            Assert.False(string.IsNullOrEmpty(
                service.Row("al_reviewinstance", TaxReview).GetAttributeValue<string>("al_pendingaccountability")));
        }

        [Fact]
        public void A_checker_on_another_check_of_the_same_case_is_refused()
        {
            var service = Case();

            var ex = Assert.ThrowsAny<Exception>(() => Park(service, AqsChecker));

            Assert.Contains("assigned to you", ex.Message);
            Assert.True(string.IsNullOrEmpty(
                service.Row("al_reviewinstance", TaxReview).GetAttributeValue<string>("al_pendingaccountability")));
        }
    }
}
