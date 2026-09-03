using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command DrainNotifications (AD-003, PP-15, OD-030). Registered against
    /// the Custom API <c>al_DrainNotifications</c>. Re-drains a bounded batch of outbox rows
    /// that are not <c>Sent</c>.
    ///
    /// <see cref="NotificationDrainPlugin"/> drains each row once, as it is created. This is
    /// the recovery path for the rows that step cannot reach:
    ///
    /// - rows queued on an environment before the drain step was registered — every
    ///   notification PP-15 emitted while the mailbox was unconfirmed;
    /// - rows that failed against a mailbox that has since been approved;
    /// - rows whose recipient was empty and has since been resolved.
    ///
    /// Without it, an approved mailbox arriving later would send nothing that was already
    /// queued, and the architecture invariant that integrations are recoverable would hold
    /// only for rows created after the fix. This is the "recoverable" half.
    ///
    /// Bounded on purpose. A batch is capped so one invocation cannot try to send an
    /// unbounded number of emails, and re-running is the way to work through a backlog —
    /// a runaway send is much harder to undo than a second click.
    /// </summary>
    public class DrainNotificationsPlugin : PluginBase
    {
        private const string InMaxRows = "MaxRows";

        private const string OutProcessed = "Processed";
        private const string OutSent = "Sent";
        private const string OutFailed = "Failed";

        /// <summary>Rows drained when the caller does not say.</summary>
        public const int DefaultMaxRows = 50;

        /// <summary>The most one invocation will ever attempt, whatever the caller asks for.</summary>
        public const int MaxRowsCeiling = 250;

        public DrainNotificationsPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(DrainNotificationsPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var systemService = localPluginContext.PluginUserService;

            // Draining is an operational act on behalf of the whole environment, not work on
            // a case, so it is gated on the administrative permission rather than a
            // case-level one. No new permission key is minted for it: permission.manage is
            // the existing gate for environment-wide administration (AD-062).
            PermissionHelpers.EnsureAppPermission(
                systemService, context, "permission.manage", PermissionHelpers.AccessManage);

            var batch = Clamp(CommandHelpers.GetOptionalInt(context, InMaxRows));

            var pending = systemService.RetrieveMultiple(PendingQuery(batch)).Entities;

            var sent = 0;
            var failed = 0;
            foreach (var row in pending)
            {
                switch (NotificationDrain.Send(systemService, row, Sender(context)))
                {
                    case NotificationDrain.Result.Sent:
                        sent++;
                        break;
                    case NotificationDrain.Result.Failed:
                        failed++;
                        break;
                }
            }

            context.OutputParameters[OutProcessed] = pending.Count;
            context.OutputParameters[OutSent] = sent;
            context.OutputParameters[OutFailed] = failed;
        }

        /// <summary>
        /// Oldest first, so a backlog drains in the order the events happened rather than
        /// newest-first — a person reading a run of allocation emails should get them in the
        /// order the cases were allocated.
        /// </summary>
        public static QueryExpression PendingQuery(int maxRows)
        {
            var query = new QueryExpression(NotificationOutbox.NotificationEntity)
            {
                ColumnSet = NotificationDrain.Columns(),
                TopCount = maxRows,
                Criteria = new FilterExpression(),
                Orders = { new OrderExpression("al_queuedon", OrderType.Ascending) },
            };

            // Pending and Failed, never Sent. Selecting on "not Sent" instead would sweep in
            // any status added later and send against it by default; naming the two the
            // drain owns means a new status has to be considered deliberately.
            query.Criteria.AddCondition(
                "al_status",
                ConditionOperator.In,
                NotificationOutbox.StatusPending,
                NotificationOutbox.StatusFailed);

            return query;
        }

        /// <summary>
        /// Keeps a caller's batch size inside the ceiling, and treats a missing, zero or
        /// negative value as the default rather than as "everything".
        /// </summary>
        public static int Clamp(int? requested)
        {
            if (!requested.HasValue || requested.Value <= 0)
            {
                return DefaultMaxRows;
            }

            return requested.Value > MaxRowsCeiling ? MaxRowsCeiling : requested.Value;
        }

        private static EntityReference Sender(IPluginExecutionContext context)
        {
            return context.UserId == Guid.Empty
                ? null
                : new EntityReference("systemuser", context.UserId);
        }
    }
}
