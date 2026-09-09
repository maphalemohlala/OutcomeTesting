using System;
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
        /// What the adviser is told they have to put right.
        ///
        /// <paramref name="reason"/> is the grade or result that triggered this, as a label
        /// rather than an option number. <paramref name="observation"/> is the checker's own
        /// words from the fail observation question, which AD-019 leaves optional - so this
        /// has to read as a whole sentence without it.
        /// </summary>
        public static string Describe(string reason, string observation)
        {
            var text = "Raised automatically when the review was submitted"
                + (string.IsNullOrWhiteSpace(reason) ? string.Empty : " (" + reason.Trim() + ")")
                + ". Review the file and record what you have put right.";

            if (!string.IsNullOrWhiteSpace(observation))
            {
                text = "The checker recorded: " + observation.Trim() + Environment.NewLine + Environment.NewLine + text;
            }

            return text.Length <= DescriptionMaxLength ? text : text.Substring(0, DescriptionMaxLength);
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
        /// Raises the action, or returns the one already raised.
        ///
        /// An existing code is a reason to do nothing at all, not to write again. An upsert
        /// would put the status back to Open and overwrite <c>al_adviserresponse</c>, so a
        /// replay would silently undo work the adviser had already done and restart their
        /// ten days. The id of the existing row is returned so the caller can still report
        /// what the submission produced.
        ///
        /// <paramref name="adviserContact"/> may be null: the case carries the adviser as
        /// text (AD-029), so the name can match no contact or two.
        /// <see cref="AdviserContact"/> refuses to guess, and an unassigned action is far
        /// better than no action - the remediation is on the worklist for a manager to
        /// route, rather than lost to a directory gap.
        /// </summary>
        public static Guid Raise(
            IOrganizationService service,
            EntityReference caseRef,
            string caseReference,
            Guid reviewId,
            int sequence,
            string reason,
            string observation,
            EntityReference adviserContact,
            DateTime raisedOn)
        {
            var code = ActionCode(caseReference, sequence);

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
                ["al_description"] = Describe(reason, observation),
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
