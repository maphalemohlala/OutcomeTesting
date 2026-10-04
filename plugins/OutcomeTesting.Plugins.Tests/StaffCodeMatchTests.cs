using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The staff code travels with the match (2026-09-22).
    ///
    /// MatchPerson is already the one place that decides which contact a case means, and it
    /// already refuses an ambiguous answer. Carrying the code back from the same resolution
    /// is what stops a second, weaker lookup growing beside it.
    /// </summary>
    public class StaffCodeMatchTests
    {
        private static void Contact(
            FakeOrganizationService service, string name, string email, string staffCode)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", name,
                "emailaddress1", email,
                "al_staffcode", staffCode,
                "statecode", 0);
        }

        [Fact]
        public void A_matched_person_carries_their_code()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", "4471");

            var match = NotificationOutbox.MatchPerson(service, "jane.adviser@example.com", "adviser");

            Assert.True(match.IsMatch);
            Assert.Equal("4471", match.StaffCode);
        }

        [Fact]
        public void A_matched_person_without_a_code_carries_none()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", null);

            var match = NotificationOutbox.MatchPerson(service, "jane.adviser@example.com", "adviser");

            Assert.True(match.IsMatch);
            Assert.True(string.IsNullOrEmpty(match.StaffCode));
        }

        [Fact]
        public void A_missing_email_carries_no_code_even_when_a_contact_shares_the_name()
        {
            // No name branch (AD-228): a blank email resolves to nobody no matter what the
            // row's name happens to match, so there is no code to report.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com", "8820");

            var match = NotificationOutbox.MatchPerson(service, null, "para-planner");

            Assert.False(match.IsMatch);
            Assert.Equal(NotificationOutbox.PersonMatchKind.NoEmail, match.Kind);
            Assert.True(string.IsNullOrEmpty(match.StaffCode));
        }

        [Fact]
        public void An_address_picks_out_one_of_two_same_named_contacts_and_brings_the_code()
        {
            // Two contacts share a display name; the email identifies exactly one of them,
            // and their code comes with it.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com", "8820");
            Contact(service, "Sam Jones", "s.jones@example.com", "9910");

            var match = NotificationOutbox.MatchPerson(service, "s.jones@example.com", "para-planner");

            Assert.True(match.IsMatch);
            Assert.Equal("9910", match.StaffCode);
        }
    }
}
