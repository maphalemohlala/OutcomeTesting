using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who may see a case, decided from what the case is (AD-218, AR-01 to AR-04).
    ///
    /// Power Pages can narrow a read only through a lookup to the signed-in contact, a lookup
    /// to their account, or a parent chain. So "who may see this case" has to be written ONTO
    /// the case, and this is the one place that decides what is written. The reconciler reads
    /// the case, asks this, and writes the difference.
    ///
    /// Pure, with no service: every rule the portal permissions rest on is a unit test.
    /// </summary>
    public static class CaseAccess
    {
        /// <summary>
        /// The statuses at which a case with remediation is released to its adviser. Closed is
        /// included because the adviser keeps a case after sign-off (answer 2a); a Pass case
        /// reaches Closed too, which is why release also requires a remedial action.
        /// </summary>
        private static readonly int[] ReleaseStatuses =
        {
            CaseLifecycle.AwaitingRemediation,
            CaseLifecycle.RemediationInProgress,
            CaseLifecycle.AwaitingSignoff,
            CaseLifecycle.AwaitingRecheck,
            CaseLifecycle.Closed,
        };

        public static CaseAccessResult Decide(CaseAccessInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            var reviews = Active(input);

            var result = new CaseAccessResult
            {
                TaxChecker = CheckerFor(reviews, ResponseRules.ReviewTypeTax),
                AqsChecker = CheckerFor(reviews, ResponseRules.ReviewTypeAqs),

                // Answer 4c: both managers see a Tax-then-AQS case for its whole life, so a
                // team keeps a case its discipline has ever worked, whatever the route says now.
                ShareWithTaxTeam = input.RouteRequiresTax || reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeTax),
                ShareWithAqsTeam = input.RouteRequiresAqs || reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeAqs),
            };

            if (InAqsQueue(input))
            {
                result.QueueAccount = input.AqsQueueAccount;
                result.QueuedOn = input.CurrentQueuedOn ?? input.Now;
            }

            if (IsReleased(input))
            {
                // Never guessed (AD-082): an unmatched adviser leaves the case released to
                // nobody, and the reconciler reports it.
                result.Adviser = input.ResolvedAdviser;
                result.Supervisor = input.ResolvedSupervisor;
            }

            return result;
        }

        /// <summary>
        /// Waiting for an AQS checker: Queued, the route needs AQS, nobody holds the AQS review,
        /// and any Tax check the route needs is submitted (BR-004). The same test the portal's
        /// queue used to make in FetchXML, moved here so the queue can be a permission.
        /// </summary>
        public static bool InAqsQueue(CaseAccessInput input)
        {
            if (input.CaseStatus != CaseLifecycle.Queued || !input.RouteRequiresAqs)
            {
                return false;
            }

            var reviews = Active(input);
            if (reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeAqs && r.AssignedContactId.HasValue))
            {
                return false;
            }

            return !input.RouteRequiresTax
                || reviews.Any(r => r.ReviewType == ResponseRules.ReviewTypeTax && r.Submitted);
        }

        /// <summary>
        /// Released to the adviser (AR-04, answer 2): the case has a remedial action, sits in the
        /// remediation range, and owes no unsubmitted review, so no draft finding is ever visible.
        /// </summary>
        public static bool IsReleased(CaseAccessInput input)
        {
            if (!input.HasRemediation || !input.CaseStatus.HasValue)
            {
                return false;
            }

            if (Array.IndexOf(ReleaseStatuses, input.CaseStatus.Value) < 0)
            {
                return false;
            }

            return Active(input).All(r => r.Submitted);
        }

        private static List<ReviewFact> Active(CaseAccessInput input)
        {
            return (input.Reviews ?? new List<ReviewFact>()).Where(r => r != null && r.Active).ToList();
        }

        /// <summary>The contact on the latest review of a discipline, by sequence.</summary>
        private static EntityReference CheckerFor(IList<ReviewFact> reviews, int reviewType)
        {
            var holder = reviews
                .Where(r => r.ReviewType == reviewType && r.AssignedContactId.HasValue)
                .OrderByDescending(r => r.Sequence)
                .FirstOrDefault();

            return holder == null ? null : new EntityReference("contact", holder.AssignedContactId.Value);
        }
    }

    /// <summary>One review instance, as far as access is concerned.</summary>
    public sealed class ReviewFact
    {
        public int ReviewType { get; set; }

        public Guid? AssignedContactId { get; set; }

        public bool Submitted { get; set; }

        public bool Active { get; set; } = true;

        public int Sequence { get; set; }
    }

    public sealed class CaseAccessInput
    {
        public int? CaseStatus { get; set; }

        public bool RouteRequiresTax { get; set; }

        public bool RouteRequiresAqs { get; set; }

        public IList<ReviewFact> Reviews { get; set; } = new List<ReviewFact>();

        public bool HasRemediation { get; set; }

        public EntityReference ResolvedAdviser { get; set; }

        public EntityReference ResolvedSupervisor { get; set; }

        public EntityReference AqsQueueAccount { get; set; }

        public DateTime? CurrentQueuedOn { get; set; }

        public DateTime Now { get; set; }
    }

    public sealed class CaseAccessResult
    {
        public EntityReference TaxChecker { get; set; }

        public EntityReference AqsChecker { get; set; }

        public EntityReference QueueAccount { get; set; }

        public DateTime? QueuedOn { get; set; }

        public EntityReference Adviser { get; set; }

        public EntityReference Supervisor { get; set; }

        public bool ShareWithTaxTeam { get; set; }

        public bool ShareWithAqsTeam { get; set; }
    }
}
