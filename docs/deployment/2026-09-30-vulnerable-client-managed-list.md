# "Vulnerable client?" becomes a managed list - DEV

Date: 2026-09-30
Spec: `docs/superpowers/specs/2026-09-30-vulnerable-client-managed-list-design.md`
Plan: `docs/superpowers/plans/2026-09-30-vulnerable-client-managed-list.md`
Decision: AD-226

## What changed

**Vulnerable client?** is now the sixth list on the Code App's **Dropdown options** page. An
administrator can add, rename, reorder, retire and reinstate its options there with no
deployment, as with Sample source and the others (AD-187, AD-188).

| Part | Change |
|---|---|
| Dataverse | `al_listoption.al_list` value **120910845 "Vulnerable client"**. New lookup `al_outcomecase.al_vulnerableclientid` (relationship `al_listoption_al_outcomecase_vulnerableclient`). The choice column `al_vulnerableclient` stays as the fallback |
| Plug-ins | `ListOptionRules` knows the list. `UpdateCaseDetailsPlugin` accepts the lookup and refuses an option from another list. `CaseHeaderRequestPlugin` lets the checker send it. The PDF reads the lookup, then the legacy value |
| Code App | The list is on Dropdown options. The case edit panel offers it as a managed dropdown. The Checker Checklist header reads it, then the legacy value |
| Portal | The `OT Review Detail` header select is drawn from the list's rows. Its read-only row and `OT Case Detail` read the lookup, then the legacy value |
| Registration tool | One `Def` in `ListOptionTable.Lists`, so `createlistoptiontable` and `seedlistoptions` handle it |

Tests: plug-ins 1754/1754, app 1214/1214, `tsc -b` clean.

## What ran in DEV (`org0b075da8`)

| Step | Result |
|---|---|
| `addoptionvalue ... al_listoption al_list 120910845 "Vulnerable client"` | Inserted and published (6 values) |
| `createlistoptiontable` | `al_outcomecase.al_vulnerableclientid: created`; everything else already present |
| `seedlistoptions --confirm` | Four rows created: Yes 10, No 20, Potentially vulnerable 30, N/A 40, with legacy values 120910550 to 120910553. **1 of 1** case holding a value backfilled (900000006, "No") |
| `metadatamembership` | Every `al_` table is carried, so the new lookup travels with `al_outcomecase` |
| Model regenerated (`al_outcomecase`, `al_listoption`) | `dataSourcesInfo.ts` restored afterwards: the generator dropped `StaffCode` again |
| `pushassembly` | 379392 bytes, sha256 `8b70b569...32aba43`, which matches the DLL read back from `pluginassemblies` |
| `pushwebtemplate` OT Review Detail, OT Case Detail | Before the push, DEV's content was identical to `main` (272708 and 58460 chars), so nothing newer on DEV was overwritten |
| `npm run build`, `npx pa app push` | Bundle `index-fRdzGovV.js`, pushed |

## Proved in DEV

Server-side, through `al_UpdateCaseDetails` on case 900000006:

| Call | Result |
|---|---|
| Set to "Potentially vulnerable" (its own list) | Saved. The lookup changed; the legacy column was untouched |
| Set to "Random" (a Sample source row) | **Refused**: "Vulnerable client is not an option on that list." |
| Set back to "No" | Saved; the case is as it was |

In the browser (portal session, Service Account):
- `managed-lists.e2e.ts`, 3/3 passed, including a new test. On review `d27c0fa6...` the Vulnerable client select offers Yes, No, Potentially vulnerable, N/A. Every option value is a row id. No select is bound to the old choice column.
- With the case set to "Potentially vulnerable" and the legacy column still at "No", the review header selected **Potentially vulnerable**. The case record also showed **Potentially vulnerable**, so both pages read the lookup and not the fallback.

**Portal cache.** The case record at first kept showing "No" after the change. Power Pages had cached the page's fetch result from a visit made before the change. The same page requested with the case id in upper case (a different query text, so not cached) showed the new value at once. The cache clears on its own.

**Not driven:**
- **The Code App in a browser.** Its Entra sign-in has expired. The unit tests cover it (`listOptions`, `checklistForm`, `useCaseDetail`).
- **A PDF.** DEV has no completed check on a case with a value; `CompletedCheckDocumentTests` covers it.
- **Adding a new option on the page.** The spec leaves adding options to the owner. The add, retire and reinstate paths are the same code the other five lists use.

## Found on the way (not changed)

`seedlistoptions` also backfilled **6 DEV cases' Pre or post check** lookup. The case import (`ImportRules.cs`, and `caseUpload.ts` in the app) writes only the legacy `al_preorpostcheck`, never `al_preorpostcheckid`, so every newly imported case shows "—" in that header select until a backfill runs. The read-only views are unaffected, because they fall back to the legacy value. The import does not set Vulnerable client at all, so this change does not repeat the gap.

## TEST, when the owner says

1. Export the solution managed as **1.0.17.0** and import it through `ImportSolutionAsync`. Read back the version, the step states and the assembly sha256.
2. Add the list value, then seed and backfill. These writes are expected to be refused to the agent, so the owner runs them:

   ```powershell
   $env:DOTNET_ROLL_FORWARD='Major'; & plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe seedlistoptions https://org37995f36.crm11.dynamics.com --confirm https://org37995f36.crm11.dynamics.com
   ```

   The managed import carries the `al_list` value and the lookup, so `addoptionvalue` and `createlistoptiontable` are not needed on TEST.
3. Compare OT Review Detail, OT Case Detail and `outcome-testing.css` on TEST with the repo by id. A direct push has masked managed imports of these before.
