using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// When the adviser finishes the last action, the case's T&amp;C Manager is told a
    /// sign-off is waiting (Fixes 5, AD-162).
    ///
    /// <para>
    /// Nothing was sent here at all until 2026-09-20. The adviser was told when their
    /// remediation was approved or sent back, but the person who had to do the approving was
    /// never told there was anything to approve - the case reached Awaiting Sign-off and
    /// waited for somebody to notice it.
    /// </para>
    /// <para>
    /// The two tests that matter most are the ones where nothing is sent: an unmapped adviser
    /// and a broken lookup must both cost a notification and nothing else. The adviser has
    /// just finished their work, and failing their completion over a missing configuration
    /// row would punish the wrong person for the wrong thing.
    /// </para>
    /// </summary>
    public class SignoffDueNotificationTests
    {
        private static readonly Guid ActionId = Guid.Parse("a1110000-1111-4111-8111-111111111111");
        private static readonly Guid CaseId = Guid.Parse("c1110000-2222-4222-8222-222222222222");
        private static readonly Guid OwnerId = Guid.Parse("01110000-3333-4333-8333-333333333333");

        private const int StatusOpen = 120910600;
        private const string Adviser = "adviser@example.com";

        [Fact]
        public void Tells_the_mapped_manager_that_a_signoff_is_waiting()
        {
            var svc = Ready(mapManager: true);

            Complete(svc);

            var queued = Notifications(svc);
            var row = Assert.Single(queued);
            Assert.Equal("pat@example.com", row.GetAttributeValue<string>("al_recipientemail"));
            Assert.Equal(
                NotificationOutbox.EventSignoffDue,
                row.GetAttributeValue<OptionSetValue>("al_event").Value);
            Assert.Contains("IO-1", row.GetAttributeValue<string>("al_subject"));
        }

        [Fact]
        public void The_case_still_reaches_awaiting_signoff_when_the_adviser_is_unmapped()
        {
            // The important negative. A configuration gap costs the notification, never the
            // adviser's completion and never the case's progress - and the sign-off is still
            // possible, because every T&C Manager may perform one.
            var svc = Ready(mapManager: false);

            var result = Complete(svc);

            Assert.Empty(Notifications(svc));
            Assert.Equal("Completed", result.Status);
            Assert.Equal(
                CaseLifecycle.AwaitingSignoff,
                svc.Retrieve("al_outcomecase", CaseId, new ColumnSet("al_casestatus"))
                    .GetAttributeValue<OptionSetValue>("al_casestatus").Value);
        }

        [Fact]
        public void Says_in_the_trace_log_why_nobody_was_told()
        {
            // An administrator whose manager says "I was never told" needs to find the
            // reason somewhere. Silence and a missing email look identical without this.
            var svc = Ready(mapManager: false);
            var trace = new List<string>();

            Complete(svc, trace.Add);

            Assert.Contains(trace, line => line.Contains(Adviser));
        }

        [Fact]
        public void A_failure_resolving_the_manager_never_fails_the_completion()
        {
            // The mapping row points at a contact that is not there. Whatever that throws,
            // the adviser's work is already done and recorded.
            var svc = Ready(mapManager: false);
            svc.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                TcManagerRouting.MappingEmailAttr, Adviser,
                TcManagerRouting.ManagerAttr, new EntityReference("contact", Guid.NewGuid()),
                "statecode", new OptionSetValue(0));

            var result = Complete(svc);

            Assert.Equal("Completed", result.Status);
            Assert.Empty(Notifications(svc));
        }

        [Fact]
        public void The_letter_links_to_the_cases_remediation_page()
        {
            // Reported 2026-10-08: the coach had to open OTIS, go to Remediation and find the
            // case under "Awaiting T&C sign-off". The page signs a case off from ?case=.
            var svc = Ready(mapManager: true);
            svc.Seed("powerpagesite", Guid.NewGuid(), "primarydomainname", "outcometesting.powerappsportals.com");

            Complete(svc);

            var row = Assert.Single(Notifications(svc));
            Assert.Contains(
                "https://outcometesting.powerappsportals.com/remediation?case=" + CaseId.ToString("D"),
                row.GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void The_letter_greets_the_manager_and_names_the_adviser()
        {
            var svc = Ready(mapManager: true);
            svc.Update(new Entity("al_outcomecase", CaseId) { ["al_advisername"] = "Adam Strumidlo" });

            Complete(svc);

            var body = Assert.Single(Notifications(svc)).GetAttributeValue<string>("al_body");
            Assert.StartsWith("<p>Dear Pat Manager,</p>", body);
            Assert.Contains("Adam Strumidlo has completed every remediation action on case IO-1", body);
        }

        [Fact]
        public void Rework_after_a_rejection_tells_the_manager_again()
        {
            // Reported 2026-10-08: once the adviser redid the work the coach sent back, nobody
            // told the coach. The outbox row was keyed on the case alone, so the second
            // "sign-off due" found the first and was dropped as a duplicate.
            var svc = Ready(mapManager: true);
            Complete(svc);

            svc.Seed("al_signoff", Guid.NewGuid(),
                "al_remediationactionid", new EntityReference("al_remediationaction", ActionId),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_signoffdecision", new OptionSetValue(SignoffProgressPlugin.DecisionRejectedValue),
                "al_notes", "Add the client's signed declaration.",
                "statecode", new OptionSetValue(0));
            svc.Update(SignoffProgressPlugin.ReopenedAction(ActionId, DateTime.UtcNow));
            svc.Update(new Entity("al_outcomecase", CaseId)
            {
                ["al_casestatus"] = new OptionSetValue(CaseLifecycle.AwaitingRemediation),
            });

            Complete(svc);

            Assert.Equal(2, Notifications(svc).Count);
        }

        // ------------------------------------------------------------------ fixture

        /// <summary>A case in remediation with one open action, about to be its last.</summary>
        private static FakeOrganizationService Ready(bool mapManager)
        {
            var svc = new FakeOrganizationService();

            svc.Seed("al_outcomecase", CaseId,
                "al_casereference", "IO-1",
                TcManagerRouting.CaseAdviserEmailAttr, Adviser,
                "al_casestatus", new OptionSetValue(CaseLifecycle.AwaitingRemediation));

            svc.Seed("al_remediationaction", ActionId,
                "al_actionstatus", new OptionSetValue(StatusOpen),
                "al_adviserresponse", "Rewritten and re-sent to the client.",
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "ownerid", new EntityReference("systemuser", OwnerId));

            if (mapManager)
            {
                var manager = svc.Seed("contact", Guid.NewGuid(),
                    "fullname", "Pat Manager",
                    "emailaddress1", "pat@example.com",
                    "statecode", new OptionSetValue(0));

                svc.Seed(TcManagerRouting.MappingEntity, Guid.NewGuid(),
                    TcManagerRouting.MappingEmailAttr, Adviser,
                    TcManagerRouting.ManagerAttr, manager.ToEntityReference(),
                    "statecode", new OptionSetValue(0));
            }

            return svc;
        }

        private static CompleteRemediationPlugin.CompleteResult Complete(
            FakeOrganizationService svc, Action<string> trace = null)
        {
            return CompleteRemediationPlugin.Complete(
                svc,
                ActionId,
                "key-" + Guid.NewGuid().ToString("N"),
                expectedRowVersion: null,
                actorId: OwnerId,
                correlationId: Guid.NewGuid(),
                requireCallerOwnsAction: false,
                details: null,
                trace: trace);
        }

        private static List<Entity> Notifications(FakeOrganizationService svc)
        {
            var found = svc.RetrieveMultiple(new QueryExpression("al_notification")
            {
                ColumnSet = new ColumnSet(true),
            }).Entities;

            var rows = new List<Entity>();
            foreach (var row in found)
            {
                rows.Add(row);
            }

            return rows;
        }
    }
}
