using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// A template that would send a broken letter is refused server-side (Change 1, AD-163).
    ///
    /// <para>
    /// These are refusals, not warnings. The table is reachable from the Web API and from any
    /// other client, so a check that lived only in the Code App screen would be a suggestion -
    /// and what gets through is not a page that looks wrong, it is a letter somebody receives
    /// about a client's advice outcome.
    /// </para>
    /// <para>
    /// The empty-body refusal is the subtle one. Without it the row is simply ignored at send
    /// time and the compiled copy goes out instead: correct, and indistinguishable from the
    /// save having worked. The administrator would believe they had changed the letter.
    /// </para>
    /// </summary>
    public class NotificationTemplateGuardTests
    {
        [Fact]
        public void Accepts_every_letter_this_solution_ships()
        {
            // The catalogue must satisfy its own guard, or seeding would be refused by the
            // very rule the seed is meant to demonstrate.
            foreach (var definition in NotificationTemplates.All)
            {
                Assert.Null(NotificationTemplateGuardPlugin.Refusal(
                    definition.Code, definition.Subject, definition.Body));
            }
        }

        [Fact]
        public void Refuses_a_code_the_solution_never_reads()
        {
            var refusal = NotificationTemplateGuardPlugin.Refusal("CASE-PASSSED", "Subject", "Body");

            Assert.NotNull(refusal);
            // The value is named, because a typo is the likely cause and the administrator
            // needs to see which one it was.
            Assert.Contains("CASE-PASSSED", refusal);
        }

        [Fact]
        public void Refuses_a_missing_code()
        {
            Assert.NotNull(NotificationTemplateGuardPlugin.Refusal("   ", "Subject", "Body"));
        }

        [Fact]
        public void Refuses_an_empty_subject()
        {
            Assert.NotNull(NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.SignoffDue, "  ", "Body"));
        }

        [Fact]
        public void Refuses_an_empty_body_and_says_why_it_matters()
        {
            // Not "this field is required". The reason is that the save would look like it
            // worked while the built-in wording went out instead.
            var refusal = NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.SignoffDue, "Subject", "   ");

            Assert.NotNull(refusal);
            Assert.Contains("fall back", refusal);
        }

        [Fact]
        public void Refuses_a_token_the_letter_never_supplies()
        {
            var refusal = NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.SignoffDue,
                "Sign-off needed on case {{reference}}",
                "Dear {{adviser}}, case {{reference}} needs you.");

            Assert.NotNull(refusal);
            Assert.Contains("adviser", refusal);
        }

        [Fact]
        public void Tells_the_administrator_which_tokens_they_may_use()
        {
            // A refusal that only says no leaves someone guessing at a token list they
            // cannot see from the editor.
            var refusal = NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.SignoffDue, "Subject", "{{nonsense}}");

            Assert.Contains("{{reference}}", refusal);
        }

        [Fact]
        public void Reads_naturally_when_several_tokens_are_wrong()
        {
            var refusal = NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.SignoffDue, "Subject", "{{one}} {{two}} {{three}}");

            // Braced since AD-168: the offenders were reported bare while the allowed
            // set beside them was braced, so one sentence named tokens two ways.
            Assert.Contains("{{one}}, {{two}} and {{three}}", refusal);
            Assert.Contains("They would render as gaps", refusal);
        }

        [Fact]
        public void A_letters_own_tokens_are_accepted()
        {
            Assert.Null(NotificationTemplateGuardPlugin.Refusal(
                NotificationTemplates.SignoffApprovedClosed,
                "Remediation approved on case {{reference}}",
                "Closed with a final outcome of {{finalOutcome}}.{{notes}}"));
        }
    }
}
