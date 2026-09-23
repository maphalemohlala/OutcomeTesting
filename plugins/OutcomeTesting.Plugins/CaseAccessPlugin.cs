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

            CaseAccessReconciler.Reconcile(system, caseId.Value, DateTime.UtcNow);
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
