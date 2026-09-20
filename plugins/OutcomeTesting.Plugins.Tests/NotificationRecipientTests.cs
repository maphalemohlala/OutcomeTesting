using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Choosing who a letter goes to, and adding letters of your own (AD-168).
    ///
    /// <para>
    /// The two dangerous directions are covered deliberately, because both fail quietly:
    /// an override that empties a recipient the sending code had worked out correctly, and a
    /// custom row carrying a built-in code, which would send that letter twice.
    /// </para>
    /// </summary>
    public class NotificationRecipientTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee2222-2222-4222-8222-222222222222");
        private static readonly Guid ReviewId = Guid.Parse("7e111111-1111-4111-8111-111111111111");

        // ------------------------------------------------------------ resolving a kind

        [Fact]
        public void The_adviser_is_the_email_on_the_case()
        {
            var service = Case();

            Assert.Equal(
                "adviser@example.com",
                NotificationRecipients.EmailFor(
                    service, NotificationRecipients.KindAdviser, CaseRef(), null, null));
        }

        [Fact]
        public void The_checker_is_who_the_review_is_allocated_to()
        {
            var service = Case();
            var checker = service.Seed("contact", Guid.NewGuid(),
                "emailaddress1", "checker@example.com", "statecode", 0);
            service.Seed("al_reviewinstance", ReviewId,
                "al_outcomecaseid", CaseRef(),
                "al_assignedcontactid", new EntityReference("contact", checker.Id));

            Assert.Equal(
                "checker@example.com",
                NotificationRecipients.EmailFor(
                    service,
                    NotificationRecipients.KindChecker,
                    CaseRef(),
                    new EntityReference("al_reviewinstance", ReviewId),
                    null));
        }

        [Fact]
        public void The_checker_is_never_read_from_the_name_on_the_upload_file()
        {
            // al_checkername arrives on the spreadsheet and names whoever it said, so it is
            // not evidence that anybody has been given the case. The allocation is.
            var service = Case();
            service.Row("al_outcomecase", CaseId)["al_checkername"] = "Someone From The Sheet";

            Assert.Null(NotificationRecipients.EmailFor(
                service, NotificationRecipients.KindChecker, CaseRef(), null, null));
        }

        [Fact]
        public void A_kind_the_case_cannot_answer_resolves_to_nobody_rather_than_throwing()
        {
            var service = new FakeOrganizationService();

            Assert.Null(NotificationRecipients.EmailFor(
                service, NotificationRecipients.KindAdviser, CaseRef(), null, null));
        }

        [Fact]
        public void A_kind_nobody_recognises_is_not_a_recipient()
        {
            Assert.False(NotificationRecipients.IsKnown(999));
            Assert.Null(NotificationRecipients.EmailFor(
                new FakeOrganizationService(), 999, CaseRef(), null, null));
        }

        // ------------------------------------------------------------ overriding a built-in

        [Fact]
        public void A_chosen_recipient_replaces_the_one_the_sending_code_worked_out()
        {
            var service = Case();
            Template(service, NotificationTemplates.CasePassed, NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindAdviser, subject: null, body: null);

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                occurrence: null, templateCode: NotificationTemplates.CasePassed);

            Assert.Equal("adviser@example.com", Sent(service).Single().Recipient);
        }

        [Fact]
        public void A_chosen_recipient_that_reaches_nobody_leaves_the_original_alone()
        {
            // The negative that matters. An override resolving to nothing must not empty a
            // recipient the sending code had correctly worked out - that would turn a
            // configuration mistake into a letter nobody ever receives.
            var service = Case();
            service.Row("al_outcomecase", CaseId)["al_adviseremail"] = null;
            Template(service, NotificationTemplates.CasePassed, NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindAdviser, subject: null, body: null);

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                occurrence: null, templateCode: NotificationTemplates.CasePassed);

            Assert.Equal("original@example.com", Sent(service).Single().Recipient);
        }

        [Fact]
        public void A_letter_with_no_chosen_recipient_keeps_its_own_routing()
        {
            var service = Case();

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            Assert.Equal("original@example.com", Sent(service).Single().Recipient);
        }

        // ------------------------------------------------------------ letters of your own

        [Fact]
        public void A_letter_attached_to_an_event_is_sent_as_well_as_the_built_in_one()
        {
            var service = Case();
            Template(service, "TELL-THE-MANAGER", NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindContact,
                "A case passed", "<p>Case {{reference}} passed.</p>",
                contact: Manager(service));

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            var sent = Sent(service);
            Assert.Equal(2, sent.Count);
            Assert.Contains(sent, n => n.Recipient == "original@example.com");
            Assert.Contains(sent, n => n.Recipient == "manager@example.com");
        }

        [Fact]
        public void A_letter_of_your_own_fills_in_the_tokens_the_case_can_answer()
        {
            var service = Case();
            Template(service, "TELL-THE-MANAGER", NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindContact,
                "Case {{reference}} passed", "<p>{{client}} was advised by {{adviser}}.</p>",
                contact: Manager(service));

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            var custom = Sent(service).Single(n => n.Recipient == "manager@example.com");
            Assert.Equal("Case OT-2026-0417 passed", custom.Subject);
            Assert.Contains("A. Client was advised by Adviser User 1", custom.Body);
        }

        [Fact]
        public void A_row_carrying_a_built_in_code_is_not_also_sent_as_an_extra()
        {
            // The double-send. A row for one of the twelve is that letter's own wording, and
            // treating it as an addition would send the same letter twice.
            var service = Case();
            Template(service, NotificationTemplates.CasePassed, NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindContact, "Edited", "<p>Edited.</p>",
                contact: Manager(service));

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            Assert.Single(Sent(service));
        }

        [Fact]
        public void A_letter_with_nobody_to_send_to_is_not_queued_at_all()
        {
            // An unaddressed row can never drain and nothing watches the outbox, so it would
            // sit there unnoticed. Not queuing it is the loud option.
            var service = Case();
            Template(service, "TELL-THE-MANAGER", NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindTcManager, "A case passed", "<p>It passed.</p>");

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            Assert.Single(Sent(service));
        }

        [Fact]
        public void A_letter_with_an_empty_body_is_not_sent_rather_than_arriving_blank()
        {
            var service = Case();
            Template(service, "TELL-THE-MANAGER", NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindContact, "A case passed", "   ",
                contact: Manager(service));

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            Assert.Single(Sent(service));
        }

        [Fact]
        public void A_letter_attached_to_another_event_stays_out_of_this_one()
        {
            var service = Case();
            Template(service, "TELL-THE-MANAGER", NotificationOutbox.EventRecheckDue,
                NotificationRecipients.KindContact, "Recheck", "<p>Recheck.</p>",
                contact: Manager(service));

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            Assert.Single(Sent(service));
        }

        [Fact]
        public void Two_letters_at_one_event_do_not_collide_with_each_other()
        {
            var service = Case();
            var manager = Manager(service);
            Template(service, "TELL-ONE", NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindContact, "One", "<p>One.</p>", contact: manager);
            Template(service, "TELL-TWO", NotificationOutbox.EventCasePassed,
                NotificationRecipients.KindContact, "Two", "<p>Two.</p>", contact: manager);

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            var sent = Sent(service);
            Assert.Equal(3, sent.Count);
            Assert.Equal(3, sent.Select(n => n.Code).Distinct().Count());
        }

        [Fact]
        public void An_unreadable_template_table_costs_the_extra_letters_and_not_the_event()
        {
            // The whole point of doing this after the built-in is created. A letter somebody
            // added must never take down the transaction that raised the event.
            var service = new ThrowsOnTemplateRead();

            var id = NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            Assert.NotEqual(Guid.Empty, id);
        }

        // ------------------------------------------------------------ helpers

        private static EntityReference CaseRef()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

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

        private static EntityReference Manager(FakeOrganizationService service)
        {
            var contact = service.Seed("contact", Guid.NewGuid(),
                "emailaddress1", "manager@example.com", "statecode", 0);
            return new EntityReference("contact", contact.Id);
        }

        private static void Template(
            FakeOrganizationService service,
            string code,
            int eventValue,
            int recipientKind,
            string subject,
            string body,
            EntityReference contact = null)
        {
            var row = service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, code,
                NotificationTemplateRows.EventAttr, new OptionSetValue(eventValue),
                NotificationTemplateRows.RecipientKindAttr, new OptionSetValue(recipientKind),
                "statecode", 0);

            if (subject != null) row[NotificationTemplates.SubjectAttr] = subject;
            if (body != null) row[NotificationTemplates.BodyAttr] = body;
            if (contact != null) row[NotificationTemplateRows.RecipientContactAttr] = contact;
        }

        private sealed class Queued
        {
            public string Recipient { get; set; }
            public string Subject { get; set; }
            public string Body { get; set; }
            public string Code { get; set; }
        }

        private static List<Queued> Sent(FakeOrganizationService service)
        {
            var query = new QueryExpression(NotificationOutbox.NotificationEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_recipientemail", "al_subject", "al_body", "al_notificationcode"),
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => new Queued
                {
                    Recipient = e.GetAttributeValue<string>("al_recipientemail"),
                    Subject = e.GetAttributeValue<string>("al_subject"),
                    Body = e.GetAttributeValue<string>("al_body"),
                    Code = e.GetAttributeValue<string>("al_notificationcode"),
                })
                .ToList();
        }

        /// <summary>Reads of the template table fail; everything else works.</summary>
        private sealed class ThrowsOnTemplateRead : IOrganizationService
        {
            private readonly FakeOrganizationService _inner = new FakeOrganizationService();

            public EntityCollection RetrieveMultiple(QueryBase query)
            {
                var expression = query as QueryExpression;
                if (expression != null
                    && expression.EntityName == NotificationTemplates.TemplateEntity)
                {
                    throw new InvalidOperationException("template table unavailable");
                }

                return _inner.RetrieveMultiple(query);
            }

            public Guid Create(Entity entity) { return _inner.Create(entity); }
            public Entity Retrieve(string name, Guid id, ColumnSet columns) { return _inner.Retrieve(name, id, columns); }
            public void Update(Entity entity) { _inner.Update(entity); }
            public void Delete(string name, Guid id) { _inner.Delete(name, id); }
            public OrganizationResponse Execute(OrganizationRequest request) { return _inner.Execute(request); }
            public void Associate(string name, Guid id, Relationship r, EntityReferenceCollection e) { _inner.Associate(name, id, r, e); }
            public void Disassociate(string name, Guid id, Relationship r, EntityReferenceCollection e) { _inner.Disassociate(name, id, r, e); }
        }
    }
}
