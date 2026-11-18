using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        /// <summary>al_auditevent.al_command for MoveQuestion.</summary>
        public const int CommandMoveQuestion = 120910795;

        private static readonly System.Text.RegularExpressions.Regex MoveName =
            new System.Text.RegularExpressions.Regex(@"^MoveQuestion\s+(\S+)\s*->\s*(\S+)\s*$");

        /// <summary>al_auditevent.al_name's maximum length (src/Entities/al_AuditEvent/Entity.xml).</summary>
        public const int AuditNameMaxLength = 100;

        /// <summary>The name MoveQuestion gives its audit event; <see cref="QuestionMoves"/> reads it back.</summary>
        public static string MoveAuditName(string fromCode, string toCode)
        {
            return "MoveQuestion " + fromCode + " -> " + toCode;
        }

        /// <summary>
        /// Why a question already moved cannot be moved or retired again, or null when it has
        /// not been moved. Its version is dated out on the move's day; a second move or a
        /// retirement would date it out again, so the old question and the one it became would
        /// both be asked (or neither), and the carry, which follows one move per code, would
        /// stop finding the answer.
        /// </summary>
        public static string MovedRefusal(IOrganizationService service, string questionCode, string action)
        {
            KeyValuePair<string, Guid> move;
            if (string.IsNullOrEmpty(questionCode) || !QuestionMoves(service).TryGetValue(questionCode, out move))
            {
                return null;
            }

            return "That question has already been moved to " + move.Key + ", so it cannot be " + action
                + ". Make the change to " + move.Key + " instead.";
        }

        /// <summary>
        /// Every question move, as the code it left mapped to the code and question it became.
        /// The move's audit event is the link: it is written in the move's own transaction, names
        /// both codes, and targets the new question. Codes are unique and never freed, so the old
        /// code names one question for good. No column on the question records where it came from.
        /// </summary>
        public static Dictionary<string, KeyValuePair<string, Guid>> QuestionMoves(IOrganizationService service)
        {
            // shortcut: reads every move ever made; moves are rare administrator acts. Filter by date if they number thousands.
            var query = new QueryExpression("al_auditevent") { ColumnSet = new ColumnSet("al_name", "al_targetid") };
            query.Criteria.AddCondition("al_command", ConditionOperator.Equal, CommandMoveQuestion);

            var moves = new Dictionary<string, KeyValuePair<string, Guid>>(StringComparer.OrdinalIgnoreCase);
            foreach (var audit in service.RetrieveMultiple(query).Entities)
            {
                var match = MoveName.Match(audit.GetAttributeValue<string>("al_name") ?? string.Empty);
                Guid moved;
                if (match.Success && Guid.TryParse(audit.GetAttributeValue<string>("al_targetid"), out moved))
                {
                    moves[match.Groups[1].Value] = new KeyValuePair<string, Guid>(match.Groups[2].Value, moved);
                }
            }

            return moves;
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
                    // The move's audit name, which the answer carry reads back, splits on spaces.
                    if (code.Trim().Any(char.IsWhiteSpace))
                    {
                        throw new InvalidPluginExecutionException(
                            CommandHelpers.PreconditionPrefix +
                            "Question code '" + code.Trim() + "' contains a space. Question codes cannot contain spaces.");
                    }

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
        ///
        /// A command changing the question from a given day may act on it only when it is
        /// also the version in force that day: <see cref="ScheduledVersionRefusal"/> says why
        /// not when a later version is already waiting to start.
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
        /// Why a change from <paramref name="day"/> cannot act on <paramref name="latest"/>,
        /// or null when it can. A version still waiting to start on that day is not the one
        /// in force: ending it would leave the version before it running alongside whatever
        /// the change creates, so the question would be asked twice. A first version with
        /// nothing before it has nothing to overlap, so it may be changed before it starts.
        /// </summary>
        public static string ScheduledVersionRefusal(Entity latest, DateTime day)
        {
            var from = latest.GetAttributeValue<DateTime?>("al_effectivefrom");
            if (!from.HasValue || from.Value.Date <= day.Date || latest.GetAttributeValue<int>("al_versionnumber") <= 1)
            {
                return null;
            }

            return "A change to that question is already scheduled to start on "
                + from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                + ". Date this change on or after that day, so the question is never asked in two versions at once.";
        }

        /// <summary>
        /// The end date a successor starting on <paramref name="startsOn"/> must keep: a
        /// retirement scheduled for later would otherwise end with the version it was set on,
        /// and the question would stay asked for good.
        /// </summary>
        public static DateTime? CarriedEnd(Entity current, DateTime startsOn)
        {
            var end = current.GetAttributeValue<DateTime?>("al_effectiveto");
            return end.HasValue && end.Value.Date > startsOn.Date ? end : null;
        }

        /// <summary>
        /// The checklist version in force today (BR-013, AD-004), or the refusal the caller
        /// supplies when none is.
        ///
        /// The window is applied in memory rather than in the query: there are a handful of
        /// versions, and a date-range condition would put the answer at the mercy of how the
        /// platform compares a date-only column to a UTC timestamp. The window is the one
        /// every other effective-dated row uses (<see cref="ResponseRules.IsVersionEffective"/>):
        /// out of force from the start of the effective-to day. This alone used to count that
        /// day as in force, so on a changeover day a claim opened against the outgoing version.
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

            var now = DateTime.UtcNow;
            foreach (var version in service.RetrieveMultiple(query).Entities)
            {
                if (ResponseRules.IsVersionEffective(
                    version.GetAttributeValue<DateTime?>("al_effectivefrom"),
                    version.GetAttributeValue<DateTime?>("al_effectiveto"),
                    now))
                {
                    return version.Id;
                }
            }

            throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
        }
    }
}
