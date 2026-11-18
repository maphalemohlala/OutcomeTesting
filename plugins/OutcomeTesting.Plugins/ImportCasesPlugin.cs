using System;
using System.Collections.Generic;
using System.Globalization;
using System.ServiceModel;
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
            "TaskID already exists in Dataverse - skipped to avoid a duplicate case.";

        private const string RejectedReason =
            "Dataverse rejected this case without a reason that can be shown here; an administrator can find it in the plug-in trace log.";

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

            // One moment for the whole upload, shared by the batch's own stamp and by every
            // case's due date, so the two can never disagree about when this file arrived.
            var uploadedAt = DateTime.UtcNow;

            var batchId = systemService.Create(new Entity(BatchEntity)
            {
                ["al_name"] = CommandHelpers.Truncate(fileName, 100),
                ["al_importbatchcode"] = batchCode,
                ["al_batchstatus"] = new OptionSetValue(BatchStatusValidating),
                ["al_source"] = CommandHelpers.Truncate(fileName, 400),
                ["al_importedon"] = uploadedAt,
                ["al_totalrows"] = parsed.Total,
                ["al_importedcount"] = 0,
                ["al_exceptioncount"] = 0,
            });

            var existingReferences = FindExistingReferences(systemService, parsed.Valid);

            // Important 1: one memoising resolver for the whole import rather than an
            // uncached RetrieveMultiple per row - at 1000 rows sharing two route codes that
            // was up to 1000 round trips inside the two-minute plug-in budget.
            var findRoute = BuildRouteResolver(systemService);

            // The same for the two contact checks below: one query per address per import,
            // not two per row, since a file names the same few advisers and para-planners.
            var matchParaplanner = CasePeople.CachedMatcher(systemService, "para-planner");
            var matchAdviser = CasePeople.CachedMatcher(systemService, "adviser");

            var imported = 0;
            var duplicates = 0;
            var failed = 0;
            var report = new List<string>();
            var invalid = new List<ImportRules.ImportRowError>(parsed.Invalid);
            var ready = new List<KeyValuePair<ImportRules.ImportRow, Entity>>();

            // Pass 1 (project owner, 2026-11-17): everything that can be known before writing
            // is checked here, so a refused row is reported as invalid and every other row
            // still imports. Inside this synchronous plug-in a fault from a write aborts the
            // platform transaction, so finding a bad row by failing to write it cannot work.
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

                // Set here rather than read from the sheet (project owner, 2026-09-19). The
                // deadline is this system's, so it is derived from the upload and cannot be
                // moved by what Intelligent Office put in the DueDate column - nor, once
                // al_duedate left the editable set, by anyone editing the case afterwards.
                record["al_duedate"] = ImportRules.DueDateFor(uploadedAt);

                // A route that is not configured is a refusal raised here, not a service
                // fault, so it can be caught and the row reported. CreateRoutedCase derives
                // the route again below from the resolver's cache, at no extra query.
                try
                {
                    UpdateCaseDetailsPlugin.DeriveRoute(new Entity(CaseEntity), record, new List<string>(), findRoute);
                }
                catch (InvalidPluginExecutionException refusal)
                {
                    invalid.Add(new ImportRules.ImportRowError
                    {
                        RowNumber = row.RowNumber,
                        Reference = row.Reference,
                        Reason = RowFailureReason(refusal),
                        Raw = row.Raw,
                    });
                    continue;
                }

                // Both emails are now required on the row (Task 3); this reports one that
                // no active contact holds, or that two hold, on the day of the upload.
                //
                // Reported, never fatal: the case is real and the check must still happen.
                // Refusing the row would throw away work over a contact record that can be
                // fixed afterwards, and the row is not dropped silently either - it is
                // named in the report with the reason and the value that failed.
                //
                // Read as the system user: the person running the import is not guaranteed
                // read on Contact, and a diagnostic must not fail for want of a privilege.
                var paraplanner = matchParaplanner(
                    record.GetAttributeValue<string>(ImportRules.ParaplannerEmailAttribute));
                if (!paraplanner.IsMatch)
                {
                    report.Add(ReportRow(
                        row.RowNumber, row.Reference, "Imported (para-planner unmatched)",
                        paraplanner.Reason, row.Raw));
                }

                // The adviser, on the same terms (project owner, 2026-09-20). Reported
                // rather than fatal for the reason above, and it is a WEAKER statement
                // than the para-planner's: an adviser letter is still sent to the stored
                // address when no contact holds it, because losing a letter is worse than
                // sending one to an address the directory does not happen to carry. What
                // this line buys is knowing on the day that the adviser on this case is
                // nobody the system can point at.
                var adviser = matchAdviser(record.GetAttributeValue<string>("al_adviseremail"));
                if (!adviser.IsMatch)
                {
                    report.Add(ReportRow(
                        row.RowNumber, row.Reference, "Imported (adviser unmatched)",
                        adviser.Reason, row.Raw));
                }

                ready.Add(new KeyValuePair<ImportRules.ImportRow, Entity>(row, record));
            }

            // Pass 2: the writes. A fault here has already doomed the platform transaction, so
            // it is not swallowed into a count the platform would then throw away: the whole
            // file fails with one sentence naming the row (project owner, 2026-11-17).
            foreach (var item in ready)
            {
                try
                {
                    CreateRoutedCase(userService, findRoute, item.Value);
                }
                catch (Exception error)
                {
                    localPluginContext.Trace("Row " + item.Key.RowNumber + " could not be saved: " + error);
                    throw new InvalidPluginExecutionException(
                        WholeFileFailure(item.Key.RowNumber, item.Key.Reference, error), error);
                }

                imported++;
            }

            foreach (var bad in invalid)
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
        /// Creates one imported case and, when the file answered "Tax check required",
        /// derives its route (BR-004) and queues it (AD-093). The route is stamped before the
        /// create so the row never exists unrouted; the hops run after it because the
        /// lifecycle walker needs a row to move. A file that gives no answer leaves the case
        /// at Imported, where the dashboard shows it as awaiting a route. <paramref
        /// name="changes"/> is not kept: the import's audit event is batch-level, not a
        /// per-case history line the way the case-edit command's is.
        ///
        /// A missing route configuration throws DeriveRoute's precondition before anything is
        /// written; the import finds that in its first pass and reports the row. A fault from
        /// the create or the queue hop is not caught here (project owner, 2026-11-17): inside
        /// the import's transaction it has already aborted the whole file, so the caller
        /// fails the file rather than counting a case the platform will roll back.
        /// </summary>
        public static RoutedCaseResult CreateRoutedCase(IOrganizationService userService, Func<string, Guid?> findRoute, Entity record)
        {
            if (userService == null)
            {
                throw new ArgumentNullException(nameof(userService));
            }

            if (findRoute == null)
            {
                throw new ArgumentNullException(nameof(findRoute));
            }

            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var changes = new List<string>();
            UpdateCaseDetailsPlugin.DeriveRoute(new Entity(CaseEntity), record, changes, findRoute);

            var caseId = userService.Create(record);

            // Minor 4: read the status actually on the record rather than hardcoding
            // Imported, so this does nothing surprising if it is ever called for another
            // status.
            var status = record.GetAttributeValue<OptionSetValue>("al_casestatus");

            var queued = CaseQueueing.QueueIfRouted(
                userService,
                caseId,
                status != null ? status.Value : (int?)null,
                record.Contains("al_reviewrouteid"),
                changes);

            return new RoutedCaseResult(caseId, queued);
        }

        /// <summary>
        /// A route-code resolver that answers every call after the first from cache
        /// (Important 1). Built once before the import loop and passed to every row's
        /// <see cref="CreateRoutedCase"/> call, so a file of 1000 rows sharing the same two
        /// route codes costs at most two RetrieveMultiple calls rather than one per row.
        /// </summary>
        public static Func<string, Guid?> BuildRouteResolver(IOrganizationService service)
        {
            var cache = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
            return code =>
            {
                Guid? cached;
                if (cache.TryGetValue(code, out cached))
                {
                    return cached;
                }

                var resolved = UpdateCaseDetailsPlugin.FindRouteByCode(service, code);
                cache[code] = resolved;
                return resolved;
            };
        }

        /// <summary>
        /// What <see cref="CreateRoutedCase"/> produced: the case's id, and whether the
        /// post-create queue hop moved it (false when the case had no route to queue on).
        /// </summary>
        public struct RoutedCaseResult
        {
            public RoutedCaseResult(Guid caseId, bool queued)
            {
                CaseId = caseId;
                Queued = queued;
            }

            public Guid CaseId { get; }

            public bool Queued { get; }
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
                ["al_reason"] = CommandHelpers.Truncate(reason, ImportRules.RawDataLimit),
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
            // A guard on the case's create reaches this plug-in as a platform fault carrying
            // the guard's prefixed sentence, not as the guard's own exception type.
            var message = error == null ? null : error.Message;
            if (!(error is InvalidPluginExecutionException || error is FaultException<OrganizationServiceFault>)
                || string.IsNullOrWhiteSpace(message)
                || message.IndexOf("UNEXPECTED:", StringComparison.Ordinal) >= 0)
            {
                return RejectedReason;
            }

            var refusal = CommandHelpers.RefusalWithin(message);
            if (refusal != null)
            {
                return refusal.Substring(refusal.IndexOf(": ", StringComparison.Ordinal) + 2).Trim();
            }

            if (CommandHelpers.IsPrivilegeDenied(message))
            {
                return CommandHelpers.PrivilegeDeniedMessage.Substring(CommandHelpers.UnauthorizedPrefix.Length);
            }

            return error is InvalidPluginExecutionException ? message : RejectedReason;
        }

        /// <summary>
        /// The one sentence a failed write sends back for the whole file (project owner,
        /// 2026-11-17). Double quotes and backslashes are swapped out because the app cuts a
        /// refusal at the first of either, which would end the sentence mid-reason.
        /// </summary>
        public static string WholeFileFailure(int rowNumber, string reference, Exception error)
        {
            var reason = RowFailureReason(error).Trim();
            var sentence = "Row " + rowNumber.ToString(CultureInfo.InvariantCulture) + " (TaskID " + reference
                + ") could not be saved, so nothing from this file was imported: " + reason
                + (reason.EndsWith(".", StringComparison.Ordinal) ? string.Empty : ".")
                + " Fix that row and upload the file again.";

            return CommandHelpers.ValidationPrefix + sentence
                .Replace('"', '\'').Replace('\\', '/').Replace('\r', ' ').Replace('\n', ' ');
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
