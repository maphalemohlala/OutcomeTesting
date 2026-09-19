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
        private const string RouteEntity = "al_reviewroute";
        private const string CaseReviewRouteAttr = "al_reviewrouteid";
        private const string RouteRequiresAqsAttr = "al_requiresaqsreview";
        private const string AnswerChoiceAttr = "al_answerchoice";

        public const string FqAdviserFlag = "al_fqadviseraccountable";
        public const string FqParaplannerFlag = "al_fqparaplanneraccountable";
        public const string AqAdviserFlag = "al_aqadviseraccountable";
        public const string AqParaplannerFlag = "al_aqparaplanneraccountable";
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
                    "al_casereference", "al_advisername", "al_advisercode", "al_adviseremail",
                    "al_paraplanner", "al_paraplannercode",
                    "al_casetype", "al_productsolutiontype", "al_checkdate", "al_clientname", "al_preorpostcheck",
                    CaseReviewRouteAttr),
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
                    : Outcomes.EffectiveOutcomeLabel(outcomeRow);
                // One resolution, both values. Asking twice ran the same query twice per
                // case for no gain.
                var fileQualityAnswer = FileQuality.Resolve(userService, outcomeCase.Id);
                var fileQualityGrade = FileQuality.Label(fileQualityAnswer);
                var fileQualityChoice = FileQuality.Choice(fileQualityAnswer);
                var effectiveOutcome = Outcomes.EffectiveOutcome(outcomeRow);
                var code = "EXR-" + batchCode + "-" + caseRef;

                var incomplete = DescribeIncompleteRow(outcomeRow, fileQualityGrade, AqsExpected(userService, outcomeCase));
                if (incomplete != null)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix + "Case " + caseRef + " " + incomplete);
                }

                var record = new Entity(RecordEntity)
                {
                    ["al_name"] = "Export " + caseRef,
                    ["al_exportrecordcode"] = code,
                    ["al_exportbatchid"] = batchRef,
                    ["al_outcomecaseid"] = new EntityReference(CaseEntity, outcomeCase.Id),
                    ["al_advisername"] = outcomeCase.GetAttributeValue<string>("al_advisername"),
                    ["al_advisercode"] = outcomeCase.GetAttributeValue<string>("al_advisercode"),
                    // Trail Light col 21 (project owner, 2026-09-19). Snapshotted like every
                    // other column here rather than read live at file-build time, so a later
                    // correction to the case cannot change what an already-delivered batch
                    // says it sent.
                    ["al_adviseremail"] = outcomeCase.GetAttributeValue<string>("al_adviseremail"),
                    ["al_paraplannername"] = outcomeCase.GetAttributeValue<string>("al_paraplanner"),
                    ["al_paraplannercode"] = outcomeCase.GetAttributeValue<string>("al_paraplannercode"),
                    ["al_casetype"] = CommandHelpers.Formatted(outcomeCase, "al_casetype"),
                    ["al_productsolutiontype"] = CommandHelpers.Formatted(outcomeCase, "al_productsolutiontype"),
                    ["al_clientname"] = outcomeCase.GetAttributeValue<string>("al_clientname"),
                    ["al_preorpostcheck"] = CommandHelpers.Formatted(outcomeCase, "al_preorpostcheck"),
                    ["al_advicequalitygrade"] = adviceGrade,
                    ["al_filequalitygrade"] = fileQualityGrade,
                    ["al_fqfailadvisername"] = FlaggedText(
                        IsAccountable(outcomeRow, FqAdviserFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_advisername"),
                    ["al_fqfailadvisercode"] = FlaggedText(
                        IsAccountable(outcomeRow, FqAdviserFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_advisercode"),
                    ["al_fqfailparaplannername"] = FlaggedText(
                        IsAccountable(outcomeRow, FqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_paraplanner"),
                    ["al_fqfailparaplannercode"] = FlaggedText(
                        IsAccountable(outcomeRow, FqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_paraplannercode"),
                    ["al_aqfailadvisername"] = FlaggedText(
                        IsAccountable(outcomeRow, AqAdviserFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_advisername"),
                    ["al_aqfailadvisercode"] = FlaggedText(
                        IsAccountable(outcomeRow, AqAdviserFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_advisercode"),
                    ["al_aqfailparaplannername"] = FlaggedText(
                        IsAccountable(outcomeRow, AqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_paraplanner"),
                    ["al_aqfailparaplannercode"] = FlaggedText(
                        IsAccountable(outcomeRow, AqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                        outcomeCase, "al_paraplannercode"),
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

            // One Outcome per case is the intent, but nothing in the schema enforces it and
            // a re-review can leave a second. This row decides the exported grade and the
            // accountability gate, so picking whichever row Dataverse happened to return
            // first would make the export non-deterministic. Newest wins.
            query.AddOrder("createdon", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>
        /// Why this case cannot be exported, phrased to follow "Case {reference} ", or null
        /// when the row is complete.
        ///
        /// AD-039 is a fixed-position contract, so a blank in a graded column does not read
        /// downstream as "not assessed" — it reads as an assessment that came back empty.
        /// An incomplete row therefore refuses the batch and names the case, which is how
        /// the OD-024 accountability check already behaved.
        ///
        /// <paramref name="aqsExpected"/> is what separates incomplete from finished.
        /// Both graded columns are sourced from the AQS review — column 10 from Q-FQ-01,
        /// column 15 from the Outcome that <c>SubmitReviewPlugin.CreateOutcome</c> writes on
        /// the AQS submit — so on a Tax-only case neither has a source and never will.
        /// AD-075 settles OD-031 in favour of exporting those cases with the two columns
        /// blank, so the missing-value rules are asked only where an AQS review was due.
        /// The accountability rule still applies whenever an outcome exists, because an
        /// outcome on a case with no AQS review is a fact about the case either way.
        ///
        /// Pure and Entity-only so the decision is unit-testable without a fake
        /// organisation service, matching <see cref="FlaggedText"/>.
        /// </summary>
        public static string DescribeIncompleteRow(Entity outcomeRow, string fileQualityGrade, bool aqsExpected)
        {
            // The old gate sat inside `if (outcomeRow != null)`, so a case closed without an
            // Outcome escaped every check and exported blanks. Reachable by a manager moving
            // a case Submitted -> Closed, which AD-057 permits; the submit path itself always
            // writes the Outcome (SubmitReviewPlugin.CreateOutcome).
            if (outcomeRow == null)
            {
                return aqsExpected
                    ? "is closed with no outcome recorded, so the Advice Quality grade would be blank. "
                        + "Record the outcome, or reopen the case if it was closed in error."
                    : null;
            }

            var effective = Outcomes.EffectiveOutcome(outcomeRow);
            if (!effective.HasValue)
            {
                return aqsExpected
                    ? "has an outcome carrying neither an initial nor a final grade, so the "
                        + "Advice Quality grade would be blank (BR-007)."
                    : null;
            }

            // The OD-024 gate stood here. It refused a non-pass that recorded no
            // accountability, because four blank pairs read as "nobody is responsible"
            // rather than "nobody has said yet". Accountability is now derived from the
            // people the case already names (project owner, 2026-09-16), so a fail can no
            // longer export blank and the gate could only refuse rows that are complete.
            // See IsAccountable.

            if (aqsExpected && string.IsNullOrWhiteSpace(fileQualityGrade))
            {
                return "has no answer to Q-FQ-01 File quality outcome, so the File Quality "
                    + "grade would be blank. Record the file quality outcome before generating the export.";
            }

            return null;
        }

        /// <summary>
        /// Whether an AQS review was due on this case, which is what decides between "these
        /// columns are blank because nobody graded them" and "this case has no advice
        /// quality assessment to report" (AD-075, OD-031).
        ///
        /// The route answers it where one is set. Where it is null — every case created
        /// before the route seed existed — fall back to whether an AQS instance exists, the
        /// same fallback <c>SubmitReviewPlugin.AqsStillToCome</c> uses, so a legacy case is
        /// not silently reclassified as Tax-only.
        /// </summary>
        private static bool AqsExpected(IOrganizationService service, Entity outcomeCase)
        {
            var routeRef = outcomeCase.GetAttributeValue<EntityReference>(CaseReviewRouteAttr);
            if (routeRef != null)
            {
                var route = service.Retrieve(RouteEntity, routeRef.Id, new ColumnSet(RouteRequiresAqsAttr));
                return route.GetAttributeValue<bool?>(RouteRequiresAqsAttr) ?? false;
            }

            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, outcomeCase.Id);
            query.Criteria.AddCondition("al_reviewtype", ConditionOperator.Equal, ResponseRules.ReviewTypeAqs);

            return service.RetrieveMultiple(query).Entities.Count > 0;
        }

        private static bool AnyAccountability(Entity outcomeRow)
        {
            if (outcomeRow == null)
            {
                return false;
            }

            return (outcomeRow.GetAttributeValue<bool?>(FqAdviserFlag) ?? false)
                || (outcomeRow.GetAttributeValue<bool?>(FqParaplannerFlag) ?? false)
                || (outcomeRow.GetAttributeValue<bool?>(AqAdviserFlag) ?? false)
                || (outcomeRow.GetAttributeValue<bool?>(AqParaplannerFlag) ?? false);
        }

        /// <summary>
        /// The case value when that person is accountable, otherwise empty. AD-039
        /// attributes a fail to the adviser and/or the paraplanner, so a pair who is not
        /// accountable is written empty rather than filled in.
        /// </summary>
        public static string FlaggedText(bool accountable, Entity outcomeCase, string caseAttribute)
        {
            if (!accountable)
            {
                return string.Empty;
            }

            return outcomeCase.GetAttributeValue<string>(caseAttribute) ?? string.Empty;
        }

        /// <summary>
        /// Whether the person that <paramref name="flag"/> names carries this fail.
        ///
        /// A recorded judgement wins outright. Where nothing has been recorded the pair is
        /// derived from the people the case already names (project owner, 2026-09-16): the
        /// paraplanner compiles the file, so a File Quality fail is theirs; the adviser
        /// gives the advice, so an Advice Quality fail is theirs.
        ///
        /// Deriving is what makes a Tax case attributable at all. The four flags live on
        /// al_outcome and a Tax review creates no such row, so before this there was
        /// nowhere to record who was accountable for a Tax fail - 254397454 exported a
        /// file quality Fail with nobody named and nothing able to object.
        ///
        /// A row whose four flags are all false is "nobody has said yet", not "nobody is
        /// responsible", so it derives. Recording a deliberate nobody is therefore not
        /// expressible - it was not expressible before either, the OD-024 gate having
        /// refused a row that named no one.
        /// </summary>
        public static bool IsAccountable(Entity outcomeRow, string flag, int? fileQualityChoice, int? effectiveOutcome)
        {
            if (AnyAccountability(outcomeRow))
            {
                return outcomeRow.GetAttributeValue<bool?>(flag) ?? false;
            }

            if (flag == FqParaplannerFlag)
            {
                return FileQuality.Failed(fileQualityChoice);
            }

            if (flag == AqAdviserFlag)
            {
                return effectiveOutcome.HasValue && OutcomeRules.RequiresRemediation(effectiveOutcome.Value);
            }

            // The adviser does not carry the file and the paraplanner does not carry the
            // advice, so neither is derived; a judgement can still name them.
            return false;
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
