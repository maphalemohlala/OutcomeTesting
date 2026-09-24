using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Pure decision logic for the AQS Judgement and Grading section: whether the primary
    /// root cause is owed and whether a recorded one must be let go (item 3, 2026-09-19).
    ///
    /// The rules about which grades the rest of the form still leaves available moved to
    /// ChecklistGating on 2026-09-22, when they stopped being about the Suitability core
    /// checks - and about the grade alone. Only GradeAllowedWithInsufficientEvidence stayed,
    /// because it names the grade's own scale and nothing else.
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
        /// The root causes as several ticks (project owner, 2026-09-24), which Q-GR-02 answers
        /// on from its successor version. The single select above stays recognised: reviews
        /// submitted before the change hold their one cause against the earlier version.
        /// </summary>
        public const int RootCausesResponseType = ResponseRules.TypeMultiSelectRootCause;

        /// <summary>Whether a question on this response type is the primary root cause.</summary>
        public static bool IsRootCauseResponseType(int responseType)
        {
            return responseType == RootCauseResponseType || responseType == RootCausesResponseType;
        }

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
        /// The two grades a review may still carry once anything on it has been answered
        /// Insufficient evidence (item 10, 2026-09-19; widened 2026-09-22).
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

    }
}
