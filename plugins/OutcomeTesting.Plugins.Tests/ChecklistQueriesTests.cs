using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The reads the administration commands share. One query for every code, and the
    /// current version chosen by number rather than by window, so a retired version is
    /// still found and can say so.
    /// </summary>
    public class ChecklistQueriesTests
    {
        private static readonly Guid QuestionId = Guid.Parse("55555555-5555-4555-8555-555555555555");

        [Fact]
        public void Refuses_a_code_already_in_use_and_names_it()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_question", Guid.NewGuid(), "al_questioncode", "Q-E1-01");

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => ChecklistQueries.EnsureQuestionCodesAreFree(svc, new[] { "Q-NEW-01", "Q-E1-01" }));

            Assert.Contains("Q-E1-01", error.Message);
        }

        [Fact]
        public void Accepts_codes_nobody_holds()
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_question", Guid.NewGuid(), "al_questioncode", "Q-E1-01");

            ChecklistQueries.EnsureQuestionCodesAreFree(svc, new[] { "Q-NEW-01", "Q-NEW-02" });
        }

        [Fact]
        public void The_current_version_is_the_highest_numbered_one_with_the_columns_asked_for()
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_questionversion", Guid.NewGuid(),
                "al_questionid", new EntityReference("al_question", QuestionId),
                "al_versionnumber", 1, "al_questiontext", "old");
            var v2 = Guid.NewGuid();
            svc.Seed(
                "al_questionversion", v2,
                "al_questionid", new EntityReference("al_question", QuestionId),
                "al_versionnumber", 2, "al_questiontext", "new");

            var current = ChecklistQueries.CurrentVersionOf(svc, QuestionId, "al_versionnumber", "al_questiontext");

            Assert.Equal(v2, current.Id);
            Assert.Equal("new", current.GetAttributeValue<string>("al_questiontext"));
        }

        [Fact]
        public void A_question_with_no_versions_has_no_current_one()
        {
            Assert.Null(ChecklistQueries.CurrentVersionOf(new FakeOrganizationService(), QuestionId, "al_versionnumber"));
        }
    }
}
