using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command ImportCases (AD-003, BR-001, BR-002). Registered against the
    /// Custom API message <c>al_ImportCases</c>. Takes the raw extract, applies BR-002
    /// validation here, logs an al_importbatch, creates one al_outcomecase per valid row
    /// and records every rejected or skipped row as an al_importexception carrying the
    /// reason (FR-002).
    ///
    /// The extract is sent as text rather than as pre-parsed rows on purpose. If the
    /// client parsed and the server only stored, BR-002 would still be a client rule with
    /// a server-shaped wrapper around it, and a caller posting straight to the Web API
    /// would still bypass it — which is exactly the gap this command closes.
    ///
    /// The case creates run as the initiating user, so Dataverse create privilege stays
    /// the primary platform gate (NFR-SEC-01); the batch, exceptions and Audit Event are
    /// written by the system user so the record of a refused row survives regardless of
    /// what the caller can write.
    /// </summary>
    public class ImportCasesPlugin : PluginBase
    {
        private const string InFileName = "FileName";
        private const string InCsv = "Csv";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutBatchId = "BatchId";
        private const string OutBatchReference = "BatchReference";
        private const string OutTotal = "Total";
        private const string OutImported = "Imported";
        private const string OutDuplicates = "Duplicates";
        private const string OutFailed = "Failed";
        private const string OutReport = "Report";
        private const string OutAuditEventId = "AuditEventId";

        private const string BatchEntity = "al_importbatch";
        private const string ExceptionEntity = "al_importexception";
        private const string CaseEntity = "al_outcomecase";

        private const int BatchStatusValidating = 120910731;
        private const int BatchStatusCompleted = 120910732;
        private const int ExceptionStatusOpen = 120910740;
        private const int ExceptionStatusIgnored = 120910742;

        private const int CommandImportCases = 120910750;

        /// <summary>
        /// References checked per duplicate query. One query per row is what the client
        /// used to do; at 1000 rows that is 1000 round trips inside a two-minute budget.
        /// Batching keeps the whole check to a handful of queries.
        /// </summary>
        private const int ReferenceLookupChunk = 200;

        private const string DuplicateReason =
            "IO reference already exists in Dataverse - skipped to avoid a duplicate case.";

        private const string RejectedReason =
            "Dataverse rejected this case. Check the values and try again.";

        public ImportCasesPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ImportCasesPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService; // caller privileges gate the case writes
            var systemService = localPluginContext.PluginUserService;   // batch, exceptions and audit always write

            var fileName = CommandHelpers.GetRequiredString(context, InFileName);
            var csv = CommandHelpers.GetRequiredString(context, InCsv);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(systemService, context, "page.imports", PermissionHelpers.AccessEdit);

            // Idempotency: a replay returns the original batch rather than importing the
            // same file twice (NFR-REL-01). BR-001 reference skipping makes a re-upload
            // harmless, but it would still log a second batch and a wall of duplicate
            // exceptions, which is not what the first run reported.
            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandImportCases);
            if (existingAudit != null)
            {
                ReplayResponse(context, existingAudit);
                return;
            }

            var parsed = ImportRules.ParseCsv(csv);
            if (parsed.Fatal != null)
            {
                // Nothing was written, so this is a rejected request, not a failed import.
                throw new InvalidPluginExecutionException(CommandHelpers.ValidationPrefix + parsed.Fatal);
            }

            if (parsed.Total == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The file has no case rows to import.");
            }

            if (parsed.Total > ImportRules.MaxRows)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "This file has " + parsed.Total.ToString(CultureInfo.InvariantCulture) +
                    " rows. Import at most " + ImportRules.MaxRows.ToString(CultureInfo.InvariantCulture) +
                    " at a time, so the import completes rather than timing out part-written.");
            }

            var batchCode = BuildBatchCode(context);
            var batchId = systemService.Create(new Entity(BatchEntity)
            {
                ["al_name"] = Truncate(fileName, 100),
                ["al_importbatchcode"] = batchCode,
                ["al_batchstatus"] = new OptionSetValue(BatchStatusValidating),
                ["al_source"] = Truncate(fileName, 400),
                ["al_importedon"] = DateTime.UtcNow,
                ["al_totalrows"] = parsed.Total,
                ["al_importedcount"] = 0,
                ["al_exceptioncount"] = 0,
            });

            var existingReferences = FindExistingReferences(systemService, parsed.Valid);

            var imported = 0;
            var duplicates = 0;
            var failed = 0;
            var report = new List<string>();

            foreach (var row in parsed.Valid)
            {
                if (existingReferences.Contains(row.Reference))
                {
                    duplicates++;
                    report.Add(ReportRow(row.RowNumber, row.Reference, "Duplicate (skipped)", DuplicateReason, row.Raw));
                    RecordException(systemService, batchId, batchCode, row.RowNumber, row.Reference,
                        DuplicateReason, row.Raw, ExceptionStatusIgnored);
                    continue;
                }

                var record = new Entity(CaseEntity);
                foreach (var value in row.Values)
                {
                    record[value.Key] = value.Value is int
                        ? (object)new OptionSetValue((int)value.Value)
                        : value.Value;
                }

                record["al_casestatus"] = new OptionSetValue(ImportRules.CaseStatusImported);

                try
                {
                    userService.Create(record);
                    imported++;

                    // Two rows in the same file naming the same reference are already caught
                    // in the parse; this covers the reference becoming taken mid-import.
                    existingReferences.Add(row.Reference);
                }
                catch (Exception error)
                {
                    failed++;

                    // A guard plug-in refusing the row has already written a message meant
                    // for a person, so keep it; anything else is technical detail that goes
                    // to the trace log and never to the screen (NFR-OBS-01).
                    var reason = RowFailureReason(error);
                    localPluginContext.Trace("Row " + row.RowNumber + " rejected: " + error.Message);

                    report.Add(ReportRow(row.RowNumber, row.Reference, "Failed", reason, row.Raw));
                    RecordException(systemService, batchId, batchCode, row.RowNumber, row.Reference,
                        reason, row.Raw, ExceptionStatusOpen);
                }
            }

            foreach (var bad in parsed.Invalid)
            {
                failed++;
                report.Add(ReportRow(bad.RowNumber, bad.Reference, "Invalid", bad.Reason, bad.Raw));
                RecordException(systemService, batchId, batchCode, bad.RowNumber, bad.Reference,
                    bad.Reason, bad.Raw, ExceptionStatusOpen);
            }

            systemService.Update(new Entity(BatchEntity, batchId)
            {
                ["al_batchstatus"] = new OptionSetValue(BatchStatusCompleted),
                ["al_importedcount"] = imported,
                ["al_exceptioncount"] = parsed.Total - imported,
            });

            var details = BuildDetails(batchCode, parsed.Total, imported, duplicates, failed);
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService,
                CommandImportCases,
                "ImportCases " + batchCode,
                BatchEntity,
                batchId,
                null,
                details,
                idempotencyKey,
                context);

            SetResponse(context, batchId, batchCode, parsed.Total, imported, duplicates, failed,
                "[" + string.Join(",", report.ToArray()) + "]", auditId);
        }

        /// <summary>
        /// Looks up which of the file's references already exist (BR-001 makes a re-upload
        /// idempotent by skipping them). Chunked, because a single In condition with a
        /// thousand values is a query Dataverse may refuse outright.
        /// </summary>
        public static HashSet<string> FindExistingReferences(
            IOrganizationService service,
            IList<ImportRules.ImportRow> rows)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var chunk = new List<object>();

            for (var i = 0; i < rows.Count; i++)
            {
                chunk.Add(rows[i].Reference);
                if (chunk.Count < ReferenceLookupChunk && i + 1 < rows.Count)
                {
                    continue;
                }

                var query = new QueryExpression(CaseEntity)
                {
                    ColumnSet = new ColumnSet("al_casereference"),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_casereference", ConditionOperator.In, chunk.ToArray());

                foreach (var match in CommandHelpers.RetrieveAll(service, query))
                {
                    var reference = match.GetAttributeValue<string>("al_casereference");
                    if (!string.IsNullOrEmpty(reference))
                    {
                        found.Add(reference);
                    }
                }

                chunk.Clear();
            }

            return found;
        }

        private static void RecordException(
            IOrganizationService service,
            Guid batchId,
            string batchCode,
            int rowNumber,
            string caseReference,
            string reason,
            string raw,
            int status)
        {
            var code = batchCode + "-R" + rowNumber.ToString(CultureInfo.InvariantCulture);
            var record = new Entity(ExceptionEntity)
            {
                ["al_name"] = code,
                ["al_importexceptioncode"] = code,
                ["al_exceptionstatus"] = new OptionSetValue(status),
                ["al_importbatchid"] = new EntityReference(BatchEntity, batchId),
                ["al_reason"] = Truncate(reason, ImportRules.RawDataLimit),
                ["al_rownumber"] = rowNumber,
                ["al_rawdata"] = raw,
            };

            if (!string.IsNullOrEmpty(caseReference))
            {
                record["al_casereference"] = caseReference;
            }

            service.Create(record);
        }

        /// <summary>
        /// The batch code has to be unique and has to be generated here: a client-supplied
        /// one is a value a retry would repeat. The correlation id is per execution, so a
        /// replay of the same intent is caught by the idempotency key instead.
        /// </summary>
        private static string BuildBatchCode(IPluginExecutionContext context)
        {
            return "BATCH-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
                + "-" + context.CorrelationId.ToString("N").Substring(0, 6).ToUpperInvariant();
        }

        /// <summary>
        /// The counts a replay has to be able to answer with, recorded on the Audit Event
        /// because that is the row the idempotency lookup finds.
        /// </summary>
        public static string BuildDetails(string batchCode, int total, int imported, int duplicates, int failed)
        {
            return string.Join("|", new[]
            {
                batchCode,
                total.ToString(CultureInfo.InvariantCulture),
                imported.ToString(CultureInfo.InvariantCulture),
                duplicates.ToString(CultureInfo.InvariantCulture),
                failed.ToString(CultureInfo.InvariantCulture),
            });
        }

        public static string ReportRow(int rowNumber, string caseReference, string status, string reason, string raw)
        {
            var builder = new StringBuilder();
            builder.Append("{\"rowNumber\":").Append(rowNumber.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"caseReference\":");
            builder.Append(caseReference == null ? "null" : "\"" + ImportRules.JsonEscape(caseReference) + "\"");
            builder.Append(",\"status\":\"").Append(ImportRules.JsonEscape(status)).Append("\"");
            builder.Append(",\"reason\":\"").Append(ImportRules.JsonEscape(reason)).Append("\"");
            builder.Append(",\"raw\":\"").Append(ImportRules.JsonEscape(raw)).Append("\"}");
            return builder.ToString();
        }

        /// <summary>
        /// The reason recorded against a row Dataverse refused. A guard plug-in raises an
        /// InvalidPluginExecutionException whose message is already written for a person,
        /// so it is kept with the command prefix stripped; every other failure carries
        /// platform detail that must not reach a screen (NFR-OBS-01).
        /// </summary>
        public static string RowFailureReason(Exception error)
        {
            var refusal = error as InvalidPluginExecutionException;
            if (refusal == null || string.IsNullOrWhiteSpace(refusal.Message))
            {
                return RejectedReason;
            }

            var message = refusal.Message;
            var prefixes = new[]
            {
                CommandHelpers.ValidationPrefix,
                CommandHelpers.PreconditionPrefix,
                CommandHelpers.UnauthorizedPrefix,
                CommandHelpers.NotFoundPrefix,
                CommandHelpers.ConflictPrefix,
            };

            foreach (var prefix in prefixes)
            {
                var at = message.IndexOf(prefix, StringComparison.Ordinal);
                if (at >= 0)
                {
                    return message.Substring(at + prefix.Length).Trim();
                }
            }

            return message;
        }

        private static string Truncate(string value, int length)
        {
            value = value ?? string.Empty;
            return value.Length > length ? value.Substring(0, length) : value;
        }

        /// <summary>
        /// Replays a prior run from its Audit Event. The report is deliberately not
        /// reconstructed: the exceptions are rows on the batch and the screen reads them
        /// from there, so inventing a partial report here would be the one copy that could
        /// disagree with Dataverse.
        /// </summary>
        private void ReplayResponse(IPluginExecutionContext context, Entity audit)
        {
            var parts = (audit.GetAttributeValue<string>("al_details") ?? string.Empty).Split('|');
            var batchId = Guid.Empty;
            Guid.TryParse(audit.GetAttributeValue<string>("al_targetid"), out batchId);

            SetResponse(
                context,
                batchId,
                parts.Length > 0 ? parts[0] : string.Empty,
                ParseCount(parts, 1),
                ParseCount(parts, 2),
                ParseCount(parts, 3),
                ParseCount(parts, 4),
                "[]",
                audit.Id);
        }

        private static int ParseCount(string[] parts, int index)
        {
            int value;
            if (index < parts.Length && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return 0;
        }

        private static void SetResponse(
            IPluginExecutionContext context,
            Guid batchId,
            string batchReference,
            int total,
            int imported,
            int duplicates,
            int failed,
            string report,
            Guid auditEventId)
        {
            // Counts cross the wire as strings, matching every other command's contract
            // (al_GenerateExport returns RowCount the same way). The client parses them.
            context.OutputParameters[OutBatchId] = batchId.ToString("D");
            context.OutputParameters[OutBatchReference] = batchReference;
            context.OutputParameters[OutTotal] = total.ToString(CultureInfo.InvariantCulture);
            context.OutputParameters[OutImported] = imported.ToString(CultureInfo.InvariantCulture);
            context.OutputParameters[OutDuplicates] = duplicates.ToString(CultureInfo.InvariantCulture);
            context.OutputParameters[OutFailed] = failed.ToString(CultureInfo.InvariantCulture);
            context.OutputParameters[OutReport] = report;
            context.OutputParameters[OutAuditEventId] = auditEventId.ToString("D");
        }
    }
}
