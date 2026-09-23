using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The para-planner's attachment is the Checker Checklist, not a description of it
    /// (project owner, 2026-09-22: "the pdf that goes to paraplanners needs to match the pdf
    /// form in the reviews or export").
    ///
    /// <para>
    /// It carried the same facts before, as a flat run of section-name-then-question-and-answer
    /// lines. What it did not carry was the form: the document's blocks, its headings, its
    /// ruled tables of test points against an answer column, and the standalone fail points
    /// between the checking points and the File Quality outcome. A para-planner who has seen
    /// the form could not tell it was the same document.
    /// </para>
    /// <para>
    /// <see cref="ChecklistDocument"/> is the mapping, and it is the third copy of one the
    /// portal (OT Review Detail) and the Code App (<c>checklistForm.ts</c>) already hold.
    /// These tests pin the SERVER's - that the blocks are named and ordered as the document
    /// names and orders them, and that a grid section draws as a grid.
    /// </para>
    /// </summary>
    public class ParaplannerChecklistPdfTests
    {
        private static readonly Guid CaseId = Guid.Parse("0a0a0a0a-1111-4111-8111-111111111111");

        private static EntityReference Ref()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        // ================================================================== the mapping

        [Theory]
        [InlineData("S-TAX", "File Quality - Tax check section")]
        [InlineData("S-AMLCRA", "File Quality - AML and CRA checking points")]
        [InlineData("S-FQOUT", "File Quality Outcome")]
        [InlineData("S-FQTAX", "File Quality Outcome")]
        [InlineData("S-E1", "Suitability core checks")]
        [InlineData("S-E5", "Suitability core checks")]
        [InlineData("S-CRP", "Centralised Retirement Proposition")]
        [InlineData("S-CD", "Consumer Duty overlay")]
        [InlineData("S-GRADE", "Checker judgement and grading")]
        public void Heads_each_section_as_the_document_heads_it(string code, string title)
        {
            Assert.Equal(title, ChecklistDocument.BlockFor(code, "the section's own name").Title);
        }

        [Fact]
        public void Folds_the_five_suitability_sections_into_one_block()
        {
            // The whole reason the mapping exists: the model has no grouping level, and the
            // document puts E1 to E5 under one heading in one table.
            var first = ChecklistDocument.BlockFor("S-E1", null);
            var last = ChecklistDocument.BlockFor("S-E5", null);

            Assert.Equal(first.Id, last.Id);
            Assert.True(first.Subsections, "each E section still heads itself inside the block");
        }

        [Fact]
        public void Folds_both_disciplines_outcome_sections_into_one_block()
        {
            // S-FQOUT is the AQS outcome and S-FQTAX the Tax one. One review carries one of
            // them, and the document calls both "File Quality Outcome".
            Assert.Equal(
                ChecklistDocument.BlockFor("S-FQOUT", null).Id,
                ChecklistDocument.BlockFor("S-FQTAX", null).Id);
        }

        [Fact]
        public void Draws_the_test_point_sections_as_grids_and_the_outcomes_as_lines()
        {
            Assert.Equal(ChecklistDocument.Layout.Grid, ChecklistDocument.BlockFor("S-E3", null).BlockLayout);
            Assert.Equal(ChecklistDocument.Layout.Grid, ChecklistDocument.BlockFor("S-AMLCRA", null).BlockLayout);
            Assert.Equal(ChecklistDocument.Layout.Grid, ChecklistDocument.BlockFor("S-CD", null).BlockLayout);

            Assert.Equal(ChecklistDocument.Layout.Inline, ChecklistDocument.BlockFor("S-FQOUT", null).BlockLayout);
            Assert.Equal(ChecklistDocument.Layout.Inline, ChecklistDocument.BlockFor("S-GRADE", null).BlockLayout);
        }

        [Fact]
        public void Keeps_an_administered_section_under_its_own_name()
        {
            // AD-123 can add a section this mapping has never seen. Dropping its answers out
            // of the document would be the worse failure by a distance.
            var block = ChecklistDocument.BlockFor("S-NEW", "Vulnerable client deep dive");

            Assert.Equal("Vulnerable client deep dive", block.Title);
            Assert.Equal(ChecklistDocument.Layout.Inline, block.BlockLayout);
        }

        [Fact]
        public void Does_not_fold_two_administered_sections_together_by_name()
        {
            Assert.NotEqual(
                ChecklistDocument.BlockFor("S-NEW1", "Extra checks").Id,
                ChecklistDocument.BlockFor("S-NEW2", "Extra checks").Id);
        }

        [Fact]
        public void Places_the_fail_points_before_the_file_quality_outcome()
        {
            Assert.True(ChecklistDocument.TakesFailPointsBefore(ChecklistDocument.BlockFor("S-FQOUT", null)));
            Assert.True(ChecklistDocument.TakesFailPointsBefore(ChecklistDocument.BlockFor("S-FQTAX", null)));
            Assert.False(ChecklistDocument.TakesFailPointsBefore(ChecklistDocument.BlockFor("S-E1", null)));
        }

        // ================================================================== the document

        [Fact]
        public void Is_headed_as_the_form_heads_itself()
        {
            // It said "Outcome Testing - completed check", which named the email rather than
            // the document.
            var blocks = CompletedCheckPdf.Blocks(Checklist(), Ref());

            var title = blocks.First(b => b.Kind == PdfBlockKind.Title);
            Assert.Equal("Outcome Testing - Checker Checklist", title.Text);
        }

        [Fact]
        public void Draws_a_grid_section_as_a_table_with_the_documents_column_heading()
        {
            var blocks = CompletedCheckPdf.Blocks(Checklist(), Ref());

            var head = blocks.FirstOrDefault(b =>
                b.Kind == PdfBlockKind.TableHead && b.Text == "Suitability test point");

            Assert.NotNull(head);
            Assert.Equal("Answer", head.Value);
        }

        [Fact]
        public void Puts_a_test_points_answer_in_its_own_column()
        {
            // Not "question: answer" on one run any more. A column is what lets a reader scan
            // down the answers, which is the whole difference between the form and a list.
            var blocks = CompletedCheckPdf.Blocks(Checklist(), Ref());

            var row = blocks.FirstOrDefault(b =>
                b.Kind == PdfBlockKind.Row && b.Text == "Client objectives clearly evidenced");

            Assert.NotNull(row);
            Assert.Equal("Pass", row.Value);
        }

        [Fact]
        public void Heads_each_suitability_subsection_the_way_the_document_writes_it()
        {
            // "E1. Client Objectives & Information (COBS 9.2)" - the code with S- dropped.
            var blocks = CompletedCheckPdf.Blocks(Checklist(), Ref());

            var heading = blocks.FirstOrDefault(b =>
                b.Text != null && b.Text.StartsWith("E1. Client Objectives", StringComparison.Ordinal));

            Assert.NotNull(heading);

            // A Label and not a Paragraph. Drawn as a paragraph it was indistinguishable from
            // the Outcome lens line directly beneath it, which is the one job a subsection
            // heading has - the form draws these as shaded bands.
            Assert.Equal(PdfBlockKind.Label, heading.Kind);
        }

        [Fact]
        public void Draws_the_outcome_lens_line_the_section_carries()
        {
            var text = Flat(CompletedCheckPdf.Blocks(Checklist(), Ref()));

            Assert.Contains("Outcome lens: Would a reasonable third party", text);
        }

        [Fact]
        public void Draws_the_file_quality_outcome_as_labelled_lines_not_a_grid()
        {
            var blocks = CompletedCheckPdf.Blocks(Checklist(), Ref());

            var outcome = blocks.FirstOrDefault(b =>
                b.Kind == PdfBlockKind.Field && b.Text == "File quality outcome");

            Assert.NotNull(outcome);
            Assert.Equal("Fail", outcome.Value);
        }

        [Fact]
        public void Draws_the_fail_points_as_the_whole_list_against_a_tick()
        {
            // The form shows what was CONSIDERED as well as what was found. A list of the two
            // ticked reasons gives a reader nothing to measure them against.
            var blocks = CompletedCheckPdf.Blocks(Checklist(), Ref());

            var head = blocks.FirstOrDefault(b =>
                b.Kind == PdfBlockKind.TableHead && b.Text == "File Quality fail reason");
            Assert.NotNull(head);
            Assert.Equal("Tick", head.Value);

            var ticked = blocks.First(b => b.Kind == PdfBlockKind.Row && b.Text == "Research not evidenced");
            var untouched = blocks.First(b => b.Kind == PdfBlockKind.Row && b.Text == "Charges not disclosed");

            Assert.Equal("Yes", ticked.Value);
            Assert.Equal(string.Empty, untouched.Value);
        }

        [Fact]
        public void Places_the_fail_points_between_the_checking_points_and_the_outcome()
        {
            var blocks = CompletedCheckPdf.Blocks(Checklist(), Ref());
            var titles = blocks
                .Where(b => b.Kind == PdfBlockKind.Subheading)
                .Select(b => b.Text)
                .ToList();

            var suitability = titles.IndexOf("Suitability core checks");
            var failPoints = titles.IndexOf(ChecklistDocument.FailPointsTitle);
            var outcome = titles.IndexOf("File Quality Outcome");

            Assert.True(suitability >= 0 && failPoints >= 0 && outcome >= 0, "all three blocks are drawn");
            Assert.True(suitability < failPoints, "the checking points come first");
            Assert.True(failPoints < outcome, "the fail points precede the outcome they explain");
        }

        [Fact]
        public void Leaves_the_fail_points_out_when_there_is_no_list_to_draw()
        {
            // An environment with no al_failreason rows seeded. A heading over no rows reads
            // as a document that failed to load half of itself.
            var service = Checklist(withFailReasons: false);
            var blocks = CompletedCheckPdf.Blocks(service, Ref());

            Assert.DoesNotContain(
                blocks, b => b.Kind == PdfBlockKind.Subheading && b.Text == ChecklistDocument.FailPointsTitle);
        }

        [Fact]
        public void Still_produces_a_readable_file()
        {
            // The rules are drawn as a path, and a path inside BT ... ET is a malformed
            // content stream - some readers recover from it and some show a blank page. This
            // is the cheapest check that the new block kinds did not break the file itself.
            var bytes = CompletedCheckPdf.Build(Checklist(), Ref());

            Assert.NotNull(bytes);
            var pdf = Encoding.GetEncoding(28591).GetString(bytes);

            Assert.StartsWith("%PDF-1.4", pdf);
            Assert.EndsWith("%%EOF", pdf);

            // Every path operator sits outside a text object.
            foreach (var stream in Streams(pdf))
            {
                var text = stream.IndexOf("BT", StringComparison.Ordinal);
                var rule = stream.IndexOf(" l\nS", StringComparison.Ordinal);
                if (rule >= 0)
                {
                    Assert.True(rule < text, "rules are drawn before the text object opens");
                }
            }
        }

        // ================================================================== the fixture

        /// <summary>
        /// A case with one submitted AQS check covering three of the document's blocks - a
        /// Suitability core check, the File Quality outcome, and the fail points - which is
        /// what it takes to see the ordering rule work.
        /// </summary>
        private static FakeOrganizationService Checklist(bool withFailReasons = true)
        {
            var service = new FakeOrganizationService();

            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "300000002",
                "al_clientname", "J Smith",
                "al_advisername", "A Adviser",
                "al_paraplanner", "P Planner");

            service.SeedOptionSet("al_response", "al_answerchoice",
                ResponseRules.ChoicePass, "Pass",
                ResponseRules.ChoiceFail, "Fail");

            var reviewId = Guid.NewGuid();
            service.Seed("al_reviewinstance", reviewId,
                "al_outcomecaseid", Ref(),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_sequence", 1,
                "al_submittedon", new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc),
                "statecode", 0);

            // Section display order is the document's order: the core checks, then the outcome.
            var e1 = Guid.NewGuid();
            service.Seed("al_section", e1,
                "al_name", "Client Objectives & Information (COBS 9.2)",
                "al_sectioncode", "S-E1",
                "al_helptext", "Would a reasonable third party conclude the objectives were understood?",
                "al_displayorder", 1);

            var fq = Guid.NewGuid();
            service.Seed("al_section", fq,
                "al_name", "File Quality: AQS", "al_sectioncode", "S-FQOUT", "al_displayorder", 2);

            var responseId = Answer(service, reviewId, e1, 1, "Q-E1-01",
                "Client objectives clearly evidenced",
                ResponseRules.TypePassFail, "al_answerchoice",
                new OptionSetValue(ResponseRules.ChoicePass));

            Answer(service, reviewId, fq, 1, FileQuality.QuestionCode, "File quality outcome",
                ResponseRules.TypePassFail, "al_answerchoice",
                new OptionSetValue(ResponseRules.ChoiceFail));

            if (withFailReasons)
            {
                var research = Guid.NewGuid();
                service.Seed("al_failreason", research, "al_name", "Research not evidenced", "al_displayorder", 1);
                service.Seed("al_failreason", Guid.NewGuid(), "al_name", "Charges not disclosed", "al_displayorder", 2);

                // The tick, through the response-keyed intersect (AD-025).
                service.Seed("al_al_failreason_al_response", Guid.NewGuid(),
                    "al_failreasonid", research,
                    "al_responseid", responseId);
            }

            return service;
        }

        private static Guid Answer(
            FakeOrganizationService service,
            Guid reviewId,
            Guid sectionId,
            int order,
            string code,
            string questionText,
            int responseType,
            string answerColumn,
            object answer)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var responseId = Guid.NewGuid();

            service.Seed("al_question", questionId,
                "al_questioncode", code,
                "al_sectionid", new EntityReference("al_section", sectionId));

            service.Seed("al_questionversion", versionId,
                "al_questionid", new EntityReference("al_question", questionId),
                "al_questiontext", questionText,
                "al_responsetype", new OptionSetValue(responseType),
                "al_displayorder", order);

            service.Seed("al_response", responseId,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                answerColumn, answer);

            return responseId;
        }

        /// <summary>Every block's text and value on one line, for the order assertions.</summary>
        private static string Flat(IEnumerable<PdfBlock> blocks)
        {
            var text = new StringBuilder();
            foreach (var block in blocks)
            {
                if (block == null) { continue; }
                text.Append(block.Text).Append(' ').Append(block.Value).Append(' ');
            }

            return text.ToString();
        }

        /// <summary>The content streams of a PDF, as text.</summary>
        private static IEnumerable<string> Streams(string pdf)
        {
            var at = 0;
            while (true)
            {
                var open = pdf.IndexOf("stream\n", at, StringComparison.Ordinal);
                if (open < 0) { yield break; }

                var start = open + "stream\n".Length;
                var close = pdf.IndexOf("\nendstream", start, StringComparison.Ordinal);
                if (close < 0) { yield break; }

                yield return pdf.Substring(start, close - start);
                at = close;
            }
        }
    }
}
