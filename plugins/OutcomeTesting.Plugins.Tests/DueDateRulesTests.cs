using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The due date (item 6, 2026-09-19): defaulted 72 hours after the upload, editable, and
    /// editable by managers only.
    ///
    /// Three rules that live in three places, pinned together here because they only make
    /// sense as one answer: ImportRules decides what the deadline starts as,
    /// UpdateCaseDetailsPlugin decides who may move it, and CaseHeaderRules decides where it
    /// may be moved to.
    /// </summary>
    public class DueDateRulesTests
    {
        // ------------------------------------------------------------------ the default

        [Fact]
        public void Falls_due_seventy_two_hours_after_the_upload()
        {
            var uploaded = new DateTime(2026, 9, 18, 16, 0, 0, DateTimeKind.Utc);

            Assert.Equal(
                new DateTime(2026, 9, 21, 16, 0, 0, DateTimeKind.Utc),
                ImportRules.DueDateFor(uploaded));
        }

        [Fact]
        public void Counts_calendar_time_and_not_working_days()
        {
            // "3 days" (project owner, 2026-09-19), so a Friday upload is due on Monday and a
            // Thursday upload is due on Sunday - not the following Tuesday. Pinned because
            // the business asked to be told which this was: it is the calendar, and
            // remediation's own deadline is the one that counts working days.
            var thursday = new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc);
            var due = ImportRules.DueDateFor(thursday);

            Assert.Equal(DayOfWeek.Sunday, due.DayOfWeek);
            Assert.Equal(new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), due);
        }

        [Fact]
        public void Keeps_the_time_of_day_the_upload_happened_at()
        {
            // 72 hours, not "three days at midnight". A file uploaded at 4pm is due at 4pm,
            // which is what gives the checker the whole of the third day.
            var uploaded = new DateTime(2026, 9, 18, 16, 30, 0, DateTimeKind.Utc);

            Assert.Equal(new TimeSpan(16, 30, 0), ImportRules.DueDateFor(uploaded).TimeOfDay);
        }

        // ------------------------------------------------------------------ who may move it

        [Fact]
        public void A_payload_naming_the_due_date_needs_the_managers_grant()
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "al_duedate", "2026-09-25" },
            };

            Assert.True(UpdateCaseDetailsPlugin.TouchesDueDate(fields));
        }

        [Fact]
        public void A_payload_that_leaves_the_due_date_alone_does_not()
        {
            // The whole point of a separate key: a Tax or AQS reviewer completing the header
            // fields the extract does not carry must not be stopped, and they are not.
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "al_advicedate", "2026-09-10" },
                { "al_clientname", "A Client" },
            };

            Assert.False(UpdateCaseDetailsPlugin.TouchesDueDate(fields));
        }

        [Fact]
        public void Clearing_the_due_date_still_needs_the_grant()
        {
            // An empty value is a write, not an absence of one.
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "al_duedate", string.Empty },
            };

            Assert.True(UpdateCaseDetailsPlugin.TouchesDueDate(fields));
        }

        [Fact]
        public void An_empty_payload_touches_nothing()
        {
            Assert.False(UpdateCaseDetailsPlugin.TouchesDueDate(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
            Assert.False(UpdateCaseDetailsPlugin.TouchesDueDate(null));
        }

        [Fact]
        public void The_due_date_is_an_editable_field()
        {
            // It was deliberately absent until 2026-09-19, when the later of the day's two
            // instructions narrowed "nobody may edit it" to "only managers may". Until then
            // the app's own Due date box produced "Field 'al_duedate' cannot be edited." and
            // failed the whole save, taking every other field on the form with it.
            var changes = Apply("2026-09-25", null);

            Assert.Equal(new[] { "Due date (none) -> 2026-09-25" }, changes);
        }

        [Fact]
        public void A_due_date_moved_back_before_the_recorded_meeting_is_refused()
        {
            // The rule reaches the save even though the meeting is not in the payload: the
            // before-image is what the case will still hold afterwards.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => Apply("2026-09-05", new DateTime(2026, 9, 10)));

            Assert.Contains("cannot be earlier than Date of meeting", error.Message);
        }

        [Fact]
        public void Clearing_the_due_date_contradicts_nothing()
        {
            // No deadline is not an early deadline, and item 8 already treats an absent due
            // date as nothing to compare against.
            Assert.Equal(
                new[] { "Due date 2026-09-25 -> (none)" },
                Apply(string.Empty, new DateTime(2026, 9, 10), new DateTime(2026, 9, 25)));
        }

        /// <summary>
        /// Runs one due-date edit through the real coercion path - the same method both
        /// front ends call - and hands back the audit lines it produced.
        /// </summary>
        private static List<string> Apply(string dueDate, DateTime? meetingOnRecord, DateTime? dueOnRecord = null)
        {
            var before = new Entity("al_outcomecase", Guid.NewGuid());
            if (meetingOnRecord.HasValue)
            {
                before["al_advicedate"] = meetingOnRecord.Value;
            }

            if (dueOnRecord.HasValue)
            {
                before["al_duedate"] = dueOnRecord.Value;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "al_duedate", dueDate },
            };

            var changes = new List<string>();
            UpdateCaseDetailsPlugin.ApplyFields(
                fields, before, new Entity("al_outcomecase", before.Id), changes,
                new OptionLabels(new FakeOrganizationService()));

            return changes;
        }

        // ------------------------------------------------------------------ where it may move to

        [Fact]
        public void A_deadline_after_the_meeting_is_fine()
        {
            Assert.Null(CaseHeaderRules.ValidateDueDate(
                new DateTime(2026, 9, 25), new DateTime(2026, 9, 10)));
        }

        [Fact]
        public void A_deadline_on_the_day_of_the_meeting_is_fine()
        {
            // The boundary item 8 already draws from the other end: same day passes.
            Assert.Null(CaseHeaderRules.ValidateDueDate(
                new DateTime(2026, 9, 10), new DateTime(2026, 9, 10)));
        }

        [Fact]
        public void A_deadline_moved_back_before_the_meeting_is_refused_by_name()
        {
            // Item 8's rule, broken by moving the deadline instead of the meeting. The
            // message names the rule that failed and the value it failed against.
            var message = CaseHeaderRules.ValidateDueDate(
                new DateTime(2026, 9, 5), new DateTime(2026, 9, 10));

            Assert.Equal(
                "Due date cannot be earlier than Date of meeting - Client contact (10 Sep 2026).",
                message);
        }

        [Fact]
        public void Ignores_a_time_of_day_on_the_due_date()
        {
            // al_duedate carries the upload's time of day, so a meeting on the due day must
            // not read as late because the deadline falls at 09:00 and the date at midnight.
            Assert.Null(CaseHeaderRules.ValidateDueDate(
                new DateTime(2026, 9, 10, 9, 0, 0), new DateTime(2026, 9, 10)));
        }

        [Fact]
        public void Says_nothing_about_a_meeting_in_the_future()
        {
            // ValidateAdviceDate would refuse this, and it should - from the other end.
            // Reporting it to somebody editing the DUE DATE would send them to a field they
            // have not touched.
            Assert.Null(CaseHeaderRules.ValidateDueDate(
                new DateTime(2099, 1, 1), new DateTime(2098, 1, 1)));
        }

        [Fact]
        public void Either_date_absent_passes()
        {
            Assert.Null(CaseHeaderRules.ValidateDueDate(null, new DateTime(2026, 9, 10)));
            Assert.Null(CaseHeaderRules.ValidateDueDate(new DateTime(2026, 9, 10), null));
            Assert.Null(CaseHeaderRules.ValidateDueDate(null, null));
        }
    }
}
