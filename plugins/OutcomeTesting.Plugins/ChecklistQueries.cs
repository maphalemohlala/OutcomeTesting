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
        /// What the rest of the form says, for the rules in <see cref="ChecklistGating"/>
        /// (project owner, 2026-09-22).
        /// </summary>
        public struct GatingFacts
        {
            /// <summary>Any test point answered Insufficient evidence.</summary>
            public bool InsufficientAnywhere { get; set; }

            /// <summary>Any test point answered No or Fail.</summary>
            public bool NoOrFailAnywhere { get; set; }

            /// <summary>
            /// Every AML and CRA point in force is answered, and every one of them is Yes -
            /// which is what LOCKS the File Quality fail points on an AQS check
            /// (project owner, 2026-09-23).
            ///
            /// <para>
            /// False on a section that is part-answered, however clean the answers given so
            /// far. That is why this is counted against the questions in force rather than
            /// against the rows recorded: three Yeses and two blanks would otherwise read as
            /// "all Yes" and shut the block on a checker who had not finished.
            /// </para>
            /// </summary>
            public bool AmlCraAllYes { get; set; }

            /// <summary>
            /// The Tax check outcome (Q-TAX-02), which locks the Tax fail points on a Pass,
            /// or null where it is unanswered. Not the tax FILE QUALITY outcome.
            /// </summary>
            public int? TaxCheckOutcome { get; set; }

            /// <summary>
            /// This review's file quality outcome - Q-FQ-01 or Q-FQTAX-01, whichever
            /// discipline it is - which decides Remedial action required?, or null where it
            /// is unanswered. One field for both, because a review carries exactly one of
            /// them.
            /// </summary>
            public int? FileQualityOutcome { get; set; }
        }

        /// <summary>
        /// Reads the review's answers once and reports everything the gating rules need.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This replaces HasSuitabilityInsufficient, which asked one of these three questions
        /// and filtered to the Suitability core checks (item 10, 2026-09-19). The instruction
        /// on 2026-09-22 widened that rule to every section and added two more conditions, and
        /// three near-identical queries over the same rows is exactly the triplication this
        /// class exists to prevent - the more so here, where all three run inside the
        /// transaction holding a checker's save open.
        /// </para>
        /// <para>
        /// Two queries. The first reads the review's answers, and reads ALL of them: it was
        /// filtered to Fail, No and Insufficient evidence until 2026-09-23, when the rules
        /// started needing values that filter excluded - a Yes, to know the AML and CRA
        /// section is clean, and a Pass, to read either outcome. A filter that cannot answer
        /// the question is not an optimisation, and the cost is one review's responses, tens
        /// of rows with one column on them. The question code and section code ride back on
        /// the same rows through aliases, so deciding what each one means is done in memory by
        /// the pure rules, which are tested directly. Splitting that decision between a query
        /// operator and a predicate is how S-EXTRA once looked like a Suitability core check.
        /// </para>
        /// <para>
        /// The second reads the AML and CRA questions in force, because "all of them are Yes"
        /// cannot be seen in the answers alone - an unanswered question leaves no row to find.
        /// It runs on every review, including a Tax one that has no such section: it comes
        /// back empty there, AmlCraAllYes is false, and the Tax rule does not read it anyway.
        /// </para>
        /// <para>
        /// The links are INNER joins, so a response whose chain to a question and a section
        /// cannot be walked does not come back at all and counts as nothing. That is what
        /// HasSuitabilityInsufficient did before it and is not a change; it is recorded here
        /// because the rule now decides more, and because the opposite would be the safer
        /// direction if such a row could exist. It cannot: al_sectionid is required on
        /// al_question and al_questionid on al_questionversion, so the chain is complete for
        /// every answer the checklist can produce.
        /// </para>
        /// </remarks>
        public static GatingFacts ReadGatingFacts(IOrganizationService service, Guid reviewId)
        {
            var facts = new GatingFacts();

            var query = new QueryExpression("al_response")
            {
                ColumnSet = new ColumnSet("al_answerchoice"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);

            var versionLink = query.AddLink(VersionEntity, "al_questionversionid", "al_questionversionid");
            var questionLink = versionLink.AddLink(QuestionEntity, "al_questionid", "al_questionid");
            questionLink.EntityAlias = "q";
            questionLink.Columns = new ColumnSet("al_questioncode");
            var sectionLink = questionLink.AddLink("al_section", "al_sectionid", "al_sectionid");
            sectionLink.EntityAlias = "sec";
            sectionLink.Columns = new ColumnSet("al_sectioncode");

            // The AML and CRA questions this review answered cleanly, by question code. The
            // code is the key rather than the question id because a code is unique across the
            // checklist and is what comes back through the alias; two versions of one question
            // carry the same code, which is exactly the dedupe wanted.
            var amlCraClean = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var amlCraAnswered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in service.RetrieveMultiple(query).Entities)
            {
                var value = row.GetAttributeValue<OptionSetValue>("al_answerchoice");
                var choice = value == null ? (int?)null : value.Value;
                var code = Aliased(row, "q.al_questioncode");
                var section = Aliased(row, "sec.al_sectioncode");

                if (string.Equals(
                        section, ChecklistGating.AmlCraSectionCode, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(code))
                {
                    amlCraAnswered.Add(code.Trim());
                    if (ChecklistGating.IsAmlCraClean(choice))
                    {
                        amlCraClean.Add(code.Trim());
                    }
                }

                if (!choice.HasValue)
                {
                    continue;
                }

                // The two outcomes the gating rules READ, kept whatever they say. Recorded
                // before the outcome-question test below drops the row, which is the point:
                // these are the values being gated, so they are wanted here and nowhere else.
                if (Is(code, ChecklistGating.TaxCheckOutcomeQuestionCode))
                {
                    facts.TaxCheckOutcome = choice;
                }

                if (Is(code, FileQuality.QuestionCode) || Is(code, FileQuality.TaxQuestionCode))
                {
                    facts.FileQualityOutcome = choice;
                }

                // An outcome or a decision is what the test points add up TO, so it is not
                // part of the sum. ChecklistGating says why, and says it once.
                if (ChecklistGating.IsOutcomeQuestion(code))
                {
                    continue;
                }

                if (ChecklistGating.IsInsufficient(choice.Value))
                {
                    facts.InsufficientAnywhere = true;
                }

                if (ChecklistGating.IsNoOrFail(choice.Value))
                {
                    facts.NoOrFailAnywhere = true;
                }
            }

            // Clean AND complete. An AML and CRA question with no answer row at all leaves
            // nothing in either set, so it is caught by the count rather than by a value.
            var expected = AmlCraQuestionCodes(service, ChecklistVersionOf(service, reviewId));
            facts.AmlCraAllYes = expected.Count > 0
                && expected.IsSubsetOf(amlCraClean)
                && amlCraAnswered.IsSubsetOf(amlCraClean);

            return facts;
        }

        /// <summary>Whether a question code read off a row is the one named.</summary>
        private static bool Is(string code, string expected)
        {
            return code != null && code.Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The checklist version a review was issued, or null where it carries none.
        ///
        /// One read, and only because the AML and CRA count below has to be scoped by it.
        /// </summary>
        private static Guid? ChecklistVersionOf(IOrganizationService service, Guid reviewId)
        {
            var review = service.Retrieve(
                "al_reviewinstance", reviewId, new ColumnSet("al_checklistversionid"));
            var reference = review == null
                ? null
                : review.GetAttributeValue<EntityReference>("al_checklistversionid");

            return reference == null ? (Guid?)null : reference.Id;
        }

        /// <summary>
        /// The codes of the AML and CRA questions in force today, on the checklist version
        /// the review under test was issued.
        ///
        /// <para>
        /// <b>Scoped by version, deliberately.</b> al_section carries al_checklistversionid
        /// and the rest of the solution scopes by it - ResponseGuardPlugin compares the
        /// review's against the section's before it will accept an answer at all. Counting
        /// every S-AMLCRA question in the environment instead would mean a LATER version that
        /// adds a sixth point puts a code in this set that a review on the older version can
        /// never answer: "all Yes" becomes unreachable and the AQS fail-points lock silently
        /// stops working. It fails OPEN, which is the safe direction and exactly why nothing
        /// would report it.
        /// </para>
        /// <para>
        /// A review with no checklist version scopes to nothing and yields an empty set, so
        /// AmlCraAllYes is false and the fail points stay open. That is the same safe
        /// direction, and it is the honest answer: a review that was never issued a version
        /// has no section to be complete against.
        /// </para>
        /// <para>
        /// The effective window is applied in memory, as <see cref="ChecklistVersionInForce"/>
        /// applies it and for the same reason: there are a handful of versions, and a date
        /// condition in the query would put the answer at the mercy of how the platform
        /// compares a date-only column to a UTC timestamp. A question retired and succeeded
        /// (BR-013, AD-004) keeps its code, so the two versions collapse to one entry and a
        /// retired question does not go on being owed an answer.
        /// </para>
        /// </summary>
        private static HashSet<string> AmlCraQuestionCodes(
            IOrganizationService service, Guid? checklistVersionId)
        {
            if (!checklistVersionId.HasValue)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var query = new QueryExpression(VersionEntity)
            {
                ColumnSet = new ColumnSet("al_effectivefrom", "al_effectiveto"),
            };

            var questionLink = query.AddLink(QuestionEntity, "al_questionid", "al_questionid");
            questionLink.EntityAlias = "q";
            questionLink.Columns = new ColumnSet("al_questioncode");
            var sectionLink = questionLink.AddLink("al_section", "al_sectionid", "al_sectionid");
            sectionLink.LinkCriteria.AddCondition(
                "al_sectioncode", ConditionOperator.Equal, ChecklistGating.AmlCraSectionCode);
            sectionLink.LinkCriteria.AddCondition(
                "al_checklistversionid", ConditionOperator.Equal, checklistVersionId.Value);

            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var today = DateTime.UtcNow.Date;

            foreach (var version in service.RetrieveMultiple(query).Entities)
            {
                if (!ResponseRules.IsVersionEffective(
                        version.GetAttributeValue<DateTime?>("al_effectivefrom"),
                        version.GetAttributeValue<DateTime?>("al_effectiveto"),
                        today))
                {
                    continue;
                }

                var code = Aliased(version, "q.al_questioncode");
                if (!string.IsNullOrWhiteSpace(code))
                {
                    codes.Add(code.Trim());
                }
            }

            return codes;
        }

        /// <summary>A linked column's value, or null where the link did not come back.</summary>
        private static string Aliased(Entity row, string alias)
        {
            var value = row.GetAttributeValue<AliasedValue>(alias);
            return value == null ? null : value.Value as string;
        }

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
            var query = new QueryExpression("al_response")
            {
                ColumnSet = new ColumnSet("al_answerchoice"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            query.Criteria.AddCondition("al_answerchoice", ConditionOperator.NotNull);

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
