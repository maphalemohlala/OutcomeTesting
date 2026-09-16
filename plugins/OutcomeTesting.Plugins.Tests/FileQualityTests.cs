using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// FileQuality — the file quality outcome on a case, from whichever discipline graded it.
    ///
    /// Q-FQ-01 is the AQS question and Q-FQTAX-01 the Tax one, the same question asked by the
    /// two checklists. Reading only the AQS code exported a blank AD-039 column 10 for every
    /// Tax-only case that had been graded, and left a Tax fail attributable to nobody.
    /// </summary>
    public class FileQualityTests
    {
        private static readonly Guid CaseId = Guid.NewGuid();

        private static Entity Answer(int choice, string label)
        {
            var e = new Entity("al_response", Guid.NewGuid());
            e["al_answerchoice"] = new OptionSetValue(choice);
            e.FormattedValues["al_answerchoice"] = label;
            return e;
        }

        /// <summary>Seeds one answered question against the case, on the given code.</summary>
        private static void SeedAnswer(FakeOrganizationService svc, string questionCode, int choice, string label)
        {
            var questionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var reviewId = Guid.NewGuid();

            svc.Seed("al_question", questionId, "al_questioncode", questionCode);
            svc.Seed("al_questionversion", versionId, "al_questionid", new EntityReference("al_question", questionId));
            svc.Seed("al_reviewinstance", reviewId, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));

            var response = svc.Seed("al_response", Guid.NewGuid(),
                "al_answerchoice", new OptionSetValue(choice),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", reviewId),
                "al_questionversionid", new EntityReference("al_questionversion", versionId),
                "modifiedon", DateTime.UtcNow);
            response.FormattedValues["al_answerchoice"] = label;
        }

        [Fact]
        public void Reads_the_AQS_answer_where_the_case_has_one()
        {
            var svc = new FakeOrganizationService();
            SeedAnswer(svc, FileQuality.QuestionCode, ResponseRules.ChoiceFail, "Fail");

            Assert.Equal("Fail", FileQuality.Label(FileQuality.Resolve(svc, CaseId)));
        }

        [Fact]
        public void Falls_back_to_the_tax_answer_on_a_tax_only_case()
        {
            // The whole point: a Tax-only case has no Q-FQ-01 and was exporting blank.
            var svc = new FakeOrganizationService();
            SeedAnswer(svc, FileQuality.TaxQuestionCode, ResponseRules.ChoiceFail, "Fail");

            Assert.Equal("Fail", FileQuality.Label(FileQuality.Resolve(svc, CaseId)));
            Assert.True(FileQuality.FailedOn(svc, CaseId));
        }

        [Fact]
        public void Prefers_the_AQS_answer_where_both_disciplines_graded_the_file()
        {
            // Every Tax-then-AQS case answers both. Column 10 has always carried the AQS
            // grade there, and the AQS leg is the later check.
            var svc = new FakeOrganizationService();
            SeedAnswer(svc, FileQuality.QuestionCode, ResponseRules.ChoiceFail, "Fail");
            SeedAnswer(svc, FileQuality.TaxQuestionCode, ResponseRules.ChoicePass, "Pass");

            Assert.Equal("Fail", FileQuality.Label(FileQuality.Resolve(svc, CaseId)));
        }

        [Fact]
        public void Reports_nothing_when_neither_discipline_graded_the_file()
        {
            var svc = new FakeOrganizationService();

            Assert.Null(FileQuality.Resolve(svc, CaseId));
            Assert.Null(FileQuality.Label(null));
            Assert.Null(FileQuality.Choice(null));
            Assert.False(FileQuality.FailedOn(svc, CaseId));
        }

        [Fact]
        public void A_fail_is_recognised_by_value_rather_than_label()
        {
            // Renaming the option must not quietly stop attributing anyone, so the decision
            // reads al_answerchoice and never the formatted text.
            var renamed = Answer(ResponseRules.ChoiceFail, "Did not meet standard");

            Assert.True(FileQuality.Failed(FileQuality.Choice(renamed)));
        }

        [Fact]
        public void A_pass_is_not_a_fail()
        {
            Assert.False(FileQuality.Failed(FileQuality.Choice(Answer(ResponseRules.ChoicePass, "Pass"))));
            Assert.False(FileQuality.Failed(null));
        }
    }
}
