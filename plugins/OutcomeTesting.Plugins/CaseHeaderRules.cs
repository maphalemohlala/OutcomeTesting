using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Pure decision logic for the dates on a case header. Deliberately free of Dataverse
    /// types so it can be unit-tested without a fake organisation service; the plug-ins that
    /// let a header be edited are thin wiring over this. Mirrors ResponseRules and
    /// OutcomeRules, and is mirrored client-side in app/src/features/cases/caseHeaderDates.ts.
    ///
    /// Two plug-ins write this header - UpdateCaseDetailsPlugin for a manager in the Code
    /// App, CaseHeaderRequestPlugin for a checker on the portal - and a rule that lived in
    /// one of them would hold on one surface only.
    /// </summary>
    public static class CaseHeaderRules
    {
        /// <summary>
        /// The label the business gives al_advicedate (item 9, 2026-09-19). The schema name
        /// is unchanged, so nothing that reads the column breaks; this is the wording every
        /// message about it uses, and it is held here so the two plug-ins cannot word the
        /// same refusal differently.
        /// </summary>
        public const string AdviceDateLabel = "Date of meeting - Client contact";

        /// <summary>
        /// The UK time zone the business day runs in (OD-018).
        ///
        /// Dataverse hands back UTC, and 23:30 UTC on a British Summer Time evening is
        /// already the next UK day. A checker in London entering today's date at that hour
        /// would be told their own today is in the future if the comparison were made
        /// against the UTC date.
        /// </summary>
        public const string UkTimeZoneId = "GMT Standard Time";

        /// <summary>
        /// The UK calendar date of a timestamp, at midnight.
        ///
        /// The time zone lookup is guarded rather than left to throw: a missing zone id
        /// would fail a whole command over a date, and falling back to UTC is at worst a day
        /// out on a summer evening. This is not an OrganizationService call, so catching it
        /// does not put the transaction into the state OptionLabels warns about.
        /// </summary>
        public static DateTime UkDate(DateTime value)
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

        /// <summary>
        /// Whether the date of meeting is one the case may carry, or null when it is.
        /// Returns a message with no failure prefix - the caller adds VALIDATION:.
        ///
        /// A meeting that has not happened yet cannot have been checked, so the date can
        /// never be in the future (item 9, 2026-09-19).
        ///
        /// Compared on the DATE and not the timestamp. al_advicedate is a DateOnly column
        /// (Behavior 2), so the value carries no time of day to compare and the only clock
        /// in the comparison is the one that decides what today is - which is the UK's, not
        /// UTC's. Today itself is allowed: a meeting held this morning is checked this
        /// afternoon often enough that refusing it would be refusing the ordinary case.
        ///
        /// An absent date is valid. Clearing the field is a legitimate edit, and it is the
        /// column's own required level - not this rule - that decides whether it may be left
        /// empty.
        /// </summary>
        public static string ValidateAdviceDate(DateTime? adviceDate, DateTime utcNow)
        {
            if (!adviceDate.HasValue)
            {
                return null;
            }

            return adviceDate.Value.Date > UkDate(utcNow)
                ? AdviceDateLabel + " cannot be in the future."
                : null;
        }
    }
}
