using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The checklist version in force uses the same window as every other effective-dated
    /// row: out of force from the start of its effective-to day. It alone used to count that
    /// day as in force, so on a changeover day a claim opened against the outgoing version.
    /// </summary>
    public class ChecklistQueriesFixTests
    {
        private static Guid Seed(FakeOrganizationService svc, DateTime from, DateTime? to)
        {
            var id = Guid.NewGuid();
            var row = svc.Seed("al_checklistversion", id,
                "statecode", new OptionSetValue(0), "al_effectivefrom", from);
            if (to.HasValue)
            {
                row["al_effectiveto"] = to.Value;
            }

            return id;
        }

        [Fact]
        public void A_version_ending_today_is_out_of_force_and_its_successor_is_in()
        {
            var today = DateTime.UtcNow.Date;
            var svc = new FakeOrganizationService();
            Seed(svc, today.AddDays(-100), today);
            var successor = Seed(svc, today, null);

            Assert.Equal(successor, ChecklistQueries.ChecklistVersionInForce(svc, "none"));
        }

        [Fact]
        public void A_version_ending_today_with_no_successor_is_refused()
        {
            var today = DateTime.UtcNow.Date;
            var svc = new FakeOrganizationService();
            Seed(svc, today.AddDays(-100), today);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ChecklistQueries.ChecklistVersionInForce(svc, "No checklist version is in force."));

            Assert.Contains("No checklist version is in force.", error.Message);
        }

        [Fact]
        public void A_version_ending_tomorrow_is_still_in_force()
        {
            var today = DateTime.UtcNow.Date;
            var svc = new FakeOrganizationService();
            var version = Seed(svc, today.AddDays(-100), today.AddDays(1));

            Assert.Equal(version, ChecklistQueries.ChecklistVersionInForce(svc, "none"));
        }
    }
}
