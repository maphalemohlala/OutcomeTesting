using OutcomeTesting.Registration;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Registers OR verifies the CompleteRemediation server-side command (AD-003) using the
// supported IOrganizationService API — the same calls the Plugin Registration Tool makes.
//
// Register: dotnet run -- <orgUrl> [<pluginDllPath>]
//   Creates/updates the plug-in assembly, plug-in type and the al_CompleteRemediation
//   Custom API with its request parameters and response properties. Idempotent.
//
// Register all: dotnet run -- registerall <orgUrl> [<pluginDllPath>]
//   Contract-driven: upserts the assembly once, then every command found in
//   plugins/customapi/*.customapi.json (plug-in type + Custom API + parameters). Idempotent.
//
// Verify:   dotnet run -- verify <orgUrl> <caseId> --confirm <orgUrl>        (CompleteRemediation)
//           dotnet run -- verifysignoff <orgUrl> <caseId> --confirm <orgUrl>  (SignOffRemediation)
//           dotnet run -- verifyregrade <orgUrl> <caseId> --confirm <orgUrl>  (RegradeCase)
//   Seeds temporary records, invokes the command, and asserts the state transition, the
//   audit event and idempotency. Prints PASS/FAIL evidence and cleans up.
//
//   These modes CREATE AND MUTATE REAL BUSINESS RECORDS on the case you name, and the
//   Audit Events the commands write are immutable — cleanup deletes the seeded rows but
//   cannot remove the audit trail they leave behind (NFR-AUD-01). Run them against a
//   development environment and a case seeded for the purpose, never a live client case.
//   That is why the case is named explicitly rather than picked, and why the org URL has
//   to be repeated after --confirm: neither can happen by muscle memory.
//
// Register step: dotnet run -- registerstep <orgUrl> <typeName> <message> <table> <stage>
//                              [<filteringAttributes>] [sync|async] [<runAsUpnOrId>]
//   Stage is 20 (pre-operation) or 40 (post-operation). Mode defaults to sync. runAs sets
//   the step's impersonating user, which is the account the plug-in executes as.
//
//   The PP-15 drain is the one asynchronous step, and it is registered last because
//   registering it is what switches notification delivery on for an environment:
//     registerstep <orgUrl> OutcomeTesting.Plugins.NotificationDrainPlugin Create \
//                  al_notification 40 "" async <serviceAccountUpn>
//   Do not register it until server-side email is approved and tested for that account's
//   mailbox (OD-030). Until then rows rest at Pending, which is the honest state.
//
// Add a command value: dotnet run -- addcommandvalue <orgUrl> <value> <label>
//   Mints one value on al_auditevent.al_command, the option set every command stamps on its
//   audit row. Additive: rows already written keep the value they carry, which under
//   NFR-AUD-01 is permanent, so a re-labelling is a documented cut-over date and not a
//   migration. Idempotent, and it refuses a label another value already holds.
//
// Metadata membership: dotnet run -- metadatamembership <orgUrl>
//   Read-only. Reports every al_ table's membership of the shipping solution AND its
//   rootcomponentbehavior, which is the fact that decides whether an option minted after the
//   table was added travels with an export. Prints the fix command for anything short.
//
// Add metadata: dotnet run -- addmetadatatosolution <orgUrl> <entity> [<attribute>]
//   Adds a table with all its subcomponents, or one named attribute.
//
// Prove PP-15: dotnet run -- provepp15 <orgUrl> --confirm <orgUrl>
//   Causes one allocation, then follows it emitter -> outbox -> async drain -> server-side
//   email and reports each hop. Checks the emitter step, the drain step and the sending
//   mailbox BEFORE writing anything, so a switched-off path is reported without leaving
//   business rows behind to explain.
//
//   This CREATES REAL RECORDS AND SENDS REAL EMAIL. It holds the blast radius to rows it
//   created — its own seeded case, allocated to the account the drain runs as, both deleted
//   afterwards — but the al_notification row is left in place deliberately: it is the
//   evidence. Same --confirm discipline as the verify modes, for the same reason.
//
// Prove AD-089: dotnet run -- proveadoption <orgUrl> <subjectEmail> [<roleName>] --confirm <orgUrl>
//   Proves the write path the conflict rule exists for: grants a web role in Power Pages
//   ONLY, checks it surfaces as unadopted, adopts it, withdraws it in the app, puts the
//   association back, and checks the row then reads "Withdrawn in app, still granted".
//
//   This GRANTS A REAL PERSON A REAL PORTAL ROLE for the length of the run, which is why
//   the subject is named rather than picked and the org URL is repeated after --confirm.
//   It refuses to start unless the subject holds the role in neither source, so it can
//   only put back what it found. The audit events the commands write are immutable
//   (NFR-AUD-01) and remain afterwards - that is the record of the run, not litter.
//
// Import seed data: dotnet run -- importseed <orgUrl> <packageFolder> --confirm <orgUrl>
//   Upserts a Configuration Migration package (data_schema.xml + data.xml) on the ids the
//   package gives its records, so a re-import is idempotent. Exists because there is no
//   `pac data import` in 2.11.2 and the supported tool is a desktop GUI; the reference data
//   under data/ could otherwise not be applied from a session at all.
//
// Who a sign-in reaches: dotnet run -- identities <orgUrl>
//   Read-only. Joins adx_externalidentity, systemuser.azureactivedirectoryobjectid and the
//   web role associations, and lists the contacts nothing is bound to. Exists because each
//   of those read alone is enough to reach the wrong conclusion: a binding names a contact,
//   so it looks settled, while the object id inside it can belong to a different account
//   entirely — which is exactly what DEV turned out to hold.
//
// Bind an identity: dotnet run -- bindidentity <orgUrl> <entraObjectId> <contactEmail> [--repoint] --confirm <orgUrl>
//   Binds one Entra object id to one contact, so that person's own sign-in reaches their own
//   contact. The identity provider is copied from a binding that already works rather than
//   typed, because a plausible-looking wrong issuer produces a sign-in that reaches nobody.
//
//   This CHANGES WHO A REAL PERSON IS on the portal. Additive by default — it does not
//   disturb a binding that already exists, and refuses rather than repoints one that already
//   claims the same object id.
//
//   `--repoint` moves an existing binding. That is a different act from granting one: it
//   takes a sign-in away from whoever holds it today, so it needs its own flag rather than
//   being what happens when an "add" is re-run. It reports what the previous contact can no
//   longer be reached by, which is the half of a repoint nobody asks for and everybody needs.
//
// Grant a role: dotnet run -- grantrole <orgUrl> <contactEmail> <roleName> --confirm <orgUrl>
//   Grants one web role through al_AssignUserRole, the command the app itself calls, so the
//   association and the al_userrolemapping mirror are written together. Unlike proveroles it
//   does not withdraw afterwards: it GRANTS A REAL PERSON REAL PORTAL ACCESS permanently.
//
// Route a case: dotnet run -- routecase <orgUrl> <caseName> <routeName> [<assigneeEmail>] --confirm <orgUrl>
//   Points a case at a review route and, given an assignee, allocates it the way the portal
//   claim does — al_caseassignment carrying only the case and the contact, leaving
//   ClaimCasePlugin to decide the discipline and create the review instance. Deliberately
//   does not write the review itself: a hand-written one would prove the pages render, not
//   that routing produces the right check.
//
// Delete a table: dotnet run -- deletetable <orgUrl> <logicalName> [--with-rows] --confirm <orgUrl>
//   Deletes a custom table. IRREVERSIBLE. Refuses unless it is custom, unmanaged and empty,
//   and reports what still references it rather than leaving the platform's "referenced by 1
//   other component" to be decoded. `--with-rows` allows a table that holds data, prints every
//   row first so the run's own output is the record, and is a separate opt-in because a
//   command that silently destroys rows is one nobody can safely re-run.
//   It cannot check the thing that actually breaks an environment — whether deployed code
//   still reads the table — because a plug-in's RetrieveMultiple is not a dependency
//   Dataverse can see. Retire the read, deploy that, then delete.
//
// Delete a relationship: dotnet run -- deleterelationship <orgUrl> <schemaName> --confirm <orgUrl>
//   Deletes a custom relationship and, for a many-to-many, the intersect table behind it.
//   IRREVERSIBLE — re-creating one later mints a different table — so it refuses unless the
//   relationship is custom, unmanaged, and its intersect is empty. An intersect with rows in
//   it is data somebody is relying on, whatever a register says.
//
// Add to solution: dotnet run -- addtosolution <orgUrl> [<solutionUniqueName>]
//   Adds the plug-in assembly (and its plug-in type) to the target solution for clean ALM
//   promotion. Idempotent. The Custom API is added separately via a solution-file import
//   (src/customapis/al_CompleteRemediation, pac solution import).

const string AssemblyName = "OutcomeTesting.Plugins";

// The shipping solution. Anything not a member of it does not promote to TEST or PROD.
const string SolutionUniqueName = "OutcomeTesting";
const string TypeName = "OutcomeTesting.Plugins.CompleteRemediationPlugin";
const string ApiUniqueName = "al_CompleteRemediation";
const int StatusOpen = 120910600;
const int StatusCompleted = 120910602;
const int CommandCompleteRemediation = 120910756;

// al_auditevent.al_command is the option set every server-side command stamps on its audit
// row, which is why `addcommandvalue` names one attribute rather than taking any option set:
// minting a value here is a change to the accountability trail's vocabulary.
const string AuditEntity = "al_auditevent";
const string CommandAttribute = "al_command";

// PP-15 proof-run constants, mirroring NotificationOutbox and CaseLifecycle in the plug-in
// assembly. Duplicated rather than referenced because this tool targets net8.0 and the
// assembly targets net462 (AD-062); every one of them is asserted against the environment on
// each run, so drift shows up as a FAIL rather than a wrong answer.
const int CaseStatusImported = 120910580;
const int CaseStatusQueued = 120910583;
const int CaseStatusReadyForAllocation = 120910582;
const int EventAllocation = 120910800;
const int StatusPending = 120910810;
const int StatusSent = 120910811;
const int StatusFailed = 120910812;

// The drain is asynchronous, so the platform decides when the job runs. Two minutes is long
// enough that a slow queue is not read as a failure, and short enough that a genuinely stuck
// row is reported rather than waited on.
const int DrainWaitSeconds = 120;

ServiceClient Connect(string orgUrl)
{
    var connectionString =
        $"AuthType=OAuth;Url={orgUrl.TrimEnd('/')};AppId=51f81489-12ee-4a9e-aaae-a2591f45987d;" +
        "RedirectUri=http://localhost;LoginPrompt=Auto";
    Console.WriteLine($"Connecting to {orgUrl} (a browser opens for sign-in the first time)…");
    var svc = new ServiceClient(connectionString);
    if (!svc.IsReady)
    {
        throw new InvalidOperationException($"Connection failed: {svc.LastError}");
    }

    Console.WriteLine($"Connected as {svc.OAuthUserId}.");
    return svc;
}

if (args.Length >= 2 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    var target = VerificationTarget(args);
    return target == null ? 1 : Verify(args[1], target.Value);
}

if (args.Length >= 2 && args[0].Equals("registerall", StringComparison.OrdinalIgnoreCase))
{
    return RegisterAll(args[1], args.Length > 2 ? args[2] : null);
}

if (args.Length >= 2 && args[0].Equals("verifysignoff", StringComparison.OrdinalIgnoreCase))
{
    var target = VerificationTarget(args);
    return target == null ? 1 : VerifySignOff(args[1], target.Value);
}

if (args.Length >= 2 && args[0].Equals("verifyregrade", StringComparison.OrdinalIgnoreCase))
{
    var target = VerificationTarget(args);
    return target == null ? 1 : VerifyRegrade(args[1], target.Value);
}

if (args.Length >= 2 && args[0].Equals("addtosolution", StringComparison.OrdinalIgnoreCase))
{
    return AddToSolution(args[1], args.Length > 2 ? args[2] : "OutcomeTesting");
}

if (args.Length >= 2 && args[0].Equals("grantsecurity", StringComparison.OrdinalIgnoreCase))
{
    return GrantSecurity(args[1]);
}

if (args.Length >= 3 && args[0].Equals("registertype", StringComparison.OrdinalIgnoreCase))
{
    return RegisterType(args[1], args[2]);
}

if (args.Length >= 2 && args[0].Equals("createnotificationtable", StringComparison.OrdinalIgnoreCase))
{
    return CreateNotificationTable(args[1], args.Length > 2 ? args[2] : "OutcomeTesting");
}

if (args.Length >= 2 && args[0].Equals("addmemocolumn", StringComparison.OrdinalIgnoreCase))
{
    return AddMemoColumn(args);
}

if (args.Length >= 2 && args[0].Equals("addchoicecolumn", StringComparison.OrdinalIgnoreCase))
{
    return AddChoiceColumn(args);
}

if (args.Length >= 2 && args[0].Equals("setstepfilter", StringComparison.OrdinalIgnoreCase))
{
    return SetStepFilter(args);
}

if (args.Length >= 3 && args[0].Equals("restoretablepermissions", StringComparison.OrdinalIgnoreCase))
{
    return RestoreTablePermissions(args[1], args[2]);
}

if (args.Length >= 6 && args[0].Equals("registerstep", StringComparison.OrdinalIgnoreCase))
{
    // Stage before filtering attributes: an empty trailing argument is dropped by the
    // shell, which silently shifted the stage into the attribute list when it was last.
    int stageArg;
    if (!int.TryParse(args[5], out stageArg) || (stageArg != 20 && stageArg != 40))
    {
        Console.Error.WriteLine("Stage must be 20 (pre-operation) or 40 (post-operation).");
        return 1;
    }

    // Mode and run-as are optional and trail the existing arguments, so every registerstep
    // command already in the deployment notes keeps working unchanged.
    var modeArg = args.Length > 7 ? args[7] : "sync";
    if (!modeArg.Equals("sync", StringComparison.OrdinalIgnoreCase)
        && !modeArg.Equals("async", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Mode must be 'sync' or 'async'.");
        return 1;
    }

    return RegisterStep(
        args[1], args[2], args[3], args[4], args.Length > 6 ? args[6] : string.Empty, stageArg,
        modeArg.Equals("async", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
        args.Length > 8 ? args[8] : null);
}

if (args.Length >= 2 && args[0].Equals("addsitetosolution", StringComparison.OrdinalIgnoreCase))
{
    return AddSiteToSolution(args[1], args.Length > 2 ? args[2] : SolutionUniqueName);
}

if (args.Length >= 4 && args[0].Equals("repointwebpage", StringComparison.OrdinalIgnoreCase))
{
    return RepointWebPage(args[1], args[2], args[3]);
}

if (args.Length >= 4 && args[0].Equals("setwebroleauth", StringComparison.OrdinalIgnoreCase))
{
    return SetWebRoleAuth(args[1], args[2], args[3]);
}

if (args.Length >= 3 && args[0].Equals("deletewebrole", StringComparison.OrdinalIgnoreCase))
{
    return DeleteWebRole(args);
}

if (args.Length >= 3 && args[0].Equals("seedadmin", StringComparison.OrdinalIgnoreCase))
{
    return SeedAdmin(args[1], args[2]);
}

if (args.Length >= 4 && args[0].Equals("addcommandvalue", StringComparison.OrdinalIgnoreCase))
{
    if (!int.TryParse(args[2], out var commandValue))
    {
        Console.Error.WriteLine("Usage: dotnet run -- addcommandvalue <orgUrl> <value> <label>");
        return 1;
    }

    return AddCommandValue(args[1], commandValue, args[3]);
}

if (args.Length >= 4 && args[0].Equals("setcasepeople", StringComparison.OrdinalIgnoreCase))
{
    return SetCasePeople(args[1], args[2], args[3], ConfirmedFor(args, args[1]));
}

if (args.Length >= 2 && args[0].Equals("backfilltaxoutcome", StringComparison.OrdinalIgnoreCase))
{
    return BackfillTaxOutcome(args[1], args.Length > 2 && args[2].Equals("--confirm", StringComparison.OrdinalIgnoreCase));
}

if (args.Length >= 5 && args[0].Equals("setattributedescription", StringComparison.OrdinalIgnoreCase))
{
    return SetAttributeDescription(args[1], args[2], args[3], args[4]);
}

if (args.Length >= 6 && args[0].Equals("addoptionvalue", StringComparison.OrdinalIgnoreCase))
{
    if (!int.TryParse(args[4], out var optionValue))
    {
        Console.Error.WriteLine("Usage: dotnet run -- addoptionvalue <orgUrl> <entity> <attribute> <value> <label> [<description>]");
        return 1;
    }

    return AddOptionValue(args[1], args[2], args[3], optionValue, args[5], args.Length >= 7 ? args[6] : null);
}

if (args.Length >= 2 && args[0].Equals("provepp15", StringComparison.OrdinalIgnoreCase))
{
    return ProvePp15(args);
}

if (args.Length >= 2 && args[0].Equals("pp15evidence", StringComparison.OrdinalIgnoreCase))
{
    return Pp15Evidence(args[1]);
}

if (args.Length >= 2 && args[0].Equals("metadatamembership", StringComparison.OrdinalIgnoreCase))
{
    return MetadataMembership(args[1]);
}

if (args.Length >= 3 && args[0].Equals("addmetadatatosolution", StringComparison.OrdinalIgnoreCase))
{
    return AddMetadataToSolution(args[1], args[2], args.Length >= 4 ? args[3] : null);
}

if (args.Length >= 3 && args[0].Equals("fetch", StringComparison.OrdinalIgnoreCase))
{
    return Fetch(args[1], args[2]);
}

if (args.Length >= 3 && args[0].Equals("settracelog", StringComparison.OrdinalIgnoreCase))
{
    return SetTraceLog(args);
}

if (args.Length >= 3 && args[0].Equals("setstepstate", StringComparison.OrdinalIgnoreCase))
{
    return SetStepState(args);
}

if (args.Length >= 2 && args[0].Equals("pushassembly", StringComparison.OrdinalIgnoreCase))
{
    return PushAssembly(args[1], args.Length > 2 ? args[2] : null);
}

if (args.Length >= 4 && args[0].Equals("pushwebtemplate", StringComparison.OrdinalIgnoreCase))
{
    return PushWebTemplate(args[1], args[2], args[3]);
}

if (args.Length >= 4 && args[0].Equals("pushwebfile", StringComparison.OrdinalIgnoreCase))
{
    return PushWebFile(args[1], args[2], args[3], args.Length > 4 ? args[4] : null);
}

if (args.Length >= 3 && args[0].Equals("setchangetracking", StringComparison.OrdinalIgnoreCase))
{
    return SetChangeTracking(args[1], args.Skip(2).ToArray());
}

if (args.Length >= 2 && args[0].Equals("queueroutedcases", StringComparison.OrdinalIgnoreCase))
{
    return QueueRoutedCases(args[1], args.Length > 2 && args[2].Equals("--confirm", StringComparison.OrdinalIgnoreCase));
}

if (args.Length >= 3 && args[0].Equals("requeuecase", StringComparison.OrdinalIgnoreCase))
{
    return RequeueCase(args);
}

if (args.Length >= 2 && args[0].Equals("splitremediation", StringComparison.OrdinalIgnoreCase))
{
    return SplitRemediation(args[1], ConfirmedFor(args, args[1]));
}

if (args.Length >= 2 && args[0].Equals("dropoutcomeactions", StringComparison.OrdinalIgnoreCase))
{
    return DropOutcomeActions(args[1], ConfirmedFor(args, args[1]));
}

if (args.Length >= 2 && args[0].Equals("purgecasedata", StringComparison.OrdinalIgnoreCase))
{
    return PurgeCaseData(args[1], ConfirmedFor(args, args[1]));
}

if (args.Length >= 3 && args[0].Equals("importcases", StringComparison.OrdinalIgnoreCase))
{
    return ImportCasesFile(args[1], args[2], ConfirmedFor(args, args[1]));
}

if (args.Length >= 2 && args[0].Equals("migratetocontacts", StringComparison.OrdinalIgnoreCase))
{
    return MigrateToContacts(args);
}

if (args.Length >= 2 && args[0].Equals("proveexport", StringComparison.OrdinalIgnoreCase))
{
    return ProveExport(args[1]);
}

if (args.Length >= 3 && args[0].Equals("relationships", StringComparison.OrdinalIgnoreCase))
{
    return Relationships(args[1], args[2]);
}

if (args.Length >= 2 && args[0].Equals("probewebrole", StringComparison.OrdinalIgnoreCase))
{
    return ProbeWebRole(args[1]);
}

if (args.Length >= 2 && args[0].Equals("seedwebroles", StringComparison.OrdinalIgnoreCase))
{
    return SeedWebRoles(args);
}

if (args.Length >= 2 && args[0].Equals("proveroles", StringComparison.OrdinalIgnoreCase))
{
    return ProveRoles(args[1]);
}

if (args.Length >= 2 && args[0].Equals("proveadoption", StringComparison.OrdinalIgnoreCase))
{
    return ProveAdoption(args);
}

if (args.Length >= 2 && args[0].Equals("importseed", StringComparison.OrdinalIgnoreCase))
{
    return ImportSeed(args);
}

if (args.Length >= 2 && args[0].Equals("identities", StringComparison.OrdinalIgnoreCase))
{
    return Identities(args[1]);
}

if (args.Length >= 2 && args[0].Equals("bindidentity", StringComparison.OrdinalIgnoreCase))
{
    return BindIdentity(args);
}

if (args.Length >= 2 && args[0].Equals("grantrole", StringComparison.OrdinalIgnoreCase))
{
    return GrantRole(args);
}

if (args.Length >= 2 && args[0].Equals("routecase", StringComparison.OrdinalIgnoreCase))
{
    return RouteCase(args);
}

if (args.Length >= 2 && args[0].Equals("seedcases", StringComparison.OrdinalIgnoreCase))
{
    return SeedCases(args);
}

if (args.Length >= 2 && args[0].Equals("deleterelationship", StringComparison.OrdinalIgnoreCase))
{
    return DeleteRelationship(args);
}

if (args.Length >= 2 && args[0].Equals("deletetable", StringComparison.OrdinalIgnoreCase))
{
    return DeleteTable(args);
}

if (args.Length >= 2 && args[0].Equals("setsitesetting", StringComparison.OrdinalIgnoreCase))
{
    return SetSiteSetting(args);
}

if (args.Length >= 2 && args[0].Equals("setsecuritystamp", StringComparison.OrdinalIgnoreCase))
{
    return SetSecurityStamp(args);
}

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: dotnet run -- <orgUrl> [<pluginDllPath>]   |   dotnet run -- verify <orgUrl>");
    return 1;
}

return Register(args);

// Read-only ad-hoc query. Takes FetchXML inline or as a file path (@path) and prints the
// rows as JSON, so a question about what is actually in an environment can be answered
// from evidence rather than assumption. Refuses anything that is not a <fetch> query, so
// this verb cannot be turned into a write path.
int Fetch(string orgUrl, string fetchXmlOrFile)
{
    var xml = fetchXmlOrFile.StartsWith("@", StringComparison.Ordinal)
        ? File.ReadAllText(fetchXmlOrFile.Substring(1))
        : fetchXmlOrFile;

    if (xml.IndexOf("<fetch", StringComparison.OrdinalIgnoreCase) < 0)
    {
        Console.Error.WriteLine("Not a FetchXML query.");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var results = svc.RetrieveMultiple(new FetchExpression(xml));
    var rows = new List<Dictionary<string, object?>>();
    foreach (var entity in results.Entities)
    {
        var row = new Dictionary<string, object?>();
        foreach (var pair in entity.Attributes)
        {
            object? value = pair.Value switch
            {
                EntityReference reference => reference.Name + " [" + reference.Id.ToString("D") + "]",
                OptionSetValue option => entity.FormattedValues.ContainsKey(pair.Key)
                    ? entity.FormattedValues[pair.Key] + " (" + option.Value + ")"
                    : (object)option.Value,
                Money money => money.Value,
                AliasedValue aliased => aliased.Value is EntityReference alias
                    ? alias.Name + " [" + alias.Id.ToString("D") + "]"
                    : aliased.Value,
                _ => pair.Value,
            };
            row[pair.Key] = value;
        }

        rows.Add(row);
    }

    Console.WriteLine(JsonSerializer.Serialize(
        new { count = rows.Count, moreRecords = results.MoreRecords, rows },
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

// Plug-in trace logging for the environment (organization.plugintracelogsetting).
//
// Off is the default and it is why a plug-in failure has to be diagnosed by reading code
// rather than by reading a log: plugintracelog stays empty, so the ITracingService lines
// PluginBase already writes go nowhere. Exception records a row only when a plug-in
// throws, which is the setting a live environment can carry while something is being
// chased; All records every trace and grows fast.
//
// Not gated behind --confirm: it writes no business data, changes no permission, and is
// reversible by re-running with off.
int SetTraceLog(string[] a)
{
    var orgUrl = a[1];
    var wanted = a[2].Trim().ToLowerInvariant();

    int level;
    switch (wanted)
    {
        case "off": level = 0; break;
        case "exception": level = 1; break;
        case "all": level = 2; break;
        default:
            Console.Error.WriteLine("Usage: dotnet run -- settracelog <orgUrl> <off|exception|all>");
            return 1;
    }

    using var svc = Connect(orgUrl);

    var org = svc.RetrieveMultiple(new QueryExpression("organization")
    {
        ColumnSet = new ColumnSet("name", "plugintracelogsetting"),
        TopCount = 1,
    }).Entities.FirstOrDefault();

    if (org == null)
    {
        Console.Error.WriteLine("No organization row was readable.");
        return 1;
    }

    var before = org.GetAttributeValue<OptionSetValue>("plugintracelogsetting");
    var beforeValue = before?.Value ?? 0;
    var name = org.GetAttributeValue<string>("name");

    if (beforeValue == level)
    {
        Console.WriteLine($"{name}: plug-in trace log is already {Describe(level)}.");
        return 0;
    }

    svc.Update(new Entity("organization", org.Id)
    {
        ["plugintracelogsetting"] = new OptionSetValue(level),
    });

    Console.WriteLine($"{name}: plug-in trace log {Describe(beforeValue)} -> {Describe(level)}.");
    if (level != 0)
    {
        Console.WriteLine("   Reproduce the fault, then read it with:");
        Console.WriteLine("   dotnet run -- fetch <orgUrl> \"<fetch top='5'><entity name='plugintracelog'>" +
            "<attribute name='typename'/><attribute name='messagename'/>" +
            "<attribute name='exceptiondetails'/><attribute name='createdon'/>" +
            "<order attribute='createdon' descending='true'/></entity></fetch>\"");
    }

    return 0;

    static string Describe(int value)
    {
        switch (value)
        {
            case 0: return "Off";
            case 1: return "Exception";
            case 2: return "All";
            default: return value.ToString();
        }
    }
}

// AD-093 backfill. Cases created before the rule sit at Imported or Ready for Allocation with
// a route and no way into the queue except a hand edit. Each one is re-saved through
// al_UpdateCaseDetails with its own route, which the plug-in turns into the Queued hops and an
// audit event, so the trail is the command's, not this tool's. Idempotent: the key is the case
// id, so a re-run replays the original result.
// AD-094. Power Pages renders from a server-side cache and learns of a change made outside
// the website - SubmitRequestPlugin stamping al_submittedon, AnswerWriter creating al_response
// rows, the app moving a case - only through Dataverse change tracking. With it off, a
// submitted review reloaded as still editable with no answers until the 15-minute cache SLA
// ran out. Idempotent: a table already enabled is reported and left alone.
int SetChangeTracking(string orgUrl, string[] tables)
{
    using var svc = Connect(orgUrl);

    var names = tables.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
    var changed = new List<string>();
    foreach (var logicalName in names)
    {
        EntityMetadata metadata;
        try
        {
            metadata = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Entity,
            })).EntityMetadata;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"No table named '{logicalName}': {ex.Message}");
            return 1;
        }

        if (metadata.ChangeTrackingEnabled == true)
        {
            Console.WriteLine($"{logicalName}: change tracking already enabled.");
            continue;
        }

        metadata.ChangeTrackingEnabled = true;
        svc.Execute(new UpdateEntityRequest { Entity = metadata });
        changed.Add(logicalName);
        Console.WriteLine($"{logicalName}: change tracking enabled.");
    }

    if (changed.Count > 0)
    {
        svc.Execute(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><entities>"
                + string.Concat(changed.Select(t => $"<entity>{t}</entity>"))
                + "</entities></importexportxml>",
        });
        Console.WriteLine($"Published {changed.Count} table(s).");
    }

    var failed = 0;
    foreach (var logicalName in names)
    {
        var after = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
        {
            LogicalName = logicalName,
            EntityFilters = EntityFilters.Entity,
        })).EntityMetadata;
        if (after.ChangeTrackingEnabled != true)
        {
            Console.Error.WriteLine($"{logicalName}: metadata does not read back change tracking enabled.");
            failed++;
        }
    }

    Console.WriteLine(failed == 0
        ? $"Done: change tracking on for all {names.Count} table(s)."
        : $"{failed} of {names.Count} table(s) not enabled.");
    return failed == 0 ? 0 : 2;
}

// Splitting the remediation actions raised before a review raised one per item
// (2026-09-10) into the per-item rows the agreed form needs. Read-only without --confirm.
//
// The first item stays on the action that already exists, so the adviser's response, its
// status and any sign-off against it survive; the rest are created Open beside it. The
// original is re-coded with its item number, which is what makes a replayed submit find
// these rows rather than raise the set a second time.
int SplitRemediation(string orgUrl, bool confirm)
{
    using var svc = Connect(orgUrl);

    var actions = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_remediationaction\">" +
        "<attribute name=\"al_remediationactionid\"/><attribute name=\"al_name\"/>" +
        "<attribute name=\"al_remediationactioncode\"/><attribute name=\"al_description\"/>" +
        "<attribute name=\"al_outcomecaseid\"/><attribute name=\"al_reviewinstanceid\"/>" +
        "<attribute name=\"al_actionstatus\"/><attribute name=\"al_duedate\"/>" +
        "<attribute name=\"al_assignedcontactid\"/>" +
        "<order attribute=\"al_remediationactioncode\"/></entity></fetch>")).Entities;

    var work = new List<(Entity Action, List<string> Items, string Context)>();
    foreach (var a in actions)
    {
        // More than one item is the whole criterion, and it is what makes a re-run a no-op:
        // every row this leaves behind carries exactly one. Reading the code instead does
        // not work - a case reference ends in digits of its own ("IO-SEED-TAX-01"), so
        // "REM-IO-SEED-TAX-01-1" looks like it already carries an item number and does not.
        //
        // An action that yields exactly one item is left completely alone, old code and all.
        // A replayed submit for that review would then raise a "-1" beside it rather than
        // recognising it; that is rare enough to accept and visible when it happens, and
        // re-coding rows on a guess is the worse trade.
        var parsed = SplitDescription(a.GetAttributeValue<string>("al_description"));
        if (parsed.Items.Count < 2)
        {
            continue;
        }

        work.Add((a, parsed.Items, parsed.Context));
    }

    Console.WriteLine($"{work.Count} action(s) to split, of {actions.Count} read.");
    foreach (var (a, items, _) in work)
    {
        Console.WriteLine($"   {a.GetAttributeValue<string>("al_remediationactioncode")}  ->  {items.Count} item(s)");
        foreach (var item in items)
        {
            Console.WriteLine($"      - {item}");
        }
    }

    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run with --confirm <orgUrl> to write them.");
        return 0;
    }

    var created = 0;
    var recoded = 0;
    foreach (var (a, items, context) in work)
    {
        var code = a.GetAttributeValue<string>("al_remediationactioncode") ?? string.Empty;

        // The first item keeps the row, and the row keeps everything the adviser has done
        // to it. Only its description narrows to the one item, and its code gains "-1".
        svc.Update(new Entity("al_remediationaction", a.Id)
        {
            ["al_remediationactioncode"] = code + "-1",
            ["al_description"] = DescribeOne(items[0], context),
        });
        recoded++;

        for (var i = 1; i < items.Count; i++)
        {
            var row = new Entity("al_remediationaction")
            {
                ["al_name"] = a.GetAttributeValue<string>("al_name"),
                ["al_remediationactioncode"] = code + "-" + (i + 1),
                ["al_description"] = DescribeOne(items[i], context),
                ["al_actionstatus"] = new OptionSetValue(120910600),
            };

            if (a.Contains("al_outcomecaseid")) { row["al_outcomecaseid"] = a.GetAttributeValue<EntityReference>("al_outcomecaseid"); }
            if (a.Contains("al_reviewinstanceid")) { row["al_reviewinstanceid"] = a.GetAttributeValue<EntityReference>("al_reviewinstanceid"); }
            if (a.Contains("al_duedate")) { row["al_duedate"] = a.GetAttributeValue<DateTime>("al_duedate"); }
            if (a.Contains("al_assignedcontactid")) { row["al_assignedcontactid"] = a.GetAttributeValue<EntityReference>("al_assignedcontactid"); }

            svc.Create(row);
            created++;
        }

        Console.WriteLine($"   {code}: kept item 1, created {items.Count - 1} more.");
    }

    Console.WriteLine($"Done. {recoded} action(s) re-coded, {created} created.");
    return 0;
}

// Imports a CSV extract through the al_ImportCases command, the same command the Code App's
// intake page calls.
//
// Through the command rather than by creating al_outcomecase rows directly, because the
// command is where the behaviour lives: BR-002 validation, the al_importbatch and its
// exceptions, the BR-001 skip of a reference already held, the route derived from the Tax
// check answer and the Tax team disposition (BR-004), and the AD-093 walk that queues a
// routed case on create. Rows written straight into the table would have none of it, which
// is exactly how an environment ends up with data that no rule produced.
//
// The idempotency key is derived from the file's own content, so re-running the same file
// returns the same batch instead of opening a second one. Changing a single character in
// the file makes it a different import, which is what you want when correcting an extract.
int ImportCasesFile(string orgUrl, string csvPath, bool confirm)
{
    if (!confirm)
    {
        Console.Error.WriteLine(
            "This creates cases. Re-run as: importcases <orgUrl> <path to .csv> --confirm <orgUrl>");
        return 1;
    }

    if (!File.Exists(csvPath))
    {
        Console.Error.WriteLine($"No such file: {csvPath}");
        return 1;
    }

    var csv = File.ReadAllText(csvPath);
    var fileName = Path.GetFileName(csvPath);

    string key;
    using (var sha = System.Security.Cryptography.SHA256.Create())
    {
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fileName + "\n" + csv));
        key = "import-" + BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
    }

    using var svc = Connect(orgUrl);

    var request = new OrganizationRequest("al_ImportCases")
    {
        ["FileName"] = fileName,
        ["Csv"] = csv,
        ["IdempotencyKey"] = key,
    };

    Console.WriteLine($"Importing {fileName} ({csv.Length} chars), idempotency key {key}.");

    OrganizationResponse response;
    try
    {
        response = svc.Execute(request);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("The import was refused:");
        Console.Error.WriteLine("  " + FirstLine(ex.Message));
        return 1;
    }

    foreach (var pair in response.Results.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        // Report is a multi-line block; everything else is a scalar.
        var text = pair.Value?.ToString() ?? string.Empty;
        if (text.IndexOf('\n') >= 0)
        {
            Console.WriteLine($"  {pair.Key}:");
            foreach (var line in text.Split('\n'))
            {
                Console.WriteLine("    " + line.TrimEnd('\r'));
            }
        }
        else
        {
            Console.WriteLine($"  {pair.Key}: {text}");
        }
    }

    return 0;
}

// Deletes every row of case data in the environment, and nothing else.
//
// A test environment accumulates cases whose history no longer matches the rules that
// produced it - a route derived under a rule that has since changed, a review instance
// opened for a discipline the route no longer owes - and reasoning about behaviour from
// data like that is worse than having none. This empties the transactional tables so a
// fresh import starts from the rules as they stand today.
//
// The table list is the whole of the safety and it is an allowlist, never a sweep of
// everything beginning al_. What it deliberately leaves standing:
//
//   al_reviewroute                                  the three routes
//   al_question, al_questionversion,
//   al_checklistversion                             the checklist and its versions
//   al_pagepermission, al_userrolemapping           the permission model
//   contact, mspp_webrole, mspp_sitesetting         people, roles and site configuration
//
// Those are not incidental omissions. A case imported into an environment with no route
// derives none (BR-004), and a claim against a case with no checklist version in force is
// refused outright (BR-013), so a purge that took them would leave an environment that
// cannot be re-seeded - the opposite of the point.
//
// The order is the delete order, children before parents: Dataverse refuses to delete a
// row that another row's lookup still points at, so responses go before their review
// instance, actions before their outcome, and the case itself goes last.
//
// Deleted rather than deactivated, unlike everything the commands do. AD-037/OD-010 keeps
// history because history is evidence; there is no evidence here to keep, and a
// deactivated row would still be counted by every query in the solution, none of which
// filters on statecode.
//
// Dry run unless --confirm names the same org twice, which is the pattern every
// destructive verb in this tool already uses.
int PurgeCaseData(string orgUrl, bool confirm)
{
    var tables = new[]
    {
        "al_response",
        "al_signoff",
        "al_remediationaction",
        "al_outcome",
        "al_reviewinstance",
        "al_caseassignment",
        "al_importexception",
        "al_importbatch",
        "al_exportrecord",
        "al_exportbatch",
        "al_notification",
        "al_auditevent",
        "al_outcomecase",
    };

    using var svc = Connect(orgUrl);

    Console.WriteLine(confirm ? "Purging case data." : "Dry run - counting case data.");
    Console.WriteLine();

    var found = new List<(string Table, List<Guid> Ids)>();
    var total = 0;

    foreach (var table in tables)
    {
        List<Guid> ids;
        try
        {
            ids = AllRowIds(svc, table);
        }
        catch (Exception ex)
        {
            // A table the environment never had (al_notification is created by a verb of
            // its own) is not a failure - it holds no case data either way.
            Console.WriteLine($"  {table,-22}      - not present ({FirstLine(ex.Message)})");
            continue;
        }

        found.Add((table, ids));
        total += ids.Count;
        Console.WriteLine($"  {table,-22} {ids.Count,6}");
    }

    Console.WriteLine($"  {"total",-22} {total,6}");

    ReportOrphanRisks(svc, tables);
    Console.WriteLine();

    if (!confirm)
    {
        Console.WriteLine(
            "Nothing was deleted. Re-run as: purgecasedata <orgUrl> --confirm <orgUrl>");
        return 0;
    }

    if (total == 0)
    {
        Console.WriteLine("No case data to delete.");
        return 0;
    }

    var deleted = 0;
    var failures = new List<string>();

    foreach (var (table, ids) in found)
    {
        if (ids.Count == 0) { continue; }

        var before = deleted;
        foreach (var id in ids)
        {
            try
            {
                svc.Delete(table, id);
                deleted++;
            }
            catch (Exception ex)
            {
                failures.Add($"{table} {id:D}: {FirstLine(ex.Message)}");
            }
        }

        Console.WriteLine($"  {table,-22} {deleted - before,6} deleted");
    }

    Console.WriteLine();
    Console.WriteLine($"Deleted {deleted} of {total} rows.");

    if (failures.Count > 0)
    {
        // Reported rather than swallowed: a row that refused to go is usually one a table
        // outside this list still points at, and that is worth seeing by name.
        Console.Error.WriteLine($"{failures.Count} row(s) could not be deleted:");
        foreach (var failure in failures.Take(20))
        {
            Console.Error.WriteLine($"  {failure}");
        }

        if (failures.Count > 20)
        {
            Console.Error.WriteLine($"  ... and {failures.Count - 20} more.");
        }

        return 1;
    }

    return 0;
}

// What still points at the rows about to go.
//
// "Leave no orphaned records" is a property to verify, not to assume. A lookup reaching
// into this list from a table outside it is one of three things, and only the first is
// safe on its own:
//
//   Cascade     the child is deleted with its parent, so nothing is left behind.
//   Restrict    the parent's delete is refused, which the run reports as a failure.
//   RemoveLink  the child survives with a null lookup - an orphan, and the only one of the
//               three that leaves no trace in the run's own output.
//
// So RemoveLink and Restrict are counted here, before anything is deleted, and only where
// rows actually exist. The platform's own bookkeeping tables are skipped by name: they
// reference every table in the environment, they are maintained by the platform rather
// than by this solution, and listing them would bury the rows that matter.
void ReportOrphanRisks(IOrganizationService svc, IReadOnlyCollection<string> tables)
{
    var platform = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "asyncoperation", "bulkdeletefailure", "duplicaterecord", "mailboxtrackingfolder",
        "principalobjectattributeaccess", "processsession", "syncerror",
        "userentityinstancedata", "userentityuisettings", "workflowlog", "solutioncomponent",
        "msdyn_federatedarticleincident", "expiredprocess", "processstageparameter",
        "bulkoperationlog", "slakpiinstance", "elasticfileattachment", "fileattachment",
    };

    var purging = new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase);
    var risks = new List<string>();

    foreach (var table in purging)
    {
        EntityMetadata metadata;
        try
        {
            metadata = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Relationships,
            })).EntityMetadata;
        }
        catch
        {
            // A table this environment does not have; the count pass already said so.
            continue;
        }

        foreach (var relationship in metadata.OneToManyRelationships)
        {
            var child = relationship.ReferencingEntity;
            if (child == null || purging.Contains(child) || platform.Contains(child))
            {
                continue;
            }

            var behaviour = relationship.CascadeConfiguration?.Delete;
            if (behaviour == CascadeType.Cascade)
            {
                continue;
            }

            var count = CountReferencing(svc, child, relationship.ReferencingAttribute);
            if (count == 0)
            {
                continue;
            }

            risks.Add(
                $"  {child}.{relationship.ReferencingAttribute} -> {table}"
                + $"   {count} row(s), on parent delete: {behaviour}");
        }
    }

    Console.WriteLine();

    if (risks.Count == 0)
    {
        Console.WriteLine("Orphan check: nothing outside the purge list points at these rows.");
        return;
    }

    Console.WriteLine("Orphan check - these reference rows being deleted and are NOT in the list:");
    foreach (var risk in risks.OrderBy(r => r, StringComparer.Ordinal))
    {
        Console.WriteLine(risk);
    }
}

// How many rows of a table carry a value in one lookup. ReturnTotalRecordCount caps at
// 5000, which is a signal rather than a census - enough to say whether anything is there.
int CountReferencing(IOrganizationService svc, string table, string attribute)
{
    try
    {
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(false),
            PageInfo = new PagingInfo { Count = 1, PageNumber = 1, ReturnTotalRecordCount = true },
        };
        query.Criteria.AddCondition(attribute, ConditionOperator.NotNull);
        return svc.RetrieveMultiple(query).TotalRecordCount;
    }
    catch
    {
        // A table that cannot be queried (virtual surfaces, and anything this account has
        // no read on) tells us nothing either way; it is not evidence of an orphan.
        return 0;
    }
}

// Every row id in a table, paged. ColumnSet(false) asks for no columns at all - the
// primary key comes back on Entity.Id regardless, which is also what keeps this free of a
// per-table primary key name.
List<Guid> AllRowIds(IOrganizationService svc, string table)
{
    var ids = new List<Guid>();
    var query = new QueryExpression(table)
    {
        ColumnSet = new ColumnSet(false),
        PageInfo = new PagingInfo { Count = 500, PageNumber = 1 },
    };

    while (true)
    {
        var page = svc.RetrieveMultiple(query);
        foreach (var row in page.Entities)
        {
            ids.Add(row.Id);
        }

        if (!page.MoreRecords) { break; }

        query.PageInfo.PageNumber++;
        query.PageInfo.PagingCookie = page.PagingCookie;
    }

    return ids;
}

string FirstLine(string message)
{
    if (string.IsNullOrEmpty(message)) { return string.Empty; }
    var end = message.IndexOfAny(new[] { '\r', '\n' });
    return end < 0 ? message : message.Substring(0, end);
}


// The 2026-09-10 backfill. Remediation.NonPassItems no longer lists the question that
// records the review's own outcome - Q-TAX-02 (Tax check outcome), Q-FQ-01 and Q-FQTAX-01
// (File quality outcome) - because the outcome is the result every other item is a reason
// for, and it is already named in the description's standing sentence. Actions raised
// before that rule still carry it as their one item, and no display change can remove
// them: they are rows, and the portal's fetch does not filter by state.
//
// This clears those rows, and only those: an action whose single item is an outcome
// answer. Deleted rather than deactivated because nothing that reads them filters on
// statecode, so a deactivated one would still be numbered in the table it has to leave.
//
// Except when it is the case's only action. Deleting that one would leave the case in
// Awaiting Remediation with an empty worklist - the state this solution was in before
// anything raised an action at all - so its description is rewritten instead, to the
// standing sentence alone. That is what Remediation.Raise writes today for a review whose
// only non-pass answer was its outcome, so the row ends up in the shape it would have been
// raised in, keeping its code, its clock and its assignment.
//
// Nothing an adviser has touched is deleted. An action carrying a response, a completion,
// any of the three form answers or a sign-off is reported and left alone - the row is
// evidence at that point, and losing it is worse than a stale line on a form. The rest of
// a review's actions are untouched, and the renderers number by position, so the numbering
// closes up on its own.
//
// The outcome wording is read from the environment rather than hard-coded: al_questiontext
// is versioned (BR-013), so what a description was built from is whatever version was in
// force when the review was submitted, and every version of the three questions is
// therefore a wording this has to recognise.
int DropOutcomeActions(string orgUrl, bool confirm)
{
    using var svc = Connect(orgUrl);

    var wordings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var version in svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_questionversion\">" +
        "<attribute name=\"al_questiontext\"/>" +
        "<link-entity name=\"al_question\" from=\"al_questionid\" to=\"al_questionid\">" +
        "<filter type=\"and\"><condition attribute=\"al_questioncode\" operator=\"in\">" +
        "<value>Q-TAX-02</value><value>Q-FQ-01</value><value>Q-FQTAX-01</value>" +
        "</condition></filter></link-entity></entity></fetch>")).Entities)
    {
        var text = version.GetAttributeValue<string>("al_questiontext");
        if (!string.IsNullOrWhiteSpace(text)) { wordings.Add(text.Trim()); }
    }

    if (wordings.Count == 0)
    {
        Console.Error.WriteLine(
            "No version of Q-TAX-02, Q-FQ-01 or Q-FQTAX-01 was found, so an outcome item " +
            "cannot be recognised. Nothing read, nothing written.");
        return 1;
    }

    Console.WriteLine($"Outcome wordings in force: {string.Join(", ", wordings)}");

    var actions = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_remediationaction\">" +
        "<attribute name=\"al_remediationactionid\"/><attribute name=\"al_remediationactioncode\"/>" +
        "<attribute name=\"al_description\"/><attribute name=\"al_adviserresponse\"/>" +
        "<attribute name=\"al_completedon\"/><attribute name=\"al_evidencereference\"/>" +
        "<attribute name=\"al_clientcontactrequired\"/><attribute name=\"al_recheckrequired\"/>" +
        "<attribute name=\"al_changesadvice\"/><attribute name=\"al_outcomecaseid\"/>" +
        "<order attribute=\"al_remediationactioncode\"/></entity></fetch>")).Entities;

    // How many actions each case holds, so the last one standing is never deleted.
    var perCase = new Dictionary<Guid, int>();
    foreach (var a in actions)
    {
        var caseRef = a.GetAttributeValue<EntityReference>("al_outcomecaseid");
        if (caseRef == null) { continue; }
        perCase[caseRef.Id] = perCase.TryGetValue(caseRef.Id, out var n) ? n + 1 : 1;
    }

    var signedOff = new HashSet<Guid>();
    foreach (var signoff in svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_signoff\"><attribute name=\"al_remediationactionid\"/></entity></fetch>")).Entities)
    {
        var actionRef = signoff.GetAttributeValue<EntityReference>("al_remediationactionid");
        if (actionRef != null) { signedOff.Add(actionRef.Id); }
    }

    var doomed = new List<Entity>();
    var stripped = new List<Entity>();
    var kept = new List<(Entity Action, string Why)>();
    foreach (var a in actions)
    {
        var items = SplitDescription(a.GetAttributeValue<string>("al_description")).Items;

        // One item, and that item an outcome answer. More than one means the action was
        // never split (splitremediation's job, not this one) and this must not touch it.
        if (items.Count != 1 || !IsOutcomeItem(items[0], wordings))
        {
            continue;
        }

        var why = WorkDoneOn(a, signedOff);
        if (why != null)
        {
            kept.Add((a, why));
            continue;
        }

        var owner = a.GetAttributeValue<EntityReference>("al_outcomecaseid");
        var onlyOne = owner == null || (perCase.TryGetValue(owner.Id, out var count) && count <= 1);
        if (onlyOne) { stripped.Add(a); } else { doomed.Add(a); }
    }

    Console.WriteLine(
        $"{doomed.Count} outcome action(s) to delete and {stripped.Count} to strip, " +
        $"of {actions.Count} read.");

    foreach (var a in doomed)
    {
        Console.WriteLine($"   DELETE {a.GetAttributeValue<string>("al_remediationactioncode")}  " +
            $"{SplitDescription(a.GetAttributeValue<string>("al_description")).Items[0]}");
    }

    foreach (var a in stripped)
    {
        Console.WriteLine($"   STRIP  {a.GetAttributeValue<string>("al_remediationactioncode")}  " +
            $"{SplitDescription(a.GetAttributeValue<string>("al_description")).Items[0]}  " +
            "(the case's only action, so the item goes and the row stays)");
    }

    foreach (var (a, why) in kept)
    {
        Console.WriteLine($"   KEPT   {a.GetAttributeValue<string>("al_remediationactioncode")}  {why}");
    }

    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run with --confirm <orgUrl> to write.");
        return 0;
    }

    foreach (var a in doomed)
    {
        svc.Delete("al_remediationaction", a.Id);
        Console.WriteLine($"   deleted {a.GetAttributeValue<string>("al_remediationactioncode")}");
    }

    foreach (var a in stripped)
    {
        // The context alone, which is the standing sentence and any checker observation -
        // exactly what Remediation.Describe writes when it is given no items.
        var context = SplitDescription(a.GetAttributeValue<string>("al_description")).Context;
        svc.Update(new Entity("al_remediationaction", a.Id) { ["al_description"] = context });
        Console.WriteLine($"   stripped {a.GetAttributeValue<string>("al_remediationactioncode")}");
    }

    Console.WriteLine(
        $"Done. {doomed.Count} deleted, {stripped.Count} stripped, {kept.Count} left alone.");
    return 0;
}

// An item is the outcome when it reads "<question text>: <answer>" for one of the wordings
// the three outcome questions have been issued under. Compared on the wording plus the
// colon so a test point whose text merely starts the same way is not caught.
bool IsOutcomeItem(string item, HashSet<string> wordings)
{
    foreach (var wording in wordings)
    {
        if (item.StartsWith(wording + ":", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

// Why an action must be kept, or null when nothing has been recorded against it.
string? WorkDoneOn(Entity a, HashSet<Guid> signedOff)
{
    if (signedOff.Contains(a.Id)) { return "a sign-off has been recorded against it"; }
    if (a.GetAttributeValue<DateTime?>("al_completedon") != null) { return "the adviser has completed it"; }
    if (!string.IsNullOrWhiteSpace(a.GetAttributeValue<string>("al_adviserresponse"))) { return "the adviser has responded on it"; }
    if (!string.IsNullOrWhiteSpace(a.GetAttributeValue<string>("al_evidencereference"))) { return "it carries an evidence reference"; }
    if (a.Contains("al_clientcontactrequired") || a.Contains("al_recheckrequired") || a.Contains("al_changesadvice"))
    {
        return "it carries the adviser's form answers";
    }

    return null;
}

// The item list and the context behind it, read back out of a description
// Remediation.Describe wrote. Mirrors app/src/features/remediation/remediationIssues.ts.
(List<string> Items, string Context) SplitDescription(string description)
{
    var items = new List<string>();
    var rest = new List<string>();

    foreach (var raw in (description ?? string.Empty).Split('\n'))
    {
        var line = raw.Trim();

        if (line.StartsWith("- ", StringComparison.Ordinal))
        {
            var item = line.Substring(2).Trim();
            if (item.Length > 0) { items.Add(item); }
            continue;
        }

        if (line == "Issues found on the check:") { continue; }

        rest.Add(line);
    }

    var context = System.Text.RegularExpressions.Regex
        .Replace(string.Join("\n", rest), "\n{3,}", "\n\n").Trim();

    return (items, context);
}

// One item written back in the shape Remediation.DescribeItem writes, so the split rows
// and the ones raised from now on read the same to every renderer.
string DescribeOne(string item, string context)
{
    var text = "Issues found on the check:\n- " + item;
    if (!string.IsNullOrWhiteSpace(context))
    {
        text += "\n\n" + context;
    }

    return text.Length <= 2000 ? text : text.Substring(0, 2000);
}

int QueueRoutedCases(string orgUrl, bool confirm)
{
    using var svc = Connect(orgUrl);

    var candidates = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_outcomecase\">" +
        "<attribute name=\"al_outcomecaseid\"/><attribute name=\"al_casereference\"/>" +
        "<attribute name=\"al_casestatus\"/><attribute name=\"al_reviewrouteid\"/>" +
        "<filter type=\"and\">" +
        "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\"/>" +
        "<condition attribute=\"al_reviewrouteid\" operator=\"not-null\"/>" +
        "<condition attribute=\"al_casestatus\" operator=\"in\">" +
        "<value>" + CaseStatusImported + "</value><value>" + CaseStatusReadyForAllocation + "</value>" +
        "</condition></filter><order attribute=\"al_casereference\"/></entity></fetch>")).Entities;

    Console.WriteLine($"{candidates.Count} routed case(s) short of the queue.");
    foreach (var c in candidates)
    {
        var route = c.GetAttributeValue<EntityReference>("al_reviewrouteid");
        Console.WriteLine($"   {c.GetAttributeValue<string>("al_casereference")}  status {c.GetAttributeValue<OptionSetValue>("al_casestatus").Value}  route {route.Name}");
    }

    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run with --confirm to queue them.");
        return 0;
    }

    var queued = 0;
    foreach (var c in candidates)
    {
        var reference = c.GetAttributeValue<string>("al_casereference") ?? c.Id.ToString("D");
        try
        {
            svc.Execute(new OrganizationRequest("al_UpdateCaseDetails")
            {
                ["TargetId"] = c.Id.ToString("D"),
                ["IdempotencyKey"] = "QUEUE-ROUTED-" + c.Id.ToString("N"),
                ["RouteId"] = c.GetAttributeValue<EntityReference>("al_reviewrouteid").Id.ToString("D"),
                ["Reason"] = "Queued automatically: route already set (AD-093 backfill)",
            });

            var after = svc.Retrieve("al_outcomecase", c.Id, new ColumnSet("al_casestatus"))
                .GetAttributeValue<OptionSetValue>("al_casestatus");
            var isQueued = after != null && after.Value == CaseStatusQueued;
            Console.WriteLine($"   {reference}: {(isQueued ? "Queued" : "NOT queued, status " + (after?.Value.ToString() ?? "(none)"))}");
            if (isQueued) queued++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"   {reference} FAILED: {ex.Message}");
        }
    }

    Console.WriteLine($"Done: {queued} of {candidates.Count} queued.");
    return queued == candidates.Count ? 0 : 1;
}

// Enables or disables plug-in steps by name:
//   setstepstate <orgUrl> enable|disable "<step name>" ["<step name>" ...]
//
// This exists because a solution import whose package carries SdkMessageProcessingSteps/
// leaves every step in it Disabled unless `pac solution import --activate-plugins` is
// passed. That is how the six al_response steps (ResponseGuard x4, ResponseProgress x2)
// went dark at 08:52 UTC on 2026-09-02: import job 9bdfe47f processed exactly those six
// rows, and nothing recorded it because step audits counted rows without reading state.
// pac has no verb for step state and nothing else in this tool writes it, so this is the
// one sanctioned way to change it - and it reads the state back rather than trusting the
// update returned.
int SetStepState(string[] a)
{
    const string usage = "Usage: setstepstate <orgUrl> enable|disable \"<step name>\" [\"<step name>\" ...]";

    var enable = a[2].Equals("enable", StringComparison.OrdinalIgnoreCase);
    if ((!enable && !a[2].Equals("disable", StringComparison.OrdinalIgnoreCase)) || a.Length < 4)
    {
        Console.Error.WriteLine(usage);
        return 1;
    }

    using var svc = Connect(a[1]);

    var failures = 0;
    foreach (var stepName in a.Skip(3))
    {
        var stepId = FindId(svc, "sdkmessageprocessingstep", ("name", stepName));
        if (stepId == Guid.Empty)
        {
            Console.Error.WriteLine($"No step named '{stepName}'.");
            failures++;
            continue;
        }

        svc.Update(new Entity("sdkmessageprocessingstep", stepId)
        {
            ["statecode"] = new OptionSetValue(enable ? 0 : 1),
            ["statuscode"] = new OptionSetValue(enable ? 1 : 2),
        });

        var after = svc.Retrieve("sdkmessageprocessingstep", stepId, new ColumnSet("statecode"))
            .GetAttributeValue<OptionSetValue>("statecode")?.Value;
        var isEnabled = after == 0;
        if (isEnabled == enable)
        {
            Console.WriteLine($"{(enable ? "enabled" : "disabled")} {stepName} ({stepId:D})");
        }
        else
        {
            Console.Error.WriteLine($"FAILED {stepName} ({stepId:D}): statecode read back as {after}.");
            failures++;
        }
    }

    return failures == 0 ? 0 : 1;
}

// Uploads a new build of the plug-in assembly and nothing else - the equivalent of
// `pac plugin push --type Assembly` (AD-061) for when pac has no valid token. Plug-in types
// and steps are untouched, which is the point: `registerall` also re-upserts every Custom
// API, and a code fix should not have to. Reads the Release build by default.
int PushAssembly(string orgUrl, string? dllPathArg)
{
    var dllPath = Path.GetFullPath(dllPathArg ?? Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..",
        "OutcomeTesting.Plugins", "bin", "Release", "net462", "OutcomeTesting.Plugins.dll"));
    if (!File.Exists(dllPath))
    {
        Console.Error.WriteLine($"Plug-in assembly not found: {dllPath}");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var assemblyId = FindId(svc, "pluginassembly", ("name", AssemblyName));
    if (assemblyId == Guid.Empty)
    {
        Console.Error.WriteLine($"No plug-in assembly named '{AssemblyName}'. Run 'registerall' first.");
        return 1;
    }

    var bytes = File.ReadAllBytes(dllPath);
    svc.Update(new Entity("pluginassembly", assemblyId)
    {
        ["content"] = Convert.ToBase64String(bytes),
    });

    var after = svc.Retrieve("pluginassembly", assemblyId, new ColumnSet("modifiedon", "version"));
    Console.WriteLine(
        $"pushed {AssemblyName} ({assemblyId:D}): {bytes.Length} bytes from {dllPath}, "
        + $"modified {after.GetAttributeValue<DateTime>("modifiedon"):yyyy-MM-dd HH:mm:ss}Z, version {after.GetAttributeValue<string>("version")}");
    return 0;
}

// Writes one web template's source into its Enhanced-data-model component row - the part
// of `pac pages upload` a template fix needs, for when pac has no valid token. The row's
// `content` is JSON whose `source` key holds the Liquid; every other key is preserved by a
// parse-set-serialize round trip rather than rebuilt. The component id is the
// adx_webtemplateid in the template's .webtemplate.yml. CRLF is folded to LF and a BOM
// dropped so the stored source matches what the CLI writes.
int PushWebTemplate(string orgUrl, string componentIdArg, string sourcePath)
{
    if (!Guid.TryParse(componentIdArg, out var componentId))
    {
        Console.Error.WriteLine("Usage: pushwebtemplate <orgUrl> <powerpagecomponentid> <path to .webtemplate.source.html>");
        return 1;
    }

    if (!File.Exists(sourcePath))
    {
        Console.Error.WriteLine($"Source file not found: {sourcePath}");
        return 1;
    }

    var source = File.ReadAllText(sourcePath, Encoding.UTF8).Replace("\r\n", "\n");

    using var svc = Connect(orgUrl);

    var row = svc.Retrieve("powerpagecomponent", componentId, new ColumnSet("name", "powerpagecomponenttype", "content"));
    var type = row.GetAttributeValue<OptionSetValue>("powerpagecomponenttype")?.Value;
    if (type != 8)
    {
        Console.Error.WriteLine($"Component {componentId:D} is type {type}, not a Web Template (8).");
        return 1;
    }

    var content = JsonNode.Parse(row.GetAttributeValue<string>("content") ?? "{}") as JsonObject;
    if (content == null)
    {
        Console.Error.WriteLine("The component's content is not a JSON object.");
        return 1;
    }

    var before = content["source"]?.GetValue<string>() ?? string.Empty;
    content["source"] = source;

    svc.Update(new Entity("powerpagecomponent", componentId)
    {
        ["content"] = content.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
    });

    var after = svc.Retrieve("powerpagecomponent", componentId, new ColumnSet("content", "modifiedon"));
    var stored = (JsonNode.Parse(after.GetAttributeValue<string>("content") ?? "{}") as JsonObject)?["source"]?.GetValue<string>();
    if (stored != source)
    {
        Console.Error.WriteLine("FAILED: the source read back does not match the file.");
        return 1;
    }

    Console.WriteLine(
        $"pushed web template '{row.GetAttributeValue<string>("name")}' ({componentId:D}): "
        + $"{before.Length} -> {source.Length} chars, modified {after.GetAttributeValue<DateTime>("modifiedon"):yyyy-MM-dd HH:mm:ss}Z");
    return 0;
}

// The site's css is a web file; `pac pages upload` is the normal path but pac is token-revoked
// (2026-09-09), and the content is a File column so it needs the block upload (AD-095).
int PushWebFile(string orgUrl, string componentIdArg, string path, string? mimeTypeArg)
{
    if (!Guid.TryParse(componentIdArg, out var componentId))
    {
        Console.Error.WriteLine("Usage: pushwebfile <orgUrl> <powerpagecomponentid> <path> [<mimeType>]");
        return 1;
    }

    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Source file not found: {path}");
        return 1;
    }

    var fileName = Path.GetFileName(path);
    var mimeType = mimeTypeArg ?? Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".css" => "text/css",
        ".js" => "application/javascript",
        ".png" => "image/png",
        _ => "application/octet-stream",
    };

    using var svc = Connect(orgUrl);

    var row = svc.Retrieve("powerpagecomponent", componentId, new ColumnSet("name", "powerpagecomponenttype", "modifiedon"));
    var type = row.GetAttributeValue<OptionSetValue>("powerpagecomponenttype")?.Value;
    if (type != 3)
    {
        Console.Error.WriteLine($"Component {componentId:D} is type {type}, not a Web File (3).");
        return 1;
    }

    var name = row.GetAttributeValue<string>("name");
    var bytes = File.ReadAllBytes(path);
    Console.WriteLine($"pushing web file '{name}' ({componentId:D}): {bytes.Length} bytes from {path}");

    var target = new EntityReference("powerpagecomponent", componentId);
    var init = (InitializeFileBlocksUploadResponse)svc.Execute(new InitializeFileBlocksUploadRequest
    {
        Target = target,
        FileAttributeName = "filecontent",
        FileName = fileName,
    });

    var blockIds = new List<string>();
    const int blockSize = 4 * 1024 * 1024;
    if (bytes.Length == 0)
    {
        var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
        blockIds.Add(blockId);
        svc.Execute(new UploadBlockRequest
        {
            FileContinuationToken = init.FileContinuationToken,
            BlockId = blockId,
            BlockData = Array.Empty<byte>(),
        });
    }
    else
    {
        for (var offset = 0; offset < bytes.Length; offset += blockSize)
        {
            var length = Math.Min(blockSize, bytes.Length - offset);
            var block = new byte[length];
            Array.Copy(bytes, offset, block, 0, length);
            var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N")));
            blockIds.Add(blockId);
            svc.Execute(new UploadBlockRequest
            {
                FileContinuationToken = init.FileContinuationToken,
                BlockId = blockId,
                BlockData = block,
            });
        }
    }

    svc.Execute(new CommitFileBlocksUploadRequest
    {
        FileContinuationToken = init.FileContinuationToken,
        FileName = fileName,
        MimeType = mimeType,
        BlockList = blockIds.ToArray(),
    });

    Entity after;
    try
    {
        after = svc.Retrieve("powerpagecomponent", componentId, new ColumnSet("filecontent_name", "modifiedon"));
    }
    catch
    {
        after = svc.Retrieve("powerpagecomponent", componentId, new ColumnSet("modifiedon"));
    }

    Console.WriteLine(
        $"pushed web file '{name}' ({componentId:D}): "
        + $"{bytes.Length} bytes, modified {after.GetAttributeValue<DateTime>("modifiedon"):yyyy-MM-dd HH:mm:ss}Z");
    return 0;
}

// One-off DEV migration onto the contact registry. DELETES al_user rows, REWRITES the
// adviser and paraplanner on every case, ALLOCATES cases and CLOSES some of them, and the
// Audit Events those commands write are immutable (NFR-AUD-01). The org URL is repeated
// after --confirm for the same reason the verify modes repeat it: this must not happen by
// muscle memory.
int MigrateToContacts(string[] a)
{
    var orgUrl = a[1];
    var confirmed = a.Length >= 4
        && a[2].Equals("--confirm", StringComparison.OrdinalIgnoreCase)
        && a[3].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    if (!confirmed)
    {
        Console.Error.WriteLine(
            "This rewrites business data. Re-run as: migratetocontacts <orgUrl> --confirm <orgUrl>");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var whoAmI = (WhoAmIResponse)svc.Execute(new WhoAmIRequest());
    var caller = svc.Retrieve("systemuser", whoAmI.UserId, new ColumnSet("internalemailaddress"));
    var callerEmail = (caller.GetAttributeValue<string>("internalemailaddress") ?? string.Empty).Trim();
    Console.WriteLine($"Running as {callerEmail}.");
    Console.WriteLine();

    return ContactsMigration.Run(svc, callerEmail);
}

// Runs the Trail Light export the way the Exports page does — CreateExportBatch then
// GenerateExport — and reports the row count and the records actually written. This is
// the evidence that the Download control has something to download; the control disables
// itself on an empty row set, so a batch of 0 was never a UI defect.
int ProveExport(string orgUrl)
{
    using var svc = Connect(orgUrl);

    var closed = new QueryExpression("al_outcomecase")
    {
        ColumnSet = new ColumnSet("al_casereference"),
        Criteria = new FilterExpression(),
    };
    closed.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
    closed.Criteria.AddCondition("al_casestatus", ConditionOperator.Equal, 120910591);
    var closedCases = svc.RetrieveMultiple(closed).Entities;
    Console.WriteLine($"Closed cases available to the export: {closedCases.Count}");

    var createKey = "PROVE-EXPORT-CREATE-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var created = svc.Execute(new OrganizationRequest("al_CreateExportBatch")
    {
        ["IdempotencyKey"] = createKey,
    });
    var batchId = (string)created["BatchId"];
    Console.WriteLine($"Created draft batch {batchId}.");

    var generated = svc.Execute(new OrganizationRequest("al_GenerateExport")
    {
        ["BatchId"] = batchId,
        ["IdempotencyKey"] = "PROVE-EXPORT-GEN-" + batchId,
    });
    var rowCount = (string)generated["RowCount"];
    Console.WriteLine($"Generated: RowCount={rowCount}, Status={generated["Status"]}");

    var records = new QueryExpression("al_exportrecord")
    {
        ColumnSet = new ColumnSet("al_advisername", "al_advicequalitygrade", "al_filequalitygrade", "al_clientname"),
        Criteria = new FilterExpression(),
    };
    records.Criteria.AddCondition("al_exportbatchid", ConditionOperator.Equal, new Guid(batchId));
    var rows = svc.RetrieveMultiple(records).Entities;

    Console.WriteLine($"Export records written: {rows.Count}");
    foreach (var row in rows)
    {
        Console.WriteLine(
            "  adviser=" + (row.GetAttributeValue<string>("al_advisername") ?? "(blank)")
            + " adviceGrade=" + (row.GetAttributeValue<string>("al_advicequalitygrade") ?? "(blank)")
            + " fileQuality=" + (row.GetAttributeValue<string>("al_filequalitygrade") ?? "(blank)"));
    }

    if (rows.Count == 0)
    {
        Console.Error.WriteLine("PROVE EXPORT: FAIL - the batch is empty, so Download stays disabled.");
        return 2;
    }

    Console.WriteLine("PROVE EXPORT: PASS - the batch has rows, so Download is enabled.");
    return 0;
}

// Read-only. Prints a table's many-to-many relationships, which is the only reliable way
// to learn an intersect entity's logical name — guessing at it costs a round trip per
// guess and the name differs between the classic (adx_) and enhanced (mspp_) portal
// data models.
int Relationships(string orgUrl, string entity)
{
    using var svc = Connect(orgUrl);

    var response = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Relationships,
    });

    Console.WriteLine($"{entity} many-to-many:");
    foreach (var many in response.EntityMetadata.ManyToManyRelationships)
    {
        Console.WriteLine(
            "  " + many.SchemaName
            + "  intersect=" + many.IntersectEntityName
            + "  " + many.Entity1LogicalName + "." + many.Entity1IntersectAttribute
            + " <-> " + many.Entity2LogicalName + "." + many.Entity2IntersectAttribute);
    }

    /*
     * The lookups, and — the reason this block exists — the navigation property name each
     * one is addressed by.
     *
     * `<attribute>@odata.bind` does NOT take the lookup's logical name. It takes the
     * referencing navigation property, which carries the relationship's own casing and is
     * case-sensitive. Guessing it from the logical name produces a payload the Web API
     * rejects as malformed, and the portal answers 400 with nothing that names the
     * property. Two portal write paths were built on that guess.
     */
    Console.WriteLine($"{entity} many-to-one (lookups):");
    foreach (var one in response.EntityMetadata.ManyToOneRelationships.OrderBy(r => r.ReferencingAttribute))
    {
        Console.WriteLine(
            "  " + one.ReferencingAttribute
            + "  -> " + one.ReferencedEntity
            + "  @odata.bind name=" + one.ReferencingEntityNavigationPropertyName
            + "  (" + one.SchemaName + ")");
    }

    return 0;
}

// Throwaway feasibility probe: can a web role be created, renamed, associated to a contact
// and cleaned up through the SDK? mspp_webrole is a typed surface over powerpagecomponent
// in the enhanced portal data model, and whether it accepts writes decides whether roles
// can be managed from the app at all. Everything it creates, it deletes.
int ProbeWebRole(string orgUrl)
{
    using var svc = Connect(orgUrl);

    // The website table is not directly queryable in the enhanced model, so the reference
    // is lifted off a web role that already exists rather than looked up by name.
    // FetchXML, not QueryExpression: mspp_webrole is a virtual surface over
    // powerpagecomponent and returns nothing to a plain QueryExpression here.
    var sample = svc.RetrieveMultiple(new FetchExpression(
        "<fetch top='1'><entity name='mspp_webrole'><attribute name='mspp_websiteid'/></entity></fetch>"))
        .Entities.FirstOrDefault();
    Console.WriteLine(sample == null
        ? "  (no sample mspp_webrole row came back)"
        : "  sample attributes: " + string.Join(", ", sample.Attributes.Select(a => a.Key + "=" + (a.Value?.GetType().Name ?? "null"))));

    var website = sample?.GetAttributeValue<EntityReference>("mspp_websiteid")
        ?? new EntityReference("mspp_website", Guid.Parse("b4cfe195-fd15-42e8-94e5-f27bcceaf5fc"));
    Console.WriteLine($"website: {website.Id:D} (logical name {website.LogicalName})");

    var roleId = Guid.Empty;
    try
    {
        roleId = svc.Create(new Entity("mspp_webrole")
        {
            ["mspp_name"] = "ZZ Probe Role (delete me)",
            ["mspp_description"] = "Temporary feasibility probe.",
            ["mspp_websiteid"] = website,
            ["mspp_authenticatedusersrole"] = false,
            ["mspp_anonymoususersrole"] = false,
        });
        Console.WriteLine($"CREATE mspp_webrole: OK {roleId:D}");

        svc.Update(new Entity("mspp_webrole", roleId) { ["mspp_name"] = "ZZ Probe Role renamed" });
        Console.WriteLine("UPDATE mspp_webrole: OK");

        var contact = svc.RetrieveMultiple(new FetchExpression(
            "<fetch top='1'><entity name='contact'><attribute name='fullname'/></entity></fetch>"))
            .Entities.First();

        svc.Associate(
            "contact",
            contact.Id,
            new Relationship("powerpagecomponent_mspp_webrole_contact"),
            new EntityReferenceCollection { new EntityReference("mspp_webrole", roleId) });
        Console.WriteLine($"ASSOCIATE to {contact.GetAttributeValue<string>("fullname")}: OK");

        svc.Disassociate(
            "contact",
            contact.Id,
            new Relationship("powerpagecomponent_mspp_webrole_contact"),
            new EntityReferenceCollection { new EntityReference("mspp_webrole", roleId) });
        Console.WriteLine("DISASSOCIATE: OK");

        Console.WriteLine("PROBE: PASS - web roles can be managed and assigned through the SDK.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("PROBE FAILED: " + ex.Message.Replace("\r", " ").Replace("\n", " "));
        return 2;
    }
    finally
    {
        if (roleId != Guid.Empty)
        {
            try
            {
                svc.Delete("mspp_webrole", roleId);
                Console.WriteLine("cleanup: probe role deleted.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"cleanup FAILED, delete {roleId:D} by hand: {ex.Message}");
            }
        }
    }
}

// Puts the web role model into effect: mirrors the contact-to-web-role associations into
// al_userrolemapping and seeds the al_pagepermission rules that make a web role mean
// something server-side. WRITES CONFIGURATION, so the org URL is repeated after --confirm.
int SeedWebRoles(string[] a)
{
    var orgUrl = a[1];
    var confirmed = a.Length >= 4
        && a[2].Equals("--confirm", StringComparison.OrdinalIgnoreCase)
        && a[3].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    if (!confirmed)
    {
        Console.Error.WriteLine(
            "This writes access configuration. Re-run as: seedwebroles <orgUrl> --confirm <orgUrl>");
        return 1;
    }

    using var svc = Connect(orgUrl);
    return WebRoleSeed.Run(svc);
}

// Proves the role path end to end through the commands the app calls: assign a web role to
// a contact, confirm the association really exists, then withdraw it and confirm it is
// gone. Leaves the environment as it found it.
int ProveRoles(string orgUrl)
{
    using var svc = Connect(orgUrl);

    const string probeRole = "AL Portal - Planner";
    var contact = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name='contact'><attribute name='fullname'/><attribute name='emailaddress1'/>" +
        "<filter><condition attribute='emailaddress1' operator='not-null'/></filter></entity></fetch>"))
        .Entities.FirstOrDefault();
    if (contact == null)
    {
        Console.Error.WriteLine("No contact with a work email to test with.");
        return 1;
    }

    var email = contact.GetAttributeValue<string>("emailaddress1").Trim();
    Console.WriteLine($"Test subject: {contact.GetAttributeValue<string>("fullname")} <{email}>");

    var held = RolesOf(svc, contact.Id);
    Console.WriteLine($"  roles before: {(held.Count == 0 ? "(none)" : string.Join(", ", held))}");
    if (held.Contains(probeRole))
    {
        Console.Error.WriteLine($"  {probeRole} is already held; pick a different probe role.");
        return 1;
    }

    var assign = svc.Execute(new OrganizationRequest("al_AssignUserRole")
    {
        ["UserEmail"] = email,
        ["RoleCode"] = probeRole,
        // Sent empty rather than omitted: the platform's request validator refuses a call
        // that leaves an optional Custom API parameter out entirely.
        ["AppRole"] = string.Empty,
        ["IdempotencyKey"] = "PROVE-ROLES-ASSIGN-" + Guid.NewGuid().ToString("N"),
    });
    var mappingId = (string)assign["MappingId"];
    Console.WriteLine($"  al_AssignUserRole -> mapping {mappingId}");

    var after = RolesOf(svc, contact.Id);
    Console.WriteLine($"  roles after assign: {string.Join(", ", after)}");
    var granted = after.Contains(probeRole);
    Console.WriteLine(granted
        ? "  ASSIGN: PASS - the web role association exists, not just a mapping row."
        : "  ASSIGN: FAIL - the mapping row was written but no association was made.");

    svc.Execute(new OrganizationRequest("al_SetRoleAssignmentActive")
    {
        ["MappingId"] = mappingId,
        ["Active"] = false,
        ["IdempotencyKey"] = "PROVE-ROLES-WITHDRAW-" + Guid.NewGuid().ToString("N"),
    });

    var withdrawn = RolesOf(svc, contact.Id);
    Console.WriteLine($"  roles after withdraw: {(withdrawn.Count == 0 ? "(none)" : string.Join(", ", withdrawn))}");
    var removed = !withdrawn.Contains(probeRole);
    Console.WriteLine(removed
        ? "  WITHDRAW: PASS - the association was removed, not just greyed out."
        : "  WITHDRAW: FAIL - the person still holds the web role.");

    // Clean up the mirror row the probe created, so the environment is left as found.
    try { svc.Delete("al_userrolemapping", new Guid(mappingId)); Console.WriteLine("  cleanup: probe mapping deleted."); }
    catch (Exception ex) { Console.Error.WriteLine("  cleanup failed: " + ex.Message); }

    return granted && removed ? 0 : 2;
}

List<string> RolesOf(IOrganizationService svc, Guid contactId)
{
    var fetch =
        "<fetch><entity name='contact'>" +
          "<filter><condition attribute='contactid' operator='eq' value='" + contactId.ToString("D") + "'/></filter>" +
          "<link-entity name='powerpagecomponent_mspp_webrole_contact' from='contactid' to='contactid' intersect='true'>" +
            "<link-entity name='powerpagecomponent' from='powerpagecomponentid' to='powerpagecomponentid' alias='role'>" +
              "<attribute name='name'/>" +
            "</link-entity>" +
          "</link-entity>" +
        "</entity></fetch>";

    return svc.RetrieveMultiple(new FetchExpression(fetch)).Entities
        .Select(r => (r.GetAttributeValue<AliasedValue>("role.name")?.Value as string ?? string.Empty).Trim())
        .Where(n => n.Length > 0)
        .Distinct()
        .ToList();
}

// Deletes a custom table.
//
// The most destructive thing in this tool, and the checks are the point: the table has to
// exist, be custom, be unmanaged, and hold no rows. A table with rows in it is somebody's
// data whatever a register says it is.
//
// It will not check the one thing that actually breaks an environment — whether deployed
// code still reads the table. Dataverse refuses a delete that would orphan a relationship
// or a dependent component, but a plug-in issuing RetrieveMultiple against a table that has
// gone is not a dependency it can see; it is a fault at run time, on whatever path happens
// to read first. Retire the read, deploy that, and only then delete.
int DeleteTable(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 3 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This deletes a table and cannot be undone. Re-run as: " +
            "deletetable <orgUrl> <logicalName> --confirm <orgUrl>");
        return 1;
    }

    var logicalName = a[2].Trim().ToLowerInvariant();

    using var svc = Connect(orgUrl);

    EntityMetadata metadata;
    try
    {
        metadata = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
        {
            LogicalName = logicalName,
            EntityFilters = EntityFilters.Entity,
        })).EntityMetadata;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"No table named '{logicalName}': {ex.Message}");
        return 1;
    }

    Console.WriteLine(
        $"{logicalName}: custom={metadata.IsCustomEntity}, managed={metadata.IsManaged}");

    if (metadata.IsManaged == true)
    {
        Console.Error.WriteLine("It is managed here, so only the solution that owns it can remove it.");
        return 1;
    }

    if (metadata.IsCustomEntity != true)
    {
        Console.Error.WriteLine("It is a system table, not a custom one. Refusing.");
        return 1;
    }

    var withRows = a.Any(x => x.Equals("--with-rows", StringComparison.OrdinalIgnoreCase));

    var rows = svc.RetrieveMultiple(new QueryExpression(logicalName)
    {
        ColumnSet = new ColumnSet(true),
    }).Entities;

    if (rows.Count > 0 && !withRows)
    {
        Console.Error.WriteLine(
            $"'{logicalName}' holds {rows.Count} row(s). Refusing — deleting the table deletes them. " +
            "Re-run with --with-rows if destroying them is the intent.");
        return 1;
    }

    if (rows.Count > 0)
    {
        // Printed in full before anything is destroyed, so the run's own output is the
        // record of what was there. A table worth deleting is usually one nobody can
        // describe any more, and "it held eleven rows" is not a description.
        var primaryName = metadata.PrimaryNameAttribute;
        Console.WriteLine($"  destroying {rows.Count} row(s):");
        foreach (var row in rows)
        {
            var label = primaryName != null ? row.GetAttributeValue<string>(primaryName) : null;
            Console.WriteLine($"    {row.Id:D}  {label ?? "(no name)"}");
        }
    }
    else
    {
        Console.WriteLine("  it holds no rows.");
    }

    // Ask what still points at it before trying, rather than reading the platform's
    // refusal afterwards. "referenced by 1 other component" names a GUID and a type code
    // and leaves the rest as an exercise; a delete that is going to be refused should say
    // what to go and remove.
    var dependencies = ((RetrieveDependenciesForDeleteResponse)svc.Execute(
        new RetrieveDependenciesForDeleteRequest { ObjectId = metadata.MetadataId ?? Guid.Empty, ComponentType = 1 }))
        .EntityCollection.Entities;

    if (dependencies.Count > 0)
    {
        Console.Error.WriteLine($"'{logicalName}' is referenced by {dependencies.Count} component(s):");
        foreach (var d in dependencies)
        {
            var type = d.GetAttributeValue<OptionSetValue>("dependentcomponenttype");
            var id = d.GetAttributeValue<Guid?>("dependentcomponentobjectid");
            Console.Error.WriteLine($"  {DescribeComponent(svc, type?.Value ?? -1, id)}");
        }

        Console.Error.WriteLine("Remove those first. Refusing.");
        return 1;
    }

    svc.Execute(new DeleteEntityRequest { LogicalName = logicalName });
    Console.WriteLine($"Deleted {logicalName}.");

    // Read back rather than trusting the call.
    try
    {
        svc.Execute(new RetrieveEntityRequest
        {
            LogicalName = logicalName,
            EntityFilters = EntityFilters.Entity,
        });
        Console.Error.WriteLine("FAIL: it is still there.");
        return 2;
    }
    catch
    {
        Console.WriteLine("Confirmed gone.");
        return 0;
    }
}

// Turns a solution component type code and id into something a person can act on.
//
// The type codes are the whole difficulty: a dependency reported as "type 26" is a saved
// query and "type 60" a form, and neither is guessable from the number. The common ones are
// named, and anything else prints its code rather than a wrong guess — then the id is
// resolved to a name where the table can be read.
static string DescribeComponent(IOrganizationService svc, int componentType, Guid? id)
{
    var known = new Dictionary<int, (string Label, string Entity, string NameAttr)>
    {
        [1] = ("Table", "entity", null),
        [2] = ("Column", null, null),
        [10] = ("Relationship", null, null),
        [26] = ("View", "savedquery", "name"),
        [59] = ("Chart", "savedqueryvisualization", "name"),
        [60] = ("Form", "systemform", "name"),
        [61] = ("Web resource", "webresource", "name"),
        [90] = ("Plug-in type", "plugintype", "typename"),
        [91] = ("Plug-in assembly", "pluginassembly", "name"),
        [92] = ("SDK message step", "sdkmessageprocessingstep", "name"),
        [93] = ("SDK step image", "sdkmessageprocessingstepimage", "name"),
        [371] = ("Custom API", "customapi", "uniquename"),
        [372] = ("Custom API request parameter", "customapirequestparameter", "uniquename"),
        [373] = ("Custom API response property", "customapiresponseproperty", "uniquename"),
    };

    if (!known.TryGetValue(componentType, out var info))
    {
        return $"component type {componentType}, id {id:D}";
    }

    if (info.Entity == null || info.NameAttr == null || id == null)
    {
        return $"{info.Label}, id {id:D}";
    }

    try
    {
        var row = svc.Retrieve(info.Entity, id.Value, new ColumnSet(info.NameAttr));
        return $"{info.Label} '{row.GetAttributeValue<string>(info.NameAttr)}' ({id:D})";
    }
    catch
    {
        return $"{info.Label}, id {id:D}";
    }
}

// Deletes a custom relationship, and the intersect table behind a many-to-many.
//
// This is a DESTRUCTIVE SCHEMA CHANGE and it is irreversible: the intersect table and every
// row in it go with the relationship, and re-creating it later mints a different table. So
// it refuses on every count it can check first — the relationship has to exist, be custom,
// be unmanaged, and, for a many-to-many, its intersect has to be empty. An intersect with
// rows in it is data somebody is relying on, whatever the register says.
int DeleteRelationship(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 3 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This deletes schema and cannot be undone. Re-run as: " +
            "deleterelationship <orgUrl> <schemaName> --confirm <orgUrl>");
        return 1;
    }

    var schemaName = a[2].Trim();

    using var svc = Connect(orgUrl);

    RelationshipMetadataBase metadata;
    try
    {
        metadata = ((RetrieveRelationshipResponse)svc.Execute(
            new RetrieveRelationshipRequest { Name = schemaName })).RelationshipMetadata;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"No relationship named '{schemaName}': {ex.Message}");
        return 1;
    }

    Console.WriteLine($"{schemaName}: {metadata.RelationshipType}, " +
        $"custom={metadata.IsCustomRelationship}, managed={metadata.IsManaged}");

    if (metadata.IsManaged == true)
    {
        Console.Error.WriteLine("It is managed here, so it can only be removed by the solution that owns it.");
        return 1;
    }

    if (metadata.IsCustomRelationship != true)
    {
        Console.Error.WriteLine("It is a system relationship, not a custom one. Refusing.");
        return 1;
    }

    if (metadata is ManyToManyRelationshipMetadata manyToMany)
    {
        var intersect = manyToMany.IntersectEntityName;
        var rows = svc.RetrieveMultiple(new QueryExpression(intersect)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
        }).Entities.Count;

        if (rows > 0)
        {
            Console.Error.WriteLine(
                $"The intersect '{intersect}' holds rows. Refusing — deleting the relationship deletes them too.");
            return 1;
        }

        Console.WriteLine($"  intersect '{intersect}' is empty.");
    }

    svc.Execute(new DeleteRelationshipRequest { Name = schemaName });
    Console.WriteLine($"Deleted {schemaName}.");

    // Read back rather than trusting the call: a delete that the platform queued rather than
    // applied would otherwise be reported as done.
    try
    {
        svc.Execute(new RetrieveRelationshipRequest { Name = schemaName });
        Console.Error.WriteLine("FAIL: it is still there.");
        return 2;
    }
    catch
    {
        Console.WriteLine("Confirmed gone.");
        return 0;
    }
}

// Finds a web role by name. Separate from FindId because mspp_webrole is a surface over
// powerpagecomponent and does not answer a plain QueryExpression — the shared helper comes
// back empty for a role that plainly exists, which reads as "no such role" and is not.
// The name is escaped because one of the shipped roles is "AL Portal - T&C Supervisor".
static Guid WebRoleId(IOrganizationService svc, string roleName)
{
    var fetch =
        "<fetch><entity name='mspp_webrole'>" +
          "<attribute name='mspp_webroleid'/>" +
          "<filter><condition attribute='mspp_name' operator='eq' value='" +
            System.Security.SecurityElement.Escape(roleName) + "'/></filter>" +
        "</entity></fetch>";

    return svc.RetrieveMultiple(new FetchExpression(fetch)).Entities.FirstOrDefault()?.Id ?? Guid.Empty;
}

// Shared --confirm check: the org URL has to be repeated, so a write against the wrong
// environment cannot happen by muscle memory. Same discipline as the verify modes.
static bool ConfirmedFor(string[] a, string orgUrl)
{
    for (var i = 2; i < a.Length - 1; i++)
    {
        if (a[i].Equals("--confirm", StringComparison.OrdinalIgnoreCase))
        {
            return a[i + 1].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }
    }

    return false;
}

// Read-only: what a sign-in actually reaches. Answers the one question the portal cannot
// be asked from the outside — which contact an external identity resolves to, and what
// that contact may therefore do — by joining the three facts that have to agree:
// adx_externalidentity (the binding), systemuser.azureactivedirectoryobjectid (whose Entra
// account the bound object id really is) and the web role associations.
//
// It exists because reading any one of them alone is what produced the wrong diagnosis:
// a binding names a contact, so it looks settled, while the object id in it can belong to
// an entirely different account from the one the contact is named after.
int Identities(string orgUrl)
{
    using var svc = Connect(orgUrl);

    var users = svc.RetrieveMultiple(new QueryExpression("systemuser")
    {
        ColumnSet = new ColumnSet("fullname", "internalemailaddress", "azureactivedirectoryobjectid"),
    }).Entities;

    Guid OidOf(Entity u) => u.GetAttributeValue<Guid?>("azureactivedirectoryobjectid") ?? Guid.Empty;

    Console.WriteLine("External identities — who a portal sign-in becomes:");
    var identities = svc.RetrieveMultiple(new QueryExpression("adx_externalidentity")
    {
        ColumnSet = new ColumnSet("adx_username", "adx_identityprovidername", "adx_contactid"),
    }).Entities;

    if (identities.Count == 0)
    {
        Console.WriteLine("  (none) — no Entra sign-in can reach any contact.");
    }

    foreach (var id in identities)
    {
        var username = id.GetAttributeValue<string>("adx_username") ?? string.Empty;
        var contactRef = id.GetAttributeValue<EntityReference>("adx_contactid");
        var owner = Guid.TryParse(username, out var oid)
            ? users.FirstOrDefault(u => OidOf(u) == oid)
            : null;

        Console.WriteLine($"  {username}");
        Console.WriteLine($"    provider : {id.GetAttributeValue<string>("adx_identityprovidername")}");
        Console.WriteLine($"    Entra    : {(owner == null ? "not a systemuser in this environment" : owner.GetAttributeValue<string>("internalemailaddress"))}");
        Console.WriteLine($"    contact  : {contactRef?.Name ?? "(none)"}");

        if (contactRef != null)
        {
            var roles = RolesOf(svc, contactRef.Id);
            Console.WriteLine($"    roles    : {(roles.Count == 0 ? "(none)" : string.Join(", ", roles))}");
        }

        // The crossing that is invisible when either half is read on its own.
        if (owner != null && contactRef != null)
        {
            var contact = svc.Retrieve("contact", contactRef.Id, new ColumnSet("emailaddress1"));
            var contactEmail = (contact.GetAttributeValue<string>("emailaddress1") ?? string.Empty).Trim();
            var userEmail = (owner.GetAttributeValue<string>("internalemailaddress") ?? string.Empty).Trim();
            if (contactEmail.Length > 0
                && !contactEmail.Equals(userEmail, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"    NOTE     : the bound Entra account ({userEmail}) is not the contact it resolves to ({contactEmail}).");
            }
        }

        Console.WriteLine();
    }

    Console.WriteLine("Contacts with no binding — their roles are unreachable by Entra sign-in:");
    var bound = identities
        .Select(i => i.GetAttributeValue<EntityReference>("adx_contactid")?.Id ?? Guid.Empty)
        .ToHashSet();

    foreach (var c in svc.RetrieveMultiple(new QueryExpression("contact")
             {
                 ColumnSet = new ColumnSet("fullname", "emailaddress1"),
             }).Entities.Where(c => !bound.Contains(c.Id)))
    {
        var roles = RolesOf(svc, c.Id);
        Console.WriteLine(
            $"  {c.GetAttributeValue<string>("fullname")} <{c.GetAttributeValue<string>("emailaddress1")}> — " +
            (roles.Count == 0 ? "(no roles)" : string.Join(", ", roles)));
    }

    return 0;
}

// Binds one Entra object id to one contact, so that person's own sign-in reaches their own
// contact and the roles granted to it.
//
// This CHANGES WHO A REAL PERSON IS on the portal, which is why the contact is named by
// email rather than picked and the org URL is repeated after --confirm.
//
// The identity provider is copied from a binding that already works rather than typed:
// the issuer has to match the site's provider exactly, and a plausible-looking wrong value
// produces a sign-in that silently reaches nobody. If the environment has no binding to
// copy from, it says so instead of guessing.
int BindIdentity(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 4 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This changes who a real person is on the portal. Re-run as: " +
            "bindidentity <orgUrl> <entraObjectId> <contactEmail> --confirm <orgUrl>");
        return 1;
    }

    if (!Guid.TryParse(a[2], out var objectId))
    {
        Console.Error.WriteLine($"'{a[2]}' is not an Entra object id (a GUID).");
        return 1;
    }

    var email = a[3].Trim();
    var repoint = a.Any(x => x.Equals("--repoint", StringComparison.OrdinalIgnoreCase));

    using var svc = Connect(orgUrl);

    var contactId = FindId(svc, "contact", ("emailaddress1", email));
    if (contactId == Guid.Empty)
    {
        Console.Error.WriteLine($"No contact has the email {email}.");
        return 1;
    }

    var existing = svc.RetrieveMultiple(new QueryExpression("adx_externalidentity")
    {
        ColumnSet = new ColumnSet("adx_username", "adx_identityprovidername", "adx_contactid"),
    }).Entities;

    var already = existing.FirstOrDefault(e =>
        Guid.TryParse(e.GetAttributeValue<string>("adx_username"), out var u) && u == objectId);
    if (already != null)
    {
        var who = already.GetAttributeValue<EntityReference>("adx_contactid");
        if (who != null && who.Id == contactId)
        {
            // Not "nothing to do": the binding can point here while the identity username
            // this object id signs in as still sits on the contact it was moved away from.
            // A repoint written before that was understood leaves exactly that state, and
            // it is invisible from adx_externalidentity alone, so re-running the command
            // has to be able to repair it rather than report success and change nothing.
            var repaired = MoveIdentityUsername(svc, objectId, contactId);
            Console.WriteLine($"Already bound: {objectId:D} -> {who.Name}.");
            Console.WriteLine(repaired
                ? "  Identity username reconciled onto that contact."
                : "  Identity username already sits on that contact. Nothing to do.");
            return 0;
        }

        // Repointing takes a sign-in away from whoever holds it today, which is a
        // different act from granting one and is not something to do by re-running a
        // command that reads as "add". Hence its own flag rather than an overwrite.
        if (!repoint)
        {
            Console.Error.WriteLine(
                $"{objectId:D} is already bound to {who?.Name ?? "(no contact)"}. " +
                "Re-run with --repoint to move it, which takes that sign-in away from them.");
            return 2;
        }

        svc.Update(new Entity("adx_externalidentity", already.Id)
        {
            ["adx_contactid"] = new EntityReference("contact", contactId),
        });

        // The binding is only half of a sign-in. Power Pages also keeps the ASP.NET Identity
        // username on the contact, and moving the binding without moving that leaves the
        // object id signing in as one contact while its username belongs to another — which
        // presents as a generic error page after a successful authentication, not as a
        // refusal, so nothing about it points back here.
        MoveIdentityUsername(svc, objectId, contactId);

        var moved = svc.Retrieve("contact", contactId, new ColumnSet("fullname"));
        Console.WriteLine(
            $"Repointed {objectId:D}: {who?.Name ?? "(no contact)"} -> {moved.GetAttributeValue<string>("fullname")} <{email}>.");
        Console.WriteLine($"  roles now reachable by that sign-in: {string.Join(", ", RolesOf(svc, contactId))}");

        // Said out loud because it is the half of a repoint nobody asks for and
        // everybody needs: what the previous contact can no longer be reached by.
        if (who != null)
        {
            var orphanedRoles = RolesOf(svc, who.Id);
            Console.WriteLine(
                $"  {who.Name} is now reachable by no sign-in; it holds: " +
                (orphanedRoles.Count == 0 ? "(no roles)" : string.Join(", ", orphanedRoles)));
        }

        return 0;
    }

    var providers = existing
        .Select(e => (e.GetAttributeValue<string>("adx_identityprovidername") ?? string.Empty).Trim())
        .Where(p => p.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (providers.Count != 1)
    {
        Console.Error.WriteLine(providers.Count == 0
            ? "No existing external identity to copy the provider from, so the issuer cannot be established. Bind one through the portal first."
            : $"{providers.Count} different providers are in use, so which one this binding needs is not decidable here.");
        return 1;
    }

    var provider = providers[0];
    var contact = svc.Retrieve("contact", contactId, new ColumnSet("fullname"));

    var newId = svc.Create(new Entity("adx_externalidentity")
    {
        ["adx_username"] = objectId.ToString("D"),
        ["adx_identityprovidername"] = provider,
        ["adx_contactid"] = new EntityReference("contact", contactId),
    });

    // Power Pages sets the identity username to the object id itself when it creates a
    // contact from an external sign-in — that is where the one on this site came from — so
    // a binding made by hand matches what the platform would have written rather than
    // leaving the contact half-formed.
    MoveIdentityUsername(svc, objectId, contactId);

    Console.WriteLine($"Bound {objectId:D} -> {contact.GetAttributeValue<string>("fullname")} <{email}>.");
    Console.WriteLine($"  provider     : {provider}");
    Console.WriteLine($"  identity row : {newId:D}");
    Console.WriteLine($"  roles now reachable by that sign-in: {string.Join(", ", RolesOf(svc, contactId))}");
    return 0;
}

// Gives one contact an ASP.NET Identity security stamp, which is the value Power Pages
// validates the authentication cookie against.
//
// Power Pages writes this itself for a contact it creates from an external sign-in. A
// contact created by hand — by migratetocontacts, by a seed, or in the maker portal — gets
// an external identity binding and no stamp, and the difference is invisible until someone
// tries to sign in as them.
//
// DELIBERATELY NARROW while what the stamp actually causes is still being established: it
// writes one column, refuses a contact that already has one, and does not touch
// adx_identity_lockoutenabled or anything else that differs between a hand-made contact and
// one Power Pages built. Widening it before the single-column change is shown to be the one
// that matters would make the next result unreadable.
int SetSecurityStamp(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 3 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This changes whether a real person can sign in. Re-run as: " +
            "setsecuritystamp <orgUrl> <contactEmail> --confirm <orgUrl>");
        return 1;
    }

    var email = a[2].Trim();

    using var svc = Connect(orgUrl);

    var contactId = FindId(svc, "contact", ("emailaddress1", email));
    if (contactId == Guid.Empty)
    {
        Console.Error.WriteLine($"No contact has the email {email}.");
        return 1;
    }

    var contact = svc.Retrieve(
        "contact", contactId, new ColumnSet("fullname", "adx_identity_securitystamp"));
    var existing = contact.GetAttributeValue<string>("adx_identity_securitystamp");

    // Refused rather than reissued: rotating a stamp signs the person out everywhere, and
    // this command exists to fill a gap, not to invalidate sessions.
    if (!string.IsNullOrWhiteSpace(existing))
    {
        Console.WriteLine(
            $"{contact.GetAttributeValue<string>("fullname")} already has a security stamp. Nothing to do.");
        return 0;
    }

    var stamp = Guid.NewGuid().ToString("D");
    svc.Update(new Entity("contact", contactId) { ["adx_identity_securitystamp"] = stamp });

    var after = svc.Retrieve("contact", contactId, new ColumnSet("adx_identity_securitystamp"))
        .GetAttributeValue<string>("adx_identity_securitystamp");

    Console.WriteLine($"{contact.GetAttributeValue<string>("fullname")} <{email}>: stamp set.");
    Console.WriteLine($"  read back: {(string.IsNullOrWhiteSpace(after) ? "(still empty — the write did not land)" : after)}");
    return string.IsNullOrWhiteSpace(after) ? 1 : 0;
}

// Sets one site setting to one value, and reads it back.
//
// `sitesetting.yml` under the site folder is the source of truth for these, and a
// `pac pages upload` is what normally applies it. This exists for the narrower job of
// putting a single setting back to its tracked value without redeploying pages, templates
// and table permissions that are already correct — the blast radius of a full upload on
// this site is documented at the top of Deploy-Portal.ps1 and is not worth taking for one
// field. It writes the same value the next upload would, so the two converge rather than
// fighting.
//
// An authentication setting decides who can get into the portal at all, hence --confirm
// and the repeated org URL. Use an empty string to clear a setting.
/// <summary>
/// Adds one multiline text column to an existing table.
///
/// Written for `al_answerrequest`, the second trigger column on `al_reviewinstance`: the
/// portal page PATCHes a payload onto it and a synchronous plug-in does the real write,
/// because the Power Pages Web API refuses the write the page would otherwise make. That
/// pattern now has two instances and will have more, so the column that carries it is
/// worth a command rather than a click in the maker portal — a click leaves nothing behind
/// that says the column was deliberate, and `al_submitrequested` is already in the solution
/// with no record of who added it or why.
///
/// `--confirm`-gated like `setsitesetting`, for the same reason: this writes metadata to a
/// live environment, and metadata changes are not quietly reversible.
///
/// It refuses a column that already exists rather than trying to alter it. Widening a
/// column is a different operation with different consequences, and a command that silently
/// did either would make "it ran clean" mean two different things.
/// </summary>
int AddMemoColumn(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 6 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This writes metadata to a live environment. Re-run as: addmemocolumn <orgUrl> " +
            "<entityLogicalName> <SchemaName> <displayName> <maxLength> [<description>] --confirm <orgUrl>");
        return 1;
    }

    var entity = a[2].Trim();
    var schemaName = a[3].Trim();
    var displayName = a[4];

    int maxLength;
    if (!int.TryParse(a[5], out maxLength) || maxLength < 1 || maxLength > 1048576)
    {
        Console.Error.WriteLine("Max length must be between 1 and 1048576.");
        return 1;
    }

    var description = a.Length > 6 && !a[6].StartsWith("--", StringComparison.Ordinal) ? a[6] : string.Empty;
    var logicalName = schemaName.ToLowerInvariant();

    using var svc = Connect(orgUrl);

    var existing = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    if (existing.EntityMetadata.Attributes.Any(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"'{entity}' already has a column '{logicalName}'. Nothing was changed.");
        return 1;
    }

    svc.Execute(new CreateAttributeRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        EntityName = entity,
        Attribute = new MemoAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = logicalName,
            MaxLength = maxLength,
            Format = StringFormat.TextArea,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            DisplayName = NotificationTable.Text(displayName),
            Description = NotificationTable.Text(description),
        },
    });

    // Read back, because on this project a successful-looking write is not evidence.
    var after = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    var created = after.EntityMetadata.Attributes.FirstOrDefault(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)) as MemoAttributeMetadata;

    if (created == null)
    {
        Console.Error.WriteLine($"'{logicalName}' was not found on '{entity}' after the create returned. Investigate before relying on it.");
        return 1;
    }

    Console.WriteLine(
        $"Created {entity}.{created.LogicalName} (memo, max {created.MaxLength}) in solution {SolutionUniqueName}.");
    return 0;
}

// A picklist column with its own option set (AD-095). Options are given as
// "value:label;value:label"; values are the project's own 1209107xx band, so a typo here
// collides with nothing by accident but is still read back before being trusted.
int AddChoiceColumn(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 6 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This writes metadata to a live environment. Re-run as: addchoicecolumn <orgUrl> " +
            "<entityLogicalName> <SchemaName> <displayName> <value:label;value:label...> [<description>] --confirm <orgUrl>");
        return 1;
    }

    var entity = a[2].Trim();
    var schemaName = a[3].Trim();
    var displayName = a[4];
    var optionsArg = a[5];
    var description = a.Length > 6 && !a[6].StartsWith("--", StringComparison.Ordinal) ? a[6] : string.Empty;
    var logicalName = schemaName.ToLowerInvariant();

    var options = new List<OptionMetadata>();
    foreach (var pair in optionsArg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = pair.Split(new[] { ':' }, 2);
        int value;
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out value) || string.IsNullOrWhiteSpace(parts[1]))
        {
            Console.Error.WriteLine($"Option '{pair}' is not value:label.");
            return 1;
        }

        options.Add(new OptionMetadata(NotificationTable.Text(parts[1].Trim()), value));
    }

    if (options.Count == 0)
    {
        Console.Error.WriteLine("At least one option is required.");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var existing = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    if (existing.EntityMetadata.Attributes.Any(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"'{entity}' already has a column '{logicalName}'. Nothing was changed.");
        return 1;
    }

    var optionSet = new OptionSetMetadata
    {
        IsGlobal = false,
        OptionSetType = OptionSetType.Picklist,
        DisplayName = NotificationTable.Text(displayName),
    };
    foreach (var option in options)
    {
        optionSet.Options.Add(option);
    }

    svc.Execute(new CreateAttributeRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        EntityName = entity,
        Attribute = new PicklistAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = logicalName,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            DisplayName = NotificationTable.Text(displayName),
            Description = NotificationTable.Text(description),
            OptionSet = optionSet,
        },
    });

    var after = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    var created = after.EntityMetadata.Attributes.FirstOrDefault(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)) as PicklistAttributeMetadata;

    if (created == null)
    {
        Console.Error.WriteLine($"'{logicalName}' was not found on '{entity}' after the create returned. Investigate before relying on it.");
        return 1;
    }

    var readBack = created.OptionSet.Options
        .Select(o => $"{o.Value}={o.Label?.UserLocalizedLabel?.Label}")
        .ToList();
    Console.WriteLine($"{entity}.{logicalName} created with options {string.Join(", ", readBack)}.");
    return 0;
}

// Updates the filtering attributes of an existing step by its exact name (AD-095): a guard
// registered on two columns does not fire for a write that carries only a third, so a new
// guarded column is a change to the step, not only to the plug-in.
int SetStepFilter(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 4 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This changes a registered step. Re-run as: setstepfilter <orgUrl> \"<step name>\" <attr,attr,...> --confirm <orgUrl>");
        return 1;
    }

    var stepName = a[2];
    var attributes = a[3].Trim();

    using var svc = Connect(orgUrl);

    var query = new QueryExpression("sdkmessageprocessingstep")
    {
        ColumnSet = new ColumnSet("name", "filteringattributes"),
    };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, stepName);
    var found = svc.RetrieveMultiple(query).Entities;
    if (found.Count != 1)
    {
        Console.Error.WriteLine($"Expected one step named '{stepName}', found {found.Count}.");
        return 1;
    }

    var before = found[0].GetAttributeValue<string>("filteringattributes");
    svc.Update(new Entity("sdkmessageprocessingstep", found[0].Id)
    {
        ["filteringattributes"] = attributes,
    });

    var after = svc.Retrieve("sdkmessageprocessingstep", found[0].Id, new ColumnSet("filteringattributes"))
        .GetAttributeValue<string>("filteringattributes");
    Console.WriteLine($"'{stepName}': filteringattributes '{before}' -> '{after}'.");
    return string.Equals(after, attributes, StringComparison.Ordinal) ? 0 : 2;
}

int SetSiteSetting(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 4 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This can change who is able to sign in. Re-run as: " +
            "setsitesetting <orgUrl> <name> <value> --confirm <orgUrl>");
        return 1;
    }

    var name = a[2].Trim();
    var value = a[3];

    using var svc = Connect(orgUrl);

    var found = svc.RetrieveMultiple(new QueryExpression("mspp_sitesetting")
    {
        ColumnSet = new ColumnSet("mspp_name", "mspp_value"),
        Criteria = new FilterExpression
        {
            Conditions = { new ConditionExpression("mspp_name", ConditionOperator.Equal, name) },
        },
    }).Entities;

    // Refused rather than created: a setting this site does not already carry is far more
    // likely to be a mistyped name than a new one, and a typo that silently creates a row
    // reads as applied while changing nothing about the site's behaviour.
    if (found.Count != 1)
    {
        Console.Error.WriteLine(found.Count == 0
            ? $"No site setting is named '{name}'. Nothing was written."
            : $"{found.Count} site settings are named '{name}', so which one to write is not decidable here.");
        return 1;
    }

    var before = found[0].GetAttributeValue<string>("mspp_value");

    svc.Update(new Entity("mspp_sitesetting", found[0].Id)
    {
        ["mspp_value"] = value.Length == 0 ? null : value,
    });

    // Read back, because on this site a successful-looking write is not evidence.
    var after = svc.Retrieve("mspp_sitesetting", found[0].Id, new ColumnSet("mspp_value"))
        .GetAttributeValue<string>("mspp_value");

    Console.WriteLine($"{name}: {Show(before)} -> {Show(after)}");
    return 0;

    static string Show(string? v) => string.IsNullOrEmpty(v) ? "(empty)" : v;
}

// Puts the ASP.NET Identity username for one Entra object id on the contact that object id
// now signs in as, and takes it off any other contact still holding it.
//
// adx_identity_username is unique across contacts, so the clear has to land before the set
// or the set is refused. That ordering is the whole reason this is one function rather than
// two calls at the call site.
//
// Returns true if anything moved, so a caller that found the binding already correct can
// still say whether it repaired something.
static bool MoveIdentityUsername(ServiceClient svc, Guid objectId, Guid contactId)
{
    var username = objectId.ToString("D");

    var holders = svc.RetrieveMultiple(new QueryExpression("contact")
    {
        ColumnSet = new ColumnSet("adx_identity_username"),
        Criteria = new FilterExpression
        {
            Conditions =
            {
                new ConditionExpression("adx_identity_username", ConditionOperator.Equal, username),
            },
        },
    }).Entities;

    if (holders.Count == 1 && holders[0].Id == contactId)
    {
        return false;
    }

    foreach (var holder in holders.Where(h => h.Id != contactId))
    {
        svc.Update(new Entity("contact", holder.Id) { ["adx_identity_username"] = null });
    }

    svc.Update(new Entity("contact", contactId) { ["adx_identity_username"] = username });
    return true;
}

// Grants one web role to one contact through al_AssignUserRole — the command the app
// itself calls — so the association and the al_userrolemapping mirror are written together
// and the AD-089 conflict rule has nothing to classify.
//
// It differs from proveroles in one way that matters: proveroles withdraws what it granted,
// because it is a probe. This leaves the grant in place, so it GRANTS A REAL PERSON REAL
// PORTAL ACCESS permanently. Hence the named subject and the repeated org URL.
int GrantRole(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 4 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This grants a real person real portal access. Re-run as: " +
            "grantrole <orgUrl> <contactEmail> <roleName> --confirm <orgUrl>");
        return 1;
    }

    var email = a[2].Trim();
    var roleName = a[3].Trim();

    using var svc = Connect(orgUrl);

    var contactId = FindId(svc, "contact", ("emailaddress1", email));
    if (contactId == Guid.Empty)
    {
        Console.Error.WriteLine($"No contact has the email {email}.");
        return 1;
    }

    // Checked before the call so a typo produces "no such role" rather than a mapping row
    // pointing at a role that does not exist — which reads as granted and is not.
    //
    // FetchXML, not FindId: mspp_webrole does not answer a plain QueryExpression, so the
    // shared helper returns "no such role" for every role that exists.
    var roleId = WebRoleId(svc, roleName);
    if (roleId == Guid.Empty)
    {
        Console.Error.WriteLine($"No web role is named '{roleName}'.");
        return 1;
    }

    var before = RolesOf(svc, contactId);
    Console.WriteLine($"roles before: {(before.Count == 0 ? "(none)" : string.Join(", ", before))}");

    var response = svc.Execute(new OrganizationRequest("al_AssignUserRole")
    {
        ["UserEmail"] = email,
        ["RoleCode"] = roleName,
        // Sent empty rather than omitted: the platform's request validator refuses a call
        // that leaves an optional Custom API parameter out entirely.
        ["AppRole"] = string.Empty,
        // Stable per (person, role), so a re-run returns the same mapping instead of a
        // second one.
        ["IdempotencyKey"] = "GRANTROLE-" + email.ToLowerInvariant() + "-" + roleName,
    });

    Console.WriteLine($"  al_AssignUserRole -> mapping {response["MappingId"]}");

    var after = RolesOf(svc, contactId);
    Console.WriteLine($"roles after : {string.Join(", ", after)}");

    // The association is the fact the portal reads; the mapping row alone would leave the
    // grant invisible to the site.
    if (!after.Contains(roleName))
    {
        Console.Error.WriteLine("FAIL: the mapping row was written but no association was made.");
        return 2;
    }

    Console.WriteLine($"PASS: {email} holds '{roleName}' as a web role association.");
    return 0;
}

// Points a case at a review route, and optionally allocates it, so a discipline that has
// never been exercised in an environment can be.
//
// The review instance is NOT written here. Allocating creates an al_caseassignment
// carrying only the case and the contact, exactly as the portal claim does, and
// ClaimCasePlugin then decides the discipline from the route and creates the review. Doing
// it that way is the point: a hand-written review instance would prove the pages render,
// not that routing produces the right check.
//
// It MUTATES A REAL CASE and the Audit Event the claim writes is immutable (NFR-AUD-01),
// so the case is named and the org URL repeated.
// Seeds test cases across all three review routes and allocates each to one person, so a
// reviewer has work of every shape to open (project owner, 2026-09-09).
//
// It creates real al_outcomecase rows and real allocations, and every one of them leaves a
// trail: the queueing rule (AD-093) hops the case to Queued, and the allocation creates an
// al_reviewinstance and moves the case to Assigned. Hence --confirm <orgUrl>, as every other
// write verb here. References are prefixed IO-SEED- so seeded work is obvious in a worklist
// and easy to find again; a reference that already exists is left alone, so a second run
// allocates what the first could not rather than creating a duplicate set.
//
// Routes come from data/route-seed: ROUTE-AQS is AQS only, ROUTE-TAX is Tax only, and
// ROUTE-TAX-AQS is Tax then AQS. A Tax-then-AQS case gets its Tax review here; the AQS leg is
// created by the hand-back when the Tax check is submitted, which is the flow under test
// rather than something to fabricate up front.
int SeedCases(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 3 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This creates real cases, allocations and reviews. Re-run as: " +
            "seedcases <orgUrl> <assigneeEmail> [<perRoute>] --confirm <orgUrl>");
        return 1;
    }

    var assigneeEmail = a[2].Trim();
    var perRoute = 5;
    if (a.Length >= 4 && !a[3].StartsWith("--", StringComparison.Ordinal)
        && !int.TryParse(a[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out perRoute))
    {
        Console.Error.WriteLine($"'{a[3]}' is not a number of cases per route.");
        return 1;
    }

    if (perRoute < 1 || perRoute > 50)
    {
        Console.Error.WriteLine("Cases per route must be between 1 and 50.");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var contactId = FindId(svc, "contact", ("emailaddress1", assigneeEmail));
    if (contactId == Guid.Empty)
    {
        Console.Error.WriteLine($"No contact has the email {assigneeEmail}, so nothing could be allocated.");
        return 1;
    }

    // Route code -> the reference tag, and the discipline whose review should appear first.
    var routes = new[]
    {
        ("ROUTE-AQS", "AQS", "AQS"),
        ("ROUTE-TAX", "TAX", "Tax"),
        ("ROUTE-TAX-AQS", "TXA", "Tax"),
    };

    var created = 0;
    var allocated = 0;
    var skipped = 0;
    var stuck = 0;

    foreach (var (routeCode, tag, firstReview) in routes)
    {
        var routeId = FindId(svc, "al_reviewroute", ("al_routecode", routeCode));
        if (routeId == Guid.Empty)
        {
            Console.Error.WriteLine($"No review route has the code {routeCode}; skipping that set.");
            continue;
        }

        for (var n = 1; n <= perRoute; n++)
        {
            var reference = $"IO-SEED-{tag}-{n:D2}";

            // A reference already seeded is finished rather than duplicated: an earlier run
            // that created the case but could not allocate it leaves exactly that state, and
            // re-running should complete it, not make a second copy.
            var existingId = FindId(svc, "al_outcomecase", ("al_casereference", reference));
            if (existingId != Guid.Empty)
            {
                if (HasActiveAssignment(svc, existingId))
                {
                    Console.WriteLine($"{reference}: already seeded and allocated, left alone.");
                    skipped++;
                    continue;
                }

                if (CaseStatus(svc, existingId) != CaseStatusQueued)
                {
                    svc.Update(new Entity("al_outcomecase", existingId)
                    {
                        ["al_casestatus"] = new OptionSetValue(CaseStatusQueued),
                    });
                }

                if (Allocate(svc, existingId, contactId, reference, routeCode, assigneeEmail, firstReview))
                {
                    allocated++;
                }
                else
                {
                    stuck++;
                }

                continue;
            }

            var seeded = new Entity("al_outcomecase")
            {
                ["al_name"] = reference,
                ["al_casereference"] = reference,
                ["al_clientname"] = $"Seed Client {tag}{n:D2}",
                ["al_advisername"] = $"Seed Adviser {n:D2}",
                ["al_advisercode"] = $"ADV-S{n:D2}",
                ["al_adviserstatus"] = new OptionSetValue(120910501),
                ["al_paraplanner"] = $"Seed Paraplanner {n:D2}",
                ["al_paraplannercode"] = $"PP-S{n:D2}",
                ["al_products"] = "Pension; ISA",
                ["al_casetype"] = new OptionSetValue(120910510),
                ["al_advicedate"] = DateTime.UtcNow.Date.AddDays(-30),
                ["al_productsolutiontype"] = new OptionSetValue(120910521),
                ["al_samplesource"] = new OptionSetValue(120910530),
                ["al_checkername"] = "Seed Checker",
                ["al_checkdate"] = DateTime.UtcNow.Date,
                ["al_preorpostcheck"] = new OptionSetValue(120910540),
                ["al_vulnerableclient"] = new OptionSetValue(120910551),
                ["al_taxcheckrequired"] = new OptionSetValue(
                    routeCode == "ROUTE-AQS" ? 120910561 : 120910560),
                // Queued directly, as provepp15 seeds its case. AD-093's queueing rule lives
                // in the import and case-edit commands (CaseQueueing.QueueIfRouted), not in a
                // step on the table, so a row created straight through the SDK never passes
                // through it and would sit at Imported for ever. A claim is refused on a case
                // that is not Queued, which is the state a checker picks work up from.
                ["al_casestatus"] = new OptionSetValue(CaseStatusQueued),
                ["al_reviewrouteid"] = new EntityReference("al_reviewroute", routeId),
            };

            var caseId = svc.Create(seeded);
            created++;

            if (Allocate(svc, caseId, contactId, reference, routeCode, assigneeEmail, firstReview))
            {
                allocated++;
            }
            else
            {
                stuck++;
            }
        }
    }

    Console.WriteLine(
        $"Done. {created} created, {allocated} allocated, {skipped} already existed, {stuck} left unallocated.");
    return stuck > 0 ? 2 : 0;
}

// Returns one case to the shared queue through al_UpdateCaseDetails, so the move is
// lifecycle-checked (AD-057 allows Assigned -> Queued) and audited like any other status
// change rather than being a silent direct write.
//
// For a case OD-048's rule cannot reach: that rule fires on the edit that CHANGES the route,
// and a case whose route changed before the rule existed is already past it. The active
// assignment is deliberately left as it is - a case returned to Queued for the BR-004
// handoff carries one too, and ClaimCasePlugin releases it on the next claim.
int RequeueCase(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 4 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This moves a real case back to the queue. Re-run as: " +
            "requeuecase <orgUrl> <caseReference> --confirm <orgUrl>");
        return 1;
    }

    var reference = a[2].Trim();

    using var svc = Connect(orgUrl);

    var caseId = FindId(svc, "al_outcomecase", ("al_casereference", reference));
    if (caseId == Guid.Empty)
    {
        Console.Error.WriteLine($"No case has the reference {reference}.");
        return 1;
    }

    var before = CaseStatus(svc, caseId);
    if (before == CaseStatusQueued)
    {
        Console.WriteLine($"{reference}: already Queued, left alone.");
        return 0;
    }

    try
    {
        svc.Execute(new OrganizationRequest("al_UpdateCaseDetails")
        {
            ["TargetId"] = caseId.ToString("D"),
            ["IdempotencyKey"] = "REQUEUE-" + caseId.ToString("N"),
            ["Status"] = CaseStatusQueued.ToString(CultureInfo.InvariantCulture),
            ["Reason"] = "Returned to the queue: the route owes a check that was never opened (OD-048 repair)",
        });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{reference}: refused: {ex.Message}");
        return 1;
    }

    var after = CaseStatus(svc, caseId);
    Console.WriteLine($"{reference}: {before} -> {after}"
        + (after == CaseStatusQueued ? " (Queued)" : " NOT QUEUED"));
    return after == CaseStatusQueued ? 0 : 2;
}

static int CaseStatus(ServiceClient svc, Guid caseId)
{
    return svc.Retrieve("al_outcomecase", caseId, new ColumnSet("al_casestatus"))
        .GetAttributeValue<OptionSetValue>("al_casestatus")?.Value ?? 0;
}

static bool HasActiveAssignment(ServiceClient svc, Guid caseId)
{
    var query = new QueryExpression("al_caseassignment") { ColumnSet = new ColumnSet(false), TopCount = 1 };
    query.Criteria.AddCondition("al_outcomecaseid", ConditionOperator.Equal, caseId);
    query.Criteria.AddCondition("al_isactive", ConditionOperator.Equal, true);
    return svc.RetrieveMultiple(query).Entities.Count > 0;
}

// Allocates one seeded case by creating the claim row ClaimCasePlugin fires on: it resolves
// the contact to its systemuser (AD-010), creates or reuses the al_reviewinstance for the
// discipline the route says comes first, and moves the case to Assigned. Reports what the
// plug-in actually decided rather than what was asked for.
static bool Allocate(
    ServiceClient svc,
    Guid caseId,
    Guid contactId,
    string reference,
    string routeCode,
    string assigneeEmail,
    string firstReview)
{
    try
    {
        svc.Create(new Entity("al_caseassignment")
        {
            ["al_outcomecaseid"] = new EntityReference("al_outcomecase", caseId),
            ["al_assignedcontactid"] = new EntityReference("contact", contactId),
        });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{reference}: created, but the allocation was refused: {ex.Message}");
        return false;
    }

    var reviews = svc.RetrieveMultiple(new QueryExpression("al_reviewinstance")
    {
        ColumnSet = new ColumnSet("al_reviewtype", "al_assignedcontactid"),
        Criteria =
        {
            Conditions = { new ConditionExpression("al_outcomecaseid", ConditionOperator.Equal, caseId) },
        },
    }).Entities;

    var types = string.Join(", ", reviews.Select(r =>
    {
        var t = r.GetAttributeValue<OptionSetValue>("al_reviewtype")?.Value ?? 0;
        return t == 120910200 ? "Tax" : t == 120910201 ? "AQS" : t.ToString(CultureInfo.InvariantCulture);
    }));

    Console.WriteLine(
        $"{reference}: {routeCode}, allocated to {assigneeEmail}, " +
        $"review(s) [{(types.Length == 0 ? "none" : types)}], first check should be {firstReview}.");
    return reviews.Count > 0;
}

int RouteCase(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 4 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This mutates a real case. Re-run as: " +
            "routecase <orgUrl> <caseName> <routeName> [<assigneeEmail>] --confirm <orgUrl>");
        return 1;
    }

    var caseName = a[2].Trim();
    var routeName = a[3].Trim();
    var assigneeEmail = a.Length >= 5 && !a[4].StartsWith("--", StringComparison.Ordinal)
        ? a[4].Trim()
        : null;

    using var svc = Connect(orgUrl);

    var caseId = FindId(svc, "al_outcomecase", ("al_name", caseName));
    if (caseId == Guid.Empty)
    {
        Console.Error.WriteLine($"No case is named {caseName}.");
        return 1;
    }

    var routeId = FindId(svc, "al_reviewroute", ("al_name", routeName));
    if (routeId == Guid.Empty)
    {
        Console.Error.WriteLine($"No review route is named '{routeName}'.");
        return 1;
    }

    var route = svc.Retrieve(
        "al_reviewroute", routeId, new ColumnSet("al_requirestaxreview", "al_requiresaqsreview"));
    Console.WriteLine(
        $"Route '{routeName}': Tax={route.GetAttributeValue<bool?>("al_requirestaxreview") ?? false}, " +
        $"AQS={route.GetAttributeValue<bool?>("al_requiresaqsreview") ?? false}");

    svc.Update(new Entity("al_outcomecase", caseId)
    {
        ["al_reviewrouteid"] = new EntityReference("al_reviewroute", routeId),
    });
    Console.WriteLine($"{caseName} -> route '{routeName}'.");

    if (assigneeEmail == null)
    {
        Console.WriteLine("No assignee given, so nothing was allocated and no review exists yet.");
        return 0;
    }

    var outcomeCase = svc.Retrieve("al_outcomecase", caseId, new ColumnSet("al_casestatus"));
    var status = outcomeCase.GetAttributeValue<OptionSetValue>("al_casestatus")?.Value ?? 0;
    if (status != CaseStatusQueued)
    {
        Console.Error.WriteLine(
            $"{caseName} is at status {status}, not Queued ({CaseStatusQueued}), so a claim would be refused. " +
            "The route was set; allocate it from the portal or re-run once it is back in the queue.");
        return 2;
    }

    var contactId = FindId(svc, "contact", ("emailaddress1", assigneeEmail));
    if (contactId == Guid.Empty)
    {
        Console.Error.WriteLine($"No contact has the email {assigneeEmail}.");
        return 1;
    }

    svc.Create(new Entity("al_caseassignment")
    {
        ["al_outcomecaseid"] = new EntityReference("al_outcomecase", caseId),
        ["al_assignedcontactid"] = new EntityReference("contact", contactId),
    });
    Console.WriteLine($"Allocated to {assigneeEmail}.");

    // Read back what the plug-in decided rather than reporting what was asked for.
    var reviews = svc.RetrieveMultiple(new QueryExpression("al_reviewinstance")
    {
        ColumnSet = new ColumnSet("al_name", "al_reviewtype", "al_reviewstatus", "al_assignedcontactid"),
        Criteria =
        {
            Conditions = { new ConditionExpression("al_outcomecaseid", ConditionOperator.Equal, caseId) },
        },
    }).Entities;

    foreach (var r in reviews)
    {
        var type = r.GetAttributeValue<OptionSetValue>("al_reviewtype")?.Value ?? 0;
        Console.WriteLine(
            $"  review: {r.GetAttributeValue<string>("al_name")} " +
            $"type={(type == 120910200 ? "Tax" : type == 120910201 ? "AQS" : type.ToString(CultureInfo.InvariantCulture))} " +
            $"assigned={r.GetAttributeValue<EntityReference>("al_assignedcontactid")?.Name ?? "(nobody)"}");
    }

    return reviews.Count > 0 ? 0 : 2;
}

// Proves the AD-089 write path: a role granted in Power Pages ONLY surfaces as unadopted,
// can be adopted, and - once withdrawn in the app while the association is put back -
// reads "withdrawn, still granted" rather than "withdrawn".
//
// This is the half proveroles cannot reach. proveroles drives the app's own commands, so
// both sources are always written together and never disagree; the conflict rule has
// something to classify only when an association is written on its own, which is what the
// bare Associate below does - exactly what Power Pages management does.
//
// It GRANTS A REAL PERSON A REAL PORTAL ROLE for the length of the run. Hence the subject
// is named rather than picked (proveroles takes the first contact it finds, which is not
// acceptable when the run is about access), the org URL is repeated after --confirm, and
// it refuses to start unless the subject holds the role in NEITHER source - so it can only
// ever put back what it found. Cleanup runs in a finally, so a failure part way through
// still cannot leave someone holding a role.
int ProveAdoption(string[] a)
{
    const string DefaultProbeRole = "AL Portal - Planner";
    const string ContactRelationship = "powerpagecomponent_mspp_webrole_contact";
    const string RoleEntity = "mspp_webrole";
    const string MappingEntity = "al_userrolemapping";

    var orgUrl = a[1];
    var email = a.Length > 2 ? a[2].Trim() : string.Empty;
    var confirmIndex = Array.FindIndex(a, x => x.Equals("--confirm", StringComparison.OrdinalIgnoreCase));
    var roleCode = confirmIndex > 3 ? a[3].Trim() : DefaultProbeRole;
    var confirmed = email.Length > 0
        && !email.StartsWith("--", StringComparison.Ordinal)
        && confirmIndex > 2
        && confirmIndex + 1 < a.Length
        && a[confirmIndex + 1].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    if (!confirmed)
    {
        Console.Error.WriteLine(
            "This grants a real portal role to a real person for the length of the run. Re-run as: " +
            "proveadoption <orgUrl> <subjectEmail> [<roleName>] --confirm <orgUrl>");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var contact = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name='contact'><attribute name='fullname'/><attribute name='emailaddress1'/>" +
        "<filter><condition attribute='emailaddress1' operator='eq' value='" +
        System.Security.SecurityElement.Escape(email) + "'/></filter></entity></fetch>"))
        .Entities.FirstOrDefault();
    if (contact == null)
    {
        Console.Error.WriteLine($"No contact has the work email {email}, so no web role can be granted to them.");
        return 1;
    }

    // FetchXML, not QueryExpression: mspp_webrole is a typed surface over powerpagecomponent
    // and does not answer a plain QueryExpression here (see WebRoleRegistry).
    var role = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name='" + RoleEntity + "'><attribute name='mspp_name'/>" +
        "<attribute name='mspp_authenticatedusersrole'/><attribute name='mspp_anonymoususersrole'/>" +
        "<filter><condition attribute='mspp_name' operator='eq' value='" +
        System.Security.SecurityElement.Escape(roleCode) + "'/></filter></entity></fetch>"))
        .Entities.FirstOrDefault();
    if (role == null)
    {
        Console.Error.WriteLine($"No web role is named {roleCode}.");
        return 1;
    }

    // AD-090 refuses to ADOPT an auto-granted role, so a run against one would fail at
    // step 2 for a reason that has nothing to do with the write path being proved.
    if ((role.GetAttributeValue<bool?>("mspp_authenticatedusersrole") ?? false)
        || (role.GetAttributeValue<bool?>("mspp_anonymoususersrole") ?? false))
    {
        Console.Error.WriteLine(
            $"{roleCode} is auto-granted to every signed-in user, so AD-090 refuses to adopt it. Pick another role.");
        return 1;
    }

    Console.WriteLine($"Subject:    {contact.GetAttributeValue<string>("fullname")} <{email}>");
    Console.WriteLine($"Probe role: {roleCode} [{role.Id:D}]");

    var before = ReadHolder(svc, roleCode, email);
    if (before.found && (before.associated || before.mappingId != null))
    {
        Console.Error.WriteLine(
            $"  {email} already has a relationship to {roleCode} (mappingId=" +
            (before.mappingId ?? "none") + $", associated={before.associated}). This run would " +
            "change state it did not create; pick a role they hold in neither source.");
        return 1;
    }

    Console.WriteLine("Precondition: no mapping row and no association - OK");

    var passes = 0;
    var checks = 0;
    Guid createdMapping = Guid.Empty;

    bool Step(string title, string expectedState, (bool found, string? mappingId, bool? mappingActive, bool associated) row)
    {
        checks++;
        var classification = Classify(row.mappingId, row.mappingActive, row.associated);
        Console.WriteLine(
            "   al_GetRoleHolders -> mappingId=" + (row.mappingId ?? "null") +
            " mappingActive=" + (row.mappingActive.HasValue ? (row.mappingActive.Value ? "true" : "false") : "null") +
            " associated=" + (row.associated ? "true" : "false") +
            (row.found ? string.Empty : "  (no row returned)"));
        Console.WriteLine($"   classify -> {classification.state} \"{classification.label}\"");

        var ok = classification.state == expectedState;
        Console.WriteLine($"   {title}: " + (ok ? "PASS" : $"FAIL - expected {expectedState}"));
        if (ok)
        {
            passes++;
        }

        return ok;
    }

    try
    {
        Console.WriteLine();
        Console.WriteLine("1. Grant in Power Pages only - a bare Associate, no mapping row.");
        svc.Associate(
            "contact", contact.Id, new Relationship(ContactRelationship),
            new EntityReferenceCollection { new EntityReference(RoleEntity, role.Id) });
        Step("STEP 1", "portal-only", ReadHolder(svc, roleCode, email));

        Console.WriteLine();
        Console.WriteLine("2. al_AdoptRoleAssignment Decision=Adopt.");
        var adopt = svc.Execute(new OrganizationRequest("al_AdoptRoleAssignment")
        {
            ["UserEmail"] = email,
            ["RoleCode"] = roleCode,
            ["Decision"] = "Adopt",
            ["IdempotencyKey"] = "PROVE-ADOPTION-ADOPT-" + Guid.NewGuid().ToString("N"),
        });
        var mappingId = (string)adopt["MappingId"];
        Console.WriteLine($"   -> MappingId {mappingId}, Adopted={adopt["Adopted"]}, AuditEventId {adopt["AuditEventId"]}");
        if (!string.IsNullOrWhiteSpace(mappingId))
        {
            createdMapping = new Guid(mappingId);
        }

        Step("STEP 2", "consistent", ReadHolder(svc, roleCode, email));

        Console.WriteLine();
        Console.WriteLine("3. al_SetRoleAssignmentActive Active=false - withdraw in the app.");
        svc.Execute(new OrganizationRequest("al_SetRoleAssignmentActive")
        {
            ["MappingId"] = mappingId,
            ["Active"] = false,
            ["IdempotencyKey"] = "PROVE-ADOPTION-WITHDRAW-" + Guid.NewGuid().ToString("N"),
        });
        Step("STEP 3", "consistent", ReadHolder(svc, roleCode, email));

        Console.WriteLine();
        Console.WriteLine("4. Re-associate in Power Pages - the drift AD-089 exists to surface.");
        svc.Associate(
            "contact", contact.Id, new Relationship(ContactRelationship),
            new EntityReferenceCollection { new EntityReference(RoleEntity, role.Id) });
        Step("STEP 4", "withdrawn-still-granted", ReadHolder(svc, roleCode, email));

        Console.WriteLine();
        Console.WriteLine("5. al_AdoptRoleAssignment Decision=Revoke - converge on not granted.");
        var revoke = svc.Execute(new OrganizationRequest("al_AdoptRoleAssignment")
        {
            ["UserEmail"] = email,
            ["RoleCode"] = roleCode,
            ["Decision"] = "Revoke",
            ["IdempotencyKey"] = "PROVE-ADOPTION-REVOKE-" + Guid.NewGuid().ToString("N"),
        });
        Console.WriteLine($"   -> Adopted={revoke["Adopted"]}, AuditEventId {revoke["AuditEventId"]}");
        Step("STEP 5", "consistent", ReadHolder(svc, roleCode, email));

        Console.WriteLine();
        Console.WriteLine($"PROVE ADOPTION: {(passes == checks ? "PASS" : "FAIL")} ({passes} of {checks})");
        return passes == checks ? 0 : 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("PROVE ADOPTION FAILED: " + ex.Message.Replace("\r", " ").Replace("\n", " "));
        return 2;
    }
    finally
    {
        // Put back exactly what was found: no association, no mapping row. Both are
        // attempted regardless of where the run stopped, because the failure that matters
        // is the one that leaves a real person holding a role nobody granted them.
        try
        {
            svc.Disassociate(
                "contact", contact.Id, new Relationship(ContactRelationship),
                new EntityReferenceCollection { new EntityReference(RoleEntity, role.Id) });
            Console.WriteLine("cleanup: association removed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("cleanup: no association to remove (" + ex.Message.Replace("\r", " ").Replace("\n", " ") + ").");
        }

        if (createdMapping != Guid.Empty)
        {
            try
            {
                svc.Delete(MappingEntity, createdMapping);
                Console.WriteLine("cleanup: probe mapping deleted.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"cleanup FAILED, delete {MappingEntity} {createdMapping:D} by hand: " +
                    ex.Message.Replace("\r", " ").Replace("\n", " "));
            }
        }

        var after = ReadHolder(svc, roleCode, email);
        Console.WriteLine(
            "final: mappingId=" + (after.mappingId ?? "null") +
            " associated=" + (after.associated ? "true" : "false") +
            (after.found ? "  - STILL PRESENT, CHECK BY HAND" : "  - as found"));
    }
}

// One person's row from al_GetRoleHolders, or "no row" when the read returned none - which
// is itself a fact worth carrying, since a person with neither a mapping nor an association
// is absent from the list rather than present and empty.
(bool found, string? mappingId, bool? mappingActive, bool associated) ReadHolder(
    ServiceClient svc, string roleCode, string email)
{
    var response = svc.Execute(new OrganizationRequest("al_GetRoleHolders")
    {
        ["RoleCode"] = roleCode,
    });

    using var document = JsonDocument.Parse((string)response["Holders"]);
    foreach (var element in document.RootElement.EnumerateArray())
    {
        var rowEmail = (element.GetProperty("email").GetString() ?? string.Empty).Trim();
        if (!rowEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var mapping = element.GetProperty("mappingId");
        var active = element.GetProperty("mappingActive");
        return (
            true,
            mapping.ValueKind == JsonValueKind.Null ? null : mapping.GetString(),
            active.ValueKind == JsonValueKind.Null ? null : active.GetBoolean(),
            element.GetProperty("associated").GetBoolean());
    }

    return (false, null, null, false);
}

// Mirrors classifyHolder in app/src/features/admin/roleDetail.ts. Duplicated rather than
// shared for the same reason the PP-15 constants above are: that is TypeScript in the code
// app and this is a net8.0 console. Asserting on the LABEL rather than on the raw fields is
// the point - the proof is about what an administrator would see on the role detail screen,
// and a rule that reads the fields correctly but labels them wrongly is still wrong.
(string state, string label) Classify(string? mappingId, bool? mappingActive, bool associated)
{
    var hasMapping = mappingId != null;

    if (!hasMapping && associated)
    {
        return ("portal-only", "Granted in Power Pages, not adopted");
    }

    if (hasMapping && mappingActive == false && associated)
    {
        return ("withdrawn-still-granted", "Withdrawn in app, still granted");
    }

    if (hasMapping && mappingActive == true && !associated)
    {
        return ("association-missing", "Assigned in app, association missing");
    }

    return (
        "consistent",
        hasMapping && mappingActive == true
            ? "Assigned in app"
            : hasMapping
                ? (mappingActive == false ? "Withdrawn" : "Held")
                : "Not held");
}

// Imports a Configuration Migration data package (data_schema.xml + data.xml) by upserting
// every record on the id the package gives it.
//
// This exists because `pac` cannot. There is no `pac data import` in 2.11.2 — the only
// first-party route is the Configuration Migration Tool, which is a desktop GUI, so a seed
// change could not be applied from a session at all. Same reason restoretablepermissions
// exists: the supported tool for the job is not one this environment can run.
//
// Schema-driven rather than metadata-driven: data_schema.xml already declares the type of
// every field the package writes, so the conversion cannot disagree with the file it is
// reading, and the import spends no metadata calls. A field the schema does not declare is
// an error rather than a skip — a typo in a seed that silently writes nothing is exactly the
// failure a seed importer must not have.
//
// Upsert on the package's own id, so re-importing is idempotent and a record that already
// exists keeps its relationships. Records are written in file order, which is why the
// package lists parents before children.
//
// WRITES REFERENCE DATA. The org URL is repeated after --confirm, like every other writing
// verb here.
int ImportSeed(string[] a)
{
    var orgUrl = a[1];
    var folder = a.Length > 2 ? a[2] : string.Empty;
    var confirmIndex = Array.FindIndex(a, x => x.Equals("--confirm", StringComparison.OrdinalIgnoreCase));
    var confirmed = folder.Length > 0
        && confirmIndex > 2
        && confirmIndex + 1 < a.Length
        && a[confirmIndex + 1].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    if (!confirmed)
    {
        Console.Error.WriteLine(
            "This writes reference data. Re-run as: importseed <orgUrl> <packageFolder> --confirm <orgUrl>");
        return 1;
    }

    folder = Path.GetFullPath(folder);
    var schemaPath = Path.Combine(folder, "data_schema.xml");
    var dataPath = Path.Combine(folder, "data.xml");
    if (!File.Exists(schemaPath) || !File.Exists(dataPath))
    {
        Console.Error.WriteLine($"Expected data_schema.xml and data.xml under {folder}.");
        return 1;
    }

    var schema = System.Xml.Linq.XDocument.Load(schemaPath);
    var fields = new Dictionary<string, Dictionary<string, (string Type, string LookupType)>>(StringComparer.OrdinalIgnoreCase);
    foreach (var entity in schema.Descendants("entity"))
    {
        var name = (string?)entity.Attribute("name");
        if (name == null) continue;

        var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in entity.Descendants("field"))
        {
            var fieldName = (string?)field.Attribute("name");
            if (fieldName == null) continue;
            map[fieldName] = ((string?)field.Attribute("type") ?? "string", (string?)field.Attribute("lookupType") ?? string.Empty);
        }

        fields[name] = map;
    }

    using var svc = Connect(orgUrl);

    var created = 0;
    var updated = 0;
    var data = System.Xml.Linq.XDocument.Load(dataPath);

    // Package id -> the id the environment actually holds.
    //
    // A package's ids are its own. This environment was first seeded through the
    // Configuration Migration tool, which mints its own, so a row that exists is not found
    // by the package's id and re-creating it collides with the business key instead. Rows
    // are therefore matched on that business key, and every lookup is translated through
    // this map before it is written — otherwise a child would point at an id no row has.
    //
    // Parents are listed before children in the package, which is what makes one pass enough.
    var idMap = new Dictionary<Guid, Guid>();

    foreach (var entityNode in data.Descendants("entity"))
    {
        var logicalName = (string?)entityNode.Attribute("name");
        if (logicalName == null) continue;

        if (!fields.TryGetValue(logicalName, out var fieldTypes))
        {
            Console.Error.WriteLine($"{logicalName}: not declared in data_schema.xml.");
            return 1;
        }

        Console.WriteLine($"{logicalName}…");

        foreach (var recordNode in entityNode.Descendants("record"))
        {
            var rawId = (string?)recordNode.Attribute("id");
            if (rawId == null || !Guid.TryParse(rawId, out var id))
            {
                Console.Error.WriteLine($"  {logicalName}: a record has no usable id.");
                return 1;
            }

            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var fieldNode in recordNode.Elements("field"))
            {
                var name = (string?)fieldNode.Attribute("name");
                var value = (string?)fieldNode.Attribute("value");
                if (name == null) continue;

                if (!fieldTypes.TryGetValue(name, out var type))
                {
                    Console.Error.WriteLine($"  {logicalName}.{name}: not declared in data_schema.xml.");
                    return 1;
                }

                var converted = SeedValue(logicalName, name, type.Type, type.LookupType, value, fieldNode);
                if (converted is EntityReference reference && idMap.TryGetValue(reference.Id, out var mapped))
                {
                    converted = new EntityReference(reference.LogicalName, mapped);
                }

                values[name] = converted;
            }

            // Every table in these packages carries "<logical name>code" as its business key
            // — al_sectioncode, al_questionversioncode, and so on — which is what makes one
            // uniform rule enough rather than a table of special cases.
            var codeAttr = logicalName + "code";
            var existingId = Guid.Empty;

            try
            {
                existingId = svc.Retrieve(logicalName, id, new ColumnSet(false)).Id;
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                // Not under the package's id. Fall through to the business key.
            }

            if (existingId == Guid.Empty && fieldTypes.ContainsKey(codeAttr) && values.ContainsKey(codeAttr))
            {
                var byCode = new QueryExpression(logicalName)
                {
                    ColumnSet = new ColumnSet(false),
                    TopCount = 2,
                    Criteria = new FilterExpression(),
                };
                byCode.Criteria.AddCondition(codeAttr, ConditionOperator.Equal, (string)values[codeAttr]);

                var found = svc.RetrieveMultiple(byCode).Entities;
                if (found.Count > 1)
                {
                    Console.Error.WriteLine(
                        $"  {logicalName} {values[codeAttr]}: {found.Count} rows carry this code; refusing to guess.");
                    return 1;
                }

                if (found.Count == 1)
                {
                    existingId = found[0].Id;
                }
            }

            if (existingId != Guid.Empty)
            {
                var row = new Entity(logicalName, existingId);
                foreach (var pair in values)
                {
                    row[pair.Key] = pair.Value;
                }

                svc.Update(row);
                updated++;
            }
            else
            {
                var row = new Entity(logicalName, id);
                foreach (var pair in values)
                {
                    row[pair.Key] = pair.Value;
                }

                existingId = svc.Create(row);
                created++;
            }

            idMap[id] = existingId;
        }
    }

    Console.WriteLine($"Done. {created} created, {updated} updated.");
    return 0;
}

object SeedValue(
    string entity, string field, string type, string lookupType, string? value,
    System.Xml.Linq.XElement fieldNode)
{
    switch (type.ToLowerInvariant())
    {
        case "string":
        case "memo":
            return value ?? string.Empty;

        case "bool":
        case "boolean":
            return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);

        case "number":
        case "integer":
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            throw new InvalidOperationException($"{entity}.{field}: '{value}' is not a whole number.");

        case "optionsetvalue":
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var option))
            {
                return new OptionSetValue(option);
            }

            throw new InvalidOperationException($"{entity}.{field}: '{value}' is not an option value.");

        case "datetime":
            if (DateTime.TryParse(
                    value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var moment))
            {
                return moment;
            }

            throw new InvalidOperationException($"{entity}.{field}: '{value}' is not a date and time.");

        case "entityreference":
            // The target table comes from the record when it names one, and from the schema
            // otherwise. A polymorphic lookup carries its own; a plain one does not.
            var target = (string?)fieldNode.Attribute("lookupentity") ?? lookupType;
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException($"{entity}.{field}: no lookup table for this reference.");
            }

            if (!Guid.TryParse(value, out var reference))
            {
                throw new InvalidOperationException($"{entity}.{field}: '{value}' is not a record id.");
            }

            return new EntityReference(target, reference);

        default:
            throw new InvalidOperationException($"{entity}.{field}: unsupported field type '{type}'.");
    }
}

int Register(string[] a)
{
    var orgUrl = a[0];
    var dllPath = a.Length > 1
        ? a[1]
        : Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..",
            "OutcomeTesting.Plugins", "bin", "Release", "net462", "OutcomeTesting.Plugins.dll");
    dllPath = Path.GetFullPath(dllPath);
    if (!File.Exists(dllPath))
    {
        Console.Error.WriteLine($"Plug-in assembly not found: {dllPath}");
        return 1;
    }

    using var svc = Connect(orgUrl);

    Guid Upsert(string table, Entity values, params (string attr, object value)[] key)
    {
        var id = FindId(svc, table, key);
        if (id == Guid.Empty)
        {
            id = svc.Create(values);
            Console.WriteLine($"  created {table}: {id}");
        }
        else
        {
            values.Id = id;
            values.LogicalName = table;
            svc.Update(values);
            Console.WriteLine($"  updated {table}: {id}");
        }

        return id;
    }

    Console.WriteLine("1. Plug-in assembly…");
    var assemblyId = Upsert("pluginassembly", new Entity("pluginassembly")
    {
        ["name"] = AssemblyName,
        ["version"] = "1.0.0.0",
        ["culture"] = "neutral",
        ["publickeytoken"] = "86b764d5a2430b1f",
        ["sourcetype"] = new OptionSetValue(0),
        ["isolationmode"] = new OptionSetValue(2),
        ["content"] = Convert.ToBase64String(File.ReadAllBytes(dllPath)),
    }, ("name", AssemblyName));

    Console.WriteLine("2. Plug-in type…");
    var pluginTypeId = Upsert("plugintype", new Entity("plugintype")
    {
        ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
        ["typename"] = TypeName,
        ["friendlyname"] = TypeName,
        ["name"] = TypeName,
    }, ("typename", TypeName));

    Console.WriteLine("3. Custom API…");
    var customApiId = Upsert("customapi", new Entity("customapi")
    {
        ["uniquename"] = ApiUniqueName,
        ["name"] = ApiUniqueName,
        ["displayname"] = "Complete Remediation",
        ["description"] = "Adviser marks a remediation action Completed (BR-006, BR-008). Enforces caller, transition, concurrency and idempotency, and writes an immutable Audit Event.",
        ["bindingtype"] = new OptionSetValue(0),
        ["isfunction"] = false,
        ["isprivate"] = false,
        ["allowedcustomprocessingsteptype"] = new OptionSetValue(0),
        ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
    }, ("uniquename", ApiUniqueName));

    void UpsertParam(string table, string uniqueName, string displayName, string description, int type, bool? isOptional)
    {
        var e = new Entity(table)
        {
            ["customapiid"] = new EntityReference("customapi", customApiId),
            ["uniquename"] = uniqueName,
            ["name"] = uniqueName,
            ["displayname"] = displayName,
            ["description"] = description,
            ["type"] = new OptionSetValue(type),
        };
        if (isOptional.HasValue)
        {
            e["isoptional"] = isOptional.Value;
        }

        Upsert(table, e, ("uniquename", uniqueName), ("customapiid", customApiId));
    }

    Console.WriteLine("4. Request parameters…");
    UpsertParam("customapirequestparameter", "TargetId", "Target id", "Id of the al_remediationaction to complete.", 10, false);
    UpsertParam("customapirequestparameter", "ExpectedRowVersion", "Expected row version", "Row version for optimistic concurrency. Omit to skip the check.", 10, true);
    UpsertParam("customapirequestparameter", "IdempotencyKey", "Idempotency key", "Stable key for the intent; a replay upserts the same Audit Event.", 10, false);

    Console.WriteLine("5. Response properties…");
    UpsertParam("customapiresponseproperty", "Status", "Status", "The action status after the command (Completed).", 10, null);
    UpsertParam("customapiresponseproperty", "AuditEventId", "Audit event id", "Id of the Audit Event written for this command.", 10, null);
    UpsertParam("customapiresponseproperty", "Conflict", "Conflict", "True when rejected for an optimistic-concurrency conflict.", 0, null);

    Console.WriteLine($"Done. {ApiUniqueName} is registered and bound to {TypeName}.");
    return 0;
}

int Verify(string orgUrl, Guid caseId)
{
    using var svc = Connect(orgUrl);
    var pass = true;
    void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        pass &= ok;
    }

    var caseRef = NamedCase(svc, caseId);
    if (caseRef == null)
    {
        return 1;
    }

    var code = "VERIFY-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var actionId = svc.Create(new Entity("al_remediationaction")
    {
        ["al_name"] = "VERIFY CompleteRemediation",
        ["al_remediationactioncode"] = code,
        ["al_description"] = "Temporary action created by the verification harness.",
        ["al_actionstatus"] = new OptionSetValue(StatusOpen),
        ["al_outcomecaseid"] = caseRef.ToEntityReference(),
    });
    Console.WriteLine($"Seeded remediation action {actionId} on case '{caseRef["al_name"]}'.");

    // From here the seeded action must be removed whatever happens. Without this, a
    // failure part-way strands a fake Open remediation action on the case, which then
    // blocks its sign-off (BR-008).
    try
    {

    var seeded = svc.Retrieve("al_remediationaction", actionId, new ColumnSet("al_actionstatus"));
    var key = Guid.NewGuid().ToString();

    var request = new OrganizationRequest(ApiUniqueName)
    {
        ["TargetId"] = actionId.ToString(),
        ["ExpectedRowVersion"] = seeded.RowVersion,
        ["IdempotencyKey"] = key,
    };
    var response = svc.Execute(request);
    var status = response.Results.Contains("Status") ? (string)response["Status"] : "(none)";
    Check("command returned Completed", status == "Completed", $"Status={status}");

    var after = svc.Retrieve("al_remediationaction", actionId, new ColumnSet("al_actionstatus"));
    var afterStatus = after.GetAttributeValue<OptionSetValue>("al_actionstatus")?.Value;
    Check("action status is Completed", afterStatus == StatusCompleted, $"al_actionstatus={afterStatus}");

    var audits = svc.RetrieveMultiple(new QueryExpression("al_auditevent")
    {
        ColumnSet = new ColumnSet("al_command", "al_targetid"),
        Criteria = new FilterExpression { Conditions = { new ConditionExpression("al_idempotencykey", ConditionOperator.Equal, key) } },
    }).Entities;
    Check("exactly one audit event", audits.Count == 1, $"count={audits.Count}");
    if (audits.Count > 0)
    {
        var cmd = audits[0].GetAttributeValue<OptionSetValue>("al_command")?.Value;
        Check("audit command is CompleteRemediation", cmd == CommandCompleteRemediation, $"al_command={cmd}");
    }

    // Replay with the same idempotency key must not create a second audit event.
    svc.Execute(new OrganizationRequest(ApiUniqueName)
    {
        ["TargetId"] = actionId.ToString(),
        ["IdempotencyKey"] = key,
    });
    var afterReplay = svc.RetrieveMultiple(new QueryExpression("al_auditevent")
    {
        ColumnSet = new ColumnSet(false),
        Criteria = new FilterExpression { Conditions = { new ConditionExpression("al_idempotencykey", ConditionOperator.Equal, key) } },
    }).Entities.Count;
    Check("idempotent replay (no duplicate audit)", afterReplay == 1, $"count={afterReplay}");

    }
    finally
    {
        TryDelete(svc, "al_remediationaction", actionId);
        Console.WriteLine($"Cleaned up verification action {actionId}.");
    }
    Console.WriteLine(pass ? "VERIFY: PASS" : "VERIFY: FAIL");
    return pass ? 0 : 2;
}

// Adds the plug-in assembly (and its plug-in type, as a subcomponent) to the target
// solution. The Custom API itself is NOT added here: Dataverse has no AddSolutionComponent
// component type for Custom APIs, so it is added via a solution-file import instead
// (src/customapis/al_CompleteRemediation, pac solution import). ComponentType codes:
// 90 = Plugin Type, 91 = Plugin Assembly.

// Seeds an application Administrator (AD-041) directly into the app-RBAC tables, bypassing
// the al_AssignUserRole/al_SetPagePermission command gate (which needs an existing admin).
// Writes al_userrolemapping (email -> Administrator) and al_pagepermission rows granting
// Administrator Manage on every resource key, so the named account can operate the whole app.
int SeedAdmin(string orgUrl, string email)
{
    email = email.Trim();
    const int administratorRole = 120910765; // al_approle Administrator
    const int manageLevel = 120910769;       // al_accesslevel Manage
    string[] resourceKeys =
    {
        "page.dashboard", "page.cases", "page.imports", "page.reviews", "page.remediation",
        "page.reports", "page.exports", "page.admin.questions", "page.admin.security",
        "page.admin.users", "command.assign", "command.regrade", "command.signoff",
        "remediation.complete", "question.retire", "export.generate", "permission.manage",
    };

    using var svc = Connect(orgUrl);

    Guid UpsertByCode(string table, string codeAttr, string code, Entity values)
    {
        var id = FindId(svc, table, (codeAttr, code));
        if (id == Guid.Empty)
        {
            id = svc.Create(values);
            Console.WriteLine($"  created {table}: {code}");
        }
        else
        {
            var update = new Entity(table, id);
            foreach (var attr in values.Attributes)
            {
                if (attr.Key != codeAttr)
                {
                    update[attr.Key] = attr.Value;
                }
            }
            svc.Update(update);
            Console.WriteLine($"  updated {table}: {code}");
        }
        return id;
    }

    var mappingCode = "URM-" + email.ToLowerInvariant() + "-" + administratorRole;
    UpsertByCode("al_userrolemapping", "al_userrolemappingcode", mappingCode, new Entity("al_userrolemapping")
    {
        ["al_name"] = "Administrator - " + email,
        ["al_useremail"] = email,
        ["al_approle"] = new OptionSetValue(administratorRole),
        ["al_userrolemappingcode"] = mappingCode,
        ["statecode"] = new OptionSetValue(0),
        ["statuscode"] = new OptionSetValue(1),
    });

    foreach (var resourceKey in resourceKeys)
    {
        var permissionCode = "PP-" + administratorRole + "-" + resourceKey;
        UpsertByCode("al_pagepermission", "al_pagepermissioncode", permissionCode, new Entity("al_pagepermission")
        {
            ["al_name"] = "Administrator / " + resourceKey,
            ["al_approle"] = new OptionSetValue(administratorRole),
            ["al_resourcekey"] = resourceKey,
            ["al_accesslevel"] = new OptionSetValue(manageLevel),
            ["al_pagepermissioncode"] = permissionCode,
            ["statecode"] = new OptionSetValue(0),
            ["statuscode"] = new OptionSetValue(1),
        });
    }

    Console.WriteLine($"Done. {email} is an application Administrator with Manage on all {resourceKeys.Length} resources.");
    return 0;
}

int AddToSolution(string orgUrl, string solutionUniqueName)
{
    using var svc = Connect(orgUrl);

    var assemblyId = FindId(svc, "pluginassembly", ("name", AssemblyName));
    if (assemblyId == Guid.Empty)
    {
        Console.Error.WriteLine($"Plug-in assembly '{AssemblyName}' not found. Register first.");
        return 1;
    }

    try
    {
        svc.Execute(new AddSolutionComponentRequest
        {
            ComponentId = assemblyId,
            ComponentType = 91,
            SolutionUniqueName = solutionUniqueName,
            AddRequiredComponents = false,
            DoNotIncludeSubcomponents = false,
        });
        Console.WriteLine($"  added plugin assembly ({assemblyId}) and its types to {solutionUniqueName}.");
    }
    catch (Exception ex)
    {
        // Already a member (or similar) is fine - keep the run idempotent.
        Console.WriteLine($"  plugin assembly ({assemblyId}): {ex.Message.Split('\n')[0].Trim()}");
    }

    Console.WriteLine(
        $"Done. Add the Custom API via 'pac solution import' of src/customapis, then verify '{solutionUniqueName}' in the maker portal.");
    return 0;
}

// A plug-in registered against a table message has no Custom API, so registerall - which
// iterates the contracts - never creates its type. This creates one by class name and
// prints the id, which is what the SdkMessageProcessingStep solution file has to carry.
int RegisterType(string orgUrl, string typeName)
{
    using var svc = Connect(orgUrl);

    var assemblyId = FindId(svc, "pluginassembly", ("name", AssemblyName));
    if (assemblyId == Guid.Empty)
    {
        Console.Error.WriteLine($"Plug-in assembly '{AssemblyName}' is not registered. Push it first.");
        return 1;
    }

    var values = new Entity("plugintype")
    {
        ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
        ["typename"] = typeName,
        ["friendlyname"] = typeName,
        ["name"] = typeName,
    };

    var existing = FindId(svc, "plugintype", ("typename", typeName));
    Guid id;
    if (existing == Guid.Empty)
    {
        id = svc.Create(values);
        Console.WriteLine($"created plugintype: {id}");
    }
    else
    {
        values.Id = existing;
        svc.Update(values);
        id = existing;
        Console.WriteLine($"updated plugintype: {id}");
    }

    Console.WriteLine($"PluginTypeId={id:D}");
    return 0;
}

// Registers a plug-in against a table message. Hand-authoring the solution file instead
// does not work: pack refuses a step whose plug-in type is absent from the assembly
// manifest, and the manifest is only correct when it comes from an export (AD-013). So the
// step is created here and brought back into src by the round trip.
// mode: 0 synchronous, 1 asynchronous. runAs names the systemuser the step executes as —
// a domain name (UPN) or a record id — and is what makes the PP-15 drain send from the
// service account's approved mailbox rather than from whoever's action queued the row.
int RegisterStep(string orgUrl, string typeName, string messageName, string primaryEntity, string filteringAttributes, int stage, int mode = 0, string? runAs = null)
{
    using var svc = Connect(orgUrl);

    var pluginTypeId = FindId(svc, "plugintype", ("typename", typeName));
    if (pluginTypeId == Guid.Empty)
    {
        Console.Error.WriteLine($"Plug-in type '{typeName}' is not registered. Run 'registertype' first.");
        return 1;
    }

    var messageId = FindId(svc, "sdkmessage", ("name", messageName));
    if (messageId == Guid.Empty)
    {
        Console.Error.WriteLine($"SDK message '{messageName}' was not found.");
        return 1;
    }

    // The filter binds the message to one table; without it the step fires for every table
    // that supports the message.
    var filter = new QueryExpression("sdkmessagefilter")
    {
        ColumnSet = new ColumnSet("sdkmessagefilterid"),
        TopCount = 1,
        Criteria = new FilterExpression(),
    };
    filter.Criteria.AddCondition("sdkmessageid", ConditionOperator.Equal, messageId);
    filter.Criteria.AddCondition("primaryobjecttypecode", ConditionOperator.Equal, primaryEntity);

    var filterRows = svc.RetrieveMultiple(filter).Entities;
    if (filterRows.Count == 0)
    {
        Console.Error.WriteLine($"No SDK message filter for {messageName} on {primaryEntity}.");
        return 1;
    }

    var stepName = $"{typeName.Split('.').Last()}: {messageName} of {primaryEntity}";

    var values = new Entity("sdkmessageprocessingstep")
    {
        ["name"] = stepName,
        ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
        ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
        ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterRows[0].Id),
        ["stage"] = new OptionSetValue(stage),
        // Synchronous by default, so a refusal reaches the caller. Asynchronous is for the
        // one step that must NOT reach the caller: the PP-15 drain runs after the state
        // change commits, so a mailbox refusal cannot roll back a submitted review.
        ["mode"] = new OptionSetValue(mode),
        ["rank"] = 1,
        ["supporteddeployment"] = new OptionSetValue(0),
        ["invocationsource"] = new OptionSetValue(0),
        ["filteringattributes"] = filteringAttributes,
    };

    if (mode == 1)
    {
        // Delete the system job once it succeeds. Without this an outbox that drains every
        // notification leaves one AsyncOperation row per email behind for ever.
        values["asyncautodelete"] = true;
    }

    if (!string.IsNullOrWhiteSpace(runAs))
    {
        var runAsId = ResolveSystemUser(svc, runAs);
        if (runAsId == Guid.Empty)
        {
            Console.Error.WriteLine($"No enabled systemuser matches '{runAs}' (give a UPN or a record id).");
            return 1;
        }

        values["impersonatinguserid"] = new EntityReference("systemuser", runAsId);
        Console.WriteLine($"  runs as systemuser {runAsId}.");
    }

    var existing = FindId(svc, "sdkmessageprocessingstep", ("name", stepName));
    Guid stepId;
    if (existing == Guid.Empty)
    {
        stepId = svc.Create(values);
        Console.WriteLine($"created sdkmessageprocessingstep: {stepId}");
    }
    else
    {
        values.Id = existing;
        svc.Update(values);
        stepId = existing;
        Console.WriteLine($"updated sdkmessageprocessingstep: {stepId}");
    }

    try
    {
        svc.Execute(new AddSolutionComponentRequest
        {
            ComponentId = stepId,
            ComponentType = 92,
            SolutionUniqueName = "OutcomeTesting",
            AddRequiredComponents = false,
        });
        Console.WriteLine("  added to the OutcomeTesting solution.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  solution membership: {ex.Message.Split('\n')[0].Trim()}");
    }

    Console.WriteLine($"SdkMessageProcessingStepId={stepId:D}");
    return 0;
}

// Creates or updates portal table permissions directly, because `pac pages upload` 2.11.2
// cannot write adx_entitypermission to an Enhanced data model site - it addresses the
// legacy table and aborts the whole upload. In the Enhanced model a table permission is a
// powerpagecomponent of type 18 whose settings live as JSON in `content`, so this writes
// that row from the same YAML the CLI would have read. Idempotent: the id in the YAML is
// the component id, so a re-run updates rather than duplicating (AD-059).
int RestoreTablePermissions(string orgUrl, string sitePath)
{
    var folder = Path.Combine(Path.GetFullPath(sitePath), "table-permissions");
    if (!Directory.Exists(folder))
    {
        Console.Error.WriteLine($"No table-permissions folder under {sitePath}.");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var site = new QueryExpression("powerpagesite") { ColumnSet = new ColumnSet("name"), TopCount = 2 };
    var sites = svc.RetrieveMultiple(site).Entities;
    if (sites.Count != 1)
    {
        Console.Error.WriteLine($"Expected exactly one Power Pages site, found {sites.Count}.");
        return 1;
    }

    var siteId = sites[0].Id;
    Console.WriteLine($"Site: {sites[0].GetAttributeValue<string>("name")} ({siteId:D})");

    // Parents must exist before a child permission can point at one.
    var files = Directory.GetFiles(folder, "*.tablepermission.yml")
        .Select(f => new { Path = f, Yaml = ParseFlatYaml(File.ReadAllLines(f)) })
        .OrderBy(f => f.Yaml.ContainsKey("adx_parententitypermission") ? 1 : 0)
        .ToArray();

    var written = 0;
    foreach (var file in files)
    {
        var y = file.Yaml;
        Guid id;
        if (!y.TryGetValue("adx_entitypermissionid", out var rawId) || !Guid.TryParse(rawId.Scalar, out id))
        {
            Console.Error.WriteLine($"  skipped {Path.GetFileName(file.Path)}: no adx_entitypermissionid.");
            continue;
        }

        var name = y.TryGetValue("adx_entityname", out var n) ? n.Scalar : Path.GetFileName(file.Path);

        var row = new Entity("powerpagecomponent", id)
        {
            ["name"] = name,
            ["powerpagecomponenttype"] = new OptionSetValue(18),
            ["content"] = BuildPermissionJson(y),
            ["powerpagesiteid"] = new EntityReference("powerpagesite", siteId),
        };

        var exists = svc.RetrieveMultiple(new QueryExpression("powerpagecomponent")
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
            Criteria =
            {
                Conditions = { new ConditionExpression("powerpagecomponentid", ConditionOperator.Equal, id) },
            },
        }).Entities.Count > 0;

        if (exists)
        {
            svc.Update(row);
            Console.WriteLine($"  updated {name} ({id:D})");
        }
        else
        {
            svc.Create(row);
            Console.WriteLine($"  created {name} ({id:D})");
        }

        written++;
    }

    Console.WriteLine($"Done. {written} table permission(s) written.");
    return 0;
}

/// <summary>
/// The settings JSON Dataverse stores for a type-18 component: the YAML keys with the
/// `adx_` prefix stripped, except the web-role list, which keeps its full name. Absent
/// keys are omitted rather than emitted null, matching what an export produces.
/// </summary>
string BuildPermissionJson(Dictionary<string, YamlValue> y)
{
    var sb = new StringBuilder();
    sb.AppendLine("{");

    var parts = new List<string>();
    foreach (var key in new[]
             {
                 "adx_append", "adx_appendto", "adx_contactrelationship", "adx_create", "adx_delete",
                 "adx_entitylogicalname", "adx_entityname", "adx_parententitypermission",
                 "adx_parentrelationship", "adx_read", "adx_scope", "adx_write",
             })
    {
        if (!y.TryGetValue(key, out var value) || value.Scalar == null) continue;

        var jsonKey = key.Substring("adx_".Length);
        var raw = value.Scalar;

        if (raw == "true" || raw == "false")
        {
            parts.Add($"  \"{jsonKey}\": {raw}");
        }
        else if (int.TryParse(raw, out var number))
        {
            parts.Add($"  \"{jsonKey}\": {number}");
        }
        else
        {
            parts.Add($"  \"{jsonKey}\": {System.Text.Json.JsonSerializer.Serialize(raw)}");
        }
    }

    if (y.TryGetValue("adx_entitypermission_webrole", out var roles) && roles.Items.Count > 0)
    {
        var list = string.Join(",\n", roles.Items.Select(r => $"    \"{r}\""));
        parts.Add("  \"adx_entitypermission_webrole\": [\n" + list + "\n  ]");
    }

    sb.AppendLine(string.Join(",\n", parts));
    sb.Append('}');
    return sb.ToString();
}

/// <summary>
/// Enough YAML for these files: flat `key: value` pairs plus one `key:` followed by
/// `- item` lines. Deliberately not a general parser - anything richer belongs in a real
/// library, and these files are generated by the CLI in exactly this shape.
/// </summary>
Dictionary<string, YamlValue> ParseFlatYaml(string[] lines)
{
    var result = new Dictionary<string, YamlValue>(StringComparer.OrdinalIgnoreCase);
    string listKey = null;

    foreach (var line in lines)
    {
        if (string.IsNullOrWhiteSpace(line)) continue;

        if (line.TrimStart().StartsWith("- ") && listKey != null)
        {
            result[listKey].Items.Add(line.TrimStart().Substring(2).Trim().Trim('"', '\''));
            continue;
        }

        var colon = line.IndexOf(':');
        if (colon < 0) continue;

        var key = line.Substring(0, colon).Trim();
        var value = line.Substring(colon + 1).Trim().Trim('"', '\'');

        result[key] = new YamlValue { Scalar = value.Length == 0 ? null : value };
        listKey = value.Length == 0 ? key : null;
    }

    return result;
}

int RegisterAll(string orgUrl, string? dllPathArg)
{
    var dllPath = Path.GetFullPath(dllPathArg ?? Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..",
        "OutcomeTesting.Plugins", "bin", "Release", "net462", "OutcomeTesting.Plugins.dll"));
    if (!File.Exists(dllPath))
    {
        Console.Error.WriteLine($"Plug-in assembly not found: {dllPath}");
        return 1;
    }

    var contractsDir = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "customapi"));
    var contracts = Directory.GetFiles(contractsDir, "*.customapi.json").OrderBy(f => f).ToArray();
    if (contracts.Length == 0)
    {
        Console.Error.WriteLine($"No *.customapi.json contracts found in {contractsDir}.");
        return 1;
    }

    using var svc = Connect(orgUrl);

    Guid Upsert(string table, Entity values, params (string attr, object value)[] key)
    {
        var id = FindId(svc, table, key);
        if (id == Guid.Empty)
        {
            id = svc.Create(values);
            Console.WriteLine($"  created {table}: {id}");
        }
        else
        {
            values.Id = id;
            values.LogicalName = table;
            svc.Update(values);
            Console.WriteLine($"  updated {table}: {id}");
        }

        return id;
    }

    Console.WriteLine("Plug-in assembly…");
    var assemblyId = Upsert("pluginassembly", new Entity("pluginassembly")
    {
        ["name"] = AssemblyName,
        ["version"] = "1.0.0.0",
        ["culture"] = "neutral",
        ["publickeytoken"] = "86b764d5a2430b1f",
        ["sourcetype"] = new OptionSetValue(0),
        ["isolationmode"] = new OptionSetValue(2),
        ["content"] = Convert.ToBase64String(File.ReadAllBytes(dllPath)),
    }, ("name", AssemblyName));

    // Every API this run touched, so solution membership can be checked once at the end.
    var registered = new List<(string Name, Guid Id)>();

    foreach (var file in contracts)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var api = doc.RootElement.GetProperty("customApi");
        var typeName = api.GetProperty("pluginType").GetString()!;
        var apiName = api.GetProperty("uniquename").GetString()!;
        Console.WriteLine($"Command {apiName} ({Path.GetFileName(file)})…");

        var pluginTypeId = Upsert("plugintype", new Entity("plugintype")
        {
            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
            ["typename"] = typeName,
            ["friendlyname"] = typeName,
            ["name"] = typeName,
        }, ("typename", typeName));

        var customApiId = Upsert("customapi", new Entity("customapi")
        {
            ["uniquename"] = apiName,
            ["name"] = api.GetProperty("name").GetString(),
            ["displayname"] = api.GetProperty("displayname").GetString(),
            ["description"] = api.GetProperty("description").GetString(),
            ["bindingtype"] = new OptionSetValue(api.GetProperty("bindingtype").GetInt32()),
            ["isfunction"] = api.GetProperty("isfunction").GetBoolean(),
            ["isprivate"] = api.GetProperty("isprivate").GetBoolean(),
            ["allowedcustomprocessingsteptype"] = new OptionSetValue(api.GetProperty("allowedcustomprocessingsteptype").GetInt32()),
            ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
        }, ("uniquename", apiName));

        registered.Add((apiName, customApiId));

        foreach (var p in doc.RootElement.GetProperty("requestParameters").EnumerateArray())
        {
            var pn = p.GetProperty("uniquename").GetString()!;
            Upsert("customapirequestparameter", new Entity("customapirequestparameter")
            {
                ["customapiid"] = new EntityReference("customapi", customApiId),
                ["uniquename"] = pn,
                ["name"] = p.GetProperty("name").GetString(),
                ["displayname"] = p.GetProperty("displayname").GetString(),
                ["description"] = p.GetProperty("description").GetString(),
                ["type"] = new OptionSetValue(p.GetProperty("type").GetInt32()),
                ["isoptional"] = p.GetProperty("isoptional").GetBoolean(),
            }, ("uniquename", pn), ("customapiid", customApiId));
        }

        foreach (var p in doc.RootElement.GetProperty("responseProperties").EnumerateArray())
        {
            var pn = p.GetProperty("uniquename").GetString()!;
            Upsert("customapiresponseproperty", new Entity("customapiresponseproperty")
            {
                ["customapiid"] = new EntityReference("customapi", customApiId),
                ["uniquename"] = pn,
                ["name"] = p.GetProperty("name").GetString(),
                ["displayname"] = p.GetProperty("displayname").GetString(),
                ["description"] = p.GetProperty("description").GetString(),
                ["type"] = new OptionSetValue(p.GetProperty("type").GetInt32()),
            }, ("uniquename", pn), ("customapiid", customApiId));
        }
    }

    Console.WriteLine($"Done. Registered {contracts.Length} command(s) from {contractsDir}.");

    ReportSolutionMembership(svc, registered);
    return 0;
}

// Names any Custom API that this run registered but that is not a member of the shipping
// solution, and prints the exact command that fixes it.
//
// Registering an API creates it in the *Default* solution only. That is invisible until a
// promotion to TEST or PROD quietly ships without it — `al_DrainNotifications` was caught on
// 2026-09-04 only because someone thought to check by hand, which is not a control.
//
// This reports rather than adds. Adding requires a solution component type code, and pac
// rejects both the code it maps from `371` and the documented `10088` while accepting the
// *name* `CustomAPI` — so the numeric value this SDK call would need is exactly the thing
// that is not pinned down. Printing a verified-working command is honest; guessing a code
// against a live solution is not. Automating the add is a follow-up, once the code is
// confirmed against an environment.
static void ReportSolutionMembership(ServiceClient svc, List<(string Name, Guid Id)> apis)
{
    if (apis.Count == 0)
    {
        return;
    }

    var query = new QueryExpression("solutioncomponent")
    {
        ColumnSet = new ColumnSet("objectid"),
        Criteria = new FilterExpression(),
    };
    query.Criteria.AddCondition("objectid", ConditionOperator.In, apis.Select(a => (object)a.Id).ToArray());
    var link = query.AddLink("solution", "solutionid", "solutionid");
    link.LinkCriteria.AddCondition("uniquename", ConditionOperator.Equal, SolutionUniqueName);

    var members = new HashSet<Guid>(
        svc.RetrieveMultiple(query).Entities.Select(e => e.GetAttributeValue<Guid>("objectid")));

    var missing = apis.Where(a => !members.Contains(a.Id)).ToList();
    if (missing.Count == 0)
    {
        Console.WriteLine($"All {apis.Count} Custom API(s) are members of '{SolutionUniqueName}'.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"WARNING: {missing.Count} Custom API(s) are NOT in '{SolutionUniqueName}' and will not promote:");
    foreach (var api in missing)
    {
        Console.WriteLine($"  {api.Name} ({api.Id})");
        Console.WriteLine($"    pac solution add-solution-component --solutionUniqueName {SolutionUniqueName} \\");
        Console.WriteLine($"        --component {api.Id} --componentType CustomAPI --AddRequiredComponents");
    }
}

int VerifySignOff(string orgUrl, Guid caseId)
{
    using var svc = Connect(orgUrl);
    var pass = true;
    void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        pass &= ok;
    }

    var caseRef = NamedCase(svc, caseId);
    if (caseRef == null)
    {
        return 1;
    }

    var approvedAction = SeedCompletedAction(svc, caseRef.ToEntityReference(), "APPR");
    var rejectedAction = SeedCompletedAction(svc, caseRef.ToEntityReference(), "REJ");
    var noNotesAction = SeedCompletedAction(svc, caseRef.ToEntityReference(), "NON");
    Guid approvedSignoff = Guid.Empty, rejectedSignoff = Guid.Empty;

    try
    {
        // Approved: creates a sign-off and leaves the action Completed.
        var rApproved = svc.Execute(new OrganizationRequest("al_SignOffRemediation")
        {
            ["TargetId"] = approvedAction.ToString(),
            ["Decision"] = "Approved",
            ["IdempotencyKey"] = Guid.NewGuid().ToString(),
        });
        approvedSignoff = Guid.Parse((string)rApproved["SignoffId"]);
        Check("approved returns a sign-off", approvedSignoff != Guid.Empty, approvedSignoff.ToString());
        Check("approved decision is Approved", SignoffDecision(svc, approvedSignoff) == 120910720, "decision=Approved");
        Check("approved leaves action Completed", ActionStatus(svc, approvedAction) == 120910602, "status=Completed");

        // Rejected: requires notes, records them, reopens the action to In progress.
        var key = Guid.NewGuid().ToString();
        var rRejected = svc.Execute(new OrganizationRequest("al_SignOffRemediation")
        {
            ["TargetId"] = rejectedAction.ToString(),
            ["Decision"] = "Rejected",
            ["Notes"] = "Return: evidence still missing.",
            ["IdempotencyKey"] = key,
        });
        rejectedSignoff = Guid.Parse((string)rRejected["SignoffId"]);
        Check("rejected decision is Rejected", SignoffDecision(svc, rejectedSignoff) == 120910721, "decision=Rejected");
        var notes = svc.Retrieve("al_signoff", rejectedSignoff, new ColumnSet("al_notes")).GetAttributeValue<string>("al_notes");
        Check("rejected records notes", notes == "Return: evidence still missing.", $"notes={notes}");
        Check("rejected reopens action to In progress", ActionStatus(svc, rejectedAction) == 120910601, "status=In progress");
        Check("exactly one audit event", CountAudit(svc, key) == 1, $"count={CountAudit(svc, key)}");
        Check("audit command is SignOffRemediation", FirstAuditCommand(svc, key) == 120910757, "al_command=SignOffRemediation");

        // Idempotent replay must not create a second audit event.
        svc.Execute(new OrganizationRequest("al_SignOffRemediation")
        {
            ["TargetId"] = rejectedAction.ToString(),
            ["Decision"] = "Rejected",
            ["Notes"] = "Return: evidence still missing.",
            ["IdempotencyKey"] = key,
        });
        Check("idempotent replay (no duplicate audit)", CountAudit(svc, key) == 1, $"count={CountAudit(svc, key)}");

        // A rejected sign-off without notes must be refused (BR-008).
        var refused = false;
        try
        {
            svc.Execute(new OrganizationRequest("al_SignOffRemediation")
            {
                ["TargetId"] = noNotesAction.ToString(),
                ["Decision"] = "Rejected",
                ["IdempotencyKey"] = Guid.NewGuid().ToString(),
            });
        }
        catch (Exception)
        {
            refused = true;
        }

        Check("rejected without notes is refused", refused, "precondition enforced");
    }
    finally
    {
        if (approvedSignoff != Guid.Empty) { TryDelete(svc, "al_signoff", approvedSignoff); }
        if (rejectedSignoff != Guid.Empty) { TryDelete(svc, "al_signoff", rejectedSignoff); }
        TryDelete(svc, "al_remediationaction", approvedAction);
        TryDelete(svc, "al_remediationaction", rejectedAction);
        TryDelete(svc, "al_remediationaction", noNotesAction);
    }

    Console.WriteLine(pass ? "VERIFY SIGNOFF: PASS" : "VERIFY SIGNOFF: FAIL");
    return pass ? 0 : 2;
}

int VerifyRegrade(string orgUrl, Guid caseId)
{
    using var svc = Connect(orgUrl);
    var pass = true;
    void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        pass &= ok;
    }

    var caseRef = NamedCase(svc, caseId);
    var version = FirstEntity(svc, "al_checklistversion");
    if (caseRef == null)
    {
        return 1;
    }
    if (version == null)
    {
        Console.Error.WriteLine("Need an al_checklistversion to seed a verification outcome.");
        return 1;
    }

    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
    var reviewInstanceId = svc.Create(new Entity("al_reviewinstance")
    {
        ["al_name"] = "VERIFY Regrade RI",
        ["al_reviewinstancecode"] = "VRI-" + stamp,
        ["al_checklistversionid"] = version.ToEntityReference(),
        ["al_outcomecaseid"] = caseRef.ToEntityReference(),
        ["al_reviewstatus"] = new OptionSetValue(120910212), // Submitted
        ["al_reviewtype"] = new OptionSetValue(120910201),   // AQS
        ["al_sequence"] = 2,
    });
    var outcomeId = svc.Create(new Entity("al_outcome")
    {
        ["al_name"] = "VERIFY Regrade Outcome",
        ["al_outcomecode"] = "VOC-" + stamp,
        ["al_initialoutcome"] = new OptionSetValue(120910702), // Insufficient evidence
        ["al_outcomecaseid"] = caseRef.ToEntityReference(),
        ["al_reviewinstanceid"] = new EntityReference("al_reviewinstance", reviewInstanceId),
    });

    try
    {
        var key = Guid.NewGuid().ToString();
        var resp = svc.Execute(new OrganizationRequest("al_RegradeCase")
        {
            ["TargetId"] = outcomeId.ToString(),
            ["FinalOutcome"] = "Pass",
            ["Reason"] = "Regraded to Pass after further evidence.",
            ["IdempotencyKey"] = key,
        });
        Check("command returns final outcome", (string)resp["FinalOutcome"] == "Pass", $"FinalOutcome={resp["FinalOutcome"]}");

        var after = svc.Retrieve("al_outcome", outcomeId,
            new ColumnSet("al_initialoutcome", "al_finaloutcome", "al_regradereason", "al_regradedon", "al_finalisedon"));
        Check("final outcome is Pass", after.GetAttributeValue<OptionSetValue>("al_finaloutcome")?.Value == 120910710, "al_finaloutcome=Pass");
        Check("initial outcome preserved (BR-007)", after.GetAttributeValue<OptionSetValue>("al_initialoutcome")?.Value == 120910702, "al_initialoutcome=Insufficient evidence");
        Check("regrade reason recorded", after.GetAttributeValue<string>("al_regradereason") == "Regraded to Pass after further evidence.", "al_regradereason set");
        Check("regraded-on stamped", after.Contains("al_regradedon"), "al_regradedon set");
        Check("finalised-on stamped", after.Contains("al_finalisedon"), "al_finalisedon set");
        Check("exactly one audit event", CountAudit(svc, key) == 1, $"count={CountAudit(svc, key)}");
        Check("audit command is RegradeCase", FirstAuditCommand(svc, key) == 120910758, "al_command=RegradeCase");

        // Idempotent replay must not create a second audit event.
        svc.Execute(new OrganizationRequest("al_RegradeCase")
        {
            ["TargetId"] = outcomeId.ToString(),
            ["FinalOutcome"] = "Pass",
            ["Reason"] = "Regraded to Pass after further evidence.",
            ["IdempotencyKey"] = key,
        });
        Check("idempotent replay (no duplicate audit)", CountAudit(svc, key) == 1, $"count={CountAudit(svc, key)}");
    }
    finally
    {
        TryDelete(svc, "al_outcome", outcomeId);
        TryDelete(svc, "al_reviewinstance", reviewInstanceId);
    }

    Console.WriteLine(pass ? "VERIFY REGRADE: PASS" : "VERIFY REGRADE: FAIL");
    return pass ? 0 : 2;
}

static Guid SeedCompletedAction(ServiceClient svc, EntityReference caseRef, string tag)
{
    return svc.Create(new Entity("al_remediationaction")
    {
        ["al_name"] = "VERIFY SignOff " + tag,
        ["al_remediationactioncode"] = "VSO-" + tag + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
        ["al_description"] = "Temporary action created by the sign-off verification harness.",
        ["al_actionstatus"] = new OptionSetValue(120910602), // Completed
        ["al_outcomecaseid"] = caseRef,
    });
}

/// <summary>
/// Parses and confirms the target of a verify run, or explains what is missing and
/// returns null.
///
/// The verify modes create and mutate real business records and leave immutable Audit
/// Events behind. Previously they took the FIRST al_outcomecase in the org with no filter
/// and no environment guard, so running one against production wrote a permanent audit
/// trail onto a real client case describing work that never happened — falsifying exactly
/// the record AD-031 exists to protect. Naming the case, and repeating the org URL, makes
/// both choices deliberate.
/// </summary>
static Guid? VerificationTarget(string[] args)
{
    var orgUrl = args[1];

    if (args.Length < 3 || !Guid.TryParse(args[2], out var caseId))
    {
        Console.Error.WriteLine($"Usage: dotnet run -- {args[0]} <orgUrl> <caseId> --confirm <orgUrl>");
        Console.Error.WriteLine("  <caseId> is the al_outcomecase to seed against. Use a case created for testing:");
        Console.Error.WriteLine("  this run writes real records and leaves immutable audit events on that case.");
        return null;
    }

    var confirmIndex = Array.FindIndex(args, a => a.Equals("--confirm", StringComparison.OrdinalIgnoreCase));
    var confirmed = confirmIndex >= 0
        && confirmIndex + 1 < args.Length
        && args[confirmIndex + 1].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    if (!confirmed)
    {
        Console.Error.WriteLine($"Refusing to run: this writes real records to {orgUrl} and leaves immutable audit events.");
        Console.Error.WriteLine($"Re-run with --confirm {orgUrl} if that environment is a development environment.");
        return null;
    }

    return caseId;
}

/// <summary>The case named on the command line, or null with an explanation.</summary>
static Entity? NamedCase(ServiceClient svc, Guid caseId)
{
    try
    {
        var found = svc.Retrieve("al_outcomecase", caseId, new ColumnSet("al_name"));
        Console.WriteLine($"Verifying against case '{found.GetAttributeValue<string>("al_name")}' ({caseId}).");
        return found;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"No al_outcomecase {caseId} could be read: {ex.Message}");
        return null;
    }
}

static Entity? FirstEntity(ServiceClient svc, string table)
{
    return svc.RetrieveMultiple(new QueryExpression(table)
    {
        ColumnSet = new ColumnSet(false),
        TopCount = 1,
    }).Entities.FirstOrDefault();
}

static int? SignoffDecision(ServiceClient svc, Guid id)
{
    return svc.Retrieve("al_signoff", id, new ColumnSet("al_signoffdecision")).GetAttributeValue<OptionSetValue>("al_signoffdecision")?.Value;
}

static int? ActionStatus(ServiceClient svc, Guid id)
{
    return svc.Retrieve("al_remediationaction", id, new ColumnSet("al_actionstatus")).GetAttributeValue<OptionSetValue>("al_actionstatus")?.Value;
}

static int CountAudit(ServiceClient svc, string key)
{
    return svc.RetrieveMultiple(new QueryExpression("al_auditevent")
    {
        ColumnSet = new ColumnSet(false),
        Criteria = new FilterExpression { Conditions = { new ConditionExpression("al_idempotencykey", ConditionOperator.Equal, key) } },
    }).Entities.Count;
}

static int? FirstAuditCommand(ServiceClient svc, string key)
{
    var audit = svc.RetrieveMultiple(new QueryExpression("al_auditevent")
    {
        ColumnSet = new ColumnSet("al_command"),
        TopCount = 1,
        Criteria = new FilterExpression { Conditions = { new ConditionExpression("al_idempotencykey", ConditionOperator.Equal, key) } },
    }).Entities.FirstOrDefault();
    return audit?.GetAttributeValue<OptionSetValue>("al_command")?.Value;
}

static void TryDelete(ServiceClient svc, string table, Guid id)
{
    try
    {
        svc.Delete(table, id);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  cleanup: could not delete {table} {id}: {ex.Message.Split('\n')[0].Trim()}");
    }
}

// ---------------------------------------------------------------------------------------
// Portal repair commands (OD-034).
//
// `pac pages upload` aborts partway through on this site — on a single table-permission
// record that is already correct in the environment — and the abort lands *before* web pages
// are processed, so no web page change can be deployed by CLI at all. These two commands
// exist to apply the corrections that upload cannot, and only those.
//
// Deliberately narrow rather than a generic "set any field on any row" hatch. A generic
// setter would be one typo away from silently rewriting business data, and it would not
// know that a page template is a lookup while the authenticated-users flag is a bool. Each
// command names the fault it repairs, resolves its target by business name rather than by a
// hand-copied guid, and prints the value before and after so the change is evidenced in the
// console rather than asserted. They are repair tools; `pac pages upload` remains the
// deployment path once OD-034 is closed.
// ---------------------------------------------------------------------------------------

// Repoints every web page on a partial URL at a named page template (OD-035).
//
// A web page whose page template lookup is null cannot render at all — Power Pages returns
// the generic error page — which is exactly what a component-id collision leaves behind when
// the page template row is taken over by another component (AD-084).
//
// Both the root page and its language content page carry the lookup, so this matches on
// partial URL and fixes every row it finds rather than taking the first.
int RepointWebPage(string orgUrl, string partialUrl, string pageTemplateName)
{
    using var svc = Connect(orgUrl);

    var templateId = FindPortalId(svc, "mspp_pagetemplate", "mspp_name", pageTemplateName);
    if (templateId == Guid.Empty)
    {
        Console.Error.WriteLine($"No single page template named '{pageTemplateName}'. Nothing was changed.");
        return 1;
    }

    Console.WriteLine($"Page template '{pageTemplateName}' = {templateId}");

    var pages = PortalRows(svc, "mspp_webpage", "mspp_partialurl", partialUrl,
        "mspp_name", "mspp_pagetemplateid", "mspp_partialurl", "mspp_isroot");
    if (pages.Count == 0)
    {
        Console.Error.WriteLine($"No web page has partial URL '{partialUrl}'. Nothing was changed.");
        return 1;
    }

    var changed = 0;
    foreach (var page in pages)
    {
        var before = page.GetAttributeValue<EntityReference>("mspp_pagetemplateid");
        var root = page.GetAttributeValue<bool?>("mspp_isroot") == true ? "root" : "content";

        if (before != null && before.Id == templateId)
        {
            Console.WriteLine($"  {page.Id} ({root}): already correct, left alone.");
            continue;
        }

        Console.WriteLine($"  {page.Id} ({root}): {(before == null ? "<none>" : before.Id.ToString())} -> {templateId}");

        svc.Update(new Entity("mspp_webpage", page.Id)
        {
            ["mspp_pagetemplateid"] = new EntityReference("mspp_pagetemplate", templateId),
        });
        changed++;
    }

    Console.WriteLine($"Done. {changed} of {pages.Count} web page(s) on '{partialUrl}' updated.");
    return 0;
}

// Sets the authenticated-users flag on a named web role (OD-033).
//
// The flag auto-grants the role to every authenticated portal user, so a role carrying it by
// accident is a live over-grant rather than a cosmetic drift — and an upload will not
// necessarily clear it, which is why this is a deliberate correction and not another upload.
int SetWebRoleAuth(string orgUrl, string roleName, string value)
{
    if (!bool.TryParse(value, out var flag))
    {
        Console.Error.WriteLine("Value must be 'true' or 'false'.");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var roles = PortalRows(svc, "mspp_webrole", "mspp_name", roleName,
        "mspp_name", "mspp_authenticatedusersrole");
    if (roles.Count == 0)
    {
        Console.Error.WriteLine($"No web role named '{roleName}'. Nothing was changed.");
        return 1;
    }

    // More than one role sharing a name is itself a fault worth stopping on: picking one of
    // them would leave the other granting whatever this was meant to revoke.
    if (roles.Count > 1)
    {
        Console.Error.WriteLine($"{roles.Count} web roles are named '{roleName}'. Refusing to guess; nothing was changed.");
        return 1;
    }

    var role = roles[0];
    var before = role.GetAttributeValue<bool?>("mspp_authenticatedusersrole");
    Console.WriteLine($"Web role '{roleName}' ({role.Id}): authenticatedusersrole {before} -> {flag}");

    if (before == flag)
    {
        Console.WriteLine("Already correct, left alone.");
        return 0;
    }

    svc.Update(new Entity("mspp_webrole", role.Id)
    {
        ["mspp_authenticatedusersrole"] = flag,
    });

    Console.WriteLine("Updated.");
    return 0;
}

// Deletes a web role that exists only in the environment (OD-033, second half).
//
// `Checker` was created in DEV on 2026-09-03 and appears in no source file and in no pac
// manifest, which is what made it unreachable from the pipeline in both directions: an
// upload cannot remove a component it has never tracked. So the only way it leaves is a
// deliberate delete, and the only way it could have left otherwise was to declare it in
// source first — which would have meant keeping a role nobody has claimed.
//
// **It refuses to delete a role anything is bound to.** Web role bindings in the enhanced
// data model live inside the `content` JSON of powerpagecomponent rows rather than in link
// tables, so this scans every component on the site for the role's id. That check is the
// point of the command: deleting an empty role is tidying, and deleting one that grants
// something is a privilege change nobody asked for. The two are indistinguishable from the
// role row alone, which is exactly how it would go wrong.
int DeleteWebRole(string[] a)
{
    var orgUrl = a[1];
    var roleName = a[2];
    var confirmIndex = Array.FindIndex(a, x => x.Equals("--confirm", StringComparison.OrdinalIgnoreCase));
    if (confirmIndex < 0 || confirmIndex + 1 >= a.Length
        || !a[confirmIndex + 1].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("This PERMANENTLY DELETES a portal security role.");
        Console.Error.WriteLine("Usage: dotnet run -- deletewebrole <orgUrl> <roleName> --confirm <orgUrl>");
        return 1;
    }

    using var svc = Connect(orgUrl);

    var roles = PortalRows(svc, "mspp_webrole", "mspp_name", roleName,
        "mspp_name", "mspp_authenticatedusersrole", "mspp_anonymoususersrole");
    if (roles.Count == 0)
    {
        Console.WriteLine($"No web role named '{roleName}'. Nothing to delete.");
        return 0;
    }

    if (roles.Count > 1)
    {
        Console.Error.WriteLine($"{roles.Count} web roles are named '{roleName}'. Refusing to guess; nothing was deleted.");
        return 1;
    }

    var role = roles[0];
    Console.WriteLine($"Web role '{roleName}' ({role.Id:D}): "
        + $"authenticatedusersrole={role.GetAttributeValue<bool?>("mspp_authenticatedusersrole")}, "
        + $"anonymoususersrole={role.GetAttributeValue<bool?>("mspp_anonymoususersrole")}");

    var referencedBy = ComponentsReferencing(svc, role.Id);
    if (referencedBy.Count > 0)
    {
        Console.Error.WriteLine($"{referencedBy.Count} site component(s) reference this role. Refusing to delete:");
        foreach (var component in referencedBy)
        {
            Console.Error.WriteLine($"  {component.GetAttributeValue<string>("name")} "
                + $"(type {Formatted(component, "powerpagecomponenttype")}, {component.Id:D})");
        }

        return 1;
    }

    Console.WriteLine("  no site component references it - nothing is bound to this role.");
    svc.Delete("mspp_webrole", role.Id);

    // Verified by re-query, not by the delete returning. Every portal write in this tool is
    // checked this way, because an upload's exit code has already been shown to say nothing
    // about what landed (OD-034).
    var after = PortalRows(svc, "mspp_webrole", "mspp_name", roleName, "mspp_name");
    Console.WriteLine(after.Count == 0
        ? $"Deleted. No web role named '{roleName}' remains."
        : $"Delete returned success but {after.Count} row(s) named '{roleName}' remain.");
    return after.Count == 0 ? 0 : 2;
}

/// <summary>
/// Site components whose content mentions this id. Web role bindings live inside the
/// `content` JSON of a powerpagecomponent in the enhanced data model, so a substring match
/// on the id is what finds them — there is no link table to join.
/// </summary>
static List<Entity> ComponentsReferencing(ServiceClient svc, Guid roleId)
{
    var query = new QueryExpression("powerpagecomponent")
    {
        ColumnSet = new ColumnSet("name", "powerpagecomponenttype", "content"),
    };

    var needle = roleId.ToString("D");
    return svc.RetrieveMultiple(query).Entities
        .Where(c => c.Id != roleId)
        .Where(c => (c.GetAttributeValue<string>("content") ?? string.Empty)
            .IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
        .ToList();
}

// Adds a Power Pages site AND its site components to a solution (OD-034).
//
// Adding the site record alone is not enough and looks like it is: the solution then exports
// an `Assets/powerpagesites.xml` of a few hundred bytes carrying the site header — default
// language, header and footer template ids, domain — and not one web page, web template,
// table permission or web role. That was verified by export on 2026-09-05, and it is the
// trap this command exists to close.
//
// Why not `pac solution add-solution-component`: pac 2.11.2 (the latest published version)
// resolves component types from its own table, which has no Power Pages entries. It rejects
// the type *name* ("PowerPagesSite" silently falls back to Entity and fails) and it rejects
// the numeric value outright — "Component Type Id (10434) is not known". The SDK's
// AddSolutionComponentRequest takes the number directly, which is the whole reason this runs
// here rather than through pac.
//
// The type values are resolved from `solutioncomponentdefinition` at run time rather than
// hard-coded. Microsoft's own documentation gives two different numbers for the site in one
// example (10463 in the command, 10319 in the prose immediately below it), so a literal
// copied from the docs is not trustworthy; the environment is.
int AddSiteToSolution(string orgUrl, string solutionUniqueName)
{
    using var svc = Connect(orgUrl);

    var types = ComponentTypes(svc);
    foreach (var required in new[] { "powerpagesite", "powerpagesitelanguage", "powerpagecomponent" })
    {
        if (!types.ContainsKey(required))
        {
            Console.Error.WriteLine($"This environment has no solution component definition for '{required}'. Nothing was changed.");
            return 1;
        }

        Console.WriteLine($"  {required} = {types[required]}");
    }

    var sites = PortalRows(svc, "powerpagesite", "statecode", "0", "name");
    if (sites.Count != 1)
    {
        Console.Error.WriteLine($"Expected exactly one active Power Pages site, found {sites.Count}. Nothing was changed.");
        foreach (var s in sites)
        {
            Console.Error.WriteLine($"  {s.Id}  {s.GetAttributeValue<string>("name")}");
        }

        return 1;
    }

    var site = sites[0];
    Console.WriteLine($"Site '{site.GetAttributeValue<string>("name")}' = {site.Id}");
    Console.WriteLine($"Adding to solution '{solutionUniqueName}'…");

    var added = 0;
    var failed = 0;

    // AddRequiredComponents is deliberately false throughout. The site does not declare its
    // components as required — which is exactly why adding the site alone exported an empty
    // shell — so relying on it would silently under-add. Every component is named instead.
    void Add(Guid id, int type, string what)
    {
        try
        {
            svc.Execute(new AddSolutionComponentRequest
            {
                ComponentId = id,
                ComponentType = type,
                SolutionUniqueName = solutionUniqueName,
                AddRequiredComponents = false,
            });
            added++;
        }
        catch (Exception error)
        {
            failed++;
            if (failed <= 5)
            {
                Console.Error.WriteLine($"  {what} {id}: {error.Message}");
            }
        }
    }

    Add(site.Id, types["powerpagesite"], "site");

    var languages = PortalRows(svc, "powerpagesitelanguage", "powerpagesiteid", site.Id.ToString("D"), "name");
    foreach (var row in languages)
    {
        Add(row.Id, types["powerpagesitelanguage"], "language");
    }

    var components = PortalRows(svc, "powerpagecomponent", "powerpagesiteid", site.Id.ToString("D"), "name");
    foreach (var row in components)
    {
        Add(row.Id, types["powerpagecomponent"], "component");
    }

    Console.WriteLine(
        $"Done. site 1, languages {languages.Count}, components {components.Count} " +
        $"-> {added} added, {failed} failed.");

    // Export is the only thing that proves this worked: solution membership is not the same
    // claim as "the components travel".
    Console.WriteLine($"Verify with: pac solution export --name {solutionUniqueName} --path <zip> --overwrite");
    return failed == 0 ? 0 : 1;
}

// Power Pages solution component type values, by definition name, read from the environment.
static Dictionary<string, int> ComponentTypes(ServiceClient svc)
{
    const string fetch =
        "<fetch><entity name='solutioncomponentdefinition'>" +
        "<attribute name='name' /><attribute name='solutioncomponenttype' />" +
        "<filter type='and'><condition attribute='name' operator='like' value='powerpage%' /></filter>" +
        "</entity></fetch>";

    var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var row in svc.RetrieveMultiple(new FetchExpression(fetch)).Entities)
    {
        var name = row.GetAttributeValue<string>("name");
        var type = row.GetAttributeValue<int?>("solutioncomponenttype");
        if (!string.IsNullOrEmpty(name) && type.HasValue)
        {
            map[name] = type.Value;
        }
    }

    return map;
}

// Reads Power Pages rows with FetchXML rather than QueryExpression.
//
// Not a style preference. `FindId`'s QueryExpression returns nothing at all against the
// enhanced-data-model `mspp_*` tables on this site — `repointwebpage` reported "no page
// template named 'OT Case Detail Page'" for a row that a FetchXML query with the identical
// equality filter returns immediately. FetchXML is what demonstrably answers on these
// tables, so the portal commands use it and the rest of this tool is left alone.
//
// Values are XML-escaped: a page template or web role name is operator-supplied, and an
// apostrophe in one would otherwise break the query rather than fail to match.
static List<Entity> PortalRows(
    ServiceClient svc, string table, string filterAttr, string filterValue, params string[] columns)
{
    var attrs = string.Concat(columns.Select(c => $"<attribute name='{c}' />"));
    var fetch =
        $"<fetch><entity name='{table}'>{attrs}" +
        $"<filter type='and'><condition attribute='{filterAttr}' operator='eq' " +
        $"value='{System.Security.SecurityElement.Escape(filterValue)}' /></filter>" +
        "</entity></fetch>";

    return svc.RetrieveMultiple(new FetchExpression(fetch)).Entities.ToList();
}

// The id of the single row matching a name, or Guid.Empty when there is not exactly one.
// Ambiguity is deliberately not resolved by taking the first: two components sharing a name
// is the sort of drift these commands exist to repair, not something to pick a winner from.
static Guid FindPortalId(ServiceClient svc, string table, string nameAttr, string name)
{
    var rows = PortalRows(svc, table, nameAttr, name, nameAttr);
    return rows.Count == 1 ? rows[0].Id : Guid.Empty;
}

// Resolves the account a step runs as, by record id or UPN. Disabled users are excluded:
// a step impersonating a disabled account fails at run time with an error that says nothing
// about which account it is, so it is caught here where the name is still in hand.
static Guid ResolveSystemUser(ServiceClient svc, string nameOrId)
{
    if (Guid.TryParse(nameOrId, out var id))
    {
        return FindId(svc, "systemuser", ("systemuserid", id), ("isdisabled", false));
    }

    return FindId(svc, "systemuser", ("domainname", nameOrId), ("isdisabled", false));
}

static Guid FindId(ServiceClient svc, string table, params (string attr, object value)[] conditions)
{
    var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false), TopCount = 1 };
    foreach (var (attr, value) in conditions)
    {
        query.Criteria.AddCondition(attr, ConditionOperator.Equal, value);
    }

    return svc.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id ?? Guid.Empty;
}

// Creates/updates the two application security roles (AD-041) and grants the Dataverse
// privileges the RBAC layer needs, so real users resolve their roles and the command
// plug-ins can write on their behalf. Idempotent. Assign "App User" to everyone and
// "App Admin" to administrators (or add both roles to the relevant Dataverse teams).
int GrantSecurity(string orgUrl)
{
    using var svc = Connect(orgUrl);
    var buId = RootBusinessUnitId(svc);

    // App User: read the RBAC + export tables so the client resolves roles and lists exports.
    var userRole = EnsureRole(svc, "Outcome Testing App User", buId);
    GrantTable(svc, userRole, "al_userrolemapping", read: true);
    GrantTable(svc, userRole, "al_pagepermission", read: true);
    GrantTable(svc, userRole, "al_exportbatch", read: true);
    GrantTable(svc, userRole, "al_exportrecord", read: true);

    // App Admin: manage the permission model, generate exports and succeed questions.
    // Create/write on al_userrolemapping and al_pagepermission is admin-only so a user
    // cannot self-escalate by writing a mapping directly (the escalation-safe split).
    var adminRole = EnsureRole(svc, "Outcome Testing App Admin", buId);
    GrantTable(svc, adminRole, "al_userrolemapping", read: true, create: true, write: true, delete: true, append: true, appendTo: true);
    GrantTable(svc, adminRole, "al_pagepermission", read: true, create: true, write: true, delete: true, append: true, appendTo: true);
    GrantTable(svc, adminRole, "al_exportbatch", read: true, create: true, write: true, append: true, appendTo: true);
    GrantTable(svc, adminRole, "al_exportrecord", read: true, create: true, write: true, append: true, appendTo: true);
    GrantTable(svc, adminRole, "al_questionversion", read: true, create: true, write: true, append: true, appendTo: true);
    GrantTable(svc, adminRole, "al_question", read: true, appendTo: true);

    // Add both roles to the solution for clean ALM promotion (component type 20 = Role).
    AddRoleToSolution(svc, userRole, "OutcomeTesting");
    AddRoleToSolution(svc, adminRole, "OutcomeTesting");

    Console.WriteLine("Done. Roles ready: 'Outcome Testing App User' (assign to all), 'Outcome Testing App Admin' (assign to administrators).");
    return 0;
}

static void AddRoleToSolution(ServiceClient svc, Guid roleId, string solution)
{
    try
    {
        svc.Execute(new AddSolutionComponentRequest
        {
            ComponentId = roleId,
            ComponentType = 20, // Role
            SolutionUniqueName = solution,
            AddRequiredComponents = false,
        });
        Console.WriteLine($"  added role {roleId} to solution {solution}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  role {roleId} solution add skipped: {ex.Message.Split('\n')[0].Trim()}");
    }
}

static Guid RootBusinessUnitId(ServiceClient svc)
{
    var query = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 1 };
    query.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
    return svc.RetrieveMultiple(query).Entities.First().Id;
}

static Guid EnsureRole(ServiceClient svc, string name, Guid buId)
{
    var query = new QueryExpression("role") { ColumnSet = new ColumnSet("roleid"), TopCount = 1 };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, name);
    query.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, buId);
    var found = svc.RetrieveMultiple(query).Entities.FirstOrDefault();
    if (found != null)
    {
        Console.WriteLine($"  role exists: {name}");
        return found.Id;
    }

    var id = svc.Create(new Entity("role")
    {
        ["name"] = name,
        ["businessunitid"] = new EntityReference("businessunit", buId),
    });
    Console.WriteLine($"  created role: {name}");
    return id;
}

static void GrantTable(
    ServiceClient svc,
    Guid roleId,
    string table,
    bool read = false,
    bool create = false,
    bool write = false,
    bool delete = false,
    bool append = false,
    bool appendTo = false)
{
    var response = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = table,
        EntityFilters = EntityFilters.Privileges,
    });

    var wanted = new List<RolePrivilege>();
    foreach (var privilege in response.EntityMetadata.Privileges)
    {
        var want = privilege.PrivilegeType switch
        {
            PrivilegeType.Read => read,
            PrivilegeType.Create => create,
            PrivilegeType.Write => write,
            PrivilegeType.Delete => delete,
            PrivilegeType.Append => append,
            PrivilegeType.AppendTo => appendTo,
            _ => false,
        };
        if (want)
        {
            wanted.Add(new RolePrivilege { PrivilegeId = privilege.PrivilegeId, Depth = PrivilegeDepth.Global });
        }
    }

    if (wanted.Count > 0)
    {
        svc.Execute(new AddPrivilegesRoleRequest { RoleId = roleId, Privileges = wanted.ToArray() });
        Console.WriteLine($"  granted {wanted.Count} privilege(s) on {table}");
    }
}

/// <summary>
/// Creates the al_Notification outbox table and its columns (PP-15). Idempotent: the
/// entity and every column are created only when absent, so a re-run against a
/// half-created table finishes the job rather than failing on the first existing column.
///
/// Components are created directly into <paramref name="solutionUniqueName"/>. registerall
/// does not do that and the RBAC commands had to be added afterwards by a solution import
/// (AD-062's note); naming the solution up front avoids repeating that.
/// </summary>
int CreateNotificationTable(string orgUrl, string solutionUniqueName)
{
    using var svc = Connect(orgUrl);

    var exists = true;
    try
    {
        svc.Execute(new RetrieveEntityRequest
        {
            LogicalName = NotificationTable.Logical,
            EntityFilters = EntityFilters.Entity,
        });
    }
    catch (Exception)
    {
        exists = false;
    }

    if (!exists)
    {
        Console.WriteLine($"Creating table {NotificationTable.Schema}…");
        svc.Execute(new CreateEntityRequest
        {
            SolutionUniqueName = solutionUniqueName,
            Entity = new EntityMetadata
            {
                SchemaName = NotificationTable.Schema,
                LogicalName = NotificationTable.Logical,
                DisplayName = NotificationTable.Text("Notification"),
                DisplayCollectionName = NotificationTable.Text("Notifications"),
                Description = NotificationTable.Text(
                    "Outbox for PP-15 notification events. A row is written in the same transaction as the "
                    + "state change that caused it and drained separately by server-side email (AD-035, OD-030), "
                    + "which is what makes retries safe and duplicate sends impossible."),
                OwnershipType = OwnershipTypes.UserOwned,
                IsActivity = false,
                IsAuditEnabled = new BooleanManagedProperty(true),
            },
            PrimaryAttribute = new StringAttributeMetadata
            {
                SchemaName = "al_Name",
                LogicalName = "al_name",
                RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                MaxLength = 200,
                FormatName = StringFormatName.Text,
                DisplayName = NotificationTable.Text("Name"),
                Description = NotificationTable.Text("Human-readable label for the notification."),
            },
        });
        Console.WriteLine("  created.");
    }
    else
    {
        Console.WriteLine($"Table {NotificationTable.Schema} already exists; adding any missing columns.");
    }

    var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var current = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = NotificationTable.Logical,
        EntityFilters = EntityFilters.Attributes,
    });
    foreach (var attribute in current.EntityMetadata.Attributes)
    {
        present.Add(attribute.LogicalName);
    }

    void Add(AttributeMetadata attribute)
    {
        if (present.Contains(attribute.LogicalName))
        {
            Console.WriteLine($"  {attribute.LogicalName}: already present");
            return;
        }

        svc.Execute(new CreateAttributeRequest
        {
            SolutionUniqueName = solutionUniqueName,
            EntityName = NotificationTable.Logical,
            Attribute = attribute,
        });
        Console.WriteLine($"  {attribute.LogicalName}: created");
    }

    StringAttributeMetadata Str(string schema, string logical, int length, string display, string description,
        AttributeRequiredLevel level = AttributeRequiredLevel.None) =>
        new StringAttributeMetadata
        {
            SchemaName = schema,
            LogicalName = logical,
            MaxLength = length,
            FormatName = length > 2000 ? StringFormatName.TextArea : StringFormatName.Text,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(level),
            DisplayName = NotificationTable.Text(display),
            Description = NotificationTable.Text(description),
        };

    Add(Str("al_NotificationCode", "al_notificationcode", 100, "Notification code",
        "Deterministic per event and target, and the alternate key: a retry of the same state change collides here instead of queueing a second email.",
        AttributeRequiredLevel.ApplicationRequired));

    Add(new PicklistAttributeMetadata
    {
        SchemaName = "al_Event",
        LogicalName = "al_event",
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
        DisplayName = NotificationTable.Text("Event"),
        Description = NotificationTable.Text(
            "Which business event this notifies. The five AD-035 names only; PP-15's other four are not enumerated in any requirement (OD-030 gap (a))."),
        OptionSet = BuildOptionSet("al_notification_event", "Event", NotificationTable.Events),
    });

    Add(new PicklistAttributeMetadata
    {
        SchemaName = "al_Status",
        LogicalName = "al_status",
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
        DisplayName = NotificationTable.Text("Status"),
        Description = NotificationTable.Text("Where the row is in the outbox: Pending, Sent or Failed."),
        OptionSet = BuildOptionSet("al_notification_status", "Status", NotificationTable.Statuses),
    });

    Add(Str("al_RecipientEmail", "al_recipientemail", 200, "Recipient email",
        "Work email of the person to notify (AD-010, the canonical cross-system identifier)."));
    Add(Str("al_Subject", "al_subject", 400, "Subject", "Subject line of the email to send."));
    Add(Str("al_Body", "al_body", 4000, "Body", "Body of the email to send."));
    Add(Str("al_FailureReason", "al_failurereason", 2000, "Failure reason",
        "Why the send failed. Set only on Failed, so a stuck outbox says why rather than going quiet."));
    Add(Str("al_TargetTable", "al_targettable", 100, "Target table",
        "Logical name of the record the event happened to. A string pair rather than a lookup, matching al_auditevent, so the outbox does not constrain what it can point at."));
    Add(Str("al_TargetId", "al_targetid", 100, "Target id", "Id of the record the event happened to."));
    Add(Str("al_CorrelationId", "al_correlationid", 100, "Correlation id",
        "Plug-in execution correlation id, so a notification can be tied to the command that raised it (NFR-OBS-01)."));

    Add(new DateTimeAttributeMetadata
    {
        SchemaName = "al_QueuedOn",
        LogicalName = "al_queuedon",
        Format = DateTimeFormat.DateAndTime,
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
        DisplayName = NotificationTable.Text("Queued on"),
        Description = NotificationTable.Text("When the row was written, which is when the state change committed."),
    });

    Add(new DateTimeAttributeMetadata
    {
        SchemaName = "al_SentOn",
        LogicalName = "al_senton",
        Format = DateTimeFormat.DateAndTime,
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
        DisplayName = NotificationTable.Text("Sent on"),
        Description = NotificationTable.Text("When the send succeeded. Empty while Pending or Failed."),
    });

    // The alternate key is what actually enforces one notification per event per target:
    // a duplicate insert fails on the key rather than being deduplicated after the fact.
    var keys = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = NotificationTable.Logical,
        EntityFilters = EntityFilters.Entity,
    });
    var hasKey = keys.EntityMetadata.Keys != null
        && keys.EntityMetadata.Keys.Any(k => k.LogicalName == "al_notificationcodekey");
    if (!hasKey)
    {
        svc.Execute(new CreateEntityKeyRequest
        {
            EntityName = NotificationTable.Logical,
            SolutionUniqueName = solutionUniqueName,
            EntityKey = new EntityKeyMetadata
            {
                SchemaName = "al_NotificationCodeKey",
                LogicalName = "al_notificationcodekey",
                DisplayName = NotificationTable.Text("Notification code"),
                KeyAttributes = new[] { "al_notificationcode" },
            },
        });
        Console.WriteLine("  al_notificationcodekey: created");
    }
    else
    {
        Console.WriteLine("  al_notificationcodekey: already present");
    }

    svc.Execute(new PublishAllXmlRequest());
    Console.WriteLine($"Published. {NotificationTable.Schema} is in solution '{solutionUniqueName}'.");
    return 0;
}

OptionSetMetadata BuildOptionSet(string name, string display, (int Value, string Name, string Description)[] options)
{
    var set = new OptionSetMetadata
    {
        IsGlobal = false,
        OptionSetType = OptionSetType.Picklist,
        Name = name,
        DisplayName = NotificationTable.Text(display),
    };

    foreach (var option in options)
    {
        set.Options.Add(new OptionMetadata(NotificationTable.Text(option.Name), option.Value)
        {
            Description = NotificationTable.Text(option.Description),
        });
    }

    return set;
}

// Adds one value to `al_auditevent.al_command`, the option set every server-side command
// stamps on the audit row it writes.
//
// Minting a value is the whole of the OD-032 fix. `SetFailAccountability` shared
// `SetRoleAssignmentActive`'s 120910788, so its audit rows were labelled as role changes and
// `CommandHelpers.FindAuditByKey`, which scopes a replay lookup to (idempotency key,
// command), had nothing to tell the two apart. Adding a value is additive by construction:
// rows already written keep the value they carry, and NFR-AUD-01 makes that permanent, so
// the cut-over is a documented date rather than a data migration.
//
// Read back from metadata afterwards, because InsertOptionValue reports success on the
// definition it just changed and a caller that trusted it would never notice a failed
// publish.
int AddCommandValue(string orgUrl, int value, string label)
{
    using var svc = Connect(orgUrl);

    var before = CommandOptions(svc);
    if (before.TryGetValue(value, out var current))
    {
        Console.WriteLine($"al_command already carries {value} = '{current}'.");
        return string.Equals(current, label, StringComparison.Ordinal) ? 0 : 2;
    }

    var clash = before.FirstOrDefault(o => string.Equals(o.Value, label, StringComparison.OrdinalIgnoreCase));
    if (clash.Value != null)
    {
        Console.Error.WriteLine($"'{label}' is already {clash.Key}. Two values sharing a label is the fault this command exists to fix.");
        return 1;
    }

    svc.Execute(new InsertOptionValueRequest
    {
        EntityLogicalName = AuditEntity,
        AttributeLogicalName = CommandAttribute,
        Value = value,
        Label = new Label(label, 1033),
    });

    svc.Execute(new PublishXmlRequest
    {
        ParameterXml = $"<importexportxml><entities><entity>{AuditEntity}</entity></entities></importexportxml>",
    });

    var after = CommandOptions(svc);
    var ok = after.TryGetValue(value, out var written) && written == label;
    Console.WriteLine(ok
        ? $"al_command {value} = '{label}' inserted and published ({after.Count} values)."
        : $"Insert returned success, but metadata does not read back {value} = '{label}'.");
    return ok ? 0 : 2;
}

// Adds one value to any local choice column, for the case `addcommandvalue` does not cover:
// a value authored in `src/` that DEV does not yet carry.
//
// That case is a direction-of-travel problem rather than a metadata one. AD-013 makes DEV the
// source and `src/` the copy, so the round trip overwrites `src/`. A value added to an
// Entity.xml by hand is therefore live only until the next export, and deploying an assembly
// that names it before DEV carries it fails at runtime — an OptionSetValue the column does
// not define is refused, and inside a submit that takes the whole transaction down.
//
// Minting it here first puts the two in the supported order: DEV gains the value, the export
// carries it back into `src/`, and the hand edit is confirmed rather than clobbered. The
// table's rootcomponentbehavior decides whether it travels; `metadatamembership` reports it.
//
// Read back from metadata afterwards, for the reason AddCommandValue records: InsertOptionValue
// reports success on the definition it changed, not on the publish that makes it usable.
int AddOptionValue(string orgUrl, string entity, string attribute, int value, string label, string? description)
{
    using var svc = Connect(orgUrl);

    var before = PicklistOptions(svc, entity, attribute);
    if (before.TryGetValue(value, out var current))
    {
        Console.WriteLine($"{entity}.{attribute} already carries {value} = '{current}'.");
        return string.Equals(current, label, StringComparison.Ordinal) ? 0 : 2;
    }

    var clash = before.FirstOrDefault(o => string.Equals(o.Value, label, StringComparison.OrdinalIgnoreCase));
    if (clash.Value != null)
    {
        Console.Error.WriteLine($"'{label}' is already {clash.Key} on {entity}.{attribute}. Refusing to add a second value with the same label.");
        return 1;
    }

    var request = new InsertOptionValueRequest
    {
        EntityLogicalName = entity,
        AttributeLogicalName = attribute,
        Value = value,
        Label = new Label(label, 1033),
    };

    if (!string.IsNullOrWhiteSpace(description))
    {
        request.Description = new Label(description, 1033);
    }

    svc.Execute(request);

    svc.Execute(new PublishXmlRequest
    {
        ParameterXml = $"<importexportxml><entities><entity>{entity}</entity></entities></importexportxml>",
    });

    var after = PicklistOptions(svc, entity, attribute);
    var ok = after.TryGetValue(value, out var written) && written == label;
    Console.WriteLine(ok
        ? $"{entity}.{attribute} {value} = '{label}' inserted and published ({after.Count} values)."
        : $"Insert returned success, but metadata does not read back {value} = '{label}'.");
    return ok ? 0 : 2;
}

// Points a seeded case's adviser, para-planner and checker name at one real person.
//
// Seeding exists to exercise the paths that only run when a name resolves. Both ends of the
// BR-009 / BR-006 routing match a **name** against contact.fullname - the case carries
// al_paraplanner and al_advisername as text and no address (AD-082) - so a case seeded with
// "Seed Adviser 01" queues its notifications with no recipient and the drain marks them
// Failed, which exercises nothing. Naming a contact that actually exists is what makes the
// adviser letters and the para-planner notification deliverable.
//
// Matching is deliberately strict about ambiguity for the reason AD-082 gives: two people
// sharing a name leave ParaplannerEmail unable to choose, so this refuses a name that does
// not resolve to exactly one active contact with a work email rather than seeding data that
// will silently fail later.
//
// Written straight through the SDK rather than through al_UpdateCaseDetails. No step is
// registered on al_outcomecase, so nothing is bypassed, and an audit trail of seed rows being
// relabelled is noise rather than history.
int SetCasePeople(string orgUrl, string referenceLike, string personName, bool confirm)
{
    using var svc = Connect(orgUrl);

    var name = personName.Trim();
    var matches = svc.RetrieveMultiple(new FetchExpression(
        "<fetch top=\"5\"><entity name=\"contact\"><attribute name=\"contactid\"/>" +
        "<attribute name=\"emailaddress1\"/><filter>" +
        "<condition attribute=\"fullname\" operator=\"eq\" value=\"" + System.Security.SecurityElement.Escape(name) + "\"/>" +
        "<condition attribute=\"statecode\" operator=\"eq\" value=\"0\"/>" +
        "<condition attribute=\"emailaddress1\" operator=\"not-null\"/>" +
        "</filter></entity></fetch>")).Entities;

    if (matches.Count != 1)
    {
        Console.Error.WriteLine(
            $"'{name}' resolves to {matches.Count} active contact(s) with a work email, not one. " +
            "Seeding a name the notification paths cannot resolve would fail silently later (AD-082).");
        return 1;
    }

    Console.WriteLine($"'{name}' resolves to {matches[0].GetAttributeValue<string>("emailaddress1")}.");

    var cases = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_outcomecase\"><attribute name=\"al_outcomecaseid\"/>" +
        "<attribute name=\"al_casereference\"/><filter>" +
        "<condition attribute=\"al_casereference\" operator=\"like\" value=\"" + System.Security.SecurityElement.Escape(referenceLike) + "\"/>" +
        "</filter><order attribute=\"al_casereference\"/></entity></fetch>")).Entities;

    Console.WriteLine($"{cases.Count} case(s) matching '{referenceLike}'.");
    foreach (var c in cases)
    {
        Console.WriteLine($"   {c.GetAttributeValue<string>("al_casereference")}");
    }

    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run with --confirm <orgUrl> to write.");
        return 0;
    }

    foreach (var c in cases)
    {
        svc.Update(new Entity("al_outcomecase", c.Id)
        {
            ["al_advisername"] = name,
            ["al_paraplanner"] = name,
            ["al_checkername"] = name,
        });
    }

    Console.WriteLine($"Set adviser, para-planner and checker name to '{name}' on {cases.Count} case(s).");
    return 0;
}

// Stamps al_outcomecase.al_taxoutcome from the Q-TAX-02 answer of every submitted Tax review.
//
// SubmitReviewPlugin stamps it from 2026-09-11, so this is only for the checks submitted
// before the column existed. Those are the cases the bug was reported on: IO-300004 answered
// Q-TAX-02 Pass and closed, and every screen still read it as ungraded because the grade was
// only ever on the response and nothing displays responses.
//
// Idempotent and additive. A case that already carries a value is left alone, so a re-run
// after a later submit cannot overwrite a fresher grade with an older review's answer, and
// the newest submitted review wins where a case somehow carries two Tax legs.
int BackfillTaxOutcome(string orgUrl, bool confirm)
{
    using var svc = Connect(orgUrl);

    // Submitted Tax reviews, newest last, with the case they belong to and the answer
    // recorded against Q-TAX-02. One query rather than a read per review.
    var rows = svc.RetrieveMultiple(new FetchExpression(
        "<fetch><entity name=\"al_response\">" +
        "<attribute name=\"al_answerchoice\"/>" +
        "<link-entity name=\"al_questionversion\" from=\"al_questionversionid\" to=\"al_questionversionid\">" +
        "<link-entity name=\"al_question\" from=\"al_questionid\" to=\"al_questionid\">" +
        "<filter><condition attribute=\"al_questioncode\" operator=\"eq\" value=\"Q-TAX-02\"/></filter>" +
        "</link-entity></link-entity>" +
        "<link-entity name=\"al_reviewinstance\" from=\"al_reviewinstanceid\" to=\"al_reviewinstanceid\" alias=\"ri\">" +
        "<attribute name=\"al_submittedon\"/>" +
        "<filter><condition attribute=\"al_reviewstatus\" operator=\"eq\" value=\"120910212\"/></filter>" +
        "<link-entity name=\"al_outcomecase\" from=\"al_outcomecaseid\" to=\"al_outcomecaseid\" alias=\"oc\">" +
        "<attribute name=\"al_outcomecaseid\"/><attribute name=\"al_casereference\"/>" +
        "<attribute name=\"al_taxoutcome\"/>" +
        "</link-entity></link-entity>" +
        "<filter><condition attribute=\"al_answerchoice\" operator=\"not-null\"/></filter>" +
        "</entity></fetch>")).Entities;

    var pending = new List<(Guid CaseId, string Reference, int Choice, DateTime? SubmittedOn)>();
    var already = 0;

    foreach (var row in rows)
    {
        var caseId = Alias<Guid>(row, "oc.al_outcomecaseid");
        if (caseId == Guid.Empty)
        {
            continue;
        }

        if (Alias<OptionSetValue>(row, "oc.al_taxoutcome") != null)
        {
            already++;
            continue;
        }

        var choice = row.GetAttributeValue<OptionSetValue>("al_answerchoice");
        if (choice == null)
        {
            continue;
        }

        pending.Add((
            caseId,
            Alias<string>(row, "oc.al_casereference") ?? caseId.ToString("D"),
            choice.Value,
            Alias<DateTime?>(row, "ri.al_submittedon")));
    }

    // Newest submitted review wins, and one write per case.
    var byCase = pending
        .GroupBy(p => p.CaseId)
        .Select(g => g.OrderByDescending(p => p.SubmittedOn ?? DateTime.MinValue).First())
        .OrderBy(p => p.Reference, StringComparer.Ordinal)
        .ToList();

    Console.WriteLine($"{rows.Count} submitted Tax answer(s); {already} case(s) already stamped; {byCase.Count} to write.");
    foreach (var p in byCase)
    {
        Console.WriteLine($"   {p.Reference}  <- {p.Choice}");
    }

    if (!confirm)
    {
        Console.WriteLine("Dry run. Re-run with --confirm to write.");
        return 0;
    }

    var written = 0;
    foreach (var p in byCase)
    {
        svc.Update(new Entity("al_outcomecase", p.CaseId)
        {
            ["al_taxoutcome"] = new OptionSetValue(p.Choice),
        });
        written++;
    }

    Console.WriteLine($"Stamped {written} case(s).");
    return 0;
}

/// <summary>An aliased column from a link-entity, unwrapped, or default where it is absent.</summary>
static T Alias<T>(Entity row, string name)
{
    if (!row.Contains(name))
    {
        return default!;
    }

    var value = row[name];
    if (value is AliasedValue aliased)
    {
        value = aliased.Value;
    }

    return value is T typed ? typed : default!;
}

// Rewrites a column's description, the other half of a choice value authored in `src/`.
//
// A description is documentation, so it is tempting to leave it to the next export. It is
// not safe to: the round trip copies DEV over `src/`, so a description corrected by hand is
// reverted every time, and the correction is exactly the kind of edit nobody makes twice.
// al_notification.al_event said "the five AD-035 names only" while carrying six.
int SetAttributeDescription(string orgUrl, string entity, string attribute, string description)
{
    using var svc = Connect(orgUrl);

    var response = (RetrieveAttributeResponse)svc.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = entity,
        LogicalName = attribute,
        RetrieveAsIfPublished = false,
    });

    var metadata = response.AttributeMetadata;
    var before = metadata.Description?.UserLocalizedLabel?.Label ?? string.Empty;
    if (string.Equals(before, description, StringComparison.Ordinal))
    {
        Console.WriteLine($"{entity}.{attribute} already carries that description.");
        return 0;
    }

    metadata.Description = new Label(description, 1033);
    svc.Execute(new UpdateAttributeRequest
    {
        EntityName = entity,
        Attribute = metadata,
        MergeLabels = false,
    });

    svc.Execute(new PublishXmlRequest
    {
        ParameterXml = $"<importexportxml><entities><entity>{entity}</entity></entities></importexportxml>",
    });

    var after = (RetrieveAttributeResponse)svc.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = entity,
        LogicalName = attribute,
        RetrieveAsIfPublished = false,
    });

    var written = after.AttributeMetadata.Description?.UserLocalizedLabel?.Label ?? string.Empty;
    var ok = string.Equals(written, description, StringComparison.Ordinal);
    Console.WriteLine(ok
        ? $"{entity}.{attribute} description updated and published."
        : $"Update returned success, but metadata reads back: '{written}'.");
    return ok ? 0 : 2;
}

/// <summary>The values of any local picklist, by value, so a caller can assert rather than assume.</summary>
static Dictionary<int, string> PicklistOptions(ServiceClient svc, string entity, string attribute)
{
    var response = (RetrieveAttributeResponse)svc.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = entity,
        LogicalName = attribute,
        RetrieveAsIfPublished = false,
    });

    var options = ((PicklistAttributeMetadata)response.AttributeMetadata).OptionSet.Options;
    return options
        .Where(o => o.Value.HasValue)
        .ToDictionary(o => o.Value!.Value, o => o.Label?.UserLocalizedLabel?.Label ?? string.Empty);
}

/// <summary>`al_command` values by value, so a caller can assert rather than assume.</summary>
static Dictionary<int, string> CommandOptions(ServiceClient svc)
{
    var response = (RetrieveAttributeResponse)svc.Execute(new RetrieveAttributeRequest
    {
        EntityLogicalName = AuditEntity,
        LogicalName = CommandAttribute,
        RetrieveAsIfPublished = false,
    });

    var options = ((PicklistAttributeMetadata)response.AttributeMetadata).OptionSet.Options;
    return options
        .Where(o => o.Value.HasValue)
        .ToDictionary(o => o.Value!.Value, o => o.Label?.UserLocalizedLabel?.Label ?? string.Empty);
}

// Proves PP-15 end to end in one run: causes a qualifying event, watches the outbox row it
// writes reach Sent, and names the email that carried it.
//
// Every piece of this path had been verified alone and never together. The emitters were
// deployed 2026-09-03 and the drain step registered 2026-09-05, and on that date
// `al_notification` held zero rows — an empty outbox, not a drained one. So nothing had ever
// travelled emitter -> outbox row -> asynchronous drain -> server-side email, and PP-15 was
// switched on rather than proven. This is the run that closes the difference.
//
// **It writes real business records and sends real email**, which is why it takes --confirm
// with the org URL repeated. The blast radius is held to rows this command created:
//
// - it seeds its own `al_outcomecase` rather than allocating a case someone is working on;
// - it allocates to the account the drain runs as, so the email arrives at the service
//   mailbox rather than a colleague's inbox;
// - it deletes the assignment and the case afterwards.
//
// What it deliberately does not delete is the `al_notification` row. That row is the
// evidence — the first notification this environment has produced — and an outbox row whose
// target is gone is exactly what a proof run should leave behind.
int ProvePp15(string[] a)
{
    var orgUrl = a[1];
    var confirmIndex = Array.FindIndex(a, x => x.Equals("--confirm", StringComparison.OrdinalIgnoreCase));
    if (confirmIndex < 0 || confirmIndex + 1 >= a.Length
        || !a[confirmIndex + 1].TrimEnd('/').Equals(orgUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("This CREATES REAL RECORDS and SENDS REAL EMAIL from the service mailbox.");
        Console.Error.WriteLine("Usage: dotnet run -- provepp15 <orgUrl> --confirm <orgUrl>");
        return 1;
    }

    using var svc = Connect(orgUrl);
    var pass = true;
    void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        pass &= ok;
    }

    // Preconditions. Read-only, and nothing is written unless every one of them holds: a
    // proof run that seeded records and then found the emitter switched off would leave
    // business rows behind to explain a fault it could have reported without writing at all.
    var emitter = StepState(svc, "NotificationEmitterPlugin: Create of al_caseassignment");
    Check("emitter step registered on Create of al_caseassignment",
        emitter != null && emitter.Enabled, emitter == null ? "not registered" : emitter.Describe());

    var drain = StepState(svc, "NotificationDrainPlugin: Create of al_notification");
    Check("drain step registered, asynchronous, enabled",
        drain != null && drain.Enabled && drain.Mode == 1, drain == null ? "not registered" : drain.Describe());

    var sender = drain?.RunAs ?? Guid.Empty;
    Check("drain runs as a named account", sender != Guid.Empty,
        sender == Guid.Empty ? "no impersonating user - the drain has no mailbox to send from" : sender.ToString("D"));

    var senderRow = sender == Guid.Empty
        ? null
        : svc.Retrieve("systemuser", sender, new ColumnSet("internalemailaddress", "fullname"));
    var senderAddress = senderRow?.GetAttributeValue<string>("internalemailaddress");
    Check("sending account has a work email", !string.IsNullOrWhiteSpace(senderAddress), senderAddress ?? "none");

    if (sender != Guid.Empty)
    {
        var mailbox = Mailbox(svc, sender);
        var approved = mailbox?.GetAttributeValue<bool?>("isemailaddressapprovedbyo365admin") == true;
        var outgoing = mailbox == null ? "no mailbox row" : Formatted(mailbox, "outgoingemailstatus");
        Check("mailbox approved by the O365 admin", approved, approved ? "Yes" : "No");

        // Approved and tested are different facts, and the gap between them is the trap this
        // project already walked up to: immediately after approval the mailbox read Yes and
        // Not Run, and draining in that window would have stamped the backlog Failed.
        Check("mailbox outgoing test succeeded", outgoing == "Success", outgoing);
    }

    if (!pass)
    {
        Console.Error.WriteLine("PROVE PP-15: preconditions failed. Nothing was written.");
        return 2;
    }

    var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
    var reference = "PP15-PROOF-" + stamp;
    var caseId = Guid.Empty;
    var assignmentId = Guid.Empty;

    try
    {
        caseId = svc.Create(new Entity("al_outcomecase")
        {
            ["al_name"] = "PP-15 proof " + stamp,
            ["al_casereference"] = reference,
            ["al_casestatus"] = new OptionSetValue(CaseStatusQueued),
        });
        Console.WriteLine($"  seeded al_outcomecase {caseId:D} ({reference}).");

        assignmentId = svc.Create(new Entity("al_caseassignment")
        {
            ["al_name"] = "PP-15 proof allocation " + stamp,
            ["al_caseassignmentcode"] = "PP15-" + stamp,
            ["al_outcomecaseid"] = new EntityReference("al_outcomecase", caseId),
            ["al_assigneduserid"] = new EntityReference("systemuser", sender),
            ["al_assignedon"] = DateTime.UtcNow,
            ["al_isactive"] = true,
        });
        Console.WriteLine($"  created al_caseassignment {assignmentId:D} - this is the qualifying event.");

        // The emitter is synchronous, so the row exists by the time Create returns or it
        // never will. Polling for it would only turn a missing emitter into a slow timeout.
        var code = "ALLOCATION-" + assignmentId.ToString("N").ToUpperInvariant();
        var row = NotificationByCode(svc, code);
        Check("emitter wrote an outbox row", row != null, code);
        if (row == null)
        {
            Console.Error.WriteLine("PROVE PP-15: FAIL - no notification was queued.");
            return 2;
        }

        Console.WriteLine($"  al_notification {row.Id:D}");
        Check("queued to the allocated person", row.GetAttributeValue<string>("al_recipientemail") == senderAddress,
            row.GetAttributeValue<string>("al_recipientemail") ?? "(none)");
        Check("event is Allocation", row.GetAttributeValue<OptionSetValue>("al_event")?.Value == EventAllocation,
            Formatted(row, "al_event"));

        // The drain is asynchronous, so the row stays Pending for as long as the platform
        // takes to pick the job up. Waiting is the honest way to read that; a single read
        // straight after the create would report Pending and prove nothing.
        var deadline = DateTime.UtcNow.AddSeconds(DrainWaitSeconds);
        while (row.GetAttributeValue<OptionSetValue>("al_status")?.Value == StatusPending && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5000);
            row = NotificationByCode(svc, code) ?? row;
            Console.WriteLine($"  waiting for the drain: {Formatted(row, "al_status")}");
        }

        var status = row.GetAttributeValue<OptionSetValue>("al_status")?.Value;
        Check("outbox row reached Sent", status == StatusSent, Formatted(row, "al_status"));
        if (status == StatusFailed)
        {
            Console.WriteLine($"  al_failurereason: {row.GetAttributeValue<string>("al_failurereason")}");
        }

        var sentOn = row.GetAttributeValue<DateTime?>("al_senton");
        Check("send timestamped", sentOn.HasValue, sentOn?.ToString("u") ?? "not stamped");

        // Named, not inferred. Sent on the outbox row means the drain handed the message to
        // Dataverse; the email activity is where delivery itself is readable afterwards.
        var queuedOn = row.GetAttributeValue<DateTime?>("al_queuedon") ?? DateTime.UtcNow.AddMinutes(-15);
        var email = LatestEmail(svc, row.GetAttributeValue<string>("al_subject"), queuedOn);
        Check("email activity created", email != null, email == null ? "none found" : $"{email.Id:D}");
        if (email != null)
        {
            Console.WriteLine($"  email subject: {email.GetAttributeValue<string>("subject")}");
            Console.WriteLine($"  email status: {Formatted(email, "statuscode")}, created {email.GetAttributeValue<DateTime?>("createdon"):u}");
        }
    }
    finally
    {
        // Assignment first: it points at the case, and deleting the case would otherwise
        // have to cascade to reach it.
        if (assignmentId != Guid.Empty) { TryDelete(svc, "al_caseassignment", assignmentId); }
        if (caseId != Guid.Empty) { TryDelete(svc, "al_outcomecase", caseId); }
        Console.WriteLine("  seeded case and assignment deleted. The al_notification row is left as evidence.");
    }

    Console.WriteLine(pass ? "PROVE PP-15: PASS" : "PROVE PP-15: FAIL");
    return pass ? 0 : 2;
}

// Read-only. Prints the whole outbox and, for each row, the email the drain produced.
//
// The distinction it exists to make: `Sent` on an al_notification row means the drain handed
// the message to Dataverse, which is the last thing the drain can observe. Whether it left
// the mailbox is on the email activity, and the two are hours apart when server-side email
// is backed up. Reading only the outbox is how a queue that is quietly not sending looks
// healthy — the failure OD-030 warned about, one layer further out.
int Pp15Evidence(string orgUrl)
{
    using var svc = Connect(orgUrl);

    var query = new QueryExpression("al_notification")
    {
        ColumnSet = new ColumnSet("al_notificationcode", "al_status", "al_event", "al_recipientemail", "al_subject", "al_queuedon", "al_senton", "al_failurereason"),
    };
    query.Orders.Add(new OrderExpression("al_queuedon", OrderType.Ascending));

    var rows = svc.RetrieveMultiple(query).Entities;
    Console.WriteLine($"al_notification: {rows.Count} row(s).");

    foreach (var row in rows)
    {
        Console.WriteLine();
        Console.WriteLine($"  {row.GetAttributeValue<string>("al_notificationcode")}");
        Console.WriteLine($"    {Formatted(row, "al_event")} -> {row.GetAttributeValue<string>("al_recipientemail") ?? "(no recipient)"}");
        Console.WriteLine($"    status {Formatted(row, "al_status")}, queued {row.GetAttributeValue<DateTime?>("al_queuedon"):u}, sent {row.GetAttributeValue<DateTime?>("al_senton"):u}");

        var reason = row.GetAttributeValue<string>("al_failurereason");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Console.WriteLine($"    failure: {reason}");
        }

        // Subject prefix, not equality: Dataverse appends a tracking token to the subject it
        // stores ("… CRM:0249002"), so the email never carries the string the outbox recorded.
        var subject = row.GetAttributeValue<string>("al_subject");
        var emails = string.IsNullOrWhiteSpace(subject) ? new List<Entity>() : EmailsBySubjectPrefix(svc, subject);
        if (emails.Count == 0)
        {
            Console.WriteLine("    email: none found");
        }

        // Both copies are printed, because they answer different questions. The outbound
        // activity says the drain composed and issued a message; an inbound copy of the same
        // subject says server-side email delivered it and synchronisation tracked it back —
        // which is the only evidence here that anything actually arrived.
        foreach (var email in emails)
        {
            Console.WriteLine(
                $"    email {email.Id:D}: {Formatted(email, "directioncode")}, {Formatted(email, "statuscode")}, "
                + $"created {email.GetAttributeValue<DateTime?>("createdon"):u}");
        }
    }

    return 0;
}

/// <summary>
/// Every email whose subject starts with what the outbox recorded, newest first.
///
/// Prefix, not equality: Dataverse appends a tracking token to the subject it stores
/// ("… CRM:0249002"), so an exact match on the outbox's own subject finds nothing. That cost
/// a FAIL on the first proof run and is the sort of thing that reads as "no email was sent".
/// </summary>
static List<Entity> EmailsBySubjectPrefix(ServiceClient svc, string subject)
{
    var query = new QueryExpression("email")
    {
        ColumnSet = new ColumnSet("subject", "statuscode", "directioncode", "createdon"),
        Criteria = new FilterExpression(),
    };
    query.Criteria.AddCondition("subject", ConditionOperator.BeginsWith, subject);
    query.Orders.Add(new OrderExpression("createdon", OrderType.Descending));
    return svc.RetrieveMultiple(query).Entities.ToList();
}

/// <summary>The step registered under <paramref name="stepName"/>, or null when there is none.</summary>
static StepFacts? StepState(ServiceClient svc, string stepName)
{
    var query = new QueryExpression("sdkmessageprocessingstep")
    {
        ColumnSet = new ColumnSet("statecode", "mode", "stage", "impersonatinguserid"),
        TopCount = 1,
        Criteria = new FilterExpression(),
    };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, stepName);

    var row = svc.RetrieveMultiple(query).Entities.FirstOrDefault();
    return row == null
        ? null
        : new StepFacts(
            row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0,
            row.GetAttributeValue<OptionSetValue>("mode")?.Value ?? 0,
            row.GetAttributeValue<OptionSetValue>("stage")?.Value ?? 0,
            row.GetAttributeValue<EntityReference>("impersonatinguserid")?.Id ?? Guid.Empty);
}

/// <summary>The mailbox row for a user, which is where approval and the outgoing test live.</summary>
static Entity? Mailbox(ServiceClient svc, Guid userId)
{
    var query = new QueryExpression("mailbox")
    {
        ColumnSet = new ColumnSet("isemailaddressapprovedbyo365admin", "outgoingemailstatus", "testmailboxaccesscompletedon"),
        TopCount = 1,
        Criteria = new FilterExpression(),
    };
    query.Criteria.AddCondition("regardingobjectid", ConditionOperator.Equal, userId);
    return svc.RetrieveMultiple(query).Entities.FirstOrDefault();
}

/// <summary>One outbox row by its alternate key, with everything a proof run reports on.</summary>
static Entity? NotificationByCode(ServiceClient svc, string code)
{
    var query = new QueryExpression("al_notification")
    {
        ColumnSet = new ColumnSet("al_status", "al_event", "al_recipientemail", "al_subject", "al_senton", "al_failurereason", "al_queuedon"),
        TopCount = 1,
        Criteria = new FilterExpression(),
    };
    query.Criteria.AddCondition("al_notificationcode", ConditionOperator.Equal, code);
    return svc.RetrieveMultiple(query).Entities.FirstOrDefault();
}

/// <summary>
/// The email the drain sent for this notification.
///
/// Matched on subject because the drain sets no regardingobjectid — deliberately, since a
/// regarding object would need activities enabled on all five target tables in exchange for
/// a link the body already spells out.
///
/// The fallback to "newest email since the row was queued" is not slack in the assertion. A
/// subject that does not match is a real finding — it would mean the email carried something
/// other than what the outbox recorded — and it is only findable if the search does not stop
/// at the exact match. The caller prints the subject it found, so the two are compared by a
/// person rather than collapsed into a pass.
/// </summary>
static Entity? LatestEmail(ServiceClient svc, string? subject, DateTime since)
{
    Entity? Newest(Action<FilterExpression> criteria)
    {
        var query = new QueryExpression("email")
        {
            ColumnSet = new ColumnSet("subject", "statuscode", "createdon"),
            TopCount = 1,
            Criteria = new FilterExpression(),
        };
        criteria(query.Criteria);
        query.Orders.Add(new OrderExpression("createdon", OrderType.Descending));
        return svc.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    if (!string.IsNullOrWhiteSpace(subject))
    {
        var exact = Newest(c => c.AddCondition("subject", ConditionOperator.Equal, subject));
        if (exact != null)
        {
            return exact;
        }
    }

    // A minute of slack before the queue time: the row and the email are stamped by
    // different clocks, and a send that beat its own outbox row by a second is not a miss.
    return Newest(c => c.AddCondition("createdon", ConditionOperator.OnOrAfter, since.AddMinutes(-1)));
}

/// <summary>An option set or status read as its label, so evidence reads the way a person would.</summary>
static string Formatted(Entity row, string attribute) =>
    row.FormattedValues.Contains(attribute) ? row.FormattedValues[attribute] : "(none)";

// Reports which `al_` tables are members of the shipping solution, and how.
//
// The distinction that matters is `rootcomponentbehavior`, not membership. A table added
// with **Include subcomponents** carries every attribute, option value, form and view it
// has, now and later — so an option minted afterwards through the metadata API travels with
// it and nothing needs re-adding. A table added as a **shell**, or with named subcomponents
// only, carries what was listed and silently drops the rest, which is the version of this
// that fails at import rather than at export.
//
// Worth being explicit, because it is a common misreading: adding an option value without
// naming a solution does not put the option "in the default solution" in a way that excludes
// it from this one. Unmanaged customisations all land in the same Active layer. What decides
// whether the shipping solution carries the option is whether the solution holds the
// attribute — directly, or through a table that includes its subcomponents.
int MetadataMembership(string orgUrl)
{
    using var svc = Connect(orgUrl);

    var solutionId = FindId(svc, "solution", ("uniquename", SolutionUniqueName));
    if (solutionId == Guid.Empty)
    {
        Console.Error.WriteLine($"No solution '{SolutionUniqueName}'.");
        return 1;
    }

    var members = SolutionMembers(svc, solutionId);
    Console.WriteLine($"{SolutionUniqueName} ({solutionId:D}): {members.Count} component(s).");

    var all = ((RetrieveAllEntitiesResponse)svc.Execute(new RetrieveAllEntitiesRequest
    {
        EntityFilters = EntityFilters.Relationships,
        RetrieveAsIfPublished = false,
    })).EntityMetadata;

    var entities = all
        .Where(e => e.LogicalName != null && e.LogicalName.StartsWith("al_", StringComparison.Ordinal))
        .OrderBy(e => e.LogicalName, StringComparer.Ordinal)
        .ToList();

    // Every many-to-many relationship in the environment, by the intersect table it uses.
    // An intersect table is never a solution component in its own right — the relationship
    // is (component type 10) — so checking it as a table reports a false gap, which is
    // exactly what the first run of this report did.
    var intersects = all
        .SelectMany(e => e.ManyToManyRelationships ?? Array.Empty<ManyToManyRelationshipMetadata>())
        .Where(r => r.IntersectEntityName != null)
        .GroupBy(r => r.IntersectEntityName!, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    Console.WriteLine();
    Console.WriteLine($"al_ tables in the environment: {entities.Count}");

    var missing = new List<string>();
    var shallow = new List<string>();

    foreach (var entity in entities)
    {
        var name = entity.LogicalName!;
        var id = entity.MetadataId ?? Guid.Empty;

        if (entity.IsIntersect == true || intersects.ContainsKey(name))
        {
            // An intersect table is not a solution component, and neither, in this solution,
            // is the relationship that owns it: **a many-to-many relationship travels as a
            // subcomponent of the tables it joins**, so it has no `solutioncomponent` row of
            // its own even when the export carries it in full. Looking for one reports a gap
            // that is not there — this report did exactly that before it was checked against
            // an actual export on 2026-09-06, which carried both relationships and all 149.
            //
            // The real test is therefore the participants: if both ends are members that
            // include their subcomponents, the relationship promotes.
            var relationship = intersects.TryGetValue(name, out var r) ? r : null;
            // Either end, not both. Verified against the 2026-09-06 export: it carries
            // `al_contact_al_outcomecase` in full even though `contact` is not a member,
            // because `al_outcomecase` is — one participating table including its
            // subcomponents is enough to bring the relationship with it.
            var carried = relationship != null
                && (Member(members, all, relationship.Entity1LogicalName)
                    || Member(members, all, relationship.Entity2LogicalName));

            Console.WriteLine($"  [{(carried ? "via an end" : "NEITHER END"),-16}] {name}"
                + (relationship == null ? "  (intersect)" : $"  (intersect for {relationship.SchemaName})"));
            if (!carried)
            {
                missing.Add(name);
            }

            continue;
        }

        if (!members.TryGetValue((1, id), out var behavior))
        {
            Console.WriteLine($"  [{"NOT A MEMBER",-16}] {name}");
            missing.Add(name);
            continue;
        }

        Console.WriteLine($"  [{BehaviorLabel(behavior),-16}] {name}");
        if (behavior != 0)
        {
            shallow.Add(name);
        }
    }

    // Attributes held as components in their own right. On a table that already includes its
    // subcomponents these are redundant, and their absence proves nothing — which is exactly
    // why they are reported separately rather than folded into the table's line.
    var attributeMembers = members.Keys.Count(k => k.Type == 2);
    Console.WriteLine();
    Console.WriteLine($"Attributes held as components in their own right: {attributeMembers}");

    Console.WriteLine();
    if (missing.Count == 0 && shallow.Count == 0)
    {
        Console.WriteLine("Every al_ table is carried: real tables as members including their");
        Console.WriteLine("subcomponents, intersect tables through their relationship. Options minted");
        Console.WriteLine("after a table was added therefore travel with the export - no re-adding.");
        return 0;
    }

    foreach (var name in missing)
    {
        Console.WriteLine($"  {name}: not carried - add it, or add the tables its relationship joins");
    }

    foreach (var name in shallow)
    {
        Console.WriteLine($"  addmetadatatosolution <orgUrl> {name}      # member, but not with subcomponents");
    }

    return 2;
}

/// <summary>True when a table is a solution member that includes its subcomponents.</summary>
static bool Member(Dictionary<(int Type, Guid Id), int> members, IEnumerable<EntityMetadata> all, string? logicalName)
{
    var entity = all.FirstOrDefault(e => string.Equals(e.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));
    return entity?.MetadataId is Guid id && members.TryGetValue((1, id), out var behavior) && behavior == 0;
}

/// <summary>Solution components as (componenttype, objectid) -> rootcomponentbehavior.</summary>
static Dictionary<(int Type, Guid Id), int> SolutionMembers(ServiceClient svc, Guid solutionId)
{
    var query = new QueryExpression("solutioncomponent")
    {
        ColumnSet = new ColumnSet("componenttype", "objectid", "rootcomponentbehavior"),
        Criteria = new FilterExpression(),
    };
    query.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);

    var map = new Dictionary<(int, Guid), int>();
    foreach (var row in RetrieveAll(svc, query))
    {
        var type = row.GetAttributeValue<OptionSetValue>("componenttype")?.Value;
        var objectId = row.GetAttributeValue<Guid?>("objectid");
        if (type.HasValue && objectId.HasValue)
        {
            map[(type.Value, objectId.Value)] = row.GetAttributeValue<OptionSetValue>("rootcomponentbehavior")?.Value ?? 0;
        }
    }

    return map;
}

/// <summary>
/// Every page of a query. The 5000-row default cap would silently truncate a solution this
/// size, and a truncated membership list reads as "not a member" — the one wrong answer this
/// report must not give.
/// </summary>
static List<Entity> RetrieveAll(ServiceClient svc, QueryExpression query)
{
    var results = new List<Entity>();
    query.PageInfo = new PagingInfo { PageNumber = 1, Count = 500 };

    while (true)
    {
        var page = svc.RetrieveMultiple(query);
        results.AddRange(page.Entities);
        if (!page.MoreRecords)
        {
            return results;
        }

        query.PageInfo.PageNumber++;
        query.PageInfo.PagingCookie = page.PagingCookie;
    }
}

static string BehaviorLabel(int behavior) => behavior switch
{
    0 => "subcomponents",
    1 => "NO subcomponents",
    2 => "SHELL ONLY",
    _ => $"behavior {behavior}",
};

// Adds a table to the solution with all of its subcomponents, or one named attribute.
//
// `pac solution add-solution-component` can do the table; it is here because the whole point
// is the *behaviour*, and adding a table that is already a shell member has to replace that
// membership rather than report "already present" and leave the shell in place.
int AddMetadataToSolution(string orgUrl, string entityName, string? attributeName)
{
    using var svc = Connect(orgUrl);

    var entity = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entityName,
        EntityFilters = EntityFilters.Attributes,
        RetrieveAsIfPublished = false,
    })).EntityMetadata;

    if (string.IsNullOrWhiteSpace(attributeName))
    {
        svc.Execute(new AddSolutionComponentRequest
        {
            ComponentId = entity.MetadataId ?? Guid.Empty,
            ComponentType = 1,
            SolutionUniqueName = SolutionUniqueName,
            AddRequiredComponents = false,
            DoNotIncludeSubcomponents = false,
        });
        Console.WriteLine($"Added table {entityName} to {SolutionUniqueName} with its subcomponents.");
    }
    else
    {
        var attribute = entity.Attributes.FirstOrDefault(a =>
            string.Equals(a.LogicalName, attributeName, StringComparison.OrdinalIgnoreCase));
        if (attribute == null)
        {
            Console.Error.WriteLine($"{entityName} has no attribute '{attributeName}'.");
            return 1;
        }

        svc.Execute(new AddSolutionComponentRequest
        {
            ComponentId = attribute.MetadataId ?? Guid.Empty,
            ComponentType = 2,
            SolutionUniqueName = SolutionUniqueName,
            AddRequiredComponents = false,
        });
        Console.WriteLine($"Added attribute {entityName}.{attributeName} to {SolutionUniqueName}.");
    }

    // Verified by re-query, like every other write in this tool.
    var solutionId = FindId(svc, "solution", ("uniquename", SolutionUniqueName));
    var members = SolutionMembers(svc, solutionId);
    var behavior = members.TryGetValue((1, entity.MetadataId ?? Guid.Empty), out var b) ? BehaviorLabel(b) : "NOT A MEMBER";
    Console.WriteLine($"  {entityName} now reads: {behavior}");
    return 0;
}

/// <summary>What a registered step is set to do, so a check can report it rather than restate it.</summary>
sealed record StepFacts(bool Enabled, int Mode, int Stage, Guid RunAs)
{
    public string Describe() =>
        $"stage {Stage}, {(Mode == 1 ? "asynchronous" : "synchronous")}, {(Enabled ? "enabled" : "DISABLED")}"
        + (RunAs == Guid.Empty ? string.Empty : $", runs as {RunAs:D}");
}

/// <summary>One YAML entry: either a scalar or a list of strings, never both.</summary>
sealed class YamlValue
{
    public string Scalar { get; set; }

    public List<string> Items { get; } = new List<string>();
}

/// <summary>
/// The al_Notification outbox (PP-15, AD-035, OD-030). A row is written in the same
/// transaction as the state change that caused it and drained separately, which is what
/// makes a retry safe and a duplicate send impossible.
///
/// Created through the metadata API rather than hand-authored solution XML so Dataverse
/// generates the system columns, views and forms itself, and AD-013's "commit what
/// Dataverse emits" stays true — the definition enters src/ on the next export round trip.
///
/// The event option set carries the FIVE events AD-035 names and no more. PP-15 says nine;
/// the other four are not enumerated in any requirement (OD-030 gap (a)), and a nine-value
/// option set would mean inventing four business events. Adding values later is additive
/// and safe, so shipping five is not a decision that has to be unwound.
/// </summary>
static class NotificationTable
{
    public const string Logical = "al_notification";
    public const string Schema = "al_Notification";

    // Fresh option-value block: everything up to 120910791 is taken (al_auditevent's
    // al_command reaches it), so 1209108xx starts clear of every existing set.
    public const int EventAllocation = 120910800;
    public const int EventReviewSubmitted = 120910801;
    public const int EventRemediationAssigned = 120910802;
    public const int EventSignoffApproved = 120910803;
    public const int EventSignoffRejected = 120910804;

    public const int StatusPending = 120910810;
    public const int StatusSent = 120910811;
    public const int StatusFailed = 120910812;

    public static Label Text(string value) => new Label(value, 1033);

    public static readonly (int Value, string Name, string Description)[] Events =
    {
        (EventAllocation, "Allocation", "A case was allocated to a checker (BR-003, AD-040/AD-076)."),
        (EventReviewSubmitted, "Review submitted", "A checker submitted a review (FR-017)."),
        (EventRemediationAssigned, "Remediation assigned", "A remediation action was raised against an adviser (BR-006, FR-020)."),
        (EventSignoffApproved, "Sign-off approved", "A T&C Manager approved a remediation (BR-008, FR-023)."),
        (EventSignoffRejected, "Sign-off rejected", "A T&C Manager rejected a remediation and sent it back (BR-008)."),
    };

    public static readonly (int Value, string Name, string Description)[] Statuses =
    {
        (StatusPending, "Pending", "Written and waiting to be drained. The safe resting state."),
        (StatusSent, "Sent", "Handed to Dataverse server-side email successfully."),
        (StatusFailed, "Failed", "The send failed; al_failurereason says why and the row can be retried."),
    };
}