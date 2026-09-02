using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Portal completion of a remediation action, without a cloud flow. Registered as a
    /// synchronous post-operation step on Update of <c>al_remediationaction</c>, filtered to
    /// <c>al_completerequested</c>.
    ///
    /// The adviser half of the BR-008 loop, which AD-045 puts in the portal. Same mechanic
    /// as AD-073: a page cannot call the <c>al_CompleteRemediation</c> action, so the
    /// adviser sets one allowlisted boolean and the work happens here. All the rules live
    /// in <see cref="CompleteRemediationPlugin.Complete"/>, which the Custom API also calls.
    ///
    /// Authorization is not checked against the caller, for the reason AD-053 gives: Power
    /// Pages Web API writes arrive under the site's application user. The boundary is the
    /// Contact-scoped <c>Remediation Action - assigned to me</c> permission (AD-069).
    /// </summary>
    public class CompleteRequestPlugin : PluginBase
    {
        private const string ActionEntity = "al_remediationaction";
        private const string CompleteRequestedAttr = "al_completerequested";
        private const string AssignedContactAttr = "al_assignedcontactid";

        public CompleteRequestPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CompleteRequestPlugin))
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
            if (entity == null || !string.Equals(entity.LogicalName, ActionEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Only a write that sets the flag true is a completion request. Clearing it, or
            // an unrelated update that carries the column, is not.
            if (!entity.Contains(CompleteRequestedAttr) || !entity.GetAttributeValue<bool>(CompleteRequestedAttr))
            {
                return;
            }

            var actionId = entity.Id;

            // One intent per action; a retry replays rather than writing a second Audit Event.
            var idempotencyKey = "portal-complete-" + actionId.ToString("N");

            var details = "Completed from the portal by contact " + DescribeAssignedContact(service, actionId) + ".";

            CompleteRemediationPlugin.Complete(
                service,
                actionId,
                idempotencyKey,
                expectedRowVersion: null,
                actorId: context.InitiatingUserId,
                correlationId: context.CorrelationId,
                requireCallerOwnsAction: false,
                details: details);
        }

        private static string DescribeAssignedContact(IOrganizationService service, Guid actionId)
        {
            var action = service.Retrieve(ActionEntity, actionId, new ColumnSet(AssignedContactAttr));
            var contact = action.GetAttributeValue<EntityReference>(AssignedContactAttr);
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
