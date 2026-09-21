using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The AD-057 lifecycle, enforced on the table rather than only in the commands.
    /// Registered as a synchronous PRE-operation step on Update of
    /// <c>al_outcomecase</c>, filtered to <c>al_casestatus</c>.
    ///
    /// Why this exists (F53, 2026-09-21). The ordering lived entirely in
    /// <see cref="CaseTransitions.MoveThrough"/>, which only the commands call - so anything
    /// writing the column directly bypassed it. A single PATCH moved a case from Awaiting
    /// Remediation straight to Closed, skipping sign-off and recheck, with no regrade and no
    /// audit event. That was not an administrator-only path: the shipped
    /// <c>Outcome Testing App User</c> role grants <c>prvWriteal_OutcomeCase</c> at Global,
    /// so every app user held table-wide write and could make the same call from any
    /// Dataverse client. The portal was never the exposure - it is refused on the case table
    /// outright - which is precisely why a UI-shaped fix would have missed it.
    ///
    /// The rule is not restated here. This calls the same
    /// <see cref="CaseTransitions.EnsureAllowed"/> the commands call, so there is one
    /// definition of a legal hop and no second copy to drift. A command's own write passes
    /// because <c>MoveThrough</c> only ever makes hops the rule already allows; what changes
    /// is that everything else is now held to the same rule.
    ///
    /// Read from the database rather than from a registered pre-image. In pre-operation the
    /// row still holds the old value, so <see cref="CaseTransitions.CurrentStatus"/> is the
    /// "from" - and it keeps the step's registration to one filtered attribute with no image
    /// to be dropped or mis-configured on import. A step whose correctness depends on an
    /// image that a later import quietly omits is the failure this solution has already had
    /// once (the disabled al_response steps, 2026-09-02).
    ///
    /// A no-op write is allowed, because <see cref="CaseLifecycle.IsAllowed"/> permits
    /// from == to: an update that carries the status unchanged alongside other columns must
    /// not be refused.
    ///
    /// <b>There is no bypass, deliberately.</b> An administrator is held to this too, which
    /// is consistent with the 2026-09-21 direction that a system administrator may only
    /// perform actions they are allowed to. Repairing genuinely bad lifecycle data therefore
    /// means moving a case through legal hops, or deactivating and re-creating it - not
    /// setting the column to whatever is wanted. That is the point.
    /// </summary>
    public class CaseStatusGuardPlugin : PluginBase
    {
        private const string CaseEntity = "al_outcomecase";
        private const string CaseStatus = "al_casestatus";

        public CaseStatusGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(CaseStatusGuardPlugin))
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
            if (entity == null
                || !string.Equals(entity.LogicalName, CaseEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Guard(service, entity);
        }

        /// <summary>
        /// Refuses the write when it would move the case somewhere AD-057 does not describe.
        /// Separated from the pipeline plumbing so the rule can be exercised directly, but
        /// the plug-in above is covered by its own test as well - a helper nothing calls is
        /// the shape of defect this solution has already shipped once (F50).
        /// </summary>
        public static void Guard(IOrganizationService service, Entity target)
        {
            if (service == null || target == null || !target.Contains(CaseStatus))
            {
                return;
            }

            var to = target.GetAttributeValue<OptionSetValue>(CaseStatus);

            // Contains but null is an explicit clear. A case with no status is not a
            // lifecycle state, and every reader of the column treats null as "never set" -
            // which is true only before the first write, never after it.
            if (to == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "A case cannot be left with no status. Move it to the state it belongs in instead.");
            }

            CaseTransitions.EnsureAllowed(CaseTransitions.CurrentStatus(service, target.Id), to.Value);
        }
    }
}
