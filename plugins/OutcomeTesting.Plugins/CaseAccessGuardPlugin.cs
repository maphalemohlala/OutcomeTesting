using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Only the reconciler writes a case's access columns (AD-218). They are the portal's case
    /// boundary: whoever a column names can read the case, so a client able to write one could
    /// hand itself or anyone else a case. Registered synchronous pre-operation on al_outcomecase
    /// Create and Update, filtered to the six columns, and on Associate and Disassociate for the
    /// case's T&amp;C Managers link (al_outcomecase_tcmanagers).
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
            // Set only by al_ReassignTcManager, which checks who may hand a case over.
            TcManagerRouting.OverrideAttr,
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

            // Also registered on Associate and Disassociate, which carry no entity filter: the
            // case's managers are a many-to-many, and a link names a reader just as a column does.
            if (context.InputParameters.TryGetValue("Relationship", out raw) && raw is Relationship)
            {
                EnsureLinksNotWrittenDirectly((Relationship)raw, context.Depth);
                return;
            }

            var target = context.InputParameters.TryGetValue("Target", out raw) ? raw as Entity : null;
            if (target == null)
            {
                return;
            }

            EnsureNotWrittenDirectly(target, context.Depth);

            if (string.Equals(context.MessageName, "Update", StringComparison.OrdinalIgnoreCase))
            {
                AdviserStatus.EnsureCaseWriteAllowed(localPluginContext.OrgSvcFactory.CreateOrganizationService(null), target);
            }
        }

        /// <summary>Refuses a client linking or unlinking a case's T&amp;C Managers (al_outcomecase_tcmanagers).</summary>
        public static void EnsureLinksNotWrittenDirectly(Relationship relationship, int depth)
        {
            if (depth > 1 || relationship == null
                || !string.Equals(relationship.SchemaName, CaseAccessReconciler.SupervisorsRelationship, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.UnauthorizedPrefix
                + "A case's T&C Managers come from the adviser mapping and cannot be linked directly. "
                + "Change the adviser mapping, or hand the case over, and access follows.");
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
                        + "Who can see a case is worked out from the case itself and cannot be set directly. "
                        + "Allocate the check, or correct the adviser, and access follows.");
                }
            }
        }
    }
}
