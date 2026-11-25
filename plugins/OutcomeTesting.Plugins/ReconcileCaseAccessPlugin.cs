using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// al_ReconcileCaseAccess: runs the reconciler on one case on demand (AD-218). It is the
    /// backfill for cases that existed before the step was registered, and the repair tool
    /// for a case whose access looks wrong. Gated on permission.manage, because it changes
    /// who can see a case even though every value it writes is derived.
    /// </summary>
    public class ReconcileCaseAccessPlugin : PluginBase
    {
        private const string InTargetId = "TargetId";

        public ReconcileCaseAccessPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ReconcileCaseAccessPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            PermissionHelpers.EnsureAppPermission(
                localPluginContext.PluginUserService, context, "permission.manage", PermissionHelpers.AccessManage);

            var caseId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var system = localPluginContext.OrgSvcFactory.CreateOrganizationService(null);

            // F37: a case removed since the caller listed it is named, not faulted.
            CommandHelpers.RetrieveOrNotFound(
                system, "al_outcomecase", caseId, new ColumnSet(false),
                "That case no longer exists. Refresh and try again.");

            var change = CaseAccessReconciler.Reconcile(system, caseId, DateTime.UtcNow);

            // The managers' links are not columns, but a backfill has to be able to see them move.
            var changed = new List<string>(change.ChangedColumns);
            if (change.AddedSupervisors.Count > 0 || change.RemovedSupervisors > 0)
            {
                changed.Add(CaseAccessReconciler.SupervisorsRelationship
                    + " +" + change.AddedSupervisors.Count + "/-" + change.RemovedSupervisors);
            }

            context.OutputParameters["Changed"] = string.Join(",", changed);
            context.OutputParameters["Released"] = change.Released;
            context.OutputParameters["AdviserUnmatched"] = change.AdviserUnmatched;
            context.OutputParameters["SupervisorUnmatched"] = change.SupervisorUnmatched;
        }
    }
}
