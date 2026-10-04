using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The para-planner comes from the extract's AssignedBy, and a name that will never
    /// reach anybody is said out loud at import (AD-160, audit finding 7).
    ///
    /// <para>
    /// The mapping was reversed on 2026-09-20 by the project owner, with the conflict put to
    /// them explicitly: the 2026-09-14 direction had taken the para-planner from AssignedTo,
    /// while the written specification and the only extract in this repository both say
    /// AssignedBy. The first two tests would fail under either reading, which is the point -
    /// the previous reversal changed the code and left nothing behind that could say which
    /// way round it was meant to be.
    /// </para>
    /// </summary>
    public class ParaplannerMatchTests
    {
        // ------------------------------------------------------------------ the mapping

        [Fact]
        public void The_paraplanner_comes_from_assigned_by()
        {
            Assert.Equal("al_paraplanner", AttributeForHeader("AssignedBy"));
        }

        [Fact]
        public void Assigned_to_is_not_imported_at_all()
        {
            // It is who the task was assigned TO - the checker, in the sample extract - and
            // AD-113 settled that such a name never proved a case was allocated to anyone.
            // Absent rather than mapped elsewhere, so a re-import cannot overwrite whoever
            // is actually allocated.
            Assert.Null(AttributeForHeader("AssignedTo"));
        }

        private static string AttributeForHeader(string header)
        {
            foreach (var column in ImportRules.Columns)
            {
                if (string.Equals(column.Header, header, StringComparison.OrdinalIgnoreCase))
                {
                    return column.Attribute;
                }
            }

            return null;
        }

        // ------------------------------------------------------------------ the match
        //
        // Converted from name-matching to email-matching (AD-228, 2026-10-02): the
        // para-planner is identified by al_paraplanneremail only, and the generic MatchPerson
        // behaviours below (one match, no contact, ambiguous, inactive) apply the same way
        // whether the matched value is an email - which, since Task 1, is all it ever is.

        [Fact]
        public void Matches_one_active_contact_with_an_email()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "Sam Jones", "sam@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, "sam@example.com");

            Assert.True(match.IsMatch);
            Assert.Equal("sam@example.com", match.Email);
        }

        [Fact]
        public void Says_when_the_row_carries_no_email()
        {
            var match = NotificationOutbox.MatchParaplanner(new FakeOrganizationService(), "   ");

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoEmail, match.Kind);
            Assert.Contains("no para-planner email", match.Reason);
        }

        [Fact]
        public void Says_when_no_contact_carries_the_email_and_names_the_value()
        {
            // The value has to appear in the reason. "Para-planner unmatched" on its own
            // tells an administrator nothing they can go and fix.
            var match = NotificationOutbox.MatchParaplanner(new FakeOrganizationService(), "nobody@example.com");

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoContact, match.Kind);
            Assert.Contains("nobody@example.com", match.Reason);
        }

        [Fact]
        public void Says_when_two_contacts_share_the_email()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "J Smith", "shared@example.com");
            SeedContact(service, "J Smith Two", "shared@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, "shared@example.com");

            Assert.Equal(NotificationOutbox.PersonMatchKind.Ambiguous, match.Kind);
            Assert.Contains("shared@example.com", match.Reason);
        }

        [Fact]
        public void An_inactive_contact_sharing_the_email_does_not_make_a_live_match_ambiguous()
        {
            // A para-planner who has left. Their old mailbox is not where a live case
            // outcome should go, and their row must not block the person who replaced them.
            var service = new FakeOrganizationService();
            service.Seed("contact", Guid.NewGuid(), "fullname", "Former Planner",
                "emailaddress1", "pat@example.com", "statecode", new OptionSetValue(1));
            SeedContact(service, "Pat Paraplanner", "pat@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, "pat@example.com");

            Assert.True(match.IsMatch);
            Assert.Equal("pat@example.com", match.Email);
        }

        private static void SeedContact(FakeOrganizationService service, string name, string email)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", name, "emailaddress1", email, "statecode", new OptionSetValue(0));
        }
    }
}
