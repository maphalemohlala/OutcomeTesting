using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command GenerateExport (AD-003, AD-039, AD-034 manual only). Registered
    /// against the Custom API <c>al_GenerateExport</c>. One call produces one Trail Light file
    /// (2026-11-25): it creates the batch under the id the app chose, and snapshots exactly the
    /// rows of the file asked for - Daily or Quarterly, cut to the check dates given - with the
    /// values that file reports, so the batch is the file that was delivered (BR-012). Columns
    /// B and D carry CODES (AD-207), resolved from the REGISTRY and never from the case - see
    /// <c>app/src/features/reports/trailLight.ts</c>, which is where the file's column order
    /// lives. Enforces the caller holds Edit on <c>export.generate</c>, is idempotent per
    /// intent, and writes an Audit Event.
    /// </summary>
    public class GenerateExportPlugin : PluginBase
    {
        private const string InBatchId = "BatchId";
        private const string InIdempotencyKey = "IdempotencyKey";
        private const string InFileKind = "FileKind";
        private const string InCheckedFrom = "CheckedFrom";
        private const string InCheckedTo = "CheckedTo";

        /// <summary>The two files a batch can be: <c>FileKind</c> takes one of these.</summary>
        public const string FileDaily = "Daily";
        public const string FileQuarterly = "Quarterly";

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

        /// <summary>
        /// The contact named as carrying a fail, where someone other than the case's own
        /// adviser or paraplanner was chosen (item 8, 2026-09-19). The four flags still say
        /// WHICH of AD-039's two slots they fill; these say whose name goes in it.
        /// </summary>
        public const string FqAccountableContactAttr = "al_fqaccountablecontactid";
        public const string AqAccountableContactAttr = "al_aqaccountablecontactid";

        // The administrator carrying a File Quality fail (requirements log row 10). Read only
        // by IsAccountable - it is never written to an export column.
        public const string FqAdministratorContactAttr = "al_fqadministratorcontactid";

        // Since 2026-11-25 the choice is "an administrator", with no name (project owner).
        // The contact above stays for outcomes recorded before then and still counts.
        public const string FqAdministratorFlag = "al_fqadministratoraccountable";
        private const string TaxOutcomeAttr = "al_taxoutcome";
        private const int CaseStatusClosed = 120910591;
        private const int BatchStatusDraft = 120910770;
        private const int BatchStatusGenerated = 120910771;
        private const int CommandGenerateExport = 120910775;

        // ObjectDoesNotExist - what a Retrieve of a deleted or never-existing row answers
        // with, and the one fault code NamedPerson tolerates from its contact read. Matches
        // FakeOrganizationService.Retrieve, which reproduces this exact code for a row
        // nothing seeded.
        private const int ContactNotFoundErrorCode = -2147220969;

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
            var fileKind = CommandHelpers.GetRequiredString(context, InFileKind).Trim();
            var daily = string.Equals(fileKind, FileDaily, StringComparison.OrdinalIgnoreCase);
            if (!daily && !string.Equals(fileKind, FileQuarterly, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Choose the Daily or the Quarterly file.");
            }

            var checkedFrom = ParseDay(context, InCheckedFrom);
            var checkedTo = ParseDay(context, InCheckedTo);
            if (checkedFrom.HasValue && checkedTo.HasValue && checkedFrom.Value > checkedTo.Value)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "The first check date is after the last one.");
            }

            PermissionHelpers.EnsureAppPermission(systemService, context, "export.generate", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandGenerateExport);
            if (existingAudit != null)
            {
                var priorCount = existingAudit.GetAttributeValue<string>("al_details");
                SetResponse(context, batchId.ToString("D"), priorCount ?? "0", "Generated", existingAudit.Id, false);
                return;
            }

            // The app names a new id per export; a Draft batch made by the retired two-step
            // flow is still generated in place. A query, not a Retrieve, so a new id is an
            // empty answer rather than a fault.
            var batchQuery = new QueryExpression(BatchEntity)
            {
                ColumnSet = new ColumnSet("al_exportbatchcode", "al_batchstatus"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            batchQuery.Criteria.AddCondition("al_exportbatchid", ConditionOperator.Equal, batchId);
            var batch = userService.RetrieveMultiple(batchQuery).Entities.FirstOrDefault();
            string batchCode;
            if (batch == null)
            {
                batchCode = "EXB-" + idempotencyKey;
                userService.Create(new Entity(BatchEntity, batchId)
                {
                    ["al_name"] = FileName(daily, checkedFrom, checkedTo),
                    ["al_exportbatchcode"] = batchCode,
                    ["al_batchstatus"] = new OptionSetValue(BatchStatusDraft),
                    ["al_rowcount"] = 0,
                });
            }
            else
            {
                batchCode = batch.GetAttributeValue<string>("al_exportbatchcode");

                // A batch is a snapshot of what was produced (AD-042): re-generating one would
                // rewrite its rows with today's values. A re-run is a new batch. Retries of the
                // same intent are already handled by the idempotency replay above.
                var currentStatus = batch.GetAttributeValue<OptionSetValue>("al_batchstatus");
                if (currentStatus != null && currentStatus.Value != BatchStatusDraft)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "This export batch has already been generated. Export again to produce a fresh file.");
                }
            }

            var batchRef = new EntityReference(BatchEntity, batchId);

            var cases = new QueryExpression(CaseEntity)
            {
                ColumnSet = new ColumnSet(
                    "al_casereference", "al_advisername", "al_advisercode", "al_adviseremail",
                    "al_paraplanner", "al_paraplannercode", ImportRules.ParaplannerEmailAttribute,
                    "al_casetype", ListOptionRules.CaseTypeAttribute,
                    "al_productsolutiontype", ListOptionRules.ProductTypeAttribute,
                    "al_checkdate", "al_clientname",
                    "al_preorpostcheck", ListOptionRules.PreOrPostCheckAttribute,
                    CaseReviewRouteAttr, TaxOutcomeAttr),
                Criteria = new FilterExpression(),
            };
            cases.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            // The quarterly file takes Closed cases. The daily file also takes graded cases still
            // open and dated: any non-pass goes to remediation on its check day, so Closed alone
            // holds none of the daily file's fails (owner, 2026-11-17).
            var wanted = new FilterExpression(LogicalOperator.Or);
            wanted.AddCondition("al_casestatus", ConditionOperator.Equal, CaseStatusClosed);
            if (daily)
            {
                var gradedOpen = new FilterExpression();
                gradedOpen.AddCondition("al_casestatus", ConditionOperator.In, GradedOpenStatuses);
                gradedOpen.AddCondition("al_checkdate", ConditionOperator.NotNull);
                wanted.AddFilter(gradedOpen);
            }

            cases.Criteria.AddFilter(wanted);
            cases.ColumnSet.AddColumn("al_casestatus");

            // al_checkdate is date-only, so a day compares as that day at midnight; a case with
            // no check date cannot be shown to fall in a range and drops out once one is set.
            if (checkedFrom.HasValue)
            {
                cases.Criteria.AddCondition("al_checkdate", ConditionOperator.GreaterEqual, checkedFrom.Value);
            }

            if (checkedTo.HasValue)
            {
                cases.Criteria.AddCondition("al_checkdate", ConditionOperator.LessEqual, checkedTo.Value);
            }

            // Paged: a bare RetrieveMultiple stops at 5000 and the batch would then stamp
            // that truncated figure as its complete row count (AD-042), leaving no way to
            // detect the shortfall during reconciliation.
            var rows = 0;

            // Per-batch memos. Every batch walks every Closed and graded open case, and the same
            // few routes, advisers, para-planners and named contacts recur on nearly all, so each
            // is asked once per batch rather than once per case inside the two-minute budget.
            var routeRequiresAqs = new System.Collections.Generic.Dictionary<Guid, bool>();
            var staffCodes = new System.Collections.Generic.Dictionary<Guid, string>();
            var matchAdviser = CasePeople.CachedMatcher(userService, "adviser");
            var matchParaplanner = CasePeople.CachedMatcher(userService, "para-planner");

            // A case that cannot be exported complete is left out and named on the batch's
            // audit event, rather than refusing the whole batch: a Closed case has no way back,
            // so one such case used to block every export from then on.
            var leftOutClosed = new System.Collections.Generic.List<string>();
            var leftOutOpen = new System.Collections.Generic.List<string>();

            // Every batch re-snapshots every Closed case ever, so the per-case reads are what grow:
            // the outcomes are read in chunks up front rather than one query per case.
            var allCases = CommandHelpers.RetrieveAll(userService, cases).ToList();
            var outcomes = ResolveOutcomes(userService, allCases.Select(c => c.Id).ToList());

            foreach (var outcomeCase in allCases)
            {
                var caseRef = outcomeCase.GetAttributeValue<string>("al_casereference") ?? outcomeCase.Id.ToString("D");
                var isClosed = outcomeCase.GetAttributeValue<OptionSetValue>("al_casestatus")?.Value == CaseStatusClosed;
                var leftOut = isClosed ? leftOutClosed : leftOutOpen;
                Entity outcomeRow;
                outcomes.TryGetValue(outcomeCase.Id, out outcomeRow);
                var aqsOwed = AqsExpected(userService, outcomeCase, outcomeRow, routeRequiresAqs);
                if (!aqsOwed.HasValue)
                {
                    leftOut.Add("Case " + caseRef + " " + UnknownRouteReason);
                    continue;
                }

                var aqsExpected = aqsOwed.Value;

                // A Tax-only case reports File Quality and nothing else (client, 2026-11-06),
                // so an outcome left behind by a route change does not reach the advice columns.
                var adviceRow = aqsExpected ? outcomeRow : null;
                var adviceGrade = Outcomes.EffectiveOutcomeLabel(adviceRow);
                // One resolution, both values, from the reviews the route owes: a Tax-only
                // case's grade is its Tax answer, never one an abandoned AQS review left.
                var fileQualityAnswer = FileQuality.Resolve(userService, outcomeCase.Id, aqsExpected);
                var fileQualityGrade = FileQuality.Label(fileQualityAnswer);
                var fileQualityChoice = FileQuality.Choice(fileQualityAnswer);
                var effectiveOutcome = Outcomes.EffectiveOutcome(adviceRow);
                var initialOutcome = adviceRow?.GetAttributeValue<OptionSetValue>(Outcomes.InitialOutcomeAttr)?.Value;

                var incomplete = DescribeIncompleteRow(outcomeRow, fileQualityGrade, aqsExpected);
                if (incomplete != null)
                {
                    leftOut.Add("Case " + caseRef + " " + incomplete);
                    continue;
                }

                // The daily file reports the grade first recorded, the quarterly file the one in
                // force (client, 2026-11-16); the record holds the one its file reports.
                var fileOutcome = FileOutcome(daily, effectiveOutcome, initialOutcome);
                var fileGrade = daily && adviceRow != null
                    ? CommandHelpers.Formatted(adviceRow, Outcomes.InitialOutcomeAttr) ?? adviceGrade
                    : adviceGrade;
                var taxOutcome = aqsExpected ? null : outcomeCase.GetAttributeValue<OptionSetValue>(TaxOutcomeAttr)?.Value;
                if (daily && !BelongsInDailyFile(fileQualityChoice, fileOutcome, taxOutcome))
                {
                    continue;
                }

                // Someone named in place of the case's own adviser or paraplanner (project
                // owner, 2026-09-19). Read once per case; FlaggedText decides per column
                // whether it applies, since only the slot the flags point at is filled.
                //
                // A Tax-only case has no outcome for the Tax checker's choice to be applied to,
                // so it stays on their submitted review, and is read from there.
                var judgementRow = aqsExpected ? outcomeRow : (TaxOnlyJudgement(userService, outcomeCase.Id) ?? outcomeRow);
                var fqNamedPerson = NamedPerson(userService, judgementRow, FqAccountableContactAttr, staffCodes);
                var aqNamedPerson = NamedPerson(userService, judgementRow, AqAccountableContactAttr, staffCodes);

                // One resolution per person, reused by every column that needs it. The
                // para-planner's match feeds BOTH column D's email and column D's code, and the
                // codes resolved here feed columns B and D as well as the four accountability
                // code columns L, N, R and T.
                var paraplannerMatch = matchParaplanner(
                    outcomeCase.GetAttributeValue<string>(ImportRules.ParaplannerEmailAttribute));
                var adviserMatch = matchAdviser(outcomeCase.GetAttributeValue<string>("al_adviseremail"));
                var registryCodes = new RegistryCodes
                {
                    Adviser = adviserMatch != null && adviserMatch.IsMatch ? adviserMatch.StaffCode : null,
                    Paraplanner = ParaplannerCodeOf(paraplannerMatch),
                };
                var code = "EXR-" + batchCode + "-" + caseRef;

                var record = new Entity(RecordEntity)
                {
                    ["al_name"] = "Export " + caseRef,
                    ["al_exportrecordcode"] = code,
                    ["al_exportbatchid"] = batchRef,
                    ["al_outcomecaseid"] = new EntityReference(CaseEntity, outcomeCase.Id),
                    ["al_advisername"] = outcomeCase.GetAttributeValue<string>("al_advisername"),
                    // Trail Light col B, a CODE again from 2026-09-22 (project owner:
                    // Trailight could not accommodate the emails put here on 2026-09-21).
                    // From the registry, not the case - see AdviserCode.
                    ["al_advisercode"] = registryCodes.Adviser,
                    // Trail Light col B from 2026-09-21, having been col 21 since 2026-09-19.
                    // Snapshotted like every other column here rather than read live at
                    // file-build time, so a later correction to the case cannot change what
                    // an already-delivered batch says it sent.
                    ["al_adviseremail"] = outcomeCase.GetAttributeValue<string>("al_adviseremail"),
                    ["al_paraplannername"] = outcomeCase.GetAttributeValue<string>("al_paraplanner"),
                    // Trail Light col D, a CODE again from 2026-09-22, resolved by address.
                    ["al_paraplannercode"] = registryCodes.Paraplanner,
                    // Trail Light col D (project owner, 2026-09-21: "replace column B & D
                    // ... rather than codes show emails"). Both people are identified by the
                    // email the case stores (AD-228).
                    ["al_paraplanneremail"] = ParaplannerEmailOf(paraplannerMatch, outcomeCase),
                    ["al_casetype"] = ManagedOption(
                        outcomeCase, ListOptionRules.CaseTypeAttribute, "al_casetype"),
                    ["al_productsolutiontype"] = ProductType(outcomeCase),
                    ["al_clientname"] = outcomeCase.GetAttributeValue<string>("al_clientname"),
                    ["al_preorpostcheck"] = ManagedOption(
                        outcomeCase, ListOptionRules.PreOrPostCheckAttribute, "al_preorpostcheck"),
                    ["al_advicequalitygrade"] = fileGrade,
                    // A Tax-only case's own verdict, which no Trail Light column carries; the
                    // daily file includes the row on it (client, 2026-11-16).
                    ["al_taxoutcome"] = aqsExpected ? null : CommandHelpers.Formatted(outcomeCase, TaxOutcomeAttr),
                    ["al_filequalitygrade"] = fileQualityGrade,
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                };

                // Built by a method of its own, not inline here, so the pairing - which of
                // fqNamedPerson/aqNamedPerson feeds which of the eight columns - is something
                // a unit test can pin directly against this exact code, rather than trust by
                // reading the wiring (2026-09-22 review: a one-variable swap here would have
                // passed every test FlaggedText and NamedPerson had in isolation).
                foreach (var column in AccountabilityColumns(
                    judgementRow, outcomeCase, fqNamedPerson, aqNamedPerson, registryCodes,
                    fileQualityChoice, fileOutcome))
                {
                    record[column.Key] = column.Value;
                }

                var checkDate = outcomeCase.GetAttributeValue<DateTime?>("al_checkdate");
                if (checkDate.HasValue)
                {
                    record["al_checkdate"] = checkDate.Value;
                }

                // A create, not an upsert: the batch is Draft (checked above), a failed run rolls
                // back whole, and the code carries the batch code, so no row with it can exist.
                userService.Create(record);
                rows++;
            }

            var update = new Entity(BatchEntity, batchId)
            {
                ["al_name"] = FileName(daily, checkedFrom, checkedTo),
                ["al_batchstatus"] = new OptionSetValue(BatchStatusGenerated),
                ["al_rowcount"] = rows,
                ["al_generatedon"] = DateTime.UtcNow,
            };
            userService.Update(update);

            foreach (var line in leftOutClosed.Concat(leftOutOpen))
            {
                localPluginContext.Trace("Left out of " + batchCode + ": " + line);
            }

            // Kept on the batch's audit event, the record of this run, so the cases left out
            // of the file can be found and put right. al_details stays the row count, because
            // the idempotent replay reads it back as one.
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandGenerateExport, "GenerateExport " + batchCode, BatchEntity, batchId,
                LeftOutReason(leftOutClosed, leftOutOpen), rows.ToString(), idempotencyKey, context);

            SetResponse(context, batchId.ToString("D"), rows.ToString(), "Generated", auditId, false);
        }

        /// <summary>
        /// The case's product / solution type for the export: the managed-list option it
        /// points at, else the choice column it was imported with (AD-187).
        /// </summary>
        /// <remarks>
        /// The precedence is not cosmetic here. Choosing a managed option writes the lookup
        /// and leaves the choice column at whatever it last held, so a case moved to an
        /// option added since the migration still carries the OLD integer. Reading the choice
        /// column alone would export the previous product type - not a blank, which somebody
        /// might notice, but a plausible wrong answer on a fixed-position file (AD-039).
        ///
        /// The fallback stays because a case never edited since the migration has only the
        /// integer, and an export that blanked those would be a worse trade.
        /// </remarks>
        public static string ProductType(Entity outcomeCase)
        {
            return ManagedOption(
                outcomeCase, ListOptionRules.ProductTypeAttribute, ListOptionRules.ProductTypeLegacyAttribute);
        }

        /// <summary>
        /// A managed-list column for the export: the option the case points at, else the
        /// choice column it was imported with. Shared by all four lists (AD-187).
        /// </summary>
        public static string ManagedOption(Entity outcomeCase, string lookupAttribute, string legacyAttribute)
        {
            if (outcomeCase == null) { return null; }

            var chosen = outcomeCase.GetAttributeValue<EntityReference>(lookupAttribute);
            if (chosen != null && !string.IsNullOrWhiteSpace(chosen.Name))
            {
                return chosen.Name.Trim();
            }

            return CommandHelpers.Formatted(outcomeCase, legacyAttribute);
        }

        /// <summary>
        /// Column D, the para-planner's email for this case, or null when the case holds none.
        ///
        /// Read from the case's al_paraplanneremail (AD-228), never from the name in
        /// al_paraplanner: a name describes a person, and two people can share one. Where one
        /// active contact holds the address, its email is written; otherwise the stored address
        /// itself, trimmed - an address the firm supplied is the para-planner's email whether or
        /// not the directory carries it (see <see cref="ParaplannerEmailOf"/>).
        ///
        /// Public so the outcomes can be asserted directly. The assembly is signed and carries
        /// no InternalsVisibleTo (see PluginBase), so public is what makes a helper reachable
        /// from the test project.
        /// </summary>
        public static string ParaplannerEmail(IOrganizationService service, Entity outcomeCase)
        {
            return ParaplannerEmailOf(ParaplannerMatch(service, outcomeCase), outcomeCase);
        }

        /// <summary>
        /// Resolves the case's para-planner to a contact ONCE, for every column that needs
        /// them.
        ///
        /// Column D's email and column D's code are two readings of one resolution, and the
        /// accountability codes take a third. Asking <see cref="NotificationOutbox.MatchParaplanner"/>
        /// per column ran the same two-row query repeatedly per case for no gain - the rule
        /// the file-quality read in the record build already states in as many words.
        /// </summary>
        public static NotificationOutbox.PersonMatch ParaplannerMatch(
            IOrganizationService service, Entity outcomeCase)
        {
            if (outcomeCase == null) { return null; }

            return NotificationOutbox.MatchParaplanner(
                service, outcomeCase.GetAttributeValue<string>(ImportRules.ParaplannerEmailAttribute));
        }

        /// <summary>
        /// Column D's email, read off an already-resolved match. See
        /// <see cref="ParaplannerEmail(IOrganizationService, Entity)"/> for the rules.
        /// </summary>
        public static string ParaplannerEmailOf(
            NotificationOutbox.PersonMatch match, Entity outcomeCase)
        {
            if (outcomeCase == null) { return null; }

            var stored = outcomeCase.GetAttributeValue<string>(
                ImportRules.ParaplannerEmailAttribute);

            if (match != null && match.IsMatch)
            {
                return match.Email;
            }

            // The address the extract carried, even where no contact answers to it. Column D
            // is the para-planner's email, and an address the firm supplied is that - a
            // directory gap is not a reason to send the Trail Light a blank where a real
            // address exists. This is the same fallback the adviser's letter makes (AD-168).
            return string.IsNullOrWhiteSpace(stored) ? null : stored.Trim();
        }

        /// <summary>
        /// The adviser's staff code for this case, or null where the registry does not hold
        /// one for them.
        ///
        /// Resolved from the registry rather than read off the case (project owner,
        /// 2026-09-22). al_advisercode was only ever filled by hand and almost never was -
        /// OD-050 expected Tax/AQS to type it - which is why column B was empty before the
        /// emails went in. A code belongs to a person, so it is held against the person.
        ///
        /// An unresolved adviser leaves the column EMPTY rather than falling back to the
        /// address, the name or the case's old value. AD-039 is positional: column B is the
        /// adviser's code on every row, or the file lies about the rows where it is not.
        /// </summary>
        public static string AdviserCode(IOrganizationService service, Entity outcomeCase)
        {
            if (outcomeCase == null) { return null; }

            var match = NotificationOutbox.MatchAdviser(
                service, outcomeCase.GetAttributeValue<string>("al_adviseremail"));

            return match != null && match.IsMatch ? match.StaffCode : null;
        }

        /// <summary>
        /// The para-planner's staff code for this case, or null where the registry does not
        /// hold one for them.
        ///
        /// Resolved by the case's para-planner EMAIL only (AD-186, AD-228), never by the name
        /// in al_paraplanner: a name describes a person, and two people can share one. A blank
        /// email, or one that no active contact holds or that two hold, leaves the column
        /// EMPTY, as the adviser's does.
        /// </summary>
        public static string ParaplannerCode(IOrganizationService service, Entity outcomeCase)
        {
            return ParaplannerCodeOf(ParaplannerMatch(service, outcomeCase));
        }

        /// <summary>
        /// The para-planner's staff code, read off an already-resolved match. See
        /// <see cref="ParaplannerCode(IOrganizationService, Entity)"/> for the rules.
        /// </summary>
        public static string ParaplannerCodeOf(NotificationOutbox.PersonMatch match)
        {
            return match != null && match.IsMatch ? match.StaffCode : null;
        }

        /// <summary>
        /// The two registry codes a case's own people carry, resolved once per case.
        ///
        /// A single object rather than two loose string parameters, deliberately. The codes
        /// reach <see cref="AccountabilityColumns"/>, which already takes two interchangeable
        /// <see cref="AccountablePerson"/> values; adding two interchangeable strings beside
        /// them would let an adviser's code be handed to the para-planner's slot and compile
        /// without complaint, putting one person's code beside another person's name on a
        /// file an external system reads by position (2026-09-22 review). Named properties
        /// cannot be transposed silently.
        ///
        /// Null on either side means the registry holds no code for that person, which is a
        /// BLANK column - never the case's retired al_advisercode / al_paraplannercode, and
        /// never a name or an address.
        /// </summary>
        public sealed class RegistryCodes
        {
            /// <summary>The adviser's staff code, or null where the registry holds none.</summary>
            public string Adviser { get; set; }

            /// <summary>The para-planner's staff code, or null where the registry holds none.</summary>
            public string Paraplanner { get; set; }

            /// <summary>
            /// The code belonging to whichever of the two people the given case code column
            /// names, or null for any other column.
            /// </summary>
            public string For(string caseAttribute)
            {
                if (string.Equals(caseAttribute, "al_advisercode", StringComparison.OrdinalIgnoreCase))
                {
                    return Adviser;
                }

                if (string.Equals(caseAttribute, "al_paraplannercode", StringComparison.OrdinalIgnoreCase))
                {
                    return Paraplanner;
                }

                return null;
            }
        }

        /// <summary>
        /// The "who carries this fail" choice a Tax checker made on a Tax-only case, as the
        /// four flags and two people an outcome row would carry, or null where none was made.
        ///
        /// The portal parks the choice on the review until an outcome exists to apply it to
        /// (AccountabilityRequestPlugin). A Tax-only case never gets one, so the choice used
        /// to be dropped and the file fell back to its default person. Once that review is
        /// submitted the choice can no longer be changed, so the parked value is the record.
        /// </summary>
        public static Entity TaxOnlyJudgement(IOrganizationService service, Guid caseId)
        {
            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet("al_pendingaccountability"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
            query.Criteria.AddCondition("al_reviewtype", ConditionOperator.Equal, ResponseRules.ReviewTypeTax);
            query.Criteria.AddCondition("al_pendingaccountability", ConditionOperator.NotNull);
            query.AddOrder("al_submittedon", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            var judgement = new Entity(OutcomeEntity);
            if (found.Count == 0 || !AccountabilityRequestPlugin.ApplyParked(service, found[0], judgement))
            {
                return null;
            }

            // The parked choice holds ids only; the name column needs the person's name.
            foreach (var attribute in new[] { FqAccountableContactAttr, AqAccountableContactAttr })
            {
                var person = judgement.GetAttributeValue<EntityReference>(attribute);
                if (person == null)
                {
                    continue;
                }

                // A query, not a Retrieve: a contact deleted since answers with no row rather
                // than a fault, which a plug-in may not catch and carry on from.
                var contact = new QueryExpression(ContactRegistry.Entity)
                {
                    ColumnSet = new ColumnSet(ContactRegistry.FullNameAttr),
                    TopCount = 1,
                    Criteria = new FilterExpression(),
                };
                contact.Criteria.AddCondition("contactid", ConditionOperator.Equal, person.Id);
                var rows = service.RetrieveMultiple(contact).Entities;
                person.Name = rows.Count == 0 ? null : rows[0].GetAttributeValue<string>(ContactRegistry.FullNameAttr);
            }

            return judgement;
        }

        private const string UnknownRouteReason =
            "has no review route and an Advice Quality check that is no longer active, so whether "
            + "its advice grades are owed cannot be told. It was left out of this export; set the "
            + "case's review route and generate a new batch.";

        /// <summary>
        /// The cases a batch left out, for its audit event: a count and each case's reason,
        /// cut to what the column holds. Null when every Closed case was exported.
        /// </summary>
        public static string LeftOutReason(System.Collections.Generic.IList<string> leftOut)
        {
            return LeftOutReason(leftOut, null);
        }

        /// <summary>As above, with the graded cases not yet closed counted on their own.</summary>
        public static string LeftOutReason(
            System.Collections.Generic.IList<string> closed, System.Collections.Generic.IList<string> open)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (closed != null && closed.Count > 0)
            {
                parts.Add(closed.Count + " closed case(s) left out of this export: " + string.Join(" | ", closed));
            }

            if (open != null && open.Count > 0)
            {
                parts.Add(open.Count + " case(s) not yet closed left out of this export: " + string.Join(" | ", open));
            }

            return parts.Count == 0 ? null : CommandHelpers.Truncate(string.Join(" || ", parts), 2000);
        }

        /// <summary>
        /// An optional <c>yyyy-MM-dd</c> day parameter, or null when it is not given. Anything
        /// else is refused rather than read as "no bound", which would widen the file silently.
        /// </summary>
        private static DateTime? ParseDay(IPluginExecutionContext context, string name)
        {
            var value = CommandHelpers.GetOptionalString(context, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            DateTime day;
            if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out day))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + name + " must be a date written yyyy-mm-dd.");
            }

            return day;
        }

        /// <summary>
        /// The batch's name, which says which file it is: "Daily file, checked 24 Nov 2026",
        /// "Quarterly file, checked 1 Oct 2026 to 31 Dec 2026", "Quarterly file, every case".
        /// </summary>
        public static string FileName(bool daily, DateTime? from, DateTime? to)
        {
            Func<DateTime, string> day = d => d.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            string range;
            if (from.HasValue && to.HasValue)
            {
                range = from.Value == to.Value ? "checked " + day(from.Value) : "checked " + day(from.Value) + " to " + day(to.Value);
            }
            else if (from.HasValue)
            {
                range = "checked on or after " + day(from.Value);
            }
            else if (to.HasValue)
            {
                range = "checked on or before " + day(to.Value);
            }
            else
            {
                range = "every case";
            }

            return (daily ? "Daily file, " : "Quarterly file, ") + range;
        }

        /// <summary>
        /// The advice outcome a file reports, and names its accountable people against: the
        /// daily file the grade first recorded, the quarterly file the one in force (client,
        /// 2026-11-16). A fail regraded to a pass names someone in the daily file only.
        /// </summary>
        public static int? FileOutcome(bool daily, int? effectiveOutcome, int? initialOutcome)
        {
            return daily ? initialOutcome ?? effectiveOutcome : effectiveOutcome;
        }

        /// <summary>
        /// Whether a row belongs in the daily file (client, 2026-11-16): any type of failure - a
        /// File Quality Fail; an advice grade of Pass with issues, Insufficient evidence or
        /// Potential harm; or a Tax-only check that came back Fail or Pass with issues, which no
        /// Trail Light column carries. A clean pass is left out.
        /// </summary>
        public static bool BelongsInDailyFile(int? fileQualityChoice, int? adviceOutcome, int? taxOutcome)
        {
            // The Tax scale is Pass / Fail / ChoiceInsufficient, which the Tax check labels "Pass
            // with issues" (AD-055); al_taxoutcome holds that value, never ChoicePassWithIssues.
            return FileQuality.Failed(fileQualityChoice)
                || adviceOutcome == OutcomeRules.OutcomePassWithIssues
                || AdviceQualityFailed(adviceOutcome)
                || (taxOutcome.HasValue && OutcomeRules.TaxResultRequiresRemediation(taxOutcome.Value));
        }

        /// <summary>Graded, so dated and holding its first grades, but not yet closed.</summary>
        private static readonly object[] GradedOpenStatuses =
        {
            CaseLifecycle.Submitted, CaseLifecycle.AwaitingRemediation, CaseLifecycle.RemediationInProgress,
            CaseLifecycle.AwaitingSignoff, CaseLifecycle.AwaitingRecheck,
        };

        // AD-039 col 15 Advice Quality grade = final outcome, or initial when not yet
        // finalised (BR-007). Also returns the raw outcome value and the four OD-024
        // accountability flags (Task 2), so the caller has both the grade and the
        // accountability judgement without a second read.
        /// <summary>Cases per outcome query: an In list well inside the URL and FetchXML limits.</summary>
        private const int OutcomeLookupChunk = 200;

        /// <summary>
        /// The newest al_outcome of each case, read in chunks. A case with none is absent.
        ///
        /// One Outcome per case is the intent, but nothing in the schema enforces it and a
        /// re-review can leave a second. This row decides the exported grade and the
        /// accountability gate, so picking whichever row Dataverse returned first would make the
        /// export non-deterministic. Newest wins.
        /// </summary>
        public static System.Collections.Generic.Dictionary<Guid, Entity> ResolveOutcomes(
            IOrganizationService service, System.Collections.Generic.IList<Guid> caseIds)
        {
            var newest = new System.Collections.Generic.Dictionary<Guid, Entity>();
            for (var start = 0; start < caseIds.Count; start += OutcomeLookupChunk)
            {
                var chunk = caseIds.Skip(start).Take(OutcomeLookupChunk).Cast<object>().ToArray();
                var query = new QueryExpression(OutcomeEntity)
                {
                    ColumnSet = OutcomeColumns(),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.In, chunk);
                query.AddOrder("createdon", OrderType.Descending);

                foreach (var row in CommandHelpers.RetrieveAll(service, query))
                {
                    var caseRef = row.GetAttributeValue<EntityReference>("al_outcomecaseid");
                    if (caseRef != null && !newest.ContainsKey(caseRef.Id))
                    {
                        newest[caseRef.Id] = row;
                    }
                }
            }

            return newest;
        }

        private static ColumnSet OutcomeColumns()
        {
            return new ColumnSet(
                "al_finaloutcome", "al_initialoutcome",
                "al_fqadviseraccountable", "al_fqparaplanneraccountable",
                "al_aqadviseraccountable", "al_aqparaplanneraccountable",
                FqAccountableContactAttr, AqAccountableContactAttr, FqAdministratorContactAttr, FqAdministratorFlag,
                "al_outcomecaseid");
        }

        /// <summary>
        /// Why this case cannot be exported, phrased to follow "Case {reference} ", or null
        /// when the row is complete.
        ///
        /// AD-039 is a fixed-position contract, so a blank in a graded column does not read
        /// downstream as "not assessed" — it reads as an assessment that came back empty.
        /// An incomplete row is therefore left out of the batch and named on its audit event.
        /// It used to refuse the whole batch, but a Closed case cannot be reopened, so one
        /// such case blocked every export after it.
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
                    ? "has no outcome recorded, so the Advice Quality grade would be blank. "
                        + "It was left out of this export."
                    : null;
            }

            var effective = Outcomes.EffectiveOutcome(outcomeRow);
            if (!effective.HasValue)
            {
                return aqsExpected
                    ? "has an outcome carrying neither an initial nor a final grade, so the "
                        + "Advice Quality grade would be blank. It was left out of this export."
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
                    + "grade would be blank. It was left out of this export.";
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
        ///
        /// Null when it cannot be told: no route and no active AQS review, yet an AQS review
        /// that is no longer active, or an outcome (which only an AQS submit writes), says the
        /// advice was graded once. Read as Tax-only that case would export blank advice grades,
        /// so the caller leaves it out instead. Route answers are memoised per batch in
        /// <paramref name="routeCache"/>.
        /// </summary>
        public static bool? AqsExpected(
            IOrganizationService service, Entity outcomeCase, Entity outcomeRow,
            System.Collections.Generic.IDictionary<Guid, bool> routeCache)
        {
            var routeRef = outcomeCase.GetAttributeValue<EntityReference>(CaseReviewRouteAttr);
            if (routeRef != null)
            {
                bool requires;
                if (routeCache == null || !routeCache.TryGetValue(routeRef.Id, out requires))
                {
                    var route = service.Retrieve(RouteEntity, routeRef.Id, new ColumnSet(RouteRequiresAqsAttr));
                    requires = route.GetAttributeValue<bool?>(RouteRequiresAqsAttr) ?? false;
                    if (routeCache != null)
                    {
                        routeCache[routeRef.Id] = requires;
                    }
                }

                return requires;
            }

            var query = new QueryExpression(ReviewEntity)
            {
                ColumnSet = new ColumnSet("statecode"),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, outcomeCase.Id);
            query.Criteria.AddCondition("al_reviewtype", ConditionOperator.Equal, ResponseRules.ReviewTypeAqs);

            var reviews = service.RetrieveMultiple(query).Entities;
            foreach (var review in reviews)
            {
                var state = review.GetAttributeValue<OptionSetValue>("statecode");
                if (state != null && state.Value == 0)
                {
                    return true;
                }
            }

            return reviews.Count == 0 && outcomeRow == null ? false : (bool?)null;
        }

        private static bool Flagged(Entity outcomeRow, string flag)
        {
            return outcomeRow != null && (outcomeRow.GetAttributeValue<bool?>(flag) ?? false);
        }

        /// <summary>
        /// Whether the advice grade names someone in the feed: Insufficient evidence or
        /// Potential harm. A Pass with issues needs remediation, but it is not a fail here
        /// (client, 2026-11-06).
        /// </summary>
        private static bool AdviceQualityFailed(int? effectiveOutcome)
        {
            return effectiveOutcome == OutcomeRules.OutcomeInsufficient
                || effectiveOutcome == OutcomeRules.OutcomePotentialHarm;
        }

        /// <summary>
        /// The case value when that person is accountable, otherwise empty. AD-039
        /// attributes a fail to the adviser and/or the paraplanner, so a pair who is not
        /// accountable is written empty rather than filled in.
        /// </summary>
        public static string FlaggedText(bool accountable, Entity outcomeCase, string caseAttribute)
        {
            return FlaggedText(accountable, outcomeCase, caseAttribute, null, null);
        }

        /// <summary>
        /// As the five-argument overload, with no registry codes supplied - so a CODE column
        /// on the unnamed branch is BLANK. Kept for the name columns and for callers that
        /// have already resolved the named person; the record build always passes the codes.
        /// </summary>
        public static string FlaggedText(
            bool accountable, Entity outcomeCase, string caseAttribute, AccountablePerson namedPerson)
        {
            return FlaggedText(accountable, outcomeCase, caseAttribute, namedPerson, null);
        }

        /// <summary>
        /// The text AD-039 wants in an accountability column: empty when the person that
        /// column names does not carry this fail, and otherwise their name or code.
        ///
        /// <paramref name="namedPerson"/> overrides the case's own value (project owner,
        /// 2026-09-19). Accountability can be given to any contact, not only the adviser or
        /// paraplanner the case names, and columns 11-20 are fixed as "fail adviser" and
        /// "fail paraplanner" pairs, so the chosen person is written into whichever slot the
        /// flags say they fill.
        ///
        /// A CODE column now carries THAT PERSON'S code (2026-09-22). It used to be blanked,
        /// because a contact carried no code and emitting the case's code beside someone
        /// else's name would attribute the fail to a name and a code belonging to two
        /// different people. The registry removed that constraint; the guard against mixing
        /// two people survives as the rule that the code must come from the same person as
        /// the name, and is blank when they have none.
        ///
        /// <paramref name="registryCodes"/> is what a CODE column reads on the UNNAMED
        /// branch, and the case's own al_advisercode / al_paraplannercode is NOT read at all
        /// (2026-09-22 review). Naming a specific accountable person is the rare path - the
        /// common one derives accountability from the people the case itself names, and that
        /// path used to read the case columns this body of work retired and made uneditable
        /// everywhere. The registration tool still seeds al_advisercode = "ADV-S01" onto
        /// cases, so a single Trail Light row could carry a registry code in column B and a
        /// stale hand-typed one in column N, for the same person, on a file read by position
        /// that cannot tell the two apart. The registry is the only source for a code column
        /// on BOTH branches; where it holds none the column is blank.
        ///
        /// A NAME column is unaffected: with nobody named, the case's own person is who the
        /// fail belongs to, and their name is on the case.
        /// </summary>
        public static string FlaggedText(
            bool accountable, Entity outcomeCase, string caseAttribute,
            AccountablePerson namedPerson, RegistryCodes registryCodes)
        {
            if (!accountable)
            {
                return string.Empty;
            }

            var isCodeColumn = IsCodeColumn(caseAttribute);

            if (namedPerson != null && !string.IsNullOrWhiteSpace(namedPerson.Name))
            {
                return isCodeColumn
                    ? (namedPerson.StaffCode ?? string.Empty).Trim()
                    : namedPerson.Name.Trim();
            }

            if (isCodeColumn)
            {
                var registryCode = registryCodes == null ? null : registryCodes.For(caseAttribute);
                return (registryCode ?? string.Empty).Trim();
            }

            return outcomeCase.GetAttributeValue<string>(caseAttribute) ?? string.Empty;
        }

        private static bool IsCodeColumn(string caseAttribute)
        {
            return caseAttribute != null
                && caseAttribute.EndsWith("code", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A contact named as accountable for a discipline: who they are, and their code.
        ///
        /// The code is why this is a pair rather than a name. Until 2026-09-22 a contact had
        /// no code at all, so the four accountability CODE columns were blanked whenever
        /// somebody was named - see FlaggedText. The registry now holds one.
        /// </summary>
        public sealed class AccountablePerson
        {
            /// <summary>The contact's display name, as the lookup reports it.</summary>
            public string Name { get; set; }

            /// <summary>Their staff code, or null where the registry holds none.</summary>
            public string StaffCode { get; set; }
        }

        /// <summary>
        /// The contact named as accountable for a discipline, or null where the export
        /// should use the people the case itself names.
        ///
        /// The staff-code read tolerates exactly one fault - ObjectDoesNotExist
        /// (<see cref="ContactNotFoundErrorCode"/>), for a contact deleted since the
        /// judgement was recorded - and lets every other fault propagate. In particular a
        /// privilege refusal is NOT swallowed here: it must abort the export rather than be
        /// read as "this person has no code", which would quietly blank the accountability
        /// code for every named person in the batch with nothing to show for it afterwards.
        /// </summary>
        public static AccountablePerson NamedPerson(
            IOrganizationService service, Entity outcomeRow, string lookupAttribute)
        {
            return NamedPerson(service, outcomeRow, lookupAttribute, null);
        }

        /// <summary>
        /// As the three-argument overload, with each contact's staff code read once per
        /// <paramref name="staffCodeCache"/> (one export batch), however many cases name them.
        /// </summary>
        public static AccountablePerson NamedPerson(
            IOrganizationService service, Entity outcomeRow, string lookupAttribute,
            System.Collections.Generic.IDictionary<Guid, string> staffCodeCache)
        {
            if (outcomeRow == null)
            {
                return null;
            }

            var reference = outcomeRow.GetAttributeValue<EntityReference>(lookupAttribute);
            if (reference == null)
            {
                return null;
            }

            // Retrieved rather than taken from the lookup's Name alone: the lookup carries a
            // label, never the code. A contact that has been deleted since the judgement was
            // recorded still has its name on the row, so a not-found read leaves the code
            // null rather than throwing - the name is the part that must survive.
            //
            // Only that one fault is tolerated. A privilege refusal reads identically here
            // unless it is told apart - this environment has hit SecLib::CheckPrivilege
            // faults from a missing app role repeatedly - and swallowing one would blank the
            // code for every named person in the whole export batch with nothing traced
            // afterwards, producing a file that reads as "none of these people have codes"
            // when the truth is "the export could not read Contact at all" (2026-09-22
            // review, overruling this task's own brief, which had written the blanket catch).
            string staffCode;
            if (staffCodeCache != null && staffCodeCache.TryGetValue(reference.Id, out staffCode))
            {
                return new AccountablePerson { Name = reference.Name, StaffCode = staffCode };
            }

            staffCode = null;
            try
            {
                var contact = service.Retrieve(
                    ContactRegistry.Entity,
                    reference.Id,
                    new ColumnSet(ContactRegistry.StaffCodeAttr));
                staffCode = contact.GetAttributeValue<string>(ContactRegistry.StaffCodeAttr);
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault> fault)
            {
                if (fault.Detail == null || fault.Detail.ErrorCode != ContactNotFoundErrorCode)
                {
                    throw;
                }

                staffCode = null;
            }

            if (staffCodeCache != null)
            {
                staffCodeCache[reference.Id] = staffCode;
            }

            return new AccountablePerson { Name = reference.Name, StaffCode = staffCode };
        }

        /// <summary>
        /// The eight AD-039 fail-accountability columns for one export record: the FQ and AQ
        /// "fail adviser" / "fail paraplanner" name/code pairs (columns 11-20 minus the two
        /// accountable-contact lookups, which are not exported columns).
        ///
        /// Pulled out of the record build so the pairing itself is something a test can call
        /// directly and pin: <paramref name="fqNamedPerson"/> must feed every al_fqfail*
        /// column and never an al_aqfail* one, and vice versa for
        /// <paramref name="aqNamedPerson"/>. A one-variable swap between the two would still
        /// pass every FlaggedText/NamedPerson test taken alone, because both look identical
        /// to an AccountablePerson - the defect this method exists to make visible is
        /// specifically about which slot receives which variable, not about either helper's
        /// own logic (2026-09-22 review).
        /// </summary>
        public static System.Collections.Generic.IDictionary<string, object> AccountabilityColumns(
            Entity outcomeRow, Entity outcomeCase,
            AccountablePerson fqNamedPerson, AccountablePerson aqNamedPerson,
            RegistryCodes registryCodes,
            int? fileQualityChoice, int? effectiveOutcome)
        {
            return new System.Collections.Generic.Dictionary<string, object>
            {
                ["al_fqfailadvisername"] = FlaggedText(
                    IsAccountable(outcomeRow, FqAdviserFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_advisername", fqNamedPerson),
                ["al_fqfailadvisercode"] = FlaggedText(
                    IsAccountable(outcomeRow, FqAdviserFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_advisercode", fqNamedPerson, registryCodes),
                ["al_fqfailparaplannername"] = FlaggedText(
                    IsAccountable(outcomeRow, FqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_paraplanner", fqNamedPerson),
                ["al_fqfailparaplannercode"] = FlaggedText(
                    IsAccountable(outcomeRow, FqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_paraplannercode", fqNamedPerson, registryCodes),
                ["al_aqfailadvisername"] = FlaggedText(
                    IsAccountable(outcomeRow, AqAdviserFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_advisername", aqNamedPerson),
                ["al_aqfailadvisercode"] = FlaggedText(
                    IsAccountable(outcomeRow, AqAdviserFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_advisercode", aqNamedPerson, registryCodes),
                ["al_aqfailparaplannername"] = FlaggedText(
                    IsAccountable(outcomeRow, AqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_paraplanner", aqNamedPerson),
                ["al_aqfailparaplannercode"] = FlaggedText(
                    IsAccountable(outcomeRow, AqParaplannerFlag, fileQualityChoice, effectiveOutcome),
                    outcomeCase, "al_paraplannercode", aqNamedPerson, registryCodes),
            };
        }

        /// <summary>
        /// Whether the person that <paramref name="flag"/> names carries this fail.
        ///
        /// Nobody is named unless that discipline failed (client, 2026-11-06): File Quality
        /// on a Fail, Advice Quality on Insufficient evidence or Potential harm. So a judgement
        /// left behind by a regrade drops out with the fail it described.
        ///
        /// A recorded judgement then wins for its own discipline. Where that discipline has
        /// none, its person is derived from the people the case already names (project owner,
        /// 2026-09-16): the paraplanner compiles the file, so a File Quality fail is theirs;
        /// the adviser gives the advice, so an Advice Quality fail is theirs. Per discipline,
        /// because a fail must always name someone: ticking only the file used to leave a
        /// Potential harm with nobody against it.
        ///
        /// Deriving is what makes a Tax case attributable at all. The four flags live on
        /// al_outcome and a Tax review creates no such row, so before this there was
        /// nowhere to record who was accountable for a Tax fail - 254397454 exported a
        /// file quality Fail with nobody named and nothing able to object.
        ///
        /// A discipline whose two flags are false is "nobody has said yet", not "nobody is
        /// responsible", so it derives. Recording a deliberate nobody is therefore not
        /// expressible - it was not expressible before either, the OD-024 gate having
        /// refused a row that named no one.
        /// </summary>
        /// <summary>
        /// Whether an administrator carries the File Quality fail: the flag, or a contact
        /// named before the flag replaced the name.
        /// </summary>
        public static bool AdministratorCarriesFile(Entity outcomeRow)
        {
            return Flagged(outcomeRow, FqAdministratorFlag)
                || outcomeRow?.GetAttributeValue<EntityReference>(FqAdministratorContactAttr) != null;
        }

        public static bool IsAccountable(Entity outcomeRow, string flag, int? fileQualityChoice, int? effectiveOutcome)
        {
            var fileFlag = flag == FqAdviserFlag || flag == FqParaplannerFlag;

            if (!(fileFlag ? FileQuality.Failed(fileQualityChoice) : AdviceQualityFailed(effectiveOutcome)))
            {
                return false;
            }

            // An administrator named for the file is a judgement too (requirements log row 10):
            // the fail is theirs, so the paraplanner is not derived, and the administrator
            // themselves is never exported - the File Quality person columns stay blank.
            var recorded = fileFlag
                ? Flagged(outcomeRow, FqAdviserFlag) || Flagged(outcomeRow, FqParaplannerFlag)
                    || AdministratorCarriesFile(outcomeRow)
                : Flagged(outcomeRow, AqAdviserFlag) || Flagged(outcomeRow, AqParaplannerFlag);

            // The adviser does not carry the file and the paraplanner does not carry the
            // advice, so neither is derived; a judgement can still name them.
            return recorded ? Flagged(outcomeRow, flag) : flag == FqParaplannerFlag || flag == AqAdviserFlag;
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
