using System;
using System.Linq;
using System.Text;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Ruled tables with tick boxes (2026-09-24), which is what lets the para-planner's
    /// attachment be the form the review page prints rather than a description of it.
    /// </summary>
    public class PdfTableTests
    {
        private static string Pdf(params PdfBlock[] blocks)
        {
            return Encoding.GetEncoding(28591).GetString(PdfWriter.Build(blocks));
        }

        private static PdfTable Grid(int rows)
        {
            var table = new PdfTable(0.55, 0.15, 0.15, 0.15);
            table.AddHeader(PdfCell.Head("Check"), PdfCell.Head("Yes"), PdfCell.Head("No"), PdfCell.Head("N/A"));
            for (var i = 0; i < rows; i++)
            {
                table.Add(PdfCell.Of("Test point " + i), PdfCell.Box(i % 2 == 0), PdfCell.Box(false), PdfCell.Blank());
            }

            return table;
        }

        [Fact]
        public void A_ticked_box_draws_a_tick_and_an_empty_one_does_not()
        {
            var one = new PdfTable(1);
            one.Add(PdfCell.Box(true));
            var none = new PdfTable(1);
            none.Add(PdfCell.Box(false));

            Assert.Contains("1.2 w 0 G", Pdf(PdfBlock.Table(one)));
            Assert.DoesNotContain("1.2 w 0 G", Pdf(PdfBlock.Table(none)));
        }

        [Fact]
        public void Cells_and_ticks_are_drawn_outside_the_text_object()
        {
            var pdf = Pdf(PdfBlock.Table(Grid(3)));
            var stream = pdf.Substring(pdf.IndexOf("stream\n", StringComparison.Ordinal));

            Assert.True(stream.IndexOf(" re B", StringComparison.Ordinal) < stream.IndexOf("BT", StringComparison.Ordinal));
            Assert.True(stream.LastIndexOf(" l\nS", StringComparison.Ordinal) < stream.IndexOf("BT", StringComparison.Ordinal));
        }

        [Fact]
        public void A_heading_is_never_left_at_the_foot_of_a_page_without_its_table()
        {
            // The emailed AQS check of 900000003 (2026-09-24) ended page 1 with "File Quality -
            // Fail points" and opened page 2 with its table. Every fill level is tried, so the
            // one that leaves room for the heading and not the table is among them.
            for (var filler = 30; filler <= 70; filler++)
            {
                var blocks = Enumerable.Range(0, filler).Select(i => PdfBlock.Paragraph("Filler line " + i)).ToList();
                blocks.Add(PdfBlock.Heading("Fail points"));
                blocks.Add(PdfBlock.Table(Grid(3)));

                var streams = Pdf(blocks.ToArray()).Split(new[] { "endstream" }, StringSplitOptions.None);
                var heading = Array.FindIndex(streams, s => s.Contains("(Fail points) Tj"));
                var table = Array.FindIndex(streams, s => s.Contains("(Check) Tj"));

                Assert.True(heading == table, "with " + filler + " filler lines the heading and its table were split");
            }
        }

        [Fact]
        public void A_long_table_repeats_its_heading_on_the_next_page()
        {
            var pdf = Pdf(PdfBlock.Table(Grid(120)));

            Assert.DoesNotContain("/Count 1 ", pdf);
            var headings = pdf.Split(new[] { "(Check) Tj" }, StringSplitOptions.None).Length - 1;
            var pages = int.Parse(pdf.Substring(pdf.IndexOf("/Count ", StringComparison.Ordinal) + 7).Split(' ')[0]);

            Assert.True(pages > 1);
            Assert.Equal(pages, headings);
        }

        [Fact]
        public void A_long_cell_wraps_and_makes_its_row_taller_rather_than_running_on()
        {
            var table = new PdfTable(0.5, 0.5);
            table.Add(PdfCell.Of(string.Join(" ", Enumerable.Repeat("wide", 60))), PdfCell.Of("x"));

            var pdf = Pdf(PdfBlock.Table(table));
            var drawn = pdf.Split('\n').Count(l => l.StartsWith("(wide", StringComparison.Ordinal));
            Assert.True(drawn > 1, "the text wrapped onto several lines");
        }

        [Fact]
        public void Landscape_swaps_the_page_box()
        {
            var pdf = Encoding.GetEncoding(28591).GetString(PdfWriter.Build(new[] { PdfBlock.Paragraph("x") }, true));
            Assert.Contains("/MediaBox [0 0 841.89 595.28]", pdf);
        }

        [Fact]
        public void The_cell_text_reads_ticks_as_brackets_for_search()
        {
            var cell = new PdfCell();
            cell.Runs.Add(PdfRun.Tick(true));
            cell.Runs.Add(PdfRun.Of("PASS"));
            cell.Runs.Add(PdfRun.Tick(false));
            cell.Runs.Add(PdfRun.Of("FAIL"));

            Assert.Equal("[x]PASS[ ]FAIL", cell.Text);
        }
    }
}
