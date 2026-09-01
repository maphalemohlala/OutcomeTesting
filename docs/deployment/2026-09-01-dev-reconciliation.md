# DEV reconciliation after the origin/main merge — 2026-09-01

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), authenticated `pac`
profile `svc.automate.aq@ascotlloyd.co.uk`.

## Why this run exists

`992cdf8` merged `43fccf7` (editable RBAC) into the outcome-creation branch and onto
`main`. The merge produced a source tree holding **20 plug-in types and 18 Custom APIs**.
DEV held 17 and 15.

Missing from DEV before this run:

| Component | Plug-in type | Custom API |
|---|---|---|
| Update role | `UpdateRolePlugin` | `al_UpdateRole` |
| Set role assignment active | `SetRoleAssignmentActivePlugin` | `al_SetRoleAssignmentActive` |
| Set permission rule active | `SetPermissionRuleActivePlugin` | `al_SetPermissionRuleActive` |

The Code App on `main` calls all three, through `app/src/services/commands/roles.ts`,
`permissions.ts` and `operations.ts` plus their generated services. **Until they exist in
DEV, the editable-RBAC screens fail against APIs that are not there.**

Note for the record: `43fccf7`'s commit message states "solution imported and published to
DEV… src is round-tripped from a fresh export so it matches DEV." That is not true of
`Env_AQ_Dev` — its three new components were absent when this run started. Either that work
targeted a different environment or the claim is inaccurate. It needs settling, because the
runbooks treat "round-tripped from DEV" as the trust anchor for solution source.

## 1. Push the merged assembly — DONE

```bash
pac plugin push --pluginId 7b51d0d1-f5a1-f111-b8dd-e4fade069307 \
  --pluginFile plugins/OutcomeTesting.Plugins/bin/Release/net462/OutcomeTesting.Plugins.dll \
  --type Assembly
```

**Result: RUN 2026-09-01 — succeeded.** "Plug-in assembly was updated successfully."

The pushed build is the merged 20-type assembly, a superset of what DEV was running, so
no live type was withdrawn. It was built from the merged source and verified before the
push: build clean, 142 plug-in tests pass.

## 2. Create the three plug-in types and Custom APIs — NOT RUN, blocked

Re-querying `plugintype` after step 1 returned **17 types, unchanged**. This re-confirms
AD-052: `pac plugin push` does not create plug-in type rows for classes the environment
has not seen. The three new classes are in the assembly but have no type row, and without
a type row there is nothing for a Custom API to bind to.

The registration path is `plugins/deploy/Register-CustomApiFromContract.ps1`, which needs a
Dataverse Web API bearer token. **This machine has neither the Azure CLI nor `MSAL.PS` /
`Az.Accounts` installed**, so no token can be obtained from an automated session. The three
contracts are already present in `plugins/customapi/`; nothing is missing but the token.

To finish, run:

```powershell
$org = 'https://org0b075da8.crm11.dynamics.com'
$t = az account get-access-token --resource $org --query accessToken -o tsv
foreach ($c in 'al_UpdateRole','al_SetRoleAssignmentActive','al_SetPermissionRuleActive') {
    .\plugins\deploy\Register-CustomApiFromContract.ps1 -OrgUrl $org -AccessToken $t `
        -ContractFile "$c.customapi.json"
}
```

The script is idempotent, so a partial run is safe to repeat.

## 3. AD-013 export-and-replace round trip — NOT RUN, correctly deferred

This must run **after** step 2, not before. An export reflects what DEV holds; running it
now would delete `src/customapis/al_UpdateRole/`, `al_SetRoleAssignmentActive/` and
`al_SetPermissionRuleActive/` from source, silently reverting the merge.

This round trip is the open half of OD-026 and the authoritative fix for the assembly
manifest, which `992cdf8` had to resolve by hand — see AD-062.

## Tooling corrections found during this run

Both cost time and both contradict what the existing runbooks imply.

1. **`pac org fetch --xml` fails with `System.XmlException`** when the query is passed
   inline from PowerShell 5.1 — embedded double quotes are mangled before the CLI sees
   them. Use **`--xmlFile <path>`** instead. The earlier runbooks say "queried with
   `pac org fetch`" without recording which form.
2. **`pac data import` does not exist in the installed CLI.** `pac 2.11.2+g47bc199`
   (.NET 10.0.11) has no `data` verb at all, though
   `docs/deployment/2026-08-30-outcome-creation-deployment.md` records it as verified
   against 2.11.2. The roles seed (OD-028) therefore could not be re-imported here.

## Sign-off

| Step | Run by | Date | Outcome |
|---|---|---|---|
| 1. Push merged assembly | Delivery (automated) | 2026-09-01 | Pass |
| 2. Register 3 types + 3 Custom APIs | _(unassigned)_ | | Blocked — needs a bearer token |
| 3. AD-013 round trip | _(unassigned)_ | | Deferred until step 2 completes |
