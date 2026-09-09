using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The portal completion's idempotency key (NFR-REL-01, BR-008). One key per completion
    /// round: a retry of the same round replays, a second round after a rejected sign-off
    /// is a new intent. The key used to be the action id alone, which replayed the first
    /// completion forever and left a reworked action impossible to complete.
    /// </summary>
    public class CompleteRequestPluginTests
    {
        private static readonly Guid ActionId = Guid.Parse("12121212-3434-4343-8565-121212121212");

        [Fact]
        public void The_first_round_keeps_the_original_key_shape()
        {
            // Completions already recorded in an environment carry this key and must still
            // replay as the completion they were.
            Assert.Equal(
                "portal-complete-" + ActionId.ToString("N"),
                CompleteRequestPlugin.CompletionKey(ActionId, null));
        }

        [Fact]
        public void A_retry_of_the_same_round_produces_the_same_key()
        {
            var restarted = new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);

            Assert.Equal(
                CompleteRequestPlugin.CompletionKey(ActionId, restarted),
                CompleteRequestPlugin.CompletionKey(ActionId, restarted));
        }

        [Fact]
        public void A_second_round_after_a_rejection_is_a_new_intent()
        {
            var restarted = new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);

            Assert.NotEqual(
                CompleteRequestPlugin.CompletionKey(ActionId, null),
                CompleteRequestPlugin.CompletionKey(ActionId, restarted));
        }

        [Fact]
        public void Each_rejection_starts_a_distinguishable_round()
        {
            var first = new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);
            var second = new DateTime(2026, 9, 17, 14, 5, 0, DateTimeKind.Utc);

            Assert.NotEqual(
                CompleteRequestPlugin.CompletionKey(ActionId, first),
                CompleteRequestPlugin.CompletionKey(ActionId, second));
        }
    }
}
