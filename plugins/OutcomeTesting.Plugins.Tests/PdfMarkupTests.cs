using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Rich remedial actions in the PDFs, and the Ascot Lloyd logo on every page
    /// (project owner, 2026-11-23).
    /// </summary>
    public class PdfMarkupTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee1111-1111-4111-8111-111111111111");
        private static readonly Guid AqsReviewId = Guid.Parse("a9e51111-1111-4111-8111-111111111111");

        private static string Pdf(byte[] bytes)
        {
            return Encoding.GetEncoding(28591).GetString(bytes);
        }

        private static string Pdf(params PdfBlock[] blocks)
        {
            return Pdf(PdfWriter.Build(blocks));
        }

        private static string OneCell(PdfCell cell)
        {
            var table = new PdfTable(1);
            table.Add(cell);
            return Pdf(PdfBlock.Table(table));
        }

        /// <summary>Every drawn string with its position, in drawing order.</summary>
        private static List<Tuple<double, double, string>> Drawn(string pdf)
        {
            return Regex.Matches(pdf, @"1 0 0 1 ([\d.]+) ([\d.]+) Tm\n\((.*?)\) Tj")
                .Cast<Match>()
                .Select(m => Tuple.Create(
                    double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    m.Groups[3].Value))
                .ToList();
        }

        // ================================================================== the converter

        [Fact]
        public void Plain_text_converts_exactly_as_PdfCell_Of_does()
        {
            var plain = "Re-verify ID.\nFile the evidence <today> & tell us.";

            var cell = PdfCell.FromMarkup(plain);
            var of = PdfCell.Of(plain);

            Assert.Equal(of.Text, cell.Text);
            Assert.Equal(of.Runs.Count, cell.Runs.Count);
            Assert.All(cell.Runs, r => Assert.False(r.Bold || r.Italic || r.Underline));
        }

        [Fact]
        public void Null_converts_to_an_empty_cell()
        {
            Assert.Equal(string.Empty, PdfCell.FromMarkup(null).Text);
        }

        [Fact]
        public void Nested_strong_em_and_u_set_the_runs_faces()
        {
            var cell = PdfCell.FromMarkup("<p>Do <strong>this <em>now</em></strong> and <u>that</u>.</p>");

            Assert.Equal("Do this now and that.", cell.Text);
            var now = cell.Runs.Single(r => r.Text == "now");
            Assert.True(now.Bold && now.Italic && !now.Underline);
            var that = cell.Runs.Single(r => r.Text == "that");
            Assert.True(that.Underline && !that.Bold);
            Assert.False(cell.Runs.First().Bold);
        }

        [Fact]
        public void Entities_are_decoded()
        {
            var cell = PdfCell.FromMarkup("<p>A &amp; B &lt;c&gt; &quot;d&quot; &#39;e&#39;&nbsp;f &amp;lt;</p>");

            Assert.Equal("A & B <c> \"d\" 'e' f &lt;", cell.Text);
        }

        [Fact]
        public void Paragraphs_and_line_breaks_start_new_lines()
        {
            Assert.Equal("One\nTwo\nThree", PdfCell.FromMarkup("<p>One</p><p>Two<br>Three</p>").Text);
        }

        [Fact]
        public void An_empty_paragraph_between_two_is_a_blank_line()
        {
            Assert.Equal("One\n\nTwo", PdfCell.FromMarkup("<p>One</p><p><br></p><p>Two</p>").Text);
        }

        [Fact]
        public void A_bulleted_list_gets_bullets_and_a_numbered_list_numbers()
        {
            Assert.Equal("Fix:\n• A\n• B", PdfCell.FromMarkup("<p>Fix:</p><ul><li>A</li><li>B</li></ul>").Text);
            Assert.Equal("1. A\n2. B\nAfter", PdfCell.FromMarkup("<ol><li>A</li><li>B</li></ol><p>After</p>").Text);
        }

        [Fact]
        public void A_nested_list_numbers_itself_and_indents_deeper()
        {
            var cell = PdfCell.FromMarkup("<ol><li>A<ul><li>x</li></ul></li><li>B</li></ol>");

            Assert.Equal("1. A\n• x\n2. B", cell.Text);
            var markers = cell.Runs.Where(r => r.Indent.HasValue && r.Text != null).ToList();
            Assert.True(markers[1].Indent > markers[0].Indent, "the nested item is not indented further");
            Assert.Equal(markers[0].Indent, markers[2].Indent);
        }

        [Fact]
        public void Unknown_tags_keep_their_text()
        {
            Assert.Equal("x y z", PdfCell.FromMarkup("<p>x <span class=\"k\">y</span> <a href=\"https://e.test\">z</a></p>").Text);
        }

        [Fact]
        public void Formatting_inside_a_word_does_not_split_it()
        {
            var pdf = OneCell(PdfCell.FromMarkup("<p><strong>Un</strong>important</p>"));
            var drawn = Drawn(pdf);

            var un = drawn.Single(d => d.Item3 == "Un");
            var rest = drawn.Single(d => d.Item3 == "important");
            Assert.True(Math.Abs(un.Item1 + PdfWriter.Width("Un", "F2", 9) - rest.Item1) < 0.05, "a space was put inside the word");
        }

        // ================================================================== drawing

        [Fact]
        public void A_wrapped_list_item_hangs_under_its_text_not_its_bullet()
        {
            var words = string.Join(" ", Enumerable.Repeat("remedial", 40));
            var drawn = Drawn(OneCell(PdfCell.FromMarkup("<ul><li>" + words + "</li></ul>")));

            var bullet = drawn.First();
            Assert.Equal("\\225", bullet.Item3);
            var lines = drawn.Skip(1).ToList();
            Assert.True(lines.Count > 1, "the item did not wrap");
            Assert.True(lines[0].Item1 > bullet.Item1 + 5, "the text sits on its bullet");
            Assert.All(lines, l => Assert.Equal(lines[0].Item1, l.Item1, 2));
        }

        [Fact]
        public void Underlined_text_draws_a_line_under_it()
        {
            var plain = OneCell(PdfCell.FromMarkup("<p>Read this</p>"));
            var underlined = OneCell(PdfCell.FromMarkup("<p>Read <u>this</u></p>"));

            Assert.Equal(
                Regex.Matches(plain, " re f\n").Count + 1,
                Regex.Matches(underlined, " re f\n").Count);
        }

        // ================================================================== the documents

        private static FakeOrganizationService CaseWithAction(string remedialAction)
        {
            var service = CompletedCheckDocumentTests.Case();
            service.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", AqsReviewId) { Name = "AQS check" },
                "al_description", "Issues found on the check:\n- Client objectives clearly evidenced and specific: Fail",
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "al_duedate", new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
                "al_assignedcontactid", new EntityReference("contact", Guid.NewGuid()) { Name = "Adam Strumidlo" },
                "createdon", new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
                RemedialActions.ActionAttr, remedialAction,
                "statecode", 0);
            return service;
        }

        private const string RichAction = "<p>Re-verify <strong>ID</strong>:</p><ul><li>passport</li></ul>";

        private static PdfCell RemedialCell(IEnumerable<PdfBlock> blocks)
        {
            var table = blocks.Where(b => b.Kind == PdfBlockKind.Table).Select(b => b.Grid)
                .Last(t => t.Rows[0].Cells[0].Text == "No.");
            return table.Rows.First(r => r.Cells[0].Text == "1").Cells[2];
        }

        [Fact]
        public void The_remediation_form_draws_a_rich_remedial_action_formatted()
        {
            var service = CaseWithAction(RichAction);

            var cell = RemedialCell(RemediationDocument.Blocks(service, new EntityReference("al_outcomecase", CaseId), new DateTime(2026, 9, 29)));

            Assert.Equal("Re-verify ID:\n• passport", cell.Text);
            Assert.True(cell.Runs.Single(r => r.Text == "ID").Bold);
        }

        [Fact]
        public void The_completed_checks_remedial_actions_draw_a_rich_action_formatted()
        {
            var service = CaseWithAction(RichAction);

            var cell = RemedialCell(CompletedCheck.Blocks(service, AqsReviewId));

            Assert.Equal("Re-verify ID:\n• passport", cell.Text);
            Assert.True(cell.Runs.Single(r => r.Text == "ID").Bold);
        }

        [Fact]
        public void A_legacy_plain_remedial_action_still_draws_as_written()
        {
            var service = CaseWithAction("Line one <b>not a tag</b>\nLine two");

            var cell = RemedialCell(CompletedCheck.Blocks(service, AqsReviewId));

            Assert.Equal("Line one <b>not a tag</b>\nLine two", cell.Text);
            Assert.All(cell.Runs, r => Assert.False(r.Bold));
        }

        [Fact]
        public void A_rich_text_answer_keeps_its_formatting_and_three_lines_of_room()
        {
            var service = CompletedCheckDocumentTests.Case();
            var rich = service.All("al_response").Single(r => r.Contains("al_answerrichtext"));
            rich["al_answerrichtext"] = "<p>Re-run the <b>CGT</b> calculation &amp; reissue.</p>";

            var reviewId = rich.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id;
            var tax = CompletedCheck.Blocks(service, reviewId)
                .Where(b => b.Kind == PdfBlockKind.Table).Select(b => b.Grid)
                .First(t => t.Rows.Any(r => r.Cells.Count > 0 && r.Cells[0].Text == "Tax Remedial"));
            var cell = tax.Rows.First(r => r.Cells[0].Text == "Tax Remedial").Cells[1];

            Assert.StartsWith("Re-run the CGT calculation & reissue.", cell.Text);
            Assert.True(cell.Runs.Single(r => r.Text == "CGT").Bold);
            Assert.Equal(2, cell.Runs.Count(r => r.Break));
        }

        // ================================================================== the logo

        private static List<PdfBlock> LongDocument()
        {
            var blocks = new List<PdfBlock> { PdfBlock.Note("Footer line"), PdfBlock.Title("Title") };
            for (var i = 0; i < 150; i++)
            {
                blocks.Add(PdfBlock.Paragraph("Line " + i));
            }

            return blocks;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Every_page_draws_the_logo_once(bool landscape)
        {
            var pdf = Pdf(PdfWriter.Build(LongDocument(), landscape));

            var pages = int.Parse(Regex.Match(pdf, @"/Count (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.True(pages > 1);
            Assert.Equal(pages, Regex.Matches(pdf, "/Im1 Do").Count);
            Assert.Equal(pages, Regex.Matches(pdf, @"/XObject << /Im1 \d+ 0 R >>").Count);
            Assert.Single(Regex.Matches(pdf, "/Subtype /Image").Cast<Match>());
        }

        [Fact]
        public void The_logo_stream_inflates_to_one_palette_index_per_pixel()
        {
            var pdf = Pdf(PdfBlock.Paragraph("Hello."));

            var head = Regex.Match(pdf,
                @"/Subtype /Image /Width (\d+) /Height (\d+) /ColorSpace \[/Indexed /DeviceRGB (\d+) <([0-9A-F]+)>\] "
                + @"/BitsPerComponent 8 /Filter \[/ASCIIHexDecode /FlateDecode\] /Length (\d+) >>\nstream\n");
            Assert.True(head.Success, "no image object");
            var colours = int.Parse(head.Groups[3].Value, CultureInfo.InvariantCulture) + 1;
            Assert.Equal(colours * 6, head.Groups[4].Value.Length);

            // The stream as the reader sees it: hex to bytes, then zlib (two header bytes, then deflate).
            var hex = pdf.Substring(head.Index + head.Length, int.Parse(head.Groups[5].Value, CultureInfo.InvariantCulture));
            Assert.EndsWith(">", hex);
            var zlib = Enumerable.Range(0, (hex.Length - 1) / 2)
                .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
            byte[] pixels;
            using (var inflate = new System.IO.Compression.DeflateStream(
                new System.IO.MemoryStream(zlib, 2, zlib.Length - 2), System.IO.Compression.CompressionMode.Decompress))
            using (var all = new System.IO.MemoryStream())
            {
                inflate.CopyTo(all);
                pixels = all.ToArray();
            }

            var width = int.Parse(head.Groups[1].Value, CultureInfo.InvariantCulture);
            var height = int.Parse(head.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.Equal(width * height, pixels.Length);
            Assert.All(pixels, p => Assert.True(p < colours));

            // The top-left pixel is the white background.
            Assert.Equal("FFFFFF", head.Groups[4].Value.Substring(pixels[0] * 6, 6));
        }

        [Theory]
        [InlineData(false, 841.89)]
        [InlineData(true, 595.28)]
        public void The_logo_sits_top_left_and_the_text_starts_below_it(bool landscape, double pageHeight)
        {
            var pdf = Pdf(PdfWriter.Build(LongDocument(), landscape));

            var place = Regex.Match(pdf, @"q ([\d.]+) 0 0 ([\d.]+) ([\d.]+) ([\d.]+) cm /Im1 Do Q");
            Assert.True(place.Success, "the logo is not placed");
            var height = double.Parse(place.Groups[2].Value, CultureInfo.InvariantCulture);
            var x = double.Parse(place.Groups[3].Value, CultureInfo.InvariantCulture);
            var y = double.Parse(place.Groups[4].Value, CultureInfo.InvariantCulture);

            Assert.Equal(42.5, x, 1);
            Assert.Equal(pageHeight - 42.5, y + height, 1);

            // The note line, the first thing drawn, starts below the logo with room to spare.
            var first = Drawn(pdf).First();
            Assert.True(first.Item2 + 8 < y, "text at " + first.Item2 + " overlaps the logo bottom at " + y);
        }
    }
}
