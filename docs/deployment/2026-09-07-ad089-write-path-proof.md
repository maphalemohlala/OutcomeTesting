# The AD-089 write-path proof in DEV, and the defect it found

Date: 2026-09-07
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Run as, and subject of: `svc.automate.aq@ascotlloyd.co.uk` (per project owner direction)
Probe role: `AL Portal - Planner`

This closes both items left owed by `2026-09-07-role-conflict-rule-deployment.md`: the write-path
proof, and the missing solution source under `src/customapis`.

## Why a service account, and what the run is allowed to touch

The proof has to grant a real portal role to a real person, because that is the only way to
produce the disagreement AD-089 classifies. The subject was named by the project owner rather
than picked, and it is the automation account rather than a member of a check team, so the
blast radius is an account nobody signs into to do business work.

`proveadoption` refuses to start unless the subject holds the probe role in **neither** source,
so it can only ever put back what it found; cleanup runs in a `finally`, so a failure part way
through cannot leave the role granted. The audit events the commands write are immutable
(NFR-AUD-01) and remain — that is the record of the run, not litter.

## The first run failed, and it failed at the point the whole feature turns on

```
1. Grant in Power Pages only - a bare Associate, no mapping row.
   al_GetRoleHolders -> mappingId=null mappingActive=null associated=true
   classify -> portal-only "Granted in Power Pages, not adopted"
   STEP 1: PASS

2. al_AdoptRoleAssignment Decision=Adopt.
PROVE ADOPTION FAILED: OrganizationServiceFault: ISV code reduced the open transaction
count. Custom plug-ins should not catch exceptions from OrganizationService calls and
continue processing.
```

`AssignUserRolePlugin.AssociateWebRole` caught the duplicate-key fault from `Associate` and
carried on, on the stated grounds that "assigning a role someone already holds is the idempotent
outcome this command promises (NFR-REL-01), not a failure". The intent was right and the
mechanism was not: a plug-in cannot deliver idempotency by swallowing a fault raised inside its
own transaction, because the platform answers that by aborting the transaction.

**This was not an edge case. It was the headline flow.** Adopting a portal-only grant associates
a contact that is *already associated* — every single time, by definition of what "portal-only"
means. `al_AdoptRoleAssignment` Decision=Adopt could never have succeeded against the state it
exists to reconcile.

### Why 366 passing unit tests did not see it

`FakeOrganizationService.Associate` appended to a list and returned. It modelled the call, not
the constraint, so every adopt test exercised a path the platform would have refused. The fake
now tracks which pairs are actually associated and faults on a duplicate, and
`AdoptRoleAssignmentPluginTests` seeds the association wherever the scenario implies one — a
"portal-only grant" fixture without an association was testing a state AD-089 never has to
reconcile.

### The fix

`WebRoleRegistry.IsAssociated` reads the intersect directly with a flat FetchXML on its two id
columns (`mspp_webrole` will not answer a plain `QueryExpression`, and `mspp_webroleid` and
`powerpagecomponentid` are the same id). `AssociateWebRole` and `DisassociateWebRole` now check
and skip rather than attempt and catch. A concurrent caller could still associate between the
read and the write; that fault is left to propagate deliberately, because once it is raised the
transaction is already unusable and failing the command is the only honest outcome.

### One asymmetry, observed rather than assumed

`Disassociate` of a pair that is **not** associated succeeds — it does not fault. That was
observed in the passing run's cleanup, which disassociates unconditionally after step 5 has
already removed the association, and reported success. So the tolerant catch on that side was
never load-bearing, and the fake is deliberately *not* symmetrical: faulting there would make it
stricter than the platform, which is a different way of lying. The guard is kept on both sides
anyway, so neither reads as depending on undocumented tolerance.

## The passing run

Assembly rebuilt Release (0 warnings), `registerall` re-run — 25 commands, all members of
`OutcomeTesting` — then:

| Step | What was done | `al_GetRoleHolders` | Classification | |
|---|---|---|---|---|
| 1 | Bare `Associate`, no mapping row | `mappingId=null, mappingActive=null, associated=true` | `portal-only` — "Granted in Power Pages, not adopted" | PASS |
| 2 | `al_AdoptRoleAssignment` Adopt | `mappingId` set, `mappingActive=true, associated=true` | `consistent` — "Assigned in app" | PASS |
| 3 | `al_SetRoleAssignmentActive` Active=false | `mappingActive=false, associated=false` | `consistent` — "Withdrawn" | PASS |
| 4 | Re-associate in Power Pages | `mappingActive=false, associated=true` | `withdrawn-still-granted` — **"Withdrawn in app, still granted"** | PASS |
| 5 | `al_AdoptRoleAssignment` Revoke | `mappingActive=false, associated=false` | `consistent` — "Withdrawn" | PASS |

Cleanup removed the association and deleted the probe mapping; the final read reports
`mappingId=null associated=false` — the environment as found.

The assertion is on the **label**, not on the raw fields. `proveadoption` mirrors
`classifyHolder` from `app/src/features/admin/roleDetail.ts`, so what is proved is what an
administrator would see on the role detail screen; a rule that reads the fields correctly and
labels them wrongly would still be wrong, and asserting on the fields alone would not catch it.

## `src/` now reproduces the environment again (AD-013 round trip)

`pac solution export` → `unpack` → diff → copy back, read-only against DEV.

| | Before | After |
|---|---|---|
| `src/customapis/` | 22 | **25** — `al_AdoptRoleAssignment`, `al_GetMyRoles`, `al_GetRoleHolders` |
| `src/PluginAssemblies/` | assembly of 2026-09-06 | today's, carrying the three new plug-in types |
| `src/Other/Solution.xml` | — | one added `MissingDependency` |
| `Entities`, `Roles`, `SdkMessageProcessingSteps` | — | byte-identical, nothing to copy |

Additions only, no removals. `pac solution pack --folder src` succeeds with the single expected
`CanvasApps` warning, which AD-012 excludes on purpose. `CanvasApps`, `powerpagecomponents/` and
`Assets/` were left out for the reasons AD-013 already records.

**The new `MissingDependency` is worth reading before a promotion.** `Solution.xml` now declares
that the site component `al_ascotlloydoutcometesting_72932` requires `mspp_webrole` from
`PowerPages_Apps`, supplied by the Power Pages Core package. It is a consequence of the site
being added to the solution on 2026-09-05, not of this work, but it is a real precondition on
any target environment and it was not there before.

## What this does not close

**OD-011 is unchanged.** `src/` being correct makes a promotion possible; TEST and PROD still do
not exist, and per project owner direction 2026-09-06 they are set up once everything is tested
and approved in DEV.

**AD-062 is unchanged.** `src/PluginAssemblies/` holds a committed DLL and this round trip put
today's one there. Anything packing the whole of `src/` for an **import** must strip
`PluginAssemblies/` first, or the import replaces the live assembly with whatever was last
committed — at the same version and public key token, so nothing reports it.

## Sign-off

| Step | Run by | Outcome |
|---|---|---|
| `proveadoption`, first run | Delivery (automated) | **Fail** — found the AD-089 adopt defect |
| Fix + regression tests | Delivery (automated) | Plug-in suite 368/368 (was 366; +2) |
| Release build, `registerall` | Delivery (automated) | Pass — 25 commands, all solution members |
| `proveadoption`, second run | Delivery (automated) | **Pass, 5 of 5**, environment left as found |
| AD-013 round trip and `pack` | Delivery (automated) | Pass — additions only |
