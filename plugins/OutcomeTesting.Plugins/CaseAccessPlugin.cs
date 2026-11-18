using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Keeps a case's access columns current (AD-218). Registered synchronous post-operation
    /// on the writes that can change who may see a case: al_outcomecase Update (status, route,
    /// adviser name, adviser email), al_reviewinstance Create and Update (assigned contact,
    /// submitted on, state) and al_remediationaction Create.
    ///
    /// A step rather than a call inside each command, because the status is written by
    /// CaseTransitions.MoveThrough from many commands and a new writer would otherwise be a new
    /// place to forget it. Post-operation and synchronous, so it runs in the writer's
    /// transaction and a failure rolls the writer back.
    ///
    /// Its own case update touches only access columns, which no step filter names, so it
    /// cannot re-trigger itself.
    /// </summary>
    public class CaseAccessPlugin : PluginBase
    {
        public CaseAccessPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CaseAccessPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            // SYSTEM: see CaseAccessReconciler for why the bookkeeping is not the caller's.
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);
            var caseId = CaseIdFor(localPluginContext.PluginExecutionContext, system);
            if (!caseId.HasValue)
            {
                return;
            }

            if (IsLaterRemediationAction(localPluginContext.PluginExecutionContext, system, caseId.Value))
            {
                return;
            }

            CaseAccessReconciler.Reconcile(system, caseId.Value, DateTime.UtcNow);
        }

        /// <summary>
        /// True for the create of a remediation action on a case that already holds another.
        /// An action changes access only by existing (CaseAccess.IsReleased asks whether the
        /// case has any), so the first one on a case is the only one that can change the
        /// answer, and a submit raising one action per marked-down item reconciles once
        /// rather than once per item. Post-operation, so the row just created is counted.
        ///
        /// The cost: a later action's create no longer re-runs the reconcile, so it no longer
        /// repairs access that has drifted on a case which already has actions. The status
        /// hop and the review, contact and mapping triggers still do.
        /// </summary>
        public static bool IsLaterRemediationAction(IPluginExecutionContext context, IOrganizationService service, Guid caseId)
        {
            if (!string.Equals(context.MessageName, "Create", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(context.PrimaryEntityName, "al_remediationaction", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var query = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            return service.RetrieveMultiple(query).Entities.Count > 1;
        }

        /// <summary>The case a write belongs to, or null where it belongs to none.</summary>
        public static Guid? CaseIdFor(IPluginExecutionContext context, IOrganizationService service)
        {
            if (string.Equals(context.PrimaryEntityName, "al_outcomecase", StringComparison.OrdinalIgnoreCase))
            {
                return context.PrimaryEntityId;
            }

            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            var onTarget = target == null ? null : target.GetAttributeValue<EntityReference>("al_outcomecaseid");
            if (onTarget != null)
            {
                return onTarget.Id;
            }

            var row = service.Retrieve(context.PrimaryEntityName, context.PrimaryEntityId, new ColumnSet("al_outcomecaseid"));
            var onRow = row.GetAttributeValue<EntityReference>("al_outcomecaseid");
            return onRow == null ? (Guid?)null : onRow.Id;
        }
    }
}
