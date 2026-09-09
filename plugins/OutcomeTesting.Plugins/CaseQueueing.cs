using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// AD-093: a case that has a review route and sits at Imported or Ready for Allocation
    /// belongs in the shared queue. Nothing else in the solution moves a case into Queued
    /// except a hand-back from a Tax check or a sign-off, so without this the AD-076
    /// self-service queue only ever showed returned work and every fresh case waited for a
    /// team lead to edit its status by hand.
    ///
    /// The rule is about the state the case is in, not the field that changed: an edit to
    /// any detail of a routed case still at Ready for Allocation queues it. Hops go through
    /// <see cref="CaseTransitions.MoveThrough(IOrganizationService, Guid, IEnumerable{int})"/>
    /// so a skipped state is refused the same way as anywhere else (AD-057).
    /// </summary>
    public static class CaseQueueing
    {
        private static readonly int[] NoHops = new int[0];

        /// <summary>Whether a case in this state, with or without a route, should be queued.</summary>
        public static bool ShouldQueue(int? status, bool hasRoute)
        {
            if (!hasRoute || !status.HasValue)
            {
                return false;
            }

            return status.Value == CaseLifecycle.Imported || status.Value == CaseLifecycle.ReadyForAllocation;
        }

        /// <summary>The hops from this status to Queued, in order; empty when there are none.</summary>
        public static int[] HopsToQueue(int? status)
        {
            if (!status.HasValue)
            {
                return NoHops;
            }

            switch (status.Value)
            {
                case CaseLifecycle.Imported:
                    return new[] { CaseLifecycle.ReadyForAllocation, CaseLifecycle.Queued };
                case CaseLifecycle.ReadyForAllocation:
                    return new[] { CaseLifecycle.Queued };
                default:
                    return NoHops;
            }
        }

        /// <summary>
        /// Queues the case when <see cref="ShouldQueue"/> says so, appending one line to
        /// <paramref name="changes"/> in the same shape the case-edit command writes for a
        /// status change, so the case history reads the same whether a person or the rule
        /// moved it. Returns whether the case moved.
        /// </summary>
        public static bool QueueIfRouted(
            IOrganizationService service, Guid caseId, int? status, bool hasRoute, List<string> changes)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            if (!ShouldQueue(status, hasRoute))
            {
                return false;
            }

            CaseTransitions.MoveThrough(service, caseId, HopsToQueue(status));

            changes.Add("Status " + CaseLifecycle.NameOf(status.Value) + " -> " + CaseLifecycle.NameOf(CaseLifecycle.Queued)
                + " (queued automatically: route set, AD-093)");
            return true;
        }
    }
}
