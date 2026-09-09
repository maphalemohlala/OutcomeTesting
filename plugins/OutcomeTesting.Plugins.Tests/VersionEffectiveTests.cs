using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-015, BR-013: a question version is retired by its effective-to date, not by
    /// deactivation, and RetireAndSucceedQuestion stamps the old version's effective-to and
    /// the successor's effective-from with the same day. Until 2026-09-09 nothing read those
    /// dates: the portal rendered every version of a question, the submit gate demanded an
    /// answer on each, and the outcome was read from whichever answer was saved last. On the
    /// one Tax review submitted in DEV that was the retired version's answer.
    /// </summary>
    public class VersionEffectiveTests
    {
        private static readonly DateTime Retired = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void A_version_with_no_effective_to_is_current()
        {
            Assert.True(ResponseRules.IsVersionEffective(null, null, new DateTime(2026, 9, 9, 10, 0, 0)));
        }

        [Fact]
        public void A_version_is_retired_from_the_start_of_its_effective_to_day()
        {
            // Retired "today" at 14:17 means the whole day belongs to the successor, because
            // the successor's effective-from is the same date.
            Assert.False(ResponseRules.IsVersionEffective(null, Retired, new DateTime(2026, 9, 8, 19, 2, 0)));
            Assert.False(ResponseRules.IsVersionEffective(null, Retired, new DateTime(2026, 9, 8, 0, 0, 0)));
        }

        [Fact]
        public void A_version_is_still_current_the_day_before_it_retires()
        {
            Assert.True(ResponseRules.IsVersionEffective(null, Retired, new DateTime(2026, 9, 7, 23, 59, 0)));
        }

        [Fact]
        public void A_successor_is_current_from_the_start_of_its_effective_from_day()
        {
            Assert.True(ResponseRules.IsVersionEffective(Retired, null, new DateTime(2026, 9, 8, 0, 0, 0)));
            Assert.False(ResponseRules.IsVersionEffective(Retired, null, new DateTime(2026, 9, 7, 23, 59, 0)));
        }

        [Fact]
        public void The_time_of_day_on_the_reference_never_matters()
        {
            // Effective dates are date-only columns, so the comparison is on dates. A version
            // retired on the 8th is retired at 00:01 on the 8th and at 23:59 on the 8th alike.
            Assert.False(ResponseRules.IsVersionEffective(null, Retired, new DateTime(2026, 9, 8, 0, 1, 0)));
            Assert.False(ResponseRules.IsVersionEffective(null, Retired, new DateTime(2026, 9, 8, 23, 59, 0)));
        }
    }
}
