using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// <c>{{completedCheck}}</c> attaches the completed check to whichever letter carries it
    /// (AD-171).
    ///
    /// <para>
    /// Offered as a token rather than a checkbox because that is where an administrator already
    /// looks for what a letter can carry. It renders as nothing: what the letter SAYS about the
    /// attachment is theirs to write, and a marker that inserted its own sentence would be
    /// wording they could not edit.
    /// </para>
    /// </summary>
    public class CompletedCheckMarkerTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee3333-3333-4333-8333-333333333333");

        // ------------------------------------------------------------ the marker itself

        [Fact]
        public void It_renders_as_nothing_in_the_letter()
        {
            var service = Case();
            Template(NotificationTemplates.CasePassed, service,
                "A case passed", "<p>Case {{reference}} passed.{{completedCheck}}</p>");

            var letter = NotificationTemplates.Render(
                service,
                NotificationTemplates.CasePassed,
                new Dictionary<string, string>
                {
                    { NotificationTemplates.TokenReference, "OT-1" },
                });

            Assert.True(letter.WantsCompletedCheck);
            Assert.DoesNotContain("completedCheck", letter.Body);
            Assert.DoesNotContain("{{", letter.Body);
        }

        [Fact]
        public void Every_letter_may_use_it_whatever_its_own_token_list_says()
        {
            // It says what the letter CARRIES rather than what it says, so no letter's own list
            // needs to name it - and listing it twelve times would invite the twelve to
            // disagree.
            foreach (var definition in NotificationTemplates.All)
            {
                Assert.Empty(NotificationTemplates.UnknownTokens(
                    definition.Code, "Subject", "{{completedCheck}}"));
            }
        }

        [Fact]
        public void A_letter_of_your_own_may_use_it_too()
        {
            Assert.Null(NotificationTemplateGuardPlugin.Refusal(
                "TELL-THE-MANAGER",
                "A case passed",
                "<p>Case {{reference}}.{{completedCheck}}</p>",
                NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindTcManager,
                false));
        }

        [Fact]
        public void Spacing_inside_the_braces_does_not_hide_it()
        {
            Assert.True(NotificationTemplates.Mentions("a {{ completedCheck }} b", "completedCheck"));
            Assert.False(NotificationTemplates.Mentions("a {{completedChecks}} b", "completedCheck"));
            Assert.False(NotificationTemplates.Mentions("no tokens here", "completedCheck"));
        }

        // ------------------------------------------------------------ what it attaches

        [Fact]
        public void A_letter_carrying_it_is_queued_with_the_document()
        {
            var service = Case();
            Template(NotificationTemplates.CasePassed, service,
                "A case passed", "<p>Case {{reference}} passed.{{completedCheck}}</p>");

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "adviser@example.com", "Subject", "Body",
                occurrence: null, templateCode: NotificationTemplates.CasePassed);

            var row = Queued(service).Single();
            Assert.Equal("Completed check OT-2026-0417.pdf", row.AttachmentName);
            Assert.False(string.IsNullOrWhiteSpace(row.AttachmentBody));
        }

        [Fact]
        public void A_letter_without_it_is_queued_with_nothing_attached()
        {
            // The negative. Every letter but the para-planner's sent without a document before
            // this, and adding one to all of them would be a change nobody asked for.
            var service = Case();
            Template(NotificationTemplates.CasePassed, service,
                "A case passed", "<p>Case {{reference}} passed.</p>");

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "adviser@example.com", "Subject", "Body",
                occurrence: null, templateCode: NotificationTemplates.CasePassed);

            Assert.True(string.IsNullOrWhiteSpace(Queued(service).Single().AttachmentBody));
        }

        [Fact]
        public void A_letter_of_your_own_carrying_it_is_queued_with_the_document()
        {
            var service = Case();
            var contact = service.Seed("contact", Guid.NewGuid(),
                "emailaddress1", "manager@example.com", "statecode", 0);

            var row = service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, "TELL-THE-MANAGER",
                NotificationTemplates.SubjectAttr, "A case passed",
                NotificationTemplates.BodyAttr, "<p>Case {{reference}}.{{completedCheck}}</p>",
                NotificationTemplateRows.EventAttr,
                new OptionSetValue(NotificationOutbox.EventCasePassed),
                NotificationTemplateRows.RecipientKindAttr,
                new OptionSetValue(NotificationRecipients.KindContact),
                "statecode", 0);
            row[NotificationTemplateRows.RecipientContactAttr] =
                new EntityReference("contact", contact.Id);

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "adviser@example.com", "Subject", "Body");

            var custom = Queued(service).Single(n => n.Recipient == "manager@example.com");
            Assert.False(string.IsNullOrWhiteSpace(custom.AttachmentBody));
        }

        [Fact]
        public void The_para_planners_letter_still_attaches_without_anybody_asking()
        {
            // Unchanged by all of this. It attaches by its own call, so an environment holding
            // no rows sends exactly what it sent before (AD-164).
            var service = Case();

            var id = NotificationOutbox.QueueWithCompletedCheck(
                service, Guid.NewGuid(), NotificationOutbox.EventReviewSubmitted,
                "al_outcomecase", CaseId, "para@example.com",
                "Review submitted", "The review has been submitted.",
                new EntityReference("al_outcomecase", CaseId));

            Assert.NotEqual(Guid.Empty, id);
            Assert.False(string.IsNullOrWhiteSpace(Queued(service).Single().AttachmentBody));
        }

        // ------------------------------------------------------------ helpers

        private static FakeOrganizationService Case()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "OT-2026-0417",
                "al_clientname", "A. Client",
                "al_advisername", "Adviser User 1",
                "al_adviseremail", "adviser@example.com",
                "statecode", 0);
            return service;
        }

        private static void Template(
            string code, FakeOrganizationService service, string subject, string body)
        {
            service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, code,
                NotificationTemplates.SubjectAttr, subject,
                NotificationTemplates.BodyAttr, body,
                "statecode", 0);
        }

        private sealed class Row
        {
            public string Recipient { get; set; }
            public string AttachmentName { get; set; }
            public string AttachmentBody { get; set; }
        }

        private static List<Row> Queued(FakeOrganizationService service)
        {
            var query = new QueryExpression(NotificationOutbox.NotificationEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_recipientemail",
                    NotificationOutbox.AttachmentNameAttr,
                    NotificationOutbox.AttachmentBodyAttr),
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => new Row
                {
                    Recipient = e.GetAttributeValue<string>("al_recipientemail"),
                    AttachmentName = e.GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr),
                    AttachmentBody = e.GetAttributeValue<string>(NotificationOutbox.AttachmentBodyAttr),
                })
                .ToList();
        }
    }
}
