using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The completed check attached to the para-planner's email (Change 2, AD-164, AD-165).
    ///
    /// <para>
    /// Built when the notification is QUEUED rather than when it is sent, which makes it a
    /// snapshot of the case at the moment the review was submitted. That is what a summary
    /// attached to a submission letter should be: generating it at send time would attach a
    /// document describing a case that had moved on, and a re-drain would then send a
    /// different document under the same letter.
    /// </para>
    /// <para>
    /// <b>It carries the completed check and any remedial actions</b> (AD-165). It did not
    /// until 2026-09-20: the document deliberately withheld the answers and the grade, on a
    /// reading of AD-020 that treated the para-planner as somebody who could open the review
    /// page and should not be handed a way round it. The project owner settled that -
    /// para-planners have no access to the system at all - so the attachment is not a route
    /// round a screen they could otherwise reach. It is the only sight of the check they get,
    /// and without the findings it was a covering note for a document nobody was sending.
    /// </para>
    /// </summary>
    public static class CompletedCheckPdf
    {
        private const string CaseEntity = "al_outcomecase";

        /// <summary>The file name the recipient sees.</summary>
        public static string FileName(string caseReference)
        {
            var safe = Sanitise(caseReference);
            return "Completed check " + (safe.Length == 0 ? "unreferenced" : safe) + ".pdf";
        }

        /// <summary>
        /// The document for a case, or null when there is no case to describe.
        ///
        /// Never throws. A document is worth having and is not worth losing the letter over,
        /// so the caller treats null as "send without an attachment" (see
        /// <c>NotificationOutbox.QueueWithCompletedCheck</c>).
        /// </summary>
        public static byte[] Build(IOrganizationService service, EntityReference caseRef)
        {
            var blocks = Blocks(service, caseRef);
            return blocks == null ? null : PdfWriter.Build(blocks);
        }

        /// <summary>The document's content, exposed so the layout can be tested without bytes.</summary>
        public static List<PdfBlock> Blocks(IOrganizationService service, EntityReference caseRef)
        {
            if (caseRef == null)
            {
                return null;
            }

            Entity row;
            try
            {
                row = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet(
                    "al_casereference", "al_ioreference", "al_clientname", "al_advisername",
                    "al_paraplanner", "al_advicedate", "al_duedate", "al_checklistitems",
                    "al_casestatus", "al_taxcheckrequired"));
            }
            catch (Exception)
            {
                return null;
            }

            var reference = row.GetAttributeValue<string>("al_casereference");

            var blocks = new List<PdfBlock>
            {
                PdfBlock.Title("Outcome Testing - completed check"),
                PdfBlock.Field("Case reference", Or(reference, "not recorded")),
                PdfBlock.Field("IO reference", Or(row.GetAttributeValue<string>("al_ioreference"), "not recorded")),
                PdfBlock.Field("Client", Or(row.GetAttributeValue<string>("al_clientname"), "not recorded")),
                PdfBlock.Field("Adviser", Or(row.GetAttributeValue<string>("al_advisername"), "not recorded")),
                PdfBlock.Field("Para-planner", Or(row.GetAttributeValue<string>("al_paraplanner"), "not recorded")),
                PdfBlock.Field(
                    "Date of meeting - Client contact",
                    Date(row.GetAttributeValue<DateTime?>("al_advicedate"))),
                PdfBlock.Field("Due date", Date(row.GetAttributeValue<DateTime?>("al_duedate"))),
                PdfBlock.Field("Status", StatusLabel(row.GetAttributeValue<OptionSetValue>("al_casestatus"))),
                PdfBlock.Spacer(),
            };

            // The same list the checker sees on the case, and the answer to the question a
            // para-planner is most likely to have: why this case at all (AD-158).
            var items = Checklist(row.GetAttributeValue<string>("al_checklistitems"));
            blocks.Add(PdfBlock.Heading("Why this case was selected"));
            if (items.Count == 0)
            {
                blocks.Add(PdfBlock.Paragraph("No checklist items are recorded against this case."));
            }
            else
            {
                foreach (var item in items)
                {
                    blocks.Add(PdfBlock.Bullet(item));
                }
            }

            var labels = new OptionLabels(service);
            var reviews = ReviewRows(service, caseRef);

            blocks.Add(PdfBlock.Spacer());
            blocks.Add(PdfBlock.Heading("Checks on this case"));
            if (reviews.Count == 0)
            {
                blocks.Add(PdfBlock.Bullet("No checks are open on this case."));
            }

            foreach (var review in reviews)
            {
                blocks.Add(PdfBlock.Bullet(IndexLine(review)));
            }

            // The check itself, which is what the para-planner is actually being sent
            // (Change 2, Change 7). Submitted reviews only - see CompletedCheck.
            foreach (var review in reviews)
            {
                var submitted = review.GetAttributeValue<DateTime?>("al_submittedon");
                if (!submitted.HasValue)
                {
                    continue;
                }

                var answers = CompletedCheck.Answers(service, review.Id, labels);
                if (answers.Count == 0)
                {
                    continue;
                }

                blocks.Add(PdfBlock.Spacer());
                blocks.Add(PdfBlock.Heading(TypeName(review) + " - submitted " + Date(submitted)));
                blocks.AddRange(answers);
            }

            // Omitted entirely when there are none, which the requirement asks for in as many
            // words. A heading with nothing under it reads as a document that failed to load.
            var remedial = CompletedCheck.RemedialActions(service, caseRef, labels);
            if (remedial.Count > 0)
            {
                blocks.Add(PdfBlock.Spacer());
                blocks.Add(PdfBlock.Heading("Remedial actions"));
                blocks.AddRange(remedial);
            }

            blocks.Add(PdfBlock.Spacer());
            blocks.Add(PdfBlock.Paragraph(
                "This document was produced when the review was submitted and describes the case as "
                + "it stood at that moment. The case record in the portal is the live version."));

            return blocks;
        }

        /// <summary>The case's reviews, in the order they were checked.</summary>
        private static List<Entity> ReviewRows(IOrganizationService service, EntityReference caseRef)
        {
            var reviews = new List<Entity>();

            try
            {
                var query = new QueryExpression("al_reviewinstance")
                {
                    ColumnSet = new ColumnSet("al_reviewtype", "al_submittedon", "al_sequence"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseRef.Id);
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                query.AddOrder("al_sequence", OrderType.Ascending);

                reviews.AddRange(service.RetrieveMultiple(query).Entities);
            }
            catch (Exception)
            {
                // A document missing one section is better than no document, and better than
                // a submit that failed because one could not be drawn.
                return reviews;
            }

            return reviews;
        }

        /// <summary>Which check this is.</summary>
        private static string TypeName(Entity review)
        {
            var type = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            return type != null && type.Value == ResponseRules.ReviewTypeTax ? "Tax check" : "AQS check";
        }

        /// <summary>One line for the index: which check, and where it has got to.</summary>
        private static string IndexLine(Entity review)
        {
            var submitted = review.GetAttributeValue<DateTime?>("al_submittedon");
            return TypeName(review) + " - " + (submitted.HasValue
                ? "submitted " + Date(submitted)
                : "not yet submitted");
        }

        /// <summary>The ticked items, one per line, as the case stores them.</summary>
        private static List<string> Checklist(string raw)
        {
            var items = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return items;
            }

            foreach (var line in raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    items.Add(trimmed);
                }
            }

            return items;
        }

        private static string StatusLabel(OptionSetValue status)
        {
            if (status == null)
            {
                return "not recorded";
            }

            var label = CaseLifecycle.NameOf(status.Value);
            return string.IsNullOrWhiteSpace(label) ? "not recorded" : label;
        }

        private static string Date(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                : "not recorded";
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        /// <summary>
        /// A case reference as a file name.
        ///
        /// Everything outside letters, digits, spaces, hyphens and underscores becomes a
        /// space, because this reaches a mail client and then somebody's file system, and a
        /// reference carrying a slash or a colon is a file that will not save.
        ///
        /// Replaced rather than removed, deliberately. Deleting the slash from "IO/300001"
        /// gives "IO300001", which reads as a plausible reference that is not this case's;
        /// "IO 300001" is visibly the same reference with a character taken out.
        /// </summary>
        private static string Sanitise(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var safe = new System.Text.StringBuilder();
            foreach (var ch in value.Trim())
            {
                if (char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_')
                {
                    safe.Append(ch);
                }
                else if (safe.Length > 0 && safe[safe.Length - 1] != ' ')
                {
                    // One space for a run of them, so "IO//300001" does not become a name
                    // with a gap in the middle of it.
                    safe.Append(' ');
                }
            }

            return safe.ToString().Trim();
        }
    }
}
