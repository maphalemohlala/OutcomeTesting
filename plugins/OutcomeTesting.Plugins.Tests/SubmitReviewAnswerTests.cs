using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-023: an answer lives in the typed column its response type dictates, and the
    /// mandatory-question check counts a response as answered only when one of those
    /// columns actually holds a value. Each column must be read at its real type.
    /// </summary>
    public class SubmitReviewAnswerTests
    {
        private static Entity Response()
        {
            return new Entity("al_response");
        }

        [Fact]
        public void An_empty_response_row_is_not_an_answer()
        {
            Assert.False(SubmitReviewPlugin.HasAnswer(Response()));
        }

        [Fact]
        public void Counts_a_multi_select_answer_without_throwing_on_its_real_type()
        {
            // Regression: al_answerchoices is a multi-select choice column and arrives as an
            // OptionSetValueCollection. Reading it as a string threw InvalidCastException
            // before any business rule ran, so a review holding an answered multi-select
            // could never be submitted — in the V8 checklist that is every Tax review,
            // because Q-TAX-01 is mandatory and multi-select.
            var r = Response();
            r["al_answerchoices"] = new OptionSetValueCollection { new OptionSetValue(120910340) };

            Assert.True(SubmitReviewPlugin.HasAnswer(r));
        }

        [Fact]
        public void An_empty_multi_select_collection_is_not_an_answer()
        {
            var r = Response();
            r["al_answerchoices"] = new OptionSetValueCollection();

            Assert.False(SubmitReviewPlugin.HasAnswer(r));
        }

        [Fact]
        public void Counts_a_single_choice_answer()
        {
            var r = Response();
            r["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass);

            Assert.True(SubmitReviewPlugin.HasAnswer(r));
        }

        [Fact]
        public void Counts_a_written_answer_but_not_whitespace()
        {
            var written = Response();
            written["al_answertext"] = "Evidence note";
            Assert.True(SubmitReviewPlugin.HasAnswer(written));

            var blank = Response();
            blank["al_answertext"] = "   ";
            Assert.False(SubmitReviewPlugin.HasAnswer(blank));
        }

        [Fact]
        public void Counts_a_date_answer()
        {
            var r = Response();
            r["al_answerdate"] = new DateTime(2026, 8, 30);

            Assert.True(SubmitReviewPlugin.HasAnswer(r));
        }
    }
}
