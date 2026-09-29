using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Parks the checker's remedial actions on the review, from the portal (project owner,
    /// 2026-09-29).
    ///
    /// The portal cannot invoke a Custom API (AD-053), so the page PATCHes this JSON onto the
    /// signed-in contact's own row - the Self-scoped contact permission makes that the only
    /// contact it can reach - and this plug-in does the write. The same shape as the claim,
    /// the sign-off, the regrade, the case header and "Who carries this fail".
    ///
    /// The guard is the review: open, and assigned to that contact. Tighter than the case
    /// header's "any open review on this case", because the words belong to one check and the
    /// checker doing it is the only person who should be writing them.
    ///
    /// No audit event: nothing has been decided yet. The checker is filling in a form they have
    /// not submitted, like every answer above it; the submit is what makes it a record.
    ///
    /// Register with:
    /// <c>registerstep &lt;orgUrl&gt; OutcomeTesting.Plugins.RemedialActionsRequestPlugin Update contact 40 al_remedialactionsrequest sync</c>.
    /// </summary>
    public class RemedialActionsRequestPlugin : PluginBase
    {
        public const string RequestAttr = "al_remedialactionsrequest";

        private const string ContactEntity = "contact";
        private const string ReviewEntity = "al_reviewinstance";

        public RemedialActionsRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RemedialActionsRequestPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            object target;
            if (!context.InputParameters.TryGetValue("Target", out target))
            {
                return;
            }

            var entity = target as Entity;
            if (entity == null
                || !string.Equals(entity.LogicalName, ContactEntity, StringComparison.OrdinalIgnoreCase)
                || !entity.Contains(RequestAttr))
            {
                return;
            }

            var raw = entity.GetAttributeValue<string>(RequestAttr);
            if (string.IsNullOrWhiteSpace(raw))
            {
                // The clear below comes back through the pipeline as an update to null.
                return;
            }

            var payload = RemedialActionsRequestPayload.Parse(raw);
            if (payload == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The remedial actions could not be read.");
            }

            Apply(service, context.PrimaryEntityId, payload);
        }

        /// <summary>
        /// Validates the request against the review and parks it. Public and static so the rule
        /// is testable without a plug-in context. Every save carries the page's whole map, so
        /// this replaces what was parked rather than merging into it.
        /// </summary>
        public static void Apply(IOrganizationService service, Guid contactId, RemedialActionsRequestPayload payload)
        {
            Guid reviewId;
            if (payload == null || !Guid.TryParse(payload.ReviewId, out reviewId) || reviewId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The remedial actions must name the check they belong to.");
            }

            var review = service.Retrieve(ReviewEntity, reviewId, new ColumnSet("al_assignedcontactid", "al_submittedon"));

            if (review.GetAttributeValue<DateTime?>("al_submittedon").HasValue)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This check has been submitted, so its remedial actions can no longer be changed here.");
            }

            var assigned = review.GetAttributeValue<EntityReference>("al_assignedcontactid");
            if (assigned == null || assigned.Id != contactId)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Only the checker assigned to this check can write its remedial actions.");
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in payload.Actions ?? new List<RemedialActionEntry>())
            {
                if (entry == null)
                {
                    continue;
                }

                var item = (entry.Item ?? string.Empty).Trim();
                var text = (entry.Text ?? string.Empty).Trim();
                if (item.Length == 0 || text.Length == 0)
                {
                    continue;
                }

                if (text.Length > RemedialActions.MaxLength)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "A remedial action can be at most "
                        + RemedialActions.MaxLength + " characters. Shorten the one for '" + item + "'.");
                }

                map[item] = text;
            }

            // Cleared first, in the same transaction: a refusal below rolls it back with it.
            service.Update(new Entity(ContactEntity, contactId) { [RequestAttr] = null });

            service.Update(new Entity(ReviewEntity, reviewId)
            {
                [RemedialActions.PendingAttr] = RemedialActions.Serialise(map),
            });
        }
    }
}
