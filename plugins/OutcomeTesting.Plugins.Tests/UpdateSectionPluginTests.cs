using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-123, AD-043. Sections are not versioned, so the Audit Event's before/after is the
    /// only record of what a section used to be. It has to be accurate.
    /// </summary>
    public class UpdateSectionPluginTests
    {
        private static Entity Section(string name, bool optional, int ownerRole)
        {
            return new Entity("al_section")
            {
                ["al_name"] = name,
                ["al_isoptional"] = optional,
                ["al_ownerrole"] = new OptionSetValue(ownerRole),
            };
        }

        [Fact]
        public void An_unchanged_section_describes_no_change()
        {
            var before = Section("Consumer Duty", false, 120910101);
            var after = Section("Consumer Duty", false, 120910101);

            Assert.Equal(string.Empty, UpdateSectionPlugin.DescribeChanges(before, after));
        }

        [Fact]
        public void A_renamed_section_records_both_names()
        {
            var description = UpdateSectionPlugin.DescribeChanges(
                Section("Consumer Duty", false, 120910101),
                Section("Consumer Duty overlay", false, 120910101));

            Assert.Contains("Consumer Duty", description);
            Assert.Contains("Consumer Duty overlay", description);
        }

        [Fact]
        public void A_team_change_is_recorded_because_it_reaches_submitted_reviews()
        {
            var description = UpdateSectionPlugin.DescribeChanges(
                Section("Tax check", false, 120910100),
                Section("Tax check", false, 120910105));

            Assert.Contains("120910100", description);
            Assert.Contains("120910105", description);
        }

        [Fact]
        public void Becoming_optional_is_recorded()
        {
            var description = UpdateSectionPlugin.DescribeChanges(
                Section("CRP", false, 120910101),
                Section("CRP", true, 120910101));

            Assert.Contains("optional", description.ToLowerInvariant());
        }

        [Fact]
        public void An_absent_optional_flag_reads_as_required_not_as_a_change()
        {
            // al_isoptional is null on every section that predates AD-123. Reading absent
            // as anything other than "required" would report a change that never happened
            // on the first amendment of each of the twelve seeded sections.
            var before = new Entity("al_section") { ["al_name"] = "CRP" };
            var after = new Entity("al_section") { ["al_name"] = "CRP", ["al_isoptional"] = false };

            Assert.Equal(string.Empty, UpdateSectionPlugin.DescribeChanges(before, after));
        }

        [Fact]
        public void Several_changes_are_all_recorded()
        {
            var description = UpdateSectionPlugin.DescribeChanges(
                Section("CRP", false, 120910101),
                Section("Centralised Retirement Proposition", true, 120910105));

            Assert.Contains("CRP", description);
            Assert.Contains("Centralised Retirement Proposition", description);
            Assert.Contains("120910105", description);
            Assert.Contains("optional", description.ToLowerInvariant());
        }
    }
}
