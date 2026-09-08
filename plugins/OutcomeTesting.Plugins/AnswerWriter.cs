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
            ReconcileFailReasons(service, responseId, payload.FailReasons);
            return responseId;
        }

        /// <summary>
        /// Associates every reason in <paramref name="wanted"/> that is not already linked to
        /// the response, and disassociates every currently-linked reason that is not in
        /// <paramref name="wanted"/> - the add/remove diff the page's $ref calls used to do
        /// one at a time, now done in one pass from the full list the payload carries.
        ///
        /// An unparsable entry is dropped silently rather than raising a PRECONDITION: this
        /// class shapes the write, it does not judge the payload, and a stray non-GUID string
        /// here is not a rule ResponseGuardPlugin has a rule for either - it simply cannot be
        /// resolved to something to associate.
        /// </summary>
        public static void ReconcileFailReasons(IOrganizationService service, Guid responseId, string[] wanted)
        {
            var desired = new System.Collections.Generic.HashSet<Guid>();
            if (wanted != null)
            {
                foreach (var raw in wanted)
                {
                    Guid parsed;
                    if (Guid.TryParse(raw, out parsed))
                    {
                        desired.Add(parsed);
                    }
                }
            }

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
                if (!desired.Contains(id))
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
