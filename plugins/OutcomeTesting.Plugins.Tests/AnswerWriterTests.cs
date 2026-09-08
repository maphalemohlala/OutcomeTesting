using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class AnswerWriterTests
    {
        private static readonly Guid ReviewId = Guid.Parse("b63c53c7-cca9-4111-8aac-e4fade069307");
        private static readonly Guid VersionId = Guid.Parse("3ce90bf2-64a1-4111-8bdd-e4fade069307");

        private static AnswerRequestPayload Choice(int value)
        {
            return new AnswerRequestPayload
            {
                QuestionVersionId = VersionId.ToString("D"),
                AnswerChoice = value,
            };
        }

        [Fact]
        public void Creates_a_response_when_none_exists()
        {
            var svc = new FakeOrganizationService();

            var id = AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceYes));

            var created = svc.Retrieve("al_response", id, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            Assert.Equal(ReviewId, created.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id);
            Assert.Equal(VersionId, created.GetAttributeValue<EntityReference>("al_questionversionid").Id);
            Assert.Equal(ResponseRules.ChoiceYes, created.GetAttributeValue<OptionSetValue>("al_answerchoice").Value);
        }

        [Fact]
        public void Updates_the_existing_response_rather_than_adding_a_second()
        {
            var svc = new FakeOrganizationService();
            var first = AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceYes));

            var second = AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceNo));

            Assert.Equal(first, second);
            var row = svc.Retrieve("al_response", first, new Microsoft.Xrm.Sdk.Query.ColumnSet(true));
            Assert.Equal(ResponseRules.ChoiceNo, row.GetAttributeValue<OptionSetValue>("al_answerchoice").Value);
        }

        [Fact]
        public void Does_not_set_the_lookups_on_update()
        {
            // The lookups are immutable once set: a second save must not be able to move an
            // answer onto another review, which is the one thing the Parent-scoped table
            // permission cannot police once the row exists.
            var svc = new FakeOrganizationService();
            AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceYes));

            svc.ClearUpdates();
            AnswerWriter.Save(svc, ReviewId, Choice(ResponseRules.ChoiceNo));

            var update = Assert.Single(svc.Updates);
            Assert.False(update.Contains("al_reviewinstanceid"));
            Assert.False(update.Contains("al_questionversionid"));
        }

        [Fact]
        public void Finds_an_existing_response_by_review_and_question_version_not_by_response_code()
        {
            // ResponseGuardPlugin stamps al_responsecode on Create; this fake does not run
            // plug-ins, so a row saved through it never carries the alternate key. Matching
            // FindExisting must not depend on that column - matching directly on the two
            // lookups is what makes the row findable here and against real Dataverse alike.
            var svc = new FakeOrganizationService();
            var created = svc.Create(new Entity("al_response")
            {
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", ReviewId),
                ["al_questionversionid"] = new EntityReference("al_questionversion", VersionId),
            });

            var found = AnswerWriter.FindExisting(svc, ReviewId, VersionId);

            Assert.Equal(created, found);
        }
    }
}
