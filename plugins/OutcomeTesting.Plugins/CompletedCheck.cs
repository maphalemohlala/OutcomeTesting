using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// One submitted check drawn as the review page prints it, as blocks for
    /// <see cref="PdfWriter"/> (Change 2 and Change 7, AD-165; redrawn 2026-09-24).
    ///
    /// <para>
    /// <b>The page's own printout is the specification.</b> The project owner put the two
    /// side by side on 2026-09-24 - the browser's "Save as PDF" of the review page and the
    /// attachment the para-planner was sent - and asked for the second to match the first.
    /// It did not: the attachment listed each test point against the WORD of its answer,
    /// left unanswered questions out, and folded Tax, AQS and the remedial actions into one
    /// file. It now draws what the page draws: the review summary, the checklist items, the
    /// case header table, and every block of the form as the ruled table the page rules,
    /// with its tick columns and its empty boxes. One check per file;
    /// <see cref="RemediationDocument"/> is the remedial actions' own.
    /// </para>
    /// <para>
    /// <b>The whole form in force, not only the answers.</b> The page renders every question
    /// in force on the review's checklist version for its discipline (AD-020) as of the day
    /// it was submitted (AD-091), so an unanswered optional question is an empty row, as it
    /// is on the page. Answers held against a version outside that set - an older review, or
    /// a checklist moved on since - are drawn as well rather than dropped.
    /// </para>
    /// <para>
    /// <b>Submitted checks only</b>, which the caller decides. A review still being answered
    /// is somebody's working copy.
    /// </para>
    /// <para>
    /// The layout rules are the page's - <c>OT Review Detail</c> in the portal and
    /// <c>checklistForm.ts</c> in the Code App. <see cref="ChecklistDocument"/> holds the
    /// shared vocabulary; when the form's shape changes, change all three.
    /// </para>
    /// </summary>
    public static class CompletedCheck
    {
        /// <summary>The line the page prints above the form's title, verbatim.</summary>
        public const string FormFooter = "Outcome Testing Checker Checklist | V5 Draft";

        /// <summary>Tick-grid widths: the test point, then the tick columns share the rest.</summary>
        private const double GridLabel = 0.55;

        private const string CaseEntity = "al_outcomecase";

        /// <summary>"Tax check" or "AQS check".</summary>
        public static string CheckName(int? reviewType)
        {
            return reviewType.HasValue && reviewType.Value == ResponseRules.ReviewTypeTax
                ? "Tax check"
                : "AQS check";
        }

        /// <summary>
        /// The document for one review, or an empty list when the review cannot be read.
        /// Never throws: a document is worth having and is not worth a checker's submit.
        /// </summary>
        public static List<PdfBlock> Blocks(IOrganizationService service, Guid reviewId)
        {
            var blocks = new List<PdfBlock>();

            Entity review;
            try
            {
                review = service.Retrieve("al_reviewinstance", reviewId, new ColumnSet(
                    "al_reviewtype", "al_reviewstatus", "al_checklistversionid", "al_sequence",
                    "al_startedon", "al_submittedon", "al_outcomecaseid"));
            }
            catch (Exception)
            {
                return blocks;
            }

            var labels = new OptionLabels(service);
            var type = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            var isTax = type != null && type.Value == ResponseRules.ReviewTypeTax;
            var caseRef = review.GetAttributeValue<EntityReference>("al_outcomecaseid");

            Entity row = null;
            if (caseRef != null)
            {
                try
                {
                    row = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet(
                        "al_casereference", "al_clientname", "al_advisername", "al_adviserstatus",
                        "al_paraplanner", "al_products", "al_casetype", "al_casetypeid",
                        "al_advicedate", "al_productsolutiontype", "al_producttypeid",
                        "al_samplesource", "al_samplesourceid", "al_taxcheckername",
                        "al_aqscheckername", "al_checkdate", "al_ioreference",
                        "al_preorpostcheck", "al_preorpostcheckid", "al_vulnerableclient",
                        "al_taxcheckrequired", "al_taxteamdisposition", "al_checklistitems"));
                }
                catch (Exception)
                {
                    row = null;
                }
            }

            blocks.AddRange(Summary(review, row, labels, isTax));

            blocks.Add(PdfBlock.Spacer());
            blocks.Add(PdfBlock.Note(FormFooter));
            blocks.Add(PdfBlock.Title(ChecklistDocument.Title));

            if (row != null)
            {
                blocks.Add(PdfBlock.Table(CaseHeader(service, row, labels)));
            }

            var submitted = review.GetAttributeValue<DateTime?>("al_submittedon");
            var items = Items(service, review, submitted ?? DateTime.UtcNow);
            var reasons = FailReasons(service, reviewId);

            Draw(blocks, items, reasons, isTax);
            return blocks;
        }

        // ================================================================== the page's head

        /// <summary>The review summary card and the checklist items, as the page opens.</summary>
        private static List<PdfBlock> Summary(Entity review, Entity row, OptionLabels labels, bool isTax)
        {
            var blocks = new List<PdfBlock>();

            var status = review.GetAttributeValue<OptionSetValue>("al_reviewstatus");
            var statusText = status == null
                ? string.Empty
                : labels.Label("al_reviewinstance", "al_reviewstatus", status.Value);

            var card = new PdfTable(0.5, 0.5) { Ruled = false };
            var title = new PdfCell { Span = 2 };
            title.Runs.Add(PdfRun.Of((isTax ? "Tax" : "AQS") + " review", true));
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                title.Runs.Add(PdfRun.Of("  [" + statusText + "]"));
            }

            card.Add(title);

            var version = review.GetAttributeValue<EntityReference>("al_checklistversionid");
            var sequence = review.GetAttributeValue<int?>("al_sequence");

            card.Add(
                Stacked("CASE", row == null ? null : row.GetAttributeValue<string>("al_casereference")),
                Stacked("CLIENT", row == null ? null : row.GetAttributeValue<string>("al_clientname")));
            card.Add(
                Stacked("ADVISER", row == null ? null : row.GetAttributeValue<string>("al_advisername")),
                Stacked("CHECKLIST VERSION", version == null ? null : version.Name));
            card.Add(
                Stacked("SEQUENCE", sequence.HasValue ? sequence.Value.ToString(CultureInfo.InvariantCulture) : null),
                Stacked("STARTED", Day(review.GetAttributeValue<DateTime?>("al_startedon"))));
            card.Add(
                Stacked("SUBMITTED", Day(review.GetAttributeValue<DateTime?>("al_submittedon"))),
                PdfCell.Blank());

            blocks.Add(PdfBlock.Table(card));
            blocks.Add(PdfBlock.Rule());

            // The case's own checklist items: why it was selected (AD-158).
            blocks.Add(PdfBlock.Subheading("Checklist Items"));
            var items = ChecklistItems(row == null ? null : row.GetAttributeValue<string>("al_checklistitems"));
            if (items.Count == 0)
            {
                blocks.Add(PdfBlock.Paragraph("No checklist items are recorded against this case."));
            }

            foreach (var item in items)
            {
                blocks.Add(PdfBlock.Bullet(item));
            }

            return blocks;
        }

        private static PdfCell Stacked(string label, string value)
        {
            var cell = new PdfCell();
            cell.Runs.Add(PdfRun.Of(label, true));
            cell.Runs.Add(PdfRun.LineBreak());
            cell.Runs.Add(PdfRun.Of(string.IsNullOrWhiteSpace(value) ? "—" : value.Trim()));
            return cell;
        }

        /// <summary>
        /// The case header, row for row as the submitted page draws it read-only.
        ///
        /// IO reference is <c>al_ioreference</c>, the ClientRef. The page had been drawing the
        /// case reference there (the TaskID) - the defect the Code App fixed as F6 - and is
        /// corrected in the same change.
        /// </summary>
        private static PdfTable CaseHeader(IOrganizationService service, Entity row, OptionLabels labels)
        {
            var table = new PdfTable(0.17, 0.33, 0.17, 0.33);

            string Choice(string attribute)
            {
                var value = row.GetAttributeValue<OptionSetValue>(attribute);
                return value == null ? string.Empty : labels.Label(CaseEntity, attribute, value.Value);
            }

            string Lookup(string lookup, string choice)
            {
                var reference = row.GetAttributeValue<EntityReference>(lookup);
                return reference != null && !string.IsNullOrWhiteSpace(reference.Name)
                    ? reference.Name
                    : Choice(choice);
            }

            string Text(string attribute)
            {
                return row.GetAttributeValue<string>(attribute) ?? string.Empty;
            }

            string Checker(string attribute)
            {
                var name = row.GetAttributeValue<string>(attribute);
                return string.IsNullOrWhiteSpace(name) ? "Not yet allocated" : name.Trim();
            }

            var products = Products(service, row.Id);

            table.Add(PdfCell.Label("Adviser name"), PdfCell.Of(Text("al_advisername")),
                PdfCell.Label("Adviser status"), PdfCell.Of(Choice("al_adviserstatus")));
            table.Add(PdfCell.Label("Paraplanner"), PdfCell.Of(Text("al_paraplanner")),
                PdfCell.Label("Product(s)"),
                PdfCell.Of(products.Count > 0 ? string.Join("; ", products.ToArray()) : Text("al_products")));
            table.Add(PdfCell.Label("Case type"), PdfCell.Of(Lookup("al_casetypeid", "al_casetype")),
                PdfCell.Label(CaseHeaderRules.AdviceDateLabel),
                PdfCell.Of(Day(row.GetAttributeValue<DateTime?>("al_advicedate"))));
            table.Add(PdfCell.Label("Product / solution type"),
                PdfCell.Of(Lookup("al_producttypeid", "al_productsolutiontype")),
                PdfCell.Label("Sample source"), PdfCell.Of(Lookup("al_samplesourceid", "al_samplesource")));
            table.Add(PdfCell.Label("Tax Checker"), PdfCell.Of(Checker("al_taxcheckername")),
                PdfCell.Label("AQS Checker"), PdfCell.Of(Checker("al_aqscheckername")));
            table.Add(PdfCell.Label("Check date"), PdfCell.Of(Day(row.GetAttributeValue<DateTime?>("al_checkdate"))),
                PdfCell.Label(string.Empty), PdfCell.Blank());
            table.Add(PdfCell.Label("Client name / initials"), PdfCell.Of(Text("al_clientname")),
                PdfCell.Label("IO reference"), PdfCell.Of(Text("al_ioreference")));
            table.Add(PdfCell.Label("Pre or post check"), PdfCell.Of(Lookup("al_preorpostcheckid", "al_preorpostcheck")),
                PdfCell.Label("Vulnerable client?"), PdfCell.Of(Choice("al_vulnerableclient")));
            table.Add(PdfCell.Label("Tax check required\n(Tax team to complete)"), PdfCell.Of(Choice("al_taxcheckrequired")),
                PdfCell.Label("For Tax team usage"), PdfCell.Of(Choice("al_taxteamdisposition")));

            return table;
        }

        /// <summary>The products the case carries (AD-190), by name. Empty when unreadable.</summary>
        private static List<string> Products(IOrganizationService service, Guid caseId)
        {
            var names = new List<string>();
            try
            {
                var query = new QueryExpression("al_listoption") { ColumnSet = new ColumnSet("al_name") };
                var intersect = query.AddLink(
                    "al_listoption_al_outcomecase_products", "al_listoptionid", "al_listoptionid");
                intersect.LinkCriteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
                query.AddOrder("al_name", OrderType.Ascending);

                foreach (var option in CommandHelpers.RetrieveAll(service, query))
                {
                    var name = option.GetAttributeValue<string>("al_name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name.Trim());
                    }
                }
            }
            catch (Exception)
            {
                names.Clear();
            }

            return names;
        }

        // ================================================================== what is on the form

        /// <summary>One question on the form and whatever answer the review holds to it.</summary>
        public sealed class FormItem
        {
            public FormItem()
            {
                Choices = new List<int>();
            }

            public Guid SectionId { get; set; }
            public string SectionCode { get; set; }
            public string SectionName { get; set; }
            public string SectionHelp { get; set; }
            public int SectionOrder { get; set; }
            public bool SectionOptional { get; set; }
            public int QuestionOrder { get; set; }
            public string QuestionCode { get; set; }
            public string QuestionText { get; set; }
            public int ResponseType { get; set; }
            public int? Choice { get; set; }
            public List<int> Choices { get; private set; }
            public string Text { get; set; }
            public DateTime? Date { get; set; }
            public string Rich { get; set; }
        }

        /// <summary>A fail reason, and whether this review ticked it.</summary>
        public sealed class FailReasonItem
        {
            public string Name { get; set; }
            public bool Ticked { get; set; }
        }

        /// <summary>
        /// The questions the page would draw for this review, with its answers, in the page's
        /// order. Never throws; an unreadable checklist gives whatever answers could be read.
        /// </summary>
        private static List<FormItem> Items(IOrganizationService service, Entity review, DateTime asOf)
        {
            var type = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            var version = review.GetAttributeValue<EntityReference>("al_checklistversionid");

            var sections = new Dictionary<Guid, Entity>();
            var questions = new Dictionary<Guid, Entity>();
            var versions = new Dictionary<Guid, Entity>();
            var inForce = new List<Guid>();

            // 1. What is in force on the review's checklist version, for its discipline.
            if (version != null && type != null)
            {
                try
                {
                    var sectionQuery = new QueryExpression("al_section")
                    {
                        ColumnSet = SectionColumns(),
                        Criteria = new FilterExpression(),
                    };
                    sectionQuery.Criteria.AddCondition("al_checklistversionid", ConditionOperator.Equal, version.Id);

                    foreach (var section in CommandHelpers.RetrieveAll(service, sectionQuery))
                    {
                        var owner = section.GetAttributeValue<OptionSetValue>("al_ownerrole");
                        if (owner == null || !SectionRules.OwnerRoleServes(owner.Value, type.Value))
                        {
                            continue;
                        }

                        if (!ResponseRules.IsVersionEffective(
                            section.GetAttributeValue<DateTime?>("al_effectivefrom"),
                            section.GetAttributeValue<DateTime?>("al_effectiveto"),
                            asOf))
                        {
                            continue;
                        }

                        sections[section.Id] = section;
                    }

                    foreach (var question in ByIdIn(
                        service, "al_question", new ColumnSet("al_sectionid", "al_questioncode"),
                        new List<Guid>(sections.Keys), "al_sectionid"))
                    {
                        questions[question.Id] = question;
                    }

                    foreach (var questionVersion in ByIdIn(
                        service, "al_questionversion", VersionColumns(),
                        new List<Guid>(questions.Keys), "al_questionid"))
                    {
                        if (ResponseRules.IsVersionEffective(
                            questionVersion.GetAttributeValue<DateTime?>("al_effectivefrom"),
                            questionVersion.GetAttributeValue<DateTime?>("al_effectiveto"),
                            asOf))
                        {
                            versions[questionVersion.Id] = questionVersion;
                            inForce.Add(questionVersion.Id);
                        }
                    }
                }
                catch (Exception)
                {
                    inForce.Clear();
                }
            }

            // 2. The answers, and anything they were given against that is not in the set.
            var answers = new Dictionary<Guid, Entity>();
            try
            {
                var query = new QueryExpression("al_response")
                {
                    ColumnSet = new ColumnSet(
                        "al_questionversionid", "al_answerchoice", "al_answerchoices",
                        "al_answertext", "al_answerrichtext", "al_answerdate"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, review.Id);

                foreach (var response in CommandHelpers.RetrieveAll(service, query))
                {
                    var versionRef = response.GetAttributeValue<EntityReference>("al_questionversionid");
                    if (versionRef != null)
                    {
                        answers[versionRef.Id] = response;
                    }
                }

                var missing = new List<Guid>();
                foreach (var id in answers.Keys)
                {
                    if (!versions.ContainsKey(id))
                    {
                        missing.Add(id);
                    }
                }

                if (missing.Count > 0)
                {
                    foreach (var extra in ByIdIn(service, "al_questionversion", VersionColumns(), missing, null))
                    {
                        versions[extra.Id] = extra;
                    }

                    var questionIds = new List<Guid>();
                    foreach (var id in missing)
                    {
                        Entity extra;
                        var reference = versions.TryGetValue(id, out extra)
                            ? extra.GetAttributeValue<EntityReference>("al_questionid")
                            : null;
                        if (reference != null && !questions.ContainsKey(reference.Id))
                        {
                            questionIds.Add(reference.Id);
                        }
                    }

                    foreach (var question in ByIdIn(
                        service, "al_question", new ColumnSet("al_sectionid", "al_questioncode"), questionIds, null))
                    {
                        questions[question.Id] = question;
                    }

                    var sectionIds = new List<Guid>();
                    foreach (var question in questions.Values)
                    {
                        var reference = question.GetAttributeValue<EntityReference>("al_sectionid");
                        if (reference != null && !sections.ContainsKey(reference.Id))
                        {
                            sectionIds.Add(reference.Id);
                        }
                    }

                    foreach (var section in ByIdIn(service, "al_section", SectionColumns(), sectionIds, null))
                    {
                        sections[section.Id] = section;
                    }
                }
            }
            catch (Exception)
            {
                // Draw what could be read.
            }

            var items = new List<FormItem>();
            foreach (var questionVersion in versions.Values)
            {
                Entity response;
                answers.TryGetValue(questionVersion.Id, out response);

                // Only in-force versions are drawn unanswered; anything else earns its place
                // by carrying an answer.
                if (response == null && !inForce.Contains(questionVersion.Id))
                {
                    continue;
                }

                var typeValue = questionVersion.GetAttributeValue<OptionSetValue>("al_responsetype");
                if (typeValue == null)
                {
                    continue;
                }

                var question = Lookup(questions, questionVersion, "al_questionid");
                var section = Lookup(sections, question, "al_sectionid");

                var item = new FormItem
                {
                    SectionId = section == null ? Guid.Empty : section.Id,
                    SectionCode = section == null ? null : section.GetAttributeValue<string>("al_sectioncode"),
                    SectionName = section == null ? "Other" : (section.GetAttributeValue<string>("al_name") ?? "Other"),
                    SectionHelp = section == null ? null : section.GetAttributeValue<string>("al_helptext"),
                    SectionOrder = Order(section, "al_displayorder"),
                    SectionOptional = section != null && section.GetAttributeValue<bool>("al_isoptional"),
                    QuestionOrder = Order(questionVersion, "al_displayorder"),
                    QuestionCode = question == null ? null : question.GetAttributeValue<string>("al_questioncode"),
                    QuestionText = questionVersion.GetAttributeValue<string>("al_questiontext"),
                    ResponseType = typeValue.Value,
                };

                if (response != null)
                {
                    var choice = response.GetAttributeValue<OptionSetValue>("al_answerchoice");
                    item.Choice = choice == null ? (int?)null : choice.Value;

                    var choices = response.GetAttributeValue<OptionSetValueCollection>("al_answerchoices");
                    if (choices != null)
                    {
                        foreach (var value in choices)
                        {
                            item.Choices.Add(value.Value);
                        }
                    }

                    item.Text = response.GetAttributeValue<string>("al_answertext");
                    item.Date = response.GetAttributeValue<DateTime?>("al_answerdate");
                    item.Rich = response.GetAttributeValue<string>("al_answerrichtext");
                }

                items.Add(item);
            }

            items.Sort(delegate(FormItem left, FormItem right)
            {
                var bySection = left.SectionOrder.CompareTo(right.SectionOrder);
                if (bySection != 0)
                {
                    return bySection;
                }

                var byName = string.Compare(left.SectionName, right.SectionName, StringComparison.OrdinalIgnoreCase);
                if (byName != 0)
                {
                    return byName;
                }

                var byId = left.SectionId.CompareTo(right.SectionId);
                return byId != 0 ? byId : left.QuestionOrder.CompareTo(right.QuestionOrder);
            });

            return items;
        }

        private static ColumnSet SectionColumns()
        {
            return new ColumnSet(
                "al_name", "al_displayorder", "al_sectioncode", "al_helptext", "al_ownerrole",
                "al_effectivefrom", "al_effectiveto", "al_isoptional");
        }

        private static ColumnSet VersionColumns()
        {
            return new ColumnSet(
                "al_questiontext", "al_responsetype", "al_displayorder", "al_questionid",
                "al_effectivefrom", "al_effectiveto");
        }

        /// <summary>
        /// Every fail reason in display order, ticked where this review recorded it through
        /// the response-keyed intersect (AD-025). Empty when the list cannot be read.
        /// </summary>
        private static List<FailReasonItem> FailReasons(IOrganizationService service, Guid reviewId)
        {
            var reasons = new List<FailReasonItem>();
            try
            {
                var all = new QueryExpression("al_failreason") { ColumnSet = new ColumnSet("al_name", "al_displayorder") };
                all.AddOrder("al_displayorder", OrderType.Ascending);
                var rows = new List<Entity>(CommandHelpers.RetrieveAll(service, all));
                if (rows.Count == 0)
                {
                    return reasons;
                }

                var chosen = new QueryExpression("al_failreason") { ColumnSet = new ColumnSet(false) };
                var intersect = chosen.AddLink("al_al_failreason_al_response", "al_failreasonid", "al_failreasonid");
                var response = intersect.AddLink("al_response", "al_responseid", "al_responseid");
                response.LinkCriteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

                var ticked = new HashSet<Guid>();
                foreach (var row in CommandHelpers.RetrieveAll(service, chosen))
                {
                    ticked.Add(row.Id);
                }

                foreach (var row in rows)
                {
                    var name = row.GetAttributeValue<string>("al_name");
                    reasons.Add(new FailReasonItem
                    {
                        Name = string.IsNullOrWhiteSpace(name) ? "A reason with no name recorded." : name.Trim(),
                        Ticked = ticked.Contains(row.Id),
                    });
                }
            }
            catch (Exception)
            {
                return new List<FailReasonItem>();
            }

            return reasons;
        }

        // ================================================================== drawing the form

        /// <summary>
        /// Draws the form's blocks, in section order, with the fail points placed directly
        /// before File Quality Outcome as the page places them (AD-096). Public so the layout
        /// can be tested from plain items.
        /// </summary>
        public static void Draw(
            List<PdfBlock> blocks, List<FormItem> items, List<FailReasonItem> reasons, bool isTaxReview)
        {
            var placed = false;
            var hideRootCause = RootCauseHidden(items);

            var index = 0;
            while (index < items.Count)
            {
                var block = ChecklistDocument.BlockFor(items[index].SectionCode, items[index].SectionName);

                // The run of sections folding into this block.
                var run = new List<FormItem>();
                while (index < items.Count
                    && ChecklistDocument.BlockFor(items[index].SectionCode, items[index].SectionName).Id == block.Id)
                {
                    run.Add(items[index]);
                    index++;
                }

                if (!placed && reasons != null && reasons.Count > 0 && ChecklistDocument.TakesFailPointsBefore(block))
                {
                    blocks.AddRange(FailPointsBlocks(reasons));
                    placed = true;
                }

                var optional = !block.Subsections && run[0].SectionOptional ? " (Optional)" : string.Empty;
                blocks.Add(PdfBlock.Subheading(block.Title + optional));

                var intro = ChecklistDocument.IntroFor(block, run[0].SectionHelp);
                if (!string.IsNullOrWhiteSpace(intro))
                {
                    blocks.Add(PdfBlock.Paragraph(intro));
                }

                var scale = GridScale(block, run);
                if (scale != 0)
                {
                    blocks.Add(PdfBlock.Table(Grid(block, run, scale, isTaxReview)));
                }
                else
                {
                    blocks.AddRange(Inline(run, isTaxReview, hideRootCause));
                }
            }

            if (!placed && reasons != null && reasons.Count > 0)
            {
                blocks.AddRange(FailPointsBlocks(reasons));
            }
        }

        /// <summary>
        /// The scale a block's grid is headed by, or 0 where it draws as a label / value
        /// table. A seeded grid block keeps its declared scale while its questions answer on
        /// it (or on the N/A widening of it); an administered section is a grid when its
        /// questions agree on one scale. A block whose questions disagree is a label / value
        /// table, where every row draws its own options and no column heading can misdescribe
        /// it - the page's rule.
        /// </summary>
        private static int GridScale(ChecklistDocument.Block block, List<FormItem> run)
        {
            var types = new List<int>();
            var anyTick = false;
            foreach (var item in run)
            {
                if (IsLensTick(item))
                {
                    continue;
                }

                if (ChecklistDocument.IsTickScale(item.ResponseType))
                {
                    anyTick = true;
                    types.Add(item.ResponseType);
                }
            }

            var shared = ChecklistDocument.SharedScale(types);
            var declared = ChecklistDocument.DeclaredScale(block);

            if (declared != 0)
            {
                if (!anyTick)
                {
                    return declared;
                }

                return shared;
            }

            return IsKnown(block) ? 0 : shared;
        }

        private static bool IsKnown(ChecklistDocument.Block block)
        {
            return !block.Id.StartsWith("section:", StringComparison.Ordinal)
                && !block.Id.StartsWith("name:", StringComparison.Ordinal);
        }

        private static bool IsLensTick(FormItem item)
        {
            return item.QuestionCode != null
                && item.QuestionCode.Trim().EndsWith("-LENS", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a question answers on the grid's scale, so draws in its tick columns.</summary>
        private static bool OnScale(int responseType, int scale)
        {
            return responseType == scale
                || (scale == ResponseRules.TypePassFailInsufficientNa
                    && responseType == ResponseRules.TypePassFailInsufficient);
        }

        private static PdfTable Grid(ChecklistDocument.Block block, List<FormItem> run, int scale, bool isTax)
        {
            var options = ChecklistDocument.GridOptions(scale, isTax);
            var columns = options.Count + 1;

            var widths = new double[columns];
            widths[0] = GridLabel;
            for (var i = 1; i < columns; i++)
            {
                widths[i] = (1 - GridLabel) / options.Count;
            }

            var table = new PdfTable(widths);

            var head = new List<PdfCell> { PdfCell.Head(block.ColumnHeading ?? "Check") };
            foreach (var option in options)
            {
                head.Add(PdfCell.Head(option.Label, 1, true));
            }

            table.AddHeader(head.ToArray());

            var start = 0;
            while (start < run.Count)
            {
                var sectionId = run[start].SectionId;
                var end = start;
                while (end < run.Count && run[end].SectionId == sectionId)
                {
                    end++;
                }

                var first = run[start];

                // Suitability heads each subsection by its name alone from 2026-09-24 (project
                // owner): "Client Objectives & Information (COBS 9.2)", not "E1. Client ...".
                if (block.Subsections)
                {
                    var band = new PdfCell { Span = columns, Fill = PdfCell.BandFill };
                    band.Runs.Add(PdfRun.Of(SubsectionHeading(first), true));
                    table.Add(band);
                }

                FormItem lens = null;
                for (var i = start; i < end; i++)
                {
                    var item = run[i];
                    if (IsLensTick(item))
                    {
                        lens = item;
                        continue;
                    }

                    if (ChecklistDocument.IsTickScale(item.ResponseType) && OnScale(item.ResponseType, scale))
                    {
                        var permitted = ResponseRules.PermittedChoices(item.ResponseType);
                        var cells = new List<PdfCell> { PdfCell.Of(QuestionText(item)) };
                        foreach (var option in options)
                        {
                            cells.Add(Contains(permitted, option.Value)
                                ? PdfCell.Box(item.Choice.HasValue && item.Choice.Value == option.Value)
                                : PdfCell.Blank());
                        }

                        table.Add(cells.ToArray());
                    }
                    else
                    {
                        // A written, dated or plain Yes / No question inside a grid block is
                        // drawn as the page draws it: the label, then its answer across the
                        // tick columns.
                        var value = Value(item, isTax);
                        value.Span = options.Count;
                        table.Add(PdfCell.Of(QuestionText(item)), value);
                    }
                }

                if (block.Subsections && !string.IsNullOrWhiteSpace(first.SectionHelp))
                {
                    var lensCell = new PdfCell { Span = lens == null ? columns : columns - 1, Fill = PdfCell.LabelFill };
                    lensCell.Runs.Add(PdfRun.Of("Outcome lens:", true, true));
                    lensCell.Runs.Add(PdfRun.Of(" " + Collapse(first.SectionHelp)));

                    if (lens == null)
                    {
                        table.Add(lensCell);
                    }
                    else
                    {
                        var box = PdfCell.Box(lens.Choice.HasValue && lens.Choice.Value == ResponseRules.ChoiceYes);
                        box.Fill = PdfCell.LabelFill;
                        table.Add(lensCell, box);
                    }
                }

                start = end;
            }

            return table;
        }

        /// <summary>
        /// A label / value block - the Tax check, File Quality Outcome, grading - and the
        /// primary root cause breaking out into its own three-by-three table, as the page
        /// breaks it out.
        /// </summary>
        private static List<PdfBlock> Inline(List<FormItem> run, bool isTax, bool hideRootCause)
        {
            var blocks = new List<PdfBlock>();
            var table = new PdfTable(0.17, 0.83);

            foreach (var item in run)
            {
                if (GradingRules.IsRootCauseResponseType(item.ResponseType))
                {
                    if (table.Rows.Count > 0)
                    {
                        blocks.Add(PdfBlock.Table(table));
                        table = new PdfTable(0.17, 0.83);
                    }

                    if (!hideRootCause)
                    {
                        blocks.Add(PdfBlock.Table(RootCauses(item)));
                    }

                    continue;
                }

                table.Add(PdfCell.Label(QuestionText(item)), Value(item, isTax));
            }

            if (table.Rows.Count > 0)
            {
                blocks.Add(PdfBlock.Table(table));
            }

            return blocks;
        }

        /// <summary>
        /// Primary root cause as the page grids it: nine causes, three to a row, under a
        /// shaded heading. Several may be ticked from 2026-09-24.
        /// </summary>
        private static PdfTable RootCauses(FormItem item)
        {
            var table = new PdfTable(1.0 / 3, 1.0 / 3, 1.0 / 3);
            table.Add(PdfCell.Head(QuestionText(item), 3));

            var options = ChecklistDocument.InlineOptions(item.ResponseType, false);
            var row = new List<PdfCell>();
            foreach (var option in options)
            {
                var cell = new PdfCell();
                cell.Runs.Add(PdfRun.Tick(IsChosen(item, option.Value)));
                cell.Runs.Add(PdfRun.Of(option.Label, true));
                row.Add(cell);

                if (row.Count == 3)
                {
                    table.Add(row.ToArray());
                    row = new List<PdfCell>();
                }
            }

            if (row.Count > 0)
            {
                while (row.Count < 3)
                {
                    row.Add(PdfCell.Blank());
                }

                table.Add(row.ToArray());
            }

            return table;
        }

        /// <summary>
        /// Drawn only once the grade says the file did not pass, as the page draws it. A
        /// review with no grade question at all - a Tax check - hides nothing.
        /// </summary>
        private static bool RootCauseHidden(List<FormItem> items)
        {
            foreach (var item in items)
            {
                if (item.ResponseType == ResponseRules.TypeGrade)
                {
                    return !item.Choice.HasValue || item.Choice.Value == ResponseRules.ChoicePass;
                }
            }

            return false;
        }

        /// <summary>One answer as the page draws it beside its question.</summary>
        private static PdfCell Value(FormItem item, bool isTax)
        {
            ResponseRules.AnswerColumn column;
            try
            {
                column = ResponseRules.ColumnFor(item.ResponseType);
            }
            catch (ArgumentOutOfRangeException)
            {
                return PdfCell.Blank();
            }

            switch (column)
            {
                case ResponseRules.AnswerColumn.Choice:
                case ResponseRules.AnswerColumn.Choices:
                    var cell = new PdfCell();
                    foreach (var option in ChecklistDocument.InlineOptions(item.ResponseType, isTax))
                    {
                        cell.Runs.Add(PdfRun.Tick(IsChosen(item, option.Value)));
                        cell.Runs.Add(PdfRun.Of(option.Label, true));
                    }

                    return cell;

                case ResponseRules.AnswerColumn.Date:
                    return PdfCell.Of(Day(item.Date));

                case ResponseRules.AnswerColumn.RichText:
                    return Written(PlainText(item.Rich));

                default:
                    return item.ResponseType == ResponseRules.TypeMultilineText
                        ? Written(item.Text)
                        : PdfCell.Of(item.Text ?? string.Empty);
            }
        }

        /// <summary>
        /// A written answer, given the three lines of room the page's text box gives it, so
        /// an empty Case Notes reads as a box nobody wrote in rather than a missing row.
        /// </summary>
        private static PdfCell Written(string text)
        {
            var cell = PdfCell.Of((text ?? string.Empty).Trim());
            var lines = 1;
            foreach (var run in cell.Runs)
            {
                if (run.Break)
                {
                    lines++;
                }
            }

            for (; lines < 3; lines++)
            {
                cell.Runs.Add(PdfRun.LineBreak());
                cell.Runs.Add(PdfRun.Of(string.Empty));
            }

            return cell;
        }

        private static bool IsChosen(FormItem item, int value)
        {
            return (item.Choice.HasValue && item.Choice.Value == value) || item.Choices.Contains(value);
        }

        private static List<PdfBlock> FailPointsBlocks(List<FailReasonItem> reasons)
        {
            var table = new PdfTable(0.9, 0.1);
            table.AddHeader(PdfCell.Head("File Quality fail reason"), PdfCell.Head("Tick", 1, true));

            foreach (var reason in reasons)
            {
                var label = new PdfCell();
                label.Runs.Add(PdfRun.Of(reason.Name, true));
                table.Add(label, PdfCell.Box(reason.Ticked));
            }

            return new List<PdfBlock>
            {
                PdfBlock.Subheading(ChecklistDocument.FailPointsTitle),
                PdfBlock.Table(table),
            };
        }

        /// <summary>A Suitability subsection's heading: the section's name, with no code.</summary>
        public static string SubsectionHeading(FormItem item)
        {
            var name = string.IsNullOrWhiteSpace(item.SectionName) ? "Section" : item.SectionName.Trim();
            return item.SectionOptional ? name + " (Optional)" : name;
        }

        private static string QuestionText(FormItem item)
        {
            return string.IsNullOrWhiteSpace(item.QuestionText) ? "Question" : item.QuestionText.Trim();
        }

        private static bool Contains(IReadOnlyList<int> values, int value)
        {
            foreach (var candidate in values)
            {
                if (candidate == value)
                {
                    return true;
                }
            }

            return false;
        }

        // ================================================================== text helpers

        /// <summary>
        /// Markup reduced to the text a reader should see. The PDF carries no HTML, so a
        /// rich-text answer drawn as stored would show its own tags; Change 7's Tax Remedial
        /// is the answer this exists for.
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

                    // A tag boundary is a word boundary: "a<br>b" is two words.
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

        internal static string Collapse(string text)
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

        /// <summary>A date as the page prints it: "23 Sep 2026". Empty where there is none.</summary>
        internal static string Day(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        /// <summary>The ticked items, one per line, as the case stores them.</summary>
        internal static List<string> ChecklistItems(string raw)
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
        /// Rows whose key - the primary key, or <paramref name="foreignKey"/> where given - is
        /// in the set, in one query. Empty in, empty out: an In with no values is a query
        /// Dataverse rejects.
        /// </summary>
        private static IEnumerable<Entity> ByIdIn(
            IOrganizationService service, string entity, ColumnSet columns, List<Guid> ids, string foreignKey)
        {
            if (ids.Count == 0)
            {
                return new Entity[0];
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
            query.Criteria.AddCondition(foreignKey ?? entity + "id", ConditionOperator.In, keys.ToArray());

            return CommandHelpers.RetrieveAll(service, query);
        }
    }
}
