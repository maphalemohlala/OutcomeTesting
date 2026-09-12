using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122. Retiring may be dated forward but never backward: a question that was in
    /// force when a review answered it must stay so, or the review's own history changes.
    /// </summary>
    public class RetireQuestionPluginTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 12);

        [Fact]
        public void An_absent_date_retires_today()
        {
            Assert.Equal(Today, RetireQuestionPlugin.ParseEffectiveTo(null, Today));
        }

        [Fact]
        public void A_future_date_is_accepted()
        {
            Assert.Equal(
                new DateTime(2026, 10, 1),
                RetireQuestionPlugin.ParseEffectiveTo("2026-10-01", Today));
        }

        [Fact]
        public void A_past_date_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => RetireQuestionPlugin.ParseEffectiveTo("2026-09-01", Today));

            Assert.Contains("in the past", error.Message);
        }

        [Fact]
        public void An_unparseable_date_is_refused_rather_than_defaulted()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => RetireQuestionPlugin.ParseEffectiveTo("soon", Today));
        }
    }
}
