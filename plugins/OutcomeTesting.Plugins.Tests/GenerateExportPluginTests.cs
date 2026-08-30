using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// GenerateExportPlugin.FlaggedText (AD-039 fail accountability columns). Pure and
    /// takes plain Entity objects, so it is testable without a fake IOrganizationService —
    /// the exact locus of the "silent wrong column" risk: a transposed FQ/AQ or
    /// adviser/paraplanner pair would attribute a fail to the wrong discipline or the
    /// wrong person, and nothing else would catch it.
    /// </summary>
    public class GenerateExportPluginTests
    {
        [Fact]
        public void Returns_the_case_value_when_the_flag_is_true()
        {
            var outcomeRow = new Entity("al_outcome")
            {
                ["al_fqadviseraccountable"] = true,
            };
            var outcomeCase = new Entity("al_outcomecase")
            {
                ["al_advisername"] = "A. Adviser",
            };

            Assert.Equal(
                "A. Adviser",
                GenerateExportPlugin.FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisername"));
        }

        [Fact]
        public void Returns_empty_when_the_flag_is_false()
        {
            var outcomeRow = new Entity("al_outcome")
            {
                ["al_fqadviseraccountable"] = false,
            };
            var outcomeCase = new Entity("al_outcomecase")
            {
                ["al_advisername"] = "A. Adviser",
            };

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisername"));
        }

        [Fact]
        public void Returns_empty_when_the_flag_is_absent()
        {
            // An unset flag is not accountable: AD-039 writes a pair empty unless its
            // flag is explicitly true.
            var outcomeRow = new Entity("al_outcome");
            var outcomeCase = new Entity("al_outcomecase")
            {
                ["al_advisername"] = "A. Adviser",
            };

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisername"));
        }

        [Fact]
        public void Returns_empty_when_the_outcome_row_is_null()
        {
            var outcomeCase = new Entity("al_outcomecase")
            {
                ["al_advisername"] = "A. Adviser",
            };

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(null, "al_fqadviseraccountable", outcomeCase, "al_advisername"));
        }

        [Fact]
        public void Returns_empty_when_the_case_attribute_is_missing()
        {
            var outcomeRow = new Entity("al_outcome")
            {
                ["al_fqadviseraccountable"] = true,
            };
            var outcomeCase = new Entity("al_outcomecase");

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisername"));
        }
    }
}
