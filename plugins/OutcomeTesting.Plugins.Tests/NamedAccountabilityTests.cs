using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Naming any contact as carrying a fail (project owner, 2026-09-19).
    ///
    /// AD-039 columns 11-20 are fixed pairs - "fail adviser name/code" and "fail paraplanner
    /// name/code" - and the values came from the case's own adviser and paraplanner, with
    /// the four flags deciding only whether to emit them. Accountability can now be given to
    /// someone the case does not name, and the extract has no column of its own for them, so
    /// the chosen person is written into whichever of those two slots the flags point at.
    ///
    /// The CODE column carries THAT PERSON's own code (2026-09-22, see AccountabilityCodeTests
    /// for the registry-backed round trip through NamedPerson). Before the registry gave a
    /// contact a code, this column was blanked outright: a contact carried no adviser or
    /// paraplanner code, and emitting the case's code beside someone else's name would have
    /// attributed the fail to a name and a code belonging to two different people - which is
    /// worse than a blank, because it reads as complete. That guard survives as the rule that
    /// the code must come from the same person as the name.
    /// </summary>
    public class NamedAccountabilityTests
    {
        private static Entity Case()
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            row["al_advisername"] = "Adviser User 1";
            row["al_advisercode"] = "ADV-1";
            row["al_paraplanner"] = "Jessica Bell";
            row["al_paraplannercode"] = "PP-9";
            return row;
        }

        private static GenerateExportPlugin.AccountablePerson Person(string name, string staffCode)
        {
            return new GenerateExportPlugin.AccountablePerson { Name = name, StaffCode = staffCode };
        }

        private static Entity Outcome(string fqNamed = null, string aqNamed = null)
        {
            var row = new Entity("al_outcome", Guid.NewGuid());
            if (fqNamed != null)
            {
                row[GenerateExportPlugin.FqAccountableContactAttr] =
                    new EntityReference("contact", Guid.NewGuid()) { Name = fqNamed };
            }

            if (aqNamed != null)
            {
                row[GenerateExportPlugin.AqAccountableContactAttr] =
                    new EntityReference("contact", Guid.NewGuid()) { Name = aqNamed };
            }

            return row;
        }

        [Fact]
        public void Uses_the_cases_own_person_when_nobody_was_named()
        {
            Assert.Equal(
                "Jessica Bell",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplanner", null));
            Assert.Equal(
                "PP-9",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplannercode", null));
        }

        [Fact]
        public void Writes_the_named_person_into_the_slot_instead()
        {
            Assert.Equal(
                "Clare Hook",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplanner", Person("Clare Hook", "CH-77")));
        }

        [Fact]
        public void A_named_persons_own_code_replaces_the_cases_code()
        {
            // Changed 2026-09-22: this used to assert string.Empty for both columns. A
            // contact carried no code then, so blanking was the only safe choice - showing
            // Jessica Bell's "PP-9" beside Clare Hook's name would have attributed the fail
            // to a name and a code belonging to two different people. The registry now
            // holds Clare Hook's own code, so that is what appears instead of a blank -
            // never "PP-9", which is still someone else's.
            var namedPerson = Person("Clare Hook", "CH-77");

            Assert.Equal(
                "CH-77",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplannercode", namedPerson));
            Assert.Equal(
                "CH-77",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_advisercode", namedPerson));
        }

        [Fact]
        public void A_named_person_with_no_code_leaves_the_column_blank()
        {
            // Not a fall back to the case's own code (still "PP-9" on this Case()). A named
            // person who the registry holds no code for leaves the column blank, exactly as
            // it always did - the difference from the old rule is that a code IS now shown
            // when that same person's own code exists.
            var namedPerson = Person("Clare Hook", null);

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplannercode", namedPerson));
        }

        [Fact]
        public void Emits_nothing_at_all_where_the_slot_does_not_carry_the_fail()
        {
            // The flag still decides. Naming someone does not put them in both slots.
            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(false, Case(), "al_paraplanner", Person("Clare Hook", "CH-77")));
        }

        [Fact]
        public void Ignores_a_named_person_that_is_only_whitespace()
        {
            Assert.Equal(
                "Jessica Bell",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplanner", Person("   ", "CH-77")));
        }

        [Fact]
        public void Trims_what_it_writes()
        {
            Assert.Equal(
                "Clare Hook",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplanner", Person("  Clare Hook  ", "CH-77")));
        }

        [Fact]
        public void Reads_the_named_person_off_the_outcome()
        {
            // The referenced contact is never seeded into the fake, matching a contact
            // deleted since the judgement was recorded: NamedPerson's Retrieve faults, is
            // caught, and the name off the lookup survives with a null code - see the
            // registry-backed round trip in AccountabilityCodeTests for the case where the
            // contact (and its code) is actually there.
            var service = new FakeOrganizationService();

            var named = GenerateExportPlugin.NamedPerson(
                service, Outcome(fqNamed: "Clare Hook"), GenerateExportPlugin.FqAccountableContactAttr);

            Assert.Equal("Clare Hook", named.Name);
            Assert.Null(named.StaffCode);
        }

        [Fact]
        public void Keeps_the_two_disciplines_apart()
        {
            var service = new FakeOrganizationService();
            var outcome = Outcome(fqNamed: "Clare Hook", aqNamed: "Ruth Maxwell");

            Assert.Equal(
                "Clare Hook",
                GenerateExportPlugin.NamedPerson(
                    service, outcome, GenerateExportPlugin.FqAccountableContactAttr).Name);
            Assert.Equal(
                "Ruth Maxwell",
                GenerateExportPlugin.NamedPerson(
                    service, outcome, GenerateExportPlugin.AqAccountableContactAttr).Name);
        }

        [Fact]
        public void Names_nobody_on_an_untouched_outcome()
        {
            var service = new FakeOrganizationService();

            Assert.Null(GenerateExportPlugin.NamedPerson(
                service, Outcome(), GenerateExportPlugin.FqAccountableContactAttr));
            Assert.Null(GenerateExportPlugin.NamedPerson(
                service, null, GenerateExportPlugin.FqAccountableContactAttr));
        }
    }
}
