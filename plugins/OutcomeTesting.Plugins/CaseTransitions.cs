using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Applies the AD-057 case lifecycle to a real case row. <see cref="CaseLifecycle"/>
    /// decides whether a transition is legal; this moves the case and refuses the ones that
    /// are not, so the guard is written once rather than per command.
    ///
    /// Extracted from SubmitReviewPlugin once UpdateCaseDetailsPlugin needed the same
    /// refusal: two commands writing al_casestatus is two places for the lifecycle to be
    /// enforced differently, and AD-057 exists because it was previously enforced nowhere.
    /// </summary>
    public static class CaseTransitions
    {
        private const string CaseEntity = "al_outcomecase";
        private const string CaseStatus = "al_casestatus";

        /// <summary>
        /// Refuses a transition the lifecycle does not describe (AD-057), in the wording the
        /// commands already use. <paramref name="from"/> is nullable because a case whose
        /// status was never set has no current state to move from.
        /// </summary>
        public static void EnsureAllowed(int? from, int to)
        {
            if (!CaseLifecycle.IsAllowed(from, to))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + CaseLifecycle.DescribeRefusal(from, to));
            }
        }

        /// <summary>The case's current status, or null where it has never been set.</summary>
        public static int? CurrentStatus(IOrganizationService service, Guid caseId)
        {
            var outcomeCase = service.Retrieve(CaseEntity, caseId, new ColumnSet(CaseStatus));
            var current = outcomeCase.GetAttributeValue<OptionSetValue>(CaseStatus);
            return current != null ? current.Value : (int?)null;
        }

        /// <summary>
        /// Moves the case one hop, refusing any transition the lifecycle does not describe
        /// (AD-057). A hop that is already satisfied is a no-op, so a re-run is harmless.
        /// </summary>
        public static void MoveThrough(IOrganizationService service, Guid caseId, int nextStatus)
        {
            var from = CurrentStatus(service, caseId);

            if (from.HasValue && from.Value == nextStatus)
            {
                return;
            }

            EnsureAllowed(from, nextStatus);

            service.Update(new Entity(CaseEntity, caseId)
            {
                [CaseStatus] = new OptionSetValue(nextStatus),
            });
        }

        /// <summary>
        /// Moves the case through an ordered sequence of hops, one at a time. Called with a
        /// sequence rather than a destination because the lifecycle is a sequence and
        /// skipping a state is exactly what AD-057 exists to prevent; each hop is checked in
        /// turn against the status the previous hop left behind.
        /// </summary>
        public static void MoveThrough(IOrganizationService service, Guid caseId, IEnumerable<int> hops)
        {
            if (hops == null)
            {
                throw new ArgumentNullException(nameof(hops));
            }

            foreach (var hop in hops)
            {
                MoveThrough(service, caseId, hop);
            }
        }
    }
}
