using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Trail Light columns L, N, R and T - the fail-accountability codes.
    ///
    /// These were blank whenever a specific person was named accountable, and deliberately
    /// so: a contact carried no code, and emitting the CASE's code beside someone else's
    /// name would have attributed the fail to a name and a code belonging to two different
    /// people. The registry makes that premise false, so the named person's own code goes in.
    /// </summary>
    public class AccountabilityCodeTests
    {
        private static Entity OutcomeCase()
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            row["al_advisername"] = "Jane Adviser";
            row["al_advisercode"] = "CASE-OLD";
            return row;
        }

        private static AccountablePersonFixture Named(
            FakeOrganizationService service, string name, string staffCode)
        {
            var id = Guid.NewGuid();
            service.Seed("contact", id,
                "fullname", name,
                "emailaddress1", "named@example.com",
                "al_staffcode", staffCode,
                "statecode", 0);
            return new AccountablePersonFixture { Id = id, Name = name };
        }

        private sealed class AccountablePersonFixture
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        private static Entity Outcome(AccountablePersonFixture person)
        {
            var row = new Entity("al_outcome", Guid.NewGuid());
            row[GenerateExportPlugin.FqAccountableContactAttr] =
                new EntityReference("contact", person.Id) { Name = person.Name };
            return row;
        }

        [Fact]
        public void A_named_person_brings_their_own_code()
        {
            var service = new FakeOrganizationService();
            var person = Named(service, "Robin Reviewer", "5150");

            var named = GenerateExportPlugin.NamedPerson(
                service, Outcome(person), GenerateExportPlugin.FqAccountableContactAttr);

            Assert.Equal("Robin Reviewer", named.Name);
            Assert.Equal("5150", named.StaffCode);
            Assert.Equal(
                "5150",
                GenerateExportPlugin.FlaggedText(true, OutcomeCase(), "al_advisercode", named));
        }

        [Fact]
        public void A_named_person_still_supplies_the_name_column()
        {
            var service = new FakeOrganizationService();
            var person = Named(service, "Robin Reviewer", "5150");

            var named = GenerateExportPlugin.NamedPerson(
                service, Outcome(person), GenerateExportPlugin.FqAccountableContactAttr);

            Assert.Equal(
                "Robin Reviewer",
                GenerateExportPlugin.FlaggedText(true, OutcomeCase(), "al_advisername", named));
        }

        [Fact]
        public void A_named_person_with_no_code_leaves_the_code_blank()
        {
            // NOT a fall back to the case's own code. That is the original defect: a name
            // and a code belonging to two different people reads as complete and is wrong.
            var service = new FakeOrganizationService();
            var person = Named(service, "Robin Reviewer", null);

            var named = GenerateExportPlugin.NamedPerson(
                service, Outcome(person), GenerateExportPlugin.FqAccountableContactAttr);

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(true, OutcomeCase(), "al_advisercode", named));
        }

        [Fact]
        public void Nobody_named_and_not_accountable_stays_empty()
        {
            var service = new FakeOrganizationService();

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(false, OutcomeCase(), "al_advisercode", null));
        }
    }
}
