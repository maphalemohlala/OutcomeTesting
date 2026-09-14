# The permission model exercised for the first time — DEV and TEST, 2026-09-14

Targets: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`) and **`Env_AQ_Test`**
(`https://org37995f36.crm11.dynamics.com/`), as `svc.automate.aq@ascotlloyd.co.uk`. TEST is in
scope by project owner direction of 2026-09-14, superseding the 2026-09-12 "DEV only" standing
instruction.

Reported by the project owner: "I've assigned Zoe the administrator role in the test
environment. But she does not see the same pages as Simunye Radingwana and the service account
and they all have the same role assigned."

---

## What the environment said

| Question | Answer |
|---|---|
| Application roles (`al_userrolemapping` + web roles) | Zoe: `{Administrators}`. Service account: `{Administrators}`. Simunye: `{Administrators, Tax Reviewer, AQS Reviewer}` |
| Contact registry rows | All three **Active** — not the OD-010 deactivation path |
| **Dataverse security roles** | Service account: **System Administrator**. Simunye: **System Administrator**. Zoe: **`Basic User`, and nothing else** |
| Holders of `Outcome Testing App User` / `App Admin` | **Nobody**, in DEV or TEST |

So the premise was half right. Zoe differing from **Simunye** is correct — he holds two roles
she does not. Zoe and the **service account** did hold identical application roles. The
difference was an axis nobody was looking at: the Dataverse security role.

## Three findings, each behind the last

**1. The app's security roles granted almost nothing.** The Code App has 21 Dataverse data
sources. `GrantSecurity` granted privileges on six — `al_userrolemapping`, `al_pagepermission`,
`al_exportbatch`, `al_exportrecord`, `al_questionversion`, `al_question`. Every table the app
exists to work with — `al_outcomecase`, `al_reviewinstance`, `al_response`,
`al_remediationaction`, `al_signoff`, `al_outcome` — was granted by neither role.

**2. Nobody had ever held either role**, so it had never mattered. Every user in both
environments is a System Administrator, which grants everything — and which
`PermissionHelpers.EnsurePermission` short-circuits outright:

```csharp
if (IsSystemAdministrator(systemService, context)) { return; }
```

The two people Zoe was being compared against were not being gated at all.

**3. The app could not tell "no rules" from "could not read the rules".** Both arrive as an
empty array from `al_pagepermission`, and `rulesInForce` fell back to `DEFAULT_PERMISSIONS` for
both. For an unconfigured environment that is correct and documented. For Zoe it meant she was
gated by the coded defaults instead of TEST's configured rules, and the menu looked entirely
normal. That silence is why this took an investigation rather than a glance.

## Why System Administrator would have been the wrong fix

It would have worked, and it would have destroyed the thing being tested. The sysadmin
short-circuit above means such a user never exercises the permission model — which is exactly
the state that hid this for as long as it did.

## What changed

| Piece | Change |
|---|---|
| `GrantSecurity` (AD-135) | Both roles now grant **read** on the 19 tables the app reads and **create/write** on the 10 its commands write. `al_auditevent` is **create-only** — BR-012 wants an immutable record. Writing `contact`, `mspp_webrole`, `al_role`, `al_section`, `al_failreason` stays admin-only. Depth Global throughout, per OD-022. |
| `GrantTable` | A table name that will not resolve is now reported and skipped rather than thrown. Across ~20 calls an exception would abort the rest and leave a role **half-granted, looking like it worked**. |
| `resolveRules` (AD-136, new) | Separates a failed read from an empty table. A failed read returns the defaults **flagged unavailable**, and is distrusted even if it returned rows — a partial result is not a rulebook. |
| `PermissionContextValue` / `AppShell` | Carries `rulesUnavailable`; the shell renders a banner saying the menu is a default set rather than the access granted, that pages may be missing or may refuse, and that nothing is unsafe because the server re-checks every write. |
| `Webapi/error/innererror` | **Created at last**, in both environments. Blocked by this session's permission gate three times, and by the 2026-09-13 session's once — that note handed the command over and it was never run. |
| `sitesetting.yml` | `Webapi/error/innererror`'s id was the **invented** `a1000000-…-ae`; replaced with the real `8dea7fe4-27b0-…`. An invented id is a duplicate waiting for the next upload. |

**Write, not merely read.** 50 of the 51 registered steps run as the *calling* user, so a
command that updates a case does it with the caller's privileges. Read alone would have
rendered every page and failed every action — a second bug wearing the first one's clothes.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `grantsecurity` against DEV | Every table granted, **no SKIPPED lines** |
| 2 | `grantsecurity` against TEST | Same. Solution-add refused ("managed solution") — expected; the roles are already components |
| 3 | `pac admin assign-user` (Zoe → `Outcome Testing App Admin`, TEST) | Run by the project owner; the gate refused it here |
| 4 | Read back Zoe's roles | **`Basic User` + `Outcome Testing App Admin`** — added, not replaced |
| 5 | Read back `prvReadal_OutcomeCase` holders in TEST | Both app roles now listed beside the built-ins |
| 6 | Read back `al_auditevent` privileges | **Read, Create, Append, AppendTo — no Write, no Delete.** Create-only held |
| 7 | `setsitesetting … Webapi/error/innererror` ×2 | Created in DEV and TEST, both verified Active by read-back |
| 8 | `npx vitest run` | **489 passed** (4 new), 37 files |
| 9 | `npx tsc -b` | clean |
| 10 | `npm run build` + `pa app push` | Pushed to DEV |
| 11 | Solution 1.0.1.0 → **1.0.2.0**, round-trip, managed export, TEST import | See below |

Every verification above is a read-back from Dataverse, not the tool's own success message.

## A claim I made and had to withdraw

I told the project owner the Code App's data sources "will likely need rebinding" in TEST.
**That was wrong.** `app/power.config.json` carries `"connectionReferences": {}` — empty — and
binds its data sources to `default.cds`, the host environment's own Dataverse, with no
environment-variable indirection. The `environmentId` in that file is the `pa app push` target,
not a runtime binding. Both connection references that exist are present and Active in TEST with
`connectionid` null, exactly as in DEV where the app works. Nothing to rebind.

## Not done here

- **The `src/` orphan sweep is manual.** `pac solution unpack` reports "N unnecessary files" and
  declines to delete them, so each superseded app bundle has to be removed by hand or `src/`
  accumulates dead JS. Two were removed this round, one the round before.
- Nothing committed.
