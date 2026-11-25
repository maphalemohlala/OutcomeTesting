using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The portal parks the checker's remedial actions by writing JSON onto the checker's own
    /// contact row, because a Power Pages page cannot call a Custom API (AD-053). The guard is
    /// the review itself: open, and assigned to the contact whose row carried the request.
    /// </summary>
    public class RemedialActionsRequestPluginTests
    {
        private static readonly Guid ContactId = Guid.Parse("c1c1c1c1-1111-4111-8111-c1c1c1c1c1c1");
        private static readonly Guid ReviewId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

        private static FakeOrganizationService Service(Guid? assignedTo, bool submitted = false)
        {
            var svc = new FakeOrganizationService();
            svc.Seed("contact", ContactId, RemedialActionsRequestPlugin.RequestAttr, "{}");
            var review = svc.Seed("al_reviewinstance", ReviewId);
            if (assignedTo.HasValue)
            {
                review["al_assignedcontactid"] = new EntityReference("contact", assignedTo.Value);
            }

            if (submitted)
            {
                review["al_submittedon"] = new DateTime(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);
            }

            return svc;
        }

        private static RemedialActionsRequestPayload Payload(params string[] pairs)
        {
            var payload = new RemedialActionsRequestPayload
            {
                ReviewId = ReviewId.ToString("D"),
                Actions = new List<RemedialActionEntry>(),
            };
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                payload.Actions.Add(new RemedialActionEntry { Item = pairs[i], Text = pairs[i + 1] });
            }

            return payload;
        }

        [Fact]
        public void Parks_the_words_on_the_review_and_clears_the_request()
        {
            var svc = Service(ContactId);

            RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First: Fail", " Fix it. ", "Second: No", "  "));

            var parked = RemedialActions.Parse(
                svc.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>(RemedialActions.PendingAttr));
            Assert.Equal("Fix it.", parked["First: Fail"]);
            Assert.False(parked.ContainsKey("Second: No"));
            Assert.Null(svc.Row("contact", ContactId).GetAttributeValue<string>(RemedialActionsRequestPlugin.RequestAttr));
        }

        [Fact]
        public void Refuses_a_checker_the_review_is_not_assigned_to()
        {
            var svc = Service(Guid.NewGuid());

            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First: Fail", "Fix it.")));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, thrown.Message);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Refuses_a_submitted_review()
        {
            var svc = Service(ContactId, submitted: true);

            Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First: Fail", "Fix it.")));
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void Refuses_a_request_that_names_no_review()
        {
            var payload = Payload("First: Fail", "Fix it.");
            payload.ReviewId = "not-a-guid";

            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(Service(ContactId), ContactId, payload));
            Assert.StartsWith(CommandHelpers.ValidationPrefix, thrown.Message);
        }

        [Fact]
        public void Refuses_an_action_longer_than_the_column_holds()
        {
            var thrown = Assert.Throws<InvalidPluginExecutionException>(
                () => RemedialActionsRequestPlugin.Apply(
                    Service(ContactId), ContactId, Payload("First: Fail", new string('x', RemedialActions.MaxLength + 1))));

            Assert.Contains("'First: Fail'", thrown.Message);
        }

        [Fact]
        public void Formatted_words_are_cleaned_and_limited_by_what_reads_not_by_the_markup()
        {
            var svc = Service(ContactId);
            var words = new string('x', RemedialActions.MaxLength - 10);

            RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload(
                "First: Fail", "<p><strong>" + words + "</strong><script>alert(1)</script></p>",
                "Second: No", "<p><br></p>"));

            var parked = RemedialActions.Parse(
                svc.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>(RemedialActions.PendingAttr));
            Assert.Equal("<p><strong>" + words + "</strong></p>", parked["First: Fail"]);
            Assert.False(parked.ContainsKey("Second: No"));

            var tooLong = Assert.Throws<InvalidPluginExecutionException>(() => RemedialActionsRequestPlugin.Apply(
                Service(ContactId), ContactId, Payload("First: Fail", "<p>" + new string('x', RemedialActions.MaxLength + 1) + "</p>")));
            Assert.Contains("'First: Fail'", tooLong.Message);
        }

        [Fact]
        public void Markup_too_large_to_store_is_refused_by_name()
        {
            var bulky = new System.Text.StringBuilder();
            for (var i = 0; i < 2000; i++)
            {
                bulky.Append("<u><strong>x</strong></u>");
            }

            var thrown = Assert.Throws<InvalidPluginExecutionException>(() => RemedialActionsRequestPlugin.Apply(
                Service(ContactId), ContactId, Payload("First: Fail", "<p>" + bulky + "</p>")));

            Assert.Equal(
                "VALIDATION: The formatting in the remedial action for 'First: Fail' makes it too long to store. Remove some of the formatting.",
                thrown.Message);
        }

        [Fact]
        public void Emptying_every_box_clears_the_parked_map()
        {
            var svc = Service(ContactId);
            svc.Row("al_reviewinstance", ReviewId)[RemedialActions.PendingAttr] = "[{\"item\":\"First\",\"text\":\"Old\"}]";

            RemedialActionsRequestPlugin.Apply(svc, ContactId, Payload("First", ""));

            Assert.Null(svc.Row("al_reviewinstance", ReviewId).GetAttributeValue<string>(RemedialActions.PendingAttr));
        }

        [Fact]
        public void The_plugin_ignores_its_own_clear()
        {
            var svc = Service(ContactId);
            var provider = new FakeServiceProvider(svc);
            provider.Context.MessageName = "Update";
            provider.Context.PrimaryEntityId = ContactId;
            provider.Context.InputParameters["Target"] = new Entity("contact", ContactId)
            {
                [RemedialActionsRequestPlugin.RequestAttr] = null,
            };

            new RemedialActionsRequestPlugin(null, null).Execute(provider);

            Assert.Empty(svc.Updates);
        }
    }
}
