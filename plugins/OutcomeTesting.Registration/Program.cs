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
// Add to solution: dotnet run -- addtosolution <orgUrl> [<solutionUniqueName>]
//   Adds the plug-in assembly (and its plug-in type) to the target solution for clean ALM
//   promotion. Idempotent. The Custom API is added separately via a solution-file import
//   (src/customapis/al_CompleteRemediation, pac solution import).

const string AssemblyName = "OutcomeTesting.Plugins";
const string TypeName = "OutcomeTesting.Plugins.CompleteRemediationPlugin";
const string ApiUniqueName = "al_CompleteRemediation";
const int StatusOpen = 120910600;
const int StatusCompleted = 120910602;
const int CommandCompleteRemediation = 120910756;

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

    return RegisterStep(args[1], args[2], args[3], args[4], args.Length > 6 ? args[6] : string.Empty, stageArg);
}

if (args.Length >= 3 && args[0].Equals("seedadmin", StringComparison.OrdinalIgnoreCase))
{
    return SeedAdmin(args[1], args[2]);
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
int RegisterStep(string orgUrl, string typeName, string messageName, string primaryEntity, string filteringAttributes, int stage)
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
        ["mode"] = new OptionSetValue(0),            // synchronous, so a refusal reaches the caller
        ["rank"] = 1,
        ["supporteddeployment"] = new OptionSetValue(0),
        ["invocationsource"] = new OptionSetValue(0),
        ["filteringattributes"] = filteringAttributes,
    };

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
    return 0;
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

/// <summary>One YAML entry: either a scalar or a list of strings, never both.</summary>
sealed class YamlValue
{
    public string Scalar { get; set; }

    public List<string> Items { get; } = new List<string>();
}
