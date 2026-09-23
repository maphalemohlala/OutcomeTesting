using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The content of a completed check, and the remedial actions raised on its case, as
    /// blocks for <see cref="PdfWriter"/> (Change 2 and Change 7, AD-165).
    ///
    /// <para>
    /// <b>Why this is in the attachment at all.</b> The document withheld the answers until
    /// 2026-09-20, on a reading of AD-020 that treated the para-planner as somebody who could
    /// open the review page and should not be handed a way round it. The project owner
    /// settled it: para-planners have no access to the system. The attachment is not a route
    /// round a screen they could otherwise reach - it is the only sight of the check they
    /// ever get, and withholding the findings made it a covering note for a document nobody
    /// was going to send.
    /// </para>
    /// <para>
    /// <b>Submitted checks only.</b> A review still being answered is somebody's working
    /// copy; sending it as a finding would be worse than sending nothing, because the reader
    /// has no way to tell a blank that has not been reached from one that was considered.
    /// </para>
    /// <para>
    /// Reads are batched the way <see cref="Remediation.NonPassItems"/> batches them - one
    /// query each for the responses, their versions, those versions' questions and their
    /// sections - rather than a Retrieve per answer. A forty-seven question AQS check would
    /// otherwise pay nearly two hundred round trips inside the transaction that is holding a
    /// checker's submit open.
    /// </para>
    /// </summary>
    public static class CompletedCheck
    {
        /// <summary>
        /// One check as the Checker Checklist lays it out: the document's blocks, in the
        /// document's order, grids drawn as ruled tables of test points against their answers
        /// and outcome sections as labelled lines. Empty when the review has no readable
        /// answers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This used to be a flat run of section name then question-and-answer lines. The
        /// project owner's direction on 2026-09-22 was that "the pdf that goes to paraplanners
        /// needs to match the pdf form in the reviews or export" - so the attachment is now
        /// the form, headed and grouped and ruled as the checker saw it, rather than a
        /// description of it. <see cref="ChecklistDocument"/> holds the mapping, shared with
        /// nothing and mirrored by both front ends.
        /// </para>
        /// <para>
        /// Unanswered questions are still left out. On a submitted check an unanswered
        /// question is one that was optional and skipped, and drawing an empty row against it
        /// would read as an omission rather than as a question that did not apply. That means
        /// the table a para-planner receives is the form's shape with the form's answers, and
        /// not a blank form.
        /// </para>
        /// </remarks>
        public static List<PdfBlock> Answers(
            IOrganizationService service, Guid reviewId, OptionLabels labels)
        {
            return Answers(service, reviewId, labels, null);
        }

        /// <summary>
        /// As above, with the fail points table to place before File Quality Outcome.
        ///
        /// Taken as an argument rather than read here because the reasons are reference data
        /// and the ticks hang off one answer - a separate concern, and one a caller drawing
        /// several checks may want to read once per review and no more.
        /// </summary>
        public static List<PdfBlock> Answers(
            IOrganizationService service, Guid reviewId, OptionLabels labels, List<PdfBlock> failPoints)
        {
            var blocks = new List<PdfBlock>();

            List<Entity> responses;
            try
            {
                var query = new QueryExpression("al_response")
                {
                    ColumnSet = new ColumnSet(
                        "al_questionversionid", "al_answerchoice", "al_answerchoices",
                        "al_answertext", "al_answerrichtext", "al_answerdate"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

                responses = new List<Entity>(CommandHelpers.RetrieveAll(service, query));
            }
            catch (Exception)
            {
                // A document missing a section beats a submit rolled back because one could
                // not be drawn (AD-164).
                return blocks;
            }

            var versionIds = new List<Guid>();
            foreach (var response in responses)
            {
                var versionRef = response.GetAttributeValue<EntityReference>("al_questionversionid");
                if (versionRef != null)
                {
                    versionIds.Add(versionRef.Id);
                }
            }

            Dictionary<Guid, Entity> versions, questions, sections;
            try
            {
                versions = ByIdIn(
                    service,
                    "al_questionversion",
                    new ColumnSet("al_questiontext", "al_responsetype", "al_displayorder", "al_questionid"),
                    versionIds);

                var questionIds = new List<Guid>();
                foreach (var version in versions.Values)
                {
                    var questionRef = version.GetAttributeValue<EntityReference>("al_questionid");
                    if (questionRef != null)
                    {
                        questionIds.Add(questionRef.Id);
                    }
                }

                questions = ByIdIn(
                    service, "al_question", new ColumnSet("al_sectionid", "al_questioncode"), questionIds);

                var sectionIds = new List<Guid>();
                foreach (var question in questions.Values)
                {
                    var sectionRef = question.GetAttributeValue<EntityReference>("al_sectionid");
                    if (sectionRef != null)
                    {
                        sectionIds.Add(sectionRef.Id);
                    }
                }

                // al_sectioncode is what the document's block mapping is keyed on, and
                // al_helptext is the "Outcome lens" line the Suitability subsections carry.
                sections = ByIdIn(
                    service,
                    "al_section",
                    new ColumnSet("al_name", "al_displayorder", "al_sectioncode", "al_helptext"),
                    sectionIds);
            }
            catch (Exception)
            {
                return blocks;
            }

            var items = new List<AnsweredItem>();
            foreach (var response in responses)
            {
                var versionRef = response.GetAttributeValue<EntityReference>("al_questionversionid");
                Entity version;
                if (versionRef == null || !versions.TryGetValue(versionRef.Id, out version))
                {
                    continue;
                }

                var typeValue = version.GetAttributeValue<OptionSetValue>("al_responsetype");
                if (typeValue == null)
                {
                    continue;
                }

                var answer = AnswerText(response, typeValue.Value, labels);
                if (string.IsNullOrWhiteSpace(answer))
                {
                    // An unanswered question on a submitted check is one that was optional
                    // and skipped. Drawing "not answered" against it would read as an
                    // omission rather than a question that did not apply.
                    continue;
                }

                var question = Lookup(questions, version, "al_questionid");
                var section = Lookup(sections, question, "al_sectionid");

                items.Add(new AnsweredItem
                {
                    SectionName = section == null
                        ? "Other"
                        : (section.GetAttributeValue<string>("al_name") ?? "Other"),
                    SectionCode = section == null
                        ? null
                        : section.GetAttributeValue<string>("al_sectioncode"),
                    SectionLens = section == null
                        ? null
                        : section.GetAttributeValue<string>("al_helptext"),
                    SectionId = section == null ? Guid.Empty : section.Id,
                    SectionOrder = Order(section, "al_displayorder"),
                    QuestionOrder = Order(version, "al_displayorder"),
                    QuestionText = version.GetAttributeValue<string>("al_questiontext"),
                    Answer = answer,
                });
            }

            items.Sort(delegate(AnsweredItem left, AnsweredItem right)
            {
                var bySection = left.SectionOrder.CompareTo(right.SectionOrder);
                if (bySection != 0)
                {
                    return bySection;
                }

                var byName = string.Compare(
                    left.SectionName, right.SectionName, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : left.QuestionOrder.CompareTo(right.QuestionOrder);
            });

            Draw(blocks, items, failPoints);
            return blocks;
        }

        /// <summary>
        /// Draws the sorted items as the document's blocks.
        /// </summary>
        /// <param name="failPoints">
        /// The fail points table, drawn immediately before File Quality Outcome as the
        /// document places it (AD-096), or null where the caller has none to draw. Passed in
        /// rather than read here because the reasons are reference data and the ticks hang off
        /// one answer - a second concern, and one the caller may already have in hand.
        /// </param>
        internal static void Draw(
            List<PdfBlock> blocks, List<AnsweredItem> items, List<PdfBlock> failPoints)
        {
            string currentBlockId = null;
            Guid currentSectionId = Guid.Empty;
            var failPointsPlaced = false;
            ChecklistDocument.Block block = null;

            foreach (var item in items)
            {
                var itemBlock = ChecklistDocument.BlockFor(item.SectionCode, item.SectionName);

                if (!string.Equals(currentBlockId, itemBlock.Id, StringComparison.Ordinal))
                {
                    block = itemBlock;
                    currentBlockId = itemBlock.Id;
                    currentSectionId = Guid.Empty;

                    // Between the checking points and the File Quality outcome, which is where
                    // the document puts them and where both front ends draw them.
                    if (!failPointsPlaced
                        && failPoints != null
                        && ChecklistDocument.TakesFailPointsBefore(itemBlock))
                    {
                        blocks.AddRange(failPoints);
                        failPointsPlaced = true;
                    }

                    blocks.Add(PdfBlock.Subheading(itemBlock.Title));

                    if (!string.IsNullOrWhiteSpace(itemBlock.Intro))
                    {
                        blocks.Add(PdfBlock.Paragraph(itemBlock.Intro));
                    }

                    if (itemBlock.BlockLayout == ChecklistDocument.Layout.Grid)
                    {
                        blocks.Add(PdfBlock.TableHead(itemBlock.ColumnHeading, "Answer"));
                    }
                }

                // A block that folds several sections together heads each one, which is how
                // the document draws E1 to E5 inside Suitability core checks. Every other
                // block is one section and would only be repeating its own heading.
                if (block.Subsections && item.SectionId != currentSectionId)
                {
                    currentSectionId = item.SectionId;
                    blocks.Add(PdfBlock.Label(SubsectionHeading(item)));

                    if (!string.IsNullOrWhiteSpace(item.SectionLens))
                    {
                        blocks.Add(PdfBlock.Paragraph("Outcome lens: " + Collapse(item.SectionLens)));
                    }
                }

                var text = string.IsNullOrWhiteSpace(item.QuestionText)
                    ? "Question"
                    : item.QuestionText.Trim();

                blocks.Add(block.BlockLayout == ChecklistDocument.Layout.Grid
                    ? PdfBlock.Row(text, item.Answer)
                    : PdfBlock.Field(text, item.Answer));
            }

            // A check with no File Quality Outcome answered at all - a review abandoned to an
            // optional section, or a checklist that no longer has one - would otherwise lose
            // the fail points entirely rather than drawing them at the end.
            if (!failPointsPlaced && failPoints != null)
            {
                blocks.AddRange(failPoints);
            }
        }

        /// <summary>
        /// A Suitability subsection's heading, as the document writes it: "E1. Client
        /// Objectives &amp; Information (COBS 9.2)" - the section code with its S- prefix
        /// dropped, then the name.
        ///
        /// The name alone where there is no code, because a bare ". " reads as a typo.
        /// </summary>
        private static string SubsectionHeading(AnsweredItem item)
        {
            var name = string.IsNullOrWhiteSpace(item.SectionName) ? "Section" : item.SectionName.Trim();
            var code = (item.SectionCode ?? string.Empty).Trim();

            if (code.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
            {
                code = code.Substring(2);
            }

            return code.Length == 0 ? name : code + ". " + name;
        }

        /// <summary>
        /// The File Quality fail points as the document draws them: every reason on the list,
        /// in display order, against a Tick column (FR-013, AD-096).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The WHOLE list, not only the ticked ones, because that is the form. A para-planner
        /// reading "Suitability report missing" against a tick learns something different from
        /// one reading a list of two reasons with nothing to measure them against - the form
        /// shows what was considered as well as what was found.
        /// </para>
        /// <para>
        /// Empty - and so drawn not at all - when the list itself is empty or cannot be read.
        /// A heading over no rows reads as a document that failed to load half of itself,
        /// which is the same judgement <see cref="RemedialActions"/> makes.
        /// </para>
        /// <para>
        /// Two reads: the reasons, which are reference data, and the ticks, which hang off
        /// this review's answers through the response-keyed intersect (AD-025). Never throws,
        /// for the reason <c>CompletedCheckPdf</c> gives: a document missing one table beats a
        /// submit rolled back because one could not be drawn.
        /// </para>
        /// </remarks>
        public static List<PdfBlock> FailPoints(IOrganizationService service, Guid reviewId)
        {
            var blocks = new List<PdfBlock>();

            List<Entity> reasons;
            var ticked = new HashSet<Guid>();

            try
            {
                var all = new QueryExpression("al_failreason")
                {
                    ColumnSet = new ColumnSet("al_name", "al_displayorder"),
                };
                all.AddOrder("al_displayorder", OrderType.Ascending);

                reasons = new List<Entity>(CommandHelpers.RetrieveAll(service, all));

                if (reasons.Count == 0)
                {
                    return blocks;
                }

                // The reasons ticked against any answer on THIS review. Response-keyed, so a
                // case whose two disciplines each recorded fail points keeps them apart.
                var chosen = new QueryExpression("al_failreason")
                {
                    ColumnSet = new ColumnSet(false),
                };
                var intersect = chosen.AddLink(
                    "al_al_failreason_al_response", "al_failreasonid", "al_failreasonid");
                var response = intersect.AddLink("al_response", "al_responseid", "al_responseid");
                response.LinkCriteria.AddCondition(
                    "al_reviewinstanceid", ConditionOperator.Equal, reviewId);

                foreach (var row in CommandHelpers.RetrieveAll(service, chosen))
                {
                    ticked.Add(row.Id);
                }
            }
            catch (Exception)
            {
                return new List<PdfBlock>();
            }

            blocks.Add(PdfBlock.Subheading(ChecklistDocument.FailPointsTitle));
            blocks.Add(PdfBlock.TableHead("File Quality fail reason", "Tick"));

            foreach (var reason in reasons)
            {
                var name = reason.GetAttributeValue<string>("al_name");
                blocks.Add(PdfBlock.Row(
                    string.IsNullOrWhiteSpace(name) ? "A reason with no name recorded." : name.Trim(),
                    ticked.Contains(reason.Id) ? "Yes" : string.Empty));
            }

            return blocks;
        }

        /// <summary>
        /// The remedial actions on a case, or an EMPTY list where there are none.
        ///
        /// The caller draws no heading for an empty list, which is the requirement's own
        /// instruction: "if there are no remedial actions, omit that section rather than
        /// showing an empty one". An empty heading reads as a document that failed to load
        /// half of itself.
        /// </summary>
        public static List<PdfBlock> RemedialActions(
            IOrganizationService service, EntityReference caseRef, OptionLabels labels)
        {
            var blocks = new List<PdfBlock>();
            if (caseRef == null)
            {
                return blocks;
            }

            List<Entity> actions;
            try
            {
                var query = new QueryExpression("al_remediationaction")
                {
                    ColumnSet = new ColumnSet(
                        "al_description", "al_actionstatus", "al_duedate",
                        "al_completedon", "al_adviserresponse"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseRef.Id);
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

                actions = new List<Entity>(CommandHelpers.RetrieveAll(service, query));
            }
            catch (Exception)
            {
                return blocks;
            }

            foreach (var action in actions)
            {
                var description = action.GetAttributeValue<string>("al_description");
                blocks.Add(PdfBlock.Bullet(
                    string.IsNullOrWhiteSpace(description)
                        ? "An action with no description recorded."
                        : description.Trim()));

                var status = action.GetAttributeValue<OptionSetValue>("al_actionstatus");
                var completedOn = action.GetAttributeValue<DateTime?>("al_completedon");
                var dueDate = action.GetAttributeValue<DateTime?>("al_duedate");

                var state = status == null
                    ? "not recorded"
                    : labels.Label("al_remediationaction", "al_actionstatus", status.Value);

                if (completedOn.HasValue)
                {
                    state += ", completed " + Date(completedOn);
                }
                else if (dueDate.HasValue)
                {
                    state += ", due " + Date(dueDate);
                }

                blocks.Add(PdfBlock.Field("Status", state));

                // What the adviser said they did. Absent on an open action, and an empty
                // field against one would read as an adviser who answered with nothing.
                var response = action.GetAttributeValue<string>("al_adviserresponse");
                if (!string.IsNullOrWhiteSpace(response))
                {
                    blocks.Add(PdfBlock.Field("Adviser response", PlainText(response)));
                }
            }

            return blocks;
        }

        /// <summary>
        /// Markup reduced to the text a reader should see.
        ///
        /// The PDF has no HTML in it, so a rich-text answer drawn as stored would show the
        /// para-planner its own tags. Change 7's "Tax Remedial" is the answer this exists
        /// for, and it is the one answer on the Tax check that carries markup.
        ///
        /// Block ends become a space rather than a line break: a Field draws its value as one
        /// run, so the alternative to collapsing is a paragraph break the writer would render
        /// as a missing space between two sentences.
        /// </summary>
        public static string PlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var text = new StringBuilder();
            var inTag = false;

            foreach (var ch in html)
            {
                if (ch == '<')
                {
                    inTag = true;

                    // A tag boundary is a word boundary: "a<br>b" is two words, and dropping
                    // the tag without one would join them.
                    if (text.Length > 0 && text[text.Length - 1] != ' ')
                    {
                        text.Append(' ');
                    }

                    continue;
                }

                if (ch == '>')
                {
                    inTag = false;
                    continue;
                }

                if (!inTag)
                {
                    text.Append(ch == '\r' || ch == '\n' || ch == '\t' ? ' ' : ch);
                }
            }

            return Collapse(Decode(text.ToString()));
        }

        /// <summary>The five entities the sanitiser's allow-list can leave behind, plus nbsp.</summary>
        private static string Decode(string text)
        {
            return text
                .Replace("&nbsp;", " ")
                .Replace("&quot;", "\"")
                .Replace("&#39;", "'")
                .Replace("&apos;", "'")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")

                // Last, so an escaped ampersand in "&amp;lt;" does not become a tag bracket.
                .Replace("&amp;", "&");
        }

        private static string Collapse(string text)
        {
            var collapsed = new StringBuilder(text.Length);
            var space = false;

            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    space = true;
                    continue;
                }

                if (space && collapsed.Length > 0)
                {
                    collapsed.Append(' ');
                }

                space = false;
                collapsed.Append(ch);
            }

            return collapsed.ToString();
        }

        /// <summary>One answer as the reader should see it, or empty where none was given.</summary>
        private static string AnswerText(Entity response, int responseType, OptionLabels labels)
        {
            ResponseRules.AnswerColumn column;
            try
            {
                column = ResponseRules.ColumnFor(responseType);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A response type this assembly does not know is a checklist that moved ahead
                // of the code. Leaving the answer out beats guessing which column holds it.
                return string.Empty;
            }

            switch (column)
            {
                case ResponseRules.AnswerColumn.Choice:
                    var choice = response.GetAttributeValue<OptionSetValue>("al_answerchoice");
                    return choice == null
                        ? string.Empty
                        : labels.Label("al_response", "al_answerchoice", choice.Value);

                case ResponseRules.AnswerColumn.Choices:
                    return Choices(response, labels);

                case ResponseRules.AnswerColumn.Date:
                    return Date(response.GetAttributeValue<DateTime?>("al_answerdate"));

                case ResponseRules.AnswerColumn.RichText:
                    return PlainText(response.GetAttributeValue<string>("al_answerrichtext"));

                default:
                    var text = response.GetAttributeValue<string>("al_answertext");
                    return string.IsNullOrWhiteSpace(text) ? string.Empty : Collapse(text);
            }
        }

        private static string Choices(Entity response, OptionLabels labels)
        {
            var values = response.GetAttributeValue<OptionSetValueCollection>("al_answerchoices");
            if (values == null || values.Count == 0)
            {
                return string.Empty;
            }

            var names = new List<string>();
            foreach (var value in values)
            {
                names.Add(labels.Label("al_response", "al_answerchoices", value.Value));
            }

            return string.Join(", ", names.ToArray());
        }

        private static string Date(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private static int Order(Entity row, string attribute)
        {
            if (row == null)
            {
                return int.MaxValue;
            }

            var value = row.GetAttributeValue<int?>(attribute);
            return value.HasValue ? value.Value : int.MaxValue;
        }

        private static Entity Lookup(Dictionary<Guid, Entity> rows, Entity from, string attribute)
        {
            if (from == null)
            {
                return null;
            }

            var reference = from.GetAttributeValue<EntityReference>(attribute);
            if (reference == null)
            {
                return null;
            }

            Entity row;
            return rows.TryGetValue(reference.Id, out row) ? row : null;
        }

        /// <summary>
        /// Reads a set of rows by primary key in one query, keyed by id. Empty in, empty out -
        /// an <c>In</c> with no values is a query Dataverse rejects.
        /// </summary>
        private static Dictionary<Guid, Entity> ByIdIn(
            IOrganizationService service, string entity, ColumnSet columns, List<Guid> ids)
        {
            var byId = new Dictionary<Guid, Entity>();
            if (ids.Count == 0)
            {
                return byId;
            }

            var keys = new List<object>();
            var seen = new HashSet<Guid>();
            foreach (var id in ids)
            {
                if (seen.Add(id))
                {
                    keys.Add(id);
                }
            }

            var query = new QueryExpression(entity)
            {
                ColumnSet = columns,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(entity + "id", ConditionOperator.In, keys.ToArray());

            foreach (var row in CommandHelpers.RetrieveAll(service, query))
            {
                byId[row.Id] = row;
            }

            return byId;
        }

        internal sealed class AnsweredItem
        {
            public string SectionName { get; set; }

            /// <summary>The stable key the document's block mapping is keyed on.</summary>
            public string SectionCode { get; set; }

            /// <summary>The section's "Outcome lens" line, which the Suitability block draws.</summary>
            public string SectionLens { get; set; }

            /// <summary>
            /// Which section this was asked in, so a block folding several together can tell
            /// them apart. The id rather than the name: two administered sections may share a
            /// name, and heading them as one would merge answers that are not the same
            /// checker's view of the same thing.
            /// </summary>
            public Guid SectionId { get; set; }

            public int SectionOrder { get; set; }

            public int QuestionOrder { get; set; }

            public string QuestionText { get; set; }

            public string Answer { get; set; }
        }
    }
}
