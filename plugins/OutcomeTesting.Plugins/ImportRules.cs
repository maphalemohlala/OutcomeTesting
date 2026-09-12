using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Parsing and validation for an Intelligent Office task extract (BR-001, BR-002).
    ///
    /// This is the authoritative copy of the rule. The Code App carries the same parser so
    /// a user sees rejections before uploading, but that copy is an affordance, not a
    /// boundary: until BR-002 ran here, anyone posting to the Web API created cases with
    /// no validation at all (AD-003).
    ///
    /// Deliberately static and dependency-free so the rule can be tested without a
    /// Dataverse connection, in the same shape as <see cref="OutcomeRules"/> and
    /// <see cref="ResponseRules"/>.
    /// </summary>
    public static class ImportRules
    {
        /// <summary>al_casestatus for a freshly imported case (Imported, BR-001).</summary>
        public const int CaseStatusImported = 120910580;

        /// <summary>Longest raw row kept on an exception, matching al_rawdata.</summary>
        public const int RawDataLimit = 2000;

        /// <summary>
        /// Rows accepted in one call. A synchronous plug-in has two minutes, and an import
        /// that runs out of them fails with rows already written. Refusing a file that is
        /// too large is recoverable -- the user splits it; timing out halfway is not.
        /// </summary>
        public const int MaxRows = 1000;

        /// <summary>al_taxcheckrequired, the input DeriveRoute reads to pick the route.</summary>
        public const int TaxCheckRequiredYes = 120910560;

        public const int TaxCheckRequiredNo = 120910561;

        /// <summary>al_preorpostcheck; every task in a Pre-Advice extract is a pre-check.</summary>
        public const int PreOrPostCheckPre = 120910540;

        /// <summary>
        /// al_iooutcome, the outcome the extract already carried. Deliberately its own
        /// option set and its own column: it is what IO recorded, not the BR-005 grade a
        /// checker produces here (D3).
        /// </summary>
        public const int IoOutcomePass = 120910610;

        public const int IoOutcomePassWithIssues = 120910611;

        public const int IoOutcomeInsufficientEvidence = 120910612;

        public const int IoOutcomePotentialHarm = 120910613;

        /// <summary>al_iotaskstatus, the task's own status in IO.</summary>
        public const int IoTaskStatusNotStarted = 120910620;

        public const int IoTaskStatusInProgress = 120910621;

        public const int IoTaskStatusComplete = 120910622;

        /// <summary>Checklist slots the extract carries, whether or not they are used.</summary>
        private const int ChecklistSlots = 10;

        public enum ColumnKind
        {
            Text,
            Date,
            Choice,
        }

        public sealed class ColumnDef
        {
            public ColumnDef(string header, string attribute, ColumnKind kind, IDictionary<int, string> choices)
            {
                Header = header;
                Attribute = attribute;
                Kind = kind;
                Choices = choices;
            }

            /// <summary>Header text exactly as the extract carries it.</summary>
            public string Header { get; private set; }

            /// <summary>Target al_outcomecase column.</summary>
            public string Attribute { get; private set; }

            public ColumnKind Kind { get; private set; }

            /// <summary>Option-set map (value -&gt; label) for choice columns.</summary>
            public IDictionary<int, string> Choices { get; private set; }
        }

        /// <summary>One row that passed validation, ready to become an al_outcomecase.</summary>
        public sealed class ImportRow
        {
            public int RowNumber { get; set; }

            public string Reference { get; set; }

            /// <summary>Column values to write, already coerced to Dataverse types.</summary>
            public Dictionary<string, object> Values { get; set; }

            public string Raw { get; set; }
        }

        /// <summary>One row that was rejected, with the reason a user can act on (BR-002).</summary>
        public sealed class ImportRowError
        {
            public int RowNumber { get; set; }

            public string Reference { get; set; }

            public string Reason { get; set; }

            public string Raw { get; set; }
        }

        public sealed class ParseResult
        {
            public ParseResult()
            {
                Valid = new List<ImportRow>();
                Invalid = new List<ImportRowError>();
            }

            public List<ImportRow> Valid { get; private set; }

            public List<ImportRowError> Invalid { get; private set; }

            /// <summary>Set when the file has no usable header row; nothing was parsed.</summary>
            public string Fatal { get; set; }

            public int Total { get { return Valid.Count + Invalid.Count; } }
        }

        private static Dictionary<int, string> Options(params object[] pairs)
        {
            var map = new Dictionary<int, string>();
            for (var i = 0; i + 1 < pairs.Length; i += 2)
            {
                map[(int)pairs[i]] = (string)pairs[i + 1];
            }

            return map;
        }

        /// <summary>
        /// Columns of the Intelligent Office Pre-Advice Check task extract, under IO's own
        /// header names (2026-09-12 design §4). The Code App transcribes the workbook to CSV
        /// without renaming anything, so this table is the single place the extract's shape
        /// is known.
        ///
        /// TaskID is the import key and the only mandatory column; every other column is
        /// validated only when a value is present. The extract's remaining ~60 columns are
        /// constant, empty or personal data we do not hold (D8), and are deliberately absent.
        /// </summary>
        public static readonly ColumnDef[] Columns = new[]
        {
            new ColumnDef("TaskID", "al_casereference", ColumnKind.Text, null),
            new ColumnDef("ServiceCaseSequentialRef", "al_servicecaseref", ColumnKind.Text, null),
            new ColumnDef("ClientRef", "al_clientref", ColumnKind.Text, null),
            new ColumnDef("Client", "al_clientname", ColumnKind.Text, null),
            new ColumnDef("AdviserName", "al_advisername", ColumnKind.Text, null),
            new ColumnDef("AdviserEmail", "al_adviseremail", ColumnKind.Text, null),
            // AD-113: this is the name the file carried, not proof of allocation.
            new ColumnDef("AssignedTo", "al_checkername", ColumnKind.Text, null),
            // The paraplanner who raised the pre-advice check task (project owner,
            // 2026-09-12), which is one of the header fields the client asked be
            // pre-populated from IO rather than typed in.
            new ColumnDef("AssignedBy", "al_paraplanner", ColumnKind.Text, null),
            new ColumnDef("Status", "al_iotaskstatus", ColumnKind.Choice, Options(
                IoTaskStatusNotStarted, "Not Started",
                IoTaskStatusInProgress, "In Progress",
                IoTaskStatusComplete, "Complete")),
            new ColumnDef("Outcome", "al_iooutcome", ColumnKind.Choice, Options(
                IoOutcomePass, "Pass",
                IoOutcomePassWithIssues, "Pass with issues",
                IoOutcomeInsufficientEvidence, "Insufficient evidence",
                IoOutcomePotentialHarm, "Potential harm")),
            new ColumnDef("CompletedBy", "al_iocompletedby", ColumnKind.Text, null),
            new ColumnDef("CompletedDate", "al_iocompleteddate", ColumnKind.Date, null),
            new ColumnDef("StartDate", "al_taskstartdate", ColumnKind.Date, null),
            new ColumnDef("DueDate", "al_duedate", ColumnKind.Date, null),
            new ColumnDef("CreatedDate", "al_iocreateddate", ColumnKind.Date, null),
            new ColumnDef("CreatedBy", "al_iocreatedby", ColumnKind.Text, null),
            new ColumnDef("TaskType", "al_tasktype", ColumnKind.Text, null),
            new ColumnDef("WorkflowName", "al_workflowname", ColumnKind.Text, null),
            new ColumnDef("ServiceStatus", "al_servicestatus", ColumnKind.Text, null),
        };

        /// <summary>
        /// The reasons a paraplanner can select inside the IO task, and the discipline each
        /// one calls for (client, 2026-09-11). Tax wins where both are selected, because the
        /// client's rule is that Tax is always the starting point and the Tax team then
        /// decides whether AQS is genuinely owed -- which is the disposition override
        /// DeriveRoute already applies.
        ///
        /// Keys are matched case-insensitively after trimming. The two Tax items carry the
        /// short names the client used as well as the wording the workbook exports, because
        /// both name the same item and which one IO emits is not yet confirmed (design §5).
        /// </summary>
        public static readonly IDictionary<string, bool> ChecklistItems =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                { "Tax Check", true },
                { "Tax", true },
                { "Trust Documentation Check", true },
                { "Trust documentation", true },
                { "High Risk Item 1", false },
                { "High Risk Item 2", false },
                { "Enhanced Supervision", false },
                { "Pre-CAS Adviser", false },
                { "Leaver", false },
            };

        /// <summary>
        /// The canonical name for each accepted alias, so al_checklistitems reads the same
        /// whichever wording the extract used.
        /// </summary>
        private static readonly IDictionary<string, string> ChecklistCanonicalNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Tax", "Tax Check" },
                { "Trust documentation", "Trust Documentation Check" },
            };

        /// <summary>What one row's checklist columns said (design §5).</summary>
        public sealed class ChecklistSelection
        {
            public ChecklistSelection()
            {
                Items = new List<string>();
            }

            /// <summary>Selected item names, canonicalised, in the order the row carried them.</summary>
            public List<string> Items { get; private set; }

            public string CompletedBy { get; set; }

            public DateTime? CompletedOn { get; set; }

            /// <summary>True when a selected item calls for a Tax check.</summary>
            public bool RequiresTax { get; set; }

            /// <summary>Set when the row cannot be routed; the row is rejected (BR-002).</summary>
            public string Error { get; set; }
        }

        /// <summary>
        /// Reads the ChecklistItem/CompletedBy/CompletionDate triplets into the one selection
        /// the case carries.
        ///
        /// An item counts as selected when its name is one we recognise **and** it carries a
        /// completion stamp. The column number is never consulted, which is what makes this
        /// correct under both readings of a partial selection: if IO packs selections into
        /// the first free slots, every item present is stamped and counted; if it lists every
        /// item in fixed slots and stamps only the chosen ones, the unstamped ones are
        /// skipped. The question outstanding with the client confirms this reader rather than
        /// shaping it.
        ///
        /// All seven items in the supplied workbook share one stamp -- the paraplanner
        /// completes the checklist in a single action -- so one stamp is kept per case (D4).
        /// The earliest wins if they ever disagree; the item list is what routing reads.
        /// </summary>
        public static ChecklistSelection ReadChecklist(IList<string> fields, IDictionary<string, int> headerIndex)
        {
            var selection = new ChecklistSelection();

            for (var slot = 1; slot <= ChecklistSlots; slot++)
            {
                var name = HeaderCell(fields, headerIndex, "ChecklistItem" + slot);
                var by = HeaderCell(fields, headerIndex, "CompletedBy" + slot);
                var on = HeaderCell(fields, headerIndex, "CompletionDate" + slot);

                // No stamp means the paraplanner did not pick this item, whatever its name.
                // Checked before the name is recognised, so an item IO lists but nobody
                // selected cannot fail the row.
                if (name.Length == 0 || (by.Length == 0 && on.Length == 0))
                {
                    continue;
                }

                if (!ChecklistItems.ContainsKey(name))
                {
                    selection.Error = "Checklist item \"" + name
                        + "\" is not recognised, so the review route cannot be determined.";
                    return selection;
                }

                string canonical;
                selection.Items.Add(ChecklistCanonicalNames.TryGetValue(name, out canonical) ? canonical : name);

                if (ChecklistItems[name])
                {
                    selection.RequiresTax = true;
                }

                if (by.Length > 0 && string.IsNullOrEmpty(selection.CompletedBy))
                {
                    selection.CompletedBy = by;
                }

                var stamp = ParseDateTime(on);
                if (stamp.HasValue && (!selection.CompletedOn.HasValue || stamp.Value < selection.CompletedOn.Value))
                {
                    selection.CompletedOn = stamp.Value;
                }
            }

            if (selection.Items.Count == 0)
            {
                selection.Error =
                    "No checklist items are selected, so the review route cannot be determined.";
            }

            return selection;
        }

        /// <summary>Tokenises CSV text into rows of fields, honouring quotes and embedded newlines.</summary>
        public static List<List<string>> Tokenise(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            var inQuotes = false;
            text = text ?? string.Empty;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (inQuotes)
                {
                    if (ch == Quote)
                    {
                        if (i + 1 < text.Length && text[i + 1] == Quote)
                        {
                            cell.Append(Quote);
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(ch);
                    }

                    continue;
                }

                if (ch == Quote)
                {
                    inQuotes = true;
                }
                else if (ch == Comma)
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                }
                else if (ch == NewLine)
                {
                    row.Add(cell.ToString());
                    rows.Add(row);
                    row = new List<string>();
                    cell.Length = 0;
                }
                else if (ch != Return)
                {
                    cell.Append(ch);
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private const char Quote = '"';
        private const char Comma = ',';
        private const char NewLine = '\n';
        private const char Return = '\r';

        /// <summary>Quotes a CSV cell when it carries a delimiter, quote or newline.</summary>
        public static string CsvCell(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { Quote, Comma, Return, NewLine }) < 0)
            {
                return value;
            }

            return Quote + value.Replace("\"", "\"\"") + Quote;
        }

        /// <summary>
        /// Matches an option-set label case-insensitively, and also accepts the raw numeric
        /// option value so extracts that carry codes rather than labels still import.
        /// </summary>
        public static int? FindChoice(IDictionary<int, string> map, string label)
        {
            var needle = (label ?? string.Empty).Trim();
            foreach (var pair in map)
            {
                if (string.Equals(pair.Value, needle, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Key;
                }
            }

            int numeric;
            if (int.TryParse(needle, NumberStyles.Integer, CultureInfo.InvariantCulture, out numeric)
                && map.ContainsKey(numeric))
            {
                return numeric;
            }

            return null;
        }

        /// <summary>
        /// Accepts dd/mm/yyyy (UK), yyyy-mm-dd, or any parseable date. UK order is tried
        /// first and explicitly: an extract saying 03/09/2026 means 3 September, and letting
        /// a US-order parser read it as 9 March would import a wrong advice date silently.
        /// </summary>
        public static DateTime? ParseDate(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            DateTime parsed;
            var uk = new[] { "d/M/yyyy", "dd/MM/yyyy", "d/MM/yyyy", "dd/M/yyyy" };
            if (DateTime.TryParseExact(trimmed, uk, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }

            var isoHead = trimmed.Length > 10 ? trimmed.Substring(0, 10) : trimmed;
            var iso = new[] { "yyyy-MM-dd", "yyyy-M-d" };
            if (DateTime.TryParseExact(isoHead, iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }

            // A numeric date that matched neither accepted order is rejected here rather
            // than handed to the general parser. The general parser reads invariant culture,
            // which is month-first: 01/13/2026 would come back as 13 January, and an extract
            // that meant the 13th month is a data error, not a January date. Rejecting it
            // puts the row in front of a person; accepting it writes a wrong advice date
            // that nothing downstream can detect.
            if (IsNumericDateForm(trimmed))
            {
                return null;
            }

            // Written-out forms are unambiguous, so "31 Jan 2026" still imports.
            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed.Date;
            }

            return null;
        }

        /// <summary>True when the value is digits and date separators alone.</summary>
        private static bool IsNumericDateForm(string value)
        {
            foreach (var ch in value)
            {
                if (!char.IsDigit(ch) && ch != '/' && ch != '-' && ch != '.')
                {
                    return false;
                }
            }

            return value.Length > 0;
        }

        /// <summary>
        /// A date that may carry a time, as the checklist completion stamps do. The Code App
        /// resolves the workbook's serials to ISO before posting, so the time arrives as
        /// "2026-09-04T13:54:00"; a plain date still parses through <see cref="ParseDate"/>.
        /// </summary>
        public static DateTime? ParseDateTime(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            DateTime parsed;
            var iso = new[]
            {
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm",
            };
            if (DateTime.TryParseExact(trimmed, iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return parsed;
            }

            return ParseDate(trimmed);
        }

        /// <summary>
        /// Parses an extract into cases to create and rows to flag (BR-002). No business rule
        /// is invented: only TaskID is mandatory, and an unrecognised choice or an
        /// unreadable date becomes an exception carrying the reason, never a silent default.
        /// </summary>
        public static ParseResult ParseCsv(string csv)
        {
            var result = new ParseResult();

            // Blank lines are skipped but their position is kept, because the row number is
            // what a user navigates by in their own spreadsheet. Renumbering after a blank
            // line would point every later rejection at the wrong row.
            var rows = new List<List<string>>();
            var lineNumbers = new List<int>();
            var lines = Tokenise(csv);
            for (var i = 0; i < lines.Count; i++)
            {
                foreach (var cell in lines[i])
                {
                    if (!string.IsNullOrWhiteSpace(cell))
                    {
                        rows.Add(lines[i]);
                        lineNumbers.Add(i + 1);
                        break;
                    }
                }
            }

            if (rows.Count == 0)
            {
                result.Fatal = "The file is empty.";
                return result;
            }

            var header = new List<string>();
            foreach (var cell in rows[0])
            {
                header.Add((cell ?? string.Empty).Trim());
            }

            // Indexed by the extract's own header names, because the checklist triplets are
            // addressed by name too and are not ColumnDefs.
            var headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count; i++)
            {
                if (header[i].Length > 0 && !headerIndex.ContainsKey(header[i]))
                {
                    headerIndex[header[i]] = i;
                }
            }

            if (!headerIndex.ContainsKey("TaskID"))
            {
                result.Fatal =
                    "The file is missing the \"TaskID\" column. Use the Intelligent Office task extract.";
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var r = 1; r < rows.Count; r++)
            {
                var fields = rows[r];
                var rowNumber = lineNumbers[r];
                var raw = BuildRaw(fields);

                var reference = HeaderCell(fields, headerIndex, "TaskID");
                if (reference.Length == 0)
                {
                    result.Invalid.Add(new ImportRowError
                    {
                        RowNumber = rowNumber,
                        Reference = null,
                        Reason = "Missing TaskID (BR-001).",
                        Raw = raw,
                    });
                    continue;
                }

                // D2: one task is one case, so two tasks on one service case both import. It
                // is a repeated TaskID that is the error.
                if (seen.Contains(reference))
                {
                    result.Invalid.Add(new ImportRowError
                    {
                        RowNumber = rowNumber,
                        Reference = reference,
                        Reason = "Duplicate TaskID within this file.",
                        Raw = raw,
                    });
                    continue;
                }

                var values = new Dictionary<string, object>
                {
                    { "al_name", reference },
                    { "al_casereference", reference },
                };

                string rowError = null;
                foreach (var column in Columns)
                {
                    if (column.Attribute == "al_casereference" || !headerIndex.ContainsKey(column.Header))
                    {
                        continue;
                    }

                    var value = HeaderCell(fields, headerIndex, column.Header);
                    if (value.Length == 0)
                    {
                        continue;
                    }

                    if (column.Kind == ColumnKind.Text)
                    {
                        values[column.Attribute] = value;
                    }
                    else if (column.Kind == ColumnKind.Date)
                    {
                        var parsed = ParseDate(value);
                        if (!parsed.HasValue)
                        {
                            rowError = "\"" + column.Header + "\" is not a valid date: \"" + value + "\".";
                            break;
                        }

                        values[column.Attribute] = parsed.Value;
                    }
                    else
                    {
                        var choice = FindChoice(column.Choices, value);
                        if (!choice.HasValue)
                        {
                            rowError = "\"" + column.Header + "\" value \"" + value + "\" is not an accepted option.";
                            break;
                        }

                        values[column.Attribute] = choice.Value;
                    }
                }

                // Every task in a Pre-Advice extract is a pre-check; the extract has no
                // column of its own for it.
                if (HeaderCell(fields, headerIndex, "TaskType")
                    .StartsWith("Pre-Advice", StringComparison.OrdinalIgnoreCase))
                {
                    values["al_preorpostcheck"] = PreOrPostCheckPre;
                }

                // The checklist is the route's only input now (D6), so a row whose checklist
                // cannot be read is rejected rather than created without one. DeriveRoute
                // returns early on a null tax answer and writes nothing, which would leave
                // the case at Imported with no route and nothing downstream to surface it.
                if (rowError == null)
                {
                    var checklist = ReadChecklist(fields, headerIndex);
                    if (checklist.Error != null)
                    {
                        rowError = checklist.Error;
                    }
                    else
                    {
                        values["al_checklistitems"] = string.Join("\n", checklist.Items);
                        values["al_taxcheckrequired"] =
                            checklist.RequiresTax ? TaxCheckRequiredYes : TaxCheckRequiredNo;

                        if (!string.IsNullOrEmpty(checklist.CompletedBy))
                        {
                            values["al_checklistcompletedby"] = checklist.CompletedBy;
                        }

                        if (checklist.CompletedOn.HasValue)
                        {
                            values["al_checklistcompleteddate"] = checklist.CompletedOn.Value;
                        }
                    }
                }

                if (rowError != null)
                {
                    result.Invalid.Add(new ImportRowError
                    {
                        RowNumber = rowNumber,
                        Reference = reference,
                        Reason = rowError,
                        Raw = raw,
                    });
                    continue;
                }

                seen.Add(reference);
                result.Valid.Add(new ImportRow
                {
                    RowNumber = rowNumber,
                    Reference = reference,
                    Values = values,
                    Raw = raw,
                });
            }

            return result;
        }

        /// <summary>One cell, addressed by the extract's own header name.</summary>
        private static string HeaderCell(IList<string> fields, IDictionary<string, int> headerIndex, string header)
        {
            int index;
            if (!headerIndex.TryGetValue(header, out index) || index >= fields.Count)
            {
                return string.Empty;
            }

            return (fields[index] ?? string.Empty).Trim();
        }

        private static string BuildRaw(IList<string> fields)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(Comma);
                }

                builder.Append(CsvCell(fields[i]));
            }

            var raw = builder.ToString();
            return raw.Length > RawDataLimit ? raw.Substring(0, RawDataLimit) : raw;
        }

        /// <summary>
        /// Escapes a string for a JSON document. Written here because the plug-in sandbox
        /// (net462) carries no JSON dependency, matching the reader in UpdateCaseDetailsPlugin.
        /// </summary>
        public static string JsonEscape(string value)
        {
            var builder = new StringBuilder();
            foreach (var ch in value ?? string.Empty)
            {
                switch (ch)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                        {
                            builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
