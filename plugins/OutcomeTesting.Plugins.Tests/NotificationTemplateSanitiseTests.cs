using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The stored body is cleaned before it is written (AD-169).
    ///
    /// <para>
    /// An administrator editing a letter is writing markup that lands in somebody's inbox, and
    /// <c>NotificationDrain.Compose</c> puts the body straight into <c>email.description</c>,
    /// which renders as markup. Until now the guard checked codes, tokens and emptiness and
    /// passed the markup through untouched.
    /// </para>
    /// <para>
    /// These run the plug-in rather than a helper, because the point is what is left on the
    /// Target when the platform writes it — a test of a pure function could pass while the
    /// pipeline stored the original.
    /// </para>
    /// </summary>
    public class NotificationTemplateSanitiseTests
    {
        /// <summary>Runs the guard over a create and returns the body it would store.</summary>
        private static string Stored(string body, string code = NotificationTemplates.CasePassed)
        {
            var row = Row(body, code);
            Run(row);
            return row.GetAttributeValue<string>(NotificationTemplates.BodyAttr);
        }

        private static Entity Row(string body, string code)
        {
            return new Entity(NotificationTemplates.TemplateEntity)
            {
                [NotificationTemplates.CodeAttr] = code,
                [NotificationTemplates.SubjectAttr] = "A subject",
                [NotificationTemplates.BodyAttr] = body,
            };
        }

        private static void Run(Entity row)
        {
            var provider = new FakeServiceProvider();
            provider.Context.InputParameters["Target"] = row;
            provider.Context.MessageName = "Create";

            new NotificationTemplateGuardPlugin(null, null).Execute(provider);
        }

        private static InvalidPluginExecutionException Refused(string body)
        {
            return Assert.Throws<InvalidPluginExecutionException>(
                () => Run(Row(body, NotificationTemplates.CasePassed)));
        }

        // ------------------------------------------------------------ what is removed

        [Fact]
        public void A_script_is_not_stored()
        {
            var stored = Stored("<p>Hello</p><script>alert(1)</script>");

            Assert.DoesNotContain("script", stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alert", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Hello", stored);
        }

        [Fact]
        public void An_event_handler_on_a_tag_is_not_stored()
        {
            // The attribute is the payload here, not the tag. A sanitiser that kept attributes
            // it did not understand would pass this straight through.
            var stored = Stored("<p onclick=\"steal()\">Read this</p>");

            Assert.DoesNotContain("onclick", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Read this", stored);
        }

        [Fact]
        public void A_javascript_link_keeps_its_words_and_loses_its_href()
        {
            var stored = Stored("<p><a href=\"javascript:steal()\">Click here</a></p>");

            Assert.DoesNotContain("javascript:", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Click here", stored);
        }

        [Fact]
        public void A_tag_nobody_asked_for_is_dropped_and_its_words_kept()
        {
            var stored = Stored("<p>Before<iframe src=\"http://elsewhere\"></iframe>After</p>");

            Assert.DoesNotContain("iframe", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Before", stored);
            Assert.Contains("After", stored);
        }

        // ------------------------------------------------------------ what survives

        [Fact]
        public void The_formatting_the_editor_offers_survives_unchanged()
        {
            // The toolbar was chosen to fit this allow-list, so nothing an administrator can
            // produce from the buttons should be altered on the way in.
            const string written =
                "<p><strong>Bold</strong> <em>italic</em> <u>under</u></p><ul><li>One</li></ul>";

            Assert.Equal(written, Stored(written));
        }

        [Fact]
        public void A_real_link_survives_and_is_given_safe_attributes()
        {
            var stored = Stored("<p><a href=\"https://example.com/case\">Open</a></p>");

            Assert.Contains("https://example.com/case", stored);
            Assert.Contains("noopener", stored);
        }

        [Fact]
        public void The_button_token_is_untouched()
        {
            // It is plain text in the row and becomes markup only at render, from a value this
            // assembly builds. Cleaning must not disturb it or the button stops appearing.
            var stored = Stored("<p>Case {{reference}}.</p><p>{{caseButton}}</p>");

            Assert.Contains("{{caseButton}}", stored);
            Assert.Contains("{{reference}}", stored);
        }

        [Fact]
        public void A_plain_text_letter_is_cleaned_too()
        {
            // NotificationDrain puts the body in email.description, which renders as markup
            // whatever the catalogue calls the letter - so a tag in a "plain text" letter is
            // live markup all the same.
            var stored = Stored(
                "Hello<script>alert(1)</script>", NotificationTemplates.ReviewSubmitted);

            Assert.DoesNotContain("script", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Hello", stored);
        }

        // ------------------------------------------------------------ refusals

        [Fact]
        public void A_body_that_is_nothing_but_markup_is_refused_as_such()
        {
            // Distinguished from a body nobody typed. "It needs a body" to somebody who just
            // wrote one reads as a bug in the screen.
            var refusal = Refused("<script>alert(1)</script>");

            Assert.Contains("formatting this system keeps", refusal.Message);
        }

        [Fact]
        public void An_empty_body_still_says_it_is_empty()
        {
            var refusal = Refused("   ");

            Assert.Contains("needs a body", refusal.Message);
        }

        // ------------------------------------------------------------ scope

        [Fact]
        public void A_write_that_does_not_carry_a_body_leaves_the_stored_one_alone()
        {
            // An update changing the subject must not rewrite a body it never mentioned - the
            // Target has no body attribute at all, and adding one here would clear it.
            var row = new Entity(NotificationTemplates.TemplateEntity)
            {
                Id = Guid.NewGuid(),
                [NotificationTemplates.SubjectAttr] = "A new subject",
            };

            var service = new FakeOrganizationService();
            service.Seed(NotificationTemplates.TemplateEntity, row.Id,
                NotificationTemplates.CodeAttr, NotificationTemplates.CasePassed,
                NotificationTemplates.SubjectAttr, "Old subject",
                NotificationTemplates.BodyAttr, "<p>The stored body.</p>");

            var provider = new FakeServiceProvider(service);
            provider.Context.InputParameters["Target"] = row;
            provider.Context.MessageName = "Update";

            new NotificationTemplateGuardPlugin(null, null).Execute(provider);

            Assert.False(row.Contains(NotificationTemplates.BodyAttr));
        }
    }
}
