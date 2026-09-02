using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The consequences of a sign-off (BR-008, FR-023). Registered as a synchronous
    /// post-operation step on Create of <c>al_signoff</c>, so it runs only once the
    /// sign-off has actually been written.
    ///
    /// Split from <see cref="SignoffGuardPlugin"/> for the reason AD-053 split the response
    /// guard from the progress plug-in: validation must run before the row exists and
    /// consequences must run after it, and one step cannot be both.
    ///
    /// A rejection reopens the action so the adviser reworks it. An approval leaves the
    /// action Completed and moves the case on through the AD-057 lifecycle. Either way an
    /// immutable Audit Event records who decided what (BR-012, NFR-AUD-01).
    /// </summary>
    public class SignoffProgressPlugin : PluginBase
    {
        private const string SignoffEntity = "al_signoff";
        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";
        private const string DecisionAttr = "al_signoffdecision";
        private const string NotesAttr = "al_notes";
        private const string ActionLookup = "al_remediationactionid";
        private const string CaseLookup = "al_outcomecaseid";

        private const int StatusInProgress = 120910601;
        private const int DecisionApprovedValue = 120910720;
        private const int DecisionRejectedValue = 120910721;

        private const int CommandSignOffRemediation = 120910757;

        public SignoffProgressPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SignoffProgressPlugin))
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

            var signoff = target as Entity;
            if (signoff == null || !string.Equals(signoff.LogicalName, SignoffEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var decision = signoff.GetAttributeValue<OptionSetValue>(DecisionAttr);
            var actionRef = signoff.GetAttributeValue<EntityReference>(ActionLookup);
            if (decision == null || actionRef == null)
            {
                return;
            }

            // A replay writes no second Audit Event: the key is derived from the action, and
            // the guard has already refused a second sign-off on the same one (NFR-REL-01).
            var idempotencyKey = "signoff-" + actionRef.Id.ToString("N");
            if (CommandHelpers.FindAuditByKey(service, idempotencyKey, CommandSignOffRemediation) != null)
            {
                return;
            }

            if (decision.Value == DecisionRejectedValue)
            {
                service.Update(new Entity(ActionEntity, actionRef.Id)
                {
                    [ActionStatus] = new OptionSetValue(StatusInProgress),
                });
            }
            else if (decision.Value == DecisionApprovedValue)
            {
                AdvanceCase(service, signoff, actionRef);
            }

            CommandHelpers.WriteAuditEvent(
                service,
                CommandSignOffRemediation,
                "SignOffRemediation " + actionRef.Id.ToString("D"),
                ActionEntity,
                actionRef.Id,
                signoff.GetAttributeValue<string>(NotesAttr),
                "Signed off from the portal: " + DescribeDecision(decision.Value) + ". Sign-off " + signoff.Id.ToString("D"),
                idempotencyKey,
                context);
        }

        /// <summary>
        /// An approved remediation sends the case on to recheck. The hop is checked against
        /// AD-057 rather than assumed, so a case that is not where the lifecycle expects is
        /// refused instead of jumping.
        /// </summary>
        private static void AdvanceCase(IOrganizationService service, Entity signoff, EntityReference actionRef)
        {
            var caseRef = signoff.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef == null)
            {
                var action = service.Retrieve(ActionEntity, actionRef.Id, new ColumnSet(CaseLookup));
                caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);
            }

            if (caseRef == null)
            {
                return;
            }

            var current = CaseTransitions.CurrentStatus(service, caseRef.Id);
            if (current.HasValue && current.Value == CaseLifecycle.AwaitingSignoff)
            {
                CaseTransitions.MoveThrough(service, caseRef.Id, CaseLifecycle.AwaitingRecheck);
            }
        }

        private static string DescribeDecision(int decision)
        {
            return decision == DecisionApprovedValue ? "Approved" : "Rejected";
        }
    }
}
