using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The documents attached to the para-planner's email (Change 2, AD-164, AD-165; split
    /// into files 2026-09-24).
    ///
    /// <para>
    /// Built when the notification is QUEUED rather than when it is sent, which makes each a
    /// snapshot of the case at the moment the review was submitted. A re-drain then sends
    /// the same documents under the same letter.
    /// </para>
    /// <para>
    /// <b>One file per check, and one for the remediation</b> (project owner, 2026-09-24:
    /// "If it is a tax->aqs, then the reciver need to get a tax and aqs pdf, include
    /// remediation if there were any remediations completed. a seperate file for each").
    /// A case routed Tax then AQS sends its Tax check and its AQS check as two files when
    /// the AQS check is submitted; the Remediation and escalation form goes as a third once
    /// any action on the case has been completed. Until then all of it was one file.
    /// </para>
    /// <para>
    /// Each check is drawn as the review page prints it (<see cref="CompletedCheck"/>), and
    /// the remediation as its own page prints it (<see cref="RemediationDocument"/>).
    /// </para>
    /// </summary>
    public static class CompletedCheckPdf
    {
        private const string CaseEntity = "al_outcomecase";

        /// <summary>One attachment: the name the recipient sees, and the PDF.</summary>
        public sealed class Document
        {
            public Document(string name, byte[] content)
            {
                Name = name;
                Content = content;
            }

            public string Name { get; private set; }

            public byte[] Content { get; private set; }
        }

        /// <summary>The file name the recipient sees: "AQS check 300000006.pdf".</summary>
        public static string FileName(string documentName, string caseReference)
        {
            var safe = Sanitise(caseReference);
            return documentName + " " + (safe.Length == 0 ? "unreferenced" : safe) + ".pdf";
        }

        /// <summary>
        /// The documents for a case, in the order the case was checked, the remediation last.
        /// Empty when there is no case to describe or nothing submitted on it.
        ///
        /// Never throws. A document is worth having and is not worth losing the letter over,
        /// so the caller sends an empty list as a letter without attachments.
        /// </summary>
        public static List<Document> Documents(IOrganizationService service, EntityReference caseRef)
        {
            var documents = new List<Document>();
            if (caseRef == null)
            {
                return documents;
            }

            string reference;
            try
            {
                var row = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet("al_casereference"));
                reference = row.GetAttributeValue<string>("al_casereference");
            }
            catch (Exception)
            {
                return documents;
            }

            foreach (var review in SubmittedReviews(service, caseRef))
            {
                try
                {
                    var blocks = CompletedCheck.Blocks(service, review.Id);
                    if (blocks.Count == 0)
                    {
                        continue;
                    }

                    var type = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
                    documents.Add(new Document(
                        FileName(CompletedCheck.CheckName(type == null ? (int?)null : type.Value), reference),
                        PdfWriter.Build(blocks)));
                }
                catch (Exception)
                {
                    // One check that cannot be drawn does not cost the others.
                }
            }

            try
            {
                if (RemediationDocument.AnyCompleted(service, caseRef))
                {
                    var blocks = RemediationDocument.Blocks(service, caseRef, DateTime.UtcNow);
                    if (blocks.Count > 0)
                    {
                        documents.Add(new Document(FileName("Remediation", reference), PdfWriter.Build(blocks, true)));
                    }
                }
            }
            catch (Exception)
            {
                // As above.
            }

            return documents;
        }

        /// <summary>The case's submitted reviews, in the order they were checked.</summary>
        private static List<Entity> SubmittedReviews(IOrganizationService service, EntityReference caseRef)
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

                foreach (var review in service.RetrieveMultiple(query).Entities)
                {
                    if (review.GetAttributeValue<DateTime?>("al_submittedon").HasValue)
                    {
                        reviews.Add(review);
                    }
                }
            }
            catch (Exception)
            {
                reviews.Clear();
            }

            return reviews;
        }

        /// <summary>
        /// A case reference as a file name.
        ///
        /// Everything outside letters, digits, spaces, hyphens and underscores becomes a
        /// space, because this reaches a mail client and then somebody's file system, and a
        /// reference carrying a slash or a colon is a file that will not save. The pipe goes
        /// the same way, which matters twice here: it is also what separates several files
        /// on one outbox row (<see cref="NotificationOutbox"/>).
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
                    safe.Append(' ');
                }
            }

            return safe.ToString().Trim();
        }
    }
}
