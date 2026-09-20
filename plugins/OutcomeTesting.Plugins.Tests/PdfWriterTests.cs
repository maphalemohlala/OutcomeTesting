using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The PDF writer produces a file a reader will actually open (Change 2, AD-164).
    ///
    /// <para>
    /// The test that earns its place is <see cref="Every_xref_offset_points_at_its_object"/>.
    /// A PDF's cross-reference table is byte offsets computed by hand, and an off-by-one
    /// there is the classic way to produce a file that looks right in a hex dump and is
    /// refused by every reader. Nothing else here would catch it.
    /// </para>
    /// <para>
    /// The rest is about text that comes off a case: a client's name carrying a bracket, a
    /// backslash or a character outside WinAnsi must not corrupt the document.
    /// </para>
    /// </summary>
    public class PdfWriterTests
    {
        private static string Text(byte[] pdf)
        {
            return Encoding.GetEncoding(28591).GetString(pdf);
        }

        // ------------------------------------------------------------ structure

        [Fact]
        public void Writes_a_pdf_header_and_trailer()
        {
            var pdf = Text(PdfWriter.Build(new[] { PdfBlock.Paragraph("Hello.") }));

            Assert.StartsWith("%PDF-1.4", pdf);
            Assert.EndsWith("%%EOF", pdf);
            Assert.Contains("/Type /Catalog", pdf);
        }

        [Fact]
        public void Every_xref_offset_points_at_its_object()
        {
            // The offsets are computed from string lengths as the file is assembled. If the
            // encoding ever stopped being one byte per character, or an object moved, every
            // offset after it would be wrong and the file would not open - while still
            // containing every word anybody looked for.
            var bytes = PdfWriter.Build(LongDocument());
            var pdf = Text(bytes);

            // "\nxref\n", not "xref": the last bare "xref" in the file is the one inside
            // "startxref", and matching that would read the offset as the table.
            var xrefAt = pdf.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
            Assert.True(xrefAt > 0, "no xref table");

            var lines = pdf.Substring(xrefAt).Split('\n');
            // lines[0] "xref", lines[1] "0 N", lines[2] the free entry, then one per object.
            var count = int.Parse(lines[1].Split(' ')[1], CultureInfo.InvariantCulture);

            for (var i = 1; i < count; i++)
            {
                var offset = int.Parse(lines[i + 2].Substring(0, 10), CultureInfo.InvariantCulture);
                var expected = i.ToString(CultureInfo.InvariantCulture) + " 0 obj";

                Assert.True(
                    offset + expected.Length <= pdf.Length,
                    "object " + i + " offset " + offset + " runs past the end of the file");
                Assert.Equal(expected, pdf.Substring(offset, expected.Length));
            }
        }

        [Fact]
        public void Startxref_points_at_the_xref_table()
        {
            var pdf = Text(PdfWriter.Build(new[] { PdfBlock.Paragraph("Hello.") }));

            var marker = pdf.LastIndexOf("startxref", StringComparison.Ordinal);
            var value = int.Parse(
                pdf.Substring(marker + "startxref".Length).Trim().Split('\n')[0].Trim(),
                CultureInfo.InvariantCulture);

            Assert.Equal("xref", pdf.Substring(value, 4));
        }

        [Fact]
        public void Long_content_runs_onto_more_pages()
        {
            var one = Text(PdfWriter.Build(new[] { PdfBlock.Paragraph("Short.") }));
            var many = Text(PdfWriter.Build(LongDocument()));

            Assert.Contains("/Count 1", one);
            Assert.DoesNotContain("/Count 1 ", many.Replace("/Count 10", "x"));
        }

        [Fact]
        public void An_empty_document_is_still_a_valid_pdf()
        {
            // A case with nothing to say about it must not produce a file that fails to
            // open. One empty page is an honest answer.
            var pdf = Text(PdfWriter.Build(new PdfBlock[0]));

            Assert.StartsWith("%PDF-1.4", pdf);
            Assert.EndsWith("%%EOF", pdf);
            Assert.Contains("/Count 1", pdf);
        }

        [Fact]
        public void A_null_list_is_treated_as_empty_rather_than_throwing()
        {
            Assert.NotEmpty(PdfWriter.Build(null));
        }

        [Fact]
        public void No_font_data_is_embedded()
        {
            // Helvetica is one of the fourteen standard fonts, which is what keeps this to a
            // few kilobytes and keeps font licensing out of the question entirely.
            var pdf = Text(PdfWriter.Build(new[] { PdfBlock.Title("Case summary") }));

            Assert.Contains("/BaseFont /Helvetica", pdf);
            Assert.DoesNotContain("/FontFile", pdf);
        }

        // ------------------------------------------------------------ escaping

        [Theory]
        [InlineData("Mr (Bob) Smith", "Mr \\(Bob\\) Smith")]
        [InlineData("a\\b", "a\\\\b")]
        public void Brackets_and_backslashes_are_escaped(string input, string expected)
        {
            // These come off the case. An unescaped bracket ends the string early and every
            // byte after it is read as an operator.
            Assert.Contains(expected, Text(PdfWriter.Build(new[] { PdfBlock.Paragraph(input) })));
        }

        [Fact]
        public void A_character_outside_winansi_is_replaced_rather_than_written_raw()
        {
            var pdf = Text(PdfWriter.Build(new[] { PdfBlock.Paragraph("Zoë 日本") }));

            Assert.Contains("Zo?", pdf);
            Assert.DoesNotContain("日", pdf);
        }

        [Fact]
        public void The_bullet_is_written_as_its_winansi_code()
        {
            var pdf = Text(PdfWriter.Build(new[] { PdfBlock.Bullet("Tax Check") }));

            Assert.Contains("\\225", pdf);
            Assert.Contains("Tax Check", pdf);
        }

        // ------------------------------------------------------------ layout

        [Fact]
        public void Text_wraps_within_the_content_width()
        {
            // Helvetica at 10pt across a 20mm-margin A4 page. Without real widths this is
            // the assertion that fails: a monospace guess wraps visibly wrongly.
            var words = new StringBuilder();
            for (var i = 0; i < 80; i++)
            {
                words.Append("wide ");
            }

            var pdf = Text(PdfWriter.Build(new[] { PdfBlock.Paragraph(words.ToString()) }));

            foreach (var line in ExtractDrawnText(pdf))
            {
                Assert.True(
                    PdfWriter.Width(line, "F1", 10) <= 595.28 - (2 * 56.7) + 0.5,
                    "line ran past the margin: " + line);
            }
        }

        [Fact]
        public void A_word_too_long_to_fit_is_kept_whole()
        {
            // A case reference broken across two lines is unreadable and unsearchable.
            // Running slightly wide is the lesser fault, and it is a deliberate one.
            var giant = new string('M', 200);
            var drawn = ExtractDrawnText(Text(PdfWriter.Build(new[] { PdfBlock.Paragraph(giant) })));

            Assert.Contains(drawn, line => line == giant);
        }

        [Fact]
        public void A_field_puts_its_label_and_value_on_one_baseline()
        {
            var pdf = Text(PdfWriter.Build(new[] { PdfBlock.Field("Case reference", "IO-300001") }));

            Assert.Contains("Case reference: ", pdf);
            Assert.Contains("IO-300001", pdf);
            // Bold for the label, regular for the value.
            Assert.Contains("/F2 10 Tf", pdf);
            Assert.Contains("/F1 10 Tf", pdf);
        }

        [Fact]
        public void Bold_and_regular_widths_are_not_the_same_table()
        {
            // If they were, every bold heading would wrap as though it were regular text.
            Assert.NotEqual(
                PdfWriter.Width("Case summary", "F1", 12),
                PdfWriter.Width("Case summary", "F2", 12));
        }

        [Fact]
        public void Width_is_proportional_not_monospaced()
        {
            // The check that the metrics table is actually being read: in Helvetica an "i"
            // is far narrower than an "m", and a monospace assumption makes them equal.
            Assert.True(PdfWriter.Width("i", "F1", 10) < PdfWriter.Width("m", "F1", 10));
        }

        // ------------------------------------------------------------ helpers

        private static List<PdfBlock> LongDocument()
        {
            var blocks = new List<PdfBlock> { PdfBlock.Title("Case summary") };
            for (var i = 0; i < 120; i++)
            {
                blocks.Add(PdfBlock.Paragraph(
                    "Line " + i + " of a summary long enough to need more than one page."));
            }

            return blocks;
        }

        /// <summary>Every string the content streams draw, unescaped.</summary>
        private static List<string> ExtractDrawnText(string pdf)
        {
            var drawn = new List<string>();
            var at = 0;

            while (true)
            {
                var open = pdf.IndexOf("(", at, StringComparison.Ordinal);
                if (open < 0) break;

                var close = open + 1;
                var text = new StringBuilder();
                while (close < pdf.Length && pdf[close] != ')')
                {
                    if (pdf[close] == '\\' && close + 1 < pdf.Length)
                    {
                        close++;
                    }

                    text.Append(pdf[close]);
                    close++;
                }

                if (close < pdf.Length && pdf.IndexOf(") Tj", close, StringComparison.Ordinal) == close)
                {
                    drawn.Add(text.ToString());
                }

                at = close + 1;
            }

            return drawn;
        }
    }
}
