using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The submission gate stops demanding a primary root cause once the grade says the file
    /// passed (item 3, 2026-09-19).
    ///
    /// Q-GR-02 stays mandatory on al_questionversion, because on the reviews that owe it it
    /// genuinely is. Unsetting al_ismandatory would have been the smaller change and the
    /// wrong one: it excuses the question on every review at once, and leaves nothing to
    /// enforce on the non-pass reviews the root cause exists for.
    /// </summary>
    public class SubmitReviewRootCauseTests
    {
        private static readonly Guid ReviewId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        private static readonly Guid GradeQuestionId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        private static readonly Guid GradeVersionId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        private static readonly Guid RootCauseVersionId = Guid.Parse("77777777-7777-4777-8777-777777777777");
        private static readonly Guid CaseNotesVersionId = Guid.Parse("99999999-9999-4999-8999-999999999999");

        /// <summary>A row of the mandatory query, which reaches the code through a link alias.</summary>
        private static Entity Required(Guid id, string code)
        {
            return new Entity("al_questionversion", id)
            {
                ["q.al_questioncode"] = new AliasedValue("al_question", "al_questioncode", code),
            };
        }

        private static List<Entity> Checklist()
        {
            return new List<Entity>
            {
                Required(GradeVersionId, "Q-GR-01"),
                Required(RootCauseVersionId, "Q-GR-02"),
                Required(CaseNotesVersionId, "Q-GR-03"),
            };
        }

        /// <summary>
        /// An unsubmitted review whose grade answer is <paramref name="grade"/>, or which has
        /// not been graded when that is null. Seeded through the same question version ->
        /// question chain the plug-in queries, so the lookup is the real one.
        /// </summary>
        private static FakeOrganizationService Graded(int? grade)
        {
            var service = new FakeOrganizationService();
            service.Seed("al_reviewinstance", ReviewId);
            service.Seed("al_question", GradeQuestionId, "al_questioncode", "Q-GR-01");
            service.Seed(
                "al_questionversion",
                GradeVersionId,
                "al_effectivefrom", new DateTime(2026, 8, 26),
                "al_questionid", new EntityReference("al_question", GradeQuestionId));

            if (grade.HasValue)
            {
                service.Seed(
                    "al_response",
                    Guid.NewGuid(),
                    "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                    "al_questionversionid", new EntityReference("al_questionversion", GradeVersionId),
                    "al_answerchoice", new OptionSetValue(grade.Value),
                    "modifiedon", DateTime.UtcNow);
            }

            return service;
        }

        private static IEnumerable<string> Codes(IEnumerable<Entity> required)
        {
            foreach (var version in required)
            {
                var aliased = version.GetAttributeValue<AliasedValue>("q.al_questioncode");
                yield return (string)aliased.Value;
            }
        }

        [Fact]
        public void A_passing_review_is_no_longer_held_by_the_root_cause()
        {
            var service = Graded(ResponseRules.ChoicePass);
            var required = Checklist();

            SubmitReviewPlugin.RemoveRootCauseWhenPassing(service, ReviewId, required);

            Assert.Equal(new[] { "Q-GR-01", "Q-GR-03" }, Codes(required));
        }

        [Theory]
        [InlineData(ResponseRules.ChoicePassWithIssues)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        [InlineData(ResponseRules.ChoicePotentialHarm)]
        public void Every_other_grade_still_owes_it(int grade)
        {
            var service = Graded(grade);
            var required = Checklist();

            SubmitReviewPlugin.RemoveRootCauseWhenPassing(service, ReviewId, required);

            Assert.Equal(new[] { "Q-GR-01", "Q-GR-02", "Q-GR-03" }, Codes(required));
        }

        [Fact]
        public void An_ungraded_review_is_not_told_about_the_root_cause_as_well()
        {
            // The grade is mandatory and stays in the list, so the review is still refused.
            // What this avoids is naming Q-GR-02 in that refusal, when whether it is owed at
            // all cannot be known until the grade is given.
            var service = Graded(null);
            var required = Checklist();

            SubmitReviewPlugin.RemoveRootCauseWhenPassing(service, ReviewId, required);

            Assert.Equal(new[] { "Q-GR-01", "Q-GR-03" }, Codes(required));
        }

        [Fact]
        public void A_tax_review_reads_no_grade_at_all()
        {
            // S-GRADE is AQS-owned and the gate's query is already scoped by discipline, so
            // a Tax review never has a root cause in the list. It must not pay a query to
            // discover that.
            var service = Graded(ResponseRules.ChoicePass);
            var required = new List<Entity> { Required(Guid.NewGuid(), "Q-TAX-02") };

            SubmitReviewPlugin.RemoveRootCauseWhenPassing(service, ReviewId, required);

            Assert.Equal(new[] { "Q-TAX-02" }, Codes(required));
            Assert.Equal(0, service.RetrieveMultipleCount);
        }

        [Fact]
        public void Every_root_cause_version_in_force_is_excused_together()
        {
            // Nothing in the schema stops a second version of Q-GR-02 being effective at
            // once. Excusing only the first would leave the other demanding an answer the
            // page has stopped drawing.
            var service = Graded(ResponseRules.ChoicePass);
            var required = Checklist();
            required.Add(Required(Guid.NewGuid(), "Q-GR-02"));

            SubmitReviewPlugin.RemoveRootCauseWhenPassing(service, ReviewId, required);

            Assert.Equal(new[] { "Q-GR-01", "Q-GR-03" }, Codes(required));
        }

        [Fact]
        public void A_code_that_did_not_come_back_is_left_in_the_list()
        {
            // A required question the query could not name is still required. Dropping it on
            // the strength of a missing alias would excuse it silently.
            var service = Graded(ResponseRules.ChoicePass);
            var required = new List<Entity>
            {
                new Entity("al_questionversion", Guid.NewGuid()),
                Required(RootCauseVersionId, "Q-GR-02"),
            };

            SubmitReviewPlugin.RemoveRootCauseWhenPassing(service, ReviewId, required);

            Assert.Single(required);
            Assert.False(required[0].Contains("q.al_questioncode"));
        }
    }
}
