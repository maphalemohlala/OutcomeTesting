using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Answering against the AD-123 section rules. The discipline check becomes
    /// membership, so a Both section is answerable by either team; and a retired section
    /// takes no new answer while the answers it already holds keep resolving (AD-091).
    /// </summary>
    public class ResponseGuardSectionRulesTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        private static Entity Section(int ownerRole, DateTime? from = null, DateTime? to = null)
        {
            var section = new Entity("al_section", Guid.NewGuid())
            {
                ["al_ownerrole"] = new OptionSetValue(ownerRole),
            };
            if (from.HasValue) section["al_effectivefrom"] = from.Value;
            if (to.HasValue) section["al_effectiveto"] = to.Value;
            return section;
        }

        [Fact]
        public void A_tax_review_may_answer_a_tax_section()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam), ResponseRules.ReviewTypeTax, Today));
        }

        [Fact]
        public void A_tax_review_may_answer_a_both_section()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(SectionRules.OwnerRoleBoth), ResponseRules.ReviewTypeTax, Today));
        }

        [Fact]
        public void An_aqs_review_may_answer_a_both_section()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(SectionRules.OwnerRoleBoth), ResponseRules.ReviewTypeAqs, Today));
        }

        [Fact]
        public void A_tax_review_may_not_answer_an_aqs_section()
        {
            var refusal = ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleAqsChecker), ResponseRules.ReviewTypeTax, Today);

            Assert.NotNull(refusal);
            Assert.Contains("another discipline", refusal);
        }

        [Fact]
        public void A_retired_section_refuses_a_new_answer()
        {
            var refusal = ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam, to: Today),
                ResponseRules.ReviewTypeTax,
                Today);

            Assert.NotNull(refusal);
            Assert.Contains("no longer part of the checklist", refusal);
        }

        [Fact]
        public void A_section_not_yet_in_force_refuses_an_answer()
        {
            Assert.NotNull(ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam, from: Today.AddDays(1)),
                ResponseRules.ReviewTypeTax,
                Today));
        }

        [Fact]
        public void A_section_retired_tomorrow_still_accepts_an_answer_today()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam, to: Today.AddDays(1)),
                ResponseRules.ReviewTypeTax,
                Today));
        }

        [Fact]
        public void A_section_with_no_owner_role_is_refused_rather_than_assumed()
        {
            var section = new Entity("al_section", Guid.NewGuid());

            Assert.NotNull(ResponseGuardPlugin.SectionRefusal(
                section, ResponseRules.ReviewTypeTax, Today));
        }
    }
}
