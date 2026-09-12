using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-123. Retiring a section takes its questions out of the form just as surely as
    /// retiring them individually would, so a section holding a load-bearing code is
    /// refused for the same reason.
    /// </summary>
    public class RetireSectionPluginTests
    {
        [Fact]
        public void A_section_of_ordinary_questions_may_be_retired()
        {
            Assert.Null(RetireSectionPlugin.ProtectedCodeIn(new[] { "Q-E1-01", "Q-E1-02" }));
        }

        [Fact]
        public void A_section_holding_a_protected_question_is_refused_and_names_it()
        {
            var refusal = RetireSectionPlugin.ProtectedCodeIn(new[] { "Q-E1-01", "Q-GR-01" });

            Assert.NotNull(refusal);
            Assert.Contains("Q-GR-01", refusal);
        }

        [Fact]
        public void An_empty_section_may_be_retired()
        {
            Assert.Null(RetireSectionPlugin.ProtectedCodeIn(new string[0]));
        }

        [Fact]
        public void A_question_with_no_code_does_not_break_the_check()
        {
            // al_questioncode is required, but a Retrieve that did not ask for it returns
            // null - and a guard that throws on the way to refusing is no guard.
            Assert.Null(RetireSectionPlugin.ProtectedCodeIn(new string[] { null, "Q-E1-01" }));
        }
    }
}
