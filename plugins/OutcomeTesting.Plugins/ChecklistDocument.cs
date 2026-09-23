using System;
using System.Collections.Generic;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// How the Checker Checklist lays itself out: which block a section belongs to, what that
    /// block is called, and whether it draws as a ruled table of test points or as a list of
    /// labelled answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The checklist model holds Section then Question with no grouping level, but the
    /// document groups them: E1 to E5 sit together under "Suitability core checks" as one
    /// table headed "Suitability test point", the Tax check is "File Quality - Tax check
    /// section", and the two disciplines' outcome sections are both "File Quality Outcome"
    /// (AD-098).
    /// </para>
    /// <para>
    /// This is the third copy of that mapping and the first server-side one. The portal has it
    /// in OT Review Detail and the Code App in <c>checklistForm.ts</c> (<c>BLOCKS</c> /
    /// <c>formBlocks</c>); both draw the form a checker fills in. It is here because the
    /// para-planner's attachment has to be the same document (project owner, 2026-09-22: "the
    /// pdf that goes to paraplanners needs to match the pdf form in the reviews or export"),
    /// and it was a flat list of question-and-answer lines. When the document's shape changes,
    /// change all three.
    /// </para>
    /// <para>
    /// Free of Dataverse types, like <see cref="ResponseRules"/> and
    /// <see cref="ChecklistGating"/>, so the mapping can be tested without a fake service.
    /// </para>
    /// <para>
    /// A section whose code the document does not know - one added through checklist
    /// administration (AD-123) - keeps its own name and draws as a list. That is what the
    /// other two copies do, and it is the direction that still renders rather than dropping
    /// a checker's answers out of the document.
    /// </para>
    /// </remarks>
    public static class ChecklistDocument
    {
        /// <summary>The document's title, as the form heads itself.</summary>
        public const string Title = "Outcome Testing - Checker Checklist";

        /// <summary>The heading over the standalone fail points table.</summary>
        public const string FailPointsTitle = "File Quality - Fail points";

        /// <summary>How a block draws.</summary>
        public enum Layout
        {
            /// <summary>A ruled table: one row per test point, answers in a column.</summary>
            Grid,

            /// <summary>A label and its answer per line, as the outcome sections are drawn.</summary>
            Inline,
        }

        /// <summary>One of the document's blocks.</summary>
        public sealed class Block
        {
            public Block(string id, string title, Layout layout, string columnHeading, string intro, bool subsections)
            {
                Id = id;
                Title = title;
                BlockLayout = layout;
                ColumnHeading = columnHeading;
                Intro = intro;
                Subsections = subsections;
            }

            /// <summary>Stable id, so two sections folding into one block are recognised as one.</summary>
            public string Id { get; private set; }

            public string Title { get; private set; }

            public Layout BlockLayout { get; private set; }

            /// <summary>The label column's heading on a grid block.</summary>
            public string ColumnHeading { get; private set; }

            /// <summary>The line the document prints under the heading, or null.</summary>
            public string Intro { get; private set; }

            /// <summary>
            /// Each section folded into this block gets its own subheading and outcome lens.
            /// True of Suitability core checks and of nothing else.
            /// </summary>
            public bool Subsections { get; private set; }
        }

        /// <summary>The id File Quality Outcome carries, which is where the fail points go.</summary>
        public const string FileQualityBlockId = "fq";

        private static readonly Block Suitability = new Block(
            "suitability",
            "Suitability core checks",
            Layout.Grid,
            "Suitability test point",
            "Suitability core checks are shown in a consistent Pass/Fail format against each test point.",
            true);

        private static readonly Block FileQualityOutcome = new Block(
            FileQualityBlockId, "File Quality Outcome", Layout.Inline, null, null, false);

        private static readonly Dictionary<string, Block> Blocks =
            new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase)
            {
                { "S-TAX", new Block("tax", "File Quality - Tax check section", Layout.Inline, null, null, false) },
                {
                    "S-AMLCRA",
                    new Block(
                        "amlcra", "File Quality - AML and CRA checking points",
                        Layout.Grid, "Check", null, false)
                },
                { "S-FQOUT", FileQualityOutcome },
                { "S-FQTAX", FileQualityOutcome },
                { "S-E1", Suitability },
                { "S-E2", Suitability },
                { "S-E3", Suitability },
                { "S-E4", Suitability },
                { "S-E5", Suitability },
                {
                    "S-CRP",
                    new Block(
                        "crp", "Centralised Retirement Proposition", Layout.Grid,
                        "Centralised Retirement Proposition test point",
                        "Complete this section where retirement income planning or decumulation advice is in scope.",
                        false)
                },
                { "S-CD", new Block("cd", "Consumer Duty overlay", Layout.Grid, "Outcome", null, false) },
                { "S-GRADE", new Block("grade", "Checker judgement and grading", Layout.Inline, null, null, false) },
            };

        /// <summary>
        /// The block a section belongs to, or a block of its own where the document does not
        /// know the code.
        ///
        /// <paramref name="sectionName"/> is used only for that fallback, so an administered
        /// section is headed with the name the checking team gave it rather than with its code.
        /// </summary>
        public static Block BlockFor(string sectionCode, string sectionName)
        {
            var code = (sectionCode ?? string.Empty).Trim();

            Block found;
            if (code.Length > 0 && Blocks.TryGetValue(code, out found))
            {
                return found;
            }

            var title = string.IsNullOrWhiteSpace(sectionName) ? "Other" : sectionName.Trim();

            // Id keyed on the code so two administered sections do not fold together merely by
            // sharing a name, and on the title where there is no code at all.
            return new Block(
                code.Length > 0 ? "section:" + code.ToUpperInvariant() : "name:" + title,
                title,
                Layout.Inline,
                null,
                null,
                false);
        }

        /// <summary>
        /// Whether this is the block the standalone fail points are drawn immediately before.
        ///
        /// The document places them between the checking points and the File Quality outcome
        /// (AD-096), which is what both front ends do; asking here keeps the placement in the
        /// same file as the mapping that decides what File Quality Outcome even is.
        /// </summary>
        public static bool TakesFailPointsBefore(Block block)
        {
            return block != null
                && string.Equals(block.Id, FileQualityBlockId, StringComparison.Ordinal);
        }
    }
}
