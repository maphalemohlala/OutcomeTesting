using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Only the reconciler writes a case's access columns (AD-218). They are the portal's case
    /// boundary: whoever a column names can read the case, so a client able to write one could
    /// hand itself or anyone else a case. Registered synchronous pre-operation on al_outcomecase
    /// Create and Update, filtered to the six columns.
    ///
    /// The test is pipeline depth. A client write - the Web API, the Code App, a portal form -
    /// arrives at depth 1; CaseAccessReconciler always writes from inside a plug-in, so its
    /// write arrives deeper. Deliberately not a user check: the reconciler runs as SYSTEM, and
    /// a System Administrator writing through the Web API is still a client write.
    /// </summary>
    public class CaseAccessGuardPlugin : PluginBase
    {
        private static readonly string[] AccessColumns =
        {
            CaseAccessReconciler.TaxCheckerAttr,
            CaseAccessReconciler.AqsCheckerAttr,
            CaseAccessReconciler.QueueAccountAttr,
            CaseAccessReconciler.QueuedOnAttr,
            CaseAccessReconciler.AdviserAttr,
            CaseAccessReconciler.SupervisorAttr,
        };

        public CaseAccessGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CaseAccessGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            object raw;
            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target == null)
            {
                return;
            }

            EnsureNotWrittenDirectly(target, context.Depth);
        }

        public static void EnsureNotWrittenDirectly(Entity target, int depth)
        {
            if (depth > 1)
            {
                return;
            }

            foreach (var column in AccessColumns)
            {
                if (target.Contains(column))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.UnauthorizedPrefix
                        + "Who can see a case is worked out from the case itself and cannot be set directly (AD-218). "
                        + "Allocate the check, or correct the adviser, and access follows.");
                }
            }
        }
    }
}
