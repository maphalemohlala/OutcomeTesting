using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Which case column holds which discipline's checker (item 2, 2026-09-19).
    ///
    /// Mirrored by app/src/features/cases/checkerNames.test.ts, which asks the same questions
    /// of the TypeScript copy.
    /// </summary>
    public class CheckerNamesTests
    {
        [Fact]
        public void A_tax_review_stamps_the_tax_checker()
        {
            Assert.Equal("al_taxcheckername", CheckerNames.AttributeFor(ResponseRules.ReviewTypeTax));
        }

        [Fact]
        public void An_aqs_review_stamps_the_aqs_checker()
        {
            Assert.Equal("al_aqscheckername", CheckerNames.AttributeFor(ResponseRules.ReviewTypeAqs));
        }

        [Fact]
        public void The_two_columns_are_not_the_same_column()
        {
            // The defect item 2 fixes, stated as a test: one column for both disciplines meant
            // the case named whichever checker was allocated second.
            Assert.NotEqual(CheckerNames.TaxAttribute, CheckerNames.AqsAttribute);
        }

        [Fact]
        public void A_review_type_the_model_does_not_define_maps_to_nothing()
        {
            // Null rather than a default, for the reason OutcomeRules.TryGradeFromAnswer
            // returns false rather than a Pass: writing an unrecognised discipline's checker
            // into either column would put a name against a check nobody made.
            Assert.Null(CheckerNames.AttributeFor(999));
        }

        [Theory]
        [InlineData(ResponseRules.ReviewTypeTax, true)]
        [InlineData(ResponseRules.ReviewTypeAqs, true)]
        [InlineData(999, false)]
        public void Try_reports_whether_the_discipline_is_one_we_stamp(int reviewType, bool expected)
        {
            string attribute;

            Assert.Equal(expected, CheckerNames.TryAttributeFor(reviewType, out attribute));
            Assert.Equal(expected, attribute != null);
        }

        [Fact]
        public void A_review_with_no_type_at_all_stamps_nothing()
        {
            string attribute;

            Assert.False(CheckerNames.TryAttributeFor(null, out attribute));
            Assert.Null(attribute);
        }

        [Fact]
        public void The_column_the_two_replace_is_still_named()
        {
            // Kept in the schema and no longer written, read or offered for editing. Dropping
            // a column deletes what it holds, and what it holds is the last allocation made
            // under the old rule - evidence about how a case was handled, not noise.
            Assert.Equal("al_checkername", CheckerNames.LegacyAttribute);
        }
    }
}
