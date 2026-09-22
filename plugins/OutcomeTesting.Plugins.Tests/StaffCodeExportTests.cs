using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Trail Light columns B and D carry CODES again (project owner, 2026-09-22), Trailight
    /// having said they could not accommodate the emails put there on 2026-09-21 (AD-183).
    ///
    /// The codes come from the registry, not from the case: al_advisercode and
    /// al_paraplannercode were never filled by anything, which is why the columns were empty
    /// and why emails went in. The case's own columns are no longer read.
    /// </summary>
    public class StaffCodeExportTests
    {
        private static Entity Case(string adviserEmail, string paraplannerEmail, string paraplanner)
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            if (adviserEmail != null) { row["al_adviseremail"] = adviserEmail; }
            if (paraplannerEmail != null) { row["al_paraplanneremail"] = paraplannerEmail; }
            if (paraplanner != null) { row["al_paraplanner"] = paraplanner; }
            return row;
        }

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
        public void The_adviser_code_comes_from_the_registry()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", "4471");

            Assert.Equal(
                "4471",
                GenerateExportPlugin.AdviserCode(
                    service, Case("jane.adviser@example.com", null, null)));
        }

        [Fact]
        public void The_para_planner_code_comes_from_the_registry_by_address()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Sam Paraplanner", "sam.paraplanner@example.com", "8820");

            Assert.Equal(
                "8820",
                GenerateExportPlugin.ParaplannerCode(
                    service, Case(null, "sam.paraplanner@example.com", "Sam Paraplanner")));
        }

        [Fact]
        public void A_person_the_registry_does_not_hold_exports_no_code()
        {
            // Blank, never a fallback. AD-039 reads by position: column B is the adviser's
            // code on every row, or the file lies about the rows where it is something else.
            var service = new FakeOrganizationService();

            Assert.True(string.IsNullOrEmpty(
                GenerateExportPlugin.AdviserCode(
                    service, Case("nobody@example.com", null, null))));
        }

        [Fact]
        public void A_contact_with_no_code_exports_no_code()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", null);

            Assert.True(string.IsNullOrEmpty(
                GenerateExportPlugin.AdviserCode(
                    service, Case("jane.adviser@example.com", null, null))));
        }

        [Fact]
        public void The_case_own_code_column_is_not_read()
        {
            // D2: the registry is the only source. A case still carrying a hand-typed code
            // from before this change must not leak it into the file.
            var service = new FakeOrganizationService();
            var outcomeCase = Case("nobody@example.com", null, null);
            outcomeCase["al_advisercode"] = "STALE-99";

            Assert.True(string.IsNullOrEmpty(
                GenerateExportPlugin.AdviserCode(service, outcomeCase)));
        }
    }
}
