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

        [Fact]
        public void Attaches_the_reasons_named_and_removes_the_ones_not()
        {
            var svc = new FakeOrganizationService();
            var keep = Guid.Parse("11111111-1111-4111-8111-111111111111");
            var drop = Guid.Parse("22222222-2222-4222-8222-222222222222");

            var payload = Choice(ResponseRules.ChoiceNo);
            payload.FailReasons = new[] { keep.ToString("D"), drop.ToString("D") };
            payload.RenderedReasons = new[] { keep.ToString("D"), drop.ToString("D") };
            var id = AnswerWriter.Save(svc, ReviewId, payload);

            // Both reasons were on screen (RenderedReasons) but only `keep` is ticked this
            // time, so `drop` is a genuine untick and must be removed.
            var second = Choice(ResponseRules.ChoiceNo);
            second.FailReasons = new[] { keep.ToString("D") };
            second.RenderedReasons = new[] { keep.ToString("D"), drop.ToString("D") };
            AnswerWriter.Save(svc, ReviewId, second);

            Assert.Contains(svc.Associations, a => a.RelatedId == keep);
            Assert.Contains(svc.Disassociations, d => d.RelatedId == drop);
            Assert.DoesNotContain(svc.Disassociations, d => d.RelatedId == keep);
            Assert.Equal(id, svc.Associations[0].TargetId);
        }

        [Fact]
        public void A_reason_outside_the_rendered_set_survives_a_save_that_does_not_tick_it()
        {
            // FR-013 regression (finding 2, 2026-09-08 review). `outsideCategory` is linked to
            // the response but was never offered on this page - e.g. it belongs to the other
            // team's category filter (template ~line 217, owner_role) - so it never appears in
            // RenderedReasons. Its absence from FailReasons on this save must not be read as
            // an untick: there was no checkbox for the reviewer to untick.
            var svc = new FakeOrganizationService();
            var visible = Guid.Parse("33333333-3333-4333-8333-333333333333");
            var outsideCategory = Guid.Parse("44444444-4444-4444-8444-444444444444");

            var payload = Choice(ResponseRules.ChoiceNo);
            payload.FailReasons = new[] { visible.ToString("D"), outsideCategory.ToString("D") };
            payload.RenderedReasons = new[] { visible.ToString("D"), outsideCategory.ToString("D") };
            AnswerWriter.Save(svc, ReviewId, payload);

            // A later save - triggered by editing the evidence note, say - only renders and
            // ticks `visible`. `outsideCategory` is absent from both FailReasons and
            // RenderedReasons, so it must be left alone rather than disassociated.
            var second = Choice(ResponseRules.ChoiceNo);
            second.FailReasons = new[] { visible.ToString("D") };
            second.RenderedReasons = new[] { visible.ToString("D") };
            AnswerWriter.Save(svc, ReviewId, second);

            Assert.DoesNotContain(svc.Disassociations, d => d.RelatedId == outsideCategory);

            var stillLinked = svc.RetrieveMultiple(new Microsoft.Xrm.Sdk.Query.QueryExpression("al_al_failreason_al_response")
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet("al_failreasonid"),
            }).Entities;
            Assert.Contains(stillLinked, e => e.GetAttributeValue<Guid>("al_failreasonid") == outsideCategory);
        }

        [Fact]
        public void An_absent_rendered_set_removes_nothing_rather_than_everything()
        {
            // The safe default: a caller that cannot vouch for what was on screen (an older
            // client, or a payload that failed to collect RenderedReasons) must fail closed.
            // Reading "nothing rendered" as "remove everything currently linked" is exactly
            // the bug finding 2 closes, so RenderedReasons left null must not reinstate it.
            var svc = new FakeOrganizationService();
            var linked = Guid.Parse("55555555-5555-4555-8555-555555555555");

            var payload = Choice(ResponseRules.ChoiceNo);
            payload.FailReasons = new[] { linked.ToString("D") };
            payload.RenderedReasons = new[] { linked.ToString("D") };
            AnswerWriter.Save(svc, ReviewId, payload);

            var second = Choice(ResponseRules.ChoiceNo);
            second.FailReasons = null;
            second.RenderedReasons = null;
            AnswerWriter.Save(svc, ReviewId, second);

            Assert.DoesNotContain(svc.Disassociations, d => d.RelatedId == linked);
        }
    }
}
