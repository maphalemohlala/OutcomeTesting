using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Post-operation on al_response Create and Update: what saving an answer does to things
    /// other than that answer.
    ///
    /// Three effects, all of which have to happen whatever wrote the answer. The first saved
    /// answer moves the review from Assigned to Review In Progress, which is the FR-010
    /// lifecycle step the checker never performs explicitly (AD-053), and dates the case with
    /// it. A grade of Pass clears any primary root cause already recorded (item 3,
    /// 2026-09-19). A Suitability core check answered Insufficient evidence clears a grade of
    /// Pass or Pass with issues, which can no longer stand beside it (item 10, 2026-09-19).
    ///
    /// This runs server-side because the portal holds no write permission on
    /// al_reviewinstance and must not be given one: a checker who could write the review
    /// row directly could also write al_submittedon. The root cause is cleared here for the
    /// surface-independent reason rather than that one - the portal reaches al_response
    /// through AnswerWriter and a direct Dataverse write reaches it without, so a rule that
    /// lived in AnswerWriter would hold for the portal alone.
    /// </summary>
    public class ResponseProgressPlugin : PluginBase
    {
        private const string ReviewEntity = "al_reviewinstance";

        public ResponseProgressPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ResponseProgressPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            if (!context.InputParameters.Contains("Target"))
            {
                return;
            }

            var target = context.InputParameters["Target"] as Entity;
            if (target == null || target.LogicalName != "al_response")
            {
                return;
            }

            // On Update the Target carries only changed columns, so the review link comes
            // from the pre-image registered by Register-ResponseGuard.ps1.
            var pre = context.PreEntityImages.Values.FirstOrDefault();
            var reviewRef = target.GetAttributeValue<EntityReference>("al_reviewinstanceid")
                ?? (pre == null ? null : pre.GetAttributeValue<EntityReference>("al_reviewinstanceid"));

            if (reviewRef == null)
            {
                return;
            }

            // Before the status gate deliberately. The grade is rarely the first answer
            // saved, so a review that is already In progress is the ordinary case for this
            // and the only one that matters - putting it below the gate would mean the root
            // cause was cleared only on a review whose very first answer was its grade.
            ClearRootCauseOnPass(service, target, pre, reviewRef.Id);
            ClearOutcomesContradictedByAnswer(service, target, pre, reviewRef.Id);
            ReconcileRemedialAction(service, target, pre, reviewRef.Id);

            var review = service.Retrieve(
                ReviewEntity,
                reviewRef.Id,
                new ColumnSet("al_reviewstatus", "al_startedon", "al_outcomecaseid"));

            var status = review.GetAttributeValue<OptionSetValue>("al_reviewstatus");
            if (status == null || status.Value != ResponseRules.StatusAssigned)
            {
                return;
            }

            var update = new Entity(ReviewEntity, reviewRef.Id)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusInProgress),
            };

            // Started is stamped once, on the transition, so a later edit never moves it.
            if (!review.Contains("al_startedon"))
            {
                update["al_startedon"] = DateTime.UtcNow;
            }

            service.Update(update);
        }

        /// <summary>
        /// Clears the primary root cause when the answer just saved is a grade of Pass
        /// (item 3, 2026-09-19).
        ///
        /// A passing file has no root cause to name, so the front ends stop offering the
        /// question - but a checker who graded the file Potential harm, chose a cause, and
        /// then changed the grade to Pass has already left one in the table. Hiding it would
        /// not unsay it: it would still export, and still read to anyone querying al_response
        /// as a live root cause on a passing case.
        ///
        /// Guarded cheaply and then exactly. Pass is also the suitability grid's Pass, so
        /// most ticks on a checklist reach the first test; the response type is what takes
        /// them no further, since the grade's own scale belongs to Q-GR-01 alone (AD-055).
        /// Only then is the question code read, because a response type is a convention an
        /// administrator could give to another question tomorrow and the code is the AD-122
        /// contract ChecklistGuards now protects.
        ///
        /// The clearing Update re-enters this plug-in and stops at the first test: the row it
        /// writes holds no choice at all, so it cannot be a grade of Pass.
        /// </summary>
        public static void ClearRootCauseOnPass(
            IOrganizationService service,
            Entity target,
            Entity pre,
            Guid reviewId)
        {
            if (service == null || target == null || !target.Contains("al_answerchoice"))
            {
                return;
            }

            var choice = target.GetAttributeValue<OptionSetValue>("al_answerchoice");
            if (!GradingRules.RootCauseCleared(choice == null ? (int?)null : choice.Value))
            {
                return;
            }

            var questionVersionRef = target.GetAttributeValue<EntityReference>("al_questionversionid")
                ?? (pre == null ? null : pre.GetAttributeValue<EntityReference>("al_questionversionid"));
            if (questionVersionRef == null)
            {
                return;
            }

            var questionVersion = service.Retrieve(
                "al_questionversion",
                questionVersionRef.Id,
                new ColumnSet("al_responsetype", "al_questionid"));

            var responseType = questionVersion == null
                ? null
                : questionVersion.GetAttributeValue<OptionSetValue>("al_responsetype");
            if (responseType == null || responseType.Value != GradingRules.GradeResponseType)
            {
                return;
            }

            var questionRef = questionVersion.GetAttributeValue<EntityReference>("al_questionid");
            if (questionRef == null)
            {
                return;
            }

            var question = service.Retrieve(
                "al_question", questionRef.Id, new ColumnSet("al_questioncode"));
            var code = question == null ? null : question.GetAttributeValue<string>("al_questioncode");
            if (code == null
                || !code.Trim().Equals(
                    GradingRules.GradeQuestionCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var recorded in ChecklistQueries.ChoiceAnswersTo(
                service, reviewId, GradingRules.RootCauseQuestionCode))
            {
                service.Update(new Entity("al_response", recorded.Id)
                {
                    ["al_answerchoice"] = null,
                });
            }

            // Several causes at once from 2026-09-24, held as ticks.
            foreach (var recorded in ChecklistQueries.ChoicesAnswersTo(
                service, reviewId, GradingRules.RootCauseQuestionCode))
            {
                service.Update(new Entity("al_response", recorded.Id)
                {
                    ["al_answerchoices"] = null,
                });
            }
        }

        /// <summary>
        /// Clears an outcome the answer just saved has contradicted (project owner,
        /// 2026-09-22; widening item 10, 2026-09-19).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The batch asked throughout for a now-invalid outcome to be cleared and the user
        /// prompted, rather than for the tick to be refused, and that is the right way round:
        /// the checker is recording what they found on the file, and what they found is not
        /// the thing to argue with. ResponseGuardPlugin refuses the contradicting outcome from
        /// this moment on, and the page says what happened.
        /// </para>
        /// <para>
        /// Two outcomes can be contradicted. An Insufficient evidence anywhere leaves the
        /// grade only Insufficient evidence or Potential harm; a No or a Fail on any test
        /// point takes Pass off the grade and off the file quality outcome.
        /// <see cref="ChecklistGating"/> holds both rules.
        /// </para>
        /// <para>
        /// Only an outcome the rules actually refuse is cleared - never one that still stands,
        /// and never an unanswered one. The section test this used to make is gone with the
        /// rule it served: Insufficient evidence on the Consumer Duty overlay is no longer
        /// something to tell apart from Insufficient evidence on a core check, because the
        /// instruction of 2026-09-22 was "at any section not just the File quality AML and CRA
        /// section". What replaces it is the question test - an outcome is not part of the sum
        /// it is the sum of.
        /// </para>
        /// <para>
        /// The clearing Update re-enters this plug-in and stops at the first test, as the root
        /// cause clearing does: the row it writes holds no choice, so it is not a finding.
        /// </para>
        /// </remarks>
        public static void ClearOutcomesContradictedByAnswer(
            IOrganizationService service,
            Entity target,
            Entity pre,
            Guid reviewId)
        {
            if (service == null || target == null || !target.Contains("al_answerchoice"))
            {
                return;
            }

            var choice = target.GetAttributeValue<OptionSetValue>("al_answerchoice");
            if (choice == null
                || !(ChecklistGating.IsInsufficient(choice.Value)
                    || ChecklistGating.IsNoOrFail(choice.Value)))
            {
                return;
            }

            var questionVersionRef = target.GetAttributeValue<EntityReference>("al_questionversionid")
                ?? (pre == null ? null : pre.GetAttributeValue<EntityReference>("al_questionversionid"));
            if (questionVersionRef == null)
            {
                return;
            }

            // An outcome contradicts nothing by being answered: it is what the test points add
            // up to. Saving "Remedial action required? No" must not set about clearing the
            // Pass that put it there.
            if (ChecklistGating.IsOutcomeQuestion(
                    ChecklistQueries.QuestionCodeForVersion(service, questionVersionRef.Id)))
            {
                return;
            }

            var facts = ChecklistQueries.ReadGatingFacts(service, reviewId);

            foreach (var recorded in ChecklistQueries.ChoiceAnswersTo(
                service, reviewId, GradingRules.GradeQuestionCode))
            {
                var grade = recorded.GetAttributeValue<OptionSetValue>("al_answerchoice");
                if (ChecklistGating.GradeCleared(
                        grade == null ? (int?)null : grade.Value,
                        facts.InsufficientAnywhere,
                        facts.NoOrFailAnywhere))
                {
                    Clear(service, recorded.Id);
                }
            }

            // Both disciplines' file quality outcome, because one review carries only one of
            // them and asking for the other costs a query that finds nothing.
            ClearFileQuality(service, reviewId, FileQuality.QuestionCode, facts);
            ClearFileQuality(service, reviewId, FileQuality.TaxQuestionCode, facts);
        }

        private static void ClearFileQuality(
            IOrganizationService service,
            Guid reviewId,
            string questionCode,
            ChecklistQueries.GatingFacts facts)
        {
            foreach (var recorded in ChecklistQueries.ChoiceAnswersTo(service, reviewId, questionCode))
            {
                var outcome = recorded.GetAttributeValue<OptionSetValue>("al_answerchoice");
                var refusal = ChecklistGating.FileQualityRefusal(
                    outcome == null ? (int?)null : outcome.Value, facts.NoOrFailAnywhere);

                if (refusal != null)
                {
                    Clear(service, recorded.Id);
                }
            }
        }

        /// <summary>
        /// Moves "Remedial action required?" to the answer the file quality outcome implies,
        /// when that outcome is what has just been saved (project owner, 2026-09-23).
        ///
        /// <para>
        /// The third side of the rule. ResponseGuardPlugin refuses a remedial answer the
        /// outcome contradicts, and the page never offers one - but neither reaches an answer
        /// recorded BEFORE the outcome moved. A checker who ticks Yes, then settles on Pass,
        /// would otherwise leave the review holding exactly the pair both other halves exist
        /// to prevent, with no screen willing to let them correct it.
        /// </para>
        /// <para>
        /// Moved, not cleared, which is the one place this rule departs from its neighbours
        /// above. They clear a grade or an outcome the form has contradicted, because what it
        /// should become is a judgement only the checker can make. Here the outcome has
        /// already made it: on a Pass the answer is No and on a Fail it is Yes, and leaving
        /// the question blank would only oblige the checker to re-enter the one value it can
        /// now take (it is mandatory, so the submit would stop them until they did).
        /// </para>
        /// <para>
        /// An answer that is already right is not rewritten, so the ordinary save costs one
        /// query and no update, and the audit does not fill with entries recording that
        /// nothing changed. A question with no answer row yet is left alone rather than
        /// created here: the page writes that one on the checker's behalf through the
        /// answer's own autosave, where it is audited on the path every other answer takes.
        /// </para>
        /// <para>
        /// This cannot recurse. The write it issues is a Yes / No answer to Q-FQ-03 or
        /// Q-FQTAX-03, which is not a file quality outcome, so the run it triggers returns at
        /// the code test below.
        /// </para>
        /// </summary>
        private static void ReconcileRemedialAction(
            IOrganizationService service, Entity target, Entity pre, Guid reviewId)
        {
            var choice = target.GetAttributeValue<OptionSetValue>("al_answerchoice");
            if (choice == null)
            {
                return;
            }

            var implied = ChecklistGating.RemedialActionDefault(choice.Value);
            if (!implied.HasValue)
            {
                return;
            }

            var questionVersionRef = target.GetAttributeValue<EntityReference>("al_questionversionid")
                ?? (pre == null ? null : pre.GetAttributeValue<EntityReference>("al_questionversionid"));
            if (questionVersionRef == null)
            {
                return;
            }

            // Confirmed on the code and not on the Pass / Fail scale, which AD-123 could put
            // on any question. Only the two file quality outcomes carry this rule.
            var code = ChecklistQueries.QuestionCodeForVersion(service, questionVersionRef.Id);
            if (code == null)
            {
                return;
            }

            code = code.Trim();
            if (!code.Equals(FileQuality.QuestionCode, StringComparison.OrdinalIgnoreCase)
                && !code.Equals(FileQuality.TaxQuestionCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Both disciplines' question, because one review carries only one of them and
            // asking for the other costs a query that finds nothing.
            Move(service, reviewId, ChecklistGating.RemedialActionQuestionCode, implied.Value);
            Move(service, reviewId, ChecklistGating.TaxRemedialActionQuestionCode, implied.Value);
        }

        private static void Move(
            IOrganizationService service, Guid reviewId, string questionCode, int answer)
        {
            foreach (var recorded in ChecklistQueries.ChoiceAnswersTo(service, reviewId, questionCode))
            {
                var current = recorded.GetAttributeValue<OptionSetValue>("al_answerchoice");
                if (current != null && current.Value == answer)
                {
                    continue;
                }

                service.Update(new Entity("al_response", recorded.Id)
                {
                    ["al_answerchoice"] = new OptionSetValue(answer),
                });
            }
        }

        private static void Clear(IOrganizationService service, Guid responseId)
        {
            service.Update(new Entity("al_response", responseId)
            {
                ["al_answerchoice"] = null,
            });
        }
    }
}
