using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Writes a real generated document to disk so a PDF reader can be pointed at it.
    ///
    /// Skipped unless OT_PDF_SAMPLE names a path, so it costs nothing in an ordinary run. It
    /// exists because every assertion this suite makes about the PDF is made by the same
    /// codebase that writes it: a file wrong in a way both sides agree on would pass. The
    /// document is built through <see cref="CompletedCheckPdf"/> rather than from a hand-written
    /// block list, so what a reader is shown is what a para-planner would be sent.
    /// </summary>
    public class PdfSampleWriter
    {
        [Fact]
        public void Write_a_sample_when_asked()
        {
            var path = Environment.GetEnvironmentVariable("OT_PDF_SAMPLE");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var caseId = Guid.Parse("caee1111-1111-4111-8111-111111111111");
            var caseRef = new EntityReference("al_outcomecase", caseId);
            var service = new FakeOrganizationService();

            service.SeedOptionSet("al_response", "al_answerchoice",
                ResponseRules.ChoicePass, "Pass",
                ResponseRules.ChoiceFail, "Fail",
                ResponseRules.ChoiceInsufficient, "Insufficient evidence");
            service.SeedOptionSet("al_remediationaction", "al_actionstatus",
                Remediation.StatusOpen, "Open");

            service.Seed("al_outcomecase", caseId,
                "al_casereference", "OT-2026-0417",
                "al_ioreference", "IO-300001",
                "al_clientname", "A. Client",
                "al_advisername", "Adviser User 1",
                "al_paraplanner", "Pat Paraplanner",
                "al_advicedate", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                "al_duedate", new DateTime(2026, 9, 17, 17, 0, 0, DateTimeKind.Utc),
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingSignoff),
                "al_checklistitems",
                "Tax Check\nHigh Risk Item 1\nVulnerable client indicator recorded on the file\n"
                + "Pension transfer - defined benefit");

            var reviewId = Guid.NewGuid();
            service.Seed("al_reviewinstance", reviewId,
                "al_outcomecaseid", caseRef,
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_sequence", 1,
                "al_submittedon", new DateTime(2026, 9, 14, 10, 30, 0, DateTimeKind.Utc),
                "statecode", 0);

            var core = Guid.NewGuid();
            service.Seed("al_section", core,
                "al_name", "Suitability core checks", "al_sectioncode", "S-E1", "al_displayorder", 1);

            var tax = Guid.NewGuid();
            service.Seed("al_section", tax,
                "al_name", "Tax check", "al_sectioncode", "S-TAX", "al_displayorder", 2);

            Answer(service, reviewId, core, 1, "Client objectives recorded and evidenced on file",
                ResponseRules.TypePassFail, "al_answerchoice",
                new OptionSetValue(ResponseRules.ChoicePass));
            Answer(service, reviewId, core, 2,
                "Adviser charges clearly disclosed and evidenced, including any ongoing service fee",
                ResponseRules.TypePassFail, "al_answerchoice",
                new OptionSetValue(ResponseRules.ChoiceFail));
            Answer(service, reviewId, core, 3, "Attitude to risk assessed and reconciled to the recommendation",
                ResponseRules.TypePassFailInsufficient, "al_answerchoice",
                new OptionSetValue(ResponseRules.ChoiceInsufficient));
            Answer(service, reviewId, tax, 1, "Tax Remedial",
                ResponseRules.TypeRichText, "al_answerrichtext",
                "<p>Re-run the <b>CGT</b> calculation using the 2026/27 annual exempt amount "
                + "&amp; reissue the suitability report to the client.</p>");

            service.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", caseRef,
                "al_description",
                "Evidence the adviser charges disclosure and re-issue the letter to the client.",
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "al_duedate", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                "statecode", 0);

            File.WriteAllBytes(path, CompletedCheckPdf.Build(service, caseRef));
            Assert.True(File.Exists(path));
        }

        private static void Answer(
            FakeOrganizationService service,
            Guid reviewId,
            Guid sectionId,
            int order,
            string questionText,
            int responseType,
            string answerColumn,
            object answer)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();

            service.Seed("al_question", questionId,
                "al_questioncode", "Q-" + order,
                "al_sectionid", new EntityReference("al_section", sectionId));

            service.Seed("al_questionversion", versionId,
                "al_questionid", new EntityReference("al_question", questionId),
                "al_questiontext", questionText,
                "al_responsetype", new OptionSetValue(responseType),
                "al_displayorder", order);

            service.Seed("al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                answerColumn, answer);
        }
    }
}
