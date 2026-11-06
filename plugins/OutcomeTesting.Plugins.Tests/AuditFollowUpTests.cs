using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>Server halves of the Code App audit (2026-11-06).</summary>
    public class AuditFollowUpTests
    {
        private static readonly DateTime Today = new DateTime(2026, 11, 6);

        private static Entity VersionEndingOn(DateTime? end)
        {
            var version = new Entity("al_questionversion", Guid.NewGuid());
            if (end.HasValue)
            {
                version["al_effectiveto"] = end.Value;
            }

            return version;
        }

        [Fact]
        public void An_edit_keeps_a_retirement_scheduled_for_later()
        {
            var end = Today.AddMonths(1);

            Assert.Equal(end, ChecklistQueries.CarriedEnd(VersionEndingOn(end), Today));
        }

        [Fact]
        public void An_open_ended_version_has_nothing_to_carry()
        {
            Assert.Null(ChecklistQueries.CarriedEnd(VersionEndingOn(null), Today));
        }

        [Fact]
        public void An_end_on_or_before_the_start_is_not_carried()
        {
            Assert.Null(ChecklistQueries.CarriedEnd(VersionEndingOn(Today), Today));
            Assert.Null(ChecklistQueries.CarriedEnd(VersionEndingOn(Today.AddDays(-1)), Today));
        }

        [Fact]
        public void A_retired_role_is_not_active()
        {
            var retired = new Entity("mspp_webrole", Guid.NewGuid()) { ["statecode"] = new OptionSetValue(1) };
            var active = new Entity("mspp_webrole", Guid.NewGuid()) { ["statecode"] = new OptionSetValue(0) };

            Assert.False(WebRoleRegistry.IsActive(retired));
            Assert.True(WebRoleRegistry.IsActive(active));
        }

        [Fact]
        public void A_long_observation_gives_way_so_the_outcome_survives()
        {
            var text = Remediation.Describe("Tax check: Pass with issues", new string('x', 5000), new[] { "Q1: Fail" });

            Assert.True(text.Length <= 2000);
            Assert.Contains("- Q1: Fail", text);
            Assert.Contains("(Tax check: Pass with issues)", text);
            Assert.Contains("The checker recorded: xxx", text);
        }

        [Fact]
        public void A_short_description_keeps_its_order()
        {
            var nl = Environment.NewLine;

            Assert.Equal(
                Remediation.IssuesHeading + nl + "- Q1: Fail" + nl + nl
                    + "The checker recorded: gap" + nl + nl
                    + "Raised automatically when the review was submitted (Fail). Review the file and record what you have put right.",
                Remediation.Describe("Fail", "gap", new[] { "Q1: Fail" }));
        }
    }
}
