using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Portal self-claim, without an association the browser is not allowed to make.
    /// Registered as a synchronous post-operation step on Update of <c>contact</c>, filtered
    /// to <c>al_claimrequest</c>.
    ///
    /// The page used to POST an <c>al_caseassignment</c> carrying two <c>@odata.bind</c>
    /// lookups. Power Pages refused it with 90040106
    /// (<c>TablePermissionAppendToIsMissingDuringAssociationChange</c>) naming
    /// <c>al_outcomecase</c>, and kept refusing it through three separate permission repairs:
    /// setting <c>AppendTo</c> on <c>Outcome Case - read all</c>, adding a <c>contact</c>
    /// permission, and granting the Tax and AQS Reviewer web roles on the case permission so
    /// both sides of the association were held by the same role. All three were verified
    /// deployed and the refusal did not change.
    ///
    /// That is the same wall the answering module hit: Power Pages refuses an
    /// <c>@odata.bind</c> association - and a fail reason's <c>$ref</c> associate - whatever
    /// the table permissions say, which is why the browser no longer creates an
    /// <c>al_response</c> either (AD-053). This applies that same resolution to the last
    /// browser-side association left on the site: the page writes one text column, and the
    /// association happens here, as the application user, where no portal table permission
    /// is consulted at all.
    ///
    /// <b>Authorization is stronger than the model it replaces, not weaker.</b> Previously
    /// the boundary was a Contact-scoped permission on <c>al_caseassignment</c>, which still
    /// trusted the browser to name the right contact in its payload. Here the claimant is
    /// <see cref="IPluginExecutionContext.PrimaryEntityId"/> - the contact row that was
    /// actually written - and the site's <c>contact</c> permission is Self-scoped, so a
    /// signed-in user can only ever write their own. The identity comes from the platform
    /// rather than from anything the page sent.
    ///
    /// Nothing about the claim's rules lives here. The assignment is created with only its
    /// two lookups, exactly as the browser used to send it, so
    /// <see cref="ClaimCasePlugin"/> - still a pre-operation step on Create of
    /// <c>al_caseassignment</c> - runs unchanged: the queue check, the route check, the
    /// review instance, the release of prior assignments and the audit event. So does
    /// <see cref="NotificationEmitterPlugin"/>, which is what puts the allocation email back.
    /// A row without <c>al_assigneduserid</c> is what ClaimCasePlugin treats as a portal
    /// claim, and this one deliberately carries none.
    /// </summary>
    public class ClaimRequestPlugin : PluginBase
    {
        private const string ContactEntity = "contact";
        private const string AssignmentEntity = "al_caseassignment";
        private const string ClaimRequestAttr = "al_claimrequest";
        private const string CaseLookup = "al_outcomecaseid";
        private const string AssignedContactAttr = "al_assignedcontactid";
        private const string CaseEntity = "al_outcomecase";

        public ClaimRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ClaimRequestPlugin))
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
            if (entity == null || !string.Equals(entity.LogicalName, ContactEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!entity.Contains(ClaimRequestAttr))
            {
                return;
            }

            // The clear-down below writes this column back as empty, which re-enters this
            // step once at depth 2. An empty request is not a claim, so it stops here rather
            // than needing a depth check.
            var raw = entity.GetAttributeValue<string>(ClaimRequestAttr);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            Guid caseId;
            if (!Guid.TryParse(raw.Trim(), out caseId) || caseId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "That is not a case this site can pick up.");
            }

            // The contact whose row was written, never a value the page supplied. The Self
            // scope on the site's contact permission is what makes this the signed-in user.
            var contactId = context.PrimaryEntityId;

            // Cleared before the assignment is created, so the column never keeps a case id
            // after the intent has been acted on and a retry of the same case is a real
            // second write rather than an unchanged value Dataverse may not raise an Update
            // for. Both writes share this transaction: if the claim below is refused, the
            // clear is rolled back with it and the page can simply try again.
            service.Update(new Entity(ContactEntity, contactId)
            {
                [ClaimRequestAttr] = null,
            });

            // Only the two lookups, which is exactly the shape the browser used to POST.
            // Everything else - the business code, the assigned user, the timestamp, the
            // active flag - is stamped by ClaimCasePlugin, so a claim still cannot be
            // backdated or given a forged key.
            service.Create(new Entity(AssignmentEntity)
            {
                [CaseLookup] = new EntityReference(CaseEntity, caseId),
                [AssignedContactAttr] = new EntityReference(ContactEntity, contactId),
            });
        }
    }
}
