using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>Amending a case's File and Advice grades for an appeal (requirements log row 39).</summary>
    public class AmendOutcomeTests
    {
        private static readonly Guid CaseId = Guid.Parse("33333333-0000-4000-8000-000000000001");
        private static readonly Guid OldOutcome = Guid.Parse("33333333-0000-4000-8000-0000000000a1");
        private static readonly Guid NewOutcome = Guid.Parse("33333333-0000-4000-8000-0000000000a2");

        private static FakeOrganizationService Seeded(bool taxOnly = false, bool withOutcome = true, bool withFileAnswer = true)
        {
            var svc = new FakeOrganizationService();
            var route = Guid.NewGuid();
            svc.Seed("al_reviewroute", route, "al_requiresaqsreview", !taxOnly);
            svc.Seed("al_outcomecase", CaseId, "al_outcomecaseid", CaseId, "al_casereference", "259162944",
                "al_reviewrouteid", new EntityReference("al_reviewroute", route));

            if (withOutcome)
            {
                // Two outcomes: the newest is the one the export reads, so it is the one amended.
                svc.Seed("al_outcome", OldOutcome, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                    "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomePass), "createdon", DateTime.UtcNow.AddDays(-9));
                svc.Seed("al_outcome", NewOutcome, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId),
                    "al_initialoutcome", new OptionSetValue(OutcomeRules.OutcomeInsufficient), "createdon", DateTime.UtcNow.AddDays(-1));
            }

            if (withFileAnswer)
            {
                var question = Guid.NewGuid();
                var version = Guid.NewGuid();
                var review = Guid.NewGuid();
                svc.Seed("al_question", question, "al_questioncode", taxOnly ? FileQuality.TaxQuestionCode : FileQuality.QuestionCode);
                svc.Seed("al_questionversion", version, "al_questionid", new EntityReference("al_question", question));
                svc.Seed("al_reviewinstance", review, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
                svc.Seed("al_response", FileAnswer, "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceFail),
                    "al_reviewinstanceid", new EntityReference("al_reviewinstance", review),
                    "al_questionversionid", new EntityReference("al_questionversion", version),
                    "modifiedon", DateTime.UtcNow);
            }

            return svc;
        }

        private static readonly Guid FileAnswer = Guid.Parse("33333333-0000-4000-8000-0000000000f1");

        private static int Value(FakeOrganizationService svc, string table, Guid id, string column)
        {
            return svc.Retrieve(table, id, new ColumnSet(column)).GetAttributeValue<OptionSetValue>(column).Value;
        }

        [Fact]
        public void Both_grades_are_amended_and_the_line_names_each_before_and_after()
        {
            var svc = Seeded();

            var summary = AmendOutcomePlugin.Amend(svc, CaseId, "Pass", "pass with issues", "Appeal upheld");

            Assert.Equal(OutcomeRules.OutcomePassWithIssues, Value(svc, "al_outcome", NewOutcome, "al_initialoutcome"));
            Assert.Equal(OutcomeRules.OutcomePass, Value(svc, "al_outcome", OldOutcome, "al_initialoutcome"));
            Assert.Equal(ResponseRules.ChoicePass, Value(svc, "al_response", FileAnswer, "al_answerchoice"));
            Assert.Equal(
                "Case 259162944: Advice grade from Insufficient evidence to Pass with issues; File grade from Fail to Pass.",
                summary);
        }

        [Fact]
        public void An_advice_amendment_also_rewrites_the_checkers_grade_answer()
        {
            // The review page and the completed-check PDF read the grade from Q-GR-01.
            var svc = Seeded();
            var question = Guid.NewGuid();
            var version = Guid.NewGuid();
            var review = Guid.NewGuid();
            var grade = Guid.NewGuid();
            svc.Seed("al_question", question, "al_questioncode", GradingRules.GradeQuestionCode);
            svc.Seed("al_questionversion", version, "al_questionid", new EntityReference("al_question", question));
            svc.Seed("al_reviewinstance", review, "al_outcomecaseid", new EntityReference("al_outcomecase", CaseId));
            svc.Seed("al_response", grade, "al_answerchoice", new OptionSetValue(ResponseRules.ChoiceInsufficient),
                "al_reviewinstanceid", new EntityReference("al_reviewinstance", review),
                "al_questionversionid", new EntityReference("al_questionversion", version),
                "modifiedon", DateTime.UtcNow);

            AmendOutcomePlugin.Amend(svc, CaseId, null, "Potential harm", "Appeal");

            Assert.Equal(OutcomeRules.OutcomePotentialHarm, Value(svc, "al_outcome", NewOutcome, "al_initialoutcome"));
            Assert.Equal(ResponseRules.ChoicePotentialHarm, Value(svc, "al_response", grade, "al_answerchoice"));
        }

        [Fact]
        public void A_grade_left_empty_is_not_touched()
        {
            var svc = Seeded();

            var summary = AmendOutcomePlugin.Amend(svc, CaseId, null, "Pass", "Appeal");

            Assert.Equal(ResponseRules.ChoiceFail, Value(svc, "al_response", FileAnswer, "al_answerchoice"));
            Assert.DoesNotContain("File grade", summary);
        }

        [Fact]
        public void A_Tax_only_case_has_a_File_grade_but_no_Advice_grade()
        {
            var svc = Seeded(taxOnly: true);

            AmendOutcomePlugin.Amend(svc, CaseId, "Pass", null, "Appeal");
            Assert.Equal(ResponseRules.ChoicePass, Value(svc, "al_response", FileAnswer, "al_answerchoice"));

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AmendOutcomePlugin.Amend(svc, CaseId, null, "Pass", "Appeal"));
            Assert.Contains("no Advice grade to amend", error.Message);
        }

        [Fact]
        public void A_case_with_no_File_Quality_answer_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AmendOutcomePlugin.Amend(Seeded(withFileAnswer: false), CaseId, "Pass", null, "Appeal"));

            Assert.StartsWith(CommandHelpers.PreconditionPrefix, error.Message);
        }

        [Theory]
        [InlineData(null, null, "Appeal", "Choose a new File grade")]
        [InlineData("Maybe", null, "Appeal", "File grade must be Pass or Fail")]
        [InlineData(null, "Excellent", "Appeal", "Advice grade must be Pass")]
        [InlineData("Pass", null, " ", "Say why")]
        [InlineData("Fail", "Insufficient evidence", "Appeal", "already has those grades")]
        public void Refusals_name_what_is_wrong(string file, string advice, string reason, string expected)
        {
            var svc = Seeded();

            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AmendOutcomePlugin.Amend(svc, CaseId, file, advice, reason));

            Assert.StartsWith(CommandHelpers.ValidationPrefix, error.Message);
            Assert.Contains(expected, error.Message);
            Assert.Empty(svc.Updates);
        }

        [Fact]
        public void A_missing_case_is_not_found()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => AmendOutcomePlugin.Amend(Seeded(), Guid.NewGuid(), "Pass", null, "Appeal"));

            Assert.StartsWith(CommandHelpers.NotFoundPrefix, error.Message);
        }

        private static FakePluginExecutionContext Update(Guid user, Entity target)
        {
            return new FakePluginExecutionContext
            {
                MessageName = "Update",
                UserId = user,
                InitiatingUserId = user,
                InputParameters = new ParameterCollection { { "Target", target } },
            };
        }

        [Fact]
        public void Only_a_SYSTEM_change_of_the_answer_choice_alone_passes_the_submitted_lock()
        {
            var svc = new FakeOrganizationService();
            var system = FakeOrganizationService.SystemUserId;
            var choiceOnly = new Entity("al_response", FileAnswer) { ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass) };
            var withText = new Entity("al_response", FileAnswer)
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass),
                ["al_answertext"] = "edited",
            };

            Assert.True(ResponseGuardPlugin.IsGradeAmendment(svc, Update(system, choiceOnly)));

            // What the platform itself adds before pre-operation must not disqualify it.
            var stamped = new Entity("al_response", FileAnswer)
            {
                ["al_answerchoice"] = new OptionSetValue(ResponseRules.ChoicePass),
                ["modifiedon"] = DateTime.UtcNow,
                ["modifiedby"] = new EntityReference("systemuser", system),
            };
            Assert.True(ResponseGuardPlugin.IsGradeAmendment(svc, Update(system, stamped)));
            Assert.False(ResponseGuardPlugin.IsGradeAmendment(svc, Update(Guid.NewGuid(), choiceOnly)));
            Assert.False(ResponseGuardPlugin.IsGradeAmendment(svc, Update(system, withText)));

            var create = Update(system, choiceOnly);
            create.MessageName = "Create";
            Assert.False(ResponseGuardPlugin.IsGradeAmendment(svc, create));
        }
    }
}
