using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Parsing and validation for an Intelligent Office case extract (BR-001, BR-002).
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

        private const string GuideReference = "IO-000123";

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

            /// <summary>Header text as it appears in the template CSV.</summary>
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
        /// Case-header columns from knowledge/checklist-v8.md mapped to the deployed
        /// al_outcomecase schema, in the same order and with the same option labels as the
        /// upload template. IO reference is the BR-001 import key and the only mandatory
        /// column; every other column is validated only when a value is present, because no
        /// requirement makes them mandatory and inventing one would reject valid extracts.
        /// </summary>
        public static readonly ColumnDef[] Columns = new[]
        {
            new ColumnDef("IO reference", "al_casereference", ColumnKind.Text, null),
            new ColumnDef("Client name", "al_clientname", ColumnKind.Text, null),
            new ColumnDef("Adviser name", "al_advisername", ColumnKind.Text, null),
            new ColumnDef("Adviser code", "al_advisercode", ColumnKind.Text, null),
            new ColumnDef("Adviser status", "al_adviserstatus", ColumnKind.Choice, Options(
                120910500, "PreCAS", 120910501, "CAS", 120910502, "Enhanced", 120910503, "Watchlist")),
            new ColumnDef("Paraplanner", "al_paraplanner", ColumnKind.Text, null),
            new ColumnDef("Paraplanner code", "al_paraplannercode", ColumnKind.Text, null),
            new ColumnDef("Products", "al_products", ColumnKind.Text, null),
            new ColumnDef("Case type", "al_casetype", ColumnKind.Choice, Options(
                120910510, "New advice", 120910511, "Ongoing", 120910512, "Review", 120910513, "Switch/Transfer")),
            new ColumnDef("Advice date", "al_advicedate", ColumnKind.Date, null),
            new ColumnDef("Product / solution type", "al_productsolutiontype", ColumnKind.Choice, Options(
                120910520, "Accumulation investment", 120910521, "Accumulation Pension", 120910522, "IHT",
                120910523, "Protection", 120910524, "No change reviews")),
            new ColumnDef("Sample source", "al_samplesource", ColumnKind.Choice, Options(
                120910530, "Random", 120910531, "Mandatory", 120910532, "High Risk", 120910533, "Thematic")),
            new ColumnDef("Checker name", "al_checkername", ColumnKind.Text, null),
            new ColumnDef("Check date", "al_checkdate", ColumnKind.Date, null),
            new ColumnDef("Pre or post check", "al_preorpostcheck", ColumnKind.Choice, Options(
                120910540, "Pre", 120910541, "Post")),
            new ColumnDef("Vulnerable client", "al_vulnerableclient", ColumnKind.Choice, Options(
                120910550, "Yes", 120910551, "No", 120910552, "Potentially vulnerable", 120910553, "N/A")),
            new ColumnDef("Tax check required", "al_taxcheckrequired", ColumnKind.Choice, Options(
                120910560, "Yes", 120910561, "No")),
            new ColumnDef("Tax team disposition", "al_taxteamdisposition", ColumnKind.Choice, Options(
                120910570, "Submit to AQS", 120910571, "Return to paraplanner")),
        };

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

        /// <summary>The template ships a guide row of example values; it is never a case.</summary>
        public static bool IsGuideRow(IList<string> fields)
        {
            var reference = fields.Count > 0 ? (fields[0] ?? string.Empty).Trim() : string.Empty;
            return string.Equals(reference, GuideReference, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Parses an extract into cases to create and rows to flag (BR-002). No business rule
        /// is invented: only the IO reference is mandatory, and an unrecognised choice or an
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

            var columnIndex = new Dictionary<string, int>();
            foreach (var column in Columns)
            {
                for (var i = 0; i < header.Count; i++)
                {
                    if (string.Equals(header[i], column.Header, StringComparison.OrdinalIgnoreCase))
                    {
                        columnIndex[column.Attribute] = i;
                        break;
                    }
                }
            }

            if (!columnIndex.ContainsKey("al_casereference"))
            {
                result.Fatal = "The file is missing the \"IO reference\" column. Use the supplied template.";
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var r = 1; r < rows.Count; r++)
            {
                var fields = rows[r];
                if (IsGuideRow(fields))
                {
                    continue;
                }

                var rowNumber = lineNumbers[r];
                var raw = BuildRaw(fields);

                var reference = Cell(fields, columnIndex, "al_casereference");
                if (reference.Length == 0)
                {
                    result.Invalid.Add(new ImportRowError
                    {
                        RowNumber = rowNumber,
                        Reference = null,
                        Reason = "Missing IO reference (BR-001).",
                        Raw = raw,
                    });
                    continue;
                }

                if (seen.Contains(reference))
                {
                    result.Invalid.Add(new ImportRowError
                    {
                        RowNumber = rowNumber,
                        Reference = reference,
                        Reason = "Duplicate IO reference within this file.",
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
                    if (column.Attribute == "al_casereference" || !columnIndex.ContainsKey(column.Attribute))
                    {
                        continue;
                    }

                    var value = Cell(fields, columnIndex, column.Attribute);
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

        private static string Cell(IList<string> fields, IDictionary<string, int> columnIndex, string attribute)
        {
            int index;
            if (!columnIndex.TryGetValue(attribute, out index) || index >= fields.Count)
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
