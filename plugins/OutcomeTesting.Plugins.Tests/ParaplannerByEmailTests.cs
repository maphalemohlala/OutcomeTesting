using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The para-planner is identified by their ADDRESS, not their name (project owner,
    /// 2026-09-21: "the paraplanner email field should now be used to map the paraplanner ...
    /// emails are more safe than names").
    ///
    /// The extract carries <c>ParaplannerEmail</c> beside <c>AssignedBy</c>, and until it was
    /// mapped the para-planner was the one person on a case with no address at all - so every
    /// route to them resolved a display name and gave up whenever two contacts answered to it.
    /// That is the same judgement <c>AdviserEmail</c> settled on 2026-09-20, and
    /// <see cref="NotificationOutbox.MatchPerson"/> has read email first and name second ever
    /// since; the para-planner simply had nothing to hand it.
    ///
    /// The name is still mapped, still shown, and still the fallback.
    /// </summary>
    public class ParaplannerByEmailTests
    {
        private static readonly Guid CaseId = Guid.Parse("bbbbbbbb-1111-4111-8111-bbbbbbbbbbbb");

        private static void Contact(FakeOrganizationService service, string name, string email)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", name,
                "emailaddress1", email,
                "statecode", 0);
        }

        private static FakeOrganizationService Case(string email, string name)
        {
            var service = new FakeOrganizationService();
            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "OT-1",
                ImportRules.ParaplannerEmailAttribute, email,
                "al_paraplanner", name,
                "statecode", 0);
            return service;
        }

        private static Entity ExportCase(string email, string name)
        {
            var row = new Entity("al_outcomecase", CaseId);
            if (email != null) { row[ImportRules.ParaplannerEmailAttribute] = email; }
            if (name != null) { row["al_paraplanner"] = name; }
            return row;
        }

        // --- the extract's column -------------------------------------------------------

        [Fact]
        public void The_extract_maps_the_address_to_the_case()
        {
            // The header the supplied workbook actually carries, checked against the file
            // rather than assumed: "Pre-Advice Check Required Task 14-09 (1).xlsx" has
            // ParaplannerEmail in column M, immediately after AssignedBy in column L.
            var mapped = Array.Find(
                ImportRules.Columns, column => column.Header == "ParaplannerEmail");

            Assert.NotNull(mapped);
            Assert.Equal(ImportRules.ParaplannerEmailAttribute, mapped.Attribute);
        }

        [Fact]
        public void The_name_is_still_mapped_alongside_it()
        {
            // It is what a person reads on the case, and the fallback for a row with no
            // address. Replacing the name would have been the wrong reading of "use the
            // email to map the paraplanner".
            var mapped = Array.Find(
                ImportRules.Columns, column => column.Header == "AssignedBy");

            Assert.NotNull(mapped);
            Assert.Equal(ImportRules.ParaplannerAttribute, mapped.Attribute);
        }

        // --- matching --------------------------------------------------------------------

        [Fact]
        public void Two_contacts_of_one_name_are_now_told_apart_by_the_address()
        {
            // THE one that matters, and the whole reason for the change. Before the address
            // was mapped this was Ambiguous: a case naming "Sam Jones" reached nobody, no
            // letter was sent, and Trail Light column D exported blank.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com");
            Contact(service, "Sam Jones", "s.jones@example.com");

            var match = NotificationOutbox.MatchParaplanner(
                service, "s.jones@example.com", "Sam Jones");

            Assert.True(match.IsMatch);
            Assert.Equal("s.jones@example.com", match.Email);
        }

        [Fact]
        public void The_address_decides_even_when_the_name_would_have_matched_somebody_else()
        {
            // Email first, name second. A name that has since changed - or was typed
            // differently in the extract - must not outvote the mailbox.
            var service = new FakeOrganizationService();
            Contact(service, "Pat Paraplanner", "pat@example.com");
            Contact(service, "Sam Jones", "sam.jones@example.com");

            var match = NotificationOutbox.MatchParaplanner(
                service, "sam.jones@example.com", "Pat Paraplanner");

            Assert.True(match.IsMatch);
            Assert.Equal("sam.jones@example.com", match.Email);
        }

        [Fact]
        public void A_row_with_no_address_still_falls_back_to_the_name()
        {
            // Every case imported before this column existed carries no address, and they
            // must keep working exactly as they did.
            var service = new FakeOrganizationService();
            Contact(service, "Pat Paraplanner", "pat@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, null, "Pat Paraplanner");

            Assert.True(match.IsMatch);
            Assert.Equal("pat@example.com", match.Email);
        }

        // --- the letter ------------------------------------------------------------------

        [Fact]
        public void The_letter_goes_to_the_extracts_address_when_no_contact_holds_it()
        {
            // AD-168, which the adviser's letter already applies: a lost letter is worse than
            // one sent to an address the directory does not happen to carry. The para-planner
            // could not make that judgement until there was an address to fall back to.
            var service = Case("new.starter@example.com", "New Starter");

            Assert.Equal(
                "new.starter@example.com",
                NotificationOutbox.ParaplannerEmail(
                    service, new EntityReference("al_outcomecase", CaseId)));
        }

        [Fact]
        public void An_address_that_reaches_nobody_is_not_overruled_by_a_name_that_does()
        {
            // The safety property the change was asked for. MatchPerson searches by the
            // address OR the name, never both: given an address it looks up that and stops,
            // so a contact who merely shares the display name is never substituted. The
            // letter goes to the address the firm supplied.
            //
            // Worth stating out loud, because the obvious reading of "email first, name
            // second" is that the name is tried when the email misses. It is not, and that is
            // deliberate - falling through would reintroduce exactly the name-collision risk
            // the address was mapped to remove.
            var service = Case("new.starter@example.com", "Pat Paraplanner");
            Contact(service, "Pat Paraplanner", "someone.else@example.com");

            Assert.Equal(
                "new.starter@example.com",
                NotificationOutbox.ParaplannerEmail(
                    service, new EntityReference("al_outcomecase", CaseId)));
        }

        [Fact]
        public void A_matched_address_is_resolved_through_the_contact()
        {
            // Where the address does reach somebody, the contact is what answers - the same
            // path the adviser's letter takes, so a future change to how a contact's address
            // is chosen reaches both.
            var service = Case("pat@example.com", "Pat Paraplanner");
            Contact(service, "Pat Paraplanner", "pat@example.com");

            Assert.Equal(
                "pat@example.com",
                NotificationOutbox.ParaplannerEmail(
                    service, new EntityReference("al_outcomecase", CaseId)));
        }

        [Fact]
        public void A_case_naming_nobody_at_all_still_reaches_nobody()
        {
            var service = Case(null, null);

            Assert.Null(NotificationOutbox.ParaplannerEmail(
                service, new EntityReference("al_outcomecase", CaseId)));
        }

        // --- Trail Light column D --------------------------------------------------------

        [Fact]
        public void The_export_writes_the_address_the_extract_carried()
        {
            // Column D is the para-planner's email (AD-183). A directory gap is not a reason
            // to export a blank where the firm supplied a real address.
            var service = new FakeOrganizationService();

            Assert.Equal(
                "sam.paraplanner@example.com",
                GenerateExportPlugin.ParaplannerEmail(
                    service, ExportCase("sam.paraplanner@example.com", "Sam Paraplanner")));
        }

        [Fact]
        public void The_export_no_longer_blanks_column_d_for_two_contacts_of_one_name()
        {
            // What AD-183 had to accept an hour earlier and no longer does: refusing to guess
            // between two Sam Joneses left the cell empty. The address is not a guess.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com");
            Contact(service, "Sam Jones", null);

            Assert.Equal(
                "sam.jones@example.com",
                GenerateExportPlugin.ParaplannerEmail(
                    service, ExportCase("sam.jones@example.com", "Sam Jones")));
        }

        [Fact]
        public void The_export_still_blanks_column_d_when_there_is_nothing_to_write()
        {
            // A name that reaches nobody and no address is still an empty cell. AD-039 reads
            // by position, so column D is an email on every row or the file lies about the
            // rows where it is something else.
            var service = new FakeOrganizationService();

            Assert.Null(GenerateExportPlugin.ParaplannerEmail(
                service, ExportCase(null, "Nobody Here")));
        }
    }
}
