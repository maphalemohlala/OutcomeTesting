using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command GenerateExport (AD-003, AD-039, AD-034 manual only). Registered
    /// against the Custom API <c>al_GenerateExport</c>. Snapshots each Closed case into an
    /// al_exportrecord in the AD-039 20-column Trail Light shape, so the delivered values
    /// are preserved for reconciliation (BR-012). Enforces the caller holds Edit on
    /// <c>export.generate</c>, is idempotent per batch, and writes an Audit Event.
    /// </summary>
    public class GenerateExportPlugin : PluginBase
    {
        private const string InBatchId = "BatchId";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutBatchId = "BatchId";
        private const string OutRowCount = "RowCount";
        private const string OutStatus = "Status";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string BatchEntity = "al_exportbatch";
        private const string RecordEntity = "al_exportrecord";
        private const string CaseEntity = "al_outcomecase";
        private const string OutcomeEntity = "al_outcome";
        private const string ResponseEntity = "al_response";
        private const string ReviewEntity = "al_reviewinstance";
        private const string QuestionVersionEntity = "al_questionversion";
        private const string QuestionEntity = "al_question";
        private const string AnswerChoiceAttr = "al_answerchoice";
        private const string FileQualityQuestionCode = "Q-FQ-01";
        private const int CaseStatusClosed = 120910591;
        private const int BatchStatusDraft = 120910770;
        private const int BatchStatusGenerated = 120910771;
        private const int CommandGenerateExport = 120910775;

        public GenerateExportPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(GenerateExportPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var batchId = CommandHelpers.ParseRequiredGuid(context, InBatchId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, "export.generate", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandGenerateExport);
            if (existingAudit != null)
            {
                var priorCount = existingAudit.GetAttributeValue<string>("al_details");
                SetResponse(context, batchId.ToString("D"), priorCount ?? "0", "Generated", existingAudit.Id, false);
                return;
            }

            var batch = userService.Retrieve(BatchEntity, batchId, new ColumnSet("al_exportbatchcode", "al_batchstatus"));
            var batchCode = batch.GetAttributeValue<string>("al_exportbatchcode");
            var batchRef = new EntityReference(BatchEntity, batchId);

            // An export batch is a snapshot of what was produced, and the records are
            // upserted in place. Re-generating one that has already been produced would
            // rewrite those rows with today's values and revert a Delivered batch to
            // Generated, destroying the record of what was actually sent (AD-042). A
            // re-run is a new batch, not a second pass over this one. Retries of the same
            // intent are already handled by the idempotency replay above.
            var currentStatus = batch.GetAttributeValue<OptionSetValue>("al_batchstatus");
            if (currentStatus != null && currentStatus.Value != BatchStatusDraft)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "This export batch has already been generated. Create a new batch to produce a fresh export.");
            }

            var cases = new QueryExpression(CaseEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_casereference", "al_advisername", "al_advisercode", "al_paraplanner", "al_paraplannercode",
                    "al_casetype", "al_productsolutiontype", "al_checkdate", "al_clientname", "al_preorpostcheck"),
                Criteria = new FilterExpression(),
            };
            cases.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            cases.Criteria.AddCondition("al_casestatus", ConditionOperator.Equal, CaseStatusClosed);

            // Paged: a bare RetrieveMultiple stops at 5000 and the batch would then stamp
            // that truncated figure as its complete row count (AD-042), leaving no way to
            // detect the shortfall during reconciliation.
            var rows = 0;
            foreach (var outcomeCase in CommandHelpers.RetrieveAll(userService, cases))
            {
                var caseRef = outcomeCase.GetAttributeValue<string>("al_casereference") ?? outcomeCase.Id.ToString("D");
                var outcomeRow = ResolveOutcome(userService, outcomeCase.Id);
                var adviceGrade = outcomeRow == null
                    ? null
                    : (Formatted(outcomeRow, "al_finaloutcome") ?? Formatted(outcomeRow, "al_initialoutcome"));
                var fileQualityGrade = ResolveFileQualityGrade(userService, outcomeCase.Id);
                var code = "EXR-" + batchCode + "-" + caseRef;

                // A non-pass with no accountability recorded would export four blank
                // accountability pairs, which reads as "nobody is responsible" rather
                // than "nobody has said yet". Refusing here keeps an incomplete row out
                // of a delivered Trail Light file (AD-039, OD-024).
                if (outcomeRow != null)
                {
                    var effective = outcomeRow.GetAttributeValue<OptionSetValue>("al_finaloutcome")
                        ?? outcomeRow.GetAttributeValue<OptionSetValue>("al_initialoutcome");
                    var anyAccountability =
                        (outcomeRow.GetAttributeValue<bool?>("al_fqadviseraccountable") ?? false)
                        || (outcomeRow.GetAttributeValue<bool?>("al_fqparaplanneraccountable") ?? false)
                        || (outcomeRow.GetAttributeValue<bool?>("al_aqadviseraccountable") ?? false)
                        || (outcomeRow.GetAttributeValue<bool?>("al_aqparaplanneraccountable") ?? false);

                    if (effective != null && OutcomeRules.RequiresRemediation(effective.Value) && !anyAccountability)
                    {
                        throw new InvalidPluginExecutionException(
                            CommandHelpers.PreconditionPrefix
                            + "Case " + caseRef + " has a non-pass outcome with no fail accountability recorded. "
                            + "Record accountability before generating the export.");
                    }
                }

                var record = new Entity(RecordEntity)
                {
                    ["al_name"] = "Export " + caseRef,
                    ["al_exportrecordcode"] = code,
                    ["al_exportbatchid"] = batchRef,
                    ["al_outcomecaseid"] = new EntityReference(CaseEntity, outcomeCase.Id),
                    ["al_advisername"] = outcomeCase.GetAttributeValue<string>("al_advisername"),
                    ["al_advisercode"] = outcomeCase.GetAttributeValue<string>("al_advisercode"),
                    ["al_paraplannername"] = outcomeCase.GetAttributeValue<string>("al_paraplanner"),
                    ["al_paraplannercode"] = outcomeCase.GetAttributeValue<string>("al_paraplannercode"),
                    ["al_casetype"] = Formatted(outcomeCase, "al_casetype"),
                    ["al_productsolutiontype"] = Formatted(outcomeCase, "al_productsolutiontype"),
                    ["al_clientname"] = outcomeCase.GetAttributeValue<string>("al_clientname"),
                    ["al_preorpostcheck"] = Formatted(outcomeCase, "al_preorpostcheck"),
                    ["al_advicequalitygrade"] = adviceGrade,
                    ["al_filequalitygrade"] = fileQualityGrade,
                    ["al_fqfailadvisername"] = FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisername"),
                    ["al_fqfailadvisercode"] = FlaggedText(outcomeRow, "al_fqadviseraccountable", outcomeCase, "al_advisercode"),
                    ["al_fqfailparaplannername"] = FlaggedText(outcomeRow, "al_fqparaplanneraccountable", outcomeCase, "al_paraplanner"),
                    ["al_fqfailparaplannercode"] = FlaggedText(outcomeRow, "al_fqparaplanneraccountable", outcomeCase, "al_paraplannercode"),
                    ["al_aqfailadvisername"] = FlaggedText(outcomeRow, "al_aqadviseraccountable", outcomeCase, "al_advisername"),
                    ["al_aqfailadvisercode"] = FlaggedText(outcomeRow, "al_aqadviseraccountable", outcomeCase, "al_advisercode"),
                    ["al_aqfailparaplannername"] = FlaggedText(outcomeRow, "al_aqparaplanneraccountable", outcomeCase, "al_paraplanner"),
                    ["al_aqfailparaplannercode"] = FlaggedText(outcomeRow, "al_aqparaplanneraccountable", outcomeCase, "al_paraplannercode"),
                    ["al_separator"] = string.Empty,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };

                var checkDate = outcomeCase.GetAttributeValue<DateTime?>("al_checkdate");
                if (checkDate.HasValue)
                {
                    record["al_checkdate"] = checkDate.Value;
                }

                AssignUserRolePlugin.Upsert(userService, RecordEntity, "al_exportrecordcode", code, record);
                rows++;
            }

            var update = new Entity(BatchEntity, batchId)
            {
                ["al_batchstatus"] = new OptionSetValue(BatchStatusGenerated),
                ["al_rowcount"] = rows,
                ["al_generatedon"] = DateTime.UtcNow,
            };
            userService.Update(update);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandGenerateExport, "GenerateExport " + batchCode, BatchEntity, batchId,
                null, rows.ToString(), idempotencyKey, context);

            SetResponse(context, batchId.ToString("D"), rows.ToString(), "Generated", auditId, false);
        }

        private static string Formatted(Entity entity, string attribute)
        {
            return entity.FormattedValues.ContainsKey(attribute) ? entity.FormattedValues[attribute] : null;
        }

        // AD-039 col 15 Advice Quality grade = final outcome, or initial when not yet
        // finalised (BR-007). Also returns the raw outcome value and the four OD-024
        // accountability flags (Task 2), so the caller has both the grade and the
        // accountability judgement without a second read.
        private static Entity ResolveOutcome(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(OutcomeEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_finaloutcome", "al_initialoutcome",
                    "al_fqadviseraccountable", "al_fqparaplanneraccountable",
                    "al_aqadviseraccountable", "al_aqparaplanneraccountable"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>
        /// The case value when the Outcome flags that person accountable, otherwise empty.
        /// AD-039 attributes a fail to the adviser and/or the paraplanner, so a pair whose
        /// flag is false is written empty rather than filled in.
        /// </summary>
        internal static string FlaggedText(Entity outcomeRow, string flag, Entity outcomeCase, string caseAttribute)
        {
            if (outcomeRow == null || !(outcomeRow.GetAttributeValue<bool?>(flag) ?? false))
            {
                return string.Empty;
            }

            return outcomeCase.GetAttributeValue<string>(caseAttribute) ?? string.Empty;
        }

        // AD-039 col 10 File Quality grade = the answer to Q-FQ-01 "File quality outcome",
        // the one PassFail question in Checker Checklist V8 (knowledge/checklist-v8.md,
        // section S-FQOUT). It is held as a Response on the case's review, not on
        // al_Outcome, which carries only the BR-005 advice quality scale.
        //
        // Matched on the question's business code rather than its GUID so the export does
        // not break when the question is retired and succeeded (BR-013, AD-004): a
        // successor version keeps the code and stays the same question.
        private static string ResolveFileQualityGrade(IOrganizationService service, Guid caseId)
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
            question.LinkCriteria.AddCondition("al_questioncode", ConditionOperator.Equal, FileQualityQuestionCode);

            var found = service.RetrieveMultiple(query).Entities;
            if (found.Count == 0)
            {
                return null;
            }

            return found[0].FormattedValues.ContainsKey(AnswerChoiceAttr)
                ? found[0].FormattedValues[AnswerChoiceAttr]
                : null;
        }

        private static void SetResponse(IPluginExecutionContext context, string batchId, string rowCount, string status, Guid auditId, bool conflict)
        {
            context.OutputParameters[OutBatchId] = batchId;
            context.OutputParameters[OutRowCount] = rowCount;
            context.OutputParameters[OutStatus] = status;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }
    }
}
