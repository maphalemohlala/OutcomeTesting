using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Section-level rules (AD-123). A section can now be dated out and can be owned by
    /// both disciplines; these are the two rules every reader applies, held in one place
    /// so the plug-ins and the two front ends cannot drift apart.
    /// </summary>
    public class SectionRulesTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        [Fact]
        public void A_section_with_no_dates_is_always_in_force()
        {
            Assert.True(SectionRules.IsSectionEffective(null, null, Today));
        }

        [Fact]
        public void A_section_dated_from_tomorrow_is_not_yet_in_force()
        {
            Assert.False(SectionRules.IsSectionEffective(Today.AddDays(1), null, Today));
        }

        [Fact]
        public void A_section_dated_out_today_is_no_longer_in_force()
        {
            Assert.False(SectionRules.IsSectionEffective(null, Today, Today));
        }

        [Fact]
        public void A_section_dated_out_tomorrow_is_still_in_force_today()
        {
            Assert.True(SectionRules.IsSectionEffective(null, Today.AddDays(1), Today));
        }

        [Fact]
        public void A_tax_section_serves_a_tax_review_only()
        {
            Assert.True(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleTaxTeam, ResponseRules.ReviewTypeTax));
            Assert.False(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleTaxTeam, ResponseRules.ReviewTypeAqs));
        }

        [Fact]
        public void An_aqs_section_serves_an_aqs_review_only()
        {
            Assert.True(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleAqsChecker, ResponseRules.ReviewTypeAqs));
            Assert.False(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleAqsChecker, ResponseRules.ReviewTypeTax));
        }

        [Fact]
        public void A_both_section_serves_either_discipline()
        {
            Assert.True(SectionRules.OwnerRoleServes(
                SectionRules.OwnerRoleBoth, ResponseRules.ReviewTypeTax));
            Assert.True(SectionRules.OwnerRoleServes(
                SectionRules.OwnerRoleBoth, ResponseRules.ReviewTypeAqs));
        }

        [Fact]
        public void A_section_owned_by_a_non_review_role_serves_neither_discipline()
        {
            // Adviser, T&C Manager and Manager / Admin are valid owner roles that no
            // review instance is ever opened as. Such a section must not be demanded.
            Assert.False(SectionRules.OwnerRoleServes(120910102, ResponseRules.ReviewTypeTax));
            Assert.False(SectionRules.OwnerRoleServes(120910102, ResponseRules.ReviewTypeAqs));
        }

        [Fact]
        public void An_unrecognised_review_type_is_served_by_nothing()
        {
            Assert.False(SectionRules.OwnerRoleServes(SectionRules.OwnerRoleBoth, 999));
            Assert.False(SectionRules.OwnerRoleServes(ResponseRules.OwnerRoleTaxTeam, 999));
        }
    }
}
