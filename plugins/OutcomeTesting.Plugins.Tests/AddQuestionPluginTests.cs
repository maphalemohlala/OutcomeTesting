using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122: a question may be dated forward so in-flight reviews finish against the set
    /// they started with, but never backward — a question cannot retrospectively have been
    /// owed by a review that has already been answered.
    /// </summary>
    public class AddQuestionPluginTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 12);

        [Fact]
        public void An_absent_date_is_today()
        {
            Assert.Equal(Today, AddQuestionPlugin.ParseEffectiveFrom(null, Today));
            Assert.Equal(Today, AddQuestionPlugin.ParseEffectiveFrom("   ", Today));
        }

        [Fact]
        public void Today_is_accepted()
        {
            Assert.Equal(Today, AddQuestionPlugin.ParseEffectiveFrom("2026-09-12", Today));
        }

        [Fact]
        public void A_future_date_is_accepted()
        {
            Assert.Equal(
                new DateTime(2026, 10, 1),
                AddQuestionPlugin.ParseEffectiveFrom("2026-10-01", Today));
        }

        [Fact]
        public void A_past_date_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AddQuestionPlugin.ParseEffectiveFrom("2026-09-11", Today));

            Assert.Contains("in the past", error.Message);
        }

        [Fact]
        public void An_unparseable_date_is_refused_rather_than_defaulted()
        {
            // Defaulting here would silently put the question in force today when the
            // administrator asked for something else.
            Assert.Throws<InvalidPluginExecutionException>(
                () => AddQuestionPlugin.ParseEffectiveFrom("next Tuesday", Today));
        }

        [Fact]
        public void The_date_is_read_as_a_day_not_a_moment()
        {
            // Date-only columns compared against a timestamp are what AD-091 was raised to
            // stop; a time component is discarded rather than carried.
            Assert.Equal(
                new DateTime(2026, 10, 1),
                AddQuestionPlugin.ParseEffectiveFrom("2026-10-01T14:30:00", Today));
        }
    }
}
