using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who the T&amp;C Managers are for a case, for ROUTING only (Fixes 5, AD-162).
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
    /// case carries it from the extract. Both people on a case are identified by email
    /// (AD-228).
    /// </para>
    /// <para>
    /// <b>An adviser may have several managers, all equal (owner, 2026-11-24)</b>, so that one
    /// being away never holds a sign-off up. Every active mapping row for the adviser names
    /// one of them; each is told, and any of them may attest. A manager chosen for one case
    /// (<see cref="OverrideAttr"/>) still replaces them all for that case.
    /// </para>
    /// </summary>
    public static class TcManagerRouting
    {
        /// <summary>The mapping table, keyed on adviser email.</summary>
        public const string MappingEntity = "al_advisermapping";

        /// <summary>The adviser's work email on the mapping; with the manager, the alternate key.</summary>
        public const string MappingEmailAttr = "al_adviseremail";

        /// <summary>The T&amp;C Manager contact the mapping points at.</summary>
        public const string ManagerAttr = "al_tcmanagerid";

        /// <summary>The adviser's work email as the case carries it from the extract.</summary>
        public const string CaseAdviserEmailAttr = "al_adviseremail";

        /// <summary>
        /// A T&amp;C Manager chosen for one case by an admin (requirements log row 4), for when the
        /// mapped manager is away. Empty means the adviser mapping decides.
        /// </summary>
        public const string OverrideAttr = "al_tcmanageroverrideid";

        private const string CaseEntity = "al_outcomecase";

        /// <summary>Why a case did or did not route to a T&amp;C Manager.</summary>
        public enum RoutingKind
        {
            /// <summary>The case carries no adviser email, so there is nothing to map from.</summary>
            NoAdviserEmail,

            /// <summary>No mapping exists for that adviser.</summary>
            NoMapping,

            /// <summary>Managers are mapped but none of them has a work email.</summary>
            ManagerNotReachable,

            /// <summary>At least one mapped T&amp;C Manager has a work email.</summary>
            Matched,
        }

        /// <summary>One manager who can be written to.</summary>
        public sealed class Recipient
        {
            /// <summary>The T&amp;C Manager contact.</summary>
            public EntityReference Manager { get; set; }

            /// <summary>Their work email.</summary>
            public string Email { get; set; }
        }

        /// <summary>The outcome of routing a case to its T&amp;C Managers, with words for a log.</summary>
        public sealed class Routing
        {
            /// <summary>What happened.</summary>
            public RoutingKind Kind { get; set; }

            /// <summary>Every manager the case resolves to, reachable or not. Empty when none.</summary>
            public List<EntityReference> Managers { get; set; } = new List<EntityReference>();

            /// <summary>The managers with a work email, in the same order.</summary>
            public List<Recipient> Recipients { get; set; } = new List<Recipient>();

            /// <summary>One sentence naming the value that failed, for the trace log.</summary>
            public string Reason { get; set; }

            /// <summary>True only when there is somebody to send to.</summary>
            public bool IsRouted
            {
                get { return Kind == RoutingKind.Matched; }
            }

            /// <summary>Whether <paramref name="contactId"/> is one of the case's managers.</summary>
            public bool Includes(Guid contactId)
            {
                return Managers.Any(m => m.Id == contactId);
            }
        }

        /// <summary>
        /// The T&amp;C Managers for this case, or why there are none.
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

            // The stored adviser email only (AD-228). The name is never used to find one.
            var row = service.Retrieve(CaseEntity, caseRef.Id, new ColumnSet(CaseAdviserEmailAttr, OverrideAttr));

            // A manager chosen for this case alone (requirements log row 4) replaces the mapping.
            var chosen = row.GetAttributeValue<EntityReference>(OverrideAttr);
            if (chosen != null)
            {
                return Reach(service, new List<EntityReference> { chosen }, "the T&C Manager chosen for this case");
            }

            return ForAdviserEmail(service, CaseAdviser.EmailFor(row.GetAttributeValue<string>(CaseAdviserEmailAttr)));
        }

        /// <summary>The case's own T&amp;C Manager where an admin chose one, in place of the mapping.</summary>
        public static EntityReference ChosenFor(Entity caseRow)
        {
            return caseRow == null ? null : caseRow.GetAttributeValue<EntityReference>(OverrideAttr);
        }

        /// <summary>The T&amp;C Managers mapped to this adviser email, or why there are none.</summary>
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
            var managers = ManagersFor(service, trimmed);
            if (managers.Count == 0)
            {
                return new Routing
                {
                    Kind = RoutingKind.NoMapping,
                    Reason = "No T&C Manager is mapped to the adviser " + trimmed + ".",
                };
            }

            return Reach(service, managers, "the T&C Managers mapped to " + trimmed);
        }

        /// <summary>
        /// Every manager an ACTIVE mapping row names for this adviser email, once each, oldest
        /// mapping first so the order never changes under a reader. Empty for a blank email.
        /// </summary>
        public static List<EntityReference> ManagersFor(IOrganizationService service, string adviserEmail)
        {
            int? status;
            return ManagersFor(service, adviserEmail, out status);
        }

        /// <summary>
        /// The same read, also returning the adviser status OTIS holds on those rows
        /// (<see cref="AdviserStatus"/>), so one query answers both.
        /// </summary>
        public static List<EntityReference> ManagersFor(IOrganizationService service, string adviserEmail, out int? status)
        {
            var managers = new List<EntityReference>();
            status = null;
            if (string.IsNullOrWhiteSpace(adviserEmail))
            {
                return managers;
            }

            var query = new QueryExpression(MappingEntity)
            {
                ColumnSet = new ColumnSet(ManagerAttr, AdviserStatus.Attr),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(MappingEmailAttr, ConditionOperator.Equal, adviserEmail.Trim());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("createdon", OrderType.Ascending);

            foreach (var row in CommandHelpers.RetrieveAll(service, query))
            {
                var held = row.GetAttributeValue<OptionSetValue>(AdviserStatus.Attr);
                if (status == null && held != null)
                {
                    status = held.Value;
                }

                var manager = row.GetAttributeValue<EntityReference>(ManagerAttr);
                if (manager != null && !managers.Any(m => m.Id == manager.Id))
                {
                    managers.Add(manager);
                }
            }

            return managers;
        }

        /// <summary>The managers with their work emails; one with none is kept but not written to.</summary>
        private static Routing Reach(IOrganizationService service, List<EntityReference> managers, string who)
        {
            var routing = new Routing { Managers = managers };
            foreach (var manager in managers)
            {
                var email = NotificationOutbox.ContactEmail(service, manager);
                if (!string.IsNullOrWhiteSpace(email))
                {
                    routing.Recipients.Add(new Recipient { Manager = manager, Email = email });
                }
            }

            routing.Kind = routing.Recipients.Count > 0 ? RoutingKind.Matched : RoutingKind.ManagerNotReachable;
            if (routing.Recipients.Count < managers.Count)
            {
                routing.Reason = routing.Recipients.Count == 0
                    ? "None of " + who + " has a work email."
                    : (managers.Count - routing.Recipients.Count) + " of " + who + " has no work email.";
            }

            return routing;
        }
    }
}
