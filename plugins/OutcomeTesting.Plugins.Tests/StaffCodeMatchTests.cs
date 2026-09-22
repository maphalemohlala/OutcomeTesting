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

            var match = NotificationOutbox.MatchPerson(
                service, "jane.adviser@example.com", null, "adviser");

            Assert.True(match.IsMatch);
            Assert.Equal("4471", match.StaffCode);
        }

        [Fact]
        public void A_matched_person_without_a_code_carries_none()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", null);

            var match = NotificationOutbox.MatchPerson(
                service, "jane.adviser@example.com", null, "adviser");

            Assert.True(match.IsMatch);
            Assert.True(string.IsNullOrEmpty(match.StaffCode));
        }

        [Fact]
        public void An_ambiguous_name_carries_no_code()
        {
            // Two contacts of one name resolve to nobody, so there is no code to report -
            // the whole reason this matching fails loudly rather than approximately.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com", "8820");
            Contact(service, "Sam Jones", "s.jones@example.com", "9910");

            var match = NotificationOutbox.MatchPerson(service, null, "Sam Jones", "para-planner");

            Assert.False(match.IsMatch);
            Assert.True(string.IsNullOrEmpty(match.StaffCode));
        }

        [Fact]
        public void An_address_beats_a_shared_name_and_still_brings_the_code()
        {
            // This is the case that earns the ParaplannerEmail import mapping its keep: two
            // people share the name, so a name lookup would refuse, but the address the
            // extract carried picks out one of them and their code comes with it.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com", "8820");
            Contact(service, "Sam Jones", "s.jones@example.com", "9910");

            var match = NotificationOutbox.MatchPerson(
                service, "s.jones@example.com", "Sam Jones", "para-planner");

            Assert.True(match.IsMatch);
            Assert.Equal("9910", match.StaffCode);
        }
    }
}
