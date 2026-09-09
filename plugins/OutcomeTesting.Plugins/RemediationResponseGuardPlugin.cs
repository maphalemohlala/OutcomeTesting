using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Synchronous pre-operation guard on Update of <c>al_remediationaction</c>, filtered to
    /// the adviser's response columns. The adviser's response is theirs to write until it is
    /// submitted (project owner direction, 2026-09-09): once the action is Completed the
    /// response is what the T&amp;C Manager attests to (BR-008), and an attestation over
    /// words that can still change is not one. A rejected sign-off reopens the action to In
    /// progress, and the response is editable again until it is resubmitted.
    ///
    /// Register with:
    /// <c>registerstep &lt;orgUrl&gt; OutcomeTesting.Plugins.RemediationResponseGuardPlugin
    /// Update al_remediationaction 20 al_adviserresponse,al_evidencereference</c>.
    ///
    /// Who may write the response at all is not decided here, for the reason AD-053 gives:
    /// a Power Pages write reaches Dataverse as the site's application user, so the caller
    /// is never the adviser. That boundary is the Contact-scoped <c>Remediation Action -
    /// assigned to me</c> permission on the portal (AD-069) and the owner check on
    /// <c>al_CompleteRemediation</c>. This guard adds the lock those cannot express: the
    /// same adviser, after submission, is refused too.
    /// </summary>
    public class RemediationResponseGuardPlugin : PluginBase
    {
        private const string ActionEntity = "al_remediationaction";
        private const string ActionStatus = "al_actionstatus";

        /// <summary>The columns that carry the adviser's submission.</summary>
        public static readonly string[] ResponseColumns = { "al_adviserresponse", "al_evidencereference" };

        public RemediationResponseGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RemediationResponseGuardPlugin))
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

            var update = target as Entity;
            if (update == null || !string.Equals(update.LogicalName, ActionEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!CarriesResponse(update))
            {
                return;
            }

            // Pre-operation, so the stored row is the state before this write.
            var current = service.Retrieve(ActionEntity, update.Id, new ColumnSet(ResponseColumnsWith(ActionStatus)));

            var refusal = Refusal(current, update);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(refusal);
            }
        }

        /// <summary>
        /// The refusal for this write, or null when it may proceed. Public and static so the
        /// rule is testable without a plug-in context.
        ///
        /// Refused only when the action is Completed AND a response column actually changes.
        /// A write that restates the stored values is allowed, because that is what a retry
        /// of the portal's "Save and mark complete" looks like after a dropped response: the
        /// same response and the trigger column, sent again against an action the first
        /// attempt already completed (NFR-REL-01). Refusing it would turn a harmless replay
        /// into an error the adviser cannot act on.
        /// </summary>
        public static string Refusal(Entity current, Entity update)
        {
            var status = current.GetAttributeValue<OptionSetValue>(ActionStatus);
            if (status == null || status.Value != Remediation.StatusCompleted)
            {
                return null;
            }

            foreach (var column in ResponseColumns)
            {
                if (!update.Contains(column))
                {
                    continue;
                }

                var incoming = Normalise(update.GetAttributeValue<string>(column));
                var stored = Normalise(current.GetAttributeValue<string>(column));
                if (!string.Equals(incoming, stored, StringComparison.Ordinal))
                {
                    return CommandHelpers.ConflictPrefix +
                        "This response has been submitted for sign-off and can no longer be changed. "
                        + "If the sign-off is rejected it will be returned to you to rework.";
                }
            }

            return null;
        }

        private static bool CarriesResponse(Entity update)
        {
            foreach (var column in ResponseColumns)
            {
                if (update.Contains(column))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] ResponseColumnsWith(string extra)
        {
            var columns = new string[ResponseColumns.Length + 1];
            ResponseColumns.CopyTo(columns, 0);
            columns[ResponseColumns.Length] = extra;
            return columns;
        }

        private static string Normalise(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
