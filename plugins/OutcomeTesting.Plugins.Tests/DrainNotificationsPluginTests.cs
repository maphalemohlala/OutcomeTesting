using System.Linq;
using Microsoft.Xrm.Sdk.Query;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The al_DrainNotifications batch (PP-15, OD-030). What matters here is the bound —
    /// one invocation must never be able to send an unbounded number of emails — and that
    /// the batch selects the two statuses the drain owns and never the delivered ones.
    /// </summary>
    public class DrainNotificationsPluginTests
    {
        [Fact]
        public void Uses_the_default_batch_when_the_caller_says_nothing()
        {
            Assert.Equal(DrainNotificationsPlugin.DefaultMaxRows, DrainNotificationsPlugin.Clamp(null));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-500)]
        public void Treats_a_nonsense_batch_size_as_the_default_not_as_everything(int requested)
        {
            // Zero meaning "no limit" is the classic version of this bug: the guard reads as
            // present and the run sends the entire outbox.
            Assert.Equal(DrainNotificationsPlugin.DefaultMaxRows, DrainNotificationsPlugin.Clamp(requested));
        }

        [Fact]
        public void Honours_a_smaller_batch_than_the_default()
        {
            Assert.Equal(5, DrainNotificationsPlugin.Clamp(5));
        }

        [Fact]
        public void Clamps_a_batch_larger_than_the_ceiling()
        {
            Assert.Equal(DrainNotificationsPlugin.MaxRowsCeiling, DrainNotificationsPlugin.Clamp(100000));
        }

        [Fact]
        public void Selects_pending_and_failed_rows_only()
        {
            var query = DrainNotificationsPlugin.PendingQuery(10);

            var status = Assert.Single(query.Criteria.Conditions);
            Assert.Equal("al_status", status.AttributeName);
            Assert.Equal(ConditionOperator.In, status.Operator);
            Assert.Equal(
                new object[] { NotificationOutbox.StatusPending, NotificationOutbox.StatusFailed },
                status.Values.ToArray());

            // Naming the two statuses rather than excluding Sent: a status added later has
            // to be considered deliberately instead of being drained by default.
            Assert.DoesNotContain(NotificationOutbox.StatusSent, status.Values.Cast<int>());
        }

        [Fact]
        public void Carries_the_batch_bound_into_the_query()
        {
            Assert.Equal(10, DrainNotificationsPlugin.PendingQuery(10).TopCount);
        }

        [Fact]
        public void Drains_oldest_first_so_a_backlog_arrives_in_the_order_it_happened()
        {
            var order = Assert.Single(DrainNotificationsPlugin.PendingQuery(10).Orders);

            Assert.Equal("al_queuedon", order.AttributeName);
            Assert.Equal(OrderType.Ascending, order.OrderType);
        }

        [Fact]
        public void Reads_the_columns_the_drain_needs()
        {
            var columns = DrainNotificationsPlugin.PendingQuery(10).ColumnSet.Columns;

            Assert.Contains("al_status", columns);
            Assert.Contains("al_recipientemail", columns);
            Assert.Contains("al_subject", columns);
            Assert.Contains("al_body", columns);
        }
    }
}
