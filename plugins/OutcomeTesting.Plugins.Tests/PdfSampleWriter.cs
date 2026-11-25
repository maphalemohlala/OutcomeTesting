using System;
using System.IO;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Writes the real generated documents to disk so a PDF reader can be pointed at them.
    ///
    /// Skipped unless OT_PDF_SAMPLE names a folder, so it costs nothing in an ordinary run. It
    /// exists because every assertion this suite makes about the PDF is made by the same
    /// codebase that writes it: a file wrong in a way both sides agree on would pass. The
    /// documents are built through <see cref="CompletedCheckPdf"/> over the seeded V8 form, so
    /// what a reader is shown is what a para-planner would be sent - the Tax check, the AQS
    /// check, and the remediation form as three files.
    /// </summary>
    public class PdfSampleWriter
    {
        private static readonly Guid AqsReviewId = Guid.Parse("a9e51111-1111-4111-8111-111111111111");

        [Fact]
        public void Write_a_sample_when_asked()
        {
            var folder = Environment.GetEnvironmentVariable("OT_PDF_SAMPLE");
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);

            var service = CompletedCheckDocumentTests.Case();
            var caseRef = new EntityReference("al_outcomecase", Guid.Parse("caee1111-1111-4111-8111-111111111111"));

            service.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", caseRef,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", AqsReviewId) { Name = "AQS check" },
                "al_description",
                "Issues found on the check:\n- ID verification completed and retained for all relevant clients/parties.: No\n"
                + "- Client objectives clearly evidenced and specific: Fail\n\n"
                + "Raised automatically when the review was submitted (Insufficient evidence).",
                RemedialActions.ActionAttr, "Legacy plain action.\nRe-verify ID <today> & file it.",
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_duedate", new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
                "al_completedon", new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc),
                "al_adviserresponse", "ID re-verified and retained on file; objectives restated in the suitability report.",
                "al_assignedcontactid", new EntityReference("contact", Guid.NewGuid()) { Name = "Adam Strumidlo" },
                "createdon", new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
                "statecode", 0);

            // A checker's rich remedial action (2026-11-23), to see the formatting drawn.
            service.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", caseRef,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", AqsReviewId) { Name = "AQS check" },
                "al_description", "Issues found on the check:\n- Client objectives clearly evidenced and specific: Fail",
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "al_duedate", new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
                "al_assignedcontactid", new EntityReference("contact", Guid.NewGuid()) { Name = "Adam Strumidlo" },
                "createdon", new DateTime(2026, 9, 25, 10, 1, 0, DateTimeKind.Utc),
                RemedialActions.ActionAttr,
                "<p>Please <strong>restate the objectives</strong> in the report and <u>re-issue it</u>; "
                + "<em>do not</em> change the fund choice.</p>"
                + "<ul><li>Objectives in the client&#39;s own words, long enough to wrap onto a second line of the cell</li>"
                + "<li>Attitude to risk &amp; capacity for loss</li></ul>"
                + "<ol><li>Update the report</li><li>Send it to the client</li></ol>",
                "statecode", 0);

            foreach (var document in CompletedCheckPdf.Documents(service, caseRef))
            {
                File.WriteAllBytes(Path.Combine(folder, document.Name), document.Content);
            }

            Assert.NotEmpty(Directory.GetFiles(folder, "*.pdf"));
        }
    }
}
