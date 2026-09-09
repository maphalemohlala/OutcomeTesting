using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The al_SignOffRemediation command's own footprint. It used to reopen the action
    /// itself (without the OD-018 clock reset) and write a second Audit Event, on top of
    /// what SignoffProgressPlugin already does on the create it issues. The command now
    /// records the decision and nothing else; these pin that down.
    /// </summary>
    public class SignOffRemediationPluginTests
    {
        private static readonly Guid ActionId = Guid.Parse("55555555-eeee-4eee-8eee-555555555555");
        private static readonly Guid CaseId = Guid.Parse("66666666-ffff-4fff-8fff-666666666666");

        private const int StatusInProgress = 120910601;
        private const int StatusCompleted = 120910602;
        private const int Approved = 120910720;
        private const int Rejected = 120910721;

        private static FakeOrganizationService CompletedAction(string rowVersion = null)
        {
            var svc = new FakeOrganizationService();
            var action = svc.Seed(
                "al_remediationaction",
                ActionId,
                "al_actionstatus", new OptionSetValue(StatusCompleted),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            action.RowVersion = rowVersion;
            return svc;
        }

        [Fact]
        public void Creates_the_signoff_carrying_the_decision_action_case_and_notes()
        {
            var svc = CompletedAction();

            var id = SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Rejected", "Missing evidence.", null);

            var signoff = svc.Row("al_signoff", id);
            Assert.Equal(Rejected, signoff.GetAttributeValue<OptionSetValue>("al_signoffdecision").Value);
            Assert.Equal(ActionId, signoff.GetAttributeValue<EntityReference>("al_remediationactionid").Id);
            Assert.Equal(CaseId, signoff.GetAttributeValue<EntityReference>("al_outcomecaseid").Id);
            Assert.Equal("Missing evidence.", signoff.GetAttributeValue<string>("al_notes"));
        }

        [Fact]
        public void Does_not_write_the_action_itself()
        {
            // Reopening on a rejection belongs to SignoffProgressPlugin, which fires on the
            // create and also restarts the BR-010 clock. A second reopen here was the one
            // that did not.
            var svc = CompletedAction();

            SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Rejected", "Missing evidence.", null);

            Assert.Empty(svc.Updates);
            Assert.Equal(StatusCompleted, svc.Row("al_remediationaction", ActionId)
                .GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
        }

        [Fact]
        public void Writes_nothing_but_the_signoff()
        {
            var svc = CompletedAction();

            SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", null, null);

            Assert.Single(svc.Creates);
            Assert.Equal("al_signoff", svc.Creates.Single().LogicalName);
        }

        [Fact]
        public void Leaves_the_name_code_and_timestamp_to_the_guard()
        {
            // SignoffGuardPlugin stamps these on every create, portal or command, so the
            // browser and the command cannot choose a key or backdate a sign-off.
            var svc = CompletedAction();

            var id = SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", null, null);

            var signoff = svc.Row("al_signoff", id);
            Assert.False(signoff.Contains("al_signoffcode"));
            Assert.False(signoff.Contains("al_signedoffon"));
        }

        [Fact]
        public void A_rejection_without_notes_is_refused()
        {
            var svc = CompletedAction();

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Rejected", "  ", null));

            Assert.Contains("PRECONDITION:", ex.Message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Only_a_completed_action_can_be_signed_off()
        {
            var svc = CompletedAction();
            svc.Row("al_remediationaction", ActionId)["al_actionstatus"] = new OptionSetValue(StatusInProgress);

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", null, null));

            Assert.Contains("completed", ex.Message);
        }

        [Fact]
        public void A_second_signoff_on_the_same_action_is_refused()
        {
            var svc = CompletedAction();
            svc.Seed(
                "al_signoff",
                Guid.NewGuid(),
                "al_remediationactionid", new EntityReference("al_remediationaction", ActionId),
                "al_signoffdecision", new OptionSetValue(Approved),
                "statecode", new OptionSetValue(0));

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Rejected", "Again.", null));

            Assert.Contains("already been signed off", ex.Message);
        }

        [Fact]
        public void Refuses_when_the_action_moved_since_the_manager_loaded_it()
        {
            var svc = CompletedAction(rowVersion: "200");

            var ex = Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", null, "199"));

            Assert.Contains("CONFLICT:", ex.Message);
            Assert.Empty(svc.Creates);
        }

        [Fact]
        public void Accepts_the_version_the_manager_loaded()
        {
            var svc = CompletedAction(rowVersion: "200");

            SignOffRemediationPlugin.CreateSignoff(svc, svc, ActionId, "Approved", null, "200");

            Assert.Single(svc.Creates);
        }

        [Fact]
        public void A_replay_resolves_the_recorded_signoff_from_the_row_not_the_audit_text()
        {
            var svc = CompletedAction();
            var existing = Guid.NewGuid();
            svc.Seed(
                "al_signoff",
                existing,
                "al_remediationactionid", new EntityReference("al_remediationaction", ActionId),
                "al_signoffdecision", new OptionSetValue(Rejected),
                "statecode", new OptionSetValue(0));

            var found = SignOffRemediationPlugin.FindSignoff(svc, ActionId);

            Assert.Equal(existing, found.Id);
            Assert.Equal(Rejected, found.GetAttributeValue<OptionSetValue>("al_signoffdecision").Value);
        }

        [Theory]
        [InlineData("approved", "Approved")]
        [InlineData("REJECTED", "Rejected")]
        public void Normalises_the_decision_label(string raw, string expected)
        {
            Assert.Equal(expected, SignOffRemediationPlugin.NormaliseDecision(raw));
        }

        [Fact]
        public void Refuses_a_decision_that_is_neither()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => SignOffRemediationPlugin.NormaliseDecision("Maybe"));
        }
    }
}
