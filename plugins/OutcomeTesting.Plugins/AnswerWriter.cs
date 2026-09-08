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

            if (existing.HasValue)
            {
                // The lookups are deliberately absent here: an answer never moves onto
                // another review or question once it exists, which is the one thing the
                // Parent-scoped table permission cannot police once the row is there.
                row.Id = existing.Value;
                service.Update(row);
                return existing.Value;
            }

            row["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", reviewId);
            row["al_questionversionid"] = new EntityReference("al_questionversion", questionVersionId);
            return service.Create(row);
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
