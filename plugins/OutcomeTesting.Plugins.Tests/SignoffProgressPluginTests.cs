using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What a rejected sign-off does to the action (BR-008, OD-018). The clock reset is the
    /// half of PP-13 that was missing: without it a case sent back for rework carried on
    /// ageing from its original start, so a second round always read as breached before the
    /// adviser had had a day on it.
    /// </summary>
    public class SignoffProgressPluginTests
    {
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        private static readonly DateTime Now = new DateTime(2026, 9, 3, 10, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void Sends_a_rejected_action_back_to_in_progress()
        {
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.Equal(120910601, update.GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
        }

        [Fact]
        public void Restarts_the_clock_on_a_rejection()
        {
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.Equal(Now, update.GetAttributeValue<DateTime>("al_clockstartedon"));
        }

        [Fact]
        public void Leaves_the_original_start_alone_so_the_previous_period_survives()
        {
            // OD-018 requires both periods, not one merged age. createdon is the original
            // start and is never written here; overwriting it would merge them.
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.False(update.Contains("createdon"));
        }

        [Fact]
        public void Targets_the_action_it_was_given()
        {
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.Equal("al_remediationaction", update.LogicalName);
            Assert.Equal(ActionId, update.Id);
        }

        private static readonly Guid CaseId = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
        private const int Approved = 120910720;
        private const int Rejected = 120910721;

        private static FakeOrganizationService CaseAt(int status)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("al_outcomecase", CaseId, "al_casestatus", new OptionSetValue(status));
            return svc;
        }

        private static int CaseStatus(FakeOrganizationService svc)
        {
            return svc.Row("al_outcomecase", CaseId).GetAttributeValue<OptionSetValue>("al_casestatus").Value;
        }

        private static readonly Guid RouteId = Guid.Parse("cccccccc-3333-4333-8333-cccccccccccc");

        private static void Route(FakeOrganizationService svc, bool tax, bool aqs)
        {
            svc.Seed("al_reviewroute", RouteId, "al_requirestaxreview", tax, "al_requiresaqsreview", aqs);
            svc.Row("al_outcomecase", CaseId)["al_reviewrouteid"] = new EntityReference("al_reviewroute", RouteId);
        }

        private static void SubmittedReview(FakeOrganizationService svc, int reviewType)
        {
            svc.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(reviewType),
                "al_reviewstatus", new OptionSetValue(ResponseRules.StatusSubmitted),
                "al_submittedon", new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc),
                "statecode", new OptionSetValue(0));
        }

        private static void Outcome(FakeOrganizationService svc)
        {
            svc.Seed(
                "al_outcome",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomePassWithIssues));
        }

        [Fact]
        public void An_approval_returns_a_case_that_still_owes_aqs_to_the_queue()
        {
            // OD-038: the Tax check raised remediation; approved, the case goes through to
            // its AQS check rather than closing without one.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: true, aqs: true);
            SubmittedReview(svc, ResponseRules.ReviewTypeTax);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved);

            Assert.Equal(CaseLifecycle.Queued, CaseStatus(svc));
        }

        [Fact]
        public void An_approval_sends_a_graded_case_on_to_recheck()
        {
            // The AQS leg's remediation: the AQS review is in and the Outcome exists, so the
            // T&C Manager sets the final outcome next (al_RegradeCase closes it).
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: true, aqs: true);
            SubmittedReview(svc, ResponseRules.ReviewTypeTax);
            SubmittedReview(svc, ResponseRules.ReviewTypeAqs);
            Outcome(svc);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved);

            Assert.Equal(CaseLifecycle.AwaitingRecheck, CaseStatus(svc));
        }

        [Fact]
        public void An_approval_closes_a_tax_only_case_that_has_nothing_to_regrade()
        {
            // A Tax check records no Outcome (AD-055), so a remediated Tax-only case has no
            // final outcome to set at recheck; waiting there would wait forever.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: true, aqs: false);
            SubmittedReview(svc, ResponseRules.ReviewTypeTax);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved);

            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));
            var hops = svc.Updates.ConvertAll(u => u.GetAttributeValue<OptionSetValue>("al_casestatus").Value);
            Assert.Equal(new[] { CaseLifecycle.AwaitingRecheck, CaseLifecycle.Closed }, hops);
        }

        [Fact]
        public void An_approval_on_a_case_with_no_route_falls_back_to_recheck()
        {
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Outcome(svc);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved);

            Assert.Equal(CaseLifecycle.AwaitingRecheck, CaseStatus(svc));
        }

        [Fact]
        public void A_rejection_returns_the_case_to_awaiting_remediation()
        {
            // AD-057: "a rejected sign-off returns Awaiting Sign-off to Awaiting Remediation"
            // (BR-008). The action was reopened, but the case stayed at Awaiting Sign-off, so
            // the worklists said the T&C Manager still held a case the adviser was reworking.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Rejected);

            Assert.Equal(CaseLifecycle.AwaitingRemediation, CaseStatus(svc));
        }

        [Theory]
        [InlineData(Approved)]
        [InlineData(Rejected)]
        public void Leaves_a_case_that_is_not_awaiting_signoff_where_it_is(int decision)
        {
            var svc = CaseAt(CaseLifecycle.Closed);

            SignoffProgressPlugin.MoveCase(svc, CaseId, decision);

            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Does_not_touch_completion_so_the_earlier_completion_is_not_erased()
        {
            // The adviser did complete the first round; the T&C Manager rejected it. Blanking
            // al_completedon here would erase that they ever responded.
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.False(update.Contains("al_completedon"));
        }
    }
}
