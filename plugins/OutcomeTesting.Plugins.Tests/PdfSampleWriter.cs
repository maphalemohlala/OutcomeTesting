using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Writes a sample document to disk so a real PDF reader can be pointed at it.
    ///
    /// Skipped unless OT_PDF_SAMPLE names a path, so it costs nothing in an ordinary run. It
    /// exists because every assertion this suite makes about the PDF is made by the same
    /// codebase that writes it: a file that is wrong in a way both sides agree on would pass.
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

            var blocks = new List<PdfBlock>
            {
                PdfBlock.Title("Outcome Testing - completed check"),
                PdfBlock.Field("Case reference", "IO-300001"),
                PdfBlock.Field("Client", "A. Client"),
                PdfBlock.Spacer(),
                PdfBlock.Heading("Tax check - submitted 14 September 2026"),
            };

            // Long enough to run over a page, which is where a hand-built xref table breaks.
            for (var section = 1; section <= 4; section++)
            {
                blocks.Add(PdfBlock.Subheading("Suitability core checks " + section));
                for (var question = 1; question <= 12; question++)
                {
                    blocks.Add(PdfBlock.Field(
                        "Question " + question + " - the wording of a checklist item, which is "
                        + "usually a full sentence and sometimes rather longer than one line",
                        question % 3 == 0 ? "Fail" : "Pass"));
                }
            }

            blocks.Add(PdfBlock.Spacer());
            blocks.Add(PdfBlock.Heading("Remedial actions"));
            blocks.Add(PdfBlock.Bullet("Evidence the charges disclosure and re-issue the letter."));
            blocks.Add(PdfBlock.Field("Status", "Open, due 1 October 2026"));

            File.WriteAllBytes(path, PdfWriter.Build(blocks));
            Assert.True(File.Exists(path));
        }
    }
}
