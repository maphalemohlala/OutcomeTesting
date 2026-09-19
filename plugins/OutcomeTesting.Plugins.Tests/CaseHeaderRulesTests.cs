using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The dates a case header may carry (item 9, 2026-09-19). Pure rules, so the UK day
    /// boundary can be tested at the hour it actually matters rather than only when the
    /// suite happens to run.
    /// </summary>
    public class CaseHeaderRulesTests
    {
        // 30 June 2026 at 23:30 UTC is already 1 July in British Summer Time.
        private static readonly DateTime SummerEvening =
            new DateTime(2026, 6, 30, 23, 30, 0, DateTimeKind.Utc);

        // 15 January 2026 at 23:30 UTC is still 15 January in the UK - GMT, no offset.
        private static readonly DateTime WinterEvening =
            new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void Accepts_a_date_in_the_past()
        {
            Assert.Null(CaseHeaderRules.ValidateAdviceDate(new DateTime(2026, 1, 1), WinterEvening));
        }

        [Fact]
        public void Accepts_today()
        {
            // A meeting held this morning is checked this afternoon often enough that
            // refusing today would be refusing the ordinary case.
            Assert.Null(CaseHeaderRules.ValidateAdviceDate(new DateTime(2026, 1, 15), WinterEvening));
        }

        [Fact]
        public void Refuses_tomorrow_and_names_the_field_by_its_label()
        {
            var message = CaseHeaderRules.ValidateAdviceDate(new DateTime(2026, 1, 16), WinterEvening);

            Assert.Equal("Date of meeting - Client contact cannot be in the future.", message);
        }

        [Fact]
        public void Accepts_an_absent_date()
        {
            // Clearing the field is a legitimate edit; the column's required level decides
            // whether it may be left empty, not this rule.
            Assert.Null(CaseHeaderRules.ValidateAdviceDate(null, WinterEvening));
        }

        [Fact]
        public void Reads_today_in_UK_time_on_a_summer_evening()
        {
            // The case this rule exists for: 23:30 UTC on 30 June is 00:30 on 1 July in
            // London, so a checker entering their own today must not be told it is in the
            // future. Comparing against the UTC date would refuse it.
            Assert.Null(CaseHeaderRules.ValidateAdviceDate(new DateTime(2026, 7, 1), SummerEvening));
        }

        [Fact]
        public void Still_refuses_the_day_after_the_UK_today()
        {
            // The summer shift moves the boundary by a day; it does not remove it.
            var message = CaseHeaderRules.ValidateAdviceDate(new DateTime(2026, 7, 2), SummerEvening);

            Assert.Equal("Date of meeting - Client contact cannot be in the future.", message);
        }

        [Fact]
        public void Ignores_a_time_of_day_on_the_value()
        {
            // al_advicedate is DateOnly, but a caller can still hand this a timestamp. The
            // comparison is on the date, so late on the allowed day is still the allowed day.
            Assert.Null(CaseHeaderRules.ValidateAdviceDate(
                new DateTime(2026, 1, 15, 22, 0, 0), WinterEvening));
        }

        [Fact]
        public void UkDate_takes_a_summer_evening_into_the_next_day()
        {
            Assert.Equal(new DateTime(2026, 7, 1), CaseHeaderRules.UkDate(SummerEvening));
        }

        [Fact]
        public void UkDate_leaves_a_winter_evening_on_its_own_day()
        {
            Assert.Equal(new DateTime(2026, 1, 15), CaseHeaderRules.UkDate(WinterEvening));
        }
    }
}
