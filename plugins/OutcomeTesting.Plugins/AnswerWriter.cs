using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Writes an answer on behalf of the review page: creates the al_response row when none
    /// exists yet, or updates the one that does.
    ///
    /// This exists because the browser cannot create al_response directly - Power Pages
    /// refuses the @odata.bind association with 90040106, and no table permission value
    /// fixes it (2026-09-08 record, sections 20-35). The page PATCHes an
    /// <see cref="AnswerRequestPayload"/> onto al_reviewinstance.al_answerrequest instead,
    /// and this is what turns that payload into the row.
    ///
    /// Every rule still belongs to ResponseGuardPlugin, which fires on the Create and Update
    /// this issues (the submission lock, the AD-023 answer shape, the option subset, AD-020
    /// section ownership). Nothing here validates an answer; doing so would put the same rule
    /// in two places and let them drift. This class only shapes the write - it does not judge
    /// it.
    /// </summary>
    public static class AnswerWriter
    {
        private const string ResponseEntity = "al_response";

        /// <summary>
        /// The N:N relationship the review page used to $ref-associate directly - the same
        /// refused mechanism (90040106) as the al_response create itself - and its intersect
        /// entity (src/Other/Relationships/al_FailReason.xml, IntersectEntityName). Fail
        /// reasons now travel inside AnswerRequestPayload instead, and ReconcileFailReasons
        /// is what turns that list into the Associate/Disassociate calls the page can no
        /// longer make itself. ResponseGuardPlugin still guards both messages on this exact
        /// relationship (AD-053, PP-11); nothing here duplicates that guard.
        /// </summary>
        private const string FailReasonRelationship = "al_failreason_response";
        private const string FailReasonEntity = "al_failreason";
        private const string FailReasonIntersect = "al_al_failreason_al_response";

        /// <summary>
        /// Looks up an existing answer by the review and question version it belongs to,
        /// rather than by al_responsecode. That alternate key is stamped by
        /// ResponseGuardPlugin on Create (see ResponseRules.BuildResponseCode), so it is not
        /// available yet on the way in - matching on the two lookups directly is what a
        /// caller can always do, before the row exists and after.
        ///
        /// TopCount = 1 with no ordering is safe only because that same alternate key -
        /// al_ResponseCodeKey over al_responsecode (src/Entities/al_Response/Entity.xml) -
        /// makes a second al_response for this review and question version impossible to
        /// create, not merely unlikely. If that key is ever dropped or loosened, this query
        /// can silently start picking an arbitrary one of several rows instead of the one
        /// row that is guaranteed to exist; add an explicit order (or reject the ambiguity)
        /// before removing the key.
        /// </summary>
        public static Guid? FindExisting(IOrganizationService service, Guid reviewId, Guid questionVersionId)
        {
            var query = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            query.Criteria.AddCondition("al_questionversionid", ConditionOperator.Equal, questionVersionId);

            var found = service.RetrieveMultiple(query);
            return found.Entities.Count == 0 ? (Guid?)null : found.Entities[0].Id;
        }

        private static readonly string[] AnswerColumns =
            { "al_answertext", "al_answerrichtext", "al_answerchoice", "al_answerchoices", "al_answerdate" };

        private static readonly string[] ReviewColumns =
            { "al_reviewstatus", "al_reviewtype", "al_checklistversionid" };

        /// <summary>
        /// Carries this review's answers over to the questions they were moved to (project
        /// owner, 2026-11-17). A move gives the question a new code and version in the target
        /// section, so the answer on the old version would otherwise be left behind and the
        /// checker made to give it again. Run at save and at submit, so a move dated forward
        /// carries the first time the review is touched on or after its day. Returns how many
        /// answers it carried.
        ///
        /// Only an answer to the version a move dated out (its question's newest, ending the day
        /// the moved question's first version starts), along each move of a chain, and only into that first version:
        /// an answer to an older wording, or carried into a newer one, answers a question nobody
        /// asked. Only to a version in force today, in a section this review's discipline answers,
        /// on the review's checklist version, on the same answer scale, and never where the review
        /// already holds a row on the new question: each of those is a write ResponseGuardPlugin
        /// would refuse or a checker's answer it would overwrite. A submitted review is left alone.
        /// </summary>
        public static int CarryMovedAnswers(IOrganizationService service, Guid reviewId)
        {
            var moves = ChecklistQueries.QuestionMoves(service);
            if (moves.Count == 0)
            {
                return 0;
            }

            // Only answers on a moved question or one it became: no other answer can carry or block a carry.
            var codes = moves.Keys.Concat(moves.Values.Select(m => m.Key))
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(c => (object)c).ToArray();
            var query = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet(AnswerColumns.Concat(new[] { "al_questionversionid" }).ToArray()),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            var version = query.AddLink(ChecklistQueries.VersionEntity, "al_questionversionid", "al_questionversionid");
            version.EntityAlias = "v";
            version.Columns = new ColumnSet("al_questionid", "al_versionnumber", "al_responsetype", "al_effectiveto");
            var question = version.AddLink(ChecklistQueries.QuestionEntity, "al_questionid", "al_questionid");
            question.EntityAlias = "q";
            question.Columns = new ColumnSet("al_questioncode");
            question.LinkCriteria.AddCondition("al_questioncode", ConditionOperator.In, codes);
            var rows = service.RetrieveMultiple(query).Entities;

            var held = new HashSet<Guid>(
                rows.Select(r => Aliased<EntityReference>(r, "v.al_questionid")).Where(r => r != null).Select(r => r.Id));
            var answered = rows.Where(SubmitReviewPlugin.HasAnswer).ToList();

            var reachable = new HashSet<Guid>();
            foreach (var source in answered)
            {
                var code = Aliased<string>(source, "q.al_questioncode");
                KeyValuePair<string, Guid> next;
                for (var hops = 1; code != null && hops <= moves.Count && moves.TryGetValue(code, out next); hops++)
                {
                    reachable.Add(next.Value);
                    code = next.Key;
                }
            }

            if (reachable.Count == 0)
            {
                return 0;
            }

            // Each question a moved one became, with the answer nearest it along the moves: an
            // answer given after an earlier move is newer than one given before it. A hop counts
            // only from the newest version of its question - the one the move dated out - and
            // where that version ends the day the next question starts. An older wording can end
            // that day too, when a change of wording was dated on the move's day.
            Dictionary<Guid, int> newest;
            var firsts = FirstVersions(
                service,
                reachable.Concat(answered.Select(r => Aliased<EntityReference>(r, "v.al_questionid")).Where(r => r != null).Select(r => r.Id)),
                out newest);
            var sources = new Dictionary<Guid, Entity>();
            var distance = new Dictionary<Guid, int>();
            foreach (var source in answered)
            {
                var code = Aliased<string>(source, "q.al_questioncode");
                var endsOn = Aliased<DateTime?>(source, "v.al_effectiveto");
                var from = Aliased<EntityReference>(source, "v.al_questionid");
                var fromId = from == null ? Guid.Empty : from.Id;
                var number = VersionNumber(source);
                KeyValuePair<string, Guid> next;
                for (var hops = 1; code != null && hops <= moves.Count && moves.TryGetValue(code, out next); hops++)
                {
                    Entity first;
                    int latest;
                    if (!newest.TryGetValue(fromId, out latest) || latest != number
                        || !firsts.TryGetValue(next.Value, out first)
                        || !SameDay(endsOn, first.GetAttributeValue<DateTime?>("al_effectivefrom")))
                    {
                        break;
                    }

                    int known;
                    if (!distance.TryGetValue(next.Value, out known) || hops < known)
                    {
                        distance[next.Value] = hops;
                        sources[next.Value] = source;
                    }

                    endsOn = first.GetAttributeValue<DateTime?>("al_effectiveto");
                    fromId = next.Value;
                    number = 1;
                    code = next.Key;
                }
            }

            var now = DateTime.UtcNow;
            var targets = sources.Keys.Where(id => !held.Contains(id)).Select(id => firsts[id]).Where(v => InForce(v, now)).ToList();
            if (targets.Count == 0)
            {
                return 0;
            }

            var review = service.Retrieve("al_reviewinstance", reviewId, new ColumnSet(ReviewColumns));
            if (IsSubmitted(review))
            {
                return 0;
            }

            var carried = 0;
            foreach (var target in targets)
            {
                if (CarryInto(service, review, sources[target.GetAttributeValue<EntityReference>("al_questionid").Id], target, now))
                {
                    carried++;
                }
            }

            return carried;
        }

        /// <summary>
        /// The move's own half of the carry: every open review's answer to the version the move
        /// dated out, written onto the new question when the move is already in force. A move
        /// dated forward finds no version in force here and is carried at save or submit instead.
        /// The new question is brand new, so no review holds an answer on it yet.
        /// </summary>
        public static int CarryMovedAnswers(IOrganizationService service, Guid movedVersionId, Guid toQuestionId)
        {
            var now = DateTime.UtcNow;
            Entity target;
            if (!FirstVersions(service, new[] { toQuestionId }, out _).TryGetValue(toQuestionId, out target) || !InForce(target, now))
            {
                return 0;
            }

            var query = new QueryExpression(ResponseEntity)
            {
                ColumnSet = new ColumnSet(AnswerColumns.Concat(new[] { "al_reviewinstanceid" }).ToArray()),
            };
            query.Criteria.AddCondition("al_questionversionid", ConditionOperator.Equal, movedVersionId);
            var version = query.AddLink(ChecklistQueries.VersionEntity, "al_questionversionid", "al_questionversionid");
            version.EntityAlias = "v";
            version.Columns = new ColumnSet("al_responsetype");
            var review = query.AddLink("al_reviewinstance", "al_reviewinstanceid", "al_reviewinstanceid");
            review.EntityAlias = "r";
            review.Columns = new ColumnSet(ReviewColumns);

            // A submitted review is never carried into, so its answers are not read at all.
            var open = new FilterExpression(LogicalOperator.Or);
            open.AddCondition("al_reviewstatus", ConditionOperator.NotEqual, ResponseRules.StatusSubmitted);
            open.AddCondition("al_reviewstatus", ConditionOperator.Null);
            review.LinkCriteria.AddFilter(open);

            var carried = 0;
            foreach (var source in CommandHelpers.RetrieveAll(service, query).Where(SubmitReviewPlugin.HasAnswer))
            {
                var reviewRow = Unaliased(source, "r", "al_reviewinstance", ReviewColumns);
                reviewRow.Id = source.GetAttributeValue<EntityReference>("al_reviewinstanceid").Id;
                if (!IsSubmitted(reviewRow) && CarryInto(service, reviewRow, source, target, now))
                {
                    carried++;
                }
            }

            return carried;
        }

        /// <summary>
        /// The first version of each of these questions - the one a move creates - each with its
        /// section as "s.", by question; and, in <paramref name="newest"/>, each one's highest
        /// version number. One read for both.
        /// </summary>
        private static Dictionary<Guid, Entity> FirstVersions(
            IOrganizationService service, IEnumerable<Guid> questionIds, out Dictionary<Guid, int> newest)
        {
            var ids = questionIds.Distinct().Select(id => (object)id).ToArray();
            if (ids.Length == 0)
            {
                newest = new Dictionary<Guid, int>();
                return new Dictionary<Guid, Entity>();
            }

            var query = new QueryExpression(ChecklistQueries.VersionEntity)
            {
                ColumnSet = new ColumnSet("al_questionid", "al_versionnumber", "al_responsetype", "al_effectivefrom", "al_effectiveto"),
            };
            query.Criteria.AddCondition("al_questionid", ConditionOperator.In, ids);
            var section = query.AddLink(ChecklistQueries.QuestionEntity, "al_questionid", "al_questionid")
                .AddLink("al_section", "al_sectionid", "al_sectionid");
            section.EntityAlias = "s";
            section.Columns = new ColumnSet(ResponseGuardPlugin.SectionColumns);

            var byQuestion = service.RetrieveMultiple(query).Entities
                .GroupBy(v => v.GetAttributeValue<EntityReference>("al_questionid").Id)
                .ToList();
            newest = byQuestion.ToDictionary(g => g.Key, g => g.Max(v => v.GetAttributeValue<int>("al_versionnumber")));
            return byQuestion
                .Select(g => g.FirstOrDefault(v => v.GetAttributeValue<int>("al_versionnumber") == 1))
                .Where(v => v != null)
                .ToDictionary(v => v.GetAttributeValue<EntityReference>("al_questionid").Id);
        }

        private static bool InForce(Entity version, DateTime asOf)
        {
            return ResponseRules.IsVersionEffective(
                version.GetAttributeValue<DateTime?>("al_effectivefrom"),
                version.GetAttributeValue<DateTime?>("al_effectiveto"),
                asOf);
        }

        private static bool SameDay(DateTime? a, DateTime? b)
        {
            return a.HasValue && b.HasValue && a.Value.Date == b.Value.Date;
        }

        /// <summary>
        /// Writes <paramref name="source"/>'s answer and its live fail points onto
        /// <paramref name="target"/> when ResponseGuardPlugin would accept it; false, writing
        /// nothing, when it would not. Checked here first because a refused write inside a move
        /// or a submit would fail the whole command, not just this answer.
        /// </summary>
        private static bool CarryInto(IOrganizationService service, Entity review, Entity source, Entity target, DateTime now)
        {
            var reviewType = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            var section = Unaliased(target, "s", "al_section", ResponseGuardPlugin.SectionColumns);
            if (reviewType == null || ResponseGuardPlugin.SectionRefusal(section, reviewType.Value, now) != null)
            {
                return false;
            }

            var issued = review.GetAttributeValue<EntityReference>("al_checklistversionid");
            var sectionVersion = section.GetAttributeValue<EntityReference>("al_checklistversionid");
            if (issued == null || sectionVersion == null || issued.Id != sectionVersion.Id)
            {
                return false;
            }

            var fromType = Aliased<OptionSetValue>(source, "v.al_responsetype");
            var toType = target.GetAttributeValue<OptionSetValue>("al_responsetype");
            if (fromType == null || toType == null || fromType.Value != toType.Value)
            {
                return false;
            }

            var choice = source.GetAttributeValue<OptionSetValue>("al_answerchoice");
            var choices = source.GetAttributeValue<OptionSetValueCollection>("al_answerchoices");
            if (ResponseRules.ValidateAnswer(
                    toType.Value,
                    !string.IsNullOrWhiteSpace(source.GetAttributeValue<string>("al_answertext")),
                    source.GetAttributeValue<object>("al_answerdate") is DateTime,
                    choice == null ? (int?)null : choice.Value,
                    choices == null ? new int[0] : choices.Select(c => c.Value).ToArray(),
                    HtmlSanitiser.HasText(source.GetAttributeValue<string>("al_answerrichtext"))) != null)
            {
                return false;
            }

            var row = new Entity(ResponseEntity)
            {
                ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", review.Id),
                ["al_questionversionid"] = new EntityReference(ChecklistQueries.VersionEntity, target.Id),
            };
            foreach (var column in AnswerColumns.Where(source.Contains))
            {
                row[column] = source[column];
            }

            var responseId = service.Create(row);

            // Retired points are left behind: the submit would untick them anyway, and
            // ReconcileFailReasons refuses a new tick on one.
            var points = CurrentReasons(service, source.Id);
            if (points.Count > 0)
            {
                var retired = new QueryExpression(FailReasonEntity) { ColumnSet = new ColumnSet(false) };
                retired.Criteria.AddCondition("al_failreasonid", ConditionOperator.In, points.Select(p => (object)p).ToArray());
                retired.Criteria.AddCondition("statecode", ConditionOperator.Equal, 1);
                points.ExceptWith(service.RetrieveMultiple(retired).Entities.Select(r => r.Id));
            }

            if (points.Count > 0)
            {
                service.Associate(
                    ResponseEntity, responseId, new Relationship(FailReasonRelationship),
                    new EntityReferenceCollection(points.Select(p => new EntityReference(FailReasonEntity, p)).ToList()));
            }

            return true;
        }

        private static bool IsSubmitted(Entity review)
        {
            var status = review.GetAttributeValue<OptionSetValue>("al_reviewstatus");
            return status != null && status.Value == ResponseRules.StatusSubmitted;
        }

        private static int VersionNumber(Entity response)
        {
            return Aliased<int>(response, "v.al_versionnumber");
        }

        private static T Aliased<T>(Entity row, string key)
        {
            var aliased = row.GetAttributeValue<AliasedValue>(key);
            return aliased != null && aliased.Value is T ? (T)aliased.Value : default(T);
        }

        /// <summary>A linked row's columns, read back off their alias onto a plain entity.</summary>
        private static Entity Unaliased(Entity row, string alias, string logicalName, string[] columns)
        {
            var plain = new Entity(logicalName);
            foreach (var column in columns)
            {
                var aliased = row.GetAttributeValue<AliasedValue>(alias + "." + column);
                if (aliased != null)
                {
                    plain[column] = aliased.Value;
                }
            }

            return plain;
        }

        public static Guid Save(IOrganizationService service, Guid reviewId, AnswerRequestPayload payload)
        {
            var questionVersionId = AnswerRequest.RequireQuestionVersion(payload);

            // Checked here because the platform refuses an over-long column before any
            // pre-operation guard runs, with no plain reason (Case Notes, TEST, 2026-11-22).
            var tooLong = ResponseRules.AnswerTextRefusal(payload.AnswerText);
            if (tooLong != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix + tooLong);
            }

            var existing = FindExisting(service, reviewId, questionVersionId);

            var row = new Entity(ResponseEntity);
            ApplyAnswer(row, payload);

            Guid responseId;
            if (existing.HasValue)
            {
                // The lookups are deliberately absent here: an answer never moves onto
                // another review or question once it exists, which is the one thing the
                // Parent-scoped table permission cannot police once the row is there.
                row.Id = existing.Value;
                service.Update(row);
                responseId = existing.Value;
            }
            else
            {
                row["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", reviewId);
                row["al_questionversionid"] = new EntityReference("al_questionversion", questionVersionId);
                responseId = service.Create(row);
            }

            // Reconciled on both the create and the update path: a fail reason named on the
            // very first save has to attach just as much as one added on a later edit, and a
            // reason dropped from the list has to be disassociated whichever path got here.
            ReconcileFailReasons(service, responseId, payload.FailReasons, payload.RenderedReasons);

            // After the checker's own write, so an answer they have just given is never carried over.
            CarryMovedAnswers(service, reviewId);
            return responseId;
        }

        /// <summary>
        /// Associates every reason in <paramref name="wanted"/> that is not already linked to
        /// the response, and disassociates every currently-linked reason that is both in
        /// <paramref name="rendered"/> and missing from <paramref name="wanted"/> - the
        /// add/remove diff the page's $ref calls used to do one at a time, now done in one
        /// pass from the two lists the payload carries.
        ///
        /// <paramref name="rendered"/> exists because CurrentReasons reads every reason ever
        /// linked to this response, but the page's picker is category-filtered by the
        /// review's owner_role (template ~line 217: Tax sees al_category eq 120910403, AQS
        /// sees ne) - so a reason from the other team's category can be linked and rendered
        /// nowhere on this page. Before this parameter existed, such a reason was simply
        /// absent from <paramref name="wanted"/> (there was no checkbox for it to appear in),
        /// which this method read as "the reviewer unticked it" and disassociated on the very
        /// next save, including one triggered by editing an unrelated evidence note. That
        /// silently deleted FR-013 data while the page reported "Saved". Restricting removal
        /// to ids the page actually rendered a checkbox for closes that path: an id outside
        /// <paramref name="rendered"/> was never offered to this reviewer, so its absence from
        /// <paramref name="wanted"/> says nothing about their intent and must not be read as
        /// one.
        ///
        /// A null or empty <paramref name="rendered"/> removes nothing at all, deliberately -
        /// it is read as "the caller cannot vouch for what was on screen" rather than as
        /// "nothing was rendered, so remove everything currently linked". The latter is the
        /// exact bug this parameter exists to close: an older client, or a payload that failed
        /// to collect the rendered set, must fail safe (no deletion) rather than fail open
        /// (delete everything). Additions are unaffected either way - an id in
        /// <paramref name="wanted"/> was explicitly ticked by the reviewer this save, so it is
        /// always safe to associate regardless of what the rendered set says.
        ///
        /// An unparsable entry in either list is dropped silently rather than raising a
        /// PRECONDITION: this class shapes the write, it does not judge the payload, and a
        /// stray non-GUID string here is not a rule ResponseGuardPlugin has a rule for either
        /// - it simply cannot be resolved to something to associate or remove.
        /// </summary>
        public static void ReconcileFailReasons(
            IOrganizationService service, Guid responseId, string[] wanted, string[] rendered)
        {
            var desired = ParseGuids(wanted);
            var renderedSet = ParseGuids(rendered);

            var current = CurrentReasons(service, responseId);

            var toAdd = new EntityReferenceCollection();
            foreach (var id in desired)
            {
                if (!current.Contains(id))
                {
                    toAdd.Add(new EntityReference(FailReasonEntity, id));
                }
            }

            RefuseRetired(service, toAdd);

            var toRemove = new EntityReferenceCollection();
            foreach (var id in current)
            {
                if (renderedSet.Contains(id) && !desired.Contains(id))
                {
                    toRemove.Add(new EntityReference(FailReasonEntity, id));
                }
            }

            var target = new EntityReference(ResponseEntity, responseId);
            var relationship = new Relationship(FailReasonRelationship);

            if (toAdd.Count > 0)
            {
                service.Associate(target.LogicalName, target.Id, relationship, toAdd);
            }

            if (toRemove.Count > 0)
            {
                service.Disassociate(target.LogicalName, target.Id, relationship, toRemove);
            }
        }

        /// <summary>
        /// Unticks every fail point retired in the question library from a review's answers.
        /// Called by the submit while the review is still open: a retired point is not drawn on
        /// a check in progress, so a tick made before it was retired would otherwise reach the
        /// submitted review unseen by the checker. Once submitted, a review's ticks are kept
        /// whatever is retired later.
        /// </summary>
        public static void DropRetiredFailReasons(IOrganizationService service, Guid reviewId)
        {
            var responses = new QueryExpression(ResponseEntity) { ColumnSet = new ColumnSet(false) };
            responses.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            var responseIds = service.RetrieveMultiple(responses).Entities.Select(r => (object)r.Id).ToArray();
            if (responseIds.Length == 0)
            {
                return;
            }

            var linkQuery = new QueryExpression(FailReasonIntersect)
            {
                ColumnSet = new ColumnSet("al_responseid", "al_failreasonid"),
            };
            linkQuery.Criteria.AddCondition("al_responseid", ConditionOperator.In, responseIds);
            var links = service.RetrieveMultiple(linkQuery).Entities
                .Select(l => new System.Collections.Generic.KeyValuePair<Guid, Guid>(
                    l.GetAttributeValue<Guid>("al_responseid"), l.GetAttributeValue<Guid>("al_failreasonid")))
                .ToList();
            if (links.Count == 0)
            {
                return;
            }

            var retiredQuery = new QueryExpression(FailReasonEntity) { ColumnSet = new ColumnSet(false) };
            retiredQuery.Criteria.AddCondition(
                "al_failreasonid", ConditionOperator.In, links.Select(l => (object)l.Value).Distinct().ToArray());
            retiredQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, 1);
            var retired = new System.Collections.Generic.HashSet<Guid>(
                service.RetrieveMultiple(retiredQuery).Entities.Select(r => r.Id));

            foreach (var perResponse in links.Where(l => retired.Contains(l.Value)).GroupBy(l => l.Key))
            {
                service.Disassociate(
                    ResponseEntity,
                    perResponse.Key,
                    new Relationship(FailReasonRelationship),
                    new EntityReferenceCollection(
                        perResponse.Select(l => new EntityReference(FailReasonEntity, l.Value)).ToList()));
            }
        }

        /// <summary>
        /// A fail point retired in the question library is not drawn on a check in progress, so
        /// a NEW tick on one comes from a page loaded before it was retired or from a hand-made
        /// request. Either way it is refused rather than silently dropped, so the checker is not
        /// left believing it was recorded. A tick already linked is untouched here: only ids not
        /// yet on the response reach this, and the submit clears the rest.
        /// </summary>
        private static void RefuseRetired(IOrganizationService service, EntityReferenceCollection toAdd)
        {
            if (toAdd.Count == 0)
            {
                return;
            }

            var query = new QueryExpression(FailReasonEntity) { ColumnSet = new ColumnSet("al_name") };
            query.Criteria.AddCondition(
                "al_failreasonid", ConditionOperator.In, toAdd.Select(r => (object)r.Id).ToArray());
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 1);

            var retired = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            if (retired != null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "The fail point '"
                    + retired.GetAttributeValue<string>("al_name")
                    + "' has been retired and can no longer be ticked. Reload the page to see the current list.");
            }
        }

        /// <summary>
        /// Parses every well-formed GUID string in <paramref name="raw"/> into a set, dropping
        /// anything else silently. Shared by the wanted and rendered lists in
        /// ReconcileFailReasons, which both arrive as the same shape for the same reason: JSON
        /// has no native GUID type, so the template sends checkbox values as strings.
        /// </summary>
        private static System.Collections.Generic.HashSet<Guid> ParseGuids(string[] raw)
        {
            var set = new System.Collections.Generic.HashSet<Guid>();
            if (raw != null)
            {
                foreach (var value in raw)
                {
                    Guid parsed;
                    if (Guid.TryParse(value, out parsed))
                    {
                        set.Add(parsed);
                    }
                }
            }

            return set;
        }

        /// <summary>
        /// Reads the reasons currently linked to a response by querying the relationship's
        /// intersect entity directly, rather than starting from al_failreason and joining
        /// through it as the page's own Liquid fetch does.
        ///
        /// Both shapes are legal against real Dataverse: a native N:N intersect entity
        /// (al_al_failreason_al_response - src/Other/Relationships/al_FailReason.xml) cannot
        /// be written to directly (Create/Update/Delete on it are refused; Associate and
        /// Disassociate are the only writes), but it can always be read with an ordinary
        /// RetrieveMultiple/QueryExpression against its own logical name, filtering on either
        /// foreign key. That is what this does: filter the intersect rows on al_responseid
        /// and read back al_failreasonid. Querying from al_failreason and linking to the
        /// intersect table instead would additionally require every candidate al_failreason
        /// row to already exist for the join to find it - true on the platform, but nothing
        /// this method needs: it only wants the ids currently linked, not the failreason rows
        /// themselves, so there is nothing to gain from starting the query on that side.
        /// </summary>
        private static System.Collections.Generic.HashSet<Guid> CurrentReasons(
            IOrganizationService service, Guid responseId)
        {
            var query = new QueryExpression(FailReasonIntersect)
            {
                ColumnSet = new ColumnSet("al_failreasonid"),
            };
            query.Criteria.AddCondition("al_responseid", ConditionOperator.Equal, responseId);

            var set = new System.Collections.Generic.HashSet<Guid>();
            foreach (var row in service.RetrieveMultiple(query).Entities)
            {
                set.Add(row.GetAttributeValue<Guid>("al_failreasonid"));
            }

            return set;
        }

        /// <summary>
        /// Every answer column is written on every save, including as null, so clearing an
        /// answer clears it. ResponseGuardPlugin reads the effective value and decides
        /// whether the shape is legal for the question's response type (AD-023).
        /// </summary>
        private static void ApplyAnswer(Entity row, AnswerRequestPayload payload)
        {
            row["al_answertext"] = string.IsNullOrWhiteSpace(payload.AnswerText) ? null : payload.AnswerText;

            // Sanitised here rather than at the surfaces, because this is the one path every
            // write takes. HtmlSanitiser returns null for markup carrying no words, so an
            // editor that was opened and emptied clears the answer instead of storing
            // "<p><br></p>" and making an unanswered question look answered.
            row["al_answerrichtext"] = HtmlSanitiser.Clean(payload.AnswerRichText);

            row["al_answerchoice"] = payload.AnswerChoice.HasValue
                ? new OptionSetValue(payload.AnswerChoice.Value)
                : null;

            if (payload.AnswerChoices == null || payload.AnswerChoices.Length == 0)
            {
                row["al_answerchoices"] = null;
            }
            else
            {
                var choices = new OptionSetValueCollection();
                foreach (var choice in payload.AnswerChoices)
                {
                    choices.Add(new OptionSetValue(choice));
                }

                row["al_answerchoices"] = choices;
            }

            DateTime parsed;
            row["al_answerdate"] = !string.IsNullOrWhiteSpace(payload.AnswerDate)
                && DateTime.TryParse(
                    payload.AnswerDate,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal
                        | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out parsed)
                ? (object)parsed
                : null;
        }
    }
}
