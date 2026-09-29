using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The checker's remedial actions, parked on the review until the submit raises the
    /// actions they belong to (project owner, 2026-09-29: the checker fills in the remedial
    /// action, not the adviser).
    ///
    /// The map is keyed by the item's text exactly as Remediation.NonPassItems produces it,
    /// because that is the only thing the page and the server both know about an item before
    /// any action exists.
    /// </summary>
    public class RemedialActionsTests
    {
        private static Dictionary<string, string> Map(params string[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                map[pairs[i]] = pairs[i + 1];
            }

            return map;
        }

        [Fact]
        public void A_serialised_map_parses_back_to_itself_including_awkward_characters()
        {
            var map = Map(
                "AML - \"ID\" & address <check>: No", "Re-verify & retain.",
                RemedialActions.OverallKey, "Explain the rationale.");

            var back = RemedialActions.Parse(RemedialActions.Serialise(map));

            Assert.Equal("Re-verify & retain.", back["AML - \"ID\" & address <check>: No"]);
            Assert.Equal("Explain the rationale.", back[RemedialActions.OverallKey]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"item\":\"x\"}")]
        public void Nothing_readable_parses_to_an_empty_map(string json)
        {
            Assert.Empty(RemedialActions.Parse(json));
        }

        [Fact]
        public void Serialise_keeps_only_items_with_words_and_returns_null_when_none_do()
        {
            Assert.Null(RemedialActions.Serialise(Map("First", "   ", "Second", "")));

            var kept = RemedialActions.Parse(RemedialActions.Serialise(Map("First", " Fix it. ", "Second", " ")));
            Assert.Single(kept);
            Assert.Equal("Fix it.", kept["First"]);
        }

        [Fact]
        public void The_refusal_names_the_first_item_with_no_words()
        {
            var refusal = RemedialActions.Refusal(
                new List<string> { "First: Fail", "Second: No" },
                Map("First: Fail", "Done."));

            Assert.Contains("'Second: No'", refusal);
        }

        [Fact]
        public void Every_item_answered_is_no_refusal_and_stale_keys_are_ignored()
        {
            // "Old: Fail" was written for a fail point the checker later unticked. It stays
            // in the map so ticking it again brings the words back, and the gate ignores it.
            Assert.Null(RemedialActions.Refusal(
                new List<string> { "First: Fail" },
                Map("First: Fail", "Done.", "Old: Fail", "")));
        }

        [Fact]
        public void With_nothing_itemised_the_overall_action_is_what_is_owed()
        {
            var refusal = RemedialActions.Refusal(new List<string>(), Map());
            Assert.Contains("overall remedial action", refusal);

            Assert.Null(RemedialActions.Refusal(
                new List<string>(), Map(RemedialActions.OverallKey, "Explain the rationale.")));
        }

        [Fact]
        public void Align_gives_each_item_its_own_words_in_item_order()
        {
            var texts = RemedialActions.Align(
                new List<string> { "B", "A", "C" },
                Map("A", "a words", "B", "b words"));

            Assert.Equal(new[] { "b words", "a words", null }, texts.ToArray());
        }

        [Fact]
        public void EnsureWritten_refuses_a_review_that_parked_nothing()
        {
            var service = new FakeOrganizationService();
            var reviewId = Guid.NewGuid();
            service.Seed("al_reviewinstance", reviewId);

            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActions.EnsureWritten(service, reviewId, DateTime.UtcNow));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, thrown.Message);
        }

        [Fact]
        public void EnsureWritten_passes_a_review_whose_overall_action_is_parked()
        {
            var service = new FakeOrganizationService();
            var reviewId = Guid.NewGuid();
            service.Seed(
                "al_reviewinstance", reviewId,
                RemedialActions.PendingAttr,
                RemedialActions.Serialise(Map(RemedialActions.OverallKey, "Explain the rationale.")));

            RemedialActions.EnsureWritten(service, reviewId, DateTime.UtcNow);
        }

        [Fact]
        public void The_portal_payload_carries_the_review_and_its_entries()
        {
            var payload = RemedialActionsRequestPayload.Parse(
                "{\"reviewId\":\"f3f3f3f3-0003-4003-8003-f3f3f3f3f3f3\",\"actions\":[{\"item\":\"First\",\"text\":\"Fix it.\"}]}");

            Assert.Equal("f3f3f3f3-0003-4003-8003-f3f3f3f3f3f3", payload.ReviewId);
            Assert.Equal("First", payload.Actions[0].Item);
            Assert.Equal("Fix it.", payload.Actions[0].Text);
            Assert.Null(RemedialActionsRequestPayload.Parse("garbage"));
        }

        [Theory]
        [InlineData(RemedialActions.ActionPerformedYes, "Yes")]
        [InlineData(RemedialActions.ActionPerformedNo, "No")]
        [InlineData(null, null)]
        [InlineData(1, null)]
        public void Action_performed_reads_as_the_form_says_it(int? value, string expected)
        {
            Assert.Equal(expected, RemedialActions.ActionPerformedLabel(value));
        }
    }
}
