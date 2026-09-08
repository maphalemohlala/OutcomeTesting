using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Raising the remediation action a non-pass outcome or a flagged checklist demands
    /// (BR-006, BR-010).
    ///
    /// Until this existed nothing anywhere created an al_remediationaction: a case reached
    /// Awaiting Remediation and the whole downstream loop - the adviser's response, the
    /// completion, the T and C sign-off, the ageing clock - sat waiting on rows that could
    /// not be created. The rules are kept pure here for the reason OutcomeRules records:
    /// two callers writing the same state is two places for it to be enforced differently.
    /// </summary>
    public class RemediationTests
    {
        // 2026-09-07 is a Monday; 2026-09-12 a Saturday.
        private static readonly DateTime Monday = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Saturday = new DateTime(2026, 9, 12, 9, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Counts_the_starting_day_as_day_one()
        {
            // OD-018: the day a case enters remediation is day 1, not day 0. Ten working
            // days from a Monday is therefore the Friday of the following week, not the
            // Monday after - the same arithmetic app/src/lib/workingDays.ts performs, so
            // the date this writes and the age the portal renders agree.
            Assert.Equal(
                new DateTime(2026, 9, 18),
                Remediation.AddWorkingDays(Monday, 10).Date);
        }

        [Fact]
        public void Skips_the_weekend()
        {
            // Monday + 5 working days is the Friday of the same week.
            Assert.Equal(new DateTime(2026, 9, 11), Remediation.AddWorkingDays(Monday, 5).Date);
        }

        [Fact]
        public void Never_falls_due_on_a_weekend()
        {
            var due = Remediation.AddWorkingDays(Saturday, 10);
            Assert.NotEqual(DayOfWeek.Saturday, due.DayOfWeek);
            Assert.NotEqual(DayOfWeek.Sunday, due.DayOfWeek);
        }

        [Fact]
        public void Starts_a_weekend_case_on_the_monday()
        {
            // A Saturday is not a working day, so the count starts on the Monday and ten
            // working days lands on the Friday of the week after it.
            Assert.Equal(
                new DateTime(2026, 9, 25),
                Remediation.AddWorkingDays(Saturday, 10).Date);
        }

        [Fact]
        public void Derives_a_code_that_replays_onto_the_same_row()
        {
            // The al_remediationactioncode alternate key is what stops a replayed submit
            // raising a second action against the same review.
            Assert.Equal("REM-IO-001-2", Remediation.ActionCode("IO-001", 2));
            Assert.Equal(
                Remediation.ActionCode("IO-001", 2),
                Remediation.ActionCode("IO-001", 2));
        }

        [Fact]
        public void Keeps_the_code_inside_the_column()
        {
            // al_remediationactioncode is nvarchar(100). A long case reference must not
            // produce a code the platform refuses on write.
            var code = Remediation.ActionCode(new string('X', 200), 1);
            Assert.True(code.Length <= 100, "code was " + code.Length + " characters");
        }

        [Fact]
        public void Names_the_grade_that_caused_it()
        {
            var description = Remediation.Describe("Potential harm", null);
            Assert.Contains("Potential harm", description);
        }

        [Fact]
        public void Carries_the_checkers_observation_into_the_description()
        {
            // The adviser is being asked to put something right, so what the checker
            // actually wrote is the useful half. AD-019 leaves the observation optional,
            // which is why the description has to read sensibly without it.
            var description = Remediation.Describe("Fail", "Attitude to risk was not evidenced.");
            Assert.Contains("Attitude to risk was not evidenced.", description);
        }

        [Fact]
        public void Describes_a_flagged_pass_without_an_observation()
        {
            var description = Remediation.Describe("Pass", null);
            Assert.False(string.IsNullOrWhiteSpace(description));
        }

        [Fact]
        public void Keeps_the_description_inside_the_column()
        {
            // al_description is nvarchar(2000) and required. A checker who writes a very
            // long observation must not make the review impossible to submit.
            var description = Remediation.Describe("Fail", new string('y', 4000));
            Assert.True(description.Length <= 2000, "description was " + description.Length + " characters");
        }

        [Fact]
        public void Raises_one_open_action_against_the_case_and_the_review()
        {
            var service = new FakeOrganizationService();
            var caseId = Guid.NewGuid();
            var reviewId = Guid.NewGuid();

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", caseId),
                "IO-001",
                reviewId,
                1,
                "Pass with issues",
                "Charges were not evidenced.",
                null,
                Monday);

            var created = Assert.Single(service.Creates);
            Assert.Equal("al_remediationaction", created.LogicalName);
            Assert.Equal("REM-IO-001-1", created.GetAttributeValue<string>("al_remediationactioncode"));
            Assert.Equal(caseId, created.GetAttributeValue<EntityReference>("al_outcomecaseid").Id);
            Assert.Equal(reviewId, created.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id);
            Assert.Equal(Remediation.StatusOpen, created.GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
            Assert.Equal(
                new DateTime(2026, 9, 18),
                created.GetAttributeValue<DateTime>("al_duedate").Date);
            Assert.Contains("Charges were not evidenced.", created.GetAttributeValue<string>("al_description"));
        }

        [Fact]
        public void Assigns_the_action_to_the_adviser_when_one_was_resolved()
        {
            var service = new FakeOrganizationService();
            var adviser = new EntityReference("contact", Guid.NewGuid());

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-002",
                Guid.NewGuid(),
                1,
                "Fail",
                null,
                adviser,
                Monday);

            var created = Assert.Single(service.Creates);
            Assert.Equal(adviser.Id, created.GetAttributeValue<EntityReference>("al_assignedcontactid").Id);
        }

        [Fact]
        public void Raises_an_unassigned_action_rather_than_none_when_the_adviser_is_unknown()
        {
            // The case carries the adviser as text (AD-029), so the name may match no
            // contact or two. Refusing to raise the action would lose the remediation
            // entirely over a directory gap; raising it unassigned keeps the work visible
            // on the remediation worklist for a manager to route.
            var service = new FakeOrganizationService();

            Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-003",
                Guid.NewGuid(),
                1,
                "Insufficient evidence",
                null,
                null,
                Monday);

            var created = Assert.Single(service.Creates);
            Assert.False(created.Contains("al_assignedcontactid"));
        }

        [Fact]
        public void Leaves_an_action_that_already_exists_alone()
        {
            // A replay must not reopen an action the adviser has already worked on. An
            // upsert would overwrite the status and wipe the response, so an existing code
            // is a reason to do nothing at all rather than to write again.
            var service = new FakeOrganizationService();
            var existing = Guid.NewGuid();
            service.Seed(
                "al_remediationaction", existing,
                "al_remediationactioncode", "REM-IO-004-1",
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_adviserresponse", "Already put right.");

            var raised = Remediation.Raise(
                service,
                new EntityReference("al_outcomecase", Guid.NewGuid()),
                "IO-004",
                Guid.NewGuid(),
                1,
                "Fail",
                null,
                null,
                Monday);

            Assert.Equal(existing, raised);
            Assert.Empty(service.Creates);
            Assert.Empty(service.Updates);
            Assert.Equal(
                Remediation.StatusCompleted,
                service.Row("al_remediationaction", existing).GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
        }
    }
}
