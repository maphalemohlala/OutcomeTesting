using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The guard's reads on an answer save: one linked read for a whole checklist chain, and
    /// the chained reads, with the refusals they always gave, for a broken one.
    /// </summary>
    public class ResponseGuardPluginTests
    {
        private static readonly Guid ReviewId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000001");
        private static readonly Guid VersionId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000002");
        private static readonly Guid QuestionId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000003");
        private static readonly Guid SectionId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000004");
        private static readonly Guid ChecklistId = Guid.Parse("eeeeeeee-0000-4000-8000-000000000005");

        private static FakeOrganizationService Checklist(
            int sectionOwnerRole, bool questionHasSection = true, int responseType = ResponseRules.TypeText)
        {
            var service = new FakeOrganizationService();
            service.Seed("al_reviewinstance", ReviewId,
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusAssigned),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistId));
            service.Seed("al_questionversion", VersionId,
                "al_responsetype", new OptionSetValue(responseType),
                "al_questionid", new EntityReference("al_question", QuestionId));
            if (questionHasSection)
            {
                service.Seed("al_question", QuestionId, "al_sectionid", new EntityReference("al_section", SectionId));
            }
            else
            {
                service.Seed("al_question", QuestionId);
            }

            service.Seed("al_section", SectionId,
                "al_ownerrole", new OptionSetValue(sectionOwnerRole),
                "al_checklistversionid", new EntityReference("al_checklistversion", ChecklistId));
            return service;
        }

        private static Entity Create(FakeOrganizationService service)
        {
            var target = new Entity("al_response", Guid.NewGuid())
            {
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", ReviewId),
                ["al_questionversionid"] = new EntityReference("al_questionversion", VersionId),
                ["al_answertext"] = "An answer.",
            };

            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "Create";
            provider.Context.PrimaryEntityName = "al_response";
            provider.Context.InputParameters["Target"] = target;

            new ResponseGuardPlugin(null, null).Execute(provider);
            return target;
        }

        /// <summary>
        /// An Update whose Target carries the rich-text answer and nothing else, with the
        /// pre-image the step is registered with: the two links and every answer column as
        /// the row held them.
        /// </summary>
        private static Entity UpdateRichTextOnly(FakeOrganizationService service, string markup, string storedText)
        {
            var target = new Entity("al_response", Guid.NewGuid()) { ["al_answerrichtext"] = markup };

            var pre = new Entity("al_response", target.Id)
            {
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", ReviewId),
                ["al_questionversionid"] = new EntityReference("al_questionversion", VersionId),
                ["al_answerrichtext"] = "<p>Before.</p>",
            };
            if (storedText != null)
            {
                pre["al_answertext"] = storedText;
            }

            var provider = new FakeServiceProvider(service);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityName = "al_response";
            provider.Context.InputParameters["Target"] = target;
            provider.Context.PreEntityImages["PreImage"] = pre;

            new ResponseGuardPlugin(null, null).Execute(provider);
            return target;
        }

        [Fact]
        public void An_update_carrying_only_the_rich_text_answer_is_sanitised()
        {
            var service = Checklist(ResponseRules.OwnerRoleTaxTeam, responseType: ResponseRules.TypeRichText);

            var target = UpdateRichTextOnly(
                service, "<p>Kept.</p><script>alert(1)</script>", storedText: null);

            var stored = (string)target["al_answerrichtext"];
            Assert.Contains("Kept.", stored);
            Assert.DoesNotContain("script", stored, StringComparison.OrdinalIgnoreCase);
            // An Update is not given a response code: that is stamped on Create alone.
            Assert.False(target.Contains("al_responsecode"));
        }

        [Fact]
        public void An_update_carrying_only_rich_text_is_judged_with_the_stored_answer_beside_it()
        {
            // The written answer comes from the pre-image, so the pair is what is refused.
            var service = Checklist(ResponseRules.OwnerRoleTaxTeam, responseType: ResponseRules.TypeRichText);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateRichTextOnly(service, "<p>New.</p>", storedText: "A written answer."));

            Assert.Equal("PRECONDITION: This question takes a formatted answer only.", error.Message);
        }

        [Fact]
        public void An_update_carrying_only_rich_text_is_refused_on_a_question_that_takes_none()
        {
            var service = Checklist(ResponseRules.OwnerRoleTaxTeam);

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => UpdateRichTextOnly(service, "<p>New.</p>", storedText: "A written answer."));

            Assert.Equal("PRECONDITION: This question does not take a formatted answer.", error.Message);
        }

        [Fact]
        public void A_whole_chain_is_read_once_and_the_answer_is_let_through()
        {
            var service = Checklist(ResponseRules.OwnerRoleTaxTeam);

            var target = Create(service);

            Assert.Equal(ResponseRules.BuildResponseCode(ReviewId, VersionId), target["al_responsecode"]);
            // The review, then version, question and section together.
            Assert.Equal(1, service.RetrieveCount);
            Assert.Equal(1, service.RetrieveMultipleCount);
        }

        [Fact]
        public void A_section_read_through_the_link_still_refuses_another_discipline()
        {
            var service = Checklist(ResponseRules.OwnerRoleAqsChecker);

            var error = Assert.Throws<InvalidPluginExecutionException>(() => Create(service));

            Assert.Equal(
                "PRECONDITION: This question belongs to another discipline's section.", error.Message);
        }

        [Fact]
        public void A_question_with_no_section_keeps_its_own_refusal()
        {
            var service = Checklist(ResponseRules.OwnerRoleTaxTeam, questionHasSection: false);

            var error = Assert.Throws<InvalidPluginExecutionException>(() => Create(service));

            Assert.Equal("PRECONDITION: This question is not attached to a section.", error.Message);
        }
    }
}
