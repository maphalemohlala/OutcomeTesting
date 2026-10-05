using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Who a drained notification is sent from (owner direction 2026-10-02: the shared mailbox
    /// <c>tc.outcometesting@</c> is used for all communications, closing OD-046's sender half).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A queue, not a user.</b> A shared mailbox has no licence and no sign-in, so it cannot
    /// be the account the drain step runs as. Dataverse sends from one through a queue whose
    /// email address is the mailbox's, with that queue's mailbox approved and enabled for
    /// server-side email. The email's <c>from</c> party is then the queue.
    /// </para>
    /// <para>
    /// <b>The address is an environment variable</b>, never a literal (AGENTS.md rule 7), and
    /// for a reason beyond the rule: one Exchange mailbox can be enabled for server-side sync
    /// in only one environment at a time, so the environments cannot all share it.
    /// </para>
    /// <para>
    /// <b>Unset keeps the old sender; set but unusable refuses.</b> An environment nobody has
    /// configured goes on sending from the run-as account, as before. Once the address is set,
    /// a missing or deactivated queue fails the row with the address in the reason rather than
    /// sending from the service account - a letter from the wrong mailbox looks delivered and
    /// tells nobody anything, a Failed row is visible and al_DrainNotifications retries it.
    /// </para>
    /// </remarks>
    public static class NotificationSender
    {
        /// <summary>Schema name of the environment variable holding the shared mailbox address.</summary>
        public const string AddressVariable = "al_NotificationSenderAddress";

        private const string QueueEntity = "queue";
        private const int QueueActive = 0;

        /// <summary>
        /// The <c>from</c> party for this environment, or null with <paramref name="problem"/>
        /// saying why. <paramref name="runAs"/> is the account the drain step runs as.
        /// </summary>
        public static EntityReference Resolve(
            IOrganizationService service, EntityReference runAs, out string problem)
        {
            problem = null;

            var address = (EnvironmentVariable.Read(service, AddressVariable) ?? string.Empty).Trim();
            if (address.Length == 0)
            {
                if (runAs == null)
                {
                    problem = "No sending account. The drain step must be registered to run as the "
                        + "account whose mailbox is approved for server-side email.";
                }

                return runAs;
            }

            var queue = ActiveQueue(service, address);
            if (queue == null)
            {
                problem = "No active queue has the sender address " + address + ", so nothing was "
                    + "sent. Create a queue with that email address, approve and enable its "
                    + "mailbox for server-side email, then retry.";
            }

            return queue;
        }

        private static EntityReference ActiveQueue(IOrganizationService service, string address)
        {
            var query = new QueryExpression(QueueEntity)
            {
                ColumnSet = new ColumnSet("queueid"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("emailaddress", ConditionOperator.Equal, address);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, QueueActive);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : new EntityReference(QueueEntity, found[0].Id);
        }
    }
}
