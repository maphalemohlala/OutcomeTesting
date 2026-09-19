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
    /// Two effects, both of which have to happen whatever wrote the answer. The first saved
    /// answer moves the review from Assigned to Review In Progress, which is the FR-010
    /// lifecycle step the checker never performs explicitly (AD-053), and dates the case with
    /// it. A grade of Pass clears any primary root cause already recorded (item 3,
    /// 2026-09-19).
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

            StampCheckDate(service, review);
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

            foreach (var recorded in RootCauseAnswers(service, reviewId))
            {
                service.Update(new Entity("al_response", recorded.Id)
                {
                    ["al_answerchoice"] = null,
                });
            }
        }

        /// <summary>
        /// Every answer this review holds against the primary root cause that actually
        /// records a cause. Matched on the question code through Question Version -> Question,
        /// the same chain SubmitReviewPlugin's gate walks, so the two agree on which row the
        /// rule is about.
        ///
        /// Rows already holding nothing are left out rather than cleared again: an Update
        /// that changes no value still writes a modified-on stamp and still fires every step
        /// registered on the table.
        /// </summary>
        private static IEnumerable<Entity> RootCauseAnswers(IOrganizationService service, Guid reviewId)
        {
            var query = new QueryExpression("al_response")
            {
                ColumnSet = new ColumnSet("al_answerchoice"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            query.Criteria.AddCondition("al_answerchoice", ConditionOperator.NotNull);

            var versionLink = query.AddLink(
                "al_questionversion", "al_questionversionid", "al_questionversionid");
            var questionLink = versionLink.AddLink("al_question", "al_questionid", "al_questionid");
            questionLink.LinkCriteria.AddCondition(
                "al_questioncode", ConditionOperator.Equal, GradingRules.RootCauseQuestionCode);

            return service.RetrieveMultiple(query).Entities;
        }

        /// <summary>
        /// Stamps the case's check date the moment checks actually start on it (project
        /// owner, 2026-09-19: "the check date should default to when someone starts checks
        /// on the case").
        ///
        /// Here rather than on assignment, because a case can sit assigned for days before
        /// anyone opens it, and the business reads this column as the day the work was
        /// done. This runs on the Assigned -> In progress transition, which is the first
        /// answer saved, so it fires exactly once per review and the first review to start
        /// is the one that dates the case.
        ///
        /// Only when the column is empty. It is a DEFAULT, not a derived value: the import
        /// may have carried one, and a checker may correct it afterwards, and neither should
        /// be overwritten by the next review on the same case starting.
        ///
        /// Date-only column (behavior 2), so the date is written without a time.
        /// </summary>
        public static void StampCheckDate(IOrganizationService service, Entity review)
        {
            if (service == null || review == null)
            {
                return;
            }

            var caseRef = review.GetAttributeValue<EntityReference>("al_outcomecaseid");
            if (caseRef == null)
            {
                return;
            }

            var outcomeCase = service.Retrieve(
                "al_outcomecase", caseRef.Id, new ColumnSet("al_checkdate"));

            if (outcomeCase != null
                && outcomeCase.GetAttributeValue<DateTime?>("al_checkdate").HasValue)
            {
                return;
            }

            service.Update(new Entity("al_outcomecase", caseRef.Id)
            {
                ["al_checkdate"] = DateTime.UtcNow.Date,
            });
        }
    }
}
