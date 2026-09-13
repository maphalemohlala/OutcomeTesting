using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// PP-07, PP-08. The refusal for unanswered required questions names them, so the page
    /// can mark the rows and the checker is not left counting.
    /// </summary>
    public class SubmitReviewMandatoryTests
    {
        private static Entity Version(Guid id, string code)
        {
            var version = new Entity("al_questionversion", id);
            if (code != null)
            {
                version["q.al_questioncode"] = new AliasedValue("al_question", "al_questioncode", code);
            }

            return version;
        }

        [Fact]
        public void Names_every_unanswered_question_by_code_and_carries_the_ids_for_the_page()
        {
            var first = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var second = Guid.Parse("22222222-2222-4222-8222-222222222222");

            var refusal = SubmitReviewPlugin.UnansweredRefusal(
                new[] { Version(first, "Q-E1-02"), Version(second, "Q-GR-01") }, 42);

            Assert.StartsWith("PRECONDITION: Complete all questions marked Required", refusal);
            Assert.Contains("2 of 42 required questions are unanswered: Q-E1-02, Q-GR-01.", refusal);
            Assert.EndsWith("[unanswered:" + first.ToString("D") + "," + second.ToString("D") + "]", refusal);
        }

        [Fact]
        public void A_version_whose_code_did_not_come_back_is_still_listed()
        {
            var refusal = SubmitReviewPlugin.UnansweredRefusal(new[] { Version(Guid.NewGuid(), null) }, 1);

            Assert.Contains("(no code)", refusal);
        }
    }
}
