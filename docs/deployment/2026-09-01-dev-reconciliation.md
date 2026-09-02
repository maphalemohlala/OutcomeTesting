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

## 2. Create the three plug-in types and Custom APIs — DONE 2026-09-02

Re-querying `plugintype` after step 1 returned **17 types, unchanged**. This re-confirms
AD-052: `pac plugin push` does not create plug-in type rows for classes the environment
has not seen. The three new classes are in the assembly but have no type row, and without
a type row there is nothing for a Custom API to bind to.

`plugins/deploy/Register-CustomApiFromContract.ps1` (AD-063) needs a Dataverse Web API
bearer token, and this machine still has no Azure CLI, `MSAL.PS` or `Az.Accounts` — checked
again on 2026-09-02. **The blocker was the token, not the registration**, so the run used the
other contract-driven path the repository already owns:

```powershell
dotnet bin\Debug\net8.0\OutcomeTesting.Registration.dll registerall https://org0b075da8.crm11.dynamics.com
```

`plugins/OutcomeTesting.Registration` connects with `ServiceClient` interactive OAuth and
caches its token, so it needs no external token tool. It reads the same
`plugins/customapi/*.customapi.json` contracts and is idempotent — the run reported
`updated` for the fifteen commands that already existed and `created` for the three that
did not:

| Command | New plug-in type id |
|---|---|
| `al_SetPermissionRuleActive` | `7f3fbf12-a7a6-f111-aaac-e4fade069307` |
| `al_SetRoleAssignmentActive` | `dc0ac518-a7a6-f111-aaac-e4fade069307` |
| `al_UpdateRole` | `cff85832-a7a6-f111-aaac-e4fade069307` |

`registerall` also upserts the assembly, which step 1 had already pushed, so the same bits
were re-uploaded rather than a different build landing. **Result: DEV holds 18 Custom APIs.**

**A trap this run exposed.** `src/customapis/*/customapi.xml` carried `plugintypeexportkey`
values from the environment where AD-045 was first deployed — the very ids AD-052 removed
from DEV as orphans on 2026-08-29. Left alone, the solution import would have bound each
Custom API to a plug-in type id that no longer exists. The three keys were corrected to the
ids above before packing. **Anything that re-creates a plug-in type invalidates the
committed export key**, and neither the pack nor the import says so.

## 3. AD-013 export-and-replace round trip — DONE 2026-09-02

`registerall` creates components in the **default** solution, so the three commands existed
but were not solution members. A Custom-API-only package was packed from a staging folder
holding just `customapis/al_UpdateRole`, `al_SetRoleAssignmentActive`,
`al_SetPermissionRuleActive` plus `Other/` with `<RootComponents />` emptied and an empty
`Relationships.xml`. An unmanaged import merges, so this added the three Custom APIs to
`OutcomeTesting` and touched nothing else — the alternative, packing the whole of `src`,
would have pushed every pending local change to DEV in the same operation.

Then export, unpack, and copy back over `src` (`customapis`, `PluginAssemblies`, `Entities`,
`Roles`, `Other/Solution.xml`, `Other/Customizations.xml`, `Other/Relationships*`,
`SdkMessageProcessingSteps`). Never `CanvasApps` (AD-012).

Verified from the export, not inferred:

- `customapis/` holds **18** folders, the three new ones among them.
- The plug-in assembly manifest declares **20** plug-in types, matching what the assembly
  builds. This **closes the open half of OD-026** — the manifest no longer under-declares.
- `pac solution pack --folder src` succeeds, warning only about `CanvasApps`, which AD-012
  excludes on purpose.

`SdkMessageProcessingSteps/` is new to `src` in this round trip. It is what DEV holds — the
`al_response` steps behind AD-053 — and AD-013 says commit what Dataverse emits rather than
deciding which components deserve to be in source.

The staging step matters for the import, not the round trip: `src/PluginAssemblies/` still
holds a committed DLL, so anything packing the whole of `src` must strip it first (AD-062).

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
| 2. Register 3 types + 3 Custom APIs | Delivery (automated) | 2026-09-02 | Pass — 18 Custom APIs in DEV |
| 3. AD-013 round trip | Delivery (automated) | 2026-09-02 | Pass — 18 APIs and 20 plug-in types in the export; `src` packs clean |
