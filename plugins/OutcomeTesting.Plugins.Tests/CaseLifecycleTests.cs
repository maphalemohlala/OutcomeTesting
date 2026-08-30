using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The canonical lifecycle in knowledge/project-context.md, encoded as tests. Every
    /// case here names the step it comes from; a transition nothing in the requirements
    /// describes is refused rather than guessed at.
    /// </summary>
    public class CaseLifecycleTests
    {
        [Theory]
        // Imported -> Validation Failed | Ready for Allocation
        [InlineData(CaseLifecycle.Imported, CaseLifecycle.ValidationFailed)]
        [InlineData(CaseLifecycle.Imported, CaseLifecycle.ReadyForAllocation)]
        // -> Queued -> Assigned -> Review In Progress -> Submitted
        [InlineData(CaseLifecycle.ReadyForAllocation, CaseLifecycle.Queued)]
        [InlineData(CaseLifecycle.Queued, CaseLifecycle.Assigned)]
        [InlineData(CaseLifecycle.Assigned, CaseLifecycle.ReviewInProgress)]
        [InlineData(CaseLifecycle.ReviewInProgress, CaseLifecycle.Submitted)]
        // Submitted -> Awaiting Remediation | Closed
        [InlineData(CaseLifecycle.Submitted, CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.Submitted, CaseLifecycle.Closed)]
        // -> Remediation In Progress -> Awaiting Sign-off -> Awaiting Recheck -> Closed
        [InlineData(CaseLifecycle.AwaitingRemediation, CaseLifecycle.RemediationInProgress)]
        [InlineData(CaseLifecycle.RemediationInProgress, CaseLifecycle.AwaitingSignoff)]
        [InlineData(CaseLifecycle.AwaitingSignoff, CaseLifecycle.AwaitingRecheck)]
        [InlineData(CaseLifecycle.AwaitingRecheck, CaseLifecycle.Closed)]
        public void Allows_the_canonical_forward_path(int from, int to)
        {
            Assert.True(CaseLifecycle.IsAllowed(from, to));
        }

        [Fact]
        public void Allows_a_rejected_signoff_to_return_the_case_to_remediation()
        {
            // "Rejected remediation returns with notes" — project-context step 8, BR-008.
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.AwaitingSignoff, CaseLifecycle.AwaitingRemediation));
        }

        [Fact]
        public void Allows_a_corrected_validation_failure_back_into_allocation()
        {
            // BR-002: an invalid case is returned with a reason; correcting it puts it back
            // in the queue, and where work must restart it is closed and resubmitted anew.
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.ValidationFailed, CaseLifecycle.ReadyForAllocation));
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.ValidationFailed, CaseLifecycle.Closed));
        }

        [Theory]
        [InlineData(CaseLifecycle.Imported)]
        [InlineData(CaseLifecycle.ValidationFailed)]
        [InlineData(CaseLifecycle.ReadyForAllocation)]
        [InlineData(CaseLifecycle.Queued)]
        [InlineData(CaseLifecycle.Assigned)]
        [InlineData(CaseLifecycle.ReviewInProgress)]
        public void Allows_the_bypass_from_any_state_before_a_grade_exists(int from)
        {
            // AD-036: No Check Required is for cases that must not receive a grading outcome.
            Assert.True(CaseLifecycle.IsAllowed(from, CaseLifecycle.NoCheckRequired));
        }

        [Theory]
        [InlineData(CaseLifecycle.Submitted)]
        [InlineData(CaseLifecycle.AwaitingRemediation)]
        [InlineData(CaseLifecycle.AwaitingSignoff)]
        public void Refuses_the_bypass_once_the_case_has_been_reviewed(int from)
        {
            // The bypass exists for cases that must not be graded. Past Submitted one exists,
            // and hiding it behind a bypass would drop it from MI without an audited override.
            Assert.False(CaseLifecycle.IsAllowed(from, CaseLifecycle.NoCheckRequired));
        }

        [Theory]
        [InlineData(CaseLifecycle.Closed)]
        [InlineData(CaseLifecycle.NoCheckRequired)]
        public void Treats_terminal_states_as_terminal(int from)
        {
            // Reopening a closed case is a privileged correction with a mandatory reason
            // (AD-031), owned by al_RegradeCase — not an ordinary details edit.
            Assert.False(CaseLifecycle.IsAllowed(from, CaseLifecycle.ReviewInProgress));
            Assert.False(CaseLifecycle.IsAllowed(from, CaseLifecycle.Queued));
            Assert.False(CaseLifecycle.IsAllowed(from, CaseLifecycle.Submitted));
        }

        [Fact]
        public void Refuses_the_jump_that_would_export_an_ungraded_case()
        {
            // The export filters on Closed. Imported -> Closed would deliver a case with no
            // review instance and a blank grade.
            Assert.False(CaseLifecycle.IsAllowed(CaseLifecycle.Imported, CaseLifecycle.Closed));
        }

        [Theory]
        [InlineData(CaseLifecycle.Queued, CaseLifecycle.Submitted)]
        [InlineData(CaseLifecycle.ReadyForAllocation, CaseLifecycle.ReviewInProgress)]
        [InlineData(CaseLifecycle.Assigned, CaseLifecycle.AwaitingSignoff)]
        [InlineData(CaseLifecycle.Submitted, CaseLifecycle.AwaitingRecheck)]
        public void Refuses_skipping_a_step(int from, int to)
        {
            Assert.False(CaseLifecycle.IsAllowed(from, to));
        }

        [Theory]
        [InlineData(CaseLifecycle.Submitted, CaseLifecycle.ReviewInProgress)]
        [InlineData(CaseLifecycle.Assigned, CaseLifecycle.Queued)]
        [InlineData(CaseLifecycle.AwaitingRecheck, CaseLifecycle.AwaitingRemediation)]
        public void Refuses_walking_the_lifecycle_backwards(int from, int to)
        {
            Assert.False(CaseLifecycle.IsAllowed(from, to));
        }

        [Fact]
        public void Allows_a_status_that_is_not_changing()
        {
            // An edit that resubmits the current status alongside other fields is not a
            // transition and must not be refused as one.
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.Closed, CaseLifecycle.Closed));
            Assert.True(CaseLifecycle.IsAllowed(CaseLifecycle.Queued, CaseLifecycle.Queued));
        }

        [Fact]
        public void Refuses_a_status_value_that_is_not_in_the_model()
        {
            Assert.False(CaseLifecycle.IsAllowed(CaseLifecycle.Queued, 999));
            Assert.False(CaseLifecycle.IsAllowed(999, CaseLifecycle.Queued));
        }

        [Fact]
        public void Allows_any_first_status_when_the_case_has_none_recorded()
        {
            // A row with no status yet is not mid-lifecycle; refusing here would make an
            // unstatused case uneditable rather than correctable.
            Assert.True(CaseLifecycle.IsAllowed(null, CaseLifecycle.ReadyForAllocation));
            Assert.True(CaseLifecycle.IsAllowed(null, CaseLifecycle.Closed));
        }

        [Fact]
        public void Names_both_states_in_its_refusal_message()
        {
            var message = CaseLifecycle.DescribeRefusal(CaseLifecycle.Imported, CaseLifecycle.Closed);

            Assert.Contains("Imported", message);
            Assert.Contains("Closed", message);
        }

        [Fact]
        public void Lists_what_is_reachable_so_the_message_can_say_what_to_do_instead()
        {
            var message = CaseLifecycle.DescribeRefusal(CaseLifecycle.Queued, CaseLifecycle.Closed);

            Assert.Contains("Assigned", message);
        }
    }
}
