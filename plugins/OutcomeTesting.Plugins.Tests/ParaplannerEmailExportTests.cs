using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Trail Light column D, which carries the para-planner's EMAIL from 2026-09-21 (project
    /// owner: "replace column B &amp; D on the trail light exports with emails. rather than
    /// codes show emails").
    ///
    /// Column B is a straight read of <c>al_adviseremail</c> off the case and needs no rule.
    /// Column D does: the case has no para-planner email column at all - the import carries
    /// their name and nothing else (AD-160) - so the address has to be resolved from the
    /// Contact that name matches, and the interesting cases are the ones where it does not
    /// match exactly one.
    /// </summary>
    public class ParaplannerEmailExportTests
    {
        private static Entity Case(string paraplanner)
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            if (paraplanner != null)
            {
                row["al_paraplanner"] = paraplanner;
            }

            return row;
        }

        private static void Contact(FakeOrganizationService service, string name, string email)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", name,
                "emailaddress1", email,
                "statecode", 0);
        }

        [Fact]
        public void One_active_contact_of_that_name_gives_the_address()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Sam Paraplanner", "sam.paraplanner@example.com");

            Assert.Equal(
                "sam.paraplanner@example.com",
                GenerateExportPlugin.ParaplannerEmail(service, Case("Sam Paraplanner")));
        }

        [Fact]
        public void A_case_naming_no_para_planner_exports_an_empty_cell()
        {
            var service = new FakeOrganizationService();

            Assert.Null(GenerateExportPlugin.ParaplannerEmail(service, Case(null)));
        }

        [Fact]
        public void A_name_no_contact_carries_exports_an_empty_cell()
        {
            // Not a fallback to the name or the code. AD-039 reads by position, so column D
            // is "Paraplanner Email" on every row or the file lies about the rows where it
            // is something else - and a wrong address in a file that leaves this system is
            // worse than a blank one.
            var service = new FakeOrganizationService();
            Contact(service, "Someone Else", "someone.else@example.com");

            Assert.Null(GenerateExportPlugin.ParaplannerEmail(service, Case("Sam Paraplanner")));
        }

        [Fact]
        public void Two_contacts_of_one_name_export_an_empty_cell_rather_than_a_guess()
        {
            // The one worth stating. Sending a client's outcome to the wrong para-planner is
            // a data-protection incident where a blank cell is an operational one, and a
            // missing email address is not evidence about which Sam Paraplanner is meant -
            // which is why the match deliberately does not prefer whichever one has an
            // address.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Paraplanner", "sam.paraplanner@example.com");
            Contact(service, "Sam Paraplanner", null);

            Assert.Null(GenerateExportPlugin.ParaplannerEmail(service, Case("Sam Paraplanner")));
        }

        [Fact]
        public void A_matched_contact_with_no_work_email_exports_an_empty_cell()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Sam Paraplanner", null);

            Assert.Null(GenerateExportPlugin.ParaplannerEmail(service, Case("Sam Paraplanner")));
        }
    }
}
