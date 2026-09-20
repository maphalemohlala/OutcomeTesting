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
                TcManagerRouting.ManagerAttr, new EntityReference("contact", Guid.NewGuid()));

            var result = Complete(svc);

            Assert.Equal("Completed", result.Status);
            Assert.Empty(Notifications(svc));
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
                    TcManagerRouting.ManagerAttr, manager.ToEntityReference());
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
