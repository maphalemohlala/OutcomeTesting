using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The case summary attached to the para-planner's email (Change 2, AD-164).
    ///
    /// <para>
    /// The audit noted that the item included a failure-handling question with nothing to
    /// answer it, because there was no generation step that could fail. There is now, and the
    /// answer is that a document never costs a letter: generation runs inside the transaction
    /// that caused the notification, so throwing would roll back a checker's submit because a
    /// PDF could not be drawn.
    /// </para>
    /// </summary>
    public class CaseSummaryAttachmentTests
    {
        private static readonly Guid CaseId = Guid.Parse("caee1111-1111-4111-8111-111111111111");

        // ------------------------------------------------------------ the document

        [Fact]
        public void Describes_the_case_the_paraplanner_is_being_told_about()
        {
            var service = Case();

            var text = Drawn(CaseSummaryPdf.Build(service, Ref()));

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
            var text = Drawn(CaseSummaryPdf.Build(Case(), Ref()));

            Assert.Contains("Why this case was selected", text);
            Assert.Contains("Tax Check", text);
            Assert.Contains("High Risk Item 1", text);
        }

        [Fact]
        public void Carries_no_answers_and_no_grade()
        {
            // The para-planner is not a checker and AD-020 keeps them out of the review page.
            // Putting the findings in an attachment would route round that rather than
            // deciding it, so this is a boundary the document must not quietly cross.
            var service = Case();
            service.Seed("al_response", Guid.NewGuid(),
                "al_answertext", "The adviser did not evidence the recommendation.");

            var text = Drawn(CaseSummaryPdf.Build(service, Ref()));

            Assert.DoesNotContain("did not evidence", text);
            Assert.DoesNotContain("Potential harm", text);
        }

        [Fact]
        public void Says_so_when_a_case_names_no_checklist_items()
        {
            var service = Case(checklist: null);

            var text = Drawn(CaseSummaryPdf.Build(service, Ref()));

            Assert.Contains("No checklist items are recorded", text);
        }

        [Fact]
        public void Names_the_file_after_the_case()
        {
            Assert.Equal("Case summary IO-300001.pdf", CaseSummaryPdf.FileName("IO-300001"));
        }

        [Fact]
        public void Strips_characters_a_file_system_would_refuse()
        {
            // The name reaches a mail client and then somebody's disk. A reference carrying a
            // slash is a file that will not save.
            Assert.Equal("Case summary IO 300001.pdf", CaseSummaryPdf.FileName("IO/300001:"));
        }

        [Fact]
        public void Still_names_a_file_when_the_case_has_no_reference()
        {
            Assert.Equal("Case summary unreferenced.pdf", CaseSummaryPdf.FileName(null));
        }

        // ------------------------------------------------------------ failure handling

        [Fact]
        public void A_case_that_cannot_be_read_produces_no_document_rather_than_throwing()
        {
            Assert.Null(CaseSummaryPdf.Build(new FakeOrganizationService(), Ref()));
        }

        [Fact]
        public void No_case_produces_no_document()
        {
            Assert.Null(CaseSummaryPdf.Build(new FakeOrganizationService(), null));
        }

        [Fact]
        public void The_letter_is_queued_even_when_the_document_cannot_be_built()
        {
            // The answer to the audit's failure-handling question. The notification is what
            // has to survive; the attachment is the part that may be missing.
            var service = new FakeOrganizationService();

            var id = NotificationOutbox.QueueWithCaseSummary(
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

            var id = NotificationOutbox.QueueWithCaseSummary(
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
                "Case summary IO-300001.pdf",
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
