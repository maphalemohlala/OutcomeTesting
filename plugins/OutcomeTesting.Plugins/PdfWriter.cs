using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OutcomeTesting.Plugins
{
    /// <summary>What a block of the document is, which decides how it is drawn.</summary>
    public enum PdfBlockKind
    {
        /// <summary>The document's one title, largest and bold.</summary>
        Title,

        /// <summary>A section heading, bold.</summary>
        Heading,

        /// <summary>A run of body text, wrapped across as many lines as it needs.</summary>
        Paragraph,

        /// <summary>A label and its value on one line, the label bold.</summary>
        Field,

        /// <summary>A bulleted item.</summary>
        Bullet,

        /// <summary>Vertical space.</summary>
        Spacer,
    }

    /// <summary>One block of a generated document.</summary>
    public sealed class PdfBlock
    {
        /// <summary>What kind of block this is.</summary>
        public PdfBlockKind Kind { get; set; }

        /// <summary>The text, or the label for a <see cref="PdfBlockKind.Field"/>.</summary>
        public string Text { get; set; }

        /// <summary>The value, for a <see cref="PdfBlockKind.Field"/>.</summary>
        public string Value { get; set; }

        /// <summary>A title.</summary>
        public static PdfBlock Title(string text)
        {
            return new PdfBlock { Kind = PdfBlockKind.Title, Text = text };
        }

        /// <summary>A section heading.</summary>
        public static PdfBlock Heading(string text)
        {
            return new PdfBlock { Kind = PdfBlockKind.Heading, Text = text };
        }

        /// <summary>A paragraph of body text.</summary>
        public static PdfBlock Paragraph(string text)
        {
            return new PdfBlock { Kind = PdfBlockKind.Paragraph, Text = text };
        }

        /// <summary>A label and its value.</summary>
        public static PdfBlock Field(string label, string value)
        {
            return new PdfBlock { Kind = PdfBlockKind.Field, Text = label, Value = value };
        }

        /// <summary>A bulleted item.</summary>
        public static PdfBlock Bullet(string text)
        {
            return new PdfBlock { Kind = PdfBlockKind.Bullet, Text = text };
        }

        /// <summary>Vertical space.</summary>
        public static PdfBlock Spacer()
        {
            return new PdfBlock { Kind = PdfBlockKind.Spacer };
        }
    }

    /// <summary>
    /// Writes a small, valid PDF from a list of blocks, with no dependency on anything
    /// outside the base class library (Change 2, AD-164).
    ///
    /// <para>
    /// <b>Why this exists rather than a library.</b> A Dataverse plug-in runs sandboxed and
    /// in partial trust: there is no file system, and <c>System.Drawing</c> — which almost
    /// every net462 PDF library needs for font metrics — is not available. A library would
    /// also have to be ILMerged into this assembly, because the sandbox loads one file. The
    /// documents this solution produces are a page or two of text, so the part of a PDF
    /// library that is hard (images, colour spaces, embedded font subsetting) is the part
    /// nothing here uses.
    /// </para>
    /// <para>
    /// <b>No embedded fonts.</b> Helvetica and Helvetica-Bold are two of the fourteen
    /// standard PDF fonts that every reader is required to have, so the file names them and
    /// carries no font data at all. That is what keeps this to a few kilobytes and removes
    /// font licensing from the question entirely.
    /// </para>
    /// <para>
    /// <b>The widths tables are the whole trick.</b> Wrapping text means knowing how wide a
    /// string is, and with no drawing library to ask, the metrics are carried here — the
    /// standard Adobe widths for the two fonts, in units of 1/1000 em. Without them the only
    /// options are a monospace assumption, which wraps visibly wrongly, or no wrapping at
    /// all, which runs text off the page.
    /// </para>
    /// <para>
    /// <b>WinAnsi, and what happens outside it.</b> The encoding is WinAnsiEncoding, so a
    /// character it cannot represent is replaced rather than written raw — a client's name
    /// carrying a character outside it must not corrupt the file, and a visible '?' is
    /// honest where a broken document is not.
    /// </para>
    /// </summary>
    public static class PdfWriter
    {
        // A4 in PDF points (1/72"), which is what the page box is measured in.
        private const double PageWidth = 595.28;
        private const double PageHeight = 841.89;
        private const double Margin = 56.7;          // 20mm
        private const double ContentWidth = PageWidth - (2 * Margin);

        private const double TitleSize = 18;
        private const double HeadingSize = 12;
        private const double BodySize = 10;
        private const double LineGap = 1.35;         // multiplied by the font size
        private const double BlockGap = 6;
        private const double BulletIndent = 14;

        private const string Regular = "F1";
        private const string Bold = "F2";

        /// <summary>
        /// The document as PDF bytes. Never throws for content reasons: text that cannot be
        /// encoded is replaced, and a block with nothing in it is skipped.
        /// </summary>
        public static byte[] Build(IEnumerable<PdfBlock> blocks)
        {
            var pages = Layout(blocks ?? new PdfBlock[0]);
            return Assemble(pages);
        }

        // ------------------------------------------------------------------ layout

        /// <summary>One page's worth of drawing instructions, already positioned.</summary>
        private sealed class Line
        {
            public string Font;
            public double Size;
            public double X;
            public double Y;
            public string Text;
        }

        private static List<List<Line>> Layout(IEnumerable<PdfBlock> blocks)
        {
            var pages = new List<List<Line>>();
            var current = new List<Line>();
            var y = PageHeight - Margin;

            void NewPage()
            {
                pages.Add(current);
                current = new List<Line>();
                y = PageHeight - Margin;
            }

            void Emit(string font, double size, double x, string text)
            {
                // A line that would start below the margin begins a new page instead of
                // being drawn off the bottom, where a reader would never see it.
                if (y - (size * LineGap) < Margin)
                {
                    NewPage();
                }

                y -= size * LineGap;
                current.Add(new Line { Font = font, Size = size, X = x, Y = y, Text = text });
            }

            foreach (var block in blocks)
            {
                if (block == null)
                {
                    continue;
                }

                switch (block.Kind)
                {
                    case PdfBlockKind.Spacer:
                        y -= BlockGap;
                        break;

                    case PdfBlockKind.Title:
                        foreach (var line in Wrap(block.Text, Bold, TitleSize, ContentWidth))
                        {
                            Emit(Bold, TitleSize, Margin, line);
                        }

                        y -= BlockGap;
                        break;

                    case PdfBlockKind.Heading:
                        y -= BlockGap;
                        foreach (var line in Wrap(block.Text, Bold, HeadingSize, ContentWidth))
                        {
                            Emit(Bold, HeadingSize, Margin, line);
                        }

                        break;

                    case PdfBlockKind.Paragraph:
                        foreach (var line in Wrap(block.Text, Regular, BodySize, ContentWidth))
                        {
                            Emit(Regular, BodySize, Margin, line);
                        }

                        break;

                    case PdfBlockKind.Bullet:
                        var bulletLines = Wrap(block.Text, Regular, BodySize, ContentWidth - BulletIndent);
                        for (var i = 0; i < bulletLines.Count; i++)
                        {
                            if (i == 0)
                            {
                                Emit(Regular, BodySize, Margin, "•");
                                // The marker and the first line share a baseline, so the
                                // text is placed without advancing y again.
                                var marker = current[current.Count - 1];
                                current.Add(new Line
                                {
                                    Font = Regular,
                                    Size = BodySize,
                                    X = Margin + BulletIndent,
                                    Y = marker.Y,
                                    Text = bulletLines[i],
                                });
                            }
                            else
                            {
                                Emit(Regular, BodySize, Margin + BulletIndent, bulletLines[i]);
                            }
                        }

                        break;

                    case PdfBlockKind.Field:
                        var label = (block.Text ?? string.Empty) + ": ";
                        var labelWidth = Width(label, Bold, BodySize);
                        var valueLines = Wrap(block.Value, Regular, BodySize, ContentWidth - labelWidth);

                        if (valueLines.Count == 0)
                        {
                            valueLines.Add(string.Empty);
                        }

                        Emit(Bold, BodySize, Margin, label);
                        var labelLine = current[current.Count - 1];
                        current.Add(new Line
                        {
                            Font = Regular,
                            Size = BodySize,
                            X = Margin + labelWidth,
                            Y = labelLine.Y,
                            Text = valueLines[0],
                        });

                        for (var i = 1; i < valueLines.Count; i++)
                        {
                            Emit(Regular, BodySize, Margin + labelWidth, valueLines[i]);
                        }

                        break;
                }
            }

            pages.Add(current);
            return pages;
        }

        /// <summary>Breaks text into lines that fit, on spaces where it can.</summary>
        private static List<string> Wrap(string text, string font, double size, double maxWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return lines;
            }

            foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var words = paragraph.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0)
                {
                    lines.Add(string.Empty);
                    continue;
                }

                var line = new StringBuilder();
                foreach (var word in words)
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (Width(candidate, font, size) <= maxWidth || line.Length == 0)
                    {
                        // A single word wider than the line is kept whole rather than cut
                        // mid-word: a case reference broken across two lines is unreadable
                        // and unsearchable, and running slightly wide is the lesser fault.
                        line.Clear();
                        line.Append(candidate);
                    }
                    else
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                        line.Append(word);
                    }
                }

                if (line.Length > 0)
                {
                    lines.Add(line.ToString());
                }
            }

            return lines;
        }

        /// <summary>The width of a string at a size, in points.</summary>
        public static double Width(string text, string font, double size)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var widths = font == Bold ? BoldWidths : RegularWidths;
            double total = 0;

            foreach (var ch in text)
            {
                var index = ch - 32;
                total += index >= 0 && index < widths.Length ? widths[index] : 556;
            }

            return total * size / 1000.0;
        }

        // ------------------------------------------------------------------ assembly

        private static byte[] Assemble(List<List<Line>> pages)
        {
            // Object 1 catalog, 2 pages, 3 regular font, 4 bold font, then a content stream
            // and a page object per page.
            var objects = new List<string>();
            var pageObjectIds = new List<int>();

            var firstPageObject = 5 + pages.Count;
            for (var i = 0; i < pages.Count; i++)
            {
                pageObjectIds.Add(firstPageObject + i);
            }

            var kids = new StringBuilder();
            foreach (var id in pageObjectIds)
            {
                kids.Append(id.ToString(CultureInfo.InvariantCulture)).Append(" 0 R ");
            }

            objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
            objects.Add("<< /Type /Pages /Kids [" + kids.ToString().Trim() + "] /Count "
                + pages.Count.ToString(CultureInfo.InvariantCulture) + " >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

            var streams = new List<string>();
            foreach (var page in pages)
            {
                streams.Add(Content(page));
            }

            // Content streams occupy objects 5 .. 4+pages.Count.
            foreach (var stream in streams)
            {
                objects.Add("<< /Length " + stream.Length.ToString(CultureInfo.InvariantCulture)
                    + " >>\nstream\n" + stream + "\nendstream");
            }

            for (var i = 0; i < pages.Count; i++)
            {
                objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "
                    + Num(PageWidth) + " " + Num(PageHeight) + "] "
                    + "/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents "
                    + (5 + i).ToString(CultureInfo.InvariantCulture) + " 0 R >>");
            }

            var pdf = new StringBuilder();
            pdf.Append("%PDF-1.4\n");

            var offsets = new List<int>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(pdf.Length);
                pdf.Append((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }

            var xref = pdf.Length;
            pdf.Append("xref\n0 ").Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
            pdf.Append("0000000000 65535 f \n");
            foreach (var offset in offsets)
            {
                pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }

            pdf.Append("trailer\n<< /Size ")
                .Append((objects.Count + 1).ToString(CultureInfo.InvariantCulture))
                .Append(" /Root 1 0 R >>\nstartxref\n")
                .Append(xref.ToString(CultureInfo.InvariantCulture))
                .Append("\n%%EOF");

            // Latin-1 rather than UTF-8: every byte written above is within WinAnsi by
            // construction, and a multi-byte encoding here would shift every xref offset
            // computed from string lengths.
            return Encoding.GetEncoding(28591).GetBytes(pdf.ToString());
        }

        private static string Content(List<Line> lines)
        {
            var content = new StringBuilder();
            content.Append("BT\n");

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line.Text))
                {
                    continue;
                }

                content.Append("/").Append(line.Font).Append(' ').Append(Num(line.Size)).Append(" Tf\n");
                content.Append("1 0 0 1 ").Append(Num(line.X)).Append(' ').Append(Num(line.Y)).Append(" Tm\n");
                content.Append('(').Append(Escape(line.Text)).Append(") Tj\n");
            }

            content.Append("ET");
            return content.ToString();
        }

        /// <summary>
        /// Text as a PDF literal string.
        ///
        /// The backslash goes first: escaping it last would re-escape the backslashes the
        /// bracket replacements had just written. Anything outside WinAnsi becomes '?',
        /// because a character this file cannot represent must not be written raw into it.
        /// </summary>
        private static string Escape(string text)
        {
            var escaped = new StringBuilder(text.Length + 8);

            foreach (var ch in text)
            {
                if (ch == '\\' || ch == '(' || ch == ')')
                {
                    escaped.Append('\\').Append(ch);
                }
                else if (ch == '•')
                {
                    // The bullet, which WinAnsi carries at 149.
                    escaped.Append("\\225");
                }
                else if (ch >= ' ' && ch <= '~')
                {
                    escaped.Append(ch);
                }
                else
                {
                    escaped.Append('?');
                }
            }

            return escaped.ToString();
        }

        private static string Num(double value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ metrics

        /// <summary>Adobe's standard Helvetica widths, characters 32 to 126, per 1000 em.</summary>
        private static readonly int[] RegularWidths =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
        };

        /// <summary>Adobe's standard Helvetica-Bold widths, characters 32 to 126.</summary>
        private static readonly int[] BoldWidths =
        {
            278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
            333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
        };
    }
}
