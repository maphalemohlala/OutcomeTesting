using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Portal submission without a cloud flow. Registered as a synchronous post-operation
    /// step on Update of <c>al_reviewinstance</c>, filtered to <c>al_submitrequested</c>.
    ///
    /// The Portals Web API supports create and update but explicitly not actions, so a page
    /// cannot invoke the <c>al_SubmitReview</c> Custom API directly. The documented route was
    /// a Power Automate flow; this replaces it by letting the page set one boolean the site
    /// allowlists, and doing the work here. That is the AD-053 pattern the answering module
    /// already uses, so answering and submitting now reach Dataverse the same way and the
    /// portal needs no Power Automate licence.
    ///
    /// All the rules live in <see cref="SubmitReviewPlugin.Submit"/>, which the Custom API
    /// also calls. Nothing is reimplemented here.
    ///
    /// Authorization is deliberately not checked against the caller. Power Pages Web API
    /// writes arrive under the site's application user, so the caller is never the checker
    /// and an identity check would look like security while enforcing nothing (AD-053). The
    /// boundary is the Contact-scoped <c>Review Instance - assigned to me</c> table
    /// permission: reaching this plug-in at all means the platform already allowed the write
    /// on a review whose assigned contact is the signed-in user (AD-047, AD-056).
    /// </summary>
    public class SubmitRequestPlugin : PluginBase
    {
        private const string ReviewEntity = "al_reviewinstance";
        private const string SubmitRequestedAttr = "al_submitrequested";
        private const string AssignedContactAttr = "al_assignedcontactid";

        public SubmitRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SubmitRequestPlugin))
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
            if (entity == null || !string.Equals(entity.LogicalName, ReviewEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Only a write that sets the flag true is a submission. Clearing it, or any other
            // update that happens to carry the column, is not.
            if (!entity.Contains(SubmitRequestedAttr) || !entity.GetAttributeValue<bool>(SubmitRequestedAttr))
            {
                return;
            }

            var reviewId = entity.Id;

            // One intent per review, so a retry after a dropped response replays the original
            // submission instead of writing a second Audit Event (NFR-REL-01). The browser
            // cannot supply a stable key across page loads, so it is derived here.
            var idempotencyKey = "portal-submit-" + reviewId.ToString("N");

            // Recorded because the caller identity is the portal application user, not the
            // checker. Without the contact the trail would say only that "the portal" did it.
            var details = "Submitted from the portal by contact " + DescribeAssignedContact(service, reviewId) + ".";

            SubmitReviewPlugin.Submit(
                service,
                reviewId,
                idempotencyKey,
                expectedRowVersion: null,
                actorId: context.InitiatingUserId,
                correlationId: context.CorrelationId,
                requireCallerOwnsReview: false,
                details: details);
        }

        private static string DescribeAssignedContact(IOrganizationService service, Guid reviewId)
        {
            var review = service.Retrieve(ReviewEntity, reviewId, new ColumnSet(AssignedContactAttr));
            var contact = review.GetAttributeValue<EntityReference>(AssignedContactAttr);
            if (contact == null)
            {
                return "(none recorded)";
            }

            return string.IsNullOrEmpty(contact.Name)
                ? contact.Id.ToString("D")
                : contact.Name + " " + contact.Id.ToString("D");
        }
    }
}
