using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What a rejected sign-off does to the action (BR-008, OD-018). The clock reset is the
    /// half of PP-13 that was missing: without it a case sent back for rework carried on
    /// ageing from its original start, so a second round always read as breached before the
    /// adviser had had a day on it.
    /// </summary>
    public class SignoffProgressPluginTests
    {
        private static readonly Guid ActionId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        private static readonly DateTime Now = new DateTime(2026, 9, 3, 10, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void Sends_a_rejected_action_back_to_in_progress()
        {
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.Equal(120910601, update.GetAttributeValue<OptionSetValue>("al_actionstatus").Value);
        }

        [Fact]
        public void Restarts_the_clock_on_a_rejection()
        {
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.Equal(Now, update.GetAttributeValue<DateTime>("al_clockstartedon"));
        }

        [Fact]
        public void Leaves_the_original_start_alone_so_the_previous_period_survives()
        {
            // OD-018 requires both periods, not one merged age. createdon is the original
            // start and is never written here; overwriting it would merge them.
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.False(update.Contains("createdon"));
        }

        [Fact]
        public void Targets_the_action_it_was_given()
        {
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.Equal("al_remediationaction", update.LogicalName);
            Assert.Equal(ActionId, update.Id);
        }

        [Fact]
        public void Does_not_touch_completion_so_the_earlier_completion_is_not_erased()
        {
            // The adviser did complete the first round; the T&C Manager rejected it. Blanking
            // al_completedon here would erase that they ever responded.
            var update = SignoffProgressPlugin.ReopenedAction(ActionId, Now);

            Assert.False(update.Contains("al_completedon"));
        }
    }
}
