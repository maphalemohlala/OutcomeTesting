using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What the guard refuses once an administrator can invent a letter of their own (AD-168).
    ///
    /// <para>
    /// Every refusal here is about a row that would look like configuration and do nothing.
    /// That is the whole failure mode: unlike one of the twelve, a letter of your own has no
    /// compiled copy behind it, so a bad row does not fall back to the old wording - it simply
    /// never sends, and nothing says so.
    /// </para>
    /// </summary>
    public class NotificationTemplateCustomGuardTests
    {
        private const string Code = "TELL-THE-MANAGER";

        private static string Refuse(
            string subject = "A case passed",
            string body = "<p>Case {{reference}} passed.</p>",
            int? eventValue = NotificationOutbox.EventCasePassed,
            int? recipientKind = NotificationRecipients.KindTcManager,
            bool hasContact = false,
            string code = Code)
        {
            return NotificationTemplateGuardPlugin.Refusal(
                code, subject, body, eventValue, recipientKind, hasContact);
        }

        [Fact]
        public void A_letter_of_your_own_is_allowed_now()
        {
            // It was not before: any code outside the twelve was refused outright.
            Assert.Null(Refuse());
        }

        [Fact]
        public void A_letter_attached_to_nothing_is_refused()
        {
            var refusal = Refuse(eventValue: null);

            Assert.NotNull(refusal);
            Assert.Contains("attached to something", refusal);
        }

        [Fact]
        public void A_letter_attached_to_an_event_nothing_raises_is_refused()
        {
            var refusal = Refuse(eventValue: 999);

            Assert.NotNull(refusal);
            Assert.Contains("not an event this system raises", refusal);
        }

        [Fact]
        public void A_letter_with_nobody_to_go_to_is_refused()
        {
            var refusal = Refuse(recipientKind: null);

            Assert.NotNull(refusal);
            Assert.Contains("nobody to go to", refusal);
        }

        [Fact]
        public void A_recipient_this_system_cannot_work_out_is_refused()
        {
            var refusal = Refuse(recipientKind: 999);

            Assert.NotNull(refusal);
            Assert.Contains("not somebody this system can work out", refusal);
        }

        [Fact]
        public void Choosing_a_named_contact_without_naming_one_is_refused()
        {
            var refusal = Refuse(
                recipientKind: NotificationRecipients.KindContact, hasContact: false);

            Assert.NotNull(refusal);
            Assert.Contains("no contact is chosen", refusal);
        }

        [Fact]
        public void Choosing_a_named_contact_and_naming_one_is_allowed()
        {
            Assert.Null(Refuse(
                recipientKind: NotificationRecipients.KindContact, hasContact: true));
        }

        [Fact]
        public void An_empty_body_is_refused_and_says_there_is_no_fallback()
        {
            // The subtle one. For a built-in, an empty body falls back to the compiled copy.
            // For a letter of your own there is nothing to fall back to.
            var refusal = Refuse(body: "   ");

            Assert.NotNull(refusal);
            Assert.Contains("never sent at all", refusal);
        }

        [Fact]
        public void An_empty_subject_is_refused()
        {
            Assert.NotNull(Refuse(subject: " "));
        }

        [Fact]
        public void A_token_only_one_event_could_answer_is_refused()
        {
            // {{grading}} exists at the moment a remediation is raised and nowhere else. A
            // letter attached to an arbitrary event has no claim on it, and it would arrive as
            // a gap rather than an error.
            var refusal = Refuse(body: "<p>The grade was {{grading}}.</p>");

            Assert.NotNull(refusal);
            Assert.Contains("{{grading}}", refusal);
            Assert.Contains("{{reference}}", refusal);
        }

        [Fact]
        public void The_tokens_a_case_can_always_answer_are_allowed()
        {
            Assert.Null(Refuse(
                subject: "Case {{reference}}",
                body: "<p>{{client}}, advised by {{adviser}}. {{caseButton}} {{caseLink}}</p>"));
        }

        [Fact]
        public void A_built_in_letter_still_has_its_own_rules()
        {
            // The twelve are unchanged: their own token list still applies, and it is wider.
            Assert.Null(NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.RemediationHarm,
                "Remediation required on {{reference}}",
                "<p>{{grading}}</p>",
                NotificationOutbox.EventRemediationAssigned,
                null,
                false));
        }

        [Fact]
        public void A_built_in_letter_may_be_pointed_at_somebody_else()
        {
            Assert.Null(NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.CasePassed,
                "Your case passed",
                "<p>Case {{reference}} passed.</p>",
                null,
                NotificationRecipients.KindTcManager,
                false));
        }

        [Fact]
        public void A_built_in_letter_pointed_at_a_contact_still_needs_the_contact()
        {
            var refusal = NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.CasePassed,
                "Your case passed",
                "<p>Case {{reference}} passed.</p>",
                null,
                NotificationRecipients.KindContact,
                false);

            Assert.NotNull(refusal);
            Assert.Contains("no contact is chosen", refusal);
        }

        [Fact]
        public void A_template_with_no_code_is_still_refused()
        {
            Assert.NotNull(Refuse(code: "  "));
        }
    }
}
