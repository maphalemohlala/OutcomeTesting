# Deployment — role assignment conflict rule, and the portal upload path

Date: 2026-09-07
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Source: commit `441a1ab`
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Covers AD-089 and AD-090, and the first portal upload through `Deploy-Portal.ps1`. Design in
`docs/superpowers/specs/2026-09-06-role-assignment-conflict-rule-design.md`; the plan and its
execution ledger are `docs/superpowers/plans/2026-09-06-role-assignment-conflict-rule.md`.

## What was deployed, in order

| # | Step | Command | Result |
|---|---|---|---|
| 1 | Plug-in assembly, Release | `dotnet build -c Release` | 0 warnings — the Debug build the tests produce lacks the AD-090 resolver fix |
| 2 | Plug-in types and Custom APIs | `registerall <orgUrl>` | 3 new Custom APIs; failed twice first, see below |
| 3 | Custom APIs into the solution | `pac solution add-solution-component` ×3 | All three added; `registerall` prints these rather than running them |
| 4 | Assembly and types into the solution | `addtosolution <orgUrl>` | Added |
| 5 | Custom APIs into the Code App | `npx pa app add dataverse-api --api-name` ×3 | All three; `--non-interactive` works |
| 6 | Code App | `pa app push` | Succeeded |
| 7 | Portal | `Deploy-Portal.ps1 -OrgUrl <orgUrl>` | Upload succeeded, 13 of 13 table permissions written and verified |

## Two undocumented Dataverse limits cost two failed runs

`registerall` was refused twice on field lengths that appear in neither the contract schema
nor any local validation:

- `customapi.description` — maximum **300**. Two of the three new contracts were over.
- `customapiresponseproperty.description` — maximum **100**. All three were over.

The safe bounds were derived from the contracts already deployed rather than guessed:
request parameter descriptions run to 285 characters in the existing set and are accepted,
so `Decision` at 107 was left alone instead of trimmed defensively. Fixed in `a19737c`.

**The console's idempotency is what made this recoverable.** The first run created a
plug-in type and then died on the description; the second updated that same type and carried
on. Without upsert semantics the partial write would have needed manual cleanup in a shared
environment before a retry was possible.

## Verification, after the fact

All by query against the environment, not inferred from command output.

| Check | Result |
|---|---|
| Three Custom APIs registered | `al_GetRoleHolders`, `al_GetMyRoles`, `al_AdoptRoleAssignment`, all `isfunction: No`, `bindingtype: Global`, each bound to its own plug-in type |
| Request parameters | Adopt 4 (`UserEmail`, `RoleCode`, `Decision`, `IdempotencyKey`), GetRoleHolders 1 (`RoleCode`), GetMyRoles **0** — correct, it takes none |
| Response properties | Adopt 3 (`MappingId`, `Adopted`, `AuditEventId`), GetRoleHolders 1 (`Holders`), GetMyRoles 1 (`RoleCodes`) |
| Code App resolution | `operations.test.ts` 22/22 — each API resolves as `dataSourcesInfo[tableName].apis[operationName]` |
| Table permissions | 13 in source, 13 in the environment, nothing else |
| Local suites at the deployed commit | Plug-ins 366/366, app 192/192, `tsc` clean, lint 0 errors |

### AD-090's central lookup, checked first and deliberately

`ExcludedFromResolution` feeds `powerpagecomponent.name` into a lookup on `mspp_name`. That
coupling was new and had only ever run against a test double, and its failure mode is
silent: an unresolvable name returns "not excluded" by design, with no error, no log and no
failing test. It was therefore the first thing checked after deploy, ahead of anything else.

**It holds.** `powerpagecomponent.name` matches `mspp_name` exactly for every role sampled,
including the flagged `Authenticated Users`.

### OD-033 has been corrected in DEV, which narrows what AD-090 can be observed doing

`Administrators` now carries `mspp_authenticatedusersrole = No` (modified 2026-09-05). Only
`Authenticated Users` carries the flag, and `Anonymous Users` the anonymous one.

The consequence is worth stating plainly: the flag-versus-name distinction AD-090 turns on
**cannot currently be observed in this environment**, because the only flagged role is also
excluded by name. The rule still stands — OD-033 is the evidence that this drift happens —
but nothing in DEV exercises the part of it that a name check would have missed.

## The portal upload, and what it closed

First upload through `Deploy-Portal.ps1`. Both gates ran clean beforehand:
`Check-ComponentIds.ps1` across 237 component identities, and `-VerifyOnly` reporting 13 of
13 permissions already deployed.

**This closes OD-034.** `pac pages upload` completed against this site for the first time,
because the script removes what makes it dangerous rather than working around it: the
`adx_entitypermission` sections come out of the manifest before `pac` runs, so there is
nothing for it to route down the Standard-model path and nothing for it to reconcile away.

**It did not close OD-035, though the decision log had it marked Open.** I checked the two
`case-details` pages after the deploy and found both carrying page template
`a1000000-...-002b`, and initially recorded that as a side effect of the upload. That was
wrong: `docs/2026-09-04-outstanding-work.md` §1 records OD-035 as closed on 2026-09-05, with
a corrected diagnosis — the pages were never carrying a null template, they pointed at the
colliding `…0022`. Today's query re-confirms the fix holds; it is not evidence this deploy
produced it. The stale `Open` marker in the decision log has been corrected.

### One error in the upload log, and what it is not

```
Updating table powerpagecomponent with record ID:f065878a-… FAILED
  due to Entity 'powerpagecomponent' With Id = f065878a-… Does Not Exist
```

That id is the `annotationid` in `outcome-testing.css.webfile.yml`. It is `pac` attempting
to write web file content down the Standard-model `annotation` path. **No web file on this
site uses `annotation` at all** — a query for annotations returns nothing, for any file — so
this is not specific to that stylesheet and is not evidence that it failed to deploy. The
component itself updated: `outcome-testing.css` (`a1000000-...-0050`) carries this
deployment's timestamp.

What cannot be confirmed by query is whether the file *bytes* refreshed, because on the
enhanced model the content lives in a file column rather than in the `content` field, which
holds metadata JSON. The practical check is the rendered page, allowing for the ~15 minute
template cache.

**Small follow-up, not urgent:** that stale `annotationid` will produce this FAILED line on
every future upload. It is a leftover of a Standard-model download and should be removed
from the web file yml so a real failure is not lost in a familiar one.

## Still owed — both closed later the same day

Recorded here as they stood; the evidence is in
`docs/deployment/2026-09-07-ad089-write-path-proof.md`.

- ~~The write-path proof in DEV~~ **Run, and it failed the first time.** The project owner
  named `svc.automate.aq@ascotlloyd.co.uk` as the subject. Adopting a portal-only grant
  aborted the transaction: `AssignUserRolePlugin.AssociateWebRole` caught the duplicate-key
  fault from `Associate` and carried on, which the platform refuses. Since a portal-only
  grant is by definition one where the association already exists, `al_AdoptRoleAssignment`
  Adopt could never have worked against the state it exists for — and no unit test could see
  it, because the fake modelled the call and not the constraint. Fixed, and the re-run passes
  5 of 5.
- ~~`src/customapis` lags the environment~~ **Closed by an AD-013 round trip.** 22 → 25
  Custom APIs, additions only, `pac solution pack --folder src` clean. OD-011 is unchanged by
  it: correct source makes a promotion possible, it does not make TEST or PROD exist.
