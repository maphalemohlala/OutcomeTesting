using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A case failing both checks produces ONE remediation, raised after AQS (project owner,
    /// 2026-09-20).
    ///
    /// <para>
    /// This reverses OD-027 and the remediation half of BR-006. Those sent a Tax non-pass
    /// straight to remediation whatever the route, so AQS never reviewed a file with something
    /// unaddressed on it - and a case that failed both checks produced two sets of actions and
    /// two letters about one file. The owner's direction is that the adviser is asked once.
    /// </para>
    /// <para>
    /// The trade was put to the owner and accepted: a serious Tax failure now waits for the
    /// AQS check. The AQS checker sees the Tax result while they work, so they are not grading
    /// blind.
    /// </para>
    /// <para>
    /// Every edge case listed in the remediation brief has a test here, including the ones
    /// whose behaviour is UNCHANGED - a Tax-only case must not be swept into the reversal, and
    /// nothing says so except a test that would fail if it were.
    /// </para>
    /// </summary>
    public class CombinedRemediationTests
    {
        private static readonly Guid CaseId = Guid.Parse("caaeaaaa-3333-4333-8333-333333333333");

        private const int TaxFail = ResponseRules.ChoiceFail;
        private const int TaxPass = ResponseRules.ChoicePass;

        // ------------------------------------------------------------------ Tax then AQS

        [Fact]
        public void Tax_fail_then_aqs_pass_remediates_once_after_aqs()
        {
            // The combination the old rule could not produce at all: a Tax fail never reached
            // AQS. Tax queues the case and raises nothing; AQS then refuses to close it.
            Assert.Equal(
                CaseLifecycle.Queued,
                OutcomeRules.NextCaseStatusForTax(TaxFail, aqsStillToCome: true, remedialActionFlagged: false));
            Assert.False(OutcomeRules.TaxRaisesRemediationNow(true, aqsStillToCome: true));

            Assert.Equal(
                CaseLifecycle.AwaitingRemediation,
                OutcomeRules.NextCaseStatusForAqs(
                    OutcomeRules.OutcomePass, remedialActionFlagged: false, taxFailDeferred: true));
        }

        [Fact]
        public void Tax_pass_then_aqs_fail_is_unchanged()
        {
            Assert.Equal(
                CaseLifecycle.Queued,
                OutcomeRules.NextCaseStatusForTax(TaxPass, aqsStillToCome: true, remedialActionFlagged: false));

            Assert.Equal(
                CaseLifecycle.AwaitingRemediation,
                OutcomeRules.NextCaseStatusForAqs(
                    OutcomeRules.OutcomePotentialHarm, remedialActionFlagged: false, taxFailDeferred: false));
        }

        [Fact]
        public void Both_fail_produces_one_remediation_not_two()
        {
            // The defect this whole change exists to fix. The Tax leg raises nothing, so the
            // only action set is the one the AQS leg raises - carrying both.
            Assert.False(OutcomeRules.TaxRaisesRemediationNow(true, aqsStillToCome: true));

            Assert.Equal(
                CaseLifecycle.AwaitingRemediation,
                OutcomeRules.NextCaseStatusForAqs(
                    OutcomeRules.OutcomeInsufficient, remedialActionFlagged: false, taxFailDeferred: true));
        }

        [Fact]
        public void Neither_fails_closes_the_case()
        {
            Assert.Equal(
                CaseLifecycle.Queued,
                OutcomeRules.NextCaseStatusForTax(TaxPass, aqsStillToCome: true, remedialActionFlagged: false));

            Assert.Equal(
                CaseLifecycle.Closed,
                OutcomeRules.NextCaseStatusForAqs(
                    OutcomeRules.OutcomePass, remedialActionFlagged: false, taxFailDeferred: false));
        }

        // ------------------------------------------------------------------ single-discipline

        [Theory]
        [InlineData(ResponseRules.ChoiceFail)]
        [InlineData(ResponseRules.ChoiceInsufficient)]
        public void A_tax_only_case_still_remediates_at_the_tax_submit(int answer)
        {
            // UNCHANGED, and it has to be: there is no AQS check coming that could carry the
            // fail, so deferring it would lose it. This is why the rule takes aqsStillToCome
            // rather than reading the route.
            Assert.Equal(
                CaseLifecycle.AwaitingRemediation,
                OutcomeRules.NextCaseStatusForTax(answer, aqsStillToCome: false, remedialActionFlagged: false));
            Assert.True(OutcomeRules.TaxRaisesRemediationNow(true, aqsStillToCome: false));
        }

        [Fact]
        public void A_tax_only_case_that_passes_still_closes()
        {
            Assert.Equal(
                CaseLifecycle.Closed,
                OutcomeRules.NextCaseStatusForTax(TaxPass, aqsStillToCome: false, remedialActionFlagged: false));
        }

        [Fact]
        public void An_aqs_only_case_is_untouched_by_the_reversal()
        {
            // Nothing was deferred because there was no Tax check, so the AQS submit decides
            // alone - exactly as before.
            Assert.Equal(
                CaseLifecycle.AwaitingRemediation,
                OutcomeRules.NextCaseStatusForAqs(
                    OutcomeRules.OutcomePassWithIssues, remedialActionFlagged: false, taxFailDeferred: false));
        }

        // ------------------------------------------------------------------ reading the deferral

        [Fact]
        public void Reads_a_deferred_tax_fail_off_the_case()
        {
            var service = WithCase(TaxFail, submittedTaxReview: true);

            var deferred = SubmitReviewPlugin.DeferredTaxFail(service, CaseId);

            Assert.NotNull(deferred);
            Assert.Contains("Tax check:", deferred.Reason);
        }

        [Fact]
        public void Finds_nothing_to_defer_when_the_tax_check_passed()
        {
            Assert.Null(SubmitReviewPlugin.DeferredTaxFail(WithCase(TaxPass, true), CaseId));
        }

        [Fact]
        public void Finds_nothing_to_defer_on_a_case_with_no_tax_outcome()
        {
            // An AQS-only case. The column is empty, which is not a fail.
            Assert.Null(SubmitReviewPlugin.DeferredTaxFail(WithCase(null, false), CaseId));
        }

        [Fact]
        public void Finds_nothing_to_defer_when_the_tax_review_is_not_submitted()
        {
            // A stamped outcome with no submitted review behind it is not a verdict anyone
            // reached. Without this the combined action would try to read items off a review
            // still being worked on.
            Assert.Null(SubmitReviewPlugin.DeferredTaxFail(WithCase(TaxFail, submittedTaxReview: false), CaseId));
        }

        [Fact]
        public void Stays_silent_on_a_tax_outcome_it_does_not_recognise()
        {
            // Deliberately null rather than a throw. An unreadable Tax result must not stop an
            // AQS checker submitting their own work, and the AQS outcome still raises whatever
            // it raises on its own account.
            Assert.Null(SubmitReviewPlugin.DeferredTaxFail(WithCase(999, true), CaseId));
        }

        /// <summary>A case carrying the given Tax outcome, and optionally a submitted Tax review.</summary>
        private static FakeOrganizationService WithCase(int? taxOutcome, bool submittedTaxReview)
        {
            var service = new FakeOrganizationService();
            var row = service.Seed("al_outcomecase", CaseId, "al_casereference", "IO-1");
            if (taxOutcome.HasValue)
            {
                row["al_taxoutcome"] = new OptionSetValue(taxOutcome.Value);
            }

            var review = service.Seed(
                "al_reviewinstance",
                Guid.NewGuid(),
                "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                "al_reviewtype", new OptionSetValue(ResponseRules.ReviewTypeTax),
                "statecode", new OptionSetValue(0));

            if (submittedTaxReview)
            {
                review["al_submittedon"] = new DateTime(2026, 9, 18);
            }

            return service;
        }
    }
}
