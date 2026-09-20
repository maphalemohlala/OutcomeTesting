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
        // The final outcome's own band. al_Outcome keeps the initial and the final in
        // separate columns so both survive a regrade (BR-007), and they are separate option
        // sets: 1209107_0_x is the grade a check gave, 1209107_1_x the grade it ended on.
        public const int FinalOutcomePass = 120910710;
        public const int FinalOutcomePassWithIssues = 120910711;
        public const int FinalOutcomeInsufficient = 120910712;
        public const int FinalOutcomePotentialHarm = 120910713;

        /// <summary>Whether the value is one the BR-005 final scale defines.</summary>
        public static bool IsFinalOutcome(int value)
        {
            return value == FinalOutcomePass
                || value == FinalOutcomePassWithIssues
                || value == FinalOutcomeInsufficient
                || value == FinalOutcomePotentialHarm;
        }

        public const int OutcomePass = 120910700;
        public const int OutcomePassWithIssues = 120910701;
        public const int OutcomeInsufficient = 120910702;
        public const int OutcomePotentialHarm = 120910703;

        /// <summary>
        /// A grade expressed on the common scale the OutcomeRules grade constants use.
        ///
        /// The two columns carry the same four grades on different option sets, so a value
        /// read off al_finaloutcome cannot be compared against OutcomePass and friends until
        /// it is brought onto their band. Skipping that made a final Pass (120910710) differ
        /// from OutcomePass (120910700) and read as a non-pass, which blocked the AD-039
        /// export on every case a regrade had cleared.
        ///
        /// A value on neither scale is returned untouched rather than mapped to a default,
        /// for the reason TryGradeFromAnswer refuses to default: the most favourable grade
        /// is the one least likely to be questioned.
        /// </summary>
        public static int ToGradeScale(int value)
        {
            switch (value)
            {
                case FinalOutcomePass:
                    return OutcomePass;
                case FinalOutcomePassWithIssues:
                    return OutcomePassWithIssues;
                case FinalOutcomeInsufficient:
                    return OutcomeInsufficient;
                case FinalOutcomePotentialHarm:
                    return OutcomePotentialHarm;
                default:
                    return value;
            }
        }

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
            return NextCaseStatusForAqs(outcome, remedialActionFlagged, false);
        }

        /// <summary>
        /// As above, and also carrying a Tax fail that was deferred rather than actioned
        /// (project owner, 2026-09-20).
        ///
        /// <paramref name="taxFailDeferred"/> is what makes the combined remediation possible:
        /// the Tax submit moved the case to the queue and raised nothing, so this submit is
        /// the only one that will ask the adviser for anything. An AQS pass on a case whose
        /// Tax check failed must therefore NOT close - it is the one combination the old rule
        /// could not produce, because a Tax fail never reached AQS at all.
        /// </summary>
        public static int NextCaseStatusForAqs(int outcome, bool remedialActionFlagged, bool taxFailDeferred)
        {
            return RequiresRemediation(outcome, remedialActionFlagged) || taxFailDeferred
                ? CaseLifecycle.AwaitingRemediation
                : CaseLifecycle.Closed;
        }

        /// <summary>
        /// Where a Tax submit leaves the case.
        ///
        /// <para>
        /// <b>If AQS is still to come, the case goes to the queue whatever the Tax result.</b>
        /// A Tax fail is RECORDED - <c>al_taxoutcome</c> is stamped on the case by the same
        /// submit - but it is not actioned yet. The AQS check then runs, and whichever of the
        /// two failed is gathered into ONE combined remediation when AQS is submitted.
        /// </para>
        /// <para>
        /// <b>This reverses OD-027 and the remediation half of BR-006</b> (project owner,
        /// 2026-09-20). Those said a Tax non-pass entered remediation whatever the route, so
        /// that AQS never reviewed a file with something unaddressed on it. The cost was two
        /// remediations on a case that failed both checks: two sets of actions, two letters to
        /// the adviser, and two rounds of the same conversation about one file. The owner's
        /// direction is that the adviser should be asked once.
        /// </para>
        /// <para>
        /// The trade was put to the owner explicitly and accepted: a serious Tax failure now
        /// sits unactioned for as long as the AQS check takes. The AQS checker sees the Tax
        /// result while they work - Tax notes render on a non-Tax review - so they are not
        /// grading blind, which was the other half of the concern.
        /// </para>
        /// <para>
        /// A Tax-only case is unaffected. There is no AQS check to wait for, so a fail goes
        /// straight to remediation exactly as before, and this is why the rule takes
        /// <paramref name="aqsStillToCome"/> rather than reading the route.
        /// </para>
        /// <para>
        /// OD-038 said a case had to pass through remediation before it could reach AQS. It no
        /// longer can, because there is no remediation before AQS on this route. The hop
        /// <see cref="SignoffProgressPlugin.MoveCase"/> makes out of Awaiting Sign-off back to
        /// the queue stays - a case remediated after AQS can still owe a recheck.
        /// </para>
        /// </summary>
        public static int NextCaseStatusForTax(int answerChoice, bool aqsStillToCome, bool remedialActionFlagged)
        {
            // The queue, whatever the result. Deliberately before the fail test: the fail is
            // not lost, it is deferred to the combined remediation that the AQS submit raises.
            if (aqsStillToCome)
            {
                return CaseLifecycle.Queued;
            }

            // Tax-only case, or the Tax leg of a case whose AQS is already done. Nothing is
            // coming that could carry the fail, so it is actioned here.
            if (remedialActionFlagged || TaxResultRequiresRemediation(answerChoice))
            {
                return CaseLifecycle.AwaitingRemediation;
            }

            return CaseLifecycle.Closed;
        }

        /// <summary>
        /// Whether a Tax submit should raise remediation now, or leave it to the AQS submit
        /// to raise once, combined (project owner, 2026-09-20).
        ///
        /// Pure and separate from <see cref="NextCaseStatusForTax"/> because the two answer
        /// different questions and used to be conflated: the status says where the case goes,
        /// this says whether an adviser is asked to do something. A Tax fail with AQS still to
        /// come moves the case to the queue AND raises nothing, which no single value can say.
        /// </summary>
        public static bool TaxRaisesRemediationNow(bool taxRequiresRemediation, bool aqsStillToCome)
        {
            return taxRequiresRemediation && !aqsStillToCome;
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
