using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Notification wording is editable data, and the compiled copy is the fallback
    /// (Change 1, AD-163).
    ///
    /// <para>
    /// The test that matters most is <see cref="The_compiled_copy_still_writes_todays_letter"/>.
    /// Moving every word of every email into a table is only safe if an environment with an
    /// empty table sends exactly what it sent before, and "exactly" has to mean byte for byte
    /// - this is the letter an adviser receives about a client's advice outcome.
    /// </para>
    /// <para>
    /// After that, the escaping direction. The template is markup an administrator wrote and
    /// is used as-is; the token VALUES are case data and are escaped. Getting that backwards
    /// either breaks every letter or lets a client name rewrite one.
    /// </para>
    /// </summary>
    public class NotificationTemplateTests
    {
        private const string Link = "https://portal.example.com/case?id=1&ref=2";

        // ------------------------------------------------- the copy has not changed

        [Fact]
        public void The_compiled_copy_still_writes_todays_letter()
        {
            // NotificationBodies is what this replaced. With no template row, the two must
            // agree character for character, or this change quietly rewrote every Pass
            // letter the moment it deployed.
            var expected = NotificationBodies.Pass("Jane Adviser", "A. Client", Link);

            var actual = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.CasePassed,
                Tokens(
                    NotificationTemplates.TokenAdviser, "Jane Adviser",
                    NotificationTemplates.TokenClient, "A. Client",
                    NotificationTemplates.TokenCaseButton,
                        NotificationTemplates.CaseButton(Link, "View the case"))).Body;

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void The_compiled_subject_still_matches_too()
        {
            var expected = NotificationBodies.PassSubject("IO-1");

            var actual = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.CasePassed,
                Tokens(NotificationTemplates.TokenReference, "IO-1")).Subject;

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(OutcomeRules.OutcomePassWithIssues, NotificationTemplates.RemediationPassWithIssues,
            "a pass with issues grading")]
        [InlineData(OutcomeRules.OutcomePotentialHarm, NotificationTemplates.RemediationHarm,
            "an insufficient evidence/ potential harm grading")]
        public void The_remedial_letter_is_unchanged_for_both_gradings(
            int outcome, string code, string grading)
        {
            // The branching letter, which is why it is two codes. A template language with
            // conditionals in it is a language, and the person editing this writes letters.
            var expected = NotificationBodies.Remediation(
                outcome, "Jane Adviser", "A. Client", Link, " It is due by 3 March 2026.");

            var actual = NotificationTemplates.Render(
                new FakeOrganizationService(),
                code,
                Tokens(
                    NotificationTemplates.TokenAdviser, "Jane Adviser",
                    NotificationTemplates.TokenClient, "A. Client",
                    NotificationTemplates.TokenGrading, grading,
                    NotificationTemplates.TokenDueText, " It is due by 3 March 2026.",
                    NotificationTemplates.TokenCaseButton,
                        NotificationTemplates.CaseButton(Link, "Confirm remedial action"))).Body;

            Assert.Equal(expected, actual);
        }

        // ------------------------------------------------------------- escaping

        [Fact]
        public void A_client_name_carrying_markup_cannot_rewrite_the_letter()
        {
            // The negative that matters. The value is data off the case; whatever it
            // contains, it is text in the finished letter and never markup.
            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.CasePassed,
                Tokens(NotificationTemplates.TokenClient, "<script>alert(1)</script>"));

            Assert.DoesNotContain("<script>", letter.Body);
            Assert.Contains("&lt;script&gt;", letter.Body);
        }

        [Fact]
        public void The_templates_own_markup_survives_untouched()
        {
            // The other half of the same rule. Escaping the template as well would put
            // &lt;p&gt; in front of a reader.
            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.CasePassed,
                Tokens(NotificationTemplates.TokenClient, "A. Client"));

            Assert.Contains("<p>", letter.Body);
        }

        [Fact]
        public void The_button_is_markup_and_is_not_escaped()
        {
            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.CasePassed,
                Tokens(
                    NotificationTemplates.TokenCaseButton,
                        NotificationTemplates.CaseButton(Link, "View the case")));

            Assert.Contains("<a href=", letter.Body);
            // The link's own ampersand is still escaped inside the attribute.
            Assert.Contains("&amp;ref=2", letter.Body);
        }

        [Fact]
        public void A_plain_text_body_is_not_html_escaped()
        {
            // Escaping a plain-text letter would show "&amp;" to a reader.
            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.ReviewSubmitted,
                Tokens(NotificationTemplates.TokenReference, "IO-1 & IO-2"));

            Assert.Contains("IO-1 & IO-2", letter.Body);
            Assert.DoesNotContain("&amp;", letter.Body);
        }

        [Fact]
        public void A_missing_token_leaves_a_gap_rather_than_its_own_name()
        {
            // Several letters are written around a fragment that is legitimately absent -
            // a due date, a note. Printing {{dueText}} would be worse than nothing.
            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.RemediationOther,
                Tokens(NotificationTemplates.TokenReference, "IO-1"));

            Assert.DoesNotContain("{{", letter.Body);
        }

        // ------------------------------------------------------- the stored template

        [Fact]
        public void A_stored_template_replaces_the_compiled_copy()
        {
            var service = Stored(NotificationTemplates.SignoffDue,
                "New subject for {{reference}}", "New body for {{reference}}.");

            var letter = NotificationTemplates.Render(
                service, NotificationTemplates.SignoffDue,
                Tokens(NotificationTemplates.TokenReference, "IO-9"));

            Assert.True(letter.FromTemplate);
            Assert.Equal("New subject for IO-9", letter.Subject);
            Assert.Equal("New body for IO-9.", letter.Body);
        }

        [Fact]
        public void An_inactive_template_is_ignored()
        {
            var service = new FakeOrganizationService();
            var row = service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, NotificationTemplates.SignoffDue,
                NotificationTemplates.SubjectAttr, "Should not be used",
                NotificationTemplates.BodyAttr, "Should not be used");
            row["statecode"] = new OptionSetValue(1);

            Assert.False(
                NotificationTemplates.Render(service, NotificationTemplates.SignoffDue, null)
                    .FromTemplate);
        }

        [Fact]
        public void A_template_with_only_half_its_wording_is_ignored()
        {
            // A subject with an empty body would send a letter with nothing in it, which
            // reads as a fault and is worse than wording that is out of date.
            var service = Stored(NotificationTemplates.SignoffDue, "Only a subject", "   ");

            Assert.False(
                NotificationTemplates.Render(service, NotificationTemplates.SignoffDue, null)
                    .FromTemplate);
        }

        [Fact]
        public void A_template_that_cannot_be_read_still_sends_the_letter()
        {
            // This runs inside the transaction that caused the notification. An unreadable
            // template must cost the wording, never the submit.
            var letter = NotificationTemplates.Render(
                new ThrowingService(), NotificationTemplates.SignoffDue,
                Tokens(NotificationTemplates.TokenReference, "IO-1"));

            Assert.False(letter.FromTemplate);
            Assert.Contains("IO-1", letter.Body);
        }

        [Fact]
        public void An_unknown_code_is_a_programming_error_and_throws()
        {
            // Unlike every other failure here. A code that is not in the catalogue means the
            // caller asked for a letter that does not exist, which no fallback can guess.
            Assert.Throws<ArgumentException>(
                () => NotificationTemplates.Render(new FakeOrganizationService(), "NOPE", null));
        }

        // ------------------------------------------------------------- validation

        [Fact]
        public void A_token_the_letter_never_supplies_is_reported()
        {
            var offenders = NotificationTemplates.UnknownTokens(
                NotificationTemplates.SignoffDue, "Subject", "Body with {{clientName}} in it.");

            Assert.Equal(new[] { "clientName" }, offenders);
        }

        [Fact]
        public void A_token_that_belongs_to_a_different_letter_is_still_refused()
        {
            // {{grading}} is real, and meaningless here: the sign-off-due letter has no
            // grading to put in it, so it would render as a gap in something somebody reads.
            var offenders = NotificationTemplates.UnknownTokens(
                NotificationTemplates.SignoffDue, "Subject", "Body {{grading}}.");

            Assert.Equal(new[] { "grading" }, offenders);
        }

        [Fact]
        public void The_letters_own_tokens_pass_validation()
        {
            foreach (var definition in NotificationTemplates.All)
            {
                Assert.Empty(NotificationTemplates.UnknownTokens(
                    definition.Code, definition.Subject, definition.Body));
            }
        }

        [Fact]
        public void Every_letter_has_a_code_a_name_a_subject_and_a_body()
        {
            foreach (var definition in NotificationTemplates.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(definition.Code));
                Assert.False(string.IsNullOrWhiteSpace(definition.Name));
                Assert.False(string.IsNullOrWhiteSpace(definition.Subject));
                Assert.False(string.IsNullOrWhiteSpace(definition.Body));
            }
        }

        // ------------------------------------------------------------- helpers

        private static IDictionary<string, string> Tokens(params string[] pairs)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                map[pairs[i]] = pairs[i + 1];
            }

            return map;
        }

        // ------------------------------------------------------------ stored bodies are markup

        [Fact]
        public void A_stored_body_escapes_its_token_values_even_on_a_plain_text_letter()
        {
            // AD-170. Every letter is edited in a rich-text editor now, so a stored body is
            // markup whatever the catalogue calls the letter - and email.description renders
            // as markup regardless. A client name carrying an ampersand has to be escaped here
            // or it lands in the markup raw.
            var service = Stored(
                NotificationTemplates.RecheckDue,
                "Recheck due",
                "<p>{{reference}} for {{client}}.</p>");

            var letter = NotificationTemplates.Render(
                service,
                NotificationTemplates.RecheckDue,
                new Dictionary<string, string>
                {
                    { NotificationTemplates.TokenReference, "OT-1" },
                    { NotificationTemplates.TokenClient, "Smith & Co <script>" },
                });

            Assert.True(letter.FromTemplate);
            Assert.Contains("Smith &amp; Co", letter.Body);
            Assert.DoesNotContain("<script>", letter.Body);
        }

        [Fact]
        public void The_compiled_copy_of_a_plain_text_letter_is_unchanged()
        {
            // The other half of AD-170, and the reason the flag was not simply flipped: the
            // fallback is code-authored, known safe, and compared byte for byte in
            // NotificationBodiesTests. An environment holding no rows sends what it always did.
            var letter = NotificationTemplates.Render(
                new FakeOrganizationService(),
                NotificationTemplates.RecheckDue,
                new Dictionary<string, string>
                {
                    { NotificationTemplates.TokenReference, "Smith & Co" },
                });

            Assert.False(letter.FromTemplate);
            Assert.Contains("Smith & Co", letter.Body);
            Assert.DoesNotContain("&amp;", letter.Body);
        }

        private static FakeOrganizationService Stored(string code, string subject, string body)
        {
            var service = new FakeOrganizationService();
            service.Seed(NotificationTemplates.TemplateEntity, Guid.NewGuid(),
                NotificationTemplates.CodeAttr, code,
                NotificationTemplates.SubjectAttr, subject,
                NotificationTemplates.BodyAttr, body,
                "statecode", new OptionSetValue(0));
            return service;
        }

        /// <summary>
        /// A service whose every read fails, standing in for a table that is not there -
        /// which is exactly the state an environment is in before the solution carrying
        /// al_notificationtemplate has been imported.
        /// </summary>
        private sealed class ThrowingService : IOrganizationService
        {
            public Microsoft.Xrm.Sdk.EntityCollection RetrieveMultiple(
                Microsoft.Xrm.Sdk.Query.QueryBase query)
            {
                throw new InvalidOperationException("no such table");
            }

            public Entity Retrieve(string entityName, Guid id, Microsoft.Xrm.Sdk.Query.ColumnSet columnSet)
            {
                throw new InvalidOperationException("no such table");
            }

            public Guid Create(Entity entity)
            {
                throw new NotSupportedException();
            }

            public void Update(Entity entity)
            {
                throw new NotSupportedException();
            }

            public void Delete(string entityName, Guid id)
            {
                throw new NotSupportedException();
            }

            public OrganizationResponse Execute(OrganizationRequest request)
            {
                throw new NotSupportedException();
            }

            public void Associate(
                string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            {
                throw new NotSupportedException();
            }

            public void Disassociate(
                string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
            {
                throw new NotSupportedException();
            }
        }
    }
}
