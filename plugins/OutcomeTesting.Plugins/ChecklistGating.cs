using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The checklist's outcome questions, by code. Until 2026-10-05 this also held the rules
    /// that took options off the grade and the file quality outcome and decided "Remedial
    /// action required?" from the outcome; the project owner withdrew them ("allow any
    /// answer"), so the checker's choice stands and remediation follows the grade or the flag
    /// alone (OutcomeRules.RequiresRemediation).
    /// </summary>
    public static class ChecklistGating
    {
        /// <summary>The AQS "Remedial action required?" answer, which raises remediation.</summary>
        public const string RemedialActionQuestionCode = "Q-FQ-03";

        /// <summary>The Tax "Remedial action required?" answer.</summary>
        public const string TaxRemedialActionQuestionCode = "Q-FQTAX-03";

        /// <summary>
        /// Whether an answer on a test point is a finding of the kind that a checker might
        /// flag: a Fail, or a No.
        ///
        /// Insufficient evidence is deliberately NOT here. It is a different kind of finding
        /// - a gap in the evidence rather than a fail - so it is asked separately, by
        /// <see cref="IsInsufficient"/>, rather than flattened into one "something is wrong"
        /// test that could not tell the two apart.
        /// </summary>
        public static bool IsNoOrFail(int choice)
        {
            return choice == ResponseRules.ChoiceFail || choice == ResponseRules.ChoiceNo;
        }

        /// <summary>Whether an answer is Insufficient evidence.</summary>
        public static bool IsInsufficient(int choice)
        {
            return choice == ResponseRules.ChoiceInsufficient;
        }
    }
}
