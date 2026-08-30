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

        /// <summary>Where an AQS submit leaves the case: closed on a Pass, awaiting
        /// remediation on anything else (BR-006).</summary>
        public static int NextCaseStatusForAqs(int outcome)
        {
            return RequiresRemediation(outcome)
                ? CaseLifecycle.AwaitingRemediation
                : CaseLifecycle.Closed;
        }

        /// <summary>
        /// Where a Tax submit leaves the case. When AQS is still to come the case returns
        /// to the shared queue for manual allocation (BR-003, AD-040) whatever the Tax
        /// result — the Tax outcome does not finalise a case that has not had its advice
        /// quality check. Otherwise the Tax result finalises it.
        /// </summary>
        public static int NextCaseStatusForTax(int answerChoice, bool aqsStillToCome)
        {
            if (aqsStillToCome)
            {
                return CaseLifecycle.Queued;
            }

            return TaxResultRequiresRemediation(answerChoice)
                ? CaseLifecycle.AwaitingRemediation
                : CaseLifecycle.Closed;
        }
    }
}
