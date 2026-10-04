using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The para-planner is identified by their ADDRESS only, never their name (AD-228,
    /// owner 2026-10-02: "two people can have the same name").
    ///
    /// The extract carries <c>ParaplannerEmail</c> beside <c>AssignedBy</c>; the name is still
    /// mapped and still shown, but <see cref="NotificationOutbox.MatchPerson"/> never reads it
    /// - a display name describes a person, only the address identifies them.
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

            var match = NotificationOutbox.MatchParaplanner(service, "s.jones@example.com");

            Assert.True(match.IsMatch);
            Assert.Equal("s.jones@example.com", match.Email);
        }

        [Fact]
        public void The_address_decides_even_when_the_name_would_have_matched_somebody_else()
        {
            // The name is never read. A name that has since changed - or was typed
            // differently in the extract - has no vote on who the mailbox reaches.
            var service = new FakeOrganizationService();
            Contact(service, "Pat Paraplanner", "pat@example.com");
            Contact(service, "Sam Jones", "sam.jones@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, "sam.jones@example.com");

            Assert.True(match.IsMatch);
            Assert.Equal("sam.jones@example.com", match.Email);
        }

        [Fact]
        public void A_row_with_no_address_matches_nobody_even_though_the_name_would_have()
        {
            // The inverse of the old behaviour. Every case imported before this column
            // existed carries no address, and such a row now reaches nobody rather than
            // falling back to a name match.
            var service = new FakeOrganizationService();
            Contact(service, "Pat Paraplanner", "pat@example.com");

            var match = NotificationOutbox.MatchParaplanner(service, null);

            Assert.Equal(NotificationOutbox.PersonMatchKind.NoEmail, match.Kind);
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
            // ParaplannerEmail returns the stored address as-is (AD-228); it never resolves a
            // contact at all, so a contact who merely shares the display name is never
            // substituted. The letter goes to the address the firm supplied.
            var service = Case("new.starter@example.com", "Pat Paraplanner");
            Contact(service, "Pat Paraplanner", "someone.else@example.com");

            Assert.Equal(
                "new.starter@example.com",
                NotificationOutbox.ParaplannerEmail(
                    service, new EntityReference("al_outcomecase", CaseId)));
        }

        [Fact]
        public void A_matched_address_is_the_stored_value_itself()
        {
            // The stored email is used directly as the letter address (AD-228); a para-planner
            // need not be a contact at all (BR-009).
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
