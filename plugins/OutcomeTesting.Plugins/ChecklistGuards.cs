using System;
using System.Collections.Generic;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Question codes that compiled C# or a portal template reads by name (AD-122). Taking one out of the form
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
                { "Q-GR-02", "Q-GR-02 is the primary root cause, which GradingRules requires on every grade but Pass and clears when the grade returns to Pass. Without it no non-pass case records why." },
                { "Q-TAX-02", "Q-TAX-02 is the Tax check outcome, which decides whether a Tax case goes to remediation." },
                { "Q-FQ-01", "Q-FQ-01 is the AQS file quality outcome and Trail Light column 10; the export refuses a row without it." },
                { "Q-FQTAX-01", "Q-FQTAX-01 is the Tax file quality outcome." },
                { "Q-FQ-02", "Q-FQ-02 is the AQS checker observation, carried into the remediation description." },
                { "Q-FQTAX-02", "Q-FQTAX-02 is the Tax checker observation, carried into the remediation description." },
                { "Q-FQ-03", "Q-FQ-03 is the AQS \"Remedial action required?\" answer, which raises remediation." },
                { "Q-FQTAX-03", "Q-FQTAX-03 is the Tax \"Remedial action required?\" answer, which raises remediation." },
                { "Q-GR-03", "Q-GR-03 is the AQS case notes, which the portal's AQS check notes card finds by this code." },
                { "Q-GR-04", "Q-GR-04 is the AQS \"Even Better If\" note, which the portal's AQS check notes card finds by this code." },
                { "Q-TAX-03", "Q-TAX-03 is the Tax case notes, which the portal's Tax check notes card finds by this code." },
                { "Q-TAX-04", "Q-TAX-04 is the Tax remedial note, which the portal's Tax check notes card finds by this code." },
                { "Q-TAX-05", "Q-TAX-05 is the Calculations remediation answer; a Yes raises its own remedial action." },
                { "Q-TAX-06", "Q-TAX-06 holds the Calculations remediation comments, which become that action's wording." },
                { "Q-TAX-07", "Q-TAX-07 is the Taxation Narrative remediation answer; a Yes raises its own remedial action." },
                { "Q-TAX-08", "Q-TAX-08 holds the Taxation Narrative remediation comments, which become that action's wording." },
                { "Q-TAX-09", "Q-TAX-09 is the Omissions remediation answer; a Yes raises its own remedial action." },
                { "Q-TAX-10", "Q-TAX-10 holds the Omissions remediation comments, which become that action's wording." },
                { "Q-TAX-11", "Q-TAX-11 is the Other remediation answer; a Yes raises its own remedial action." },
                { "Q-TAX-12", "Q-TAX-12 holds the Other remediation comments, which become that action's wording." },
            };

        /// <summary>
        /// Codes read by their start rather than whole: grading counts every question coded
        /// Q-AML... as an AML checking point (SubmitReviewPlugin, OT Review Detail).
        /// </summary>
        private static readonly Dictionary<string, string> ProtectedPrefixes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Q-AML", "is an AML checking point: grading reads every question whose code starts Q-AML as one, so taking it out or giving it a new code changes how each AQS grade is checked." },
            };

        /// <summary>Every protected code, for tests and for an administration screen.</summary>
        public static IEnumerable<string> ProtectedCodes
        {
            get { return Protected.Keys; }
        }

        /// <summary>Every protected code prefix, for tests and for an administration screen.</summary>
        public static IEnumerable<string> ProtectedCodePrefixes
        {
            get { return ProtectedPrefixes.Keys; }
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

            var code = questionCode.Trim();
            string reason;
            if (Protected.TryGetValue(code, out reason))
            {
                return reason;
            }

            foreach (var prefix in ProtectedPrefixes)
            {
                if (code.StartsWith(prefix.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return code + " " + prefix.Value;
                }
            }

            return null;
        }
    }
}
