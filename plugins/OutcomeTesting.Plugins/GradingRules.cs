using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Pure decision logic for the AQS Judgement and Grading section: whether the primary
    /// root cause is owed and whether a recorded one must be let go (item 3, 2026-09-19), and
    /// which advice quality grades the Suitability core checks still leave available
    /// (item 10, 2026-09-19).
    ///
    /// Deliberately free of Dataverse types, like ResponseRules and CaseHeaderRules, so the
    /// rule can be tested without a fake organisation service. Mirrored client-side in
    /// app/src/features/reviews/gradingRules.ts and read by OT Review Detail; when one
    /// changes, change all three.
    ///
    /// The root cause answers "why was this not a pass". A passing file has no root cause to
    /// name, and Q-GR-02 is seeded mandatory, so before this rule every Pass review was held
    /// at submission by a question with no true answer - the checker had to invent one, and
    /// that invention then went out in the trend MI Q-GR-02 exists to feed (AD-022).
    /// </summary>
    public static class GradingRules
    {
        /// <summary>
        /// Advice Quality Grade. Load-bearing by name in OutcomeRules, which maps its answer
        /// to the case outcome, and in ChecklistGuards, which refuses to retire it.
        /// </summary>
        public const string GradeQuestionCode = "Q-GR-01";

        /// <summary>
        /// Primary root cause. Load-bearing by name from this rule onwards, which is why it
        /// joins the ChecklistGuards list (AD-122): retire it and every non-pass review
        /// loses the column the trend MI reads.
        /// </summary>
        public const string RootCauseQuestionCode = "Q-GR-02";

        /// <summary>
        /// The grade's own scale (Pass / Pass with issues / Insufficient evidence / Potential
        /// harm) and the nine-cause single select, each used by exactly one question in the
        /// V8 checklist - the grade scale by Q-GR-01 and the single select by Q-GR-02, which
        /// is what AD-055 deliberately arranged.
        ///
        /// Held here because both front ends already have the response type in hand where
        /// they do not have the question code: the portal writes it into data-response-type
        /// on every answer row, and the app reads it off the form row. They are a cheap first
        /// filter, never the last word - the plug-in confirms the question code before it
        /// clears anything, because a response type is a convention and a code is a contract.
        /// </summary>
        public const int GradeResponseType = 120910010;

        /// <summary>See <see cref="GradeResponseType"/>.</summary>
        public const int RootCauseResponseType = 120910003;

        /// <summary>
        /// Whether this review still owes a primary root cause.
        ///
        /// Owed for every grade but Pass, including a grade outside the scale: an
        /// unrecognised value is not a pass, and demanding the root cause is the safe
        /// direction, exactly as OutcomeRules.TryGradeFromAnswer refuses to read one as a
        /// pass.
        ///
        /// Not owed while the grade is unanswered, and that is not the same as excusing it.
        /// The grade is mandatory in its own right, so an ungraded review is refused at
        /// submission anyway; what this avoids is naming Q-GR-02 in that refusal, when
        /// whether it is owed at all is not yet knowable. The checker answers the grade, and
        /// the root cause is asked for - or is not - on the next attempt.
        /// </summary>
        public static bool RootCauseRequired(int? gradeAnswer)
        {
            return gradeAnswer.HasValue && gradeAnswer.Value != ResponseRules.ChoicePass;
        }

        /// <summary>
        /// Whether a recorded primary root cause must be cleared, because the grade now says
        /// the file passed.
        ///
        /// Not simply the negation of <see cref="RootCauseRequired"/>: an unanswered grade
        /// owes no root cause and destroys none either. A checker who picks the root cause
        /// before the grade - the two sit one above the other and nothing stops it - would
        /// otherwise have the answer wiped on the way past.
        ///
        /// Clearing rather than hiding, because a hidden value is still a stored value: it
        /// would export as a live root cause against a passing case and read to anyone
        /// querying the table as a contradiction nobody could account for.
        /// </summary>
        public static bool RootCauseCleared(int? gradeAnswer)
        {
            return gradeAnswer.HasValue && gradeAnswer.Value == ResponseRules.ChoicePass;
        }

        /// <summary>
        /// The section code every Suitability core check carries: S-E1 to S-E5 in V8, and
        /// whatever an S-E6 added through checklist administration would be (item 10,
        /// 2026-09-19).
        ///
        /// The batch asked for the section to be identified "by a stable key, not its display
        /// name", and for the rule to hold across checklist versions. al_sectioncode is that
        /// key: it is carried forward when a version is reissued, where the name is editable
        /// through al_UpdateSection and the id is per row. A prefix rather than a fixed list
        /// for the same reason - the five are a family, and a sixth belongs to it without a
        /// code change, which is how isOutcomeLens already reads them client-side.
        ///
        /// S-CRP and S-CD deliberately do NOT match, though they answer on scales that also
        /// offer Insufficient evidence. They are separate blocks on the Checker Checklist and
        /// the batch named the Suitability core checks alone.
        /// </summary>
        public const string SuitabilitySectionPrefix = "S-E";

        /// <summary>
        /// Whether this section is one of the Suitability core checks.
        ///
        /// S-E must be followed by a digit, so a section coded S-EXTRA is not swept in by a
        /// prefix match that never asked what came next.
        /// </summary>
        public static bool IsSuitabilitySection(string sectionCode)
        {
            if (string.IsNullOrWhiteSpace(sectionCode))
            {
                return false;
            }

            var code = sectionCode.Trim();
            if (code.Length <= SuitabilitySectionPrefix.Length)
            {
                return false;
            }

            if (!code.StartsWith(SuitabilitySectionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return char.IsDigit(code[SuitabilitySectionPrefix.Length]);
        }

        /// <summary>
        /// The two grades a review may still carry once any Suitability core check has been
        /// answered Insufficient evidence (item 10, 2026-09-19).
        ///
        /// Evidence the checker found insufficient on a core check cannot be reconciled with
        /// a file that passed, with or without issues. The grade is not overridden - the
        /// checker still chooses between Insufficient evidence and Potential harm, which is a
        /// judgement only they can make - it is the two grades that contradict their own ticks
        /// that stop being available.
        /// </summary>
        public static bool GradeAllowedWithInsufficientEvidence(int gradeAnswer)
        {
            return gradeAnswer == ResponseRules.ChoiceInsufficient
                || gradeAnswer == ResponseRules.ChoicePotentialHarm;
        }

        /// <summary>
        /// Why this grade cannot be recorded against these Suitability answers, or null when
        /// it can. Returns a message with no failure prefix - the caller adds PRECONDITION:.
        ///
        /// An absent grade passes: clearing the grade is what this rule does to an invalid one
        /// already saved, so refusing the cleared state would leave the review unrecoverable.
        /// </summary>
        public static string SuitabilityGradeRefusal(int? gradeAnswer, bool suitabilityInsufficient)
        {
            if (!suitabilityInsufficient || !gradeAnswer.HasValue)
            {
                return null;
            }

            if (GradeAllowedWithInsufficientEvidence(gradeAnswer.Value))
            {
                return null;
            }

            return "A Suitability core check has been answered Insufficient evidence, so the "
                + "advice quality grade can only be Insufficient evidence or Potential harm.";
        }

        /// <summary>
        /// Whether a grade already recorded must be cleared because a Suitability core check
        /// has just been answered Insufficient evidence (item 10, 2026-09-19).
        ///
        /// The batch's instruction is to clear a now-invalid outcome and prompt, rather than
        /// to refuse the tick: the checker is recording what they found on the file, and the
        /// finding is not the thing to argue with. The grade they must now re-make is between
        /// Insufficient evidence and Potential harm.
        ///
        /// Named against the two grades this clears rather than against everything the rule
        /// disallows, so a value outside the scale is refused at the guard and never quietly
        /// destroyed here.
        /// </summary>
        public static bool GradeClearedBySuitability(int? gradeAnswer)
        {
            return gradeAnswer.HasValue
                && (gradeAnswer.Value == ResponseRules.ChoicePass
                    || gradeAnswer.Value == ResponseRules.ChoicePassWithIssues);
        }
    }
}
