using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The checklist reads the administration commands share (AD-122, AD-123): which
    /// question codes are taken, which version of a question is current, and which
    /// checklist version is in force today.
    ///
    /// One home rather than a private copy per plug-in. The 2026-09-13 audit found the
    /// code check written three times, the current-version read three times with three
    /// different column sets, and the in-force checklist version twice - so a change to any
    /// one rule had to be found and made in every copy, and the copies had already drifted.
    /// </summary>
    public static class ChecklistQueries
    {
        public const string QuestionEntity = "al_question";
        public const string VersionEntity = "al_questionversion";
        public const string ChecklistVersionEntity = "al_checklistversion";

        /// <summary>
        /// The section code the question behind this answer belongs to, or null where any
        /// link in the chain is missing (item 10, 2026-09-19).
        ///
        /// Response -> Question Version -> Question -> Section is the single-parent chain in
        /// the model, so there is no ambiguity to resolve - only three reads, which is why
        /// callers reach this behind a cheap test on the answer value itself.
        /// </summary>
        public static string SectionCodeForVersion(IOrganizationService service, Guid questionVersionId)
        {
            var version = service.Retrieve(
                VersionEntity, questionVersionId, new ColumnSet("al_questionid"));
            var questionRef = version == null
                ? null
                : version.GetAttributeValue<EntityReference>("al_questionid");
            if (questionRef == null)
            {
                return null;
            }

            var question = service.Retrieve(
                QuestionEntity, questionRef.Id, new ColumnSet("al_sectionid"));
            var sectionRef = question == null
                ? null
                : question.GetAttributeValue<EntityReference>("al_sectionid");
            if (sectionRef == null)
            {
                return null;
            }

            var section = service.Retrieve(
                "al_section", sectionRef.Id, new ColumnSet("al_sectioncode"));
            return section == null ? null : section.GetAttributeValue<string>("al_sectioncode");
        }

        /// <summary>
        /// The business code of the question behind this answer, or null where the chain
        /// cannot be walked (project owner, 2026-09-22).
        ///
        /// Response -> Question Version -> Question, the single-parent chain in the model, so
        /// there is nothing to disambiguate - two reads, which is why the caller reaches this
        /// only after a cheap test on the answer value itself.
        ///
        /// Read rather than inferred from the response type: a type is a convention that
        /// checklist administration can reassign (AD-123) and the code is the AD-122 contract,
        /// carried forward whole when a question is retired and succeeded (BR-013, AD-004).
        /// </summary>
        public static string QuestionCodeForVersion(IOrganizationService service, Guid questionVersionId)
        {
            var version = service.Retrieve(
                VersionEntity, questionVersionId, new ColumnSet("al_questionid"));
            var questionRef = version == null
                ? null
                : version.GetAttributeValue<EntityReference>("al_questionid");
            if (questionRef == null)
            {
                return null;
            }

            var question = service.Retrieve(
                QuestionEntity, questionRef.Id, new ColumnSet("al_questioncode"));
            return question == null ? null : question.GetAttributeValue<string>("al_questioncode");
        }

        /// <summary>
        /// Every answer this review holds against one question code, matched through Question
        /// Version -> Question so a question retired and succeeded under BR-013 still resolves
        /// (item 3 and item 10, 2026-09-19).
        ///
        /// Only rows actually holding a choice come back: an Update that changes no value
        /// still stamps modified-on and still fires every step registered on the table.
        /// </summary>
        public static IEnumerable<Entity> ChoiceAnswersTo(
            IOrganizationService service, Guid reviewId, string questionCode)
        {
            return AnswersHolding(service, reviewId, questionCode, "al_answerchoice");
        }

        /// <summary>
        /// As <see cref="ChoiceAnswersTo"/>, for the rows holding ticks in al_answerchoices -
        /// which is where Q-GR-02 answers from 2026-09-24, when the primary root cause became
        /// several ticks rather than one. Its earlier versions still answer in al_answerchoice,
        /// so a caller letting go of a root cause asks both.
        /// </summary>
        public static IEnumerable<Entity> ChoicesAnswersTo(
            IOrganizationService service, Guid reviewId, string questionCode)
        {
            return AnswersHolding(service, reviewId, questionCode, "al_answerchoices");
        }

        private static IEnumerable<Entity> AnswersHolding(
            IOrganizationService service, Guid reviewId, string questionCode, string column)
        {
            var query = new QueryExpression("al_response")
            {
                ColumnSet = new ColumnSet(column),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            query.Criteria.AddCondition(column, ConditionOperator.NotNull);

            var versionLink = query.AddLink(VersionEntity, "al_questionversionid", "al_questionversionid");
            var questionLink = versionLink.AddLink(QuestionEntity, "al_questionid", "al_questionid");
            questionLink.LinkCriteria.AddCondition(
                "al_questioncode", ConditionOperator.Equal, questionCode);

            return service.RetrieveMultiple(query).Entities;
        }

        /// <summary>
        /// al_questioncode carries the al_questioncodekey alternate key, so a duplicate
        /// collides at the platform. Caught here so the caller reads a sentence about the
        /// code rather than a key-violation stack.
        /// </summary>
        public static void EnsureQuestionCodeIsFree(IOrganizationService service, string questionCode)
        {
            EnsureQuestionCodesAreFree(service, new[] { questionCode });
        }

        /// <summary>
        /// Every code at once, in one query. A section added with twenty questions used to
        /// spend twenty round trips learning what one In condition answers.
        /// </summary>
        public static void EnsureQuestionCodesAreFree(IOrganizationService service, IEnumerable<string> questionCodes)
        {
            var codes = new List<object>();
            foreach (var code in questionCodes)
            {
                if (!string.IsNullOrWhiteSpace(code))
                {
                    codes.Add(code.Trim());
                }
            }

            if (codes.Count == 0)
            {
                return;
            }

            var query = new QueryExpression(QuestionEntity)
            {
                ColumnSet = new ColumnSet("al_questioncode"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questioncode", ConditionOperator.In, codes.ToArray());

            var taken = service.RetrieveMultiple(query).Entities;
            if (taken.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Question code '" + taken[0].GetAttributeValue<string>("al_questioncode") + "' is already in use.");
            }
        }

        /// <summary>
        /// The highest-numbered version of a question, carrying the columns asked for, or
        /// null when it has none. The version number, not the effective window, decides
        /// which is current: a dated-out version is still the latest until a successor
        /// exists, which is what lets RetireQuestion see that it has already been retired.
        /// </summary>
        public static Entity CurrentVersionOf(IOrganizationService service, Guid questionId, params string[] columns)
        {
            var query = new QueryExpression(VersionEntity)
            {
                ColumnSet = columns == null || columns.Length == 0
                    ? new ColumnSet("al_versionnumber")
                    : new ColumnSet(columns),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questionid", ConditionOperator.Equal, questionId);
            query.AddOrder("al_versionnumber", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0] : null;
        }

        /// <summary>
        /// The checklist version in force today (BR-013, AD-004), or the refusal the caller
        /// supplies when none is.
        ///
        /// The window is applied in memory rather than in the query: there are a handful of
        /// versions, and a date-range condition would put the answer at the mercy of how the
        /// platform compares a date-only column to a UTC timestamp. The effective-to day is
        /// counted as in force here, as both callers always have; whether that should match
        /// the question-version window (out of force from the start of effective-to) is a
        /// question for the checklist publisher, not one this consolidation answers.
        /// </summary>
        public static Guid ChecklistVersionInForce(IOrganizationService service, string refusal)
        {
            var query = new QueryExpression(ChecklistVersionEntity)
            {
                ColumnSet = new ColumnSet("al_effectivefrom", "al_effectiveto"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) },
                },
                Orders = { new OrderExpression("al_effectivefrom", OrderType.Descending) },
            };

            var today = DateTime.UtcNow.Date;
            foreach (var version in service.RetrieveMultiple(query).Entities)
            {
                var from = version.GetAttributeValue<DateTime?>("al_effectivefrom");
                var to = version.GetAttributeValue<DateTime?>("al_effectiveto");

                if ((!from.HasValue || from.Value.Date <= today) && (!to.HasValue || to.Value.Date >= today))
                {
                    return version.Id;
                }
            }

            throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
        }
    }
}
