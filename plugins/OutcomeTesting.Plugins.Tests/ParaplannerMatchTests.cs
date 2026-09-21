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

        [Fact]
        public void Matches_one_active_contact_with_an_email()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "Sam Jones", "sam@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, null, "Sam Jones");

            Assert.True(match.IsMatch);
            Assert.Equal("sam@example.com", match.Email);
        }

        [Fact]
        public void Says_when_the_row_named_nobody()
        {
            var match = NotificationOutbox.MatchParaplanner(new FakeOrganizationService(), null, "   ");

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoName, match.Kind);
            Assert.Contains("names no para-planner", match.Reason);
        }

        [Fact]
        public void Says_when_no_contact_carries_the_name_and_names_the_value()
        {
            // The value has to appear in the reason. "Para-planner unmatched" on its own
            // tells an administrator nothing they can go and fix.
            var match = NotificationOutbox.MatchParaplanner(new FakeOrganizationService(), null, "Nobody Here");

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoContact, match.Kind);
            Assert.Contains("Nobody Here", match.Reason);
        }

        [Fact]
        public void Says_when_two_people_share_the_name()
        {
            var service = new FakeOrganizationService();
            SeedContact(service, "J Smith", "first@example.com");
            SeedContact(service, "J Smith", "second@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, null, "J Smith");

            Assert.Equal(NotificationOutbox.PersonMatchKind.Ambiguous, match.Kind);
            Assert.Contains("J Smith", match.Reason);
        }

        [Fact]
        public void Says_when_the_one_match_has_no_work_email()
        {
            var service = new FakeOrganizationService();
            service.Seed("contact", Guid.NewGuid(),
                "fullname", "Sam Jones", "statecode", new OptionSetValue(0));

            var match = NotificationOutbox.MatchParaplanner(service, null, "Sam Jones");

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoEmail, match.Kind);
            Assert.Contains("no work email", match.Reason);
        }

        [Fact]
        public void Two_of_a_name_is_ambiguous_even_when_only_one_can_be_emailed()
        {
            // DELIBERATELY STRICTER than before 2026-09-20. The old query filtered on the
            // email being present, so this resolved to the one that had one and sent. A
            // missing email address is not evidence about which Sam Jones the case means.
            var service = new FakeOrganizationService();
            SeedContact(service, "Sam Jones", "sam@example.com");
            service.Seed("contact", Guid.NewGuid(),
                "fullname", "Sam Jones", "statecode", new OptionSetValue(0));

            var match = NotificationOutbox.MatchParaplanner(service, null, "Sam Jones");

            Assert.Equal(NotificationOutbox.PersonMatchKind.Ambiguous, match.Kind);
            Assert.Null(match.Email);
        }

        [Fact]
        public void An_inactive_namesake_does_not_make_a_live_match_ambiguous()
        {
            // A para-planner who has left. Their old mailbox is not where a live case
            // outcome should go, and their row must not block the person who replaced them.
            var service = new FakeOrganizationService();
            service.Seed("contact", Guid.NewGuid(), "fullname", "Sam Jones",
                "emailaddress1", "left@example.com", "statecode", new OptionSetValue(1));
            SeedContact(service, "Sam Jones", "current@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, null, "Sam Jones");

            Assert.True(match.IsMatch);
            Assert.Equal("current@example.com", match.Email);
        }

        // ------------------------------------------------- the send path did not loosen

        [Theory]
        [InlineData(NotificationOutbox.PersonMatchKind.NoName)]
        [InlineData(NotificationOutbox.PersonMatchKind.NoContact)]
        [InlineData(NotificationOutbox.PersonMatchKind.Ambiguous)]
        [InlineData(NotificationOutbox.PersonMatchKind.NoEmail)]
        public void No_email_is_returned_for_anything_that_is_not_a_match(
            NotificationOutbox.PersonMatchKind kind)
        {
            // Splitting one null into four reasons must not turn any of them into a send.
            // This is the negative: the reporting is richer, the addressing is not.
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            var name = NameProducing(service, kind);
            service.Seed("al_outcomecase", caseId, "al_paraplanner", name);

            Assert.Null(NotificationOutbox.ParaplannerEmail(
                service, new EntityReference("al_outcomecase", caseId)));
        }

        /// <summary>Seeds whatever contacts produce <paramref name="kind"/>, and returns the name.</summary>
        private static string NameProducing(
            FakeOrganizationService service, NotificationOutbox.PersonMatchKind kind)
        {
            switch (kind)
            {
                case NotificationOutbox.PersonMatchKind.NoName:
                    return null;
                case NotificationOutbox.PersonMatchKind.NoContact:
                    return "Nobody Here";
                case NotificationOutbox.PersonMatchKind.Ambiguous:
                    SeedContact(service, "J Smith", "first@example.com");
                    SeedContact(service, "J Smith", "second@example.com");
                    return "J Smith";
                default:
                    service.Seed("contact", Guid.NewGuid(),
                        "fullname", "No Mailbox", "statecode", new OptionSetValue(0));
                    return "No Mailbox";
            }
        }

        private static void SeedContact(FakeOrganizationService service, string name, string email)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", name, "emailaddress1", email, "statecode", new OptionSetValue(0));
        }
    }
}
