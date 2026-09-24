using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The documents sent with the para-planner's letter (AD-164, AD-165), redrawn on
    /// 2026-09-24 to match what the review page prints, and split one file per check with the
    /// remediation as its own.
    ///
    /// Drawn over the real Checker Checklist: the fixture loads data/v8-seed, so a change to the
    /// seed's wording or types shows up here as the form it produces.
    /// </summary>
    public class CompletedCheckDocumentTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee1111-1111-4111-8111-111111111111");
        private static readonly Guid TaxReviewId = Guid.Parse("7a4e1111-1111-4111-8111-111111111111");
        private static readonly Guid AqsReviewId = Guid.Parse("a9e51111-1111-4111-8111-111111111111");
        private static readonly DateTime Submitted = new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc);

        // ================================================================== the page's head

        [Fact]
        public void Opens_as_the_page_opens_with_the_review_summary_and_checklist_items()
        {
            var blocks = CompletedCheck.Blocks(Case(), AqsReviewId);
            var text = Flat(blocks);

            Assert.Contains("AQS review", text);
            Assert.Contains("CHECKLIST VERSION Checker Checklist V8", text);
            Assert.Contains("Checklist Items", text);
            Assert.Contains("High Risk Item 1", text);

            var note = blocks.First(b => b.Kind == PdfBlockKind.Note);
            Assert.Equal(CompletedCheck.FormFooter, note.Text);
            Assert.Equal(ChecklistDocument.Title, blocks.First(b => b.Kind == PdfBlockKind.Title).Text);
        }

        [Fact]
        public void The_case_header_is_the_pages_table_and_io_reference_is_the_client_ref()
        {
            // F6: the IO reference is al_ioreference, not the case reference (the TaskID).
            var header = Tables(CompletedCheck.Blocks(Case(), AqsReviewId))
                .First(t => t.Rows.Any(r => r.Cells.Any(c => c.Text == "Adviser name")));

            var io = Row(header, "Client name / initials");
            Assert.Equal("IO reference", io.Cells[2].Text);
            Assert.Equal("30000001-30000106", io.Cells[3].Text);

            Assert.Equal("Not yet allocated", Row(header, "Tax Checker").Cells[1].Text);
            Assert.Equal("Service Account", Row(header, "Tax Checker").Cells[3].Text);
        }

        // ================================================================== the form

        [Fact]
        public void Heads_each_suitability_subsection_by_name_alone()
        {
            // 2026-09-24: "Client Objectives & Information (COBS 9.2)", not "E1. Client ...".
            var suitability = Grid(CompletedCheck.Blocks(Case(), AqsReviewId), "Suitability test point");

            Assert.Contains(suitability.Rows, r => r.Cells[0].Text == "Client Objectives & Information (COBS 9.2)");
            Assert.DoesNotContain(suitability.Rows, r => r.Cells[0].Text.StartsWith("E1.", StringComparison.Ordinal));
        }

        [Fact]
        public void Draws_ticks_as_boxes_in_the_columns_the_page_heads()
        {
            var aml = Grid(CompletedCheck.Blocks(Case(), AqsReviewId), "Check");

            Assert.Equal(new[] { "Check", "Yes", "No", "N/A" }, aml.Rows[0].Cells.Select(c => c.Text).ToArray());
            var id = Row(aml, "ID verification completed and retained for all relevant clients/parties.");
            Assert.Equal(new[] { "[x]", "[ ]", "[ ]" }, id.Cells.Skip(1).Select(c => c.Text).ToArray());
        }

        [Fact]
        public void A_retyped_aml_section_is_headed_by_its_questions_scale()
        {
            // DEV since 2026-09-21: every AML question's version in force is Yes / No /
            // Insufficient evidence, retyped in the Question library. The page follows the
            // questions, so the emailed copy must too, or it describes answers nobody gave.
            var service = Case();
            foreach (var version in service.All("al_questionversion")
                .Where(v => (v.GetAttributeValue<string>("al_questionversioncode") ?? string.Empty).StartsWith("Q-AML-", StringComparison.Ordinal)))
            {
                version["al_responsetype"] = new OptionSetValue(ResponseRules.TypeYesNoInsufficient);
            }

            var aml = Grid(CompletedCheck.Blocks(service, AqsReviewId), "Check");

            Assert.Equal(new[] { "Check", "Yes", "No", "Insufficient evidence" }, aml.Rows[0].Cells.Select(c => c.Text).ToArray());
            var id = Row(aml, "ID verification completed and retained for all relevant clients/parties.");
            Assert.Equal(new[] { "[x]", "[ ]", "[ ]" }, id.Cells.Skip(1).Select(c => c.Text).ToArray());
        }

        [Fact]
        public void The_suitability_grid_takes_an_na_column_that_only_the_concessions_row_ticks()
        {
            var suitability = Grid(CompletedCheck.Blocks(Case(), AqsReviewId), "Suitability test point");

            Assert.Equal(
                new[] { "Suitability test point", "Pass", "Fail", "Insufficient evidence", "N/A" },
                suitability.Rows[0].Cells.Select(c => c.Text).ToArray());

            var concessions = Row(suitability, "Any concessions / off-tariff pricing approved and recorded");
            Assert.Equal("[x]", concessions.Cells[4].Text);

            // A row on the plain suitability scale is not offered N/A: its cell is empty.
            var charges = Row(suitability, "Adviser charges clearly disclosed and evidenced");
            Assert.Equal(string.Empty, charges.Cells[4].Text);
            Assert.Equal("[x]", charges.Cells[1].Text);
        }

        [Fact]
        public void Crp_takes_na_on_every_row()
        {
            var crp = Grid(CompletedCheck.Blocks(Case(), AqsReviewId), "Centralised Retirement Proposition test point");

            Assert.Equal("N/A", crp.Rows[0].Cells[4].Text);
            Assert.Equal("[x]", Row(crp, "Cashflow model stress tests on file").Cells[4].Text);
            Assert.All(crp.Rows.Skip(1), r => Assert.StartsWith("[", r.Cells[4].Text));
        }

        [Fact]
        public void An_unanswered_question_is_still_drawn_with_empty_boxes()
        {
            // The page draws the whole form. Leaving a row out read as an omission.
            var suitability = Grid(CompletedCheck.Blocks(Case(), AqsReviewId), "Suitability test point");

            var unanswered = Row(suitability, "Language is clear, fair and not misleading");
            Assert.Equal(new[] { "[ ]", "[ ]", "[ ]", string.Empty }, unanswered.Cells.Skip(1).Select(c => c.Text).ToArray());
        }

        [Fact]
        public void Draws_the_outcome_lens_rows_and_the_e2_lens_tick()
        {
            var suitability = Grid(CompletedCheck.Blocks(Case(), AqsReviewId), "Suitability test point");

            var lens = suitability.Rows.First(r => r.Cells[0].Text.StartsWith("Outcome lens: Would a reasonable third party", StringComparison.Ordinal));
            Assert.Equal("[x]", lens.Cells[1].Text);
            Assert.True(lens.Cells[0].Runs[0].Italic && lens.Cells[0].Runs[0].Bold);
        }

        [Fact]
        public void Consumer_duty_no_longer_prints_the_section_h_line()
        {
            var blocks = CompletedCheck.Blocks(Case(), AqsReviewId);

            Assert.DoesNotContain(blocks, b => b.Text != null && b.Text.Contains("section H"));
            Assert.NotNull(Grid(blocks, "Outcome"));
        }

        [Fact]
        public void Draws_the_fail_points_before_the_file_quality_outcome_with_the_ticked_one_ticked()
        {
            var blocks = CompletedCheck.Blocks(Case(), AqsReviewId);
            var titles = blocks.Where(b => b.Kind == PdfBlockKind.Subheading).Select(b => b.Text).ToList();

            Assert.True(titles.IndexOf(ChecklistDocument.FailPointsTitle) < titles.IndexOf("File Quality Outcome"));

            var fails = Grid(blocks, "File Quality fail reason");
            Assert.Equal("[x]", Row(fails, "AML - ID verification issue").Cells[1].Text);
            Assert.Equal("[ ]", Row(fails, "Breach - Any other process breach has been identified").Cells[1].Text);
        }

        [Fact]
        public void Outcome_questions_draw_their_options_inline_with_the_answer_ticked()
        {
            var blocks = CompletedCheck.Blocks(Case(), AqsReviewId);
            var outcome = Tables(blocks).First(t => t.Rows.Any(r => r.Cells[0].Text == "File quality outcome"));

            Assert.Equal("[ ]PASS[x]FAIL", Row(outcome, "File quality outcome").Cells[1].Text);
            Assert.Equal("[x]YES[ ]NO", Row(outcome, "Remedial action required?").Cells[1].Text);
        }

        [Fact]
        public void Several_root_causes_are_drawn_ticked()
        {
            var blocks = CompletedCheck.Blocks(Case(), AqsReviewId);
            var causes = Tables(blocks).First(t => t.Rows[0].Cells[0].Text == "Primary root cause");

            var ticked = causes.Rows.SelectMany(r => r.Cells).Where(c => c.Text.StartsWith("[x]", StringComparison.Ordinal)).Select(c => c.Text).ToList();
            Assert.Equal(new[] { "[x]FactFind quality", "[x]AML / CRA" }, ticked);
        }

        [Fact]
        public void The_root_cause_is_not_drawn_on_a_passing_grade()
        {
            var service = Case();
            SetChoice(service, AqsReviewId, "Q-GR-01", ResponseRules.ChoicePass);

            var blocks = CompletedCheck.Blocks(service, AqsReviewId);
            Assert.DoesNotContain(Tables(blocks), t => t.Rows[0].Cells[0].Text == "Primary root cause");
        }

        [Fact]
        public void A_tax_check_draws_the_tax_sections_only_with_the_tax_wording()
        {
            var blocks = CompletedCheck.Blocks(Case(), TaxReviewId);
            var titles = blocks.Where(b => b.Kind == PdfBlockKind.Subheading).Select(b => b.Text).ToList();

            Assert.Contains("File Quality - Tax check section", titles);
            Assert.DoesNotContain("Suitability core checks", titles);

            var tax = Tables(blocks).First(t => t.Rows.Any(r => r.Cells[0].Text == "Tax check outcome"));
            Assert.Equal("[x]PASS[ ]PASS WITH ISSUES[ ]FAIL", Row(tax, "Tax check outcome").Cells[1].Text);
            Assert.Contains("Re-run the CGT calculation & reissue.", Row(tax, "Tax Remedial").Cells[1].Text);
        }

        [Fact]
        public void The_writer_turns_the_form_into_a_readable_file()
        {
            var bytes = PdfWriter.Build(CompletedCheck.Blocks(Case(), AqsReviewId));
            var pdf = Encoding.GetEncoding(28591).GetString(bytes);

            Assert.StartsWith("%PDF-1.4", pdf);
            Assert.EndsWith("%%EOF", pdf);
            Assert.Contains("re B", pdf);         // shaded, ruled cells
            Assert.Contains("1.2 w 0 G", pdf);    // a tick
            Assert.Contains("/Helvetica-BoldOblique", pdf);
        }

        // ================================================================== the files

        [Fact]
        public void A_tax_then_aqs_case_sends_each_check_as_its_own_file_in_order()
        {
            var documents = CompletedCheckPdf.Documents(Case(), Ref());

            Assert.Equal(
                new[] { "Tax check 300000006.pdf", "AQS check 300000006.pdf" },
                documents.Select(d => d.Name).ToArray());
            Assert.All(documents, d => Assert.StartsWith("%PDF-1.4", Encoding.GetEncoding(28591).GetString(d.Content)));
        }

        [Fact]
        public void A_check_not_yet_submitted_is_not_sent()
        {
            var service = Case();
            service.Row("al_reviewinstance", AqsReviewId).Attributes.Remove("al_submittedon");

            Assert.Equal(new[] { "Tax check 300000006.pdf" }, CompletedCheckPdf.Documents(service, Ref()).Select(d => d.Name).ToArray());
        }

        [Fact]
        public void The_remediation_goes_as_a_third_file_once_an_action_is_completed()
        {
            var service = Case();
            var action = Action(service, completed: false);

            Assert.Equal(2, CompletedCheckPdf.Documents(service, Ref()).Count);

            action["al_completedon"] = new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);
            action["al_adviserresponse"] = "ID re-verified and retained on file.";

            var documents = CompletedCheckPdf.Documents(service, Ref());
            Assert.Equal("Remediation 300000006.pdf", documents.Last().Name);
            Assert.Contains("/MediaBox [0 0 841.89 595.28]", Encoding.GetEncoding(28591).GetString(documents.Last().Content));
        }

        [Fact]
        public void The_remediation_form_numbers_the_issues_and_says_who_completed_them()
        {
            var service = Case();
            var action = Action(service, completed: true);

            var blocks = RemediationDocument.Blocks(service, Ref(), new DateTime(2026, 9, 29));
            var table = Tables(blocks).First(t => t.Rows[0].Cells[0].Text == "No.");

            var first = table.Rows.First(r => r.Cells[0].Text == "1");
            Assert.Equal("ID verification completed and retained for all relevant clients/parties.: No", first.Cells[1].Text);
            Assert.Equal("ID re-verified and retained on file.", first.Cells[2].Text);
            Assert.Equal("Adviser completed 26 Sep 2026; awaiting supervisor", first.Cells[7].Text);
            Assert.Equal("2", table.Rows.First(r => r.Cells[0].Text == "2").Cells[0].Text);

            Assert.Contains("OUTCOME\nInsufficient evidence", Tables(blocks).First().Rows[0].Cells[2].Text);
        }

        [Fact]
        public void Working_days_count_both_ends_and_skip_the_weekend()
        {
            // Thursday to the following Monday: Thu, Fri, Mon.
            Assert.Equal("3 working days", RemediationDocument.Age(new DateTime(2026, 9, 24), new DateTime(2026, 9, 28)));
            Assert.Equal("1 working day", RemediationDocument.Age(new DateTime(2026, 9, 24), new DateTime(2026, 9, 24)));
        }

        [Fact]
        public void One_outbox_row_carries_every_file_and_the_drain_can_pair_them_again()
        {
            var service = Case();
            var action = Action(service, completed: true);

            var id = NotificationOutbox.QueueWithCompletedCheck(
                service, Guid.NewGuid(), NotificationOutbox.EventReviewSubmitted,
                "al_reviewinstance", AqsReviewId, "para@example.com",
                "Review submitted", "The review has been submitted.",
                NotificationTemplates.ReviewSubmitted, Ref());

            var row = service.Retrieve(NotificationOutbox.NotificationEntity, id, new ColumnSet(
                NotificationOutbox.AttachmentNameAttr, NotificationOutbox.AttachmentBodyAttr));

            var pairs = NotificationOutbox.UnpackAttachments(
                row.GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr),
                row.GetAttributeValue<string>(NotificationOutbox.AttachmentBodyAttr));

            Assert.Equal(
                new[] { "Tax check 300000006.pdf", "AQS check 300000006.pdf", "Remediation 300000006.pdf" },
                pairs.Select(p => p.Key).ToArray());
            Assert.All(pairs, p => Assert.StartsWith("%PDF", Encoding.GetEncoding(28591).GetString(Convert.FromBase64String(p.Value))));
        }

        [Fact]
        public void A_row_written_before_the_change_still_unpacks_to_its_one_file()
        {
            var pairs = NotificationOutbox.UnpackAttachments("Completed check 1.pdf", "JVBERi0=");
            Assert.Equal("Completed check 1.pdf", Assert.Single(pairs).Key);
        }

        [Fact]
        public void Mismatched_columns_attach_nothing_rather_than_the_wrong_names()
        {
            Assert.Empty(NotificationOutbox.UnpackAttachments("a.pdf|b.pdf", "JVBERi0="));
        }

        [Fact]
        public void No_case_and_an_unreadable_case_produce_no_documents()
        {
            Assert.Empty(CompletedCheckPdf.Documents(new FakeOrganizationService(), null));
            Assert.Empty(CompletedCheckPdf.Documents(new FakeOrganizationService(), Ref()));
        }

        [Fact]
        public void The_letter_is_queued_even_when_nothing_can_be_attached()
        {
            var service = new FakeOrganizationService();
            var id = NotificationOutbox.QueueWithCompletedCheck(
                service, Guid.NewGuid(), NotificationOutbox.EventReviewSubmitted,
                "al_reviewinstance", Guid.NewGuid(), "para@example.com",
                "Review submitted", "The review has been submitted.",
                NotificationTemplates.ReviewSubmitted, Ref());

            Assert.NotEqual(Guid.Empty, id);
            var row = service.Retrieve(NotificationOutbox.NotificationEntity, id, new ColumnSet(NotificationOutbox.AttachmentBodyAttr));
            Assert.True(string.IsNullOrEmpty(row.GetAttributeValue<string>(NotificationOutbox.AttachmentBodyAttr)));
        }

        [Fact]
        public void File_names_carry_the_check_and_a_safe_reference()
        {
            Assert.Equal("AQS check IO 300001.pdf", CompletedCheckPdf.FileName("AQS check", "IO/300001"));
            Assert.Equal("Tax check unreferenced.pdf", CompletedCheckPdf.FileName("Tax check", "  "));
            Assert.Equal("Remediation a b.pdf", CompletedCheckPdf.FileName("Remediation", "a|b"));
        }

        // ================================================================== the fixture

        private static EntityReference Ref()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        /// <summary>
        /// Case 300000006, routed Tax then AQS, both checks submitted, over the seeded V8.
        /// </summary>
        public static FakeOrganizationService Case()
        {
            var service = new FakeOrganizationService();
            V8Seed.Load(service);

            service.SeedOptionSet("al_reviewinstance", "al_reviewstatus", ResponseRules.StatusSubmitted, "Submitted");
            service.SeedOptionSet("al_remediationaction", "al_actionstatus",
                Remediation.StatusOpen, "Open", Remediation.StatusCompleted, "Completed");

            service.Seed("al_outcomecase", CaseId,
                "al_casereference", "300000006",
                "al_ioreference", "30000001-30000106",
                "al_clientname", "Test Client 6",
                "al_advisername", "Adam Strumidlo",
                "al_paraplanner", "Adam Strumidlo",
                "al_aqscheckername", "Service Account",
                "al_checklistitems", "High Risk Item 1\nEnhanced Supervision");

            var version = new EntityReference("al_checklistversion", V8Seed.ChecklistVersionId) { Name = "Checker Checklist V8" };

            service.Seed("al_reviewinstance", TaxReviewId,
                "al_outcomecaseid", Ref(),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_checklistversionid", version,
                "al_sequence", 1,
                "al_submittedon", Submitted.AddHours(-2),
                "statecode", 0);

            service.Seed("al_reviewinstance", AqsReviewId,
                "al_outcomecaseid", Ref(),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeAqs),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_checklistversionid", version,
                "al_sequence", 2,
                "al_submittedon", Submitted,
                "statecode", 0);

            // Tax
            Answer(service, TaxReviewId, "Q-TAX-02", "al_answerchoice", new OptionSetValue(ResponseRules.ChoicePass));
            Answer(service, TaxReviewId, "Q-TAX-04", "al_answerrichtext", "<p>Re-run the <b>CGT</b> calculation &amp; reissue.</p>");

            // AQS
            Answer(service, AqsReviewId, "Q-AML-01", "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceYes));
            Answer(service, AqsReviewId, "Q-E4-01", "al_answerchoice", new OptionSetValue(ResponseRules.ChoicePass));
            Answer(service, AqsReviewId, "Q-E4-03", "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceNa));
            Answer(service, AqsReviewId, "Q-E2-LENS", "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceYes));
            Answer(service, AqsReviewId, "Q-CRP-01", "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceNa));
            Answer(service, AqsReviewId, "Q-FQ-03", "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceYes));
            Answer(service, AqsReviewId, "Q-GR-01", "al_answerchoice", new OptionSetValue(ResponseRules.ChoicePassWithIssues));
            Answer(service, AqsReviewId, "Q-GR-02", "al_answerchoices",
                new OptionSetValueCollection { new OptionSetValue(120910320), new OptionSetValue(120910326) });
            var fq = Answer(service, AqsReviewId, "Q-FQ-01", "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceFail));

            // One fail point ticked, through the response-keyed intersect (AD-025).
            var idIssue = service.All("al_failreason").First(r => (string)r["al_name"] == "AML - ID verification issue");
            service.Seed("al_al_failreason_al_response", Guid.NewGuid(),
                "al_failreasonid", idIssue.Id, "al_responseid", fq);

            return service;
        }

        private static Entity Action(FakeOrganizationService service, bool completed)
        {
            var action = service.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", AqsReviewId) { Name = "AQS check" },
                "al_description",
                "Issues found on the check:\n- ID verification completed and retained for all relevant clients/parties.: No\n"
                + "- Client objectives clearly evidenced and specific: Fail\n\n"
                + "Raised automatically when the review was submitted (Insufficient evidence). Review the file and record what you have put right.",
                "al_actionstatus", new OptionSetValue(completed ? Remediation.StatusCompleted : Remediation.StatusOpen),
                "al_duedate", new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
                "al_assignedcontactid", new EntityReference("contact", Guid.NewGuid()) { Name = "Adam Strumidlo" },
                "createdon", new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
                "statecode", 0);

            if (completed)
            {
                action["al_completedon"] = new DateTime(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);
                action["al_adviserresponse"] = "ID re-verified and retained on file.";
            }

            return action;
        }

        private static Guid Answer(FakeOrganizationService service, Guid reviewId, string code, string column, object value)
        {
            var id = Guid.NewGuid();
            service.Seed("al_response", id,
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", V8Seed.VersionOf(service, code, Submitted)),
                column, value);
            return id;
        }

        private static void SetChoice(FakeOrganizationService service, Guid reviewId, string code, int choice)
        {
            var versionId = V8Seed.VersionOf(service, code, Submitted);
            var response = service.All("al_response").First(r =>
                r.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id == reviewId
                && r.GetAttributeValue<EntityReference>("al_questionversionid").Id == versionId);
            response["al_answerchoice"] = new OptionSetValue(choice);
        }

        private static IEnumerable<PdfTable> Tables(IEnumerable<PdfBlock> blocks)
        {
            return blocks.Where(b => b.Kind == PdfBlockKind.Table).Select(b => b.Grid);
        }

        /// <summary>The table whose heading row opens with this column heading.</summary>
        private static PdfTable Grid(IEnumerable<PdfBlock> blocks, string heading)
        {
            return Tables(blocks).First(t => t.Rows[0].Header && t.Rows[0].Cells[0].Text == heading);
        }

        private static PdfTableRow Row(PdfTable table, string label)
        {
            return table.Rows.First(r => r.Cells.Count > 0 && r.Cells[0].Text == label);
        }

        private static string Flat(IEnumerable<PdfBlock> blocks)
        {
            var text = new StringBuilder();
            foreach (var block in blocks)
            {
                if (block.Grid != null)
                {
                    foreach (var row in block.Grid.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            text.Append(cell.Text.Replace('\n', ' ')).Append(' ');
                        }
                    }
                }
                else
                {
                    text.Append(block.Text).Append(' ').Append(block.Value).Append(' ');
                }
            }

            return text.ToString();
        }
    }
}
