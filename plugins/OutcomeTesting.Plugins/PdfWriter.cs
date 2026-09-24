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

        /// <summary>
        /// A heading one level below <see cref="Heading"/>, bold and a point smaller.
        ///
        /// Added for the completed check (AD-165), which has two levels of structure: the
        /// check, and the sections of the checklist inside it. Drawing both with
        /// <see cref="Heading"/> would have made a section indistinguishable from the check
        /// it belongs to.
        /// </summary>
        Subheading,

        /// <summary>A run of body text, wrapped across as many lines as it needs.</summary>
        Paragraph,

        /// <summary>A label and its value on one line, the label bold.</summary>
        Field,

        /// <summary>A bulleted item.</summary>
        Bullet,

        /// <summary>
        /// One row of a two-column table: a test point on the left and its answer in a
        /// column of its own on the right.
        ///
        /// Added so the para-planner's attachment can be the Checker Checklist rather than a
        /// description of it (project owner, 2026-09-22: "the pdf that goes to paraplanners
        /// needs to match the pdf form in the reviews"). The form's own shape is a ruled
        /// table of test points against a tick column, and a <see cref="Field"/> - which puts
        /// the answer immediately after the question, wherever that falls - cannot line
        /// answers up for a reader scanning down them.
        /// </summary>
        Row,

        /// <summary>
        /// The column headings of a <see cref="Row"/> table: the same two columns, bold,
        /// with a rule under them.
        /// </summary>
        TableHead,

        /// <summary>A hairline across the content width, closing a table.</summary>
        Rule,

        /// <summary>
        /// A bold line at body size, with no value after it.
        ///
        /// The document's subsection rows - "E1. Client Objectives &amp; Information (COBS
        /// 9.2)" inside Suitability core checks - which are shaded bands on the form. Drawn as
        /// a <see cref="Paragraph"/> first, where they were indistinguishable from the Outcome
        /// lens line beneath them; and a <see cref="Field"/> would have put a colon on the end
        /// of a heading. <see cref="Subheading"/> is a size up and is the BLOCK's, so using it
        /// here would make a subsection look like a sibling of the block containing it.
        /// </summary>
        Label,

        /// <summary>Vertical space.</summary>
        Spacer,

        /// <summary>
        /// A ruled table of cells, which may hold tick boxes (project owner, 2026-09-24: the
        /// emailed PDF "needs to match the pdf form in the system"). The review page prints
        /// every block of the Checker Checklist as a table with every cell ruled, tick
        /// columns of empty and ticked squares, shaded heading rows and shaded label cells;
        /// <see cref="Row"/> could only put an answer's WORD in a column, which is a
        /// description of the form rather than the form.
        /// </summary>
        Table,

        /// <summary>A small line of body text, as the page's document footer line is set.</summary>
        Note,
    }

    /// <summary>
    /// One piece of a table cell's content: a run of text, a tick box, or a line break.
    /// </summary>
    public sealed class PdfRun
    {
        public string Text { get; set; }

        public bool Bold { get; set; }

        public bool Italic { get; set; }

        /// <summary>A tick box rather than text: false draws it empty, true ticked.</summary>
        public bool? Box { get; set; }

        /// <summary>Ends the line here.</summary>
        public bool Break { get; set; }

        public static PdfRun Of(string text, bool bold = false, bool italic = false)
        {
            return new PdfRun { Text = text, Bold = bold, Italic = italic };
        }

        public static PdfRun Tick(bool ticked)
        {
            return new PdfRun { Box = ticked };
        }

        public static PdfRun LineBreak()
        {
            return new PdfRun { Break = true };
        }
    }

    /// <summary>One cell of a <see cref="PdfTable"/>.</summary>
    public sealed class PdfCell
    {
        public PdfCell()
        {
            Runs = new List<PdfRun>();
            Span = 1;
        }

        public List<PdfRun> Runs { get; private set; }

        /// <summary>How many columns the cell runs across.</summary>
        public int Span { get; set; }

        /// <summary>A grey fill, 0 black to 1 white, or null for none.</summary>
        public double? Fill { get; set; }

        /// <summary>Centred rather than set from the left, as a tick column is.</summary>
        public bool Centre { get; set; }

        /// <summary>The cell's text, runs joined and ticks written [x] / [ ], for tests and search.</summary>
        public string Text
        {
            get
            {
                var text = new StringBuilder();
                foreach (var run in Runs)
                {
                    if (run.Box.HasValue)
                    {
                        text.Append(run.Box.Value ? "[x]" : "[ ]");
                    }
                    else if (run.Break)
                    {
                        text.Append('\n');
                    }
                    else
                    {
                        text.Append(run.Text);
                    }
                }

                return text.ToString();
            }
        }

        /// <summary>Plain text.</summary>
        public static PdfCell Of(string text, int span = 1)
        {
            var cell = new PdfCell { Span = span };
            AddText(cell, text, false);
            return cell;
        }

        /// <summary>Bold on a light fill, as the form sets a label cell.</summary>
        public static PdfCell Label(string text, int span = 1)
        {
            var cell = new PdfCell { Span = span, Fill = LabelFill };
            AddText(cell, text, true);
            return cell;
        }

        /// <summary>Bold on the heading fill, as the form sets a column heading.</summary>
        public static PdfCell Head(string text, int span = 1, bool centre = false)
        {
            var cell = new PdfCell { Span = span, Fill = HeadFill, Centre = centre };
            AddText(cell, text, true);
            return cell;
        }

        /// <summary>A lone tick box, centred.</summary>
        public static PdfCell Box(bool ticked)
        {
            var cell = new PdfCell { Centre = true };
            cell.Runs.Add(PdfRun.Tick(ticked));
            return cell;
        }

        /// <summary>A cell with nothing in it.</summary>
        public static PdfCell Blank(int span = 1)
        {
            return new PdfCell { Span = span };
        }

        public const double HeadFill = 0.85;
        public const double BandFill = 0.9;
        public const double LabelFill = 0.95;

        private static void AddText(PdfCell cell, string text, bool bold)
        {
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    cell.Runs.Add(PdfRun.LineBreak());
                }

                cell.Runs.Add(PdfRun.Of(lines[i], bold));
            }
        }
    }

    /// <summary>One row of a <see cref="PdfTable"/>.</summary>
    public sealed class PdfTableRow
    {
        public PdfTableRow(params PdfCell[] cells)
        {
            Cells = new List<PdfCell>(cells ?? new PdfCell[0]);
        }

        public List<PdfCell> Cells { get; private set; }

        /// <summary>A heading row, drawn again at the top of a page the table runs onto.</summary>
        public bool Header { get; set; }
    }

    /// <summary>A table: column widths as fractions of the line, then its rows.</summary>
    public sealed class PdfTable
    {
        public PdfTable(params double[] widths)
        {
            Widths = widths ?? new double[0];
            Rows = new List<PdfTableRow>();
            Ruled = true;
        }

        public double[] Widths { get; private set; }

        public List<PdfTableRow> Rows { get; private set; }

        /// <summary>Every cell ruled, as the checklist's tables are. False for a layout grid.</summary>
        public bool Ruled { get; set; }

        public PdfTableRow Add(params PdfCell[] cells)
        {
            var row = new PdfTableRow(cells);
            Rows.Add(row);
            return row;
        }

        public PdfTableRow AddHeader(params PdfCell[] cells)
        {
            var row = Add(cells);
            row.Header = true;
            return row;
        }
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

        /// <summary>The table, for a <see cref="PdfBlockKind.Table"/>.</summary>
        public PdfTable Grid { get; set; }

        /// <summary>A ruled table.</summary>
        public static PdfBlock Table(PdfTable table)
        {
            return new PdfBlock { Kind = PdfBlockKind.Table, Grid = table };
        }

        /// <summary>A small line of text.</summary>
        public static PdfBlock Note(string text)
        {
            return new PdfBlock { Kind = PdfBlockKind.Note, Text = text };
        }

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

        /// <summary>A heading one level below <see cref="Heading(string)"/>.</summary>
        public static PdfBlock Subheading(string text)
        {
            return new PdfBlock { Kind = PdfBlockKind.Subheading, Text = text };
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

        /// <summary>One row of a two-column table: a test point and its answer.</summary>
        public static PdfBlock Row(string text, string value)
        {
            return new PdfBlock { Kind = PdfBlockKind.Row, Text = text, Value = value };
        }

        /// <summary>The column headings above a run of <see cref="Row(string, string)"/>.</summary>
        public static PdfBlock TableHead(string text, string value)
        {
            return new PdfBlock { Kind = PdfBlockKind.TableHead, Text = text, Value = value };
        }

        /// <summary>A hairline across the content width.</summary>
        public static PdfBlock Rule()
        {
            return new PdfBlock { Kind = PdfBlockKind.Rule };
        }

        /// <summary>A bold line at body size, for the document's subsection rows.</summary>
        public static PdfBlock Label(string text)
        {
            return new PdfBlock { Kind = PdfBlockKind.Label, Text = text };
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
    /// <b>WinAnsi, and what happens outside it.</b> The encoding is WinAnsiEncoding, and
    /// everything it can represent is written as an octal escape — the dashes, the smart
    /// quotes, the accented letters and the pound sign included. Only a character the
    /// encoding has no code for at all is replaced: a client's name carrying one must not
    /// corrupt the file, and a visible '?' is honest where a broken document is not. The
    /// replacement is the last resort, not the first.
    /// </para>
    /// </summary>
    public static class PdfWriter
    {
        // A4 in PDF points (1/72"), which is what the page box is measured in. Portrait unless
        // a document asks for landscape, which the remediation form does: it is eight columns
        // wide on the page it copies, and portrait would squeeze each to a word a line.
        private const double A4Short = 595.28;
        private const double A4Long = 841.89;
        private const double Margin = 42.5;          // 15mm

        /// <summary>A table's body text, a point under the running text as the page sets it.</summary>
        private const double TableSize = 9;

        /// <summary>Space inside a table cell, each side.</summary>
        private const double CellPad = 4;

        /// <summary>A tick box's side, and the gap after it before its label.</summary>
        private const double BoxSide = 8;
        private const double BoxGap = 3.5;

        /// <summary>The grey the text of a <see cref="PdfBlockKind.Note"/> is set in.</summary>
        private const double NoteSize = 8;

        private const double TitleSize = 18;
        private const double HeadingSize = 12;

        // Between a heading and the body, because a Field's LABEL is also bold at body size:
        // at 10 a subheading was indistinguishable from the questions beneath it, which is
        // the one job it has. Found by rendering a sample and looking at it.
        private const double SubheadingSize = 11;

        private const double BodySize = 10;
        private const double LineGap = 1.35;         // multiplied by the font size
        private const double BlockGap = 6;
        private const double BulletIndent = 14;

        /// <summary>
        /// The width of a Row table's answer column. 120pt holds the longest answer the
        /// checklist can record - "Insufficient evidence" at body size is 96pt - with room
        /// for the padding, so no answer wraps and the column reads as a column.
        /// </summary>
        private const double AnswerColumn = 120;

        /// <summary>Space between the two columns, so a long test point cannot touch its answer.</summary>
        private const double ColumnGap = 10;

        /// <summary>A rule's thickness, in points. Hairline: the form's tables are ruled, not boxed.</summary>
        private const double RuleWeight = 0.5;

        private const string Regular = "F1";
        private const string Bold = "F2";
        private const string Italic = "F3";
        private const string BoldItalic = "F4";

        /// <summary>
        /// The document as PDF bytes. Never throws for content reasons: text that cannot be
        /// encoded is replaced, and a block with nothing in it is skipped.
        /// </summary>
        public static byte[] Build(IEnumerable<PdfBlock> blocks)
        {
            return Build(blocks, false);
        }

        /// <summary>As <see cref="Build(IEnumerable{PdfBlock})"/>, on landscape pages when asked.</summary>
        public static byte[] Build(IEnumerable<PdfBlock> blocks, bool landscape)
        {
            var width = landscape ? A4Long : A4Short;
            var height = landscape ? A4Short : A4Long;
            var pages = Layout(blocks ?? new PdfBlock[0], width, height);
            return Assemble(pages, width, height);
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

            /// <summary>
            /// A horizontal rule rather than a run of text. <see cref="Text"/> is null on one
            /// of these and <see cref="Span"/> is its width.
            ///
            /// Carried on the same list as the text so both are laid out by the one pass that
            /// knows where the page break falls - a rule positioned by a second pass would
            /// have to re-derive that, and would be wrong the first time a table straddled
            /// two pages.
            /// </summary>
            public bool IsRule;

            /// <summary>How far a rule runs, in points.</summary>
            public double Span;

            /// <summary>
            /// A rectangle - a table cell or a tick box - at X, Y (its lower left corner) of
            /// <see cref="Span"/> by <see cref="Height"/>, filled and/or stroked.
            /// </summary>
            public bool IsRect;

            public double Height;

            /// <summary>Fill grey, 0 black to 1 white; negative for no fill.</summary>
            public double Fill = -1;

            /// <summary>Stroke grey; negative for no outline.</summary>
            public double Stroke = -1;

            public double Weight = RuleWeight;

            /// <summary>A tick mark drawn in a box whose lower left corner is X, Y.</summary>
            public bool IsTick;
        }

        private static List<List<Line>> Layout(IEnumerable<PdfBlock> blocks, double pageWidth, double pageHeight)
        {
            var pages = new List<List<Line>>();
            var current = new List<Line>();
            var y = pageHeight - Margin;
            var ContentWidth = pageWidth - (2 * Margin);

            void NewPage()
            {
                pages.Add(current);
                current = new List<Line>();
                y = pageHeight - Margin;
            }

            void DrawRule()
            {
                // A rule sits just under the line above it rather than claiming a line of its
                // own, so a ruled table is no taller than an unruled one.
                var gap = BodySize * 0.35;
                if (y - gap < Margin)
                {
                    NewPage();
                    return;
                }

                y -= gap;
                current.Add(new Line { IsRule = true, X = Margin, Y = y, Span = ContentWidth });
            }

            /*
             * A table is laid out a row at a time, and a row is never split: a test point
             * broken across two pages loses its tick column on one of them. A row that will
             * not fit starts a new page, and the table's heading rows are drawn again at the
             * top of it, which is what the browser's print does with the form's thead.
             */
            void DrawTable(PdfTable table)
            {
                var widths = ColumnWidths(table.Widths, ContentWidth);

                var header = new List<PdfTableRow>();
                foreach (var row in table.Rows)
                {
                    if (!row.Header)
                    {
                        break;
                    }

                    header.Add(row);
                }

                double headerHeight = 0;
                foreach (var row in header)
                {
                    headerHeight += RowHeight(row, widths);
                }

                y -= BlockGap / 2;
                var drawnOnPage = 0;

                for (var i = 0; i < table.Rows.Count; i++)
                {
                    var row = table.Rows[i];
                    var height = RowHeight(row, widths);

                    // Keep the heading with the first row under it, so a table never opens
                    // with its column headings stranded at the foot of a page.
                    var needed = height;
                    if (i == 0 && header.Count > 0 && header.Count < table.Rows.Count)
                    {
                        needed = headerHeight + RowHeight(table.Rows[header.Count], widths);
                    }

                    if (y - needed < Margin && (drawnOnPage > 0 || current.Count > 0))
                    {
                        NewPage();
                        drawnOnPage = 0;

                        if (!row.Header && header.Count > 0)
                        {
                            foreach (var repeat in header)
                            {
                                PlaceRow(repeat, widths, RowHeight(repeat, widths), table.Ruled);
                            }
                        }
                    }

                    PlaceRow(row, widths, height, table.Ruled);
                    drawnOnPage++;
                }

                y -= BlockGap / 2;
            }

            void PlaceRow(PdfTableRow row, double[] widths, double height, bool ruled)
            {
                var top = y;
                var column = 0;
                var lineHeight = TableSize * LineGap;

                foreach (var cell in row.Cells)
                {
                    if (column >= widths.Length)
                    {
                        break;
                    }

                    var x = Margin;
                    for (var c = 0; c < column; c++)
                    {
                        x += widths[c];
                    }

                    var span = Math.Max(1, Math.Min(cell.Span, widths.Length - column));
                    double width = 0;
                    for (var c = column; c < column + span; c++)
                    {
                        width += widths[c];
                    }

                    column += span;

                    current.Add(new Line
                    {
                        IsRect = true,
                        X = x,
                        Y = top - height,
                        Span = width,
                        Height = height,
                        Fill = cell.Fill.HasValue ? cell.Fill.Value : -1,
                        Stroke = ruled ? 0 : -1,
                    });

                    var lines = Flow(cell, width - (2 * CellPad));
                    for (var l = 0; l < lines.Count; l++)
                    {
                        var baseline = top - CellPad - (l * lineHeight) - (TableSize * 0.95);
                        var offset = cell.Centre
                            ? Math.Max(0, (width - (2 * CellPad) - lines[l].Width) / 2)
                            : 0;

                        foreach (var piece in lines[l].Pieces)
                        {
                            var px = x + CellPad + offset + piece.X;
                            if (piece.Box.HasValue)
                            {
                                current.Add(new Line
                                {
                                    IsRect = true,
                                    X = px,
                                    Y = baseline - 1,
                                    Span = BoxSide,
                                    Height = BoxSide,
                                    Fill = 1,
                                    Stroke = 0.45,
                                    Weight = 0.6,
                                });

                                if (piece.Box.Value)
                                {
                                    current.Add(new Line { IsTick = true, X = px, Y = baseline - 1 });
                                }
                            }
                            else if (!string.IsNullOrEmpty(piece.Text))
                            {
                                current.Add(new Line
                                {
                                    Font = piece.Font,
                                    Size = TableSize,
                                    X = px,
                                    Y = baseline,
                                    Text = piece.Text,
                                });
                            }
                        }
                    }
                }

                y = top - height;
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

            /*
             * What has to share a page with a heading: the intro line straight under it, if
             * there is one, and the opening of the table it heads - that table's heading rows
             * and its first row. Without it a heading can land alone at the foot of a page with
             * its table overleaf, which is how "File Quality - Fail points" first arrived.
             */
            double Opening(List<PdfBlock> list, int from)
            {
                double needed = 0;
                for (var j = from; j < list.Count; j++)
                {
                    var next = list[j];
                    if (next == null)
                    {
                        continue;
                    }

                    if (next.Kind == PdfBlockKind.Paragraph)
                    {
                        needed += Wrap(next.Text, Regular, BodySize, ContentWidth).Count * BodySize * LineGap;
                        continue;
                    }

                    if (next.Kind == PdfBlockKind.Table && next.Grid != null && next.Grid.Rows.Count > 0)
                    {
                        var widths = ColumnWidths(next.Grid.Widths, ContentWidth);
                        needed += BlockGap / 2;
                        foreach (var row in next.Grid.Rows)
                        {
                            needed += RowHeight(row, widths);
                            if (!row.Header)
                            {
                                break;
                            }
                        }
                    }

                    return needed;
                }

                return needed;
            }

            var ordered = new List<PdfBlock>(blocks);
            for (var index = 0; index < ordered.Count; index++)
            {
                var block = ordered[index];
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
                        var headingLines = Wrap(block.Text, Bold, HeadingSize, ContentWidth);
                        var opening = BlockGap + (headingLines.Count * HeadingSize * LineGap) + Opening(ordered, index + 1);
                        if (y - opening < Margin && current.Count > 0)
                        {
                            NewPage();
                        }

                        y -= BlockGap;
                        foreach (var line in headingLines)
                        {
                            Emit(Bold, HeadingSize, Margin, line);
                        }

                        break;

                    case PdfBlockKind.Subheading:
                        // Half a block gap rather than a whole one: it belongs to the heading
                        // above it, and spacing it equally would read as a sibling.
                        y -= BlockGap / 2;
                        foreach (var line in Wrap(block.Text, Bold, SubheadingSize, ContentWidth))
                        {
                            Emit(Bold, SubheadingSize, Margin, line);
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

                    case PdfBlockKind.Rule:
                        DrawRule();
                        break;

                    case PdfBlockKind.Label:
                        // A little air above it, so a subsection reads as opening something
                        // rather than as one more row of the table above.
                        y -= BlockGap / 2;
                        foreach (var line in Wrap(block.Text, Bold, BodySize, ContentWidth))
                        {
                            Emit(Bold, BodySize, Margin, line);
                        }

                        break;

                    case PdfBlockKind.TableHead:
                    case PdfBlockKind.Row:
                        {
                            var head = block.Kind == PdfBlockKind.TableHead;
                            var font = head ? Bold : Regular;
                            var cellWidth = ContentWidth - AnswerColumn - ColumnGap;

                            var cells = Wrap(block.Text, font, BodySize, cellWidth);
                            if (cells.Count == 0)
                            {
                                cells.Add(string.Empty);
                            }

                            // The answer is not wrapped. AnswerColumn is sized to hold the
                            // longest the checklist can record, and an answer that did wrap
                            // would put its second line against the NEXT test point - which
                            // is worse than running a few points wide.
                            var answer = block.Value ?? string.Empty;

                            Emit(font, BodySize, Margin, cells[0]);
                            var first = current[current.Count - 1];
                            if (answer.Length > 0)
                            {
                                current.Add(new Line
                                {
                                    Font = font,
                                    Size = BodySize,
                                    X = Margin + cellWidth + ColumnGap,
                                    Y = first.Y,
                                    Text = answer,
                                });
                            }

                            for (var i = 1; i < cells.Count; i++)
                            {
                                Emit(font, BodySize, Margin, cells[i]);
                            }

                            // Under the headings, so the table has a head; and under every row,
                            // so a reader following a long test point across to its answer has
                            // a line to follow. That is how the form itself is ruled.
                            DrawRule();
                            break;
                        }

                    case PdfBlockKind.Note:
                        foreach (var line in Wrap(block.Text, Regular, NoteSize, ContentWidth))
                        {
                            Emit(Regular, NoteSize, Margin, line);
                        }

                        DrawRule();
                        break;

                    case PdfBlockKind.Table:
                        if (block.Grid != null && block.Grid.Rows.Count > 0)
                        {
                            DrawTable(block.Grid);
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

        // ------------------------------------------------------------------ tables

        /// <summary>One positioned piece of a cell line: a run of text, or a tick box.</summary>
        private sealed class Piece
        {
            public double X;
            public string Text;
            public string Font;
            public bool? Box;
        }

        private sealed class FlowLine
        {
            public readonly List<Piece> Pieces = new List<Piece>();
            public double Width;
        }

        /// <summary>The columns in points, from fractions that need not sum to exactly one.</summary>
        private static double[] ColumnWidths(double[] fractions, double contentWidth)
        {
            if (fractions == null || fractions.Length == 0)
            {
                return new[] { contentWidth };
            }

            double total = 0;
            foreach (var fraction in fractions)
            {
                total += Math.Max(0, fraction);
            }

            var widths = new double[fractions.Length];
            for (var i = 0; i < fractions.Length; i++)
            {
                widths[i] = total <= 0
                    ? contentWidth / fractions.Length
                    : contentWidth * Math.Max(0, fractions[i]) / total;
            }

            return widths;
        }

        private static double RowHeight(PdfTableRow row, double[] widths)
        {
            var lineHeight = TableSize * LineGap;
            var tallest = 1;
            var column = 0;

            foreach (var cell in row.Cells)
            {
                if (column >= widths.Length)
                {
                    break;
                }

                var span = Math.Max(1, Math.Min(cell.Span, widths.Length - column));
                double width = 0;
                for (var c = column; c < column + span; c++)
                {
                    width += widths[c];
                }

                column += span;
                tallest = Math.Max(tallest, Flow(cell, width - (2 * CellPad)).Count);
            }

            return (2 * CellPad) + (tallest * lineHeight);
        }

        /// <summary>
        /// Sets a cell's runs into lines no wider than the cell.
        ///
        /// Words wrap on spaces as <see cref="Wrap"/> wraps them. A tick box travels with the
        /// label after it - "[ ] PASS" never breaks between the box and its word, because a
        /// box at the end of one line and its label at the start of the next reads as two
        /// different options.
        /// </summary>
        private static List<FlowLine> Flow(PdfCell cell, double width)
        {
            var lines = new List<FlowLine>();
            var line = new FlowLine();
            double cursor = 0;
            var afterBox = false;

            void Finish()
            {
                line.Width = cursor;
                lines.Add(line);
                line = new FlowLine();
                cursor = 0;
                afterBox = false;
            }

            var runs = cell.Runs;
            for (var r = 0; r < runs.Count; r++)
            {
                var run = runs[r];

                if (run.Break)
                {
                    Finish();
                    continue;
                }

                if (run.Box.HasValue)
                {
                    // The box and the first word of the label after it are measured together.
                    var gap = cursor > 0 ? BoxGap * 3 : 0;
                    var next = r + 1 < runs.Count ? runs[r + 1] : null;
                    var label = next != null && !next.Box.HasValue && !next.Break
                        ? FirstWord(next.Text)
                        : null;
                    var need = gap + BoxSide + (label == null ? 0 : BoxGap + Width(label, FontFor(next), TableSize));

                    if (cursor > 0 && cursor + need > width)
                    {
                        Finish();
                        gap = 0;
                    }

                    cursor += gap;
                    line.Pieces.Add(new Piece { X = cursor, Box = run.Box });
                    cursor += BoxSide;
                    afterBox = true;
                    continue;
                }

                var font = FontFor(run);
                var space = Width(" ", font, TableSize);
                var words = (run.Text ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                for (var w = 0; w < words.Length; w++)
                {
                    var word = words[w];
                    var wordWidth = Width(word, font, TableSize);
                    var lead = cursor <= 0 ? 0 : (afterBox ? BoxGap : space);

                    // After a box the first word stays with it, whatever the width: the pair
                    // was measured as one before the box was placed.
                    if (cursor > 0 && !afterBox && cursor + lead + wordWidth > width)
                    {
                        Finish();
                        lead = 0;
                    }

                    var last = line.Pieces.Count > 0 ? line.Pieces[line.Pieces.Count - 1] : null;
                    if (last != null && last.Text != null && last.Font == font && lead == space && w > 0)
                    {
                        last.Text += " " + word;
                    }
                    else
                    {
                        line.Pieces.Add(new Piece { X = cursor + lead, Text = word, Font = font });
                    }

                    cursor += lead + wordWidth;
                    afterBox = false;
                }
            }

            if (line.Pieces.Count > 0 || lines.Count == 0)
            {
                Finish();
            }

            return lines;
        }

        private static string FirstWord(string text)
        {
            var words = (text ?? string.Empty).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0 ? null : words[0];
        }

        private static string FontFor(PdfRun run)
        {
            if (run.Bold)
            {
                return run.Italic ? BoldItalic : Bold;
            }

            return run.Italic ? Italic : Regular;
        }

        /// <summary>The width of a string at a size, in points.</summary>
        public static double Width(string text, string font, double size)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            // The oblique faces share their upright faces' widths; Adobe's metrics say so.
            var widths = font == Bold || font == BoldItalic ? BoldWidths : RegularWidths;
            double total = 0;

            foreach (var ch in text)
            {
                var index = ch - 32;
                total += index >= 0 && index < widths.Length ? widths[index] : 556;
            }

            return total * size / 1000.0;
        }

        // ------------------------------------------------------------------ assembly

        private static byte[] Assemble(List<List<Line>> pages, double pageWidth, double pageHeight)
        {
            // Object 1 catalog, 2 pages, 3 to 6 the four faces (regular, bold, italic, bold
            // italic), then a content stream and a page object per page.
            var objects = new List<string>();
            var pageObjectIds = new List<int>();

            var firstPageObject = FirstStreamObject + pages.Count;
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
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-BoldOblique /Encoding /WinAnsiEncoding >>");

            var streams = new List<string>();
            foreach (var page in pages)
            {
                streams.Add(Content(page));
            }

            // Content streams occupy objects FirstStreamObject onwards, one per page.
            foreach (var stream in streams)
            {
                objects.Add("<< /Length " + stream.Length.ToString(CultureInfo.InvariantCulture)
                    + " >>\nstream\n" + stream + "\nendstream");
            }

            for (var i = 0; i < pages.Count; i++)
            {
                objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "
                    + Num(pageWidth) + " " + Num(pageHeight) + "] "
                    + "/Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R /F4 6 0 R >> >> /Contents "
                    + (FirstStreamObject + i).ToString(CultureInfo.InvariantCulture) + " 0 R >>");
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

        /// <summary>The object number of the first page's content stream, after the fonts.</summary>
        private const int FirstStreamObject = 7;

        private static string Content(List<Line> lines)
        {
            var content = new StringBuilder();

            /*
             * Cells and boxes first, so a shaded cell sits under its text rather than over
             * it, then the ticks inside the boxes. All outside the text object, for the reason
             * the rules below give.
             */
            foreach (var line in lines)
            {
                if (!line.IsRect)
                {
                    continue;
                }

                var fill = line.Fill >= 0;
                var stroke = line.Stroke >= 0;
                if (!fill && !stroke)
                {
                    continue;
                }

                if (fill)
                {
                    content.Append(Num(line.Fill)).Append(" g\n");
                }

                if (stroke)
                {
                    content.Append(Num(line.Weight)).Append(" w ").Append(Num(line.Stroke)).Append(" G\n");
                }

                content.Append(Num(line.X)).Append(' ').Append(Num(line.Y)).Append(' ')
                    .Append(Num(line.Span)).Append(' ').Append(Num(line.Height))
                    .Append(fill && stroke ? " re B\n" : fill ? " re f\n" : " re S\n");
            }

            foreach (var line in lines)
            {
                if (!line.IsTick)
                {
                    continue;
                }

                // A check mark inside the box: down to the lower third, then up to the far corner.
                content.Append("1.2 w 0 G 1 J 1 j\n");
                content.Append(Num(line.X + 1.6)).Append(' ').Append(Num(line.Y + 4.3)).Append(" m\n");
                content.Append(Num(line.X + 3.3)).Append(' ').Append(Num(line.Y + 2.1)).Append(" l\n");
                content.Append(Num(line.X + 6.7)).Append(' ').Append(Num(line.Y + 6.6)).Append(" l\nS\n");
                content.Append("0 J 0 j\n");
            }

            content.Append("0 g\n");

            /*
             * Rules first, outside the text object. BT ... ET may hold text operators only, so
             * a path drawn inside one is a malformed content stream - some readers recover and
             * some show a blank page. They are drawn before the text rather than after because
             * nothing overlaps: a rule sits in the gap under its row.
             */
            foreach (var line in lines)
            {
                if (!line.IsRule)
                {
                    continue;
                }

                content.Append(Num(RuleWeight)).Append(" w 0.8 G\n");
                content.Append(Num(line.X)).Append(' ').Append(Num(line.Y)).Append(" m\n");
                content.Append(Num(line.X + line.Span)).Append(' ').Append(Num(line.Y)).Append(" l\nS\n");
            }

            content.Append("BT\n");

            foreach (var line in lines)
            {
                if (line.IsRule || line.IsRect || line.IsTick || string.IsNullOrEmpty(line.Text))
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
        /// bracket replacements had just written.
        ///
        /// Everything WinAnsi can represent is written as an OCTAL ESCAPE rather than a raw
        /// byte, so the file stays ASCII and every xref offset computed from a string length
        /// stays right. Only a character the encoding genuinely has no code for becomes '?'.
        ///
        /// Until 2026-09-22 everything above '~' did, and the bullet was the single exception.
        /// That put a visible '?' into the para-planner's copy wherever ordinary British
        /// punctuation appeared - an en dash in "Tax check – insufficient evidence", a curly
        /// apostrophe in "the client’s actual needs". Both are in WinAnsi; neither was being
        /// written. Found by reading a document the deployed build had actually produced.
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
                else if (ch >= ' ' && ch <= '~')
                {
                    escaped.Append(ch);
                }
                else
                {
                    var code = WinAnsi(ch);
                    if (code == 0)
                    {
                        escaped.Append('?');
                    }
                    else
                    {
                        escaped.Append('\\').Append(Convert.ToString(code, 8).PadLeft(3, '0'));
                    }
                }
            }

            return escaped.ToString();
        }

        /// <summary>
        /// The WinAnsiEncoding code for a character above ASCII, or 0 when the encoding has
        /// no glyph for it.
        ///
        /// <para>
        /// 0xA0-0xFF is Latin-1 and maps to itself, which covers the accented names and the
        /// pound sign. 0x80-0x9F is the Windows block - the smart quotes, the dashes, the
        /// bullet, the ellipsis - and those do NOT sit at their Unicode code points, so each
        /// one has to be named. Getting this wrong is silent: the character simply comes out
        /// as something else.
        /// </para>
        /// </summary>
        private static int WinAnsi(char ch)
        {
            switch (ch)
            {
                case '€': return 0x80;   // euro
                case '‚': return 0x82;   // single low quote
                case 'ƒ': return 0x83;   // florin
                case '„': return 0x84;   // double low quote
                case '…': return 0x85;   // ellipsis
                case '†': return 0x86;   // dagger
                case '‡': return 0x87;   // double dagger
                case 'ˆ': return 0x88;   // modifier circumflex
                case '‰': return 0x89;   // per mille
                case 'Š': return 0x8A;   // S caron
                case '‹': return 0x8B;   // single left angle quote
                case 'Œ': return 0x8C;   // OE ligature
                case 'Ž': return 0x8E;   // Z caron
                case '‘': return 0x91;   // left single quote
                case '’': return 0x92;   // right single quote - the curly apostrophe
                case '“': return 0x93;   // left double quote
                case '”': return 0x94;   // right double quote
                case '•': return 0x95;   // bullet
                case '–': return 0x96;   // en dash
                case '—': return 0x97;   // em dash
                case '˜': return 0x98;   // small tilde
                case '™': return 0x99;   // trade mark
                case 'š': return 0x9A;   // s caron
                case '›': return 0x9B;   // single right angle quote
                case 'œ': return 0x9C;   // oe ligature
                case 'ž': return 0x9E;   // z caron
                case 'Ÿ': return 0x9F;   // Y diaeresis
                default:
                    // Latin-1 from the non-breaking space upwards sits at its own code point.
                    // Below that is either ASCII, which the caller has already taken, or a
                    // control character with no glyph to draw.
                    return ch >= ' ' && ch <= 'ÿ' ? ch : 0;
            }
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
