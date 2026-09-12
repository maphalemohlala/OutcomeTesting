using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-123: a question is owed only when its section is in force and is not optional.
    /// The section columns arrive through a link-entity alias, so they are boxed in
    /// AliasedValue and a null column arrives as no entry at all — which is the shape
    /// this fixture reproduces.
    /// </summary>
    public class SubmitReviewSectionRulesTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        /// <summary>One row as the mandatory query returns it: the version, plus its
        /// section's columns under the "s" alias.</summary>
        private static Entity Row(
            bool optional = false,
            DateTime? sectionFrom = null,
            DateTime? sectionTo = null,
            DateTime? versionFrom = null,
            DateTime? versionTo = null)
        {
            var row = new Entity("al_questionversion", Guid.NewGuid());
            row["s.al_isoptional"] = new AliasedValue("al_section", "al_isoptional", optional);

            if (sectionFrom.HasValue)
            {
                row["s.al_effectivefrom"] =
                    new AliasedValue("al_section", "al_effectivefrom", sectionFrom.Value);
            }

            if (sectionTo.HasValue)
            {
                row["s.al_effectiveto"] =
                    new AliasedValue("al_section", "al_effectiveto", sectionTo.Value);
            }

            if (versionFrom.HasValue) row["al_effectivefrom"] = versionFrom.Value;
            if (versionTo.HasValue) row["al_effectiveto"] = versionTo.Value;

            return row;
        }

        [Fact]
        public void A_question_in_a_live_required_section_is_owed()
        {
            // The regression guard. Without it every test below passes by accident if the
            // gate simply stops demanding anything at all.
            Assert.True(SubmitReviewPlugin.IsOwed(Row(), Today));
        }

        [Fact]
        public void A_question_in_an_optional_section_is_not_owed()
        {
            Assert.False(SubmitReviewPlugin.IsOwed(Row(optional: true), Today));
        }

        [Fact]
        public void A_question_in_a_retired_section_is_not_owed()
        {
            Assert.False(SubmitReviewPlugin.IsOwed(Row(sectionTo: Today), Today));
        }

        [Fact]
        public void A_question_in_a_section_retired_tomorrow_is_still_owed_today()
        {
            Assert.True(SubmitReviewPlugin.IsOwed(Row(sectionTo: Today.AddDays(1)), Today));
        }

        [Fact]
        public void A_question_in_a_section_not_yet_in_force_is_not_owed()
        {
            Assert.False(SubmitReviewPlugin.IsOwed(Row(sectionFrom: Today.AddDays(1)), Today));
        }

        [Fact]
        public void A_retired_question_version_in_a_live_section_is_not_owed()
        {
            // The version window still applies; the section rule is an additional gate,
            // not a replacement for it.
            Assert.False(SubmitReviewPlugin.IsOwed(Row(versionTo: Today), Today));
        }

        [Fact]
        public void A_missing_optional_flag_is_read_as_required()
        {
            // al_isoptional is null on every section that existed before AD-123, because
            // Dataverse applies a boolean default to new rows only. Absent must therefore
            // mean required, or all twelve seeded sections silently stop being owed.
            var row = new Entity("al_questionversion", Guid.NewGuid());
            Assert.True(SubmitReviewPlugin.IsOwed(row, Today));
        }
    }
}
