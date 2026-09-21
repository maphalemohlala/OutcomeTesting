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
    /// It also holds the 2026-09-21 boundary: <c>al_recheckrequired</c> and
    /// <c>al_changesadvice</c> are the T&amp;C Manager's answers, and this refuses every
    /// write to them that did not come through the sign-off. See <see cref="TcOnlyRefusal"/>.
    ///
    /// Register with:
    /// <c>registerstep &lt;orgUrl&gt; OutcomeTesting.Plugins.RemediationResponseGuardPlugin
    /// Update al_remediationaction 20 al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice</c>.
    /// <b>All five filtering attributes are still required</b>, and now for two reasons: the
    /// first three are the adviser's response this freezes at completion, and the last two
    /// are the pair it refuses outright. A step narrowed to the three would let the refused
    /// pair through unseen.
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

        /// <summary>
        /// The columns that carry the adviser's submission: the remedial action text, the
        /// Intelligent Office reference, and the one remediation-form answer that is still
        /// theirs to give (AD-095, narrowed 2026-09-21).
        ///
        /// <c>al_recheckrequired</c> and <c>al_changesadvice</c> USED to be here. They are
        /// now <see cref="TcOnlyColumns"/>: the project owner directed on 2026-09-21 that
        /// "Recheck required?" and "Do the remedial actions change the advice?" are the T&amp;C
        /// Manager's answers, not the adviser's. They are no longer frozen at completion,
        /// because they are no longer written before it.
        /// </summary>
        public static readonly string[] ResponseColumns =
        {
            "al_adviserresponse",
            "al_evidencereference",
            "al_clientcontactrequired",
        };

        /// <summary>
        /// The two answers only the T&amp;C Manager may give (project owner, 2026-09-21:
        /// "The Recheck required? and Do the remedial actions change the advice? should only
        /// be editable to T&amp;C manager only").
        ///
        /// <c>al_recheckrequired</c> is not decoration: <see cref="SignoffProgressPlugin"/>
        /// reads it to decide whether the case closes on the approval or waits at Awaiting
        /// Recheck (AD-138). It was the adviser's answer, and the person it binds is the
        /// supervisor — so the adviser was deciding whether their own remediation needed
        /// looking at again.
        ///
        /// The write lives on <see cref="SignoffRequestPlugin"/>, which is where the role can
        /// actually be checked, and this refuses every other route to the columns.
        /// </summary>
        public static readonly string[] TcOnlyColumns =
        {
            "al_recheckrequired",
            "al_changesadvice",
        };

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

            // The T&C-only pair first: it is refused whatever the action's status is, so it
            // must not sit behind the Completed gate below.
            var trespass = TcOnlyRefusal(
                update, CommandHelpers.IsWithinMessageOn(context, "Update", "contact"));
            if (trespass != null)
            {
                throw new InvalidPluginExecutionException(trespass);
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
        /// The refusal for a write that reaches for the T&amp;C Manager's two answers by any
        /// route but the sign-off, or null when there is nothing of theirs in it. Public and
        /// static so the rule is testable without a plug-in context.
        ///
        /// <paramref name="fromContactPipeline"/> is the whole test, and
        /// <see cref="CommandHelpers.IsWithinMessageOn"/> records why it is a boundary
        /// rather than a hint: the only way onto these columns is
        /// <see cref="SignoffRequestPlugin.Apply"/>, which runs inside the Update of the
        /// signed-in supervisor's own contact row and has checked their web role before it
        /// writes. A browser PATCH of an <c>al_remediationaction</c> — which is what the
        /// adviser's page sends, and what anyone else could send — has no parent context at
        /// all.
        ///
        /// Refused on PRESENCE, not on change. A write restating the stored value is still an
        /// adviser answering the supervisor's question, and allowing it would mean the guard
        /// could be probed for what the answer currently is. That differs deliberately from
        /// <see cref="Refusal"/>, where a restated value is a dropped-response retry and
        /// refusing it would strand the adviser.
        /// </summary>
        public static string TcOnlyRefusal(Entity update, bool fromContactPipeline)
        {
            if (update == null || fromContactPipeline)
            {
                return null;
            }

            foreach (var column in TcOnlyColumns)
            {
                if (!update.Contains(column))
                {
                    continue;
                }

                return CommandHelpers.PreconditionPrefix +
                    "'Recheck required?' and 'Do the remedial actions change the advice?' are "
                    + "the T&C Manager's answers and are recorded when they sign this "
                    + "remediation off.";
            }

            return null;
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

                var incoming = Describe(update.Contains(column) ? update[column] : null);
                var stored = Describe(current.Contains(column) ? current[column] : null);
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

        /// <summary>
        /// One comparable form for a text or choice column: the option value for a choice,
        /// the normalised text otherwise, and the empty string for nothing.
        /// </summary>
        private static string Describe(object value)
        {
            var option = value as OptionSetValue;
            if (option != null)
            {
                return option.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return Normalise(value as string);
        }
    }
}
