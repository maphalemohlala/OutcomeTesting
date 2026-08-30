# Outcome Creation — DEV deployment runbook

**Executed against DEV (Env_AQ_Dev) on 2026-08-30.** All six steps are done, and **all five
verification scenarios have been run end to end and pass.** What follows is kept as the runbook for the
next environment (TEST/PROD), with the two defects found during the DEV run corrected in place.

State after the run: the four `al_outcome` accountability columns exist as `bit`, the three
`al_reviewroute` rows exist and re-import idempotently, the assembly carries 17 plug-in types,
and `al_SetFailAccountability` is registered and callable.

Running it found four defects — two in this document, and two in the product that only
appear against a real environment. See "Corrections from the DEV run" at the end.

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

> **Pack the schema without `src/PluginAssemblies/`.** `pac solution pack` over the whole of
> `src/` puts the committed `OutcomeTestingPlugins.dll` into the zip, and `solution.xml`
> carries it as `RootComponent type="91" behavior="0"`. Importing that replaces the live
> plug-in assembly with whatever was last committed, at the same version and public key
> token — the AD-061 failure mode, reached through the solution package instead of a data
> package. During the DEV run the committed DLL was 47,104 bytes with 11 plug-in types while
> DEV was running 16, so the import would have silently removed five live types. Copy `src/`
> to a temp folder, delete `PluginAssemblies/`, and pack that. The assembly is delivered by
> `pac plugin push` at step 3, per AD-061.

`src/Entities/al_Outcome/Entity.xml` is currently a **hand-authored first draft**. AD-013
says hand-written entity XML is rejected for subtle reasons, so it must be replaced by what
Dataverse emits. One known divergence candidate was the local option set naming: the draft used
`al_outcome_aqadviseraccountable`, whereas newer Dataverse-emitted files elsewhere in the repo
include the attribute's `al_` prefix (`al_reviewroute_al_requiresaqsreview`). **Settled by the
DEV round trip: Dataverse emitted the unprefixed form, matching the draft.** The real diff was
attribute ordering — Dataverse emits alphabetically, the draft grouped the four new attributes
together. All four came back as `<Type>bit</Type>`.

Flags below are verified against CLI 2.11.2 as installed.

```bash
cp -r src /tmp/src-schema && rm -rf /tmp/src-schema/PluginAssemblies   # see the warning above
pac solution pack --zipfile OutcomeTesting.zip --folder /tmp/src-schema --packagetype Unmanaged
pac solution import --path OutcomeTesting.zip --publish-changes
pac solution export --name OutcomeTesting --path OutcomeTesting-exported.zip --managed false --overwrite
pac solution unpack --zipfile OutcomeTesting-exported.zip --folder /tmp/src-exported --packagetype Unmanaged
```

Unpack to a scratch folder and adopt files deliberately, rather than unpacking over `src/`.
In the DEV run seven entity files differed from what Dataverse emits; only `al_Outcome` was in
scope for this work, and rewriting the other six would have mixed unrelated drift into the
commit. The other six are listed in "Corrections from the DEV run".

Then diff `src/Entities/al_Outcome/Entity.xml` against what `unpack` produced and **commit
what Dataverse emits**, replacing the draft. Expect all four attributes present with
`<Type>bit</Type>`.

## 2. Route seed, imported twice

`pac data import` takes a zip **or a directory**, and has no `--schema` flag (verified against
CLI 2.11.2). The schema is read from `data_schema.xml` inside the directory, as AD-027 already
records for `data/v8-seed`.

```bash
pac data import --data data/route-seed
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

**Steps 4 and 5 are both done by `plugins/deploy/Register-CustomApiFromContract.ps1`**, added
during the DEV run. It is the generic form of `Register-CompleteRemediation.ps1`'s Web API
half: it resolves-or-creates the plug-in type (step 4) and creates the Custom API with every
parameter and property read from the JSON contract (step 5). It deliberately does not push the
assembly — that is step 3, and folding it in would let a caller register a command against an
assembly older than its contract.

```powershell
$org = 'https://<your-org>.crm<n>.dynamics.com'
$t = az account get-access-token --resource $org --query accessToken -o tsv
.\Register-CustomApiFromContract.ps1 -OrgUrl $org -AccessToken $t `
    -ContractFile al_SetFailAccountability.customapi.json
```

It is idempotent — proven in DEV by running it twice, the second run reporting `exists:` for
all ten components.

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
- **OD-026** — *solution-source half closed by the DEV run.* The AD-013 round trip brought
  back all 17 plug-in types, so `src/PluginAssemblies/` now matches the assembly, and
  `src/customapis/al_SetFailAccountability/` exists. What remains open is whether
  `src/PluginAssemblies/` should hold a DLL at all: AD-061 says assemblies deploy by
  `pac plugin push`, never in a package, and while the folder exists every full-`src/` pack is
  one import away from reverting the live assembly. Removing it is the durable fix.
- **OD-027** — on a Tax-then-AQS route a Tax non-pass does not reach Awaiting Remediation;
  an AQS pass closes the case. Unresolved, pending the product owner.

## Corrections from the DEV run

Four defects. The first two were caught before they did damage, because the run stopped to
check. The last two were caught only by running the scenarios — no amount of reading would
have found them, and the unit suite was green throughout.

1. **`pac data import --schema` does not exist** (step 2). CLI 2.11.2 takes `--data <zip|dir>`
   and reads `data_schema.xml` from inside the directory. The original command would have
   failed on first contact. AD-027 already recorded the correct directory form.

2. **Packing the whole of `src/` ships the plug-in assembly** (step 1). The committed DLL was
   47,104 bytes with 11 plug-in types; DEV was running 16 at the same version and public key
   token. Importing it would have removed `SubmitReviewPlugin`, `ResponseGuardPlugin`,
   `ResponseProgressPlugin`, `SetUserActivePlugin` and `UpdateUserPlugin` from the live
   assembly — every one of those commands failing at runtime with no schema change to explain
   it. This is AD-061's failure mode arriving through the solution package rather than a data
   package, and AD-061 did not cover it.

3. **The route seed imported every Boolean as False** (step 2). `data/route-seed/data.xml`
   wrote `value="1"` / `value="0"`; the Configuration Migration importer accepts those and
   silently stores **False**. The import reported "3 of 3" and this runbook's row-count check
   passed, so the seed looked correct while every route branch stayed inert — the exact
   condition the seed exists to remove. A Tax submit on `ROUTE-TAX-AQS` closed the case
   instead of queuing it for AQS. Fixed to `True`/`False`, matching `data/v8-seed`, which had
   the convention right all along. Recorded as **AD-064**. *Check the flag values, not just
   the row count.*

4. **Every Tax review was unsubmittable** (step 6). `SubmitReviewPlugin.HasAnswer` read
   `al_response.al_answerchoices` — a multi-select choice column — with
   `GetAttributeValue<string>`, which throws `InvalidCastException` as soon as the column
   holds a value. `Q-TAX-01` is mandatory and multi-select in the V8 checklist, so every Tax
   submit failed with a type error before any business rule ran. `ResponseGuardPlugin` had
   always read it correctly, so the two plug-ins disagreed about the schema. Recorded as
   **AD-065**, fixed, and covered by `SubmitReviewAnswerTests`.

### Entity drift not adopted

`pac solution unpack` of the DEV export differs from `src/` in seven entity files. Only
`al_Outcome` was adopted, being the one this work changed. The rest are pre-existing drift and
want their own change:

| Entity | Differing lines |
|---|---|
| `al_OutcomeCase` | 224 |
| `al_Role` | 108 |
| `al_RemediationAction` | 80 |
| `al_AuditEvent` | 15 |
| `al_CaseAssignment` | 1 |
| `al_Signoff` | 1 |

### What was verified in DEV

- Route seed imported twice: 3 rows both times, same GUIDs (AD-014, NFR-REL-01).
- `pac plugin push` left the type count at 16, confirming AD-052 — the new plug-in type had to
  be created explicitly.
- `al_SetFailAccountability` sets exactly the four columns passed, replays to the *same* audit
  event id on a repeated idempotency key, and refuses a Pass outcome with
  `PRECONDITION: This case passed, so there is no fail to attribute.`
- Plan scenario 5 end to end: `al_GenerateExport` refused with
  `PRECONDITION: Case IO-DEV-EXPGATE-001 has a non-pass outcome with no fail accountability
  recorded.`, then generated `RowCount=1` after accountability was recorded, with the two
  flagged pairs populated and the two unflagged pairs blank.
- Scenario 1 — Tax-only, Tax Pass: case `Closed`, **no** Outcome created (a Tax review
  creates none; the Tax scale is AD-055's, not BR-005's).
- Scenario 2 — Tax-then-AQS: the AQS submit before Tax was refused with *"Tax must be
  completed before the AQS review (BR-004)"*; the Tax Pass then left the case `Queued`; the
  AQS submit while still `Queued` was refused with *"A case cannot move from Queued to
  Submitted"*; after a manager moved it to `Assigned` the AQS submit succeeded and closed the
  case with one Outcome. **That middle refusal is the step the plan's original text omits** —
  it is correct behaviour, not a bug (BR-003, AD-040).
- Scenario 3 — AQS Pass: one Outcome, `al_initialoutcome` Pass, case `Closed`.
- Scenario 4 — AQS Potential harm: one Outcome, case `Awaiting Remediation`.

Every record these scenarios created was removed afterwards; the `data/case-verify` seed rows
were left in place.

### Reproducing the submit path

Scenarios 1-4 need a case on the right route, a review instance owned by the caller, and an
answered response for every mandatory question version of that discipline — 4 for Tax, 36 for
AQS in the V8 checklist. The mandatory set is question versions with `al_ismandatory` true
whose section belongs to the checklist version *and* carries the matching `al_ownerrole`.
DEV already holds the V8 graph (1 checklist version, 11 sections, 42 questions, 44 versions),
so only the case, the reviews and the responses need creating.

`al_GenerateExport` sweeps *every* closed case in the environment, not just the batch's. A
single closed non-pass case with no accountability blocks all export generation org-wide. DEV
held none at the time of the run, but this is worth checking before the assembly lands in
TEST or PROD.
