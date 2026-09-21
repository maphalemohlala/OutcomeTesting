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
        private const string TaxRequiredAttr = "al_taxcheckrequired";
        private const string RouteCodeAttr = "al_routecode";

        private const int TaxCheckRequiredYes = 120910560;
        private const int TaxCheckRequiredNo = 120910561;
        private const string RouteCodeTaxThenAqs = "ROUTE-TAX-AQS";
        private const string RouteCodeAqsOnly = "ROUTE-AQS";
        private const string RouteCodeTaxOnly = "ROUTE-TAX";
        private const string DispositionAttr = "al_taxteamdisposition";
        private const int DispositionReturnToParaplanner = 120910571;

        public const int CommandUpdateCaseDetails = 120910778;

        /// <summary>
        /// The capability a caller needs, on top of page.cases Edit, to move a case's due
        /// date (item 6, 2026-09-19: "3 days, only editable by managers in codeapps").
        ///
        /// Its own key rather than a higher level on page.cases, because page.cases Edit is
        /// what lets a Tax or AQS reviewer fill in the header fields the extract does not
        /// carry (project owner, 2026-09-12) - four roles hold it in DEV, two of them
        /// checkers. Raising the bar on page.cases would have taken the header away from the
        /// people who are meant to complete it; a separate key moves one field instead.
        /// </summary>
        public const string DueDateResource = "case.duedate";

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
            // Optional, by project owner direction 2026-09-08. It was mandatory on both sides,
            // which made an amendment cost a sentence of justification every time and taught
            // people to type "update" to get past it - a field that is always filled and never
            // read is not an audit trail. What BR-012 actually needs is what changed, and the
            // audit event's details line carries that field by field, computed here rather than
            // supplied by the caller. A reason still travels when one is given; when it is not,
            // the event says so plainly instead of holding an empty string that reads as though
            // the column failed to save.
            var reason = CommandHelpers.GetOptionalString(context, InReason);
            if (string.IsNullOrWhiteSpace(reason))
            {
                reason = "(no reason given)";
            }
            var expectedRowVersion = CommandHelpers.GetOptionalString(context, InExpectedRowVersion);

            var status = ParseOptionalInt(context, InStatus);
            var priority = ParseOptionalInt(context, InPriority);
            var routeId = ParseOptionalGuid(context, InRouteId);
            var dueDate = ParseOptionalDate(context, InDueDate);
            var fields = ParseFields(context);

            // The legacy DueDate scalar is folded into the Fields payload rather than
            // applied beside it (item 6, 2026-09-19). al_duedate is an editable field now,
            // so two parameters wrote the same column: two audit lines worded differently,
            // two places to gate, and a due date the date-of-meeting comparison could not
            // see because EffectiveDueDate reads the payload. One door instead.
            //
            // An explicit Fields entry wins over the scalar. A caller that sends both has
            // said the same thing twice, and the newer parameter is the one the app uses.
            if (dueDate.HasValue && !fields.ContainsKey(DueDateAttr))
            {
                fields[DueDateAttr] = dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            // Idempotency: a replay with the same key returns the original result (NFR-REL-01).
            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandUpdateCaseDetails);
            if (existingAudit != null)
            {
                SetResponse(context, targetId.ToString("D"), existingAudit.Id, false);
                return;
            }

            // Application RBAC gate (AD-041): manager-level edit on the case worklist.
            PermissionHelpers.EnsureAppPermission(systemService, context, "page.cases", PermissionHelpers.AccessEdit);

            // A second gate for one field. The due date is derived at import and is a
            // deadline the business manages, not a detail of the check, so moving it is a
            // manager's act even though everything beside it on the same form is not.
            // Checked before the record is read so a caller who may not move it is told
            // that, rather than being told the row version is stale.
            if (TouchesDueDate(fields))
            {
                PermissionHelpers.EnsureAppPermission(
                    systemService, context, DueDateResource, PermissionHelpers.AccessEdit);
            }

            // Capture the before-values of every attribute we may touch, so the Audit Event
            // records a true before/after and the caller's read privilege gates the command.
            var before = CommandHelpers.RetrieveOrNotFound(
                userService, CaseEntity, targetId, BuildBeforeColumnSet(),
                "That case no longer exists. Refresh and try again.");

            var update = new Entity(CaseEntity, targetId);
            var changes = new List<string>();

            // The change lines below are what a person reads on the case history screen
            // (FR-033), so choices are named rather than numbered. Resolved against the
            // system service, and once per column — see OptionLabels.
            var labels = new OptionLabels(systemService);

            // Legacy scalar parameters, retained so existing callers keep working.
            if (status.HasValue)
            {
                update[StatusAttr] = new OptionSetValue(status.Value);
                changes.Add("Status " + labels.Describe(CaseEntity, StatusAttr, before.GetAttributeValue<OptionSetValue>(StatusAttr))
                    + " -> " + labels.Label(CaseEntity, StatusAttr, status.Value));
            }

            if (routeId.HasValue)
            {
                update[RouteAttr] = new EntityReference(RouteEntity, routeId.Value);
                changes.Add("Route " + Describe(before.GetAttributeValue<EntityReference>(RouteAttr))
                    + " -> " + RouteName(systemService, routeId.Value));
            }

            if (priority.HasValue)
            {
                EnsureOption(labels, PriorityAttr, "Priority", priority.Value);
                update[PriorityAttr] = new OptionSetValue(priority.Value);
                changes.Add("Priority " + labels.Describe(CaseEntity, PriorityAttr, before.GetAttributeValue<OptionSetValue>(PriorityAttr))
                    + " -> " + labels.Label(CaseEntity, PriorityAttr, priority.Value));
            }

            // General editable case fields, addressed by logical name via the Fields payload.
            // The system service, as the labels above already use: validating the chosen option
            // is reading the reference catalogue, not reading the caller's data, and it must
            // answer the same way whoever is asking.
            ApplyFields(systemService, fields, before, update, changes, labels);

            // One lifecycle check for both status paths — the legacy Status parameter and
            // the Fields payload both land on the same attribute, so validating after they
            // have been applied is what stops either route writing an impossible status.
            EnsureLifecycleTransition(before, update);

            if (changes.Count == 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix + "Provide at least one detail to change.");
            }

            // BR-004: an explicit RouteId is the AD-036/OD-008 reassignment and always wins;
            // otherwise the route follows the Tax check required answer.
            if (!routeId.HasValue)
            {
                DeriveRoute(systemService, before, update, changes);
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

            // AD-093: a routed case still short of the queue is queued by the save. Runs after
            // the update so the walk starts from the status the caller's own change left.
            QueueAfterEdit(userService, before, update, changes);

            // OD-048: a case already past the queue whose route now owes a check nobody has
            // opened goes back to it. Runs after QueueAfterEdit and is exclusive of it - that
            // one moves a case forward INTO the queue from Imported or Ready for Allocation,
            // this one returns a case to it from Assigned or Review In Progress, so no status
            // satisfies both. The system service, because releasing the prior assignment and
            // moving the case are the command's consequences rather than the caller's edit.
            RequeueAfterRouteChange(systemService, before, update, changes);

            // The adviser named on the case is how remediation is routed (BR-006), so a change
            // to that name takes the case's open actions with it — whether they were raised
            // unassigned because the old name matched no contact, or assigned to the adviser
            // who has just been replaced. Completed actions keep the name of whoever did the
            // work, and an action already held by this adviser is not rewritten.
            if (update.Contains("al_advisername"))
            {
                var assigned = Remediation.AssignOpenActions(
                    systemService, new EntityReference(CaseEntity, targetId), context.CorrelationId);
                if (assigned > 0)
                {
                    changes.Add("Assigned " + assigned + " open remediation action(s) to the adviser now named");
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

        /// <summary>
        /// BR-004 route derivation: Tax check required Yes routes the case Tax then AQS, No
        /// routes it AQS only. Tax team disposition is not an input - it advances or ends the
        /// route rather than choosing it, and Tax only is only reachable through the AD-036
        /// wrong-route reassignment (an explicit RouteId). The route is recalculated only when
        /// the tax answer changes or the case has no route yet, so a deliberate reassignment
        /// is never silently reverted by an unrelated edit.
        ///
        /// Route codes are matched against <c>data/route-seed</c>, so the route rows must be
        /// seeded before this command can set a route.
        /// </summary>
        public static void DeriveRoute(IOrganizationService service, Entity before, Entity update, List<string> changes)
        {
            DeriveRoute(before, update, changes, code => FindRouteByCode(service, code));
        }

        /// <summary>
        /// The same BR-004 derivation as the <see cref="IOrganizationService"/> overload, but
        /// taking the route lookup as a resolver rather than a service. ImportCasesPlugin
        /// builds one memoising resolver before its per-row loop (Important 1): an uncached
        /// RetrieveMultiple per row is up to 1000 round trips at 1000 rows, inside the
        /// two-minute plug-in budget the file's own comment exists to protect.
        /// </summary>
        public static void DeriveRoute(Entity before, Entity update, List<string> changes, Func<string, Guid?> findRoute)
        {
            var beforeTax = before.GetAttributeValue<OptionSetValue>(TaxRequiredAttr);
            var afterTax = update.Contains(TaxRequiredAttr)
                ? update.GetAttributeValue<OptionSetValue>(TaxRequiredAttr)
                : beforeTax;

            // The Tax team's disposition decides whether the case goes on to AQS at all.
            // "Return to paraplanner" means the Tax check is the whole of it and no AQS
            // check is owed (project owner, 2026-09-10), so it overrides what the tax-check
            // answer would otherwise derive - that answer says a Tax check was needed, not
            // where the case goes afterwards.
            var beforeDisposition = before.GetAttributeValue<OptionSetValue>(DispositionAttr);
            var afterDisposition = update.Contains(DispositionAttr)
                ? update.GetAttributeValue<OptionSetValue>(DispositionAttr)
                : beforeDisposition;
            var returnedToParaplanner =
                afterDisposition != null && afterDisposition.Value == DispositionReturnToParaplanner;

            if (afterTax == null && !returnedToParaplanner)
            {
                return;
            }

            var currentRoute = before.GetAttributeValue<EntityReference>(RouteAttr);
            var taxChanged = afterTax != null && (beforeTax == null || beforeTax.Value != afterTax.Value);
            var dispositionChanged = (beforeDisposition == null) != (afterDisposition == null)
                || (beforeDisposition != null && afterDisposition != null
                    && beforeDisposition.Value != afterDisposition.Value);

            if (!taxChanged && !dispositionChanged && currentRoute != null)
            {
                return;
            }

            string code;
            if (returnedToParaplanner)
            {
                code = RouteCodeTaxOnly;
            }
            else
            {
                switch (afterTax.Value)
                {
                    case TaxCheckRequiredYes:
                        code = RouteCodeTaxThenAqs;
                        break;
                    case TaxCheckRequiredNo:
                        code = RouteCodeAqsOnly;
                        break;
                    default:
                        return;
                }
            }

            var routeId = findRoute(code);
            if (!routeId.HasValue)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Review route '" + code + "' is not configured.");
            }

            if (currentRoute != null && currentRoute.Id == routeId.Value)
            {
                return;
            }

            update[RouteAttr] = new EntityReference(RouteEntity, routeId.Value);
            changes.Add("Route " + Describe(currentRoute) + " -> " + code + " (derived BR-004)");
        }

        /// <summary>
        /// The route matching a route code, or null when it is not configured. Public so
        /// ImportCasesPlugin can build its own memoising resolver over it (Important 1)
        /// rather than paying an uncached RetrieveMultiple per imported row.
        /// </summary>
        public static Guid? FindRouteByCode(IOrganizationService service, string code)
        {
            var query = new QueryExpression(RouteEntity)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
            };
            query.Criteria.AddCondition(RouteCodeAttr, ConditionOperator.Equal, code);

            var result = service.RetrieveMultiple(query);
            return result.Entities.Count > 0 ? result.Entities[0].Id : (Guid?)null;
        }

        /// <summary>
        /// A lookup as its name. Retrieve populates EntityReference.Name from the primary
        /// name column, so the route a case was on can be named without a second read; the
        /// id remains the fallback for a reference that arrived without one.
        /// </summary>
        private static string Describe(EntityReference value)
        {
            if (value == null)
            {
                return "(none)";
            }

            return string.IsNullOrWhiteSpace(value.Name) ? value.Id.ToString("D") : value.Name;
        }

        /// <summary>
        /// The route code behind an id the caller supplied. The explicit RouteId path is the
        /// AD-036/OD-008 reassignment, where the caller sends a bare guid and nothing on the
        /// case names it yet — so the history line has to go and look it up or print a guid.
        /// </summary>
        private static string RouteName(IOrganizationService service, Guid routeId)
        {
            var route = service.Retrieve(RouteEntity, routeId, new ColumnSet(RouteCodeAttr, "al_name"));
            var code = route.GetAttributeValue<string>(RouteCodeAttr);
            if (!string.IsNullOrWhiteSpace(code))
            {
                return code;
            }

            var name = route.GetAttributeValue<string>("al_name");
            return string.IsNullOrWhiteSpace(name) ? routeId.ToString("D") : name;
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

            /// <summary>
            /// A lookup to an al_listoption row: a dropdown whose choices are data the
            /// checking team maintains, not choice metadata a developer deploys.
            /// </summary>
            ListOption,
        }

        private sealed class EditableField
        {
            public EditableField(EditableKind kind, string label, int listValue = 0)
            {
                Kind = kind;
                Label = label;
                ListValue = listValue;
            }

            public EditableKind Kind { get; }

            public string Label { get; }

            /// <summary>
            /// For <see cref="EditableKind.ListOption"/>, the al_listoption.al_list value this
            /// field accepts. Every list's options share one table, so without it a sample
            /// source could be written into the product type and read back as an ordinary
            /// value.
            /// </summary>
            public int ListValue { get; }
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
                // Display name only (item 9, 2026-09-19). The schema name is unchanged, so
                // every reader of the column is untouched; this is the wording the audit
                // trail and the refusals use, and it comes from CaseHeaderRules so the
                // portal's own header edit words it identically.
                { "al_advicedate", new EditableField(EditableKind.DateOnly, CaseHeaderRules.AdviceDateLabel) },
                // The choice column stays editable while cases exist that hold only it. The
                // lookup below is what a current edit writes; this is the way back for a row
                // the backfill could not resolve.
                { "al_productsolutiontype", new EditableField(EditableKind.Option, "Product/solution type") },
                {
                    ListOptionRules.ProductTypeAttribute,
                    new EditableField(
                        EditableKind.ListOption,
                        "Product/solution type",
                        ListOptionRules.ProductSolutionType)
                },
                { "al_samplesource", new EditableField(EditableKind.Option, "Sample source") },
                // al_checkername is deliberately absent (item 2, 2026-09-19). The case header
                // now carries a Tax Checker and an AQS Checker, and each one REFLECTS the
                // checker assigned to that review - so neither is free text a manager types.
                // Typing a name here was already mistaken for allocating the check once
                // (2026-09-09); the two derived columns make that mistake unavailable rather
                // than merely discouraged. Allocation is al_AssignCase, and a portal claim.
                // al_checkdate is deliberately absent (project owner, 2026-09-21: "the check
                // date has to be uneditable as it is automatically updated on submit"). It is
                // now derived - SubmitReviewPlugin.StampCheckDate writes the day of the
                // submit - and a column a command may overwrite on the next submit is not one
                // a manager can usefully be offered. Absent from this map is what refuses it:
                // anything not listed here is rejected by name.
                { "al_preorpostcheck", new EditableField(EditableKind.Option, "Pre or post check") },
                { "al_vulnerableclient", new EditableField(EditableKind.Option, "Vulnerable client") },
                { "al_taxcheckrequired", new EditableField(EditableKind.Option, "Tax check required") },
                { "al_taxteamdisposition", new EditableField(EditableKind.Option, "Tax team disposition") },
                { "al_casestatus", new EditableField(EditableKind.Option, "Status") },
                { "al_priority", new EditableField(EditableKind.Option, "Priority") },

                // al_duedate is editable, and its own capability decides by whom (item 6,
                // 2026-09-19: "3 days, only editable by managers in codeapps"). See
                // DueDateResource and TouchesDueDate.
                //
                // This SUPERSEDES the earlier direction on the same day - "due date should
                // not be edited and should always be set to 72 hours after the upload" -
                // which took the column out of this map altogether. The default is unchanged
                // and still ImportRules.DueDateFor; what changed is that a manager may move
                // a deadline afterwards. Recorded rather than quietly replaced because the
                // two instructions are a day apart and the narrower one came second.
                //
                // Being in this map is what makes EffectiveDueDate work: it reads the
                // payload for the due date this save leaves behind, and until now the only
                // payload that could carry one was a payload about to be refused.
                { "al_duedate", new EditableField(EditableKind.DateOnly, CaseHeaderRules.DueDateLabel) },
            };

        /// <summary>
        /// Whether this save moves the due date, and so needs the manager's grant.
        ///
        /// Public and pure so the rule can be tested without a permission gate behind it.
        /// Reads the payload AFTER the legacy scalar has been folded in, which is why it
        /// takes one argument and not two.
        ///
        /// Presence, not change: a payload naming al_duedate asks to write it, and a caller
        /// who may not move the deadline may not write today's value back either - that
        /// would be an edit whose audit line says nothing happened.
        /// </summary>
        public static bool TouchesDueDate(Dictionary<string, string> fields)
        {
            return fields != null && fields.ContainsKey(DueDateAttr);
        }

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

        /// <summary>
        /// Refuses a status change the lifecycle does not describe (FR-010 range).
        ///
        /// al_casestatus was previously written straight from caller input, so a case could
        /// jump from Imported to Closed — skipping validation, allocation, review and
        /// remediation — and then be collected by the export, which filters on Closed, and
        /// delivered with no review instance and a blank grade.
        /// </summary>
        /// <summary>
        /// Refuses a choice value the option set does not hold.
        ///
        /// Without this the value reached Dataverse, which threw, and the caller got
        /// "UNEXPECTED: OutcomeTesting.Plugins.UpdateCaseDetailsPlugin could not complete.
        /// OrganizationServiceFault: ... is outside the valid range" - the plug-in class and
        /// the internal table, where every other refusal on this command is a sentence
        /// (F23, the same NFR-OBS-01 failure as F1).
        ///
        /// The accepted labels are named because the caller cannot otherwise know them: the
        /// numbers are not guessable and the refusal is the only place they appear.
        /// </summary>
        private static void EnsureOption(OptionLabels labels, string attribute, string label, int value)
        {
            if (labels.IsMember(CaseEntity, attribute, value))
            {
                return;
            }

            var accepted = labels.AcceptedLabels(CaseEntity, attribute);
            throw new InvalidPluginExecutionException(
                CommandHelpers.ValidationPrefix + label + " is not one of the values this field accepts"
                + (accepted == null ? "." : ": " + accepted + "."));
        }

        private static void EnsureLifecycleTransition(Entity before, Entity update)
        {
            if (!update.Contains(StatusAttr))
            {
                return;
            }

            var target = update[StatusAttr] as OptionSetValue;
            if (target == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.ValidationPrefix +
                    "A case must have a status. Move it to the next status in the lifecycle rather than clearing it.");
            }

            var current = before.GetAttributeValue<OptionSetValue>(StatusAttr);
            int? from = current != null ? current.Value : (int?)null;

            CaseTransitions.EnsureAllowed(from, target.Value);
        }

        /// <summary>
        /// AD-093 on the case-edit command. The rule reads the state the save leaves behind:
        /// the route after this call (set explicitly, derived by DeriveRoute, or already on
        /// the case) and the case's current status, read fresh (Minor 3) rather than from
        /// <paramref name="before"/>, which was retrieved before the update ran; a case that
        /// moved past the queue in the meantime must not have this update's hop refused
        /// against a status it no longer holds. A status the caller set in the same call is
        /// theirs: EnsureLifecycleTransition has already checked it, and a second move on top
        /// of a deliberate one would make the history read as two decisions.
        /// </summary>
        public static bool QueueAfterEdit(IOrganizationService service, Entity before, Entity update, List<string> changes)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            if (update.Contains(StatusAttr))
            {
                return false;
            }

            var routeAfter = update.Contains(RouteAttr)
                ? update.GetAttributeValue<EntityReference>(RouteAttr)
                : before.GetAttributeValue<EntityReference>(RouteAttr);
            var status = CaseTransitions.CurrentStatus(service, before.Id);

            return CaseQueueing.QueueIfRouted(
                service,
                before.Id,
                status,
                routeAfter != null,
                changes);
        }

        /// <summary>
        /// OD-048: an edit that changes the route can leave a case owing a check nobody can
        /// pick up, so the case goes back to the shared queue for it.
        ///
        /// A route is re-derived from the tax-check answer and the Tax team's disposition
        /// (<see cref="DeriveRoute(Entity, Entity, List{string}, Func{string, Guid?})"/>),
        /// and the review instances already opened are not reconciled with it. An AQS-only
        /// case that has been claimed carries an open AQS instance; edited to require a Tax
        /// check, it now owes Tax first (BR-004) — but it sits at Assigned, so it is out of
        /// the queue and cannot be claimed, and the AQS submit it still offers is refused by
        /// <c>SubmitReviewPlugin.EnsureTaxPrecedesAqs</c> until the Tax check is in. The case
        /// could not progress by any route at all. AD-112 fixed the claim's half of that;
        /// this is the half that gets the case back within reach of a claim.
        ///
        /// Deliberately narrow. It runs only when this edit actually changed the route
        /// (<see cref="DeriveRoute(Entity, Entity, List{string}, Func{string, Guid?})"/>
        /// writes the column only then), only from Assigned or Review In Progress, and only
        /// when the discipline now due has <em>no open instance</em> — so a route change that
        /// did not change what comes next leaves the checker holding their work. A status the
        /// caller set in the same call is theirs, as it is for
        /// <see cref="QueueAfterEdit"/>: a second move on top of a deliberate one would make
        /// the history read as two decisions.
        ///
        /// The active assignment is released with the move, because a case in the queue
        /// carrying a live assignment is the same inconsistency the Tax-to-AQS handoff
        /// already avoids (AD-076). The row itself is preserved, never deleted (AD-037), so
        /// the allocation trail survives (BR-003, BR-012).
        /// </summary>
        public static bool RequeueAfterRouteChange(
            IOrganizationService service, Entity before, Entity update, List<string> changes)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            if (!update.Contains(RouteAttr) || update.Contains(StatusAttr))
            {
                return false;
            }

            var status = CaseTransitions.CurrentStatus(service, before.Id);
            if (status != CaseLifecycle.Assigned && status != CaseLifecycle.ReviewInProgress)
            {
                return false;
            }

            var effective = new Entity(CaseEntity, before.Id)
            {
                [RouteAttr] = update.GetAttributeValue<EntityReference>(RouteAttr),
            };

            int due;
            if (!ClaimCasePlugin.TryNextDiscipline(service, effective, out due))
            {
                return false;
            }

            if (ClaimCasePlugin.HasOpenReview(service, before.Id, due))
            {
                return false;
            }

            AssignCasePlugin.ReleasePriorAssignments(service, before.Id);
            CaseTransitions.MoveThrough(service, before.Id, status, new[] { CaseLifecycle.Queued });

            changes.Add(
                "Status " + CaseLifecycle.NameOf(status.Value) + " -> " + CaseLifecycle.NameOf(CaseLifecycle.Queued)
                + " (returned to the queue: the new route owes a "
                + (due == ResponseRules.ReviewTypeTax ? "Tax" : "AQS")
                + " check that has not been opened, OD-048)");

            return true;
        }

        /// <summary>
        /// Coerces each named field onto the update and describes the move for the audit.
        ///
        /// Public because CaseHeaderRequestPlugin applies the same fields from the portal
        /// (item 5, 2026-09-19). One reader of Editables, one set of coercion rules and one
        /// phrasing of "Adviser 'x' -> 'y'", so the two front ends cannot come to disagree
        /// about what a header edit means or how it reads in the audit.
        ///
        /// It does NOT decide who may edit what. Editables says which fields are editable at
        /// all; the caller decides which of those THIS caller may touch, which is how the
        /// portal offers a checker a narrower set than an administrator gets here.
        /// </summary>
        public static void ApplyFields(
            IOrganizationService service,
            Dictionary<string, string> fields,
            Entity before,
            Entity update,
            List<string> changes,
            OptionLabels labels)
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
                                changes.Add(def.Label + " " + labels.Describe(CaseEntity, attr, before.GetAttributeValue<OptionSetValue>(attr)) + " -> (none)");
                                break;
                            }

                            int option;
                            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out option))
                            {
                                throw new InvalidPluginExecutionException(
                                    CommandHelpers.ValidationPrefix + def.Label + " must be a whole number option value.");
                            }

                            EnsureOption(labels, attr, def.Label, option);
                            update[attr] = new OptionSetValue(option);
                            changes.Add(def.Label + " " + labels.Describe(CaseEntity, attr, before.GetAttributeValue<OptionSetValue>(attr))
                                + " -> " + labels.Label(CaseEntity, attr, option));
                            break;
                        }

                    case EditableKind.ListOption:
                        {
                            var was = before.GetAttributeValue<EntityReference>(attr);
                            var wasName = was == null || string.IsNullOrWhiteSpace(was.Name) ? "(none)" : was.Name;

                            if (value.Length == 0)
                            {
                                update[attr] = null;
                                changes.Add(def.Label + " '" + wasName + "' -> (none)");
                                break;
                            }

                            Guid optionId;
                            if (!Guid.TryParse(value, out optionId))
                            {
                                throw new InvalidPluginExecutionException(
                                    CommandHelpers.ValidationPrefix + def.Label + " is not an option on that list.");
                            }

                            // The row must exist, belong to THIS list, and be offered today.
                            // Checked here rather than trusted from the caller: a payload sent
                            // by hand never goes near the management page.
                            var option = ListOptionRules.Resolve(
                                service, optionId, def.ListValue, def.Label, DateTime.UtcNow.Date);

                            update[attr] = new EntityReference(ListOptionRules.Entity, optionId);
                            changes.Add(
                                def.Label + " '" + wasName + "' -> '"
                                + option.GetAttributeValue<string>(ListOptionRules.NameAttribute) + "'");
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

                            // A meeting that has not happened yet cannot have been checked
                            // (item 9, 2026-09-19). Compared on the UK day, not UTC's, and
                            // refused here as well as in the page so a payload sent by hand
                            // cannot get round it.
                            if (string.Equals(attr, "al_advicedate", StringComparison.OrdinalIgnoreCase))
                            {
                                var refusal = CaseHeaderRules.ValidateAdviceDate(
                                    parsed.Date, DateTime.UtcNow, EffectiveDueDate(fields, before));
                                if (refusal != null)
                                {
                                    throw new InvalidPluginExecutionException(
                                        CommandHelpers.ValidationPrefix + refusal);
                                }
                            }

                            // The same invariant from the other end (item 6, 2026-09-19).
                            // Moving the deadline under a meeting that already happened
                            // after it would break item 8's rule without ever touching the
                            // date item 8 guards, and a save that changes only the due date
                            // never reaches the branch above.
                            if (string.Equals(attr, DueDateAttr, StringComparison.OrdinalIgnoreCase))
                            {
                                var refusal = CaseHeaderRules.ValidateDueDate(
                                    parsed.Date, EffectiveAdviceDate(fields, before));
                                if (refusal != null)
                                {
                                    throw new InvalidPluginExecutionException(
                                        CommandHelpers.ValidationPrefix + refusal);
                                }
                            }

                            update[attr] = parsed.Date;
                            changes.Add(def.Label + " " + Describe(before.GetAttributeValue<DateTime?>(attr)) + " -> " + parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                            break;
                        }
                }
            }
        }

        /// <summary>
        /// The due date this save leaves behind: the one being written where the payload
        /// carries it, otherwise the one the case already holds.
        ///
        /// al_duedate is not in the editable set today - it is derived at import and locked -
        /// so in practice this reads the before-image. It is written this way so the rule
        /// stays right on the day a manager is allowed to move the deadline, rather than
        /// comparing the new meeting date against a due date the same save is replacing.
        /// </summary>
        private static DateTime? EffectiveDueDate(Dictionary<string, string> fields, Entity before)
        {
            string raw;
            if (fields != null
                && fields.TryGetValue(DueDateAttr, out raw)
                && !string.IsNullOrWhiteSpace(raw))
            {
                DateTime written;
                if (DateTime.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out written))
                {
                    return written.Date;
                }
            }

            return before == null ? null : before.GetAttributeValue<DateTime?>(DueDateAttr);
        }

        /// <summary>
        /// The date of meeting this save leaves behind: the one being written where the
        /// payload carries it, otherwise the one the case already holds.
        ///
        /// EffectiveDueDate's mirror, and needed for the same reason. A save that moves the
        /// due date and the meeting together must compare the two values it is writing, not
        /// one of them against a value it is replacing - whichever of the pair ApplyFields
        /// happens to walk first.
        /// </summary>
        private static DateTime? EffectiveAdviceDate(Dictionary<string, string> fields, Entity before)
        {
            string raw;
            if (fields != null
                && fields.TryGetValue("al_advicedate", out raw)
                && !string.IsNullOrWhiteSpace(raw))
            {
                DateTime written;
                if (DateTime.TryParse(
                        raw,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out written))
                {
                    return written.Date;
                }
            }

            return before == null ? null : before.GetAttributeValue<DateTime?>("al_advicedate");
        }

        // Minimal reader for a flat JSON object of string values, used because the plugin
        // sandbox (net462) carries no JSON dependency. Values are read as strings; unquoted
        // numbers, booleans and null are accepted and returned as their literal text.
        /// <summary>
        /// Public because CaseHeaderRequestPlugin reads the portal's Fields payload with it
        /// (item 5, 2026-09-19), so both front ends parse a header edit with one reader.
        /// </summary>
        public static class SimpleJson
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
