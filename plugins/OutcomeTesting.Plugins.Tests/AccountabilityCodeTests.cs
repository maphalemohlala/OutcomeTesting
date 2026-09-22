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

        [Fact]
        public void A_privilege_fault_on_the_contact_read_propagates()
        {
            // Narrower than the blanket catch this task's own brief had written (2026-09-22
            // review): NamedPerson tolerates only a not-found response from the contact
            // read. A privilege refusal must abort the export rather than be swallowed and
            // misread as "this person has no code" - that misreading would quietly blank
            // the accountability code for every named person in the whole batch, with
            // nothing traced to say why. SecLib::CheckPrivilege faults from a missing app
            // role have hit this environment repeatedly, so this is not a hypothetical.
            var service = new FakeOrganizationService();
            var person = Named(service, "Robin Reviewer", "5150");
            service.RetrieveThrows = new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault
                {
                    ErrorCode = -2147220960,
                    Message = "SecLib::CheckPrivilege failed. Caller does not have Read privilege on Contact.",
                },
                "SecLib::CheckPrivilege failed. Caller does not have Read privilege on Contact.");

            Assert.Throws<System.ServiceModel.FaultException<OrganizationServiceFault>>(() =>
                GenerateExportPlugin.NamedPerson(
                    service, Outcome(person), GenerateExportPlugin.FqAccountableContactAttr));
        }
    }

    /// <summary>
    /// GenerateExportPlugin.AccountabilityColumns - the exact method the record build calls
    /// to fill the eight AD-039 fail-accountability columns from fqNamedPerson/aqNamedPerson.
    ///
    /// FlaggedText and NamedPerson are both well covered taken alone, but neither can catch
    /// a mistake in WHICH of the two named-person values feeds WHICH discipline's pair - a
    /// one-variable swap between fqNamedPerson and aqNamedPerson would still pass every test
    /// either of them has on its own, because both sides are ordinary AccountablePerson
    /// values (2026-09-22 review). These tests call the production pairing method directly,
    /// with a DIFFERENT contact named for each discipline and both disciplines accountable
    /// simultaneously, so a crossed wire would show up as one person's code sitting beside
    /// the other person's name.
    /// </summary>
    public class AccountabilityColumnWiringTests
    {
        private static Entity Case()
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            row["al_advisername"] = "Case Adviser";
            row["al_advisercode"] = "CASE-ADV";
            row["al_paraplanner"] = "Case Paraplanner";
            row["al_paraplannercode"] = "CASE-PP";
            return row;
        }

        private static readonly GenerateExportPlugin.AccountablePerson FqPerson =
            new GenerateExportPlugin.AccountablePerson { Name = "Fiona Quinn", StaffCode = "FQ-1" };

        private static readonly GenerateExportPlugin.AccountablePerson AqPerson =
            new GenerateExportPlugin.AccountablePerson { Name = "Adam Quill", StaffCode = "AQ-2" };

        /// <summary>
        /// What the registry holds for the case's OWN two people, different from the codes
        /// the case row carries so a read of the retired case columns cannot pass by
        /// coincidence.
        /// </summary>
        private static GenerateExportPlugin.RegistryCodes Registry()
        {
            return new GenerateExportPlugin.RegistryCodes { Adviser = "REG-ADV", Paraplanner = "REG-PP" };
        }

        [Fact]
        public void The_adviser_pair_carries_each_disciplines_own_person_and_nobody_elses()
        {
            var outcomeRow = new Entity("al_outcome")
            {
                [GenerateExportPlugin.FqAdviserFlag] = true,
                [GenerateExportPlugin.AqAdviserFlag] = true,
            };

            var columns = GenerateExportPlugin.AccountabilityColumns(
                outcomeRow, Case(), FqPerson, AqPerson, Registry(),
                fileQualityChoice: null, effectiveOutcome: null);

            Assert.Equal("Fiona Quinn", columns["al_fqfailadvisername"]);
            Assert.Equal("FQ-1", columns["al_fqfailadvisercode"]);
            Assert.Equal("Adam Quill", columns["al_aqfailadvisername"]);
            Assert.Equal("AQ-2", columns["al_aqfailadvisercode"]);
        }

        [Fact]
        public void The_paraplanner_pair_carries_each_disciplines_own_person_and_nobody_elses()
        {
            var outcomeRow = new Entity("al_outcome")
            {
                [GenerateExportPlugin.FqParaplannerFlag] = true,
                [GenerateExportPlugin.AqParaplannerFlag] = true,
            };

            var columns = GenerateExportPlugin.AccountabilityColumns(
                outcomeRow, Case(), FqPerson, AqPerson, Registry(),
                fileQualityChoice: null, effectiveOutcome: null);

            Assert.Equal("Fiona Quinn", columns["al_fqfailparaplannername"]);
            Assert.Equal("FQ-1", columns["al_fqfailparaplannercode"]);
            Assert.Equal("Adam Quill", columns["al_aqfailparaplannername"]);
            Assert.Equal("AQ-2", columns["al_aqfailparaplannercode"]);
        }

        [Fact]
        public void With_nobody_named_the_code_columns_take_the_registry_and_the_name_columns_the_case()
        {
            // The COMMON path. A specific accountable contact is recorded only when a
            // reviewer names one; otherwise accountability is derived from the case's own
            // adviser and paraplanner, and this branch used to read al_advisercode /
            // al_paraplannercode straight off the case - the retired columns, one of which
            // plugins/OutcomeTesting.Registration still seeds as "ADV-S01". A single Trail
            // Light row could then carry a registry code in column B and a stale hand-typed
            // one in column N, for the SAME person, on a file read by position.
            //
            // Both disciplines are accountable at once and the registry codes differ from
            // the case's, so a reverted fallback shows up as "CASE-ADV"/"CASE-PP".
            var outcomeRow = new Entity("al_outcome")
            {
                [GenerateExportPlugin.FqAdviserFlag] = true,
                [GenerateExportPlugin.FqParaplannerFlag] = true,
                [GenerateExportPlugin.AqAdviserFlag] = true,
                [GenerateExportPlugin.AqParaplannerFlag] = true,
            };

            var columns = GenerateExportPlugin.AccountabilityColumns(
                outcomeRow, Case(), null, null, Registry(),
                fileQualityChoice: null, effectiveOutcome: null);

            Assert.Equal("REG-ADV", columns["al_fqfailadvisercode"]);
            Assert.Equal("REG-PP", columns["al_fqfailparaplannercode"]);
            Assert.Equal("REG-ADV", columns["al_aqfailadvisercode"]);
            Assert.Equal("REG-PP", columns["al_aqfailparaplannercode"]);

            // The name columns are unaffected: with nobody named, the fail belongs to the
            // case's own people and their names are on the case.
            Assert.Equal("Case Adviser", columns["al_fqfailadvisername"]);
            Assert.Equal("Case Paraplanner", columns["al_fqfailparaplannername"]);
            Assert.Equal("Case Adviser", columns["al_aqfailadvisername"]);
            Assert.Equal("Case Paraplanner", columns["al_aqfailparaplannername"]);
        }

        [Fact]
        public void With_nobody_named_and_no_registry_code_the_code_columns_are_blank()
        {
            // Blank, never the case's "CASE-ADV"/"CASE-PP". AD-039 is positional and read by
            // an external system that cannot tell a registry code from a stale one.
            var outcomeRow = new Entity("al_outcome")
            {
                [GenerateExportPlugin.FqAdviserFlag] = true,
                [GenerateExportPlugin.FqParaplannerFlag] = true,
                [GenerateExportPlugin.AqAdviserFlag] = true,
                [GenerateExportPlugin.AqParaplannerFlag] = true,
            };

            var columns = GenerateExportPlugin.AccountabilityColumns(
                outcomeRow, Case(), null, null, new GenerateExportPlugin.RegistryCodes(),
                fileQualityChoice: null, effectiveOutcome: null);

            Assert.Equal(string.Empty, columns["al_fqfailadvisercode"]);
            Assert.Equal(string.Empty, columns["al_fqfailparaplannercode"]);
            Assert.Equal(string.Empty, columns["al_aqfailadvisercode"]);
            Assert.Equal(string.Empty, columns["al_aqfailparaplannercode"]);

            // Still named, though. A blank code does not mean nobody carries the fail.
            Assert.Equal("Case Adviser", columns["al_fqfailadvisername"]);
            Assert.Equal("Case Paraplanner", columns["al_aqfailparaplannername"]);
        }
    }
}
