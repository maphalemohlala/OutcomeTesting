using System;
using System.Collections.Generic;
using System.Text;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The canonical case lifecycle, as a pure transition table (FR-010 range, BR-002,
    /// BR-008, AD-036). Free of Dataverse types so it is unit-testable without a fake
    /// organisation service, matching <see cref="ResponseRules"/> and the client-side
    /// guards in app/src/types/domain.ts.
    ///
    /// Without this, al_casestatus is written straight from caller input: a case can jump
    /// from Imported to Closed, skipping validation, allocation, review and remediation,
    /// and then be collected by the export — which filters on Closed — and delivered with
    /// no review instance and a blank grade.
    ///
    /// The table encodes only transitions the requirements describe. Anything they do not
    /// describe is refused rather than guessed at; where a genuinely needed path is
    /// missing, the fix is a decision recorded in the decision log, not a wider table.
    /// </summary>
    public static class CaseLifecycle
    {
        // al_outcomecase.al_casestatus
        public const int Imported = 120910580;
        public const int ValidationFailed = 120910581;
        public const int ReadyForAllocation = 120910582;
        public const int Queued = 120910583;
        public const int Assigned = 120910584;
        public const int ReviewInProgress = 120910585;
        public const int Submitted = 120910586;
        public const int AwaitingRemediation = 120910587;
        public const int RemediationInProgress = 120910588;
        public const int AwaitingSignoff = 120910589;
        public const int AwaitingRecheck = 120910590;
        public const int Closed = 120910591;
        public const int NoCheckRequired = 120910592;

        private static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            { Imported, "Imported" },
            { ValidationFailed, "Validation Failed" },
            { ReadyForAllocation, "Ready for Allocation" },
            { Queued, "Queued" },
            { Assigned, "Assigned" },
            { ReviewInProgress, "Review In Progress" },
            { Submitted, "Submitted" },
            { AwaitingRemediation, "Awaiting Remediation" },
            { RemediationInProgress, "Remediation In Progress" },
            { AwaitingSignoff, "Awaiting Sign-off" },
            { AwaitingRecheck, "Awaiting Recheck" },
            { Closed, "Closed" },
            { NoCheckRequired, "No Check Required" },
        };

        /// <summary>
        /// What each state may become. Sources, state by state:
        ///
        /// The spine is the canonical lifecycle in knowledge/project-context.md:
        /// Imported -> Validation Failed | Ready for Allocation -> Queued -> Assigned ->
        /// Review In Progress -> Submitted -> Awaiting Remediation | Closed ->
        /// Remediation In Progress -> Awaiting Sign-off -> Awaiting Recheck -> Closed.
        ///
        /// Validation Failed returns to Ready for Allocation when the intake data is
        /// corrected, or Closed where work must restart and the case is resubmitted as a
        /// new one (BR-002; the replacement is linked through Previous/Replacement Case,
        /// AD-029, and a superseded case stays Closed, AD-036).
        ///
        /// Awaiting Sign-off returns to Awaiting Remediation when the T&C Manager rejects
        /// the remediation, which "returns with notes" (project-context step 9, BR-008).
        /// That return path is already implemented server-side in SignOffRemediationPlugin.
        ///
        /// No Check Required is the AD-036 bypass "for cases that must not receive a
        /// grading outcome", so it is reachable only while no grade exists — up to and
        /// including Review In Progress. Past Submitted a grade has been recorded, and
        /// routing such a case into the bypass would drop it from MI with no audited
        /// override; correcting a graded case is the privileged AD-031 path instead.
        ///
        /// Closed and No Check Required are terminal. Reopening, overriding or regrading a
        /// closed outcome is a privileged T&C Manager correction requiring a mandatory
        /// reason on an immutable Audit Event (OD-007, AD-031) — al_RegradeCase, not an
        /// ordinary details edit.
        ///
        /// A case returns to Queued from Assigned or Review In Progress when one
        /// discipline has finished and another is still required — the Tax-then-AQS
        /// handoff (BR-004). Allocation is manual (BR-003, AD-040), so the case goes back
        /// to the shared queue for a manager to assign the AQS checker rather than moving
        /// straight to a named person. This is a handoff, not a backwards step.
        /// </summary>
        private static readonly Dictionary<int, int[]> Allowed = new Dictionary<int, int[]>
        {
            { Imported, new[] { ValidationFailed, ReadyForAllocation, NoCheckRequired } },
            { ValidationFailed, new[] { ReadyForAllocation, Closed, NoCheckRequired } },
            { ReadyForAllocation, new[] { Queued, NoCheckRequired } },
            { Queued, new[] { Assigned, NoCheckRequired } },
            { Assigned, new[] { ReviewInProgress, Queued, NoCheckRequired } },
            { ReviewInProgress, new[] { Submitted, Queued, NoCheckRequired } },
            { Submitted, new[] { AwaitingRemediation, Closed } },
            { AwaitingRemediation, new[] { RemediationInProgress } },
            { RemediationInProgress, new[] { AwaitingSignoff } },
            { AwaitingSignoff, new[] { AwaitingRecheck, AwaitingRemediation } },
            { AwaitingRecheck, new[] { Closed } },
            { Closed, new int[0] },
            { NoCheckRequired, new int[0] },
        };

        /// <summary>True when <paramref name="to"/> is a value the model defines.</summary>
        public static bool IsKnownStatus(int status)
        {
            return Names.ContainsKey(status);
        }

        /// <summary>
        /// Whether a case may move from <paramref name="from"/> to <paramref name="to"/>.
        ///
        /// A null <paramref name="from"/> — a case with no status recorded — allows any
        /// known value: such a row is not part-way through the lifecycle, and refusing
        /// would leave it uneditable rather than correctable. Re-stating the current status
        /// is not a transition and is always allowed, so an edit that resubmits the status
        /// alongside other fields is not refused as one.
        /// </summary>
        public static bool IsAllowed(int? from, int to)
        {
            if (!IsKnownStatus(to))
            {
                return false;
            }

            if (!from.HasValue)
            {
                return true;
            }

            if (from.Value == to)
            {
                return true;
            }

            int[] next;
            if (!Allowed.TryGetValue(from.Value, out next))
            {
                return false;
            }

            return Array.IndexOf(next, to) >= 0;
        }

        /// <summary>The display name for a status value, or the raw value if unknown.</summary>
        public static string NameOf(int status)
        {
            string name;
            return Names.TryGetValue(status, out name) ? name : status.ToString();
        }

        /// <summary>
        /// The refusal message. It names both states and what the case can actually move
        /// to, because "that transition is not allowed" leaves the user guessing at a
        /// lifecycle they cannot see (PP-16, NFR-OBS-01).
        /// </summary>
        public static string DescribeRefusal(int? from, int to)
        {
            if (!IsKnownStatus(to))
            {
                return "Status " + to + " is not a case status this solution recognises.";
            }

            var fromName = from.HasValue ? NameOf(from.Value) : "(none)";
            var message = new StringBuilder();
            message.Append("A case cannot move from ").Append(fromName).Append(" to ").Append(NameOf(to)).Append(". ");

            int[] next;
            if (from.HasValue && Allowed.TryGetValue(from.Value, out next) && next.Length > 0)
            {
                message.Append("From ").Append(fromName).Append(" it can move to ");
                for (var i = 0; i < next.Length; i++)
                {
                    if (i > 0)
                    {
                        message.Append(i == next.Length - 1 ? " or " : ", ");
                    }
                    message.Append(NameOf(next[i]));
                }
                message.Append(".");
            }
            else
            {
                message.Append(fromName).Append(" is a final state. Reopening or regrading a closed case is a privileged correction that requires a reason (AD-031).");
            }

            return message.ToString();
        }
    }
}
