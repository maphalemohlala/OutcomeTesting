using System.Collections.Generic;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Pure decision logic for grading a submitted review (BR-004 to BR-007). Deliberately
    /// free of Dataverse types so it can be unit-tested without a fake organisation
    /// service; SubmitReviewPlugin is thin wiring over this. Mirrors ResponseRules and
    /// CaseLifecycle.
    ///
    /// Option-set values that already exist on ResponseRules are reused rather than
    /// redeclared, so an answer value has one definition in the assembly.
    /// </summary>
    public static class OutcomeRules
    {
        // al_outcome.al_initialoutcome / al_finaloutcome — the BR-005 four-value scale.
        public const int OutcomePass = 120910700;
        public const int OutcomePassWithIssues = 120910701;
        public const int OutcomeInsufficient = 120910702;
        public const int OutcomePotentialHarm = 120910703;

        /// <summary>
        /// Maps the Q-GR-01 "Advice Quality Grade" answer to the outcome it records.
        ///
        /// Returns false rather than a default for any value outside the grade scale.
        /// Defaulting would turn an unrecognised answer into a Pass, which is the most
        /// favourable outcome and the one least likely to be noticed.
        /// </summary>
        public static bool TryGradeFromAnswer(int answerChoice, out int outcome)
        {
            switch (answerChoice)
            {
                case ResponseRules.ChoicePass:
                    outcome = OutcomePass;
                    return true;
                case ResponseRules.ChoicePassWithIssues:
                    outcome = OutcomePassWithIssues;
                    return true;
                case ResponseRules.ChoiceInsufficient:
                    outcome = OutcomeInsufficient;
                    return true;
                case ResponseRules.ChoicePotentialHarm:
                    outcome = OutcomePotentialHarm;
                    return true;
                default:
                    outcome = 0;
                    return false;
            }
        }

        /// <summary>BR-006: every non-pass outcome requires remediation.</summary>
        public static bool RequiresRemediation(int outcome)
        {
            return outcome != OutcomePass;
        }

        /// <summary>
        /// BR-006 widened by the checklist's own trigger: remediation is required when the
        /// outcome is anything but a Pass, OR when the checker answered Yes to "Remedial
        /// action required?" (Q-FQ-03 on the AQS checklist, Q-FQTAX-03 on the Tax one).
        ///
        /// The flag is not a restatement of the outcome. A non-pass already demanded
        /// remediation and still does; what the flag adds is the case the grade cannot
        /// express - a file the checker was content to pass that nonetheless has something
        /// on it the adviser must put right. Both questions are mandatory, so the answer is
        /// always present by the time a review can be submitted.
        /// </summary>
        public static bool RequiresRemediation(int outcome, bool remedialActionFlagged)
        {
            return remedialActionFlagged || RequiresRemediation(outcome);
        }

        /// <summary>
        /// Whether the answer to "Remedial action required?" is a Yes.
        ///
        /// Null is "not answered", never a Yes. Both questions are mandatory so an
        /// unanswered one cannot reach a submit, but defaulting an absent answer to a raise
        /// would invent remediation nobody asked for - a mistake much harder to notice than
        /// a missing one, because the case moves and an adviser is emailed about work that
        /// was never flagged.
        /// </summary>
        public static bool RemedialActionFlagged(int? answerChoice)
        {
            return answerChoice.HasValue && answerChoice.Value == ResponseRules.ChoiceYes;
        }

        /// <summary>
        /// Whether a Q-TAX-02 result sends the case to remediation. The Tax scale is
        /// PassFailInsufficient (AD-055), and a completed Tax check with a non-pass result
        /// enters remediation rather than being returned or cancelled (AD-006).
        /// </summary>
        public static bool TaxResultRequiresRemediation(int answerChoice)
        {
            return answerChoice == ResponseRules.ChoiceFail
                || answerChoice == ResponseRules.ChoiceInsufficient;
        }

        /// <summary>
        /// Whether a Q-TAX-02 result sends the case to remediation, refusing any value
        /// outside the AD-055 PassFailInsufficient scale. The non-Try predicate cannot
        /// distinguish "passed" from "not a tax result at all", and treating an unscaled
        /// value as a pass closes the case terminally — the same failure TryGradeFromAnswer
        /// refuses on the AQS side.
        /// </summary>
        public static bool TryTaxResultRequiresRemediation(int answerChoice, out bool requiresRemediation)
        {
            requiresRemediation = false;

            if (answerChoice != ResponseRules.ChoicePass
                && answerChoice != ResponseRules.ChoiceFail
                && answerChoice != ResponseRules.ChoiceInsufficient)
            {
                return false;
            }

            requiresRemediation = TaxResultRequiresRemediation(answerChoice);
            return true;
        }

        /// <summary>
        /// The al_section.al_ownerrole a review of this discipline is answerable for
        /// (AD-020). Returns false for a review type the model does not define, so the
        /// caller refuses rather than submitting against an empty mandatory set.
        /// </summary>
        public static bool TryOwnerRoleForReviewType(int reviewType, out int ownerRole)
        {
            switch (reviewType)
            {
                case ResponseRules.ReviewTypeTax:
                    ownerRole = ResponseRules.OwnerRoleTaxTeam;
                    return true;
                case ResponseRules.ReviewTypeAqs:
                    ownerRole = ResponseRules.OwnerRoleAqsChecker;
                    return true;
                default:
                    ownerRole = 0;
                    return false;
            }
        }

        /// <summary>
        /// Whether this submit earns the adviser the "Case check - Pass" letter (project
        /// owner, 2026-09-10): the case is finished and nothing is owed on it.
        ///
        /// Closing is the whole test, and it is a stronger one than "the grade was a Pass".
        /// <see cref="NextCaseStatusForAqs"/> reaches Closed only on an unflagged Pass, and
        /// a Tax pass with AQS still to come returns the case to the queue rather than
        /// closing it - so a leg that finished while the case has not cannot tell the
        /// adviser it is over.
        ///
        /// It also disposes of the earlier leg without a second query. A Tax leg that raised
        /// remediation parks the case at Awaiting Remediation, and that remediation has to
        /// clear sign-off before the case can reach its AQS check at all (OD-038), so a case
        /// that closes here has nothing outstanding behind it either.
        /// </summary>
        public static bool EarnsPassNotification(int nextCaseStatus, bool requiresRemediation)
        {
            return !requiresRemediation && nextCaseStatus == CaseLifecycle.Closed;
        }

        /// <summary>
        /// Where an AQS submit leaves the case: closed on an unflagged Pass, awaiting
        /// remediation on anything else (BR-006).
        ///
        /// Raising an action decides the status: a flagged Pass goes to Awaiting
        /// Remediation rather than Closed (project owner direction, 2026-09-09). Closing it
        /// would leave an open action on a case in a terminal state - AD-057 permits no
        /// transition out of Closed, so the adviser could never work it and the sign-off
        /// could never land. An open action always sits on an open case.
        /// </summary>
        public static int NextCaseStatusForAqs(int outcome, bool remedialActionFlagged)
        {
            return RequiresRemediation(outcome, remedialActionFlagged)
                ? CaseLifecycle.AwaitingRemediation
                : CaseLifecycle.Closed;
        }

        /// <summary>
        /// Where a Tax submit leaves the case. A Tax non-pass enters remediation whatever
        /// the route (BR-006, OD-027) — only a passed Tax check hands off to AQS. When the
        /// Tax check passed and AQS is still to come, the case returns to the shared queue
        /// for manual allocation (BR-003, AD-040). Otherwise the Tax result finalises it.
        ///
        /// A flagged Tax pass is held for remediation before AQS rather than handed off
        /// (project owner direction, 2026-09-09). That is the reasoning OD-027 already
        /// applies to a Tax non-pass, for the same reason: AQS must not review a file with
        /// something on it still unaddressed.
        ///
        /// Once that remediation is approved the case returns to the queue for its AQS
        /// check (OD-038, resolved 2026-09-09: the case has to go through remediation before
        /// it can reach AQS). <see cref="SignoffProgressPlugin.MoveCase"/> makes that hop,
        /// which <see cref="CaseLifecycle"/> permits out of Awaiting Sign-off.
        /// </summary>
        public static int NextCaseStatusForTax(int answerChoice, bool aqsStillToCome, bool remedialActionFlagged)
        {
            // OD-027: a Tax non-pass enters remediation whatever the route (BR-006). Only a
            // passed Tax check hands off to AQS — a case whose Tax check failed must not
            // proceed to an advice quality review with the failure unaddressed, and a later
            // AQS pass would otherwise close the case with the Tax fail unremediated.
            if (remedialActionFlagged || TaxResultRequiresRemediation(answerChoice))
            {
                return CaseLifecycle.AwaitingRemediation;
            }

            return aqsStillToCome
                ? CaseLifecycle.Queued
                : CaseLifecycle.Closed;
        }

        /// <summary>
        /// The case statuses a submit moves through, in order, from the case's current
        /// status. An AQS submit and a finalising Tax submit pass through Submitted before
        /// their final state; a Tax handoff with AQS still to come goes straight to the
        /// queue, because the case is not submitted — only its Tax review is. A case still
        /// at Assigned is opened first, since a review may be submitted from Assigned and
        /// nothing moves the case automatically. A case whose status was never set is not
        /// Assigned and so is not opened — the Submitted hop is left to refuse it against
        /// AD-057, rather than inventing a state it was never in.
        ///
        /// Pure so the chain can be asserted against CaseLifecycle.IsAllowed without a
        /// Dataverse service; SubmitReviewPlugin performs the hops this returns.
        /// </summary>
        public static int[] HopsFor(int? currentStatus, int finalStatus)
        {
            var hops = new List<int>();

            if (currentStatus == CaseLifecycle.Assigned && finalStatus != CaseLifecycle.Queued)
            {
                hops.Add(CaseLifecycle.ReviewInProgress);
            }

            if (finalStatus != CaseLifecycle.Queued)
            {
                hops.Add(CaseLifecycle.Submitted);
            }

            hops.Add(finalStatus);
            return hops.ToArray();
        }
    }
}
