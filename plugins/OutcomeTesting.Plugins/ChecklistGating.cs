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
        /// The Tax check outcome, on the Tax check section, which is what locks the Tax
        /// fail points (project owner, 2026-09-23).
        ///
        /// <para>
        /// Not to be confused with <see cref="FileQuality.TaxQuestionCode"/>, the Tax team's
        /// FILE QUALITY outcome on S-FQTAX. The rule was written against that one on
        /// 2026-09-22 and corrected here: "if Tax check outcome field is pass then the File
        /// Quality - Fail points section needs to be disabled, instead it is done on the File
        /// quality Outcome". Two questions, two sections, two different judgements about the
        /// same file - and the checklist warns that they have been conflated before.
        /// </para>
        /// </summary>
        public const string TaxCheckOutcomeQuestionCode = "Q-TAX-02";

        /// <summary>
        /// The AML and CRA checking points, which are what unlock the File Quality fail
        /// points on an AQS check. Identified by its stable section code rather than its
        /// display name, because the code survives a version being reissued where the name is
        /// editable through al_UpdateSection.
        /// </summary>
        public const string AmlCraSectionCode = "S-AMLCRA";

        /// <summary>
        /// Whether an answer on a test point is a finding of the kind that takes Pass away:
        /// a Fail, or a No.
        ///
        /// Insufficient evidence is deliberately NOT here. It takes away more than a No does -
        /// Pass with issues as well as Pass - so it is asked separately, by
        /// <see cref="IsInsufficient"/>, and the two are combined by the rules below rather
        /// than flattened into one "something is wrong" test that could only express the
        /// weaker of them.
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
