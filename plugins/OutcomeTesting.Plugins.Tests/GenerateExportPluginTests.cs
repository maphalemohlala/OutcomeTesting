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

    /// <summary>
    /// GenerateExportPlugin.DescribeIncompleteRow — the gate that keeps a partly graded case
    /// out of a delivered Trail Light file. AD-039 is a fixed-position contract, so a blank
    /// in a graded column reads downstream as an empty assessment rather than as an absent
    /// one, and nothing after this point would catch it.
    /// </summary>
    public class DescribeIncompleteRowTests
    {
        private static Entity Outcome(int? effective, params string[] accountableFlags)
        {
            var outcome = new Entity("al_outcome");
            if (effective.HasValue)
            {
                outcome[Outcomes.InitialOutcomeAttr] = new OptionSetValue(effective.Value);
            }

            foreach (var flag in accountableFlags)
            {
                outcome[flag] = true;
            }

            return outcome;
        }

        [Fact]
        public void Allows_a_pass_with_a_file_quality_grade()
        {
            Assert.Null(GenerateExportPlugin.DescribeIncompleteRow(Outcome(OutcomeRules.OutcomePass), "Pass", true));
        }

        [Fact]
        public void Refuses_a_closed_case_that_has_no_outcome_at_all()
        {
            // The regression this gate was added for: the old check sat inside
            // `if (outcomeRow != null)`, so this case skipped every rule and exported a row
            // of blanks. Reachable by a manager moving a case Submitted -> Closed (AD-057).
            var reason = GenerateExportPlugin.DescribeIncompleteRow(null, "Pass", true);

            Assert.NotNull(reason);
            Assert.Contains("no outcome recorded", reason);
        }

        [Fact]
        public void Refuses_an_outcome_carrying_neither_an_initial_nor_a_final_grade()
        {
            var reason = GenerateExportPlugin.DescribeIncompleteRow(Outcome(null), "Pass", true);

            Assert.NotNull(reason);
            Assert.Contains("Advice Quality grade", reason);
        }

        [Fact]
        public void Refuses_a_non_pass_with_no_accountability_recorded()
        {
            // OD-024: four blank accountability pairs read as "nobody is responsible"
            // rather than "nobody has said yet".
            var reason = GenerateExportPlugin.DescribeIncompleteRow(
                Outcome(OutcomeRules.OutcomePotentialHarm), "Fail", true);

            Assert.NotNull(reason);
            Assert.Contains("fail accountability", reason);
        }

        [Theory]
        [InlineData("al_fqadviseraccountable")]
        [InlineData("al_fqparaplanneraccountable")]
        [InlineData("al_aqadviseraccountable")]
        [InlineData("al_aqparaplanneraccountable")]
        public void Allows_a_non_pass_once_any_one_of_the_four_flags_is_set(string flag)
        {
            Assert.Null(GenerateExportPlugin.DescribeIncompleteRow(
                Outcome(OutcomeRules.OutcomePotentialHarm, flag), "Fail", true));
        }

        [Fact]
        public void Does_not_ask_a_pass_for_accountability()
        {
            // BR-006 attaches accountability to a non-pass only, so requiring it on a pass
            // would block every clean case.
            Assert.Null(GenerateExportPlugin.DescribeIncompleteRow(Outcome(OutcomeRules.OutcomePass), "Pass", true));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Refuses_a_case_with_no_file_quality_grade(string grade)
        {
            // Q-FQ-01 is answered on the AQS review, so an AQS case that reaches Closed
            // without one would ship a blank AD-039 column 10.
            var reason = GenerateExportPlugin.DescribeIncompleteRow(Outcome(OutcomeRules.OutcomePass), grade, true);

            Assert.NotNull(reason);
            Assert.Contains("File Quality", reason);
        }

        [Fact]
        public void Reports_the_missing_grade_before_the_missing_file_quality_answer()
        {
            // Both are wrong on a case with nothing recorded; naming the outcome first
            // points the operator at the cause rather than at a downstream symptom.
            var reason = GenerateExportPlugin.DescribeIncompleteRow(null, null, true);

            Assert.Contains("no outcome recorded", reason);
        }

        [Fact]
        public void Exports_a_tax_only_case_with_both_graded_columns_blank()
        {
            // AD-075 settles OD-031: a Tax-only case has no AQS review, so columns 10 and 15
            // have no source and never will. Refusing it blocked the whole batch.
            Assert.Null(GenerateExportPlugin.DescribeIncompleteRow(null, null, false));
        }

        [Fact]
        public void Still_refuses_a_tax_only_case_whose_outcome_is_an_ungraded_non_pass()
        {
            // An Outcome that exists is a fact about the case whatever its route, so the
            // OD-024 accountability rule is not relaxed with the missing-value rules.
            var reason = GenerateExportPlugin.DescribeIncompleteRow(
                Outcome(OutcomeRules.OutcomePotentialHarm), null, false);

            Assert.NotNull(reason);
            Assert.Contains("fail accountability", reason);
        }
    }
}
