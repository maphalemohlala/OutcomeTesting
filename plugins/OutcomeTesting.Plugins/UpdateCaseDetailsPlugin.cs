using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command UpdateCaseDetails (AD-003). Registered against the Custom API
    /// message <c>al_UpdateCaseDetails</c>. A manager amends the editable case header from
    /// the worklist: status, review route (the AD-036 Tax↔AQS reassignment), priority and
    /// due date, with a mandatory reason. Authorization is enforced by the application RBAC
    /// model (AD-041): the caller must hold Edit on <c>page.cases</c>, and the write runs as
    /// the initiating user so Dataverse privilege remains the platform gate. The command
    /// enforces optimistic concurrency and idempotency, and writes an immutable Audit Event
    /// (BR-012, NFR-AUD-01) recording the before/after of every changed field.
    /// </summary>
    public class UpdateCaseDetailsPlugin : PluginBase
    {
        private const string InTargetId = "TargetId";
        private const string InStatus = "Status";
        private const string InRouteId = "RouteId";
        private const string InPriority = "Priority";
        private const string InDueDate = "DueDate";
        private const string InFields = "Fields";
        private const string InReason = "Reason";
        private const string InExpectedRowVersion = "ExpectedRowVersion";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutCaseId = "CaseId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string CaseEntity = "al_outcomecase";
        private const string RouteEntity = "al_reviewroute";
        private const string StatusAttr = "al_casestatus";
        private const string RouteAttr = "al_reviewrouteid";
        private const string PriorityAttr = "al_priority";
        private const string DueDateAttr = "al_duedate";

        private const int CommandUpdateCaseDetails = 120910778;

        public UpdateCaseDetailsPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(UpdateCaseDetailsPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService; // caller privileges gate the write
            var systemService = localPluginContext.PluginUserService;   // audit + role lookup

            var targetId = CommandHelpers.ParseRequiredGuid(context, InTargetId);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);
            var reason = CommandHelpers.GetRequiredString(context, InReason);
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);

            var status = ParseOptionalInt(context, InStatus);
            var priority = ParseOptionalInt(context, InPriority);
            var routeId = ParseOptionalGuid(context, InRouteId);
            var dueDate = ParseOptionalDate(context, InDueDate);
            var fields = ParseFields(context);

            // Idempotency: a replay with the same key returns the original result (NFR-REL-01).
            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey);
            if (existingAudit != null)
            {
                SetResponse(context, targetId.ToString("D"), existingAudit.Id, false);
                return;
            }

            // Application RBAC gate (AD-041): manager-level edit on the case worklist.
            PermissionHelpers.EnsureAppPermission(systemService, context, "page.cases", PermissionHelpers.AccessEdit);

            // Capture the before-values of every attribute we may touch, so the Audit Event
            // records a true before/after and the caller's read privilege gates the command.
            var before = userService.Retrieve(CaseEntity, targetId, BuildBeforeColumnSet());

            var update = new Entity(CaseEntity, targetId);
            var changes = new List<string>();

            // Legacy scalar parameters, retained so existing callers keep working.
            if (status.HasValue)
            {
                update[StatusAttr] = new OptionSetValue(status.Value);
                changes.Add("Status " + Describe(before.GetAttributeValue<OptionSetValue>(StatusAttr)) + " -> " + status.Value);
            }

            if (routeId.HasValue)
            {
                update[RouteAttr] = new EntityReference(RouteEntity, routeId.Value);
                changes.Add("Route " + Describe(before.GetAttributeValue<EntityReference>(RouteAttr)) + " -> " + routeId.Value.ToString("D"));
            }

            if (priority.HasValue)
            {
                update[PriorityAttr] = new OptionSetValue(priority.Value);
                changes.Add("Priority " + Describe(before.GetAttributeValue<OptionSetValue>(PriorityAttr)) + " -> " + priority.Value);
            }

            if (dueDate.HasValue)
            {
                update[DueDateAttr] = dueDate.Value;
                changes.Add("Due date " + Describe(before.GetAttributeValue<DateTime?>(DueDateAttr)) + " -> " + dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            // General editable case fields, addressed by logical name via the Fields payload.
            ApplyFields(fields, before, update, changes);

            if (changes.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Provide at least one detail to change.");
            }

            if (string.IsNullOrEmpty(expectedRowVersion))
            {
                userService.Update(update);
            }
            else
            {
                update.RowVersion = expectedRowVersion;
                try
                {
                    userService.Execute(new Microsoft.Xrm.Sdk.Messages.UpdateRequest
                    {
                        Target = update,
                        ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
                    });
                }
                catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
                {
                    if (CommandHelpers.IsConcurrencyFault(fault))
                    {
                        throw new InvalidPluginExecutionException(
                            CommandHelpers.ConflictPrefix + "This case changed since you loaded it. Reload and try again.");
                    }

                    throw;
                }
            }

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService,
                CommandUpdateCaseDetails,
                "UpdateCaseDetails " + targetId.ToString("D"),
                CaseEntity,
                targetId,
                reason,
                string.Join("; ", changes),
                idempotencyKey,
                context);

            SetResponse(context, targetId.ToString("D"), auditId, false);
        }

        private static int? ParseOptionalInt(IPluginExecutionContext context, string name)
        {
            var raw = CommandHelpers.GetOptionalString(context, name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            int value;
            if (!int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + name + " must be a whole number.");
            }

            return value;
        }

        private static Guid? ParseOptionalGuid(IPluginExecutionContext context, string name)
        {
            var raw = CommandHelpers.GetOptionalString(context, name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            Guid value;
            if (!Guid.TryParse(raw.Trim(), out value) || value == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + name + " must be a valid record id.");
            }

            return value;
        }

        private static DateTime? ParseOptionalDate(IPluginExecutionContext context, string name)
        {
            var raw = CommandHelpers.GetOptionalString(context, name);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            DateTime value;
            if (!DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + name + " must be a valid date (yyyy-MM-dd).");
            }

            return value.Date;
        }

        private static string Describe(OptionSetValue value)
        {
            return value == null ? "(none)" : value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Describe(EntityReference value)
        {
            return value == null ? "(none)" : value.Id.ToString("D");
        }

        private static string Describe(DateTime? value)
        {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "(none)";
        }

        private static void SetResponse(IPluginExecutionContext context, string caseId, Guid auditEventId, bool conflict)
        {
            context.OutputParameters[OutCaseId] = caseId;
            context.OutputParameters[OutAuditEventId] = auditEventId.ToString("D");
            context.OutputParameters[OutConflict] = conflict;
        }

        private enum EditableKind
        {
            Text,
            Option,
            DateOnly,
        }

        private sealed class EditableField
        {
            public EditableField(EditableKind kind, string label)
            {
                Kind = kind;
                Label = label;
            }

            public EditableKind Kind { get; }

            public string Label { get; }
        }

        // Allowlist of case attributes a manager may edit via the Fields payload, keyed by
        // logical name. Anything not listed is rejected, so the command can never write an
        // attribute it was not designed to (and route/status keep their audited semantics).
        private static readonly Dictionary<string, EditableField> Editables =
            new Dictionary<string, EditableField>(StringComparer.OrdinalIgnoreCase)
            {
                { "al_clientname", new EditableField(EditableKind.Text, "Client name") },
                { "al_advisername", new EditableField(EditableKind.Text, "Adviser") },
                { "al_advisercode", new EditableField(EditableKind.Text, "Adviser code") },
                { "al_adviserstatus", new EditableField(EditableKind.Option, "Adviser status") },
                { "al_paraplanner", new EditableField(EditableKind.Text, "Paraplanner") },
                { "al_paraplannercode", new EditableField(EditableKind.Text, "Paraplanner code") },
                { "al_products", new EditableField(EditableKind.Text, "Products") },
                { "al_casetype", new EditableField(EditableKind.Option, "Case type") },
                { "al_advicedate", new EditableField(EditableKind.DateOnly, "Advice date") },
                { "al_productsolutiontype", new EditableField(EditableKind.Option, "Product/solution type") },
                { "al_samplesource", new EditableField(EditableKind.Option, "Sample source") },
                { "al_checkername", new EditableField(EditableKind.Text, "Checker") },
                { "al_checkdate", new EditableField(EditableKind.DateOnly, "Check date") },
                { "al_preorpostcheck", new EditableField(EditableKind.Option, "Pre or post check") },
                { "al_vulnerableclient", new EditableField(EditableKind.Option, "Vulnerable client") },
                { "al_taxcheckrequired", new EditableField(EditableKind.Option, "Tax check required") },
                { "al_taxteamdisposition", new EditableField(EditableKind.Option, "Tax team disposition") },
                { "al_casestatus", new EditableField(EditableKind.Option, "Status") },
                { "al_priority", new EditableField(EditableKind.Option, "Priority") },
                { "al_duedate", new EditableField(EditableKind.DateOnly, "Due date") },
            };

        private static ColumnSet BuildBeforeColumnSet()
        {
            var columns = new List<string> { StatusAttr, RouteAttr, PriorityAttr, DueDateAttr };
            foreach (var key in Editables.Keys)
            {
                if (!columns.Contains(key))
                {
                    columns.Add(key);
                }
            }

            return new ColumnSet(columns.ToArray());
        }

        private static Dictionary<string, string> ParseFields(IPluginExecutionContext context)
        {
            var raw = CommandHelpers.GetOptionalString(context, InFields);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return SimpleJson.ParseObject(raw);
            }
            catch (FormatException ex)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "The Fields payload is not valid JSON: " + ex.Message);
            }
        }

        private static void ApplyFields(
            Dictionary<string, string> fields,
            Entity before,
            Entity update,
            List<string> changes)
        {
            foreach (var pair in fields)
            {
                var attr = pair.Key;
                EditableField def;
                if (!Editables.TryGetValue(attr, out def))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.ValidationPrefix + "Field '" + attr + "' cannot be edited.");
                }

                var value = pair.Value == null ? string.Empty : pair.Value.Trim();

                switch (def.Kind)
                {
                    case EditableKind.Text:
                        {
                            var old = before.GetAttributeValue<string>(attr);
                            update[attr] = value.Length == 0 ? null : value;
                            changes.Add(def.Label + " '" + (old ?? "(none)") + "' -> '" + value + "'");
                            break;
                        }

                    case EditableKind.Option:
                        {
                            if (value.Length == 0)
                            {
                                update[attr] = null;
                                changes.Add(def.Label + " " + Describe(before.GetAttributeValue<OptionSetValue>(attr)) + " -> (none)");
                                break;
                            }

                            int option;
                            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out option))
                            {
                                throw new InvalidPluginExecutionException(
                                    CommandHelpers.ValidationPrefix + def.Label + " must be a whole number option value.");
                            }

                            update[attr] = new OptionSetValue(option);
                            changes.Add(def.Label + " " + Describe(before.GetAttributeValue<OptionSetValue>(attr)) + " -> " + option);
                            break;
                        }

                    case EditableKind.DateOnly:
                        {
                            if (value.Length == 0)
                            {
                                update[attr] = null;
                                changes.Add(def.Label + " " + Describe(before.GetAttributeValue<DateTime?>(attr)) + " -> (none)");
                                break;
                            }

                            DateTime parsed;
                            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                            {
                                throw new InvalidPluginExecutionException(
                                    CommandHelpers.ValidationPrefix + def.Label + " must be a valid date (yyyy-MM-dd).");
                            }

                            update[attr] = parsed.Date;
                            changes.Add(def.Label + " " + Describe(before.GetAttributeValue<DateTime?>(attr)) + " -> " + parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                            break;
                        }
                }
            }
        }

        // Minimal reader for a flat JSON object of string values, used because the plugin
        // sandbox (net462) carries no JSON dependency. Values are read as strings; unquoted
        // numbers, booleans and null are accepted and returned as their literal text.
        private static class SimpleJson
        {
            public static Dictionary<string, string> ParseObject(string text)
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var i = 0;
                SkipWhitespace(text, ref i);
                Expect(text, ref i, '{');
                SkipWhitespace(text, ref i);
                if (Peek(text, i) == '}')
                {
                    return result;
                }

                while (true)
                {
                    SkipWhitespace(text, ref i);
                    var key = ParseString(text, ref i);
                    SkipWhitespace(text, ref i);
                    Expect(text, ref i, ':');
                    SkipWhitespace(text, ref i);
                    result[key] = ParseValue(text, ref i);
                    SkipWhitespace(text, ref i);
                    var separator = Next(text, ref i);
                    if (separator == ',')
                    {
                        continue;
                    }

                    if (separator == '}')
                    {
                        break;
                    }

                    throw new FormatException("Expected ',' or '}'.");
                }

                return result;
            }

            private static string ParseValue(string text, ref int i)
            {
                var c = Peek(text, i);
                if (c == '"')
                {
                    return ParseString(text, ref i);
                }

                var start = i;
                while (i < text.Length && text[i] != ',' && text[i] != '}')
                {
                    i++;
                }

                var token = text.Substring(start, i - start).Trim();
                return token.Equals("null", StringComparison.OrdinalIgnoreCase) ? string.Empty : token;
            }

            private static string ParseString(string text, ref int i)
            {
                Expect(text, ref i, '"');
                var builder = new System.Text.StringBuilder();
                while (i < text.Length)
                {
                    var c = text[i++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c == '\\')
                    {
                        if (i >= text.Length)
                        {
                            break;
                        }

                        var escape = text[i++];
                        switch (escape)
                        {
                            case '"': builder.Append('"'); break;
                            case '\\': builder.Append('\\'); break;
                            case '/': builder.Append('/'); break;
                            case 'b': builder.Append('\b'); break;
                            case 'f': builder.Append('\f'); break;
                            case 'n': builder.Append('\n'); break;
                            case 'r': builder.Append('\r'); break;
                            case 't': builder.Append('\t'); break;
                            case 'u':
                                if (i + 4 > text.Length)
                                {
                                    throw new FormatException("Invalid unicode escape.");
                                }

                                builder.Append((char)int.Parse(text.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                i += 4;
                                break;
                            default:
                                throw new FormatException("Invalid escape sequence '\\" + escape + "'.");
                        }

                        continue;
                    }

                    builder.Append(c);
                }

                throw new FormatException("Unterminated string.");
            }

            private static void SkipWhitespace(string text, ref int i)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
            }

            private static char Peek(string text, int i)
            {
                return i < text.Length ? text[i] : '\0';
            }

            private static char Next(string text, ref int i)
            {
                return i < text.Length ? text[i++] : '\0';
            }

            private static void Expect(string text, ref int i, char expected)
            {
                if (i >= text.Length || text[i] != expected)
                {
                    throw new FormatException("Expected '" + expected + "'.");
                }

                i++;
            }
        }
    }
}
