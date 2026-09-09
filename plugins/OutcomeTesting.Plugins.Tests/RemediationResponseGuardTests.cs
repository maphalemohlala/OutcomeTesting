using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The adviser's response is theirs to write until it is submitted (project owner
    /// direction, 2026-09-09). Once the action is Completed it is what the T&amp;C Manager
    /// attests to (BR-008), so it locks; a rejection reopens it.
    /// </summary>
    public class RemediationResponseGuardTests
    {
        private static readonly Guid ActionId = Guid.Parse("abababab-1212-4121-8121-abababababab");

        private static Entity Action(int status, string response = "Rebuilt the report.", string evidence = "IO-123")
        {
            return new Entity("al_remediationaction", ActionId)
            {
                ["al_actionstatus"] = new OptionSetValue(status),
                ["al_adviserresponse"] = response,
                ["al_evidencereference"] = evidence,
            };
        }

        private static Entity Write(string response = null, string evidence = null)
        {
            var update = new Entity("al_remediationaction", ActionId);
            if (response != null) update["al_adviserresponse"] = response;
            if (evidence != null) update["al_evidencereference"] = evidence;
            return update;
        }

        [Theory]
        [InlineData(Remediation.StatusOpen)]
        [InlineData(Remediation.StatusInProgress)]
        public void The_response_is_editable_until_it_is_submitted(int status)
        {
            Assert.Null(RemediationResponseGuardPlugin.Refusal(Action(status), Write(response: "Changed my mind.")));
        }

        [Fact]
        public void A_reopened_action_is_editable_again()
        {
            // A rejected sign-off sets the action back to In progress; the adviser reworks it.
            Assert.Null(RemediationResponseGuardPlugin.Refusal(Action(Remediation.StatusInProgress), Write(evidence: "IO-456")));
        }

        [Fact]
        public void A_submitted_response_cannot_be_changed()
        {
            var refusal = RemediationResponseGuardPlugin.Refusal(Action(Remediation.StatusCompleted), Write(response: "Actually..."));

            Assert.NotNull(refusal);
            Assert.StartsWith("CONFLICT:", refusal);
        }

        [Fact]
        public void A_submitted_evidence_reference_cannot_be_changed_either()
        {
            Assert.NotNull(RemediationResponseGuardPlugin.Refusal(Action(Remediation.StatusCompleted), Write(evidence: "IO-999")));
        }

        [Fact]
        public void Restating_the_submitted_response_is_allowed()
        {
            // The portal's "Save and mark complete" retried after a dropped response sends the
            // same response again with the trigger column; a replay must not become an error.
            Assert.Null(RemediationResponseGuardPlugin.Refusal(
                Action(Remediation.StatusCompleted),
                Write(response: "Rebuilt the report. ", evidence: "IO-123")));
        }

        [Fact]
        public void A_write_carrying_no_response_column_is_not_the_guards_business()
        {
            var update = new Entity("al_remediationaction", ActionId) { ["al_actionstatus"] = new OptionSetValue(Remediation.StatusInProgress) };

            Assert.Null(RemediationResponseGuardPlugin.Refusal(Action(Remediation.StatusCompleted), update));
        }

        private static Entity ActionWithAnswers(int status)
        {
            var action = Action(status);
            action["al_clientcontactrequired"] = new OptionSetValue(120910794);
            action["al_recheckrequired"] = new OptionSetValue(120910797);
            action["al_changesadvice"] = new OptionSetValue(120910799);
            return action;
        }

        [Fact]
        public void The_form_answers_are_editable_until_the_action_is_submitted()
        {
            var update = new Entity("al_remediationaction", ActionId);
            update["al_clientcontactrequired"] = new OptionSetValue(120910795);

            Assert.Null(RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusInProgress), update));
        }

        [Fact]
        public void A_submitted_form_answer_cannot_be_changed()
        {
            // AD-095: the three answers travel with the response and lock with it, otherwise
            // the T&C Manager attests to a form the adviser can still rewrite underneath.
            var update = new Entity("al_remediationaction", ActionId);
            update["al_recheckrequired"] = new OptionSetValue(120910796);

            var refusal = RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusCompleted), update);

            Assert.NotNull(refusal);
            Assert.StartsWith("CONFLICT:", refusal);
        }

        [Fact]
        public void Re_sending_the_same_answer_after_submission_is_not_a_change()
        {
            var update = new Entity("al_remediationaction", ActionId);
            update["al_changesadvice"] = new OptionSetValue(120910799);

            Assert.Null(RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusCompleted), update));
        }

        [Fact]
        public void Clearing_a_submitted_answer_is_a_change()
        {
            var update = new Entity("al_remediationaction", ActionId);
            update["al_clientcontactrequired"] = null;

            Assert.NotNull(RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusCompleted), update));
        }
    }
}
