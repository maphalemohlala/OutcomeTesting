using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The adviser's earlier answers survive a rework (2026-10-08). A rejection reopens the
    /// action and the adviser writes over <c>al_adviserresponse</c>, so what the coach rejected
    /// was gone from the record. The rejection now keeps it in <c>al_responsehistory</c>
    /// before the action reopens.
    /// </summary>
    public class ResponseHistoryTests
    {
        private static readonly Guid CaseId = Guid.Parse("e1e1e1e1-0001-4001-8001-e1e1e1e1e1e1");
        private static readonly Guid ActionId = Guid.Parse("e2e2e2e2-0002-4002-8002-e2e2e2e2e2e2");

        private static FakeOrganizationService Completed(string response, DateTime completedOn)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "HIS-0001",
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingSignoff));
            svc.Seed("al_remediationaction", ActionId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted),
                "al_adviserresponse", response,
                RemedialActions.ActionPerformedAttr, new OptionSetValue(RemedialActions.ActionPerformedYes),
                "al_completedon", completedOn,
                "statecode", new OptionSetValue(0));
            return svc;
        }

        private static void Reject(FakeOrganizationService svc)
        {
            var signoff = new Entity("al_signoff")
            {
                Id = Guid.NewGuid(),
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", ActionId),
                ["al_outcomecaseid"] = new EntityReference("al_outcomecase", CaseId),
                ["al_signoffdecision"] = new OptionSetValue(SignoffProgressPlugin.DecisionRejectedValue),
                ["al_notes"] = "Add the declaration.",
                ["statecode"] = new OptionSetValue(0),
            };
            svc.Seed("al_signoff", signoff.Id,
                "al_remediationactionid", signoff["al_remediationactionid"],
                "al_outcomecaseid", signoff["al_outcomecaseid"],
                "al_signoffdecision", signoff["al_signoffdecision"],
                "al_notes", "Add the declaration.",
                "createdon", DateTime.UtcNow,
                "statecode", signoff["statecode"]);

            SignoffProgressPlugin.Progress(svc, new FakePluginExecutionContext(), signoff);
        }

        private static Entity Action(FakeOrganizationService svc)
        {
            return svc.Retrieve("al_remediationaction", ActionId,
                new ColumnSet("al_adviserresponse", RemedialActions.ResponseHistoryAttr, "al_actionstatus"));
        }

        [Fact]
        public void A_rejection_keeps_the_answer_it_rejected()
        {
            var svc = Completed("Re-verified the client's ID.", new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));

            Reject(svc);

            var action = Action(svc);
            Assert.Equal(
                "06 Oct 2026 - Action performed: Yes\nRe-verified the client's ID.",
                action.GetAttributeValue<string>(RemedialActions.ResponseHistoryAttr));

            // Left in place for the adviser to build on, as it always was.
            Assert.Equal("Re-verified the client's ID.", action.GetAttributeValue<string>("al_adviserresponse"));
            Assert.Equal(Remediation.StatusInProgress, action.GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
        }

        [Fact]
        public void A_second_rejection_adds_to_the_history_rather_than_replacing_it()
        {
            var svc = Completed("First try.", new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));
            Reject(svc);

            svc.Update(new Entity("al_remediationaction", ActionId)
            {
                ["al_actionstatus"] = new OptionSetValue(Remediation.StatusCompleted),
                ["al_adviserresponse"] = "Second try.",
                ["al_completedon"] = new DateTime(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc),
            });
            svc.Update(new Entity("al_outcomecase", CaseId)
            {
                ["al_casestatus"] = new OptionSetValue(CaseLifecycle.AwaitingSignoff),
            });
            Reject(svc);

            Assert.Equal(
                "06 Oct 2026 - Action performed: Yes\nFirst try.\n\n07 Oct 2026 - Action performed: Yes\nSecond try.",
                Action(svc).GetAttributeValue<string>(RemedialActions.ResponseHistoryAttr));
        }

        [Fact]
        public void An_approval_leaves_the_history_alone()
        {
            var svc = Completed("Done.", new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc));

            var signoff = new Entity("al_signoff")
            {
                Id = Guid.NewGuid(),
                ["al_remediationactionid"] = new EntityReference("al_remediationaction", ActionId),
                ["al_signoffdecision"] = new OptionSetValue(SignoffProgressPlugin.DecisionApprovedValue),
            };
            svc.Seed("al_signoff", signoff.Id,
                "al_remediationactionid", signoff["al_remediationactionid"],
                "al_signoffdecision", signoff["al_signoffdecision"]);
            SignoffProgressPlugin.Progress(svc, new FakePluginExecutionContext(), signoff);

            Assert.Null(Action(svc).GetAttributeValue<string>(RemedialActions.ResponseHistoryAttr));
        }

        /// <summary>
        /// Only the reopen a rejection makes may write the history: the stored action is
        /// Completed, the write sets it back In progress and only adds to what is there, and
        /// a rejection has just been recorded. Decided from state, because nothing in the
        /// pipeline reliably says a write came from the sign-off (see the guard's own notes).
        /// </summary>
        [Fact]
        public void Only_the_reopen_a_rejection_makes_may_write_the_history()
        {
            var stored = new Entity("al_remediationaction", ActionId)
            {
                ["al_actionstatus"] = new OptionSetValue(Remediation.StatusCompleted),
                [RemedialActions.ResponseHistoryAttr] = "06 Oct 2026\nFirst try.",
            };

            Entity Write(string history, int status = Remediation.StatusInProgress) => new Entity("al_remediationaction", ActionId)
            {
                ["al_actionstatus"] = new OptionSetValue(status),
                [RemedialActions.ResponseHistoryAttr] = history,
            };

            const string Appended = "06 Oct 2026\nFirst try.\n\n07 Oct 2026\nSecond try.";

            Assert.Null(RemediationResponseGuardPlugin.HistoryRefusal(Write(Appended), stored, rejectionJustRecorded: true));

            // No rejection behind it.
            Assert.StartsWith("PRECONDITION:", RemediationResponseGuardPlugin.HistoryRefusal(Write(Appended), stored, false));

            // Rewrites what was kept.
            Assert.StartsWith("PRECONDITION:", RemediationResponseGuardPlugin.HistoryRefusal(
                Write("Something the adviser never wrote."), stored, true));

            // Not the reopen.
            Assert.StartsWith("PRECONDITION:", RemediationResponseGuardPlugin.HistoryRefusal(
                Write(Appended, Remediation.StatusCompleted), stored, true));

            // A write that leaves the history alone is not this rule's business.
            Assert.Null(RemediationResponseGuardPlugin.HistoryRefusal(
                new Entity("al_remediationaction", ActionId) { ["al_adviserresponse"] = "x" }, stored, false));
        }

        [Fact]
        public void An_answer_with_nothing_in_it_adds_nothing()
        {
            Assert.Equal("kept", RemedialActions.WithEarlierResponse("kept", null, null, "  "));
        }
    }
}
