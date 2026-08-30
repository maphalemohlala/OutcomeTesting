# Outcome Creation — DEV deployment runbook

Everything in `docs/superpowers/plans/2026-08-30-outcome-creation.md` is implemented and
unit-tested in source, but **none of it has been run against a Dataverse environment.** The
plan's three environment steps were deferred because the executing session held no
authorization to mutate shared DEV. This runbook is what remains.

Until it is run: the four `al_outcome` accountability columns do not exist in DEV, the three
`al_reviewroute` rows do not exist so every route branch is inert, and
`al_SetFailAccountability` is not registered and cannot be called.

## Order is not optional

`al_GenerateExport` refuses a non-pass Outcome that records no accountability, and only
`al_SetFailAccountability` can record it. Push the assembly before importing the columns and
registering the command, and the export begins refusing batches **nobody can satisfy**.

1. Solution import, with the AD-013 export-and-replace round trip
2. Route seed import, run twice to prove idempotency
3. `pac plugin push`
4. Create the plug-in type
5. Create the Custom API
6. Only then run the verification scenarios in the plan

A human must already hold an authenticated `pac` profile pointed at the target environment.
No secrets, environment IDs, connection IDs or URLs are embedded below.

## 1. Solution import and the AD-013 round trip

`src/Entities/al_Outcome/Entity.xml` is currently a **hand-authored first draft**. AD-013
says hand-written entity XML is rejected for subtle reasons, so it must be replaced by what
Dataverse emits. One known divergence candidate: the draft names its local option sets
`al_outcome_aqadviseraccountable`, matching that file's existing rows, whereas newer
Dataverse-emitted files elsewhere in the repo include the attribute's `al_` prefix
(`al_reviewroute_al_requiresaqsreview`). The round trip settles it.

Confirm flags against the installed CLI before running — these follow CLI 2.11.2 conventions
and were not executed.

```bash
pac solution pack --zipfile OutcomeTesting.zip --folder src --packagetype Unmanaged
pac solution import --path OutcomeTesting.zip --publish-changes
pac solution export --name OutcomeTesting --path OutcomeTesting-exported.zip --managed false
pac solution unpack --zipfile OutcomeTesting-exported.zip --folder src --packagetype Unmanaged
```

Then diff `src/Entities/al_Outcome/Entity.xml` against what `unpack` produced and **commit
what Dataverse emits**, replacing the draft. Expect all four attributes present with
`<Type>bit</Type>`.

## 2. Route seed, imported twice

```bash
pac data import --data data/route-seed/data.xml --schema data/route-seed/data_schema.xml
pac org fetch --xmlFile route-fetch.xml
```

where `route-fetch.xml` selects `al_routecode` from `al_reviewroute`:

```xml
<fetch>
  <entity name="al_reviewroute">
    <attribute name="al_routecode" />
  </entity>
</fetch>
```

Expected: exactly 3 rows — `ROUTE-TAX`, `ROUTE-AQS`, `ROUTE-TAX-AQS`. Re-run both commands;
still 3 rows proves idempotency on the `al_routecode` alternate key (AD-014, NFR-REL-01).

## 3. Push the assembly

```bash
pac plugin push --pluginId 7b51d0d1-f5a1-f111-b8dd-e4fade069307 --pluginFile plugins/OutcomeTesting.Plugins/bin/Debug/net462/OutcomeTesting.Plugins.dll --type Assembly
```

## 4. Create the plug-in type explicitly

`pac plugin push` does not create plug-in type rows for classes the environment has not seen
(AD-052). This mirrors `Resolve-PluginType` in `plugins/deploy/Register-ResponseGuard.ps1`:

```powershell
$typeName = 'OutcomeTesting.Plugins.SetFailAccountabilityPlugin'
$pt = Invoke-RestMethod -Method Get -Uri "$api/plugintypes?`$filter=typename eq '$typeName'&`$select=plugintypeid" -Headers $headers
if ($pt.value.Count -eq 0) {
    $body = @{
        typename = $typeName
        name = $typeName
        friendlyname = 'SetFailAccountabilityPlugin'
        'pluginassemblyid@odata.bind' = "/pluginassemblies(7b51d0d1-f5a1-f111-b8dd-e4fade069307)"
    } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$api/plugintypes" -Headers ($headers + @{Prefer='return=representation'}) -Body $body
}
```

## 5. Create the Custom API from the contract

`al_SetFailAccountability` is a Custom API, not an SDK step, so the pattern to follow is
`plugins/deploy/Register-CompleteRemediation.ps1` steps 3-5 — not `Register-ResponseGuard.ps1`,
which only registers SDK steps and never touches `customapis`.

Cleanest path: extend `Register-CompleteRemediation.ps1` to take a `-ContractFile` /
`-CommandName`, or copy it to `Register-SetFailAccountability.ps1` pointed at
`plugins/customapi/al_SetFailAccountability.customapi.json`. That script already reads the
plug-in type name and every parameter and property straight out of the JSON contract, so no
hand-typed field lists are needed.

Ad hoc equivalent:

```
POST {api}/customapis
  uniquename: al_SetFailAccountability
  name: al_SetFailAccountability
  displayname: Set Fail Accountability
  description: <from the contract's customApi.description>
  bindingtype: 0
  isfunction: false
  isprivate: false
  allowedcustomprocessingsteptype: 0
  PluginTypeId@odata.bind: /plugintypes(<id from step 4>)

POST {api}/customapirequestparameters   (x6, one per request parameter in the contract)
  uniquename/name/displayname/description/type/isoptional from the contract
  CustomAPIId@odata.bind: /customapis(<id above>)

POST {api}/customapiresponseproperties  (x2: Status, AuditEventId)
  uniquename/name/displayname/description/type from the contract
  CustomAPIId@odata.bind: /customapis(<id above>)
```

Verify:

```
GET {api}/customapis?$filter=uniquename eq 'al_SetFailAccountability'
```

Exactly one row, bound to `OutcomeTesting.Plugins.SetFailAccountabilityPlugin`.

## 6. Then run the plan's verification scenarios

See "Verification in DEV" in the plan. Note scenario 2 needs a step the original text omits:
after the Tax submit leaves the case `Queued`, a manager must move the case to `Assigned`
before the AQS submit is accepted. That refusal is correct behaviour, not a bug — allocation
is manual (BR-003, AD-040).

## Related open decisions

- **OD-025** — `OutcomeTesting.Plugins.snk` is a committed private key awaiting rotation.
- **OD-026** — `src/PluginAssemblies/` declares 11 plug-in types against the 17 the assembly
  now builds. `SetFailAccountabilityPlugin` is the sixth missing one and, unlike the other
  five, is registered nowhere at all.
- **OD-027** — on a Tax-then-AQS route a Tax non-pass does not reach Awaiting Remediation;
  an AQS pass closes the case. Unresolved, pending the product owner.
