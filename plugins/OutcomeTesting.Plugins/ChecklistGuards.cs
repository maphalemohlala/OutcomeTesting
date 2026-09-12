using System;
using System.Collections.Generic;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Question codes that compiled C# reads by name (AD-122). Taking one out of the form
    /// does not degrade the checklist, it stops the system producing outcomes, so
    /// RetireQuestion, MoveQuestion, RetireSection and the optional flag all refuse them.
    ///
    /// Rewording a protected question is safe and is not guarded: the code lives on
    /// al_question and no version edit touches it.
    ///
    /// Each entry names what breaks, because a refusal that only says "no" leaves the
    /// administrator with nowhere to go.
    /// </summary>
    public static class ChecklistGuards
    {
        private static readonly Dictionary<string, string> Protected =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Q-GR-01", "Q-GR-01 carries the advice quality grade, which OutcomeRules maps to the case outcome. Without it no case can be graded." },
                { "Q-TAX-02", "Q-TAX-02 is the Tax check outcome, which decides whether a Tax case goes to remediation." },
                { "Q-FQ-01", "Q-FQ-01 is the AQS file quality outcome and Trail Light column 10; the export refuses a row without it." },
                { "Q-FQTAX-01", "Q-FQTAX-01 is the Tax file quality outcome." },
                { "Q-FQ-02", "Q-FQ-02 is the AQS checker observation, carried into the remediation description." },
                { "Q-FQTAX-02", "Q-FQTAX-02 is the Tax checker observation, carried into the remediation description." },
                { "Q-FQ-03", "Q-FQ-03 is the AQS \"Remedial action required?\" answer, which raises remediation under BR-006." },
                { "Q-FQTAX-03", "Q-FQTAX-03 is the Tax \"Remedial action required?\" answer, which raises remediation under BR-006." },
            };

        /// <summary>Every protected code, for tests and for an administration screen.</summary>
        public static IEnumerable<string> ProtectedCodes
        {
            get { return Protected.Keys; }
        }

        /// <summary>
        /// What breaks if this question leaves the form, or null when nothing does.
        /// </summary>
        public static string ProtectedReason(string questionCode)
        {
            if (string.IsNullOrWhiteSpace(questionCode))
            {
                return null;
            }

            string reason;
            return Protected.TryGetValue(questionCode.Trim(), out reason) ? reason : null;
        }
    }
}
