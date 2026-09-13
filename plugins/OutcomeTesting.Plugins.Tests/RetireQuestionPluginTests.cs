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

        [Fact]
        public void A_question_already_out_of_force_cannot_be_retired_again()
        {
            // A second retire used to overwrite the first date with today, which brought the
            // question back into force for the gap and changed what submitted reviews owed.
            var refusal = RetireQuestionPlugin.AlreadyRetiredRefusal(new DateTime(2026, 8, 1), Today, "question");

            Assert.NotNull(refusal);
            Assert.Contains("2026-08-01", refusal);
        }

        [Fact]
        public void A_retirement_dated_out_today_counts_as_already_retired()
        {
            Assert.NotNull(RetireQuestionPlugin.AlreadyRetiredRefusal(Today, Today, "question"));
        }

        [Fact]
        public void A_future_dated_retirement_may_still_be_revised()
        {
            Assert.Null(RetireQuestionPlugin.AlreadyRetiredRefusal(new DateTime(2026, 10, 1), Today, "question"));
        }

        [Fact]
        public void A_question_with_no_end_date_is_in_force()
        {
            Assert.Null(RetireQuestionPlugin.AlreadyRetiredRefusal(null, Today, "question"));
        }
    }
}
