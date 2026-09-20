using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Writes a sample document to disk so it can be opened by a real reader.
    ///
    /// Skipped unless OUTCOMETESTING_PDF_DUMP names a folder, because a unit test that
    /// writes files on every run is a unit test with a side effect. This exists to answer
    /// the one question the assertions cannot: does a PDF reader actually open it.
    /// </summary>
    public class PdfDumpForManualCheck
    {
        [Fact]
        public void Writes_a_sample_when_asked()
        {
            var folder = Environment.GetEnvironmentVariable("OUTCOMETESTING_PDF_DUMP");
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            var blocks = new List<PdfBlock>
            {
                PdfBlock.Title("Outcome Testing - case summary"),
                PdfBlock.Field("Case reference", "IO-300001"),
                PdfBlock.Field("Client", "Mr & Mrs (A) Smith"),
                PdfBlock.Field("Adviser", "Adviser User 1"),
                PdfBlock.Field("Para-planner", "Pat Paraplanner"),
                PdfBlock.Spacer(),
                PdfBlock.Heading("Why this case was selected"),
                PdfBlock.Bullet("Tax Check"),
                PdfBlock.Bullet("High Risk Item 1"),
                PdfBlock.Spacer(),
                PdfBlock.Heading("Outcome"),
                PdfBlock.Paragraph(
                    "The review on this case has been submitted and is locked to further edits. "
                    + "This summary reflects the case as it stood at that moment, and a very long "
                    + "sentence is included here on purpose so that the wrapping can be judged by "
                    + "eye rather than only by an assertion about widths."),
            };

            File.WriteAllBytes(Path.Combine(folder, "case-summary-sample.pdf"), PdfWriter.Build(blocks));
        }
    }
}
