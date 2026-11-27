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
            Assert.Null(ChecklistGuards.ProtectedReason("Q-TAX-01"));
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
        public void Exactly_eighteen_codes_and_one_prefix_are_protected()
        {
            // Pinned deliberately. A code appearing here without a matching read in the
            // plug-in or portal template that uses it is how this guard drifts out of truth.
            // Q-GR-02 joined on 2026-09-19, when GradingRules began reading it by name (item 3);
            // the Tax categories, the two notes cards' codes and the Q-AML prefix on 2026-11-16,
            // when ChecklistGuardsFixTests began checking every literal read against this list.
            // Q-TAX-09 / Q-TAX-10 left on 2026-11-25, when Omissions was retired at UAT.
            // Q-TAX-04 left on 2026-11-26: the Tax categories replaced it and it is read only for
            // earlier checks' notes (ChecklistGuardsFixTests.ReadForHistoryOnly).
            Assert.Equal(18, ChecklistGuards.ProtectedCodes.Count());
            Assert.Equal(new[] { "Q-AML" }, ChecklistGuards.ProtectedCodePrefixes.ToArray());
        }
    }
}
