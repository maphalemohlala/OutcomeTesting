using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The file quality outcome recorded on a case, from whichever discipline graded it.
    ///
    /// Q-FQ-01 is the AQS file quality question and Q-FQTAX-01 the Tax one. They are the same
    /// question asked by the two checklists on the same Pass/Fail scale, so a reader wanting
    /// "the file quality grade" wants whichever of them was answered. Reading only the AQS
    /// code exported a blank AD-039 column 10 for every Tax-only case that had been graded.
    ///
    /// Shared because two callers now ask the same question of the same data: the export
    /// fills column 10 and derives the paraplanner from it, and SetFailAccountability decides
    /// whether there is a fail on the case to attribute. Answering it two different ways
    /// would let the command refuse a fail the export had already attributed.
    /// </summary>
    public static class FileQuality
    {
        public const string QuestionCode = "Q-FQ-01";
        public const string TaxQuestionCode = "Q-FQTAX-01";

        private const string ResponseEntity = "al_response";
        private const string ReviewEntity = "al_reviewinstance";
        private const string QuestionVersionEntity = "al_questionversion";
        private const string QuestionEntity = "al_question";
        private const string AnswerChoiceAttr = "al_answerchoice";

        /// <summary>
        /// The answer in force, or null where neither discipline graded the file.
        ///
        /// The AQS answer wins where both exist, which is every Tax-then-AQS case: it is the
        /// grade column 10 has always carried there, and the AQS leg is the later check. The
        /// Tax code is only queried when the AQS leg did not grade the file, so a case that
        /// has both still costs one query.
        /// </summary>
        public static Entity Resolve(IOrganizationService service, Guid caseId)
        {
            return ResolveAnswer(service, caseId, QuestionCode)
                ?? ResolveAnswer(service, caseId, TaxQuestionCode);
        }

        /// <summary>The formatted answer, which is what AD-039 column 10 carries.</summary>
        public static string Label(Entity answer)
        {
            if (answer == null)
            {
                return null;
            }

            return answer.FormattedValues.ContainsKey(AnswerChoiceAttr)
                ? answer.FormattedValues[AnswerChoiceAttr]
                : null;
        }

        /// <summary>The answer value, which is what a decision should be made on.</summary>
        public static int? Choice(Entity answer)
        {
            if (answer == null)
            {
                return null;
            }

            var value = answer.GetAttributeValue<OptionSetValue>(AnswerChoiceAttr);
            return value == null ? (int?)null : value.Value;
        }

        /// <summary>
        /// Whether the file quality answer is a Fail, read from the value rather than the
        /// label so renaming the option cannot quietly stop attributing anyone.
        /// </summary>
        public static bool Failed(int? choice)
        {
            return choice.HasValue && choice.Value == ResponseRules.ChoiceFail;
        }

        /// <summary>Whether the case's file quality answer is a Fail, in one call.</summary>
        public static bool FailedOn(IOrganizationService service, Guid caseId)
        {
            return Failed(Choice(Resolve(service, caseId)));
        }

        // Matched on the question's business code rather than its GUID so this does not break
        // when the question is retired and succeeded (BR-013, AD-004): a successor version
        // keeps the code and stays the same question.
        private static Entity ResolveAnswer(IOrganizationService service, Guid caseId, string questionCode)
        {
            var query = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet(AnswerChoiceAttr),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };

            var review = query.AddLink(ReviewEntity, "al_reviewinstanceid", "al_reviewinstanceid");
            review.LinkCriteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);

            var version = query.AddLink(QuestionVersionEntity, "al_questionversionid", "al_questionversionid");
            var question = version.AddLink(QuestionEntity, "al_questionid", "al_questionid");
            question.LinkCriteria.AddCondition("al_questioncode", ConditionOperator.Equal, questionCode);

            // The case may hold several reviews, and a succeeded question several versions,
            // so this can match more than one response. The latest is the grade in force;
            // without an order the answer would be whichever row came back first.
            query.AddOrder("modifiedon", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
        }
    }
}
