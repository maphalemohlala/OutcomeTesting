using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A template that goes to several people, and the PROD fault that started it
    /// (2026-10-15): three letters attached to Remediation assigned, with template codes of
    /// 59 to 67 characters, were never sent. The outbox code took the template code whole and
    /// overran the 100 characters al_notificationcode holds; the create failed inside a catch
    /// and nothing said so.
    /// </summary>
    public class MultipleRecipientsTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee3333-3333-4333-8333-333333333333");
        private static readonly Guid ReviewId = Guid.Parse("7e333333-3333-4333-8333-333333333333");

        private const string LongCode = "REMEDIAL-NEEDED-INSUFFICIENT-EVIDENCE-OR-PORENTIAL-HARM-T-C-MANAGER";

        [Fact]
        public void A_letter_with_a_long_code_is_still_queued_and_every_code_fits_the_column()
        {
            var service = Case();
            var ian = Contact(service, "Ian Gibbs", "ian@example.com");
            Template(service, LongCode, NotificationOutbox.EventRemediationAssigned, null, ian.Id.ToString());

            QueueRemediation(service);

            var sent = Sent(service);
            Assert.Contains(sent, n => n.Recipient == "ian@example.com");
            Assert.All(sent, n => Assert.True(n.Code.Length <= 100, n.Code));
        }

        [Fact]
        public void A_letter_of_your_own_goes_to_every_role_and_contact_chosen()
        {
            var service = Case();
            var ian = Contact(service, "Ian Gibbs", "ian@example.com");
            var zoe = Contact(service, "Zoe Ramwell", "zoe@example.com");
            Template(service, "TELL-THREE", NotificationOutbox.EventRemediationAssigned,
                NotificationRecipients.KindAdviser.ToString(), ian.Id + "," + zoe.Id);

            QueueRemediation(service);

            var custom = Sent(service).Where(n => n.Subject == "Custom").ToList();
            Assert.Equal(
                new[] { "adviser@example.com", "ian@example.com", "zoe@example.com" },
                custom.Select(n => n.Recipient).OrderBy(r => r).ToArray());
            Assert.Equal(3, custom.Select(n => n.Code).Distinct().Count());
        }

        [Fact]
        public void A_built_in_letter_with_several_chosen_goes_to_each_and_not_the_default()
        {
            var service = Case();
            var ian = Contact(service, "Ian Gibbs", "ian@example.com");
            Template(service, NotificationTemplates.CasePassed, null,
                NotificationRecipients.KindAdviser.ToString(), ian.Id.ToString(), subject: null);

            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventCasePassed,
                "al_outcomecase", CaseId, "original@example.com", "Subject", "Body",
                NotificationTemplates.CasePassed);

            Assert.Equal(
                new[] { "adviser@example.com", "ian@example.com" },
                Sent(service).Select(n => n.Recipient).OrderBy(r => r).ToArray());
        }

        [Fact]
        public void Queuing_the_same_event_again_sends_nobody_twice()
        {
            var service = Case();
            var ian = Contact(service, "Ian Gibbs", "ian@example.com");
            Template(service, LongCode, NotificationOutbox.EventRemediationAssigned,
                NotificationRecipients.KindAdviser.ToString(), ian.Id.ToString());

            QueueRemediation(service);
            var first = Sent(service).Count;
            QueueRemediation(service);

            Assert.Equal(first, Sent(service).Count);
        }

        [Fact]
        public void The_para_planner_copy_does_not_send_the_letters_of_your_own_again()
        {
            // The para-planner's fixed copy is queued at the same event with an occurrence of
            // its own and no template code. Letters of your own ride with the template letter.
            var service = Case();
            var ian = Contact(service, "Ian Gibbs", "ian@example.com");
            Template(service, "TELL-IAN", NotificationOutbox.EventRemediationAssigned, null, ian.Id.ToString());

            QueueRemediation(service);
            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventRemediationAssigned,
                "al_reviewinstance", ReviewId, "planner@example.com", "Copy", "Body",
                null, ParaplannerRemediationLetter.Occurrence);

            Assert.Single(Sent(service), n => n.Recipient == "ian@example.com");
        }

        [Fact]
        public void One_unreachable_recipient_is_recorded_and_the_others_still_go()
        {
            var service = Case();
            var ian = Contact(service, "Ian Gibbs", "ian@example.com");
            var nobody = Contact(service, "No Email", null);
            Template(service, "TELL-TWO", NotificationOutbox.EventRemediationAssigned,
                null, ian.Id + "," + nobody.Id);

            QueueRemediation(service);

            Assert.Contains(Sent(service), n => n.Recipient == "ian@example.com" && n.Pending);
            var failed = Sent(service).Single(n => !n.Pending);
            Assert.Contains("Not sent to No Email", failed.Reason);
            Assert.Contains("no email address", failed.Reason);
        }

        // ------------------------------------------------------------ reading the columns

        [Fact]
        public void The_single_columns_and_the_lists_are_read_together()
        {
            var contact = Guid.NewGuid();
            var row = new Entity(NotificationTemplates.TemplateEntity);
            row[NotificationTemplateRows.RecipientKindAttr] = new OptionSetValue(NotificationRecipients.KindTcManager);
            row[NotificationTemplateRows.RecipientKindsAttr] =
                NotificationRecipients.KindAdviser + "," + NotificationRecipients.KindTcManager;
            row[NotificationTemplateRows.RecipientContactIdsAttr] = contact + ";" + contact;

            var choice = NotificationTemplateRows.ReadRecipients(row);

            Assert.Equal(
                new[] { NotificationRecipients.KindTcManager, NotificationRecipients.KindAdviser },
                choice.Kinds.ToArray());
            Assert.Single(choice.Contacts);
            Assert.Empty(choice.Unreadable);
        }

        [Fact]
        public void Text_that_is_not_a_recipient_is_kept_aside_for_the_guard_to_refuse()
        {
            var row = new Entity(NotificationTemplates.TemplateEntity);
            row[NotificationTemplateRows.RecipientKindsAttr] = "42," + NotificationRecipients.KindContact;
            row[NotificationTemplateRows.RecipientContactIdsAttr] = "not-a-guid";

            var choice = NotificationTemplateRows.ReadRecipients(row);

            Assert.True(choice.IsEmpty);
            Assert.Equal(3, choice.Unreadable.Count);
        }

        // ------------------------------------------------------------ the guard

        [Fact]
        public void A_contact_with_no_email_is_refused_when_the_letter_is_saved()
        {
            var service = new FakeOrganizationService();
            var nobody = Contact(service, "No Email", null);

            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Guard(service, Custom(contactIds: nobody.Id.ToString())));

            Assert.Contains("No Email has no email address", refusal.Message);
        }

        [Fact]
        public void A_letter_of_your_own_with_nobody_chosen_is_refused()
        {
            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Guard(new FakeOrganizationService(), Custom()));

            Assert.Contains("has nobody to go to", refusal.Message);
        }

        [Fact]
        public void A_list_with_something_unrecognisable_in_it_is_refused()
        {
            var refusal = Assert.Throws<InvalidPluginExecutionException>(
                () => Guard(new FakeOrganizationService(), Custom(kinds: "adviser")));

            Assert.Contains("not somebody this system can work out", refusal.Message);
        }

        [Fact]
        public void Roles_and_reachable_contacts_are_accepted()
        {
            var service = new FakeOrganizationService();
            var ian = Contact(service, "Ian Gibbs", "ian@example.com");

            Guard(service, Custom(
                kinds: NotificationRecipients.KindAdviser + "," + NotificationRecipients.KindTcManager,
                contactIds: ian.Id.ToString()));
        }

        // ------------------------------------------------------------ helpers

        private static void QueueRemediation(FakeOrganizationService service)
        {
            NotificationOutbox.Queue(
                service, Guid.NewGuid(), NotificationOutbox.EventRemediationAssigned,
                "al_reviewinstance", ReviewId, "adviser@example.com", "Remedial", "Body",
                NotificationTemplates.RemediationHarm);
        }

        private static FakeOrganizationService Case()
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "256617620",
                "al_advisername", "Adviser User",
                "al_adviseremail", "adviser@example.com",
                "statecode", 0);
            service.Seed("al_reviewinstance", ReviewId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            return service;
        }

        private static Entity Contact(FakeOrganizationService service, string name, string email)
        {
            var contact = service.Seed("contact", Guid.NewGuid(), "fullname", name, "statecode", 0);
            if (email != null) contact["emailaddress1"] = email;
            return contact;
        }

        private static void Template(
            FakeOrganizationService service,
            string code,
            int? eventValue,
            string kinds,
            string contactIds,
            string subject = "Custom")
        {
            var row = service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, code,
                "statecode", 0);

            if (eventValue.HasValue) row[NotificationTemplateRows.EventAttr] = new OptionSetValue(eventValue.Value);
            if (kinds != null) row[NotificationTemplateRows.RecipientKindsAttr] = kinds;
            if (contactIds != null) row[NotificationTemplateRows.RecipientContactIdsAttr] = contactIds;
            if (subject != null)
            {
                row[NotificationTemplates.SubjectAttr] = subject;
                row[NotificationTemplates.BodyAttr] = "<p>Case {{reference}}.</p>";
            }
        }

        private static Entity Custom(string kinds = null, string contactIds = null)
        {
            var row = new Entity(NotificationTemplates.TemplateEntity);
            row[NotificationTemplates.CodeAttr] = "TELL-SOMEBODY";
            row[NotificationTemplates.SubjectAttr] = "Subject";
            row[NotificationTemplates.BodyAttr] = "<p>Body</p>";
            row[NotificationTemplateRows.EventAttr] = new OptionSetValue(NotificationOutbox.EventRemediationAssigned);
            if (kinds != null) row[NotificationTemplateRows.RecipientKindsAttr] = kinds;
            if (contactIds != null) row[NotificationTemplateRows.RecipientContactIdsAttr] = contactIds;
            return row;
        }

        private static void Guard(FakeOrganizationService service, Entity row)
        {
            var provider = new FakeServiceProvider(service);
            provider.Context.InputParameters["Target"] = row;
            provider.Context.MessageName = "Create";

            new NotificationTemplateGuardPlugin(null, null).Execute(provider);
        }

        private sealed class Queued
        {
            public string Recipient { get; set; }
            public string Subject { get; set; }
            public string Code { get; set; }
            public bool Pending { get; set; }
            public string Reason { get; set; }
        }

        private static List<Queued> Sent(FakeOrganizationService service)
        {
            var query = new QueryExpression(NotificationOutbox.NotificationEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_recipientemail", "al_subject", "al_notificationcode", "al_status", "al_failurereason"),
            };

            return service.RetrieveMultiple(query).Entities
                .Select(e => new Queued
                {
                    Recipient = e.GetAttributeValue<string>("al_recipientemail"),
                    Subject = e.GetAttributeValue<string>("al_subject"),
                    Code = e.GetAttributeValue<string>("al_notificationcode"),
                    Pending = e.GetAttributeValue<OptionSetValue>("al_status").Value
                        == NotificationOutbox.StatusPending,
                    Reason = e.GetAttributeValue<string>("al_failurereason"),
                })
                .ToList();
        }
    }
}
