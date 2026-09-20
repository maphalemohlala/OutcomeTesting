using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// What a refusal tells an administrator they MAY use.
    ///
    /// F25, found in DEV on 2026-09-20 while working APP-142. Both refusals ended with a list
    /// of the tokens the letter can use, and both built that list from the letter's own tokens
    /// alone — while the rule that produced the refusal judged against those tokens PLUS
    /// <see cref="NotificationTemplates.AlwaysAllowed"/>.
    ///
    /// So a letter that accepts <c>{{completedCheck}}</c> — proved against DEV over the Web
    /// API, which took the row — told an administrator it did not. That sentence is the only
    /// place the allowed set is written down at the moment somebody needs it, and the token it
    /// left out is the one that attaches the completed check to the email.
    ///
    /// These assert the list against the rule rather than against a list written here, so a
    /// token added to either set stays covered.
    /// </summary>
    public class TemplateRefusalTokenListTests
    {
        [Fact]
        public void ListsWhatIsAlwaysAllowedAlongsideTheLettersOwn()
        {
            var allowed = NotificationTemplates.AllowedTokens(new[] { "reference" });

            Assert.Contains("reference", allowed);
            Assert.Contains(NotificationTemplates.TokenCompletedCheck, allowed);
        }

        [Fact]
        public void DoesNotNameTheSameTokenTwice()
        {
            // A letter whose own list already carries it would otherwise read
            // "… and {{completedCheck}} and {{completedCheck}}".
            var allowed = NotificationTemplates.AllowedTokens(
                new[] { "reference", NotificationTemplates.TokenCompletedCheck });

            Assert.Equal(2, allowed.Length);
        }

        [Fact]
        public void SurvivesALetterWithNoTokensOfItsOwn()
        {
            Assert.Equal(NotificationTemplates.AlwaysAllowed, NotificationTemplates.AllowedTokens(null));
        }

        [Fact]
        public void BuiltInRefusalNamesEveryTokenTheLetterWouldActuallyAccept()
        {
            // ALLOCATION-NO-LINK supplies one token. The refusal used to stop there.
            var refusal = NotificationTemplateGuardPlugin.Refusal(
                "ALLOCATION-NO-LINK",
                "Case {{reference}}",
                "Case {{reference}} for {{notARealToken}}.");

            Assert.Contains("{{reference}}", refusal);
            Assert.Contains("{{" + NotificationTemplates.TokenCompletedCheck + "}}", refusal);
        }

        [Fact]
        public void CustomRefusalNamesEveryTokenALetterOfYourOwnWouldActuallyAccept()
        {
            var refusal = NotificationTemplateGuardPlugin.Refusal(
                "UAT-BAD-TOKEN",
                "Heads up on {{reference}}",
                "Case {{reference}} graded {{grading}}.",
                NotificationOutbox.EventAllocation,
                NotificationRecipients.KindChecker,
                false);

            Assert.Contains("{{grading}}", refusal);
            Assert.Contains("{{" + NotificationTemplates.TokenCompletedCheck + "}}", refusal);
        }

        [Fact]
        public void EveryTokenARefusalNamesIsOneTheGuardWouldAccept()
        {
            // The sentence and the rule, checked against each other rather than against a
            // transcription: a letter using only what the refusal listed must then save.
            foreach (var definition in NotificationTemplates.All)
            {
                var body = "Body";
                foreach (var token in NotificationTemplates.AllowedTokens(definition.Tokens))
                {
                    body += " {{" + token + "}}";
                }

                Assert.Null(NotificationTemplateGuardPlugin.Refusal(definition.Code, "Subject", body));
            }
        }
    }
}
