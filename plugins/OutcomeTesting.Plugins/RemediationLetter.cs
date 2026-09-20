using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Which remedial letter a grading earned, and how that letter names the grading.
    ///
    /// <para>
    /// Deliberately NOT in <see cref="NotificationTemplates"/>. That file is LINKED into the
    /// registration tool so the wording has exactly one home, and the tool targets net8.0
    /// while the plug-ins target net462 - so a project reference is not available and the
    /// linked file has to stay free of anything but Microsoft.Xrm.Sdk. Reaching for
    /// OutcomeRules from there broke the tool's build the moment it was added, which is how
    /// this class came to exist.
    /// </para>
    /// </summary>
    public static class RemediationLetter
    {
        /// <summary>
        /// Which remedial letter a grading earned, falling back to the plainer one.
        ///
        /// A flagged Pass and a Tax leg record no BR-005 grade the supplied copy was written
        /// for, so they get <see cref="NotificationTemplates.RemediationOther"/> rather than being told they
        /// received a grading they did not.
        /// </summary>
        public static string RemediationCodeFor(int? outcome)
        {
            if (!outcome.HasValue)
            {
                return NotificationTemplates.RemediationOther;
            }

            switch (outcome.Value)
            {
                case OutcomeRules.OutcomePassWithIssues:
                    return NotificationTemplates.RemediationPassWithIssues;
                case OutcomeRules.OutcomeInsufficient:
                case OutcomeRules.OutcomePotentialHarm:
                    return NotificationTemplates.RemediationHarm;
                default:
                    return NotificationTemplates.RemediationOther;
            }
        }

        /// <summary>
        /// How the letter names the grading. Insufficient evidence and Potential harm share
        /// a phrase, as they share a letter.
        /// </summary>
        public static string GradingPhrase(int? outcome)
        {
            if (!outcome.HasValue)
            {
                return string.Empty;
            }

            switch (outcome.Value)
            {
                case OutcomeRules.OutcomePassWithIssues:
                    return "a pass with issues grading";
                case OutcomeRules.OutcomeInsufficient:
                case OutcomeRules.OutcomePotentialHarm:
                    return "an insufficient evidence/ potential harm grading";
                default:
                    return string.Empty;
            }
        }
    }
}
