using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Which of the three adviser letters reaches the adviser, and when (project owner,
    /// 2026-09-10). <see cref="NotificationBodiesTests"/> covers what each one says; this
    /// covers the choosing, which is where a mistake sends the wrong grading to a real
    /// person.
    /// </summary>
    public class AdviserNotificationTests
    {
        private static readonly Guid CaseId = Guid.Parse("0c0c0c0c-1d1d-4e1e-8f1f-0a0a0a0a0a0a");
        private static readonly Guid ReviewId = Guid.Parse("1b1b1b1b-2c2c-4d2d-8e2e-1f1f1f1f1f1f");
        private static readonly Guid AdviserId = Guid.Parse("2a2a2a2a-3b3b-4c3c-8d3d-2e2e2e2e2e2e");
        private static readonly Guid Correlation = Guid.NewGuid();

        private static FakeOrganizationService Case(string adviserName = "Sam Adviser")
        {
            var svc = new FakeOrganizationService();
            svc.Seed(
                "al_outcomecase",
                CaseId,
                "al_casereference", "IO-TEST-100",
                "al_advisername", adviserName,
                "al_clientname", "Mr and Mrs Smith");
            svc.Seed(
                "contact",
                AdviserId,
                "fullname", "Sam Adviser",
                "emailaddress1", "sam@example.com",
                "statecode", new OptionSetValue(0));
            return svc;
        }

        private static EntityReference Ref()
        {
            return new EntityReference("al_outcomecase", CaseId);
        }

        /// <summary>Seeds the graded outcome this case's review recorded.</summary>
        private static void Graded(FakeOrganizationService svc, int outcome)
        {
            svc.Seed(
                "al_outcome",
                Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_initialoutcome", new OptionSetValue(outcome));
        }

        private static void OpenAction(FakeOrganizationService svc)
        {
            svc.Seed(
                "al_remediationaction",
                Guid.NewGuid(),
                "al_outcomecaseid", Ref(),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", ReviewId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusOpen),
                "al_name", "Remediation IO-TEST-100");
        }

        private static Entity Queued(FakeOrganizationService svc)
        {
            return svc.Creates.Single(c => c.Contains("al_event"));
        }

        // ---- The remediation letter follows the grading -------------------------------

        [Fact]
        public void A_pass_with_issues_gets_the_pass_with_issues_letter()
        {
            var svc = Case();
            Graded(svc, OutcomeRules.OutcomePassWithIssues);
            OpenAction(svc);

            Remediation.AssignUnassignedActions(svc, Ref(), Correlation);

            var queued = Queued(svc);
            Assert.Equal("Remedial needed - Pass with issues: IO-TEST-100",
                queued.GetAttributeValue<string>("al_subject"));
            Assert.Contains("pass with issues grading", queued.GetAttributeValue<string>("al_body"));
            Assert.Contains("Mr and Mrs Smith", queued.GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void Potential_harm_gets_the_letter_that_names_the_T_and_C_manager()
        {
            var svc = Case();
            Graded(svc, OutcomeRules.OutcomePotentialHarm);
            OpenAction(svc);

            Remediation.AssignUnassignedActions(svc, Ref(), Correlation);

            var queued = Queued(svc);
            Assert.Equal("Remedial needed - insufficient evidence/ potential harm: IO-TEST-100",
                queued.GetAttributeValue<string>("al_subject"));
            Assert.Contains("liaise with your T&C Manager", queued.GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void A_grading_the_copy_does_not_cover_keeps_the_wording_it_had()
        {
            // A flagged Pass raises remediation without a "pass with issues" grading. It
            // must not be told it received one.
            var svc = Case();
            Graded(svc, OutcomeRules.OutcomePass);
            OpenAction(svc);

            Remediation.AssignUnassignedActions(svc, Ref(), Correlation);

            var queued = Queued(svc);
            Assert.Equal("Remediation required on case IO-TEST-100",
                queued.GetAttributeValue<string>("al_subject"));
            Assert.DoesNotContain("grading", queued.GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void An_ungraded_case_keeps_the_wording_it_had()
        {
            // A Tax leg records no BR-005 grade at all.
            var svc = Case();
            OpenAction(svc);

            Remediation.AssignUnassignedActions(svc, Ref(), Correlation);

            Assert.Equal("Remediation required on case IO-TEST-100",
                Queued(svc).GetAttributeValue<string>("al_subject"));
        }

        [Fact]
        public void The_remediation_letter_still_goes_to_the_adviser()
        {
            // Changing the wording must not change who it reaches.
            var svc = Case();
            Graded(svc, OutcomeRules.OutcomePassWithIssues);
            OpenAction(svc);

            Remediation.AssignUnassignedActions(svc, Ref(), Correlation);

            var queued = Queued(svc);
            Assert.Equal(NotificationOutbox.EventRemediationAssigned,
                queued.GetAttributeValue<OptionSetValue>("al_event").Value);
            Assert.Equal("sam@example.com", queued.GetAttributeValue<string>("al_recipientemail"));
        }

        // ---- The pass letter -----------------------------------------------------------

        [Fact]
        public void A_closing_submit_that_owes_nothing_earns_the_pass_letter()
        {
            Assert.True(OutcomeRules.EarnsPassNotification(CaseLifecycle.Closed, false));
        }

        [Fact]
        public void A_submit_that_raised_remediation_does_not_earn_it()
        {
            // The adviser is getting a remedial letter for this case; "no further action is
            // required" would contradict it.
            Assert.False(OutcomeRules.EarnsPassNotification(CaseLifecycle.Closed, true));
        }

        [Fact]
        public void A_submit_that_does_not_close_the_case_does_not_earn_it()
        {
            // A Tax pass with AQS still to come returns the case to the queue. The check is
            // not finished, so nothing may say it is.
            Assert.False(OutcomeRules.EarnsPassNotification(CaseLifecycle.Queued, false));
            Assert.False(OutcomeRules.EarnsPassNotification(CaseLifecycle.AwaitingRemediation, false));
        }

        [Fact]
        public void The_pass_letter_reaches_the_adviser_with_the_case_named()
        {
            var svc = Case();

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            var queued = Queued(svc);
            Assert.Equal(NotificationOutbox.EventCasePassed,
                queued.GetAttributeValue<OptionSetValue>("al_event").Value);
            Assert.Equal("Case check - Pass: IO-TEST-100", queued.GetAttributeValue<string>("al_subject"));
            Assert.Equal("sam@example.com", queued.GetAttributeValue<string>("al_recipientemail"));

            var body = queued.GetAttributeValue<string>("al_body");
            Assert.Contains("Dear Sam Adviser", body);
            Assert.Contains("Mr and Mrs Smith has been checked and graded a Pass", body);
            Assert.Contains("No further action is required.", body);
        }

        [Fact]
        public void The_pass_letter_is_queued_unaddressed_when_the_adviser_is_not_matched()
        {
            // AdviserContact refuses to guess. The outbox already treats a row with no
            // address as one the drain declines to send, which is a state a person can see
            // rather than an invented recipient. The letter still greets the adviser the
            // case names - failing to find their mailbox is not failing to know who they are.
            var svc = Case("Nobody By That Name");

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            var queued = Queued(svc);
            Assert.False(queued.Contains("al_recipientemail"));
            Assert.Contains("Dear Nobody By That Name", queued.GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void The_pass_letter_greets_an_adviser_the_case_does_not_name()
        {
            var svc = Case(null);

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            Assert.Contains("Dear Adviser", Queued(svc).GetAttributeValue<string>("al_body"));
        }

        [Fact]
        public void The_pass_letter_is_queued_once_for_a_case()
        {
            // A replayed submit must not tell the adviser twice.
            var svc = Case();

            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());
            NotificationEmitterPlugin.QueueCasePassed(svc, Correlation, Ref());

            Assert.Single(svc.Creates.Where(c => c.Contains("al_event")));
        }

        [Fact]
        public void The_pass_event_is_named_so_its_dedupe_code_is_its_own()
        {
            // CodeFor derives the alternate key from this name. An unnamed event falls to
            // "Unknown", and every unnamed event would then share one code space.
            Assert.Equal("Case passed", NotificationOutbox.EventName(NotificationOutbox.EventCasePassed));
            Assert.StartsWith("CASEPASSED-", NotificationOutbox.CodeFor(NotificationOutbox.EventCasePassed, CaseId));
        }
    }
}
