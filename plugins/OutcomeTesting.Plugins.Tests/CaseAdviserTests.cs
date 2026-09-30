using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The email a case's adviser is known by (project owner, 2026-09-30).
    ///
    /// <para>
    /// TEST case 256617620 named Adam Strumidlo as its adviser and carried no adviser email: the
    /// 29 Sep import file had names and no addresses. Remediation found him by NAME, so he could
    /// complete the actions; sign-off looked the T&amp;C Manager up by EMAIL, so nobody could
    /// sign them off - Adam included, although he is mapped as his own manager. 24 cases were in
    /// that state. The two keys for one person are made to agree here: the stored email where
    /// there is one, otherwise the email of the one active contact the name resolves to - the
    /// "email first, name second" rule NotificationOutbox.MatchPerson already applies.
    /// </para>
    /// </summary>
    public class CaseAdviserTests
    {
        private static readonly Guid CaseId = Guid.Parse("caad0000-1111-4111-8111-111111111111");

        [Fact]
        public void Uses_the_stored_adviser_email_when_the_case_has_one()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "Adam Strumidlo", "adam@example.com");

            Assert.Equal("stored@example.com", CaseAdviser.EmailFor(service, "  stored@example.com ", "Adam Strumidlo"));
        }

        [Fact]
        public void Falls_back_to_the_email_of_the_one_contact_the_adviser_name_resolves_to()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "Adam Strumidlo", "adam@example.com");

            Assert.Equal("adam@example.com", CaseAdviser.EmailFor(service, null, "Adam Strumidlo"));
            Assert.Equal("adam@example.com", CaseAdviser.EmailFor(service, "   ", " Adam Strumidlo "));
        }

        [Fact]
        public void Never_guesses_between_two_contacts_of_the_same_name()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "Sam Jones", "sam.one@example.com");
            SeedContact(service, "Sam Jones", "sam.two@example.com");

            Assert.Null(CaseAdviser.EmailFor(service, null, "Sam Jones"));
        }

        [Fact]
        public void Has_no_email_for_a_case_that_names_nobody()
        {
            var service = new FakeOrganizationService();

            Assert.Null(CaseAdviser.EmailFor(service, null, null));
            Assert.Null(CaseAdviser.EmailFor(service, null, "Nobody Known"));
        }

        [Fact]
        public void Routes_a_case_with_only_an_adviser_name_to_the_manager_mapped_to_that_adviser()
        {
            // The TEST case, as it was: name, no email, the adviser mapped to himself.
            var service = new FakeOrganizationService();
            var adam = SeedContact(service, "Adam Strumidlo", "adam@example.com");
            service.Seed("al_outcomecase", CaseId,
                CaseAdviser.NameAttr, "Adam Strumidlo",
                CaseAdviser.EmailAttr, null);
            service.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                TcManagerRouting.MappingEmailAttr, "adam@example.com",
                TcManagerRouting.ManagerAttr, adam);

            var routing = TcManagerRouting.ForCase(service, new EntityReference("al_outcomecase", CaseId));

            Assert.True(routing.IsRouted);
            Assert.Equal(adam.Id, routing.Manager.Id);

            // And the signatory gate, which is what refused Adam, now lets him through.
            SupervisorMapping.EnsureManagesCase(
                service, adam.Id, new EntityReference("al_outcomecase", CaseId), "Signing a case off");
        }

        [Fact]
        public void A_changed_adviser_brings_their_own_email_with_them()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "New Adviser", "new@example.com");
            var before = Before("Old Adviser", "old@example.com");
            var update = new Entity("al_outcomecase", CaseId) { [CaseAdviser.NameAttr] = "New Adviser" };
            var changes = new List<string>();

            CaseAdviser.FollowName(service, before, update, changes);

            Assert.Equal("new@example.com", update.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            Assert.Contains(changes, c => c.Contains("old@example.com") && c.Contains("new@example.com"));
        }

        [Fact]
        public void A_changed_adviser_who_matches_nobody_clears_the_old_email_rather_than_keeping_it()
        {
            // Keeping it would send the sign-off to the PREVIOUS adviser's T&C Manager.
            var service = new FakeOrganizationService();
            var before = Before("Old Adviser", "old@example.com");
            var update = new Entity("al_outcomecase", CaseId) { [CaseAdviser.NameAttr] = "Unknown Person" };
            var changes = new List<string>();

            CaseAdviser.FollowName(service, before, update, changes);

            Assert.True(update.Contains(CaseAdviser.EmailAttr));
            Assert.Null(update.GetAttributeValue<string>(CaseAdviser.EmailAttr));
        }

        [Fact]
        public void The_same_adviser_sent_again_leaves_the_email_alone()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "Adam Strumidlo", "adam.other@example.com");
            var before = Before("Adam Strumidlo", "adam@example.com");
            var update = new Entity("al_outcomecase", CaseId) { [CaseAdviser.NameAttr] = " adam strumidlo " };
            var changes = new List<string>();

            CaseAdviser.FollowName(service, before, update, changes);

            Assert.False(update.Contains(CaseAdviser.EmailAttr));
            Assert.Empty(changes);
        }

        [Fact]
        public void An_edit_that_does_not_touch_the_adviser_leaves_the_email_alone()
        {
            var service = new FakeOrganizationService();
            var update = new Entity("al_outcomecase", CaseId) { ["al_clientname"] = "Someone" };

            CaseAdviser.FollowName(service, Before("Adam Strumidlo", null), update, new List<string>());

            Assert.False(update.Contains(CaseAdviser.EmailAttr));
        }

        [Fact]
        public void An_imported_row_with_an_adviser_name_and_no_email_is_given_the_advisers_email()
        {
            // The 29 Sep import file: names, no addresses.
            var service = new FakeOrganizationService();
            SeedContact(service, "Adam Strumidlo", "adam@example.com");
            var record = new Entity("al_outcomecase") { [CaseAdviser.NameAttr] = "Adam Strumidlo" };

            CaseAdviser.FillMissingEmail(service, record);

            Assert.Equal("adam@example.com", record.GetAttributeValue<string>(CaseAdviser.EmailAttr));
        }

        [Fact]
        public void An_imported_row_keeps_the_adviser_email_the_file_gave()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "Adam Strumidlo", "adam@example.com");
            var record = new Entity("al_outcomecase")
            {
                [CaseAdviser.NameAttr] = "Adam Strumidlo",
                [CaseAdviser.EmailAttr] = "from.the.file@example.com",
            };

            CaseAdviser.FillMissingEmail(service, record);

            Assert.Equal("from.the.file@example.com", record.GetAttributeValue<string>(CaseAdviser.EmailAttr));
        }

        [Fact]
        public void An_imported_row_whose_adviser_matches_nobody_is_left_without_an_email()
        {
            var service = new FakeOrganizationService();
            var record = new Entity("al_outcomecase") { [CaseAdviser.NameAttr] = "Nobody Known" };

            CaseAdviser.FillMissingEmail(service, record);

            Assert.Null(record.GetAttributeValue<string>(CaseAdviser.EmailAttr));
        }

        private static Entity Before(string name, string email)
        {
            return new Entity("al_outcomecase", CaseId)
            {
                [CaseAdviser.NameAttr] = name,
                [CaseAdviser.EmailAttr] = email,
            };
        }

        private static EntityReference SeedContact(FakeOrganizationService service, string name, string email)
        {
            return service.Seed("contact", Guid.NewGuid(),
                "fullname", name, "emailaddress1", email,
                "statecode", new OptionSetValue(0)).ToEntityReference();
        }
    }
}
