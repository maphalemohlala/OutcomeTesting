using System.Linq;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122: eight question codes are read by code, not just by reviewers. Retiring or
    /// moving one is refused, and the refusal names what would break.
    /// </summary>
    public class ChecklistGuardsTests
    {
        [Theory]
        [InlineData("Q-GR-01")]
        [InlineData("Q-TAX-02")]
        [InlineData("Q-FQ-01")]
        [InlineData("Q-FQTAX-01")]
        [InlineData("Q-FQ-02")]
        [InlineData("Q-FQTAX-02")]
        [InlineData("Q-FQ-03")]
        [InlineData("Q-FQTAX-03")]
        public void Each_load_bearing_code_is_protected_and_explains_itself(string code)
        {
            var reason = ChecklistGuards.ProtectedReason(code);

            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.EndsWith(".", reason);
        }

        [Fact]
        public void An_ordinary_question_is_not_protected()
        {
            Assert.Null(ChecklistGuards.ProtectedReason("Q-E1-01"));
            Assert.Null(ChecklistGuards.ProtectedReason("Q-AML-01"));
        }

        [Fact]
        public void The_code_is_matched_without_regard_to_case()
        {
            Assert.NotNull(ChecklistGuards.ProtectedReason("q-gr-01"));
        }

        [Fact]
        public void A_missing_or_empty_code_is_not_protected()
        {
            Assert.Null(ChecklistGuards.ProtectedReason(null));
            Assert.Null(ChecklistGuards.ProtectedReason(""));
        }

        [Fact]
        public void Exactly_eight_codes_are_protected()
        {
            // Pinned deliberately. A ninth appearing here without a matching constant in the
            // plug-in that reads it is how this guard drifts out of truth.
            Assert.Equal(8, ChecklistGuards.ProtectedCodes.Count());
        }
    }
}
