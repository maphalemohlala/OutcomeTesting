using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The completed check attached to the para-planner's email (Change 2, AD-164, AD-165).
    ///
    /// <para>
    /// The audit noted that the item included a failure-handling question with nothing to
    /// answer it, because there was no generation step that could fail. There is now, and the
    /// answer is that a document never costs a letter: generation runs inside the transaction
    /// that caused the notification, so throwing would roll back a checker's submit because a
    /// PDF could not be drawn.
    /// </para>
    /// <para>
    /// <b>It carries the completed check and its remedial actions</b> (AD-165). It did not
    /// until 2026-09-20: the document was a case summary that deliberately withheld the
    /// answers, on a reading of AD-020 that treated the para-planner as somebody who could
    /// see the review page and should not be given a way round it. The project owner settled
    /// that on 2026-09-20 - para-planners have no access to the system at all, so the
    /// attachment is not a route round a screen, it is the only sight of the check they get.
    /// The test that pinned the old boundary is replaced by the ones below rather than
    /// deleted, because the assertion it made is now exactly backwards.
    /// </para>
    /// </summary>
    public class CompletedCheckAttachmentTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee1111-1111-4111-8111-111111111111");

        // ------------------------------------------------------------ the document

        [Fact]
        public void Describes_the_case_the_paraplanner_is_being_told_about()
        {
            var service = Case();

            var text = Drawn(CompletedCheckPdf.Build(service, Ref()));

            Assert.Contains("IO-300001", text);
            Assert.Contains("A. Client", text);
            Assert.Contains("Adviser User 1", text);
            Assert.Contains("Pat Paraplanner", text);
        }

        [Fact]
        public void Lists_why_the_case_was_selected()
        {
            // The question a para-planner is most likely to have, and the one thing the case
            // carried that nothing showed until AD-158.
            var text = Drawn(CompletedCheckPdf.Build(Case(), Ref()));

            Assert.Contains("Why this case was selected", text);
            Assert.Contains("Tax Check", text);
            Assert.Contains("High Risk Item 1", text);
        }

        // ------------------------------------------------------------ the completed check

        [Fact]
        public void Carries_the_answers_of_a_completed_check()
        {
            // The requirement, and what the document withheld until AD-165: "a PDF of the
            // completed checks for that case".
            var text = Flat(CompletedCheckPdf.Build(Checked(), Ref()));

            Assert.Contains("Client objectives recorded: Pass", text);
            Assert.Contains("Adviser charges evidenced: Fail", text);
        }

        [Fact]
        public void Groups_the_answers_under_the_section_they_were_asked_in()
        {
            // The checker answered them in sections and the para-planner reads them in the
            // same order; an ungrouped run of sixty questions is a list, not a check.
            var text = Flat(CompletedCheckPdf.Build(Checked(), Ref()));

            var section = text.IndexOf("Suitability core checks", StringComparison.Ordinal);
            var charges = text.IndexOf("Adviser charges evidenced", StringComparison.Ordinal);

            Assert.True(section >= 0, "the section heading is drawn");
            Assert.True(section < charges, "the section heading precedes its questions");
        }

        [Fact]
        public void Draws_a_rich_text_answer_as_readable_text()
        {
            // Change 7: Tax Remedial is the one rich-text answer, and it is markup in the
            // column. Drawn as-is the para-planner would read the tags; the PDF has no HTML.
            var text = Flat(CompletedCheckPdf.Build(Checked(), Ref()));

            Assert.Contains("Re-run the CGT calculation", text);
            Assert.DoesNotContain("<b>", text);
            Assert.DoesNotContain("&amp;", text);
        }

        [Fact]
        public void Leaves_out_a_check_that_was_never_submitted()
        {
            // "The completed checks". A half-answered review in somebody's drafts is not a
            // finding, and sending it as one would be worse than sending nothing.
            var service = Checked(submitted: false);

            var text = Flat(CompletedCheckPdf.Build(service, Ref()));

            Assert.DoesNotContain("Client objectives recorded: Pass", text);
            Assert.Contains("not yet submitted", text);
        }

        // ------------------------------------------------------------ remedial actions

        [Fact]
        public void Lists_the_remedial_actions_raised_on_the_case()
        {
            var service = Checked();
            service.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_description", "Evidence the charges disclosure and re-issue the letter.",
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "al_duedate", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
                "statecode", 0);

            var text = Flat(CompletedCheckPdf.Build(service, Ref()));

            Assert.Contains("Remedial actions", text);
            Assert.Contains("Evidence the charges disclosure", text);
            Assert.Contains("1 October 2026", text);
        }

        [Fact]
        public void Omits_the_remedial_section_entirely_when_there_are_none()
        {
            // The requirement is explicit: "If there are no remedial actions, omit that
            // section rather than showing an empty one." An empty heading reads as a document
            // that failed to load its own content.
            var text = Flat(CompletedCheckPdf.Build(Checked(), Ref()));

            Assert.DoesNotContain("Remedial actions", text);
        }

        [Fact]
        public void Does_not_count_a_remedial_action_belonging_to_another_case()
        {
            var service = Checked();
            service.Seed("al_remediationaction", Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", Guid.NewGuid()),
                "al_description", "Another case's action.",
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "statecode", 0);

            var text = Flat(CompletedCheckPdf.Build(service, Ref()));

            Assert.DoesNotContain("Remedial actions", text);
            Assert.DoesNotContain("Another case", text);
        }

        [Fact]
        public void Says_so_when_a_case_names_no_checklist_items()
        {
            var service = Case(checklist: null);

            var text = Drawn(CompletedCheckPdf.Build(service, Ref()));

            Assert.Contains("No checklist items are recorded", text);
        }

        [Fact]
        public void Names_the_file_after_the_case()
        {
            Assert.Equal("Completed check IO-300001.pdf", CompletedCheckPdf.FileName("IO-300001"));
        }

        [Fact]
        public void Strips_characters_a_file_system_would_refuse()
        {
            // The name reaches a mail client and then somebody's disk. A reference carrying a
            // slash is a file that will not save.
            Assert.Equal("Completed check IO 300001.pdf", CompletedCheckPdf.FileName("IO/300001:"));
        }

        [Fact]
        public void Still_names_a_file_when_the_case_has_no_reference()
        {
            Assert.Equal("Completed check unreferenced.pdf", CompletedCheckPdf.FileName(null));
        }

        // ------------------------------------------------------------ failure handling

        [Fact]
        public void A_case_that_cannot_be_read_produces_no_document_rather_than_throwing()
        {
            Assert.Null(CompletedCheckPdf.Build(new FakeOrganizationService(), Ref()));
        }

        [Fact]
        public void No_case_produces_no_document()
        {
            Assert.Null(CompletedCheckPdf.Build(new FakeOrganizationService(), null));
        }

        [Fact]
        public void The_letter_is_queued_even_when_the_document_cannot_be_built()
        {
            // The answer to the audit's failure-handling question. The notification is what
            // has to survive; the attachment is the part that may be missing.
            var service = new FakeOrganizationService();

            var id = NotificationOutbox.QueueWithCompletedCheck(
                service, Guid.NewGuid(), NotificationOutbox.EventReviewSubmitted,
                "al_reviewinstance", Guid.NewGuid(), "para@example.com",
                "Review submitted", "The review has been submitted.",
                Ref());

            Assert.NotEqual(Guid.Empty, id);

            var row = service.Retrieve(
                NotificationOutbox.NotificationEntity, id,
                new ColumnSet(NotificationOutbox.AttachmentBodyAttr, "al_subject"));

            Assert.Equal("Review submitted", row.GetAttributeValue<string>("al_subject"));
            Assert.True(string.IsNullOrEmpty(
                row.GetAttributeValue<string>(NotificationOutbox.AttachmentBodyAttr)));
        }

        [Fact]
        public void A_queued_letter_carries_the_document_as_base64()
        {
            var service = Case();

            var id = NotificationOutbox.QueueWithCompletedCheck(
                service, Guid.NewGuid(), NotificationOutbox.EventReviewSubmitted,
                "al_reviewinstance", Guid.NewGuid(), "para@example.com",
                "Review submitted", "The review has been submitted.",
                Ref());

            var row = service.Retrieve(
                NotificationOutbox.NotificationEntity, id,
                new ColumnSet(NotificationOutbox.AttachmentBodyAttr, NotificationOutbox.AttachmentNameAttr));

            var encoded = row.GetAttributeValue<string>(NotificationOutbox.AttachmentBodyAttr);
            Assert.False(string.IsNullOrWhiteSpace(encoded));
            Assert.Equal(
                "Completed check IO-300001.pdf",
                row.GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr));

            // It decodes to a real PDF, not merely to some bytes.
            var bytes = Convert.FromBase64String(encoded);
            Assert.StartsWith("%PDF-1.4", Encoding.GetEncoding(28591).GetString(bytes));
        }

        // ------------------------------------------------------------ helpers

        private static EntityReference Ref()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        private static FakeOrganizationService Case(string checklist = "Tax Check\nHigh Risk Item 1")
        {
            var service = new FakeOrganizationService();
            var row = service.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-300001",
                "al_clientname", "A. Client",
                "al_advisername", "Adviser User 1",
                "al_paraplanner", "Pat Paraplanner",
                "al_casestatus", new OptionSetValue(CaseLifecycle.Assigned));

            if (checklist != null)
            {
                row["al_checklistitems"] = checklist;
            }

            return service;
        }

        /// <summary>
        /// A case with one submitted Tax check: two scale answers in a named section and the
        /// rich-text Tax Remedial answer Change 7 added.
        /// </summary>
        private static FakeOrganizationService Checked(bool submitted = true)
        {
            var service = Case();
            service.SeedOptionSet("al_response", "al_answerchoice",
                ResponseRules.ChoicePass, "Pass",
                ResponseRules.ChoiceFail, "Fail");
            service.SeedOptionSet("al_remediationaction", "al_actionstatus",
                Remediation.StatusOpen, "Open",
                Remediation.StatusCompleted, "Completed");

            var reviewId = Guid.NewGuid();
            var review = service.Seed("al_reviewinstance", reviewId,
                "al_outcomecaseid", Ref(),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "al_sequence", 1,
                "statecode", 0);

            if (submitted)
            {
                review["al_submittedon"] = new DateTime(2026, 9, 14, 10, 30, 0, DateTimeKind.Utc);
            }

            var section = Guid.NewGuid();
            service.Seed("al_section", section,
                "al_name", "Suitability core checks", "al_sectioncode", "S-E1", "al_displayorder", 1);

            Answer(service, reviewId, section, 1, "Q-E1-01", "Client objectives recorded",
                ResponseRules.TypePassFail, "al_answerchoice",
                new OptionSetValue(ResponseRules.ChoicePass));

            Answer(service, reviewId, section, 2, "Q-E1-02", "Adviser charges evidenced",
                ResponseRules.TypePassFail, "al_answerchoice",
                new OptionSetValue(ResponseRules.ChoiceFail));

            Answer(service, reviewId, section, 3, "Q-TAX-04", "Tax Remedial",
                ResponseRules.TypeRichText, "al_answerrichtext",
                "<p>Re-run the <b>CGT</b> calculation &amp; reissue.</p>");

            return service;
        }

        private static void Answer(
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

            service.Seed("al_question", questionId,
                "al_questioncode", code,
                "al_sectionid", new EntityReference("al_section", sectionId));

            service.Seed("al_questionversion", versionId,
                "al_questionid", new EntityReference("al_question", questionId),
                "al_questiontext", questionText,
                "al_responsetype", new OptionSetValue(responseType),
                "al_displayorder", order);

            service.Seed("al_response", Guid.NewGuid(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                answerColumn, answer);
        }

        /// <summary>
        /// The drawn text with its line breaks flattened to spaces.
        ///
        /// The writer wraps a long value across lines and draws a Field's label and value as
        /// two separate operations, so an assertion on a phrase has to read the page rather
        /// than the individual draw calls.
        /// </summary>
        private static string Flat(byte[] pdf)
        {
            var collapsed = Drawn(pdf).Replace('\n', ' ');
            while (collapsed.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                collapsed = collapsed.Replace("  ", " ");
            }

            return collapsed;
        }

        /// <summary>Every string the document draws, unescaped.</summary>
        private static string Drawn(byte[] pdf)
        {
            var text = Encoding.GetEncoding(28591).GetString(pdf);
            var drawn = new StringBuilder();
            var at = 0;

            while (true)
            {
                var open = text.IndexOf('(', at);
                if (open < 0) break;

                var close = open + 1;
                var line = new StringBuilder();
                while (close < text.Length && text[close] != ')')
                {
                    if (text[close] == '\\' && close + 1 < text.Length)
                    {
                        close++;
                    }

                    line.Append(text[close]);
                    close++;
                }

                if (close < text.Length && text.IndexOf(") Tj", close, StringComparison.Ordinal) == close)
                {
                    drawn.Append(line).Append('\n');
                }

                at = close + 1;
            }

            return drawn.ToString();
        }
    }
}
