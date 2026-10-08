using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Every markup letter leaves in the same frame: the product's name across the top, one
    /// typeface, and a footer saying the message is automated (owner, 2026-10-08: "ensure that
    /// the letters have a professional styling as well as the links/button").
    ///
    /// <para>
    /// Applied at send, not written into the wording: a stored template is cleaned to plain
    /// tags on save (<see cref="HtmlSanitiser"/>), so any styling an administrator could type
    /// would be stripped, and the outbox keeps the words people actually edit.
    /// </para>
    /// </summary>
    public class NotificationFrameTests
    {
        private static readonly Guid NotificationId = Guid.Parse("f1a1e000-1111-4111-8111-222222222222");
        private static readonly EntityReference Sender = new EntityReference("systemuser", Guid.NewGuid());

        private static string Sent(string body)
        {
            var service = new FakeOrganizationService();
            service.Seed(NotificationOutbox.NotificationEntity, NotificationId,
                "al_status", new OptionSetValue(NotificationOutbox.StatusPending),
                "al_recipientemail", "adviser@example.com",
                "al_subject", "Remediation sent back on case C-1",
                "al_body", body);

            NotificationDrain.Send(
                service,
                service.Retrieve(NotificationOutbox.NotificationEntity, NotificationId, NotificationDrain.Columns()),
                Sender);

            return service.Creates.Single(c => c.LogicalName == NotificationDrain.EmailEntity)
                .GetAttributeValue<string>("description");
        }

        [Fact]
        public void A_markup_letter_is_sent_in_the_frame()
        {
            var description = Sent("<p>Dear Adam,</p><p>Your remediation has been sent back.</p>");

            Assert.Contains("<p>Dear Adam,</p><p>Your remediation has been sent back.</p>", description);
            Assert.Contains(">" + ProductName.Default + "<", description);
            Assert.Contains("Please do not reply to this email.", description);
            Assert.Contains("font-family:Segoe UI,Arial,sans-serif", description);
        }

        [Fact]
        public void A_plain_text_letter_is_framed_too_and_its_link_is_made_clickable()
        {
            // Owner, 2026-10-08: every email the same. The allocation and approval letters are
            // sentences; the allocation's link was a bare address the reader had to copy.
            var description = Sent(
                "The Tax check on case C-1 is now assigned to you. Open it to start the review: "
                + "https://outcometesting.powerappsportals.com/case-details?id=1.");

            Assert.Contains("Please do not reply to this email.", description);
            Assert.Contains(
                "<p>The Tax check on case C-1 is now assigned to you. Open it to start the review: "
                + "<a href=\"https://outcometesting.powerappsportals.com/case-details?id=1\" style=\"color:#0b5394;\">"
                + "https://outcometesting.powerappsportals.com/case-details?id=1</a>.</p>",
                description);
        }

        [Fact]
        public void A_plain_text_letter_keeps_its_line_breaks()
        {
            var framed = NotificationFrame.Wrap("First line.\nSecond line.", "OTIS");

            Assert.Contains("<p>First line.<br />Second line.</p>", framed);
        }

        [Fact]
        public void A_letter_already_in_the_frame_is_not_framed_again()
        {
            var once = NotificationFrame.Wrap("<p>Hello</p>", "OTIS");

            Assert.Equal(once, NotificationFrame.Wrap(once, "OTIS"));
        }

        [Fact]
        public void The_frame_names_the_environments_product_as_text()
        {
            var framed = NotificationFrame.Wrap("<p>Hello</p>", "OTIS & <Co>");

            Assert.Contains("OTIS &amp; &lt;Co&gt;", framed);
            Assert.DoesNotContain("<Co>", framed);
        }
    }
}
