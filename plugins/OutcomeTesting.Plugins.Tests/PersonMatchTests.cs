using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A person field resolves to a contact: by email where the field carries one, by name
    /// where it does not (project owner, 2026-09-20).
    ///
    /// <para>
    /// The rule is one sentence and the reason is one too. An address identifies somebody; a
    /// display name describes them. Two people share a name far more often than they share a
    /// mailbox, so where a field carries both, the email decides.
    /// </para>
    /// <para>
    /// The adviser carries both (<c>al_adviseremail</c>, <c>al_advisername</c>). The
    /// para-planner carries only a name, because the extract gives it no address - which is
    /// why the fallback is not a nicety here, it is the only path that field has.
    /// </para>
    /// </summary>
    public class PersonMatchTests
    {
        // ------------------------------------------------------------ which key is used

        [Fact]
        public void The_email_decides_when_the_field_carries_one()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Sam Adviser", "sam.adviser@example.com");
            Contact(service, "Someone Else", "someone.else@example.com");

            var match = NotificationOutbox.MatchAdviser(
                service, "someone.else@example.com", "Sam Adviser");

            // The name says one person and the email says another. The email wins.
            Assert.True(match.IsMatch);
            Assert.Equal("someone.else@example.com", match.Email);
        }

        [Fact]
        public void The_name_is_used_when_there_is_no_email()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Sam Adviser", "sam.adviser@example.com");

            var match = NotificationOutbox.MatchAdviser(service, null, "Sam Adviser");

            Assert.True(match.IsMatch);
            Assert.Equal("sam.adviser@example.com", match.Email);
        }

        [Fact]
        public void A_blank_email_falls_through_to_the_name_rather_than_matching_nothing()
        {
            // Whitespace is not an address. Treating it as one would search for a contact
            // whose email is "   " and report the adviser as unmatched with a name sitting
            // right there that would have resolved.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Adviser", "sam.adviser@example.com");

            Assert.True(NotificationOutbox.MatchAdviser(service, "   ", "Sam Adviser").IsMatch);
        }

        [Fact]
        public void The_para_planner_is_matched_by_name_because_it_has_no_email_to_use()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Pat Paraplanner", "pat@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, null, "Pat Paraplanner");

            Assert.True(match.IsMatch);
            Assert.Equal("pat@example.com", match.Email);
        }

        // ------------------------------------------------------------ the link

        [Fact]
        public void A_match_says_which_contact_it_was()
        {
            // The "link" half. A person field holds text; this is what says which record that
            // text turned out to be, so a caller can point at the person rather than repeat
            // their name.
            var service = new FakeOrganizationService();
            var contact = Contact(service, "Sam Adviser", "sam.adviser@example.com");

            var match = NotificationOutbox.MatchAdviser(service, "sam.adviser@example.com", null);

            Assert.NotNull(match.Contact);
            Assert.Equal("contact", match.Contact.LogicalName);
            Assert.Equal(contact.Id, match.Contact.Id);
        }

        [Fact]
        public void A_failure_points_at_nobody()
        {
            var service = new FakeOrganizationService();

            Assert.Null(NotificationOutbox.MatchAdviser(service, "nobody@example.com", null).Contact);
        }

        // ------------------------------------------------------------ the refusals

        [Fact]
        public void An_email_no_contact_holds_is_reported_as_the_email_it_was()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Sam Adviser", "sam.adviser@example.com");

            var match = NotificationOutbox.MatchAdviser(service, "nobody@example.com", null);

            Assert.False(match.IsMatch);
            // The value that failed is in the sentence, because an import report is read by a
            // person who has to go and fix the data.
            Assert.Contains("nobody@example.com", match.Reason);
            Assert.Contains("adviser", match.Reason);
        }

        [Fact]
        public void Two_contacts_sharing_an_address_are_refused_rather_than_guessed()
        {
            // Unique by convention, not by constraint: nothing in Dataverse stops two
            // contacts carrying one address, and picking the first would look certain.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Adviser", "shared@example.com");
            Contact(service, "Sam Adviser Junior", "shared@example.com");

            var match = NotificationOutbox.MatchAdviser(service, "shared@example.com", null);

            Assert.False(match.IsMatch);
            Assert.Equal(NotificationOutbox.PersonMatchKind.Ambiguous, match.Kind);
        }

        [Fact]
        public void An_inactive_contact_is_not_a_match()
        {
            var service = new FakeOrganizationService();
            var contact = Contact(service, "Sam Adviser", "sam.adviser@example.com");
            contact["statecode"] = 1;

            Assert.False(NotificationOutbox.MatchAdviser(service, "sam.adviser@example.com", null).IsMatch);
        }

        [Fact]
        public void A_field_carrying_neither_names_the_role_that_was_missing()
        {
            var service = new FakeOrganizationService();

            Assert.Contains("adviser", NotificationOutbox.MatchAdviser(service, null, null).Reason);
            Assert.Contains("para-planner", NotificationOutbox.MatchParaplanner(service, null, null).Reason);
        }

        // ------------------------------------------------------------ what it must not break

        [Fact]
        public void An_adviser_no_contact_holds_is_still_written_to()
        {
            // THE one that matters. Requiring a contact would mean an environment whose
            // advisers are not in the directory silently stopped receiving adviser letters,
            // and a lost letter is worse than one sent to an address the directory does not
            // happen to carry (AD-168 made the same judgement about a recipient override that
            // reaches nobody).
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            service.Seed("al_outcomecase", caseId,
                "al_casereference", "OT-1",
                "al_advisername", "Sam Adviser",
                "al_adviseremail", "sam.adviser@example.com",
                "statecode", 0);

            var email = NotificationRecipients.EmailFor(
                service,
                NotificationRecipients.KindAdviser,
                new EntityReference("al_outcomecase", caseId),
                null,
                null);

            Assert.Equal("sam.adviser@example.com", email);
        }

        [Fact]
        public void A_matched_adviser_is_written_to_at_the_address_the_contact_holds()
        {
            // Where the two disagree - the case carrying an address the contact has since
            // changed - the contact is the live record and wins.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Adviser", "sam.adviser@example.com");

            var caseId = Guid.NewGuid();
            service.Seed("al_outcomecase", caseId,
                "al_casereference", "OT-1",
                "al_advisername", "Sam Adviser",
                "al_adviseremail", "sam.adviser@example.com",
                "statecode", 0);

            var email = NotificationRecipients.EmailFor(
                service,
                NotificationRecipients.KindAdviser,
                new EntityReference("al_outcomecase", caseId),
                null,
                null);

            Assert.Equal("sam.adviser@example.com", email);
        }

        [Fact]
        public void A_case_naming_no_adviser_at_all_reaches_nobody()
        {
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            service.Seed("al_outcomecase", caseId, "al_casereference", "OT-1", "statecode", 0);

            Assert.Null(NotificationRecipients.EmailFor(
                service,
                NotificationRecipients.KindAdviser,
                new EntityReference("al_outcomecase", caseId),
                null,
                null));
        }

        // ------------------------------------------------------------ helper

        private static Entity Contact(FakeOrganizationService service, string name, string email)
        {
            return service.Seed("contact", Guid.NewGuid(),
                "fullname", name,
                "emailaddress1", email,
                "statecode", 0);
        }
    }
}
