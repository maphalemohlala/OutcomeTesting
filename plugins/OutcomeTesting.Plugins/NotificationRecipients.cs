using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who a letter goes to, when that is chosen as data rather than written into the code
    /// that sends it (AD-168).
    ///
    /// <para>
    /// Every built-in letter still computes its own recipient at the point it is raised, and
    /// that is unchanged. What this adds is a way for an administrator to say "this letter
    /// goes to the T&amp;C Manager instead", and to point a letter they invented at one of the
    /// five people a case knows about.
    /// </para>
    /// <para>
    /// <b>A kind is resolved against the case, never against the caller.</b> "Checker" means
    /// the person the case is allocated to, not whoever happened to trigger the save - the two
    /// are usually the same and the difference only shows on an import or an administrator's
    /// correction, which is exactly when a letter to the wrong person would be least noticed.
    /// </para>
    /// <para>
    /// <b>Nothing here is a permission.</b> Naming somebody as a recipient tells them a thing
    /// happened; it gives them no access to the case, and the portal's table permissions are
    /// unchanged by any of it. This is the same boundary <see cref="TcManagerRouting"/> draws
    /// and the reason its type carries a reason rather than a role.
    /// </para>
    /// </summary>
    public static class NotificationRecipients
    {
        /// <summary>The adviser named on the case.</summary>
        public const int KindAdviser = 120910810;

        /// <summary>The T&amp;C Manager the adviser is mapped to (AD-162).</summary>
        public const int KindTcManager = 120910811;

        /// <summary>The para-planner named on the case, matched to a contact (AD-161).</summary>
        public const int KindParaplanner = 120910812;

        /// <summary>The checker the case or review is allocated to.</summary>
        public const int KindChecker = 120910813;

        /// <summary>One named contact, the same for every case.</summary>
        public const int KindContact = 120910814;

        private const string CaseEntity = "al_outcomecase";
        private const string ReviewEntity = "al_reviewinstance";
        private const string AssignmentEntity = "al_caseassignment";
        private const string CaseLookup = "al_outcomecaseid";
        private const string AssignedContact = "al_assignedcontactid";

        /// <summary>What an administrator sees in a list.</summary>
        public static string Name(int kind)
        {
            switch (kind)
            {
                case KindAdviser: return "Adviser";
                case KindTcManager: return "T&C Manager";
                case KindParaplanner: return "Para-planner";
                case KindChecker: return "Checker";
                case KindContact: return "A named contact";
                default: return kind.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Whether this is one of the five kinds.</summary>
        public static bool IsKnown(int kind)
        {
            return kind == KindAdviser
                || kind == KindTcManager
                || kind == KindParaplanner
                || kind == KindChecker
                || kind == KindContact;
        }

        /// <summary>
        /// The address for a kind, or null when the case does not name that person or the
        /// person it names cannot be reached.
        ///
        /// <para>
        /// Never throws, and null is a real answer rather than a failure: a case with no
        /// para-planner is ordinary. The caller queues the letter without a recipient and the
        /// drain refuses to send it, which is the behaviour every unrouted letter has had
        /// since OD-030 - a row somebody can look at, rather than an invented address.
        /// </para>
        /// </summary>
        public static string EmailFor(
            IOrganizationService service,
            int kind,
            EntityReference caseRef,
            EntityReference reviewRef,
            EntityReference contact)
        {
            try
            {
                switch (kind)
                {
                    case KindContact:
                        return NotificationOutbox.ContactEmail(service, contact);

                    case KindAdviser:
                        return AdviserEmail(service, caseRef);

                    case KindTcManager:
                        var routing = TcManagerRouting.ForCase(service, caseRef);
                        return routing == null ? null : routing.Email;

                    case KindParaplanner:
                        return NotificationOutbox.ParaplannerEmail(service, caseRef);

                    case KindChecker:
                        return CheckerEmail(service, caseRef, reviewRef);

                    default:
                        return null;
                }
            }
            catch (Exception)
            {
                // This runs inside the transaction that caused the notification. A recipient
                // that cannot be worked out costs the letter, never the save.
                return null;
            }
        }

        /// <summary>The case a notification's target belongs to, or null.</summary>
        public static EntityReference CaseOf(
            IOrganizationService service, string targetTable, Guid targetId)
        {
            if (targetId == Guid.Empty || string.IsNullOrWhiteSpace(targetTable))
            {
                return null;
            }

            if (string.Equals(targetTable, CaseEntity, StringComparison.OrdinalIgnoreCase))
            {
                return new EntityReference(CaseEntity, targetId);
            }

            try
            {
                // Every table a notification targets carries the case lookup under the same
                // name, so this does not need a table-by-table switch that a new target would
                // silently fall out of.
                var row = service.Retrieve(targetTable, targetId, new ColumnSet(CaseLookup));
                return row.GetAttributeValue<EntityReference>(CaseLookup);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The review a notification's target is, where it is one.</summary>
        public static EntityReference ReviewOf(string targetTable, Guid targetId)
        {
            return targetId != Guid.Empty
                && string.Equals(targetTable, ReviewEntity, StringComparison.OrdinalIgnoreCase)
                    ? new EntityReference(ReviewEntity, targetId)
                    : null;
        }

        private static string AdviserEmail(IOrganizationService service, EntityReference caseRef)
        {
            if (caseRef == null)
            {
                return null;
            }

            var row = service.Retrieve(
                CaseEntity, caseRef.Id, new ColumnSet(TcManagerRouting.CaseAdviserEmailAttr));
            var email = row.GetAttributeValue<string>(TcManagerRouting.CaseAdviserEmailAttr);
            return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        }

        /// <summary>
        /// The checker a review is allocated to, or - where the letter is about the case
        /// rather than one review - the checker holding the case.
        ///
        /// Read from the allocation, never from <c>al_checkername</c>. That column arrives on
        /// the upload file and names whoever the spreadsheet said, so it is not evidence that
        /// anybody has actually been given the case.
        /// </summary>
        private static string CheckerEmail(
            IOrganizationService service, EntityReference caseRef, EntityReference reviewRef)
        {
            if (reviewRef != null)
            {
                var review = service.Retrieve(
                    ReviewEntity, reviewRef.Id, new ColumnSet(AssignedContact));
                var assigned = review.GetAttributeValue<EntityReference>(AssignedContact);
                if (assigned != null)
                {
                    return NotificationOutbox.ContactEmail(service, assigned);
                }
            }

            if (caseRef == null)
            {
                return null;
            }

            var query = new QueryExpression(AssignmentEntity)
            {
                ColumnSet = new ColumnSet(AssignedContact),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition(CaseLookup, ConditionOperator.Equal, caseRef.Id);
            query.Criteria.AddCondition("al_isactive", ConditionOperator.Equal, true);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("al_assignedon", OrderType.Descending);

            var rows = service.RetrieveMultiple(query).Entities;
            if (rows.Count == 0)
            {
                return null;
            }

            return NotificationOutbox.ContactEmail(
                service, rows[0].GetAttributeValue<EntityReference>(AssignedContact));
        }
    }
}
