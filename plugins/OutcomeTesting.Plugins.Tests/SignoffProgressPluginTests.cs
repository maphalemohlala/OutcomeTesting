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

        private static readonly Guid TaxReviewId = Guid.Parse("dddddddd-4444-4444-8444-dddddddddddd");
        private static readonly Guid AqsReviewId = Guid.Parse("eeeeeeee-5555-4555-8555-eeeeeeeeeeee");

        private static Guid ActionOn(FakeOrganizationService svc, Guid reviewId)
        {
            var id = Guid.NewGuid();
            svc.Seed(
                "al_remediationaction",
                id,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted));
            return id;
        }

        private static void SignedOff(FakeOrganizationService svc, Guid actionId, int decision = Approved)
        {
            svc.Seed(
                "al_signoff",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_remediationactionid", new EntityReference("al_remediationaction", actionId),
                "al_signoffdecision", new OptionSetValue(decision));
        }

        /// <summary>
        /// The remediation of one check is signed off as a whole, not action by action.
        ///
        /// A review raises one action per thing the checker marked down, so an AQS check can
        /// carry eighteen of them. Before this, the first approval moved the case out of
        /// Awaiting Sign-off and the other seventeen sign-offs found it already moved and
        /// changed nothing - the case reached recheck on one approval of eighteen. It looked
        /// right only because the sign-offs were done in a batch seconds apart.
        ///
        /// The completion side already had this gate (CompleteRemediationPlugin.AnyOutstanding,
        /// 2026-09-10); this is the same rule one step later, and scoped to the check rather
        /// than the case so the two legs of a Tax-then-AQS route are separate remediations.
        /// </summary>
        [Fact]
        public void An_approval_holds_the_case_while_the_same_check_has_actions_still_to_decide()
        {
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: false, aqs: true);
            SubmittedReview(svc, ResponseRules.ReviewTypeAqs);
            Outcome(svc);

            var first = ActionOn(svc, AqsReviewId);
            ActionOn(svc, AqsReviewId);
            SignedOff(svc, first);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved, AqsReviewId);

            Assert.Equal(CaseLifecycle.AwaitingSignoff, CaseStatus(svc));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void The_grade_recorded_with_the_last_approval_still_lands_after_the_case_moves()
        {
            // The order the plug-in actually runs in: MoveCase first, then the final outcome,
            // gated on the status MoveCase left behind. AD-127 now refuses a final outcome
            // before the case reaches its recheck, and this is the path that must still get
            // through it - the supervisor who sets the grade as they approve (2026-09-11).
            // Composed from the two public steps because RecordFinalOutcome is private; it is
            // the same pair, in the same order, against the same service.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: false, aqs: true);
            SubmittedReview(svc, ResponseRules.ReviewTypeAqs);

            var outcomeId = Guid.Parse("dddddddd-3333-4333-8333-333333333333");
            svc.Seed(
                "al_outcome", outcomeId,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", AqsReviewId),
                "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomeInsufficient));

            SignedOff(svc, ActionOn(svc, AqsReviewId));

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved, AqsReviewId);
            Assert.Equal(CaseLifecycle.AwaitingRecheck, CaseStatus(svc));

            RegradeCasePlugin.Regrade(
                svc,
                svc,
                outcomeId,
                RegradeCasePlugin.FinalOutcomeLabel(OutcomeRules.FinalOutcomePass),
                "Approved, and graded with the approval.",
                null,
                "signoff-regrade-" + outcomeId.ToString("N"),
                new FakePluginExecutionContext());

            Assert.Equal(
                OutcomeRules.FinalOutcomePass,
                svc.Row("al_outcome", outcomeId).GetAttributeValue<OptionSetValue>("al_finaloutcome").Value);
            Assert.Equal(CaseLifecycle.Closed, CaseStatus(svc));
        }

        [Fact]
        public void An_approval_moves_the_case_once_every_action_on_that_check_is_decided()
        {
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: false, aqs: true);
            SubmittedReview(svc, ResponseRules.ReviewTypeAqs);
            Outcome(svc);

            var first = ActionOn(svc, AqsReviewId);
            var second = ActionOn(svc, AqsReviewId);
            SignedOff(svc, first);
            SignedOff(svc, second);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved, AqsReviewId);

            Assert.Equal(CaseLifecycle.AwaitingRecheck, CaseStatus(svc));
        }

        [Fact]
        public void Another_checks_undecided_actions_do_not_hold_this_one_back()
        {
            // The point of separating them: a Tax action left undecided - a rejection that
            // reopened, say - is not the AQS check's remediation and must not stop it.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: true, aqs: true);
            SubmittedReview(svc, ResponseRules.ReviewTypeTax);
            SubmittedReview(svc, ResponseRules.ReviewTypeAqs);
            Outcome(svc);

            ActionOn(svc, TaxReviewId);

            var aqs = ActionOn(svc, AqsReviewId);
            SignedOff(svc, aqs);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved, AqsReviewId);

            Assert.Equal(CaseLifecycle.AwaitingRecheck, CaseStatus(svc));
        }

        [Fact]
        public void A_rejection_returns_the_case_without_waiting_for_the_rest()
        {
            // A rejection is the whole check going back: the adviser is reworking it, so the
            // case belongs at Awaiting Remediation immediately rather than after the other
            // actions have been decided.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: false, aqs: true);

            var first = ActionOn(svc, AqsReviewId);
            ActionOn(svc, AqsReviewId);
            SignedOff(svc, first);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Rejected, AqsReviewId);

            Assert.Equal(CaseLifecycle.AwaitingRemediation, CaseStatus(svc));
        }

        [Fact]
        public void An_action_with_no_review_falls_back_to_the_whole_case()
        {
            // Rows written before the review link existed carry none. Gating on nothing would
            // restore the first-approval-wins behaviour, so the case is the scope instead.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: false, aqs: true);
            SubmittedReview(svc, ResponseRules.ReviewTypeAqs);
            Outcome(svc);

            var id = Guid.NewGuid();
            svc.Seed(
                "al_remediationaction",
                id,
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_actionstatus", new OptionSetValue(Remediation.StatusCompleted));

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved, null);

            Assert.Equal(CaseLifecycle.AwaitingSignoff, CaseStatus(svc));
        }
    
        // ---------------------------------------------------------------------------------
        // Who signed (BR-008, AD-031, NFR-AUD-01)
        // ---------------------------------------------------------------------------------

        private static Entity SignedBy(string id, string name)
        {
            var signoff = new Entity("al_signoff", Guid.NewGuid());
            if (id != null) signoff["al_signedbycontactid"] = id;
            if (name != null) signoff["al_signedbyname"] = name;
            return signoff;
        }

        [Fact]
        public void Names_the_signatory_the_row_carries()
        {
            var contact = Guid.Parse("5ac28998-68a7-f111-aaac-e4fade069307");

            Assert.Equal(contact, SignoffProgressPlugin.SignatoryId(SignedBy(contact.ToString("D"), "A Supervisor")));
        }

        [Fact]
        public void Falls_back_to_the_caller_where_the_row_names_no_signatory()
        {
            // Every sign-off written before the signatory columns existed. Null lets
            // WriteAuditEvent use the initiating user rather than attributing to nobody.
            Assert.Null(SignoffProgressPlugin.SignatoryId(SignedBy(null, null)));
            Assert.Null(SignoffProgressPlugin.SignatoryId(SignedBy("", null)));
            Assert.Null(SignoffProgressPlugin.SignatoryId(SignedBy(Guid.Empty.ToString("D"), null)));
        }

        [Fact]
        public void Treats_a_malformed_signatory_as_absent_rather_than_refusing_the_signoff()
        {
            // The supervisor has already decided; losing that over a bad string would be
            // worse than recording it against the caller.
            Assert.Null(SignoffProgressPlugin.SignatoryId(SignedBy("not-a-guid", "A Supervisor")));
        }

        [Fact]
        public void Writes_the_signatory_into_the_details_line_instead_of_a_raw_guid()
        {
            // The line used to end "Sign-off 3a8e654a-52af-f111-aaac-e4fade069307", which
            // told a reader nothing: the event already carries the target id in its own
            // column, and a person reading a case history wants the name.
            var details = SignoffProgressPlugin.DescribeSignoff(
                SignoffProgressPlugin.DecisionApprovedValue, "A Supervisor");

            Assert.Equal("Signed off from the portal: Approved by A Supervisor.", details);
            Assert.DoesNotContain("-", details.Replace("Signed off", "").Replace("sign-off", ""));
        }

        [Fact]
        public void Leaves_the_details_line_unnamed_rather_than_hexadecimal_when_nobody_is_named()
        {
            Assert.Equal(
                "Signed off from the portal: Rejected.",
                SignoffProgressPlugin.DescribeSignoff(SignoffProgressPlugin.DecisionRejectedValue, null));
            Assert.Equal(
                "Signed off from the portal: Approved.",
                SignoffProgressPlugin.DescribeSignoff(SignoffProgressPlugin.DecisionApprovedValue, "   "));
        }

        [Fact]
        public void A_second_decision_on_the_same_action_has_its_own_replay_key()
        {
            // Keyed on the action, the approval that followed a rejection found the
            // rejection's Audit Event and returned before moving the case.
            var rejection = SignoffProgressPlugin.ReplayKeyFor(Guid.NewGuid());
            var approval = SignoffProgressPlugin.ReplayKeyFor(Guid.NewGuid());

            Assert.NotEqual(rejection, approval);
            Assert.StartsWith("signoff-", approval);
        }

        [Fact]
        public void A_reworked_action_whose_only_decision_was_a_rejection_still_holds_the_case()
        {
            // The guard lets a rejected action be decided again once reworked (2026-09-12).
            // Counting its rejection row as the decision let the other approvals move the
            // case on with this one never approved - the AD-114(a) gate with a hole in it.
            var svc = CaseAt(CaseLifecycle.AwaitingSignoff);
            Route(svc, tax: false, aqs: true);

            var reworked = ActionOn(svc, AqsReviewId);
            var other = ActionOn(svc, AqsReviewId);
            SignedOff(svc, reworked, Rejected);
            SignedOff(svc, other);

            SignoffProgressPlugin.MoveCase(svc, CaseId, Approved, AqsReviewId);

            Assert.Equal(CaseLifecycle.AwaitingSignoff, CaseStatus(svc));
        }
    }
}
