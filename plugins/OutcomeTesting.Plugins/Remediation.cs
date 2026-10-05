using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Raising the remediation action that BR-006 demands, at the moment a review is
    /// submitted.
    ///
    /// Nothing in this solution used to create an <c>al_remediationaction</c>. A non-pass
    /// outcome moved the case to Awaiting Remediation and the whole loop behind it - the
    /// adviser's response (FR-020, FR-021), the completion, the T and C sign-off (FR-023,
    /// BR-008), the BR-010 ageing clock, the "Remediation assigned" notification that
    /// <see cref="NotificationEmitterPlugin"/> already fires on Create - waited on rows
    /// that no code path could produce. The portal's remediation worklist was correct and
    /// empty. This is the missing half.
    ///
    /// Kept out of the plug-in for the reason <see cref="OutcomeRules"/> and
    /// <see cref="CaseTransitions"/> record: the arithmetic and the row's shape are worth
    /// testing without a Dataverse service, and a second caller must not be able to raise
    /// an action that differs from this one.
    /// </summary>
    public static class Remediation
    {
        private const string ActionEntity = "al_remediationaction";
        private const string ActionCodeAttr = "al_remediationactioncode";

        // al_remediationaction.al_actionstatus.
        public const int StatusOpen = 120910600;
        public const int StatusInProgress = 120910601;
        public const int StatusCompleted = 120910602;

        /// <summary>BR-010: remediation is expected to complete within ten working days.</summary>
        public const int ThresholdWorkingDays = 10;

        // al_recheckrequired on al_remediationaction (AD-095). The adviser's form asks whether
        // the remediation needs checking again; from AD-138 the answer decides whether the
        // case stops at Awaiting Recheck or closes on the approval.
        public const int RecheckRequiredYes = 120910796;
        public const int RecheckRequiredNo = 120910797;

        // al_remediationactioncode and al_name are nvarchar(100); al_description is
        // nvarchar(2000) and required. A checker's observation is free text, so the write
        // has to fit the column rather than trusting what was typed.
        private const int CodeMaxLength = 100;
        private const int NameMaxLength = 100;
        private const int DescriptionMaxLength = 2000;

        /// <summary>
        /// The business code for the action a review raises. Derived from the case
        /// reference and the review's sequence so a replayed submit resolves to the row
        /// that already exists rather than raising a second action (NFR-REL-01) - the same
        /// device <c>al_outcomecode</c> uses on the Outcome.
        /// </summary>
        public static string ActionCode(string caseReference, int sequence)
        {
            var code = "REM-" + (caseReference ?? string.Empty) + "-" + sequence;
            return code.Length <= CodeMaxLength ? code : code.Substring(0, CodeMaxLength);
        }

        /// <summary>
        /// The business code for the action raised against one item on the review, numbered
        /// from 1 in the order <see cref="NonPassItems"/> lists them.
        ///
        /// A review now raises one action per thing the checker marked down (project owner,
        /// 2026-09-10), because the agreed form carries a remedial action, an owner, a target
        /// date and a sign-off against every numbered row - and one action cannot hold four
        /// answers per issue. The index is what keeps a replayed submit resolving to the rows
        /// it already raised rather than raising the set a second time.
        ///
        /// The index is appended to the truncated stem rather than to the full code, so a
        /// long case reference cannot push the number off the end and collide two items.
        /// </summary>
        public static string ActionCode(string caseReference, int sequence, int index)
        {
            var suffix = "-" + index;
            var stem = "REM-" + (caseReference ?? string.Empty) + "-" + sequence;
            var room = CodeMaxLength - suffix.Length;
            if (stem.Length > room)
            {
                stem = stem.Substring(0, room);
            }

            return stem + suffix;
        }

        /// <summary>
        /// What the adviser is told they have to put right.
        ///
        /// <paramref name="reason"/> is the grade or result that triggered this, as a label
        /// rather than an option number. <paramref name="observation"/> is the checker's own
        /// words from the fail observation question, which AD-019 leaves optional - so this
        /// has to read as a whole sentence without it.
        ///
        /// The reason is written in brackets at the end of the standing sentence, and that
        /// is a format as much as <see cref="IssuesHeading"/> is: the two portal templates,
        /// the Code App (app/src/features/remediation/remediationIssues.ts) and the
        /// registration tool's splitter all read it back out as the remediation's Outcome,
        /// which is where the outcome is shown now that it is no longer numbered as an
        /// issue. It is the last bracket the description opens - the observation is written
        /// above this sentence - which is what makes it findable without a column of its own.
        /// </summary>
        public static string Describe(string reason, string observation)
        {
            return Describe(reason, observation, null);
        }

        /// <summary>
        /// The heading the item list is written under.
        ///
        /// Public because it is a format, not a label: the Code App
        /// (app/src/features/remediation/remediationIssues.ts) and the two portal templates
        /// split a description back into its items to number them one to a row, and this
        /// line is the marker they drop. Changing the wording here changes what they parse.
        /// </summary>
        public const string IssuesHeading = "Issues found on the check:";

        /// <summary>
        /// As <see cref="Describe(string, string)"/>, led by the issues themselves: one line
        /// per non-pass item from <see cref="NonPassItems"/>, so the "Issue / fail reason"
        /// the adviser reads is prepopulated with what the checker actually marked down
        /// (project owner, 2026-09-09) rather than a sentence that sends them back to the
        /// checklist to find out.
        ///
        /// Written as one description rather than one action per item: the action is the
        /// unit the BR-010 clock, the adviser's response and the T&amp;C sign-off hang off,
        /// and an action per item would move the case to Awaiting Sign-off on the first
        /// completion. The renderers do the numbering.
        /// </summary>
        /// <summary>
        /// The description for the action raised against one item. The item is written in
        /// the same "- item" shape the list used, so the renderers that number the rows read
        /// a one-item action and a legacy many-item one through the same path.
        /// </summary>
        public static string DescribeItem(string reason, string observation, string item)
        {
            return Describe(reason, observation, new List<string> { item });
        }

        public static string Describe(string reason, string observation, IList<string> items)
        {
            var text = "Raised automatically when the review was submitted"
                + (string.IsNullOrWhiteSpace(reason) ? string.Empty : " (" + reason.Trim() + ")")
                + ". Review the file and record what you have put right.";

            if (!string.IsNullOrWhiteSpace(observation))
            {
                text = "The checker recorded: " + observation.Trim() + Environment.NewLine + Environment.NewLine + text;
            }

            if (items != null && items.Count > 0)
            {
                var issues = IssuesHeading;
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        issues += Environment.NewLine + "- " + item.Trim();
                    }
                }

                text = issues + Environment.NewLine + Environment.NewLine + text;
            }

            return text.Length <= DescriptionMaxLength ? text : text.Substring(0, DescriptionMaxLength);
        }

        /// <summary>
        /// The questions that record the review's own result rather than something the
        /// adviser has to put right: Q-TAX-02 (Tax check outcome), and the File quality
        /// outcome question of each discipline - Q-FQ-01 for AQS, Q-FQTAX-01 for Tax.
        ///
        /// Left out of the item list for the reason the grade scale is already left out
        /// (see <see cref="IsRemediableScale"/>): the outcome is the reason the action
        /// gives, written into the description by
        /// <see cref="Describe(string, string, IList{string})"/> as "(Tax check:
        /// Pass with issues)" or as the grade's own label, so listing it again as an
        /// issue only says the same thing twice. It is the outcome, and every other item
        /// is a reason for it - the renderers show it as the remediation's Outcome and
        /// number only the reasons (project owner, 2026-09-10).
        ///
        /// A case whose only non-pass answer is its outcome therefore lists no items at
        /// all, and <see cref="Raise"/> falls to the single un-indexed action it has always
        /// raised for a grade with no test point behind it. Remediation is still owed and
        /// still raised; nothing itemisable was marked down.
        ///
        /// Held as business codes rather than question text because the text is versioned
        /// (BR-013) and the code is not.
        /// </summary>
        private static readonly HashSet<string> OutcomeQuestionCodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Q-TAX-02", "Q-FQ-01", "Q-FQTAX-01" };

        /// <summary>
        /// The scales whose answers prepopulate a remediation action: Pass / Fail,
        /// Pass / Fail / Insufficient evidence, the Consumer Duty overlay's
        /// Yes / No / Insufficient evidence, and the AML and CRA checking points'
        /// Yes / No / N/A.
        ///
        /// <b>The last two joined on 2026-09-11 (project owner), superseding the 2026-09-09
        /// direction that kept every yes/no scale out.</b> Each reaches exactly the questions
        /// it was asked for and nothing else, which is why the widening is by scale:
        ///
        /// - Yes / No / Insufficient evidence is used only by Q-CD-01 to Q-CD-04, the
        ///   Consumer Duty overlay's four outcomes.
        /// - Yes / No / N/A is used only by Q-AML-01 to Q-AML-05, section S-AMLCRA.
        ///
        /// <b>Plain Yes / No is deliberately still out, and it is not the same omission.</b>
        /// That scale carries Q-FQ-03 and Q-FQTAX-03, both "Remedial action required?", which
        /// is the question that <em>raises</em> remediation - <c>OutcomeRules.RemedialActionFlagged</c>
        /// reads it. A No there means no remedial action is needed, so listing it would put
        /// "Remedial action required?: No" in front of the adviser as something to put right
        /// on cases graded down for other reasons. It also carries Q-E2-LENS, the outcome-lens
        /// tick, which is a judgement about the file and not a test point of its own.
        ///
        /// The grade scale is out too. The grade is already the reason the action gives
        /// (<see cref="Describe(string, string, IList{string})"/>), so listing it again as
        /// an issue only says the same thing twice.
        /// </summary>
        public static bool IsRemediableScale(int responseType)
        {
            return responseType == ResponseRules.TypePassFail
                || responseType == ResponseRules.TypePassFailInsufficient
                || responseType == ResponseRules.TypePassFailInsufficientNa
                || responseType == ResponseRules.TypeYesNoInsufficient
                || responseType == ResponseRules.TypeYesNoNa;
        }

        /// <summary>The scales <see cref="IsRemediableScale"/> admits, for the pre-filter below.</summary>
        private static readonly int[] RemediableScales =
        {
            ResponseRules.TypePassFail,
            ResponseRules.TypePassFailInsufficient,
            ResponseRules.TypePassFailInsufficientNa,
            ResponseRules.TypeYesNoInsufficient,
            ResponseRules.TypeYesNoNa,
        };

        /// <summary>
        /// Whether an answer could be a non-pass on <em>some</em> remediable scale.
        ///
        /// The cheap first look, taken before the question version has been read and so
        /// before the scale is known. Derived from <see cref="IsNonPassAnswer"/> over every
        /// remediable scale rather than restated by hand: a hand-kept copy admitted "IsNonPass
        /// or No" and had to be kept in step by comment, so a scale that marks down on some
        /// other value would have passed every IsNonPassAnswer test while this filter quietly
        /// dropped its rows before they were reached. <see cref="IsNonPassAnswer"/> still
        /// decides once the scale is known.
        /// </summary>
        private static bool CouldBeNonPass(int choice)
        {
            foreach (var scale in RemediableScales)
            {
                if (IsNonPassAnswer(scale, choice))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// An answer that belongs on the remediation action's issue list: something the
        /// checker marked down on a scale <see cref="IsRemediableScale"/> admits.
        ///
        /// What counts as marked down is the scale's own question, which is why this takes
        /// both. A Fail or an Insufficient evidence on a pass/fail scale; a No on an AML or
        /// CRA checking point; a No or an Insufficient evidence on a Consumer Duty outcome.
        ///
        /// <b>N/A is not a failure</b> and is the reason the AML scale cannot be read by
        /// <see cref="ResponseRules.IsNonPass"/> alone: a checking point that does not apply
        /// to this case is answered N/A, and putting that in front of an adviser as
        /// something to put right would be worse than saying nothing. Potential harm cannot
        /// reach here, being only on the grade scale.
        /// </summary>
        public static bool IsNonPassAnswer(int responseType, int choice)
        {
            if (!IsRemediableScale(responseType))
            {
                return false;
            }

            if (responseType == ResponseRules.TypeYesNoNa)
            {
                return choice == ResponseRules.ChoiceNo;
            }

            if (responseType == ResponseRules.TypeYesNoInsufficient)
            {
                return choice == ResponseRules.ChoiceNo
                    || choice == ResponseRules.ChoiceInsufficient;
            }

            return ResponseRules.IsNonPass(choice);
        }

        /// <summary>
        /// One line per thing the checker marked down, in the order the checklist lays them
        /// out: every Fail or Insufficient evidence recorded on a pass/fail test point
        /// (<see cref="IsRemediableScale"/>) whose question version is in force on
        /// <paramref name="asOf"/>, each as "question: answer", followed by every File
        /// Quality fail point ticked on the review, as the reason reads on the checklist. This
        /// is what the
        /// remediation action's "Issue / fail reason" is prepopulated with.
        ///
        /// From 2026-09-11 this includes the Consumer Duty overlay's No and Insufficient
        /// evidence answers and a No on an AML or CRA checking point (project owner), which
        /// supersedes the 2026-09-09 direction that kept every yes/no scale out. Plain
        /// Yes / No is still out, and <see cref="IsRemediableScale"/> records why: that scale
        /// carries "Remedial action required?", where a No means no action is needed. The
        /// File Quality fail points below are unaffected and still carry the AML, Breach and
        /// Record Keeping failures in their own right (AD-096).
        ///
        /// Read from the responses rather than from a fixed list of questions so a
        /// checklist change (FR-030) reaches here without a code change. A retired version's
        /// answer is left out for the reason SubmitReviewPlugin.AnswerFor records: it is not
        /// the answer the review gave.
        ///
        /// So is an answer, or a fail point ticked on one, in a section the review page does
        /// not draw for this review on <paramref name="asOf"/> - retired, or owned by the other
        /// discipline (<see cref="SectionDrawn"/>). The checker's card is built from what the
        /// page draws and the submit gate from this list, so the two must agree on it.
        /// </summary>
        public static List<string> NonPassItems(IOrganizationService service, Guid reviewId, DateTime asOf)
        {
            var labels = new OptionLabels(service);
            var answers = new List<RankedItem>();
            var responseIds = new List<object>();
            var versionOfResponse = new Dictionary<Guid, Guid>();

            var responses = new QueryExpression("al_response")
            {
                ColumnSet = new ColumnSet("al_answerchoice", "al_questionversionid"),
                Criteria = new FilterExpression(),
            };
            responses.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

            // Two passes, so the reads below are batched rather than paid per answer. The
            // first pass keeps only what the second needs: a non-pass answer and the version
            // it was given against. The cheap check on the answer still runs first, so a Pass
            // costs nothing here either.
            var candidates = new List<KeyValuePair<Guid, OptionSetValue>>();
            foreach (var response in CommandHelpers.RetrieveAll(service, responses))
            {
                responseIds.Add(response.Id);

                var versionRef = response.GetAttributeValue<EntityReference>("al_questionversionid");
                if (versionRef != null)
                {
                    versionOfResponse[response.Id] = versionRef.Id;
                }

                var choice = response.GetAttributeValue<OptionSetValue>("al_answerchoice");
                if (choice == null || !CouldBeNonPass(choice.Value) || versionRef == null)
                {
                    continue;
                }

                candidates.Add(new KeyValuePair<Guid, OptionSetValue>(versionRef.Id, choice));
            }

            // The fail points are keyed by response (AD-025) but are one block of the
            // checklist (AD-096), so they are read across every answer on the review and
            // listed once each. Read here, ahead of the versions, because the answer each one
            // hangs off decides whether the page can draw it - see the section test below.
            var links = new List<KeyValuePair<Guid, Guid>>();
            if (responseIds.Count > 0)
            {
                var linkQuery = new QueryExpression("al_al_failreason_al_response")
                {
                    ColumnSet = new ColumnSet("al_failreasonid", "al_responseid"),
                    Criteria = new FilterExpression(),
                };
                linkQuery.Criteria.AddCondition("al_responseid", ConditionOperator.In, responseIds.ToArray());

                foreach (var link in service.RetrieveMultiple(linkQuery).Entities)
                {
                    links.Add(new KeyValuePair<Guid, Guid>(
                        link.GetAttributeValue<Guid>("al_responseid"),
                        link.GetAttributeValue<Guid>("al_failreasonid")));
                }
            }

            var versionIds = Ids(candidates);
            foreach (var link in links)
            {
                Guid hostVersion;
                if (versionOfResponse.TryGetValue(link.Key, out hostVersion) && !versionIds.Contains(hostVersion))
                {
                    versionIds.Add(hostVersion);
                }
            }

            // One query each for the versions, their questions and those questions' sections,
            // rather than three Retrieves per marked-down answer. A fifteen-item review was
            // paying forty-five round trips for what three answer.
            var versions = ByIdIn(
                service,
                "al_questionversion",
                new ColumnSet("al_questiontext", "al_responsetype", "al_effectivefrom", "al_effectiveto", "al_displayorder", "al_questionid"),
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

            var questions = ByIdIn(
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

            var sections = ByIdIn(
                service,
                "al_section",
                new ColumnSet("al_displayorder", "al_effectivefrom", "al_effectiveto", "al_ownerrole"),
                sectionIds);

            var reviewType = ReviewTypeOf(service, reviewId);

            foreach (var candidate in candidates)
            {
                var choice = candidate.Value;

                Entity version;
                if (!versions.TryGetValue(candidate.Key, out version))
                {
                    continue;
                }

                // The scale decides this, not the answer on its own: Insufficient evidence is
                // one option value shared by the suitability scale and the Consumer Duty one,
                // and only the first of those is a remediable test point.
                var responseType = version.GetAttributeValue<OptionSetValue>("al_responsetype");
                if (responseType == null || !IsNonPassAnswer(responseType.Value, choice.Value))
                {
                    continue;
                }

                if (!ResponseRules.IsVersionEffective(
                    version.GetAttributeValue<DateTime?>("al_effectivefrom"),
                    version.GetAttributeValue<DateTime?>("al_effectiveto"),
                    asOf))
                {
                    continue;
                }

                var text = version.GetAttributeValue<string>("al_questiontext");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // One lookup of the question serves both the outcome test and the ordering
                // below, which is why it is resolved here rather than inside SectionOrder.
                var question = Lookup(questions, version, "al_questionid");
                if (question != null
                    && OutcomeQuestionCodes.Contains(question.GetAttributeValue<string>("al_questioncode") ?? string.Empty))
                {
                    continue;
                }

                var section = Lookup(sections, question, "al_sectionid");
                if (!SectionDrawn(section, reviewType, asOf))
                {
                    continue;
                }

                answers.Add(new RankedItem
                {
                    Section = section == null ? 0 : section.GetAttributeValue<int?>("al_displayorder") ?? 0,
                    Order = version.GetAttributeValue<int?>("al_displayorder") ?? 0,
                    Text = text.Trim() + ": " + labels.Label("al_response", "al_answerchoice", choice.Value),
                });
            }

            answers.Sort(RankedItem.Compare);
            var items = new List<string>();
            foreach (var answer in answers)
            {
                items.Add(answer.Text);
            }

            if (links.Count == 0)
            {
                return items;
            }

            // A tick is drawn with the File Quality block, which sits in its section, so a
            // tick on an answer whose section the page no longer draws is left out for the
            // same reason as the answer itself. An answer whose version or section cannot be
            // resolved keeps its ticks: nothing says the page hides it.
            var seen = new HashSet<Guid>();
            var ticked = new List<Guid>();
            foreach (var link in links)
            {
                var reasonId = link.Value;
                if (reasonId == Guid.Empty || seen.Contains(reasonId))
                {
                    continue;
                }

                Guid hostVersion;
                Entity host;
                if (versionOfResponse.TryGetValue(link.Key, out hostVersion)
                    && versions.TryGetValue(hostVersion, out host)
                    && !SectionDrawn(Lookup(sections, Lookup(questions, host, "al_questionid"), "al_sectionid"), reviewType, asOf))
                {
                    continue;
                }

                seen.Add(reasonId);
                ticked.Add(reasonId);
            }

            // One query for the ticked reasons, not one Retrieve each. AD-096 defines twenty
            // of them, so a review that ticked most of the block was paying twenty round
            // trips to read two columns immediately after the query that found them.
            //
            // al_name holds the document's whole row, category prefix included ("AML - ID
            // verification issue"), because the document does not punctuate the twenty rows
            // consistently and a label built from al_category plus a separator cannot
            // reproduce that. So the name is used as written and nothing is prefixed here -
            // including the "Fail point: " label these carried until 2026-09-11, dropped
            // (project owner) so the adviser reads the issue itself rather than a category
            // word in front of every other row.
            var reasons = ByIdIn(
                service, "al_failreason", new ColumnSet("al_name", "al_displayorder"), ticked);

            var points = new List<RankedItem>();
            foreach (var reasonId in ticked)
            {
                Entity reason;
                if (!reasons.TryGetValue(reasonId, out reason))
                {
                    continue;
                }

                var name = reason.GetAttributeValue<string>("al_name") ?? string.Empty;

                points.Add(new RankedItem
                {
                    Section = 0,
                    Order = reason.GetAttributeValue<int?>("al_displayorder") ?? 0,
                    Text = name.Trim(),
                });
            }

            points.Sort(RankedItem.Compare);
            foreach (var point in points)
            {
                items.Add(point.Text);
            }

            return items;
        }

        /// <summary>
        /// Whether the review page draws a section for this review on <paramref name="asOf"/>:
        /// in force, and owned by the review's discipline or by Both. The same two tests the
        /// page's section fetch and SubmitReviewPlugin's mandatory-question query apply
        /// (AD-020, AD-123), so the list never names an answer the checker's card cannot show.
        /// Without them a section retired (RetireSection) or handed to the other team
        /// (UpdateSection) while a review held a non-pass answer there left the submit gate
        /// demanding words for a row nobody could see, and the review could never be submitted.
        ///
        /// Lenient where the facts are missing, never where they are present: an unresolved
        /// section, a section with no owner role, or a review whose discipline is unknown is
        /// counted as drawn. al_ownerrole is required on al_section, so the platform never
        /// holds the null case; treating it as served keeps a fixture or an old row from
        /// silently losing items, which is the direction a gate should fail in.
        /// </summary>
        private static bool SectionDrawn(Entity section, int? reviewType, DateTime asOf)
        {
            if (section == null)
            {
                return true;
            }

            if (!SectionRules.IsSectionEffective(
                section.GetAttributeValue<DateTime?>("al_effectivefrom"),
                section.GetAttributeValue<DateTime?>("al_effectiveto"),
                asOf))
            {
                return false;
            }

            var ownerRole = section.GetAttributeValue<OptionSetValue>("al_ownerrole");
            if (ownerRole == null || !reviewType.HasValue)
            {
                return true;
            }

            return SectionRules.OwnerRoleServes(ownerRole.Value, reviewType.Value);
        }

        /// <summary>
        /// The review's discipline, or null when the review or its type cannot be read. Read by
        /// query rather than Retrieve so a caller holding only responses - as the tests do -
        /// gets the unfiltered list rather than a fault.
        /// </summary>
        private static int? ReviewTypeOf(IOrganizationService service, Guid reviewId)
        {
            Entity review;
            if (!ByIdIn(service, "al_reviewinstance", new ColumnSet("al_reviewtype"), new List<Guid> { reviewId })
                .TryGetValue(reviewId, out review))
            {
                return null;
            }

            var type = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            return type == null ? (int?)null : type.Value;
        }

        /// <summary>The version ids of a candidate list, in order and with repeats kept out.</summary>
        private static List<Guid> Ids(List<KeyValuePair<Guid, OptionSetValue>> candidates)
        {
            var ids = new List<Guid>();
            var seen = new HashSet<Guid>();
            foreach (var candidate in candidates)
            {
                if (seen.Add(candidate.Key))
                {
                    ids.Add(candidate.Key);
                }
            }

            return ids;
        }

        /// <summary>
        /// Reads a set of rows by primary key in one query, keyed by id.
        ///
        /// Empty in, empty out - an <c>In</c> with no values is a query Dataverse rejects,
        /// and a review with nothing marked down is the ordinary case rather than an error.
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

        /// <summary>
        /// The row a lookup on <paramref name="from"/> points at, out of an already-read set;
        /// null where there is no lookup or the target was not returned.
        /// </summary>
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

        private sealed class RankedItem
        {
            public int Section;
            public int Order;
            public string Text;

            public static int Compare(RankedItem a, RankedItem b)
            {
                var bySection = a.Section.CompareTo(b.Section);
                if (bySection != 0)
                {
                    return bySection;
                }

                var byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Text, b.Text);
            }
        }

        /// <summary>Monday to Friday. Bank holidays are not deducted (OD-018).</summary>
        private static bool IsWorkingDay(DateTime day)
        {
            return day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday;
        }

        /// <summary>
        /// <paramref name="start"/> advanced by <paramref name="count"/> working days,
        /// counting the starting day as day 1 (OD-018). Ten working days from a Monday is
        /// therefore the Friday of the following week, which is the date the BR-010
        /// threshold falls due.
        ///
        /// Mirrors <c>addWorkingDays</c> in app/src/lib/workingDays.ts deliberately: the
        /// date written here and the age the portal renders against it have to be the same
        /// arithmetic, or a case reads as breached a day before its own due date.
        ///
        /// Returns a UK calendar date at midnight, which is what a date-only column holds.
        /// </summary>
        public static DateTime AddWorkingDays(DateTime start, int count)
        {
            var cursor = UkDate(start);

            var remaining = count;
            if (remaining > 0 && IsWorkingDay(cursor))
            {
                remaining -= 1;
            }

            while (remaining > 0)
            {
                cursor = cursor.AddDays(1);
                if (IsWorkingDay(cursor))
                {
                    remaining -= 1;
                }
            }

            // Land on a working day even where the start was a weekend and the count
            // consumed nothing: an action is never due on a Saturday.
            while (!IsWorkingDay(cursor))
            {
                cursor = cursor.AddDays(1);
            }

            return cursor;
        }

        /// <summary>
        /// Raises one action per item the checker marked down, and returns their ids in the
        /// order they were raised. An item already raised is returned rather than written
        /// again, so a replayed submit produces the same set.
        ///
        /// One action per item because the agreed form (project owner, 2026-09-10) carries a
        /// remedial action, an owner, a target date and a sign-off against every numbered
        /// row, and those are single-valued on the action - a shared action can show the
        /// issues as rows but cannot answer them one at a time.
        ///
        /// <paramref name="items"/> is the <see cref="NonPassItems"/> list and may be null
        /// or empty; the description then reads as it did before the list existed.
        ///
        /// <paramref name="adviserContact"/> may be null: the case's adviser email can match
        /// no contact or two (AD-228).
        /// <see cref="AdviserContact"/> refuses to guess, and an unassigned action is far
        /// better than no action - the remediation is on the worklist for a manager to
        /// route, rather than lost to a directory gap.
        ///
        /// <paramref name="remedialActions"/> is the checker's remedial action for each item,
        /// parallel to <paramref name="items"/>, and <paramref name="overallRemedialAction"/> is
        /// the one for a check with nothing itemised (project owner, 2026-09-29). Either may be
        /// null: an action raised without words still raises, and the adviser can still answer it.
        /// </summary>
        public static IList<Guid> Raise(
            IOrganizationService service,
            EntityReference caseRef,
            string caseReference,
            Guid reviewId,
            int sequence,
            string reason,
            string observation,
            IList<string> items,
            EntityReference adviserContact,
            DateTime raisedOn,
            IList<string> remedialActions = null,
            string overallRemedialAction = null)
        {
            var raised = RaiseActions(
                service, caseRef, caseReference, reviewId, sequence, reason, observation,
                items, adviserContact, raisedOn, remedialActions, overallRemedialAction);

            // The letters were queued on the first create and drew one action; every action
            // exists now (2026-10-05).
            NotificationOutbox.RefreshDocuments(service, caseRef, reviewId);
            return raised;
        }

        /// <summary>The actions themselves; see <see cref="Raise"/>.</summary>
        private static IList<Guid> RaiseActions(
            IOrganizationService service,
            EntityReference caseRef,
            string caseReference,
            Guid reviewId,
            int sequence,
            string reason,
            string observation,
            IList<string> items,
            EntityReference adviserContact,
            DateTime raisedOn,
            IList<string> remedialActions = null,
            string overallRemedialAction = null)
        {
            var raised = new List<Guid>();

            // No item list is not a reason to raise nothing: a grade can require remediation
            // with no pass/fail test point behind it, and that case keeps the un-indexed code
            // it has always had, so a replay still finds the row it raised.
            if (items == null || items.Count == 0)
            {
                raised.Add(RaiseOne(
                    service,
                    caseRef,
                    caseReference,
                    reviewId,
                    ActionCode(caseReference, sequence),
                    Describe(reason, observation, null),
                    adviserContact,
                    raisedOn,
                    overallRemedialAction));
                return raised;
            }

            // Indexed by position in the list the checker's words were aligned to, which
            // includes any blank item - so a skipped blank does not shift every later action
            // onto its neighbour's words.
            var index = 0;
            for (var position = 0; position < items.Count; position++)
            {
                var item = items[position];
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                index++;
                raised.Add(RaiseOne(
                    service,
                    caseRef,
                    caseReference,
                    reviewId,
                    ActionCode(caseReference, sequence, index),
                    DescribeItem(reason, observation, item),
                    adviserContact,
                    raisedOn,
                    remedialActions != null && position < remedialActions.Count ? remedialActions[position] : null));
            }

            // Every item was blank, which the list should never carry - fall back to the one
            // action rather than leaving the case in remediation with nothing to do.
            if (raised.Count == 0)
            {
                raised.Add(RaiseOne(
                    service,
                    caseRef,
                    caseReference,
                    reviewId,
                    ActionCode(caseReference, sequence),
                    Describe(reason, observation, null),
                    adviserContact,
                    raisedOn,
                    overallRemedialAction));
            }

            return raised;
        }

        /// <summary>
        /// One action, or the id of the one already carrying <paramref name="code"/>.
        ///
        /// An existing code is a reason to do nothing at all, not to write again. An upsert
        /// would put the status back to Open and overwrite <c>al_adviserresponse</c>, so a
        /// replay would silently undo work the adviser had already done and restart their
        /// ten days.
        /// </summary>
        private static Guid RaiseOne(
            IOrganizationService service,
            EntityReference caseRef,
            string caseReference,
            Guid reviewId,
            string code,
            string description,
            EntityReference adviserContact,
            DateTime raisedOn,
            string remedialAction)
        {
            var existing = FindByCode(service, code);
            if (existing != Guid.Empty)
            {
                return existing;
            }

            var name = "Remediation " + (caseReference ?? string.Empty);
            if (name.Length > NameMaxLength)
            {
                name = name.Substring(0, NameMaxLength);
            }

            var action = new Entity(ActionEntity)
            {
                ["al_name"] = name,
                [ActionCodeAttr] = code,
                ["al_description"] = description,
                ["al_outcomecaseid"] = caseRef,
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", reviewId),
                ["al_actionstatus"] = new OptionSetValue(StatusOpen),
                ["al_duedate"] = AddWorkingDays(raisedOn, ThresholdWorkingDays),
            };

            // The checker's words, written once, here. RemediationResponseGuardPlugin refuses
            // every later write to the column, so this is the only moment it can be set.
            var remedial = (remedialAction ?? string.Empty).Trim();
            if (remedial.Length > 0)
            {
                action[RemedialActions.ActionAttr] = remedial.Length <= RemedialActions.MaxLength
                    ? remedial
                    : remedial.Substring(0, RemedialActions.MaxLength);
            }

            // Set only when resolved. Writing an explicit null would be the same row to
            // Dataverse, but leaving the column absent keeps "we could not identify the
            // adviser" distinguishable from "the lookup was cleared".
            if (adviserContact != null)
            {
                action["al_assignedcontactid"] = adviserContact;
            }

            // al_clockstartedon is deliberately left unset. It holds the start of a period
            // a rejected sign-off restarted (OD-018); until then the clock runs from
            // createdon, which is what both the portal and app/src/lib/workingDays.ts fall
            // back to. Stamping it here would make every action look like a reworked one.
            return service.Create(action);
        }

        /// <summary>
        /// The contact whose email the case stores as its adviser's, or null when the case
        /// has no email, or it matches no active contact or two (AD-228). Never by name.
        /// </summary>
        public static EntityReference AdviserContact(IOrganizationService service, EntityReference caseRef)
        {
            if (caseRef == null)
            {
                return null;
            }

            var row = service.Retrieve("al_outcomecase", caseRef.Id, new ColumnSet(CaseAdviser.EmailAttr));
            var match = NotificationOutbox.MatchAdviser(service, row.GetAttributeValue<string>(CaseAdviser.EmailAttr));
            return match.IsMatch ? match.Contact : null;
        }

        /// <summary>
        /// Points the case's open remediation actions at the contact holding the adviser
        /// email now on the case, and tells them (PP-15 "Remediation assigned"). Returns how
        /// many moved. An email that no single active contact holds - none, two, or a blank
        /// email - unassigns the open actions (AD-228).
        ///
        /// Two repairs, one rule. <see cref="Raise"/> leaves an action unassigned when the
        /// adviser email matches no active contact or two, and nothing could assign it
        /// afterwards: the portal's response panel opens only for the assigned contact, so
        /// such an action sat on the worklist with nobody able to answer it. Separately, a
        /// case whose adviser changes after the actions were raised left them pinned to the
        /// person who has just been taken off the case — which is the same failure with a
        /// different cause, and the one that blocked case 254397454 on 2026-09-18.
        ///
        /// This used to fill only a null assignee, on the reading that "a header edit is not
        /// a reassignment". That reading does not survive contact with a reassigned case:
        /// the adviser email IS how remediation is routed (see <see cref="AdviserContact"/>),
        /// so changing it and having the work stay put leaves an action nobody on the case
        /// can answer and no supported way to move it. Changing the adviser email now moves
        /// the open actions with it; changing only the name is a label and moves nothing.
        ///
        /// Two things still never move. A <b>completed</b> action is history (BR-007), so it
        /// keeps whoever actually did the work. An action <b>already held by the contact the
        /// adviser email resolves to</b> is left untouched rather than rewritten, so an edit
        /// that does not change who the adviser is cannot re-notify them.
        /// </summary>
        public static int AssignOpenActions(IOrganizationService service, EntityReference caseRef, Guid correlationId)
        {
            var adviser = AdviserContact(service, caseRef);

            // The assignee is read rather than filtered on, because "not this adviser" has to
            // admit a null: a Dataverse NotEqual on a lookup excludes rows where the column is
            // null, which would drop the unassigned actions this exists to repair.
            var query = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet("al_assignedcontactid"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseRef.Id);
            query.Criteria.AddCondition("al_actionstatus", ConditionOperator.NotEqual, StatusCompleted);

            var moved = 0;
            foreach (var action in CommandHelpers.RetrieveAll(service, query))
            {
                var holder = action.GetAttributeValue<EntityReference>("al_assignedcontactid");

                // No single active contact holds the adviser email (none does, two do, or the
                // case has none): the action leaves whoever held it, because nothing shows
                // they are the adviser on this case, and nobody is guessed in their place
                // (AD-228). Nobody is told - there is nobody to tell.
                if (adviser == null)
                {
                    if (holder == null)
                    {
                        continue;
                    }

                    service.Update(new Entity(ActionEntity, action.Id) { ["al_assignedcontactid"] = null });
                    moved++;
                    continue;
                }

                if (holder != null && holder.Id == adviser.Id)
                {
                    continue;
                }

                service.Update(new Entity(ActionEntity, action.Id) { ["al_assignedcontactid"] = adviser });
                NotificationEmitterPlugin.QueueRemediationAssigned(service, correlationId, action.Id);
                moved++;
            }

            return moved;
        }

        /// <summary>
        /// Re-points the case's open remediation actions to whoever its adviser email now
        /// resolves to, and appends the one audit line both edit paths show for it (AD-228):
        /// "Re-pointed ... to the adviser email now on the case" when the email still resolves
        /// to exactly one active contact, or "Unassigned ... no single active contact holds the
        /// adviser email" when it does not - so the history reads as what actually happened,
        /// not as a re-point that left nobody holding the action.
        ///
        /// Called only when the adviser email itself changed
        /// (<see cref="CasePeople.AdviserEmailChanged"/>): a name-only change is a label and
        /// this is never reached. Shared by <see cref="UpdateCaseDetailsPlugin"/> and
        /// <see cref="CaseHeaderRequestPlugin"/> so the wording cannot drift between them.
        /// </summary>
        public static void ApplyAdviserEmailChange(
            IOrganizationService service, EntityReference caseRef, Guid correlationId, List<string> changes)
        {
            var moved = AssignOpenActions(service, caseRef, correlationId);
            if (moved == 0)
            {
                return;
            }

            changes.Add(AdviserContact(service, caseRef) != null
                ? "Re-pointed " + moved + " open remediation action(s) to the adviser email now on the case"
                : "Unassigned " + moved + " open remediation action(s): no single active contact holds the adviser email");
        }

        private static Guid FindByCode(IOrganizationService service, string code)
        {
            var query = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(ActionCodeAttr, ConditionOperator.Equal, code);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0].Id : Guid.Empty;
        }

        /// <summary>
        /// The UK calendar date of a timestamp, at midnight - the clock the BR-010 due date
        /// is counted in (OD-018). A submission at 23:30 UTC on a British Summer Time
        /// evening belongs to the next UK day; taking the UTC date would set the due date a
        /// day early for those, and disagree with the age app/src/lib/workingDays.ts renders
        /// on the very same row.
        ///
        /// The rule itself lives on CaseHeaderRules, which needs the same UK day to decide
        /// whether a date of meeting is in the future. One definition, so the two cannot
        /// disagree about when a day ends.
        /// </summary>
        private static DateTime UkDate(DateTime value)
        {
            return CaseHeaderRules.UkDate(value);
        }
    }
}
