using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The payload the portal writes onto contact.al_caseheaderrequest so a Tax checker can
    /// set the two fields the Checker Checklist assigns to the Tax team - "Tax check required
    /// (Tax team to complete)" and "For Tax team usage" (project owner, 2026-09-13).
    ///
    /// Zero means "not sent" throughout, so a page may move one field without naming the
    /// other. That matters: the two are edited from separate controls on the header row, and
    /// sending a zero for the untouched one would otherwise read as a request to clear it.
    /// </summary>
    public class CaseHeaderRequestPluginTests
    {
        [Fact]
        public void Reads_both_fields_from_the_page()
        {
            var payload = CaseHeaderRequestPayload.Parse(
                "{\"caseId\":\"af352f30-50af-f111-aaac-e4fade069307\"," +
                "\"taxCheckRequired\":120910560,\"taxTeamDisposition\":120910571}");

            Assert.NotNull(payload);
            Assert.Equal("af352f30-50af-f111-aaac-e4fade069307", payload.CaseId);
            Assert.Equal(120910560, payload.TaxCheckRequired);
            Assert.Equal(120910571, payload.TaxTeamDisposition);
        }

        [Fact]
        public void Leaves_a_field_the_page_did_not_send_at_zero()
        {
            // One control moved, the other untouched. Zero is "not sent", never "clear it".
            var payload = CaseHeaderRequestPayload.Parse(
                "{\"caseId\":\"af352f30-50af-f111-aaac-e4fade069307\",\"taxTeamDisposition\":120910570}");

            Assert.NotNull(payload);
            Assert.Equal(0, payload.TaxCheckRequired);
            Assert.Equal(120910570, payload.TaxTeamDisposition);
        }

        [Fact]
        public void Refuses_a_payload_it_cannot_read_rather_than_guessing()
        {
            // Null is what makes the plug-in throw VALIDATION rather than apply a default:
            // a half-understood edit to the field that decides whether AQS is owed is worse
            // than no edit at all.
            Assert.Null(CaseHeaderRequestPayload.Parse("not json"));
            Assert.Null(CaseHeaderRequestPayload.Parse(""));
            Assert.Null(CaseHeaderRequestPayload.Parse(null));
        }

        [Fact]
        public void Carries_no_field_the_page_has_no_business_setting()
        {
            // The payload names the case, the two Tax team options and the rest of the
            // header, and nothing else. A signatory, a status or a route in here would be a
            // value the browser could choose; the plug-in reads the contact from
            // PrimaryEntityId and derives the route itself for exactly that reason.
            //
            // Named rather than counted (item 5, 2026-09-19). Counting said "3" and told a
            // reader nothing about WHICH three, so widening the payload failed the test
            // without saying what had been added or whether it was safe.
            var properties = typeof(CaseHeaderRequestPayload)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[] { "CaseId", "Fields", "TaxCheckRequired", "TaxTeamDisposition" },
                properties);
        }

        [Fact]
        public void Reads_the_header_fields_the_portal_sends()
        {
            var payload = new CaseHeaderRequestPayload
            {
                CaseId = "x",
                Fields = "{\"al_clientname\":\"A. Client\",\"al_advicedate\":\"2026-09-01\"}",
            };

            var fields = payload.ParsedFields();

            Assert.Equal(2, fields.Count);
            Assert.Equal("A. Client", fields["al_clientname"]);
            Assert.Equal("2026-09-01", fields["al_advicedate"]);
        }

        [Fact]
        public void Reads_an_absent_field_payload_as_no_fields()
        {
            // A Tax-only edit sends none, which is the shape this page sent before item 5
            // and must keep working.
            Assert.Empty(new CaseHeaderRequestPayload { CaseId = "x" }.ParsedFields());
            Assert.Empty(new CaseHeaderRequestPayload { CaseId = "x", Fields = "" }.ParsedFields());
        }

        [Theory]
        [InlineData("al_casereference")]
        [InlineData("al_ioreference")]
        [InlineData("al_duedate")]
        [InlineData("al_casestatus")]
        [InlineData("al_priority")]
        [InlineData("al_taxcheckrequired")]
        [InlineData("ownerid")]
        public void Refuses_a_field_the_portal_has_no_business_editing(string field)
        {
            // References and IDs identify the case and key the import; the due date is
            // derived from the upload; status and priority are the lifecycle and a
            // manager's call; the Tax fields have their own role-gated path above.
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { field, "anything" },
            };

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureCheckerEditable(fields));

            Assert.Contains(field, error.Message);
        }

        [Theory]
        [InlineData("al_clientname")]
        [InlineData("al_advisername")]
        [InlineData("al_advisercode")]
        [InlineData("al_paraplanner")]
        [InlineData("al_products")]
        [InlineData("al_advicedate")]
        [InlineData("al_checkdate")]
        [InlineData("al_vulnerableclient")]
        public void Allows_the_header_fields_a_checker_owns(string field)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { field, "anything" },
            };

            CaseHeaderRequestPlugin.EnsureCheckerEditable(fields);
        }

        [Fact]
        public void Refuses_the_checker_name_a_checker_used_to_be_able_to_type()
        {
            // Item 2, 2026-09-19. The header now carries a Tax Checker and an AQS Checker,
            // and each REFLECTS the checker assigned to that review - so neither is free text
            // anyone types, on this surface or in the Code App. Allocation is the one thing
            // that knows who holds a check, and it is what writes them.
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "al_checkername", "Someone Else" },
            };

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => CaseHeaderRequestPlugin.EnsureCheckerEditable(fields));

            Assert.Contains("al_checkername", error.Message);
        }

        [Fact]
        public void Names_the_request_column_the_allowlist_exposes()
        {
            // The site setting Webapi/contact/fields must name this column, and the step's
            // filtering attribute must match it. Pinned so a rename cannot silently leave the
            // page writing a column nothing reads.
            Assert.Equal("al_caseheaderrequest", CaseHeaderRequestPlugin.RequestAttr);
        }

        [Fact]
        public void Refuses_a_value_the_option_set_does_not_carry()
        {
            // Any integer used to be written straight to the case; DeriveRoute did nothing with
            // one it did not know, and the header drew blank under an audit line naming a change.
            var swapped = new CaseHeaderRequestPayload { CaseId = "x", TaxCheckRequired = 120910571, TaxTeamDisposition = 0 };
            var typo = new CaseHeaderRequestPayload { CaseId = "x", TaxCheckRequired = 0, TaxTeamDisposition = 1 };

            Assert.Contains("Tax check required", CaseHeaderRequestPlugin.UnknownOptionRefusal(swapped));
            Assert.Contains("For Tax team usage", CaseHeaderRequestPlugin.UnknownOptionRefusal(typo));
        }

        [Fact]
        public void Accepts_the_options_the_columns_carry_and_zero_for_unsent()
        {
            Assert.Null(CaseHeaderRequestPlugin.UnknownOptionRefusal(
                new CaseHeaderRequestPayload { CaseId = "x", TaxCheckRequired = 120910561, TaxTeamDisposition = 120910570 }));
            Assert.Null(CaseHeaderRequestPlugin.UnknownOptionRefusal(
                new CaseHeaderRequestPayload { CaseId = "x", TaxCheckRequired = 0, TaxTeamDisposition = 0 }));
        }

        [Fact]
        public void Audits_under_the_same_command_as_the_command_path()
        {
            // A re-declared constant carried ReturnCase's value, so every portal header edit
            // was written to the trail as a return. The value is the command path's, and it
            // is pinned to the al_command option the app's generated model calls UpdateCaseDetails.
            Assert.Equal(UpdateCaseDetailsPlugin.CommandUpdateCaseDetails, CaseHeaderRequestPlugin.CommandUpdateCaseDetails);
            Assert.Equal(120910778, CaseHeaderRequestPlugin.CommandUpdateCaseDetails);
        }
    }
}
