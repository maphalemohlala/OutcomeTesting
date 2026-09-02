using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The T&amp;C Manager's half of the BR-008 loop, done from the portal (PP-12, FR-023,
    /// AD-045). Registered as a synchronous pre-operation step on Create of
    /// <c>al_signoff</c>.
    ///
    /// Unlike the review submit and the adviser completion, this is a **create**, and the
    /// Portals Web API does support create — so no trigger column is needed. That
    /// difference is not cosmetic, it is what makes the permission model work: a trigger
    /// column on <c>al_remediationaction</c> would sit behind one site-wide field
    /// allowlist shared with the adviser, and an adviser could then approve their own
    /// remediation. A separate table carries a separate permission, so create on
    /// <c>al_signoff</c> is bound to <c>AL Portal - T&amp;C Supervisor</c> alone.
    ///
    /// The page sends only the decision, the notes and the action. Everything identifying —
    /// the name, the business code, the case and the timestamp — is stamped here, so the
    /// browser cannot choose a key or backdate a sign-off.
    /// </summary>
    public class SignoffGuardPlugin : PluginBase
    {
        private const string SignoffEntity = "al_signoff";
        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";
        private const string DecisionAttr = "al_signoffdecision";
        private const string NotesAttr = "al_notes";
        private const string ActionLookup = "al_remediationactionid";
        private const string CaseLookup = "al_outcomecaseid";

        private const int StatusCompleted = 120910602;
        private const int DecisionRejectedValue = 120910721;

        public SignoffGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(SignoffGuardPlugin))
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
            if (decision == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A sign-off must record whether the remediation was approved or rejected.");
            }

            // BR-008: a rejected sign-off returns to the adviser, so it must say why.
            var notes = signoff.GetAttributeValue<string>(NotesAttr);
            if (decision.Value == DecisionRejectedValue && string.IsNullOrWhiteSpace(notes))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "A rejected sign-off must record notes explaining the return.");
            }

            var actionRef = signoff.GetAttributeValue<EntityReference>(ActionLookup);
            if (actionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "A sign-off must name the remediation action it relates to.");
            }

            var action = service.Retrieve(ActionEntity, actionRef.Id, new ColumnSet(ActionStatus, CaseLookup));

            var status = action.GetAttributeValue<OptionSetValue>(ActionStatus);
            if (status == null || status.Value != StatusCompleted)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Only a completed remediation action can be signed off.");
            }

            // Sign-off happens once. Without this the same action can be signed off twice,
            // and a later rejection would reopen an already-approved action, leaving two
            // contradictory sign-offs with nothing recording which is authoritative.
            if (HasExistingSignoff(service, actionRef.Id))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This remediation action has already been signed off. Reopening a completed sign-off is a privileged correction (AD-031).");
            }

            signoff["al_name"] = "SignOff " + actionRef.Id.ToString("D");
            signoff["al_signoffcode"] = "SO-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            signoff["al_signedoffon"] = DateTime.UtcNow;

            var caseRef = action.GetAttributeValue<EntityReference>(CaseLookup);
            if (caseRef != null)
            {
                signoff[CaseLookup] = caseRef;
            }
        }

        /// <summary>
        /// True when this action already carries an active sign-off. Read with the system
        /// service so the answer does not depend on the caller's read privileges: a sign-off
        /// they cannot see is still a sign-off.
        /// </summary>
        private static bool HasExistingSignoff(IOrganizationService service, Guid actionId)
        {
            var query = new QueryExpression(SignoffEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition(ActionLookup, ConditionOperator.Equal, actionId);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }
    }
}
