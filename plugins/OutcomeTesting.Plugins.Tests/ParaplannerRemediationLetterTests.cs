using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The para-planner's letter is built outside <see cref="NotificationTemplates"/> (it is
    /// not editable - see its own doc comment), but its Body is still an HTML letter body sent
    /// the same way as every other, so the case reference it carries has to be escaped the
    /// same way (final-review fix wave, 2026-10-05).
    /// </summary>
    public class ParaplannerRemediationLetterTests
    {
        [Fact]
        public void Body_escapes_a_reference_carrying_markup_characters()
        {
            var body = ParaplannerRemediationLetter.Body("300000006 & Co");

            Assert.Equal("The checks and remedial points for case 300000006 &amp; Co are attached.", body);
        }

        [Fact]
        public void Body_falls_back_when_the_reference_is_blank()
        {
            Assert.Equal(
                "The checks and remedial points for this case are attached.",
                ParaplannerRemediationLetter.Body(null));
        }
    }
}
