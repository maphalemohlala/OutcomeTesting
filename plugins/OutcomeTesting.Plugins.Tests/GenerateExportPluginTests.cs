using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// GenerateExportPlugin.FlaggedText and IsAccountable (AD-039 fail accountability
    /// columns). Pure and
    /// takes plain Entity objects, so it is testable without a fake IOrganizationService —
    /// the exact locus of the "silent wrong column" risk: a transposed FQ/AQ or
    /// adviser/paraplanner pair would attribute a fail to the wrong discipline or the
    /// wrong person, and nothing else would catch it.
    /// </summary>
    public class GenerateExportPluginTests
    {
        [Fact]
        public void Names_the_person_a_recorded_judgement_flags()
        {
            var outcomeRow = new Entity("al_outcome")
            {
                [GenerateExportPlugin.FqAdviserFlag] = true,
            };
            var outcomeCase = new Entity("al_outcomecase")
            {
                ["al_advisername"] = "A. Adviser",
            };

            Assert.Equal("A. Adviser", GenerateExportPlugin.FlaggedText(
                GenerateExportPlugin.IsAccountable(
                    outcomeRow, GenerateExportPlugin.FqAdviserFlag, ResponseRules.ChoiceFail, null),
                outcomeCase, "al_advisername"));
        }

        [Fact]
        public void Writes_an_empty_pair_for_someone_who_is_not_accountable()
        {
            // AD-039 is a fixed-position contract: a pair that does not carry the fail is
            // written empty rather than filled in with a name that is merely on the case.
            var outcomeCase = new Entity("al_outcomecase")
            {
                ["al_advisername"] = "A. Adviser",
            };

            Assert.Equal(string.Empty, GenerateExportPlugin.FlaggedText(false, outcomeCase, "al_advisername"));
        }

        [Fact]
        public void Writes_empty_when_the_case_does_not_name_that_person()
        {
            // Being accountable is not the same as being identifiable. A case that names
            // no paraplanner exports the pair empty rather than the word "null".
            var outcomeCase = new Entity("al_outcomecase");

            Assert.Equal(string.Empty, GenerateExportPlugin.FlaggedText(true, outcomeCase, "al_paraplanner"));
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
        public void A_tax_only_case_carrying_a_non_pass_outcome_exports_and_is_attributed()
        {
            // An Outcome that exists is a fact about the case whatever its route. It no
            // longer blocks the batch, and the adviser carries the advice quality fail.
            var row = Outcome(OutcomeRules.OutcomePotentialHarm);

            Assert.Null(GenerateExportPlugin.DescribeIncompleteRow(row, null, false));
            Assert.True(GenerateExportPlugin.IsAccountable(
                row, GenerateExportPlugin.AqAdviserFlag, null, Outcomes.EffectiveOutcome(row)));
        }
        [Fact]
        public void Does_not_ask_a_case_regraded_to_a_pass_for_accountability()
        {
            // A regrade writes al_finaloutcome on the final scale (1209107_1_x), which the
            // gate measured against the initial scale. A final Pass matched nothing, read as
            // a non-pass, and blocked the whole batch on a case that had passed on review.
            Assert.Null(GenerateExportPlugin.DescribeIncompleteRow(
                Regraded(OutcomeRules.OutcomeInsufficient, OutcomeRules.FinalOutcomePass), "Pass", true));
        }

        [Fact]
        public void A_regrade_that_lands_on_a_fail_is_attributed_to_the_adviser()
        {
            // A regrade down to a fail owes AD-039 its accountability columns just as a
            // first-time fail does, and the grade it is judged on is the final one.
            var row = Regraded(OutcomeRules.OutcomePass, OutcomeRules.FinalOutcomePotentialHarm);

            Assert.True(GenerateExportPlugin.IsAccountable(
                row, GenerateExportPlugin.AqAdviserFlag, null, Outcomes.EffectiveOutcome(row)));
        }

        private static Entity Regraded(int initial, int final)
        {
            return new Entity("al_outcome")
            {
                [Outcomes.InitialOutcomeAttr] = new OptionSetValue(initial),
                [Outcomes.FinalOutcomeAttr] = new OptionSetValue(final),
            };
        }
        // ---- Derived accountability (project owner, 2026-09-16) ----
        //
        // "The assigned paraplanner and adviser should be accountable respectively":
        // the paraplanner compiles the file so a File Quality fail is theirs, the adviser
        // gives the advice so an Advice Quality fail is theirs. Derived as a default and
        // still overridable by a recorded judgement.

        private static Entity CaseWithBoth()
        {
            return new Entity("al_outcomecase")
            {
                ["al_advisername"] = "Jane Adviser",
                ["al_advisercode"] = "ADV-1",
                ["al_paraplanner"] = "Paul Paraplanner",
                ["al_paraplannercode"] = "PP-1",
            };
        }

        [Fact]
        public void A_file_quality_fail_makes_the_paraplanner_accountable()
        {
            Assert.True(GenerateExportPlugin.IsAccountable(
                null, GenerateExportPlugin.FqParaplannerFlag, ResponseRules.ChoiceFail, null));
        }

        [Fact]
        public void A_file_quality_fail_does_not_make_the_adviser_accountable()
        {
            Assert.False(GenerateExportPlugin.IsAccountable(
                null, GenerateExportPlugin.FqAdviserFlag, ResponseRules.ChoiceFail, null));
        }

        [Fact]
        public void An_advice_quality_fail_makes_the_adviser_accountable()
        {
            Assert.True(GenerateExportPlugin.IsAccountable(
                null, GenerateExportPlugin.AqAdviserFlag, null, OutcomeRules.OutcomePotentialHarm));
        }

        [Fact]
        public void An_advice_quality_fail_does_not_make_the_paraplanner_accountable()
        {
            Assert.False(GenerateExportPlugin.IsAccountable(
                null, GenerateExportPlugin.AqParaplannerFlag, null, OutcomeRules.OutcomePotentialHarm));
        }

        [Fact]
        public void A_clean_case_makes_nobody_accountable()
        {
            foreach (var flag in new[]
            {
                GenerateExportPlugin.FqAdviserFlag, GenerateExportPlugin.FqParaplannerFlag,
                GenerateExportPlugin.AqAdviserFlag, GenerateExportPlugin.AqParaplannerFlag,
            })
            {
                Assert.False(GenerateExportPlugin.IsAccountable(
                    null, flag, ResponseRules.ChoicePass, OutcomeRules.OutcomePass));
            }
        }

        [Fact]
        public void A_tax_only_case_still_attributes_its_file_quality_fail()
        {
            // No al_outcome row exists on a Tax-only case, which is why nothing could be
            // recorded against one. Deriving from the case needs no such row.
            Assert.True(GenerateExportPlugin.IsAccountable(
                null, GenerateExportPlugin.FqParaplannerFlag, ResponseRules.ChoiceFail, null));
            Assert.False(GenerateExportPlugin.IsAccountable(
                null, GenerateExportPlugin.AqAdviserFlag, ResponseRules.ChoiceFail, null));
        }

        [Fact]
        public void A_recorded_judgement_overrides_the_derived_pair()
        {
            // Someone looked and decided the adviser was responsible for the file, not the
            // paraplanner. The recorded set is taken exactly as it was recorded.
            var recorded = new Entity("al_outcome")
            {
                ["al_fqadviseraccountable"] = true,
            };

            Assert.True(GenerateExportPlugin.IsAccountable(
                recorded, GenerateExportPlugin.FqAdviserFlag, ResponseRules.ChoiceFail, null));
            Assert.False(GenerateExportPlugin.IsAccountable(
                recorded, GenerateExportPlugin.FqParaplannerFlag, ResponseRules.ChoiceFail, null));
        }

        [Fact]
        public void An_outcome_row_with_nothing_recorded_still_derives()
        {
            // An AQS case has an al_outcome row whether or not anyone judged it. A row of
            // four falses is "nobody has said yet", which is exactly when to derive.
            var untouched = new Entity("al_outcome");

            Assert.True(GenerateExportPlugin.IsAccountable(
                untouched, GenerateExportPlugin.AqAdviserFlag, null, OutcomeRules.OutcomeInsufficient));
        }

        [Fact]
        public void The_accountable_person_is_named_from_the_case()
        {
            Assert.Equal("Paul Paraplanner",
                GenerateExportPlugin.FlaggedText(true, CaseWithBoth(), "al_paraplanner"));
            Assert.Equal(string.Empty,
                GenerateExportPlugin.FlaggedText(false, CaseWithBoth(), "al_paraplanner"));
        }

        [Fact]
        public void A_non_pass_no_longer_blocks_the_export()
        {
            // The OD-024 gate refused a non-pass that recorded no accountability. With a
            // derived default the columns are never blank on a fail, so the gate can only
            // refuse rows that are in fact complete.
            Assert.Null(GenerateExportPlugin.DescribeIncompleteRow(
                Outcome(OutcomeRules.OutcomePotentialHarm), "Fail", true));
        }
    }
}
