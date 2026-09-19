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
    /// The CODE column goes empty when a person is named. A contact carries no adviser or
    /// paraplanner code, and emitting the case's code beside someone else's name would
    /// attribute the fail to a name and a code belonging to two different people - which is
    /// worse than a blank, because it reads as complete.
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
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplanner", "Clare Hook"));
        }

        [Fact]
        public void Blanks_the_code_beside_a_named_person()
        {
            // Never "Clare Hook" with "PP-9" beside it: that is Jessica Bell's code.
            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplannercode", "Clare Hook"));
            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(true, Case(), "al_advisercode", "Clare Hook"));
        }

        [Fact]
        public void Emits_nothing_at_all_where_the_slot_does_not_carry_the_fail()
        {
            // The flag still decides. Naming someone does not put them in both slots.
            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(false, Case(), "al_paraplanner", "Clare Hook"));
        }

        [Fact]
        public void Ignores_a_named_person_that_is_only_whitespace()
        {
            Assert.Equal(
                "Jessica Bell",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplanner", "   "));
        }

        [Fact]
        public void Trims_what_it_writes()
        {
            Assert.Equal(
                "Clare Hook",
                GenerateExportPlugin.FlaggedText(true, Case(), "al_paraplanner", "  Clare Hook  "));
        }

        [Fact]
        public void Reads_the_named_person_off_the_outcome()
        {
            Assert.Equal(
                "Clare Hook",
                GenerateExportPlugin.NamedPerson(
                    Outcome(fqNamed: "Clare Hook"), GenerateExportPlugin.FqAccountableContactAttr));
        }

        [Fact]
        public void Keeps_the_two_disciplines_apart()
        {
            var outcome = Outcome(fqNamed: "Clare Hook", aqNamed: "Ruth Maxwell");

            Assert.Equal(
                "Clare Hook",
                GenerateExportPlugin.NamedPerson(outcome, GenerateExportPlugin.FqAccountableContactAttr));
            Assert.Equal(
                "Ruth Maxwell",
                GenerateExportPlugin.NamedPerson(outcome, GenerateExportPlugin.AqAccountableContactAttr));
        }

        [Fact]
        public void Names_nobody_on_an_untouched_outcome()
        {
            Assert.Null(GenerateExportPlugin.NamedPerson(
                Outcome(), GenerateExportPlugin.FqAccountableContactAttr));
            Assert.Null(GenerateExportPlugin.NamedPerson(
                null, GenerateExportPlugin.FqAccountableContactAttr));
        }
    }
}
