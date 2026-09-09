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

        // al_remediationactioncode and al_name are nvarchar(100); al_description is
        // nvarchar(2000) and required. A checker's observation is free text, so the write
        // has to fit the column rather than trusting what was typed.
        private const int CodeMaxLength = 100;
        private const int NameMaxLength = 100;
        private const int DescriptionMaxLength = 2000;

        /// <summary>
        /// The UK time zone the BR-010 clock runs in (OD-018).
        ///
        /// Dataverse hands back UTC, and a submission at 23:30 UTC on a British Summer Time
        /// evening belongs to the next UK day. Taking the UTC date would set the due date a
        /// day early for those, and disagree with the age app/src/lib/workingDays.ts renders
        /// on the very same row.
        /// </summary>
        private const string UkTimeZoneId = "GMT Standard Time";

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
        /// The scales whose answers prepopulate a remediation action: Pass / Fail and
        /// Pass / Fail / Insufficient evidence, and no others (project owner, 2026-09-09).
        ///
        /// The yes/no scales are deliberately out. A No on an AML and CRA checking point or
        /// on a Consumer Duty outcome used to be listed as something the adviser had to put
        /// right, and so did an Insufficient evidence on Consumer Duty, which answers
        /// Yes / No / Insufficient evidence - the same option value the suitability scale
        /// uses, which is why the answer alone cannot decide this and the scale has to.
        ///
        /// The grade scale is out too. The grade is already the reason the action gives
        /// (<see cref="Describe(string, string, IList{string})"/>), so listing it again as
        /// an issue only says the same thing twice.
        /// </summary>
        public static bool IsRemediableScale(int responseType)
        {
            return responseType == ResponseRules.TypePassFail
                || responseType == ResponseRules.TypePassFailInsufficient;
        }

        /// <summary>
        /// An answer that belongs on the remediation action's issue list: a Fail or an
        /// Insufficient evidence recorded on one of the pass/fail scales
        /// (<see cref="IsRemediableScale"/>). Potential harm cannot reach here, being only
        /// on the grade scale, and neither can a No.
        /// </summary>
        public static bool IsNonPassAnswer(int responseType, int choice)
        {
            return IsRemediableScale(responseType) && ResponseRules.IsNonPass(choice);
        }

        /// <summary>
        /// One line per thing the checker marked down, in the order the checklist lays them
        /// out: every Fail or Insufficient evidence recorded on a pass/fail test point
        /// (<see cref="IsRemediableScale"/>) whose question version is in force on
        /// <paramref name="asOf"/>, each as "question: answer", followed by every File
        /// Quality fail point ticked on the review as "Fail point: reason". This is what the
        /// remediation action's "Issue / fail reason" is prepopulated with.
        ///
        /// The yes/no answers are not here and are not an omission: remediation is raised
        /// against the pass/fail test points, and the fail points below carry the AML, Breach
        /// and Record Keeping failures in their own right (project owner, 2026-09-09).
        ///
        /// Read from the responses rather than from a fixed list of questions so a
        /// checklist change (FR-030) reaches here without a code change. A retired version's
        /// answer is left out for the reason SubmitReviewPlugin.AnswerFor records: it is not
        /// the answer the review gave.
        /// </summary>
        public static List<string> NonPassItems(IOrganizationService service, Guid reviewId, DateTime asOf)
        {
            var labels = new OptionLabels(service);
            var answers = new List<RankedItem>();
            var responseIds = new List<object>();

            var responses = new QueryExpression("al_response")
            {
                ColumnSet = new ColumnSet("al_answerchoice", "al_questionversionid"),
                Criteria = new FilterExpression(),
            };
            responses.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

            foreach (var response in CommandHelpers.RetrieveAll(service, responses))
            {
                responseIds.Add(response.Id);

                var choice = response.GetAttributeValue<OptionSetValue>("al_answerchoice");
                if (choice == null || !ResponseRules.IsNonPass(choice.Value))
                {
                    continue;
                }

                var versionRef = response.GetAttributeValue<EntityReference>("al_questionversionid");
                if (versionRef == null)
                {
                    continue;
                }

                var version = service.Retrieve(
                    "al_questionversion",
                    versionRef.Id,
                    new ColumnSet("al_questiontext", "al_responsetype", "al_effectivefrom", "al_effectiveto", "al_displayorder", "al_questionid"));

                // The scale decides this, not the answer on its own: Insufficient evidence is
                // one option value shared by the suitability scale and the Consumer Duty one,
                // and only the first of those is a remediable test point. The cheap check on
                // the answer above runs first so a Pass never costs a Retrieve.
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

                answers.Add(new RankedItem
                {
                    Section = SectionOrder(service, version.GetAttributeValue<EntityReference>("al_questionid")),
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

            if (responseIds.Count == 0)
            {
                return items;
            }

            // The fail points are keyed by response (AD-025) but are one block of the
            // checklist (AD-096), so they are read across every answer on the review and
            // listed once each.
            var links = new QueryExpression("al_al_failreason_al_response")
            {
                ColumnSet = new ColumnSet("al_failreasonid"),
                Criteria = new FilterExpression(),
            };
            links.Criteria.AddCondition("al_responseid", ConditionOperator.In, responseIds.ToArray());

            var seen = new HashSet<Guid>();
            var points = new List<RankedItem>();
            foreach (var link in service.RetrieveMultiple(links).Entities)
            {
                var reasonId = link.GetAttributeValue<Guid>("al_failreasonid");
                if (reasonId == Guid.Empty || !seen.Add(reasonId))
                {
                    continue;
                }

                // al_name holds the document's whole row, category prefix included ("AML - ID
                // verification issue"), because the document does not punctuate the twenty
                // rows consistently and a label built from al_category plus a separator
                // cannot reproduce that. So the name is used as written and nothing is
                // prefixed here; al_category is still read, for ordering within a category.
                var reason = service.Retrieve(
                    "al_failreason", reasonId, new ColumnSet("al_name", "al_displayorder"));
                var name = reason.GetAttributeValue<string>("al_name") ?? string.Empty;

                points.Add(new RankedItem
                {
                    Section = 0,
                    Order = reason.GetAttributeValue<int?>("al_displayorder") ?? 0,
                    Text = "Fail point: " + name.Trim(),
                });
            }

            points.Sort(RankedItem.Compare);
            foreach (var point in points)
            {
                items.Add(point.Text);
            }

            return items;
        }

        /// <summary>The display order of the section a question sits in; 0 when unknown.</summary>
        private static int SectionOrder(IOrganizationService service, EntityReference questionRef)
        {
            if (questionRef == null)
            {
                return 0;
            }

            var question = service.Retrieve("al_question", questionRef.Id, new ColumnSet("al_sectionid"));
            var sectionRef = question.GetAttributeValue<EntityReference>("al_sectionid");
            if (sectionRef == null)
            {
                return 0;
            }

            var section = service.Retrieve("al_section", sectionRef.Id, new ColumnSet("al_displayorder"));
            return section.GetAttributeValue<int?>("al_displayorder") ?? 0;
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
        /// <paramref name="adviserContact"/> may be null: the case carries the adviser as
        /// text (AD-029), so the name can match no contact or two.
        /// <see cref="AdviserContact"/> refuses to guess, and an unassigned action is far
        /// better than no action - the remediation is on the worklist for a manager to
        /// route, rather than lost to a directory gap.
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
            DateTime raisedOn)
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
                    raisedOn));
                return raised;
            }

            var index = 0;
            foreach (var item in items)
            {
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
                    raisedOn));
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
                    raisedOn));
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
            DateTime raisedOn)
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
        /// The contact behind the adviser named on the case, or null when the name matches
        /// no active contact or more than one.
        ///
        /// Two rows are read rather than one for the reason
        /// <see cref="NotificationOutbox.ParaplannerEmail"/> records: the second row is what
        /// proves the match was unambiguous, and a TopCount of 1 would return the first of
        /// two J Smiths and look certain. Remediation is work assigned to a named person -
        /// assigning it to the wrong adviser is worse than leaving it unassigned.
        /// </summary>
        public static EntityReference AdviserContact(IOrganizationService service, EntityReference caseRef)
        {
            if (caseRef == null)
            {
                return null;
            }

            var row = service.Retrieve("al_outcomecase", caseRef.Id, new ColumnSet("al_advisername"));
            var name = row.GetAttributeValue<string>("al_advisername");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("fullname", ConditionOperator.Equal, name.Trim());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var matches = service.RetrieveMultiple(query).Entities;
            return matches.Count == 1 ? matches[0].ToEntityReference() : null;
        }

        /// <summary>
        /// Assigns the case's open, unassigned remediation actions to the adviser now named
        /// on the case, and tells them (PP-15 "Remediation assigned"). Returns how many.
        ///
        /// <see cref="Raise"/> leaves an action unassigned when <c>al_advisername</c> matches
        /// no contact or two, and nothing could assign it afterwards: the portal's response
        /// panel opens only for the assigned contact, so such an action sat on the worklist
        /// with nobody able to answer it. Correcting the adviser's name on the case
        /// (al_UpdateCaseDetails) is the natural repair — the name was the problem — and this
        /// is what makes the correction reach the action. Only open actions with no assignee
        /// are touched: an action already assigned, or already completed, is not moved to a
        /// different person by a header edit.
        /// </summary>
        public static int AssignUnassignedActions(IOrganizationService service, EntityReference caseRef, Guid correlationId)
        {
            var adviser = AdviserContact(service, caseRef);
            if (adviser == null)
            {
                return 0;
            }

            var query = new QueryExpression(ActionEntity)
            {
                ColumnSet = new ColumnSet(false),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseRef.Id);
            query.Criteria.AddCondition("al_assignedcontactid", ConditionOperator.Null);
            query.Criteria.AddCondition("al_actionstatus", ConditionOperator.NotEqual, StatusCompleted);

            var assigned = 0;
            foreach (var action in CommandHelpers.RetrieveAll(service, query))
            {
                service.Update(new Entity(ActionEntity, action.Id)
                {
                    ["al_assignedcontactid"] = adviser,
                });

                NotificationEmitterPlugin.QueueRemediationAssigned(service, correlationId, action.Id);
                assigned++;
            }

            return assigned;
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
        /// The UK calendar date of a timestamp, at midnight.
        ///
        /// The time zone lookup is guarded rather than left to throw: a missing zone id
        /// would fail the whole submission over a date, and falling back to UTC is at worst
        /// a day out on a summer evening. This is not an OrganizationService call, so
        /// catching it does not put the transaction into the state OptionLabels warns about.
        /// </summary>
        private static DateTime UkDate(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

            try
            {
                var uk = TimeZoneInfo.FindSystemTimeZoneById(UkTimeZoneId);
                return TimeZoneInfo.ConvertTimeFromUtc(utc, uk).Date;
            }
            catch (TimeZoneNotFoundException)
            {
                return utc.Date;
            }
            catch (InvalidTimeZoneException)
            {
                return utc.Date;
            }
        }
    }
}
