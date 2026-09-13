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
            // The payload names the case and the two Tax team options and nothing else. A
            // signatory, a status or a route in here would be a value the browser could
            // choose; the plug-in reads the contact from PrimaryEntityId and derives the
            // route itself for exactly that reason.
            var properties = typeof(CaseHeaderRequestPayload).GetProperties();

            Assert.Equal(3, properties.Length);
        }

        [Fact]
        public void Names_the_request_column_the_allowlist_exposes()
        {
            // The site setting Webapi/contact/fields must name this column, and the step's
            // filtering attribute must match it. Pinned so a rename cannot silently leave the
            // page writing a column nothing reads.
            Assert.Equal("al_caseheaderrequest", CaseHeaderRequestPlugin.RequestAttr);
        }
    }
}
