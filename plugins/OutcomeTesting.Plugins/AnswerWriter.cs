using System;
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

        public static Guid Save(IOrganizationService service, Guid reviewId, AnswerRequestPayload payload)
        {
            var questionVersionId = AnswerRequest.RequireQuestionVersion(payload);
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
