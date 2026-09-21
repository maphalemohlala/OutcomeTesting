using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who the T&amp;C Manager is for a case, for ROUTING only (Fixes 5, AD-162).
    ///
    /// <para>
    /// <b>This type still decides nothing.</b> It answers "who is the T&amp;C Manager for this
    /// case" and returns a recipient and a reason - no role, no permission, no boolean anybody
    /// could mistake for "may sign off". Every caller here draws its own conclusion.
    /// </para>
    /// <para>
    /// <b>What that answer is used for changed on 2026-09-21.</b> Until then it decided only
    /// who was TOLD a sign-off was waiting: every T&amp;C Manager could attest to every case,
    /// which is the Phase 1 model, and this header said so in as many words. The owner
    /// reversed it on finding that a service account holding the supervisor role could sign
    /// off a case whose adviser it supervises nothing of. <see cref="SignoffRequestPlugin.EnsureMappedToCase"/>
    /// now compares the signatory against this mapping. <b>Reading stays open to every
    /// supervisor; attesting does not.</b>
    /// </para>
    /// <para>
    /// The consequence to keep in view: an adviser with no mapping has no one who may sign
    /// their cases off. That is intended - an attestation needs a supervisor - but it makes
    /// <c>al_advisermapping</c> operational data rather than notification convenience, and an
    /// environment that has not populated it cannot complete a remediation at all (F58).
    /// </para>
    /// <para>
    /// Resolved through <c>al_advisermapping</c>, keyed on the adviser's work EMAIL as the
    /// case carries it from the extract. An email is exact. The para-planner is matched by
    /// name only because nothing better exists on that side, and <see cref="NotificationOutbox.MatchParaplanner"/>
    /// has to fail loudly because of it; there was no reason to repeat that weakness where a
    /// strong key was already on the row.
    /// </para>
    /// </summary>
    public static class TcManagerRouting
    {
        /// <summary>The mapping table, keyed on adviser email.</summary>
        public const string MappingEntity = "al_advisermapping";

        /// <summary>The adviser's work email on the mapping, and the alternate key.</summary>
        public const string MappingEmailAttr = "al_adviseremail";

        /// <summary>The T&amp;C Manager contact the mapping points at.</summary>
        public const string ManagerAttr = "al_tcmanagerid";

        /// <summary>The adviser's work email as the case carries it from the extract.</summary>
        public const string CaseAdviserEmailAttr = "al_adviseremail";

        private const string CaseEntity = "al_outcomecase";

        /// <summary>Why a case did or did not route to a T&amp;C Manager.</summary>
        public enum RoutingKind
        {
            /// <summary>The case carries no adviser email, so there is nothing to map from.</summary>
            NoAdviserEmail,

            /// <summary>No mapping exists for that adviser.</summary>
            NoMapping,

            /// <summary>A mapping exists but its T&amp;C Manager has no work email.</summary>
            ManagerNotReachable,

            /// <summary>A mapped T&amp;C Manager with a work email.</summary>
            Matched,
        }

        /// <summary>The outcome of routing a case to its T&amp;C Manager, with words for a log.</summary>
        public sealed class Routing
        {
            /// <summary>What happened.</summary>
            public RoutingKind Kind { get; set; }

            /// <summary>The T&amp;C Manager contact, set whenever a mapping was found.</summary>
            public EntityReference Manager { get; set; }

            /// <summary>Their work email, set only when <see cref="IsRouted"/>.</summary>
            public string Email { get; set; }

            /// <summary>One sentence naming the value that failed, for the trace log.</summary>
            public string Reason { get; set; }

            /// <summary>True only when there is somebody to send to.</summary>
            public bool IsRouted
            {
                get { return Kind == RoutingKind.Matched; }
            }
        }

        /// <summary>
        /// The T&amp;C Manager for this case, or why there is none.
        ///
        /// <para>
        /// Never throws and never guesses. A case whose adviser has no mapping is a
        /// configuration gap, not a reason to fail the adviser's own completion - so the
        /// caller carries on and the reason goes to the trace log, the same trade
        /// <see cref="NotificationOutbox.ParaplannerEmail"/> makes on the other side.
        /// </para>
        /// </summary>
        public static Routing ForCase(IOrganizationService service, EntityReference caseRef)
        {
            if (caseRef == null)
            {
                return new Routing
                {
                    Kind = RoutingKind.NoAdviserEmail,
                    Reason = "No case was given, so no T&C Manager could be resolved.",
                };
            }

            var row = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet(CaseAdviserEmailAttr));
            return ForAdviserEmail(service, row.GetAttributeValue<string>(CaseAdviserEmailAttr));
        }

        /// <summary>The T&amp;C Manager mapped to this adviser email, or why there is none.</summary>
        public static Routing ForAdviserEmail(IOrganizationService service, string adviserEmail)
        {
            if (string.IsNullOrWhiteSpace(adviserEmail))
            {
                return new Routing
                {
                    Kind = RoutingKind.NoAdviserEmail,
                    Reason = "The case carries no adviser email, so no T&C Manager can be mapped to it.",
                };
            }

            var trimmed = adviserEmail.Trim();

            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet(ManagerAttr),
                // One, not two: al_adviseremail is the table's alternate key, so a second
                // row of the same email cannot exist to be ambiguous about. That is the
                // whole reason the key is on the table rather than only in a comment.
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(MappingEmailAttr, ConditionOperator.Equal, trimmed);

            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return new Routing
                {
                    Kind = RoutingKind.NoMapping,
                    Reason = "No T&C Manager is mapped to the adviser " + trimmed + ".",
                };
            }

            var manager = found[0].GetAttributeValue<EntityReference>(ManagerAttr);
            var email = NotificationOutbox.ContactEmail(service, manager);
            if (string.IsNullOrWhiteSpace(email))
            {
                return new Routing
                {
                    Kind = RoutingKind.ManagerNotReachable,
                    Manager = manager,
                    Reason = "The T&C Manager mapped to " + trimmed + " has no work email.",
                };
            }

            return new Routing { Kind = RoutingKind.Matched, Manager = manager, Email = email };
        }
    }
}
