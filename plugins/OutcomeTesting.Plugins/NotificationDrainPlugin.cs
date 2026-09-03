using System;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The PP-15 drain (AD-035, OD-030). Registered as an <b>asynchronous</b> post-operation
    /// step on Create of <c>al_notification</c>.
    ///
    /// Asynchronous is the whole design, not a performance choice. The emitters write the
    /// outbox row synchronously so it shares the transaction of the state change that caused
    /// it; this step runs after that transaction commits, so a mailbox refusal can never roll
    /// back a submitted review, and a review that rolls back can never leave an email behind.
    /// A synchronous drain would collapse the two halves and lose both guarantees.
    ///
    /// It also removes the need for a scheduler. Dataverse has no native cron, and Power
    /// Automate is excluded everywhere in this solution after AD-073 — an async step is the
    /// platform's own out-of-band execution, so the outbox drains itself with no connector,
    /// no licence and no second system to keep alive.
    ///
    /// <b>The step registration is the switch.</b> There is no separate enable setting: an
    /// unregistered or disabled step drains nothing and rows rest at <c>Pending</c>, which is
    /// exactly the state PP-15 held before this shipped. That matters because server-side
    /// email needs an approved, tested mailbox per environment (OD-030) — an environment
    /// where that has not been confirmed simply does not get the step, and the outbox goes on
    /// recording events honestly without claiming anything was sent.
    ///
    /// The sending mailbox is the account the step is registered to run as, reached through
    /// <c>PluginUserService</c>. Registering the step with an impersonating user therefore
    /// sets the sender, which is how the service account is named without an address ever
    /// appearing in code (AGENTS.md rule 7).
    /// </summary>
    public class NotificationDrainPlugin : PluginBase
    {
        public NotificationDrainPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(NotificationDrainPlugin))
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

            var record = target as Entity;
            if (record == null || !string.Equals(
                record.LogicalName, NotificationOutbox.NotificationEntity, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Read the row back rather than trusting the Target. An async step receives the
            // image as it was created, and the columns the drain needs are the ones the
            // emitter set — reading is one call and cannot be wrong.
            var notification = service.Retrieve(
                NotificationOutbox.NotificationEntity, record.Id, NotificationDrain.Columns());

            // Never rethrow. A drain that throws is retried by the async service on a
            // schedule nobody chose, against a mailbox that is refusing for a reason the
            // retry will not change; NotificationDrain has already recorded that reason on
            // the row, where a person can see it and al_DrainNotifications can retry it
            // deliberately.
            NotificationDrain.Send(service, notification, Sender(context));
        }

        /// <summary>
        /// The account the step runs as. <c>UserId</c> is the registered (or impersonated)
        /// user, not the person whose action queued the row — which is the point: portal
        /// writes reach Dataverse as the site's application user (AD-053), so the initiating
        /// user is not a mailbox anyone approved.
        /// </summary>
        private static EntityReference Sender(IPluginExecutionContext context)
        {
            return context.UserId == Guid.Empty
                ? null
                : new EntityReference("systemuser", context.UserId);
        }
    }
}
