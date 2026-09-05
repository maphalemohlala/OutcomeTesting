using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System.Text;
using System.Text.Json;

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
const int CaseStatusQueued = 120910583;
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

if (args.Length >= 2 && args[0].Equals("provepp15", StringComparison.OrdinalIgnoreCase))
{
    return ProvePp15(args);
}

if (args.Length >= 2 && args[0].Equals("pp15evidence", StringComparison.OrdinalIgnoreCase))
{
    return Pp15Evidence(args[1]);
}

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: dotnet run -- <orgUrl> [<pluginDllPath>]   |   dotnet run -- verify <orgUrl>");
    return 1;
}

return Register(args);

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
