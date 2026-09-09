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

            var action = service.Retrieve(ActionEntity, actionId, new ColumnSet(AssignedContactAttr, ClockStartedOnAttr));
            var contact = action.GetAttributeValue<EntityReference>(AssignedContactAttr);

            var idempotencyKey = CompletionKey(actionId, action.GetAttributeValue<DateTime?>(ClockStartedOnAttr));

            var details = "Completed from the portal by contact " + Describe(contact) + ".";

            CompleteRemediationPlugin.Complete(
                service,
                actionId,
                idempotencyKey,
                expectedRowVersion: null,
                // The adviser, not the caller: a Power Pages write reaches Dataverse as the
                // site's application user (AD-053), the same reason SubmitRequestPlugin names
                // the assigned contact as the actor of a portal submit.
                actorId: contact == null ? context.InitiatingUserId : contact.Id,
                correlationId: context.CorrelationId,
                requireCallerOwnsAction: false,
                details: details);
        }

        private const string ClockStartedOnAttr = "al_clockstartedon";

        /// <summary>
        /// One intent per completion ROUND, so a retry after a dropped response replays the
        /// original completion instead of writing a second Audit Event (NFR-REL-01) — and a
        /// second round is a new intent.
        ///
        /// The key used to be the action id alone. That made the adviser's completion after a
        /// rejected sign-off replay the FIRST completion: <c>Complete</c> found the earlier
        /// audit event under the same key and answered success without writing anything, so
        /// the action stayed In progress, the case stayed at Awaiting Remediation, and the
        /// BR-008 rework loop could not be closed from the portal. A rejection restarts the
        /// clock by writing al_clockstartedon (OD-018, SignoffProgressPlugin.ReopenedAction),
        /// so that timestamp is what distinguishes the rounds. The first round keeps the
        /// original key shape, so completions already recorded in an environment still
        /// replay as the completions they were.
        /// </summary>
        public static string CompletionKey(Guid actionId, DateTime? clockStartedOn)
        {
            var key = "portal-complete-" + actionId.ToString("N");
            if (clockStartedOn.HasValue)
            {
                key += "-" + clockStartedOn.Value.ToUniversalTime().ToString("yyyyMMddHHmmssfff");
            }

            return key;
        }

        private static string Describe(EntityReference contact)
        {
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
