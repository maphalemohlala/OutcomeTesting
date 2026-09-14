# Delivery status — 2026-09-14

Written at commit `001f2d9` on `feat/checklist-administration`. Supersedes
`docs/2026-09-13-delivery-status.md` as the current status. The register of what is left
remains `docs/2026-09-04-outstanding-work.md`, read with the "Still open" and "Not done here"
sections of the deployment notes since.

Every environment claim below was verified by query after the fact — against **`Env_AQ_Dev`
and `Env_AQ_Test`**, because today is the first day TEST was in scope. Where something did
**not** land, it says so.

**The day in one line: case 254398988 closed and stayed closed, the IO import turned out to
have had the paraplanner and the checker the wrong way round since 2026-09-12, and giving one
real person a real application role revealed that the app's security roles had never granted
anything the app is actually for — because until today nobody had ever held one.**

Five decisions: AD-133 (the status re-read), AD-134 (the import mapping), AD-135 (the business
tables), AD-136 (the rules banner), and AD-137 (the parked case prompted).

---

## 1. What went in today

| Commit | What | Deployment note |
|---|---|---|
| `46a8569` | AD-133 to AD-136: the status re-read, the import mapping, the business-table grants, the permissions banner | `2026-09-14-case-status-reread.md`, `-paraplanner-from-assigned-to.md`, `-permission-model-made-real.md` |
| `af32226` | `src/` round-tripped from DEV 1.0.2.0; the Code App brought into source control | same |
| `001f2d9` | The TEST verification, and the `pac solution import` trap | `-permission-model-made-real.md` |

## 2. The finding worth reading first

**Nobody had ever held either application security role.** `Outcome Testing App User` and
`App Admin` existed in both environments, were in the solution, and had **zero holders**. Every
user in DEV and TEST is a System Administrator, which grants every privilege and, in
`PermissionHelpers.EnsurePermission`, short-circuits the application gate outright:

```csharp
if (IsSystemAdministrator(systemService, context)) { return; }
```

So the permission model had never run. The roles granted six of the Code App's twenty-one data
sources — the admin surface and none of the business surface. `al_outcomecase`,
`al_reviewinstance`, `al_response`, `al_remediationaction`, `al_signoff`, `al_outcome`: none.

It surfaced the first time a real person was given an application role instead of System
Administrator. Zoe Ramwell held `Basic User`, could not read `al_pagepermission`, and so was
silently gated by `DEFAULT_PERMISSIONS` instead of TEST's configured rules — a plausible,
wrong menu, with the app reporting nothing unusual. AD-135 grants the tables; AD-136 makes the
app say when its rules are a stand-in rather than the truth.

Granting her System Administrator would have "fixed" it and destroyed the test: the
short-circuit above means such a user never exercises the model at all.

## 3. Case 254398988, continued from yesterday

Yesterday's note predicted the case would close itself once the remediation was signed off.
It did not. The six sign-offs at 17:35Z carried no `al_finaloutcome` — the supervisor left the
grade at "Leave for a separate regrade" — so `RecordFinalOutcome` returned at its first gate
and only `MoveCase` ran. The grade that would have closed it had already been spent at 17:24Z,
eleven minutes before the case reached the recheck, where `CloseAfterRecheck` correctly
declined it.

One fresh regrade closed it at 08:02Z. The case page then read **Closed** while the remediation
page still read **Awaiting Recheck** — two Liquid queries, two AD-094 cache entries, one
refreshed and one not. AD-133 stops the page trusting its own render for that one value.

## 4. The import had the two names the wrong way round

`AssignedTo` is the **paraplanner**, not the checker (AD-134). The old mapping was read off
`data/io-task-extract-sample.csv`, which is synthetic — its names are literally `Paraplanner 1`
and `Checker 4` — so which column held whom was a guess, and it reached two implementations and
a spec. The extract's own structure agrees with the correction: the checklist stamp equals
`AssignedTo`, and paraplanners are who select those items.

`al_checkername` is no longer imported at all. The checker is set manually.

## 5. Where the environments stand

| | DEV | TEST |
|---|---|---|
| Solution | 1.0.3.0 unmanaged | **1.0.3.0 managed** (1.0.2.0 landed 10:56; 1.0.3.0 carries AD-137) |
| Plugin steps | — | **51 Enabled, 0 Disabled** (baseline taken before the import) |
| Code App | pushed 10:39Z | appversion `2026-09-14T10:48:41Z` |
| `Webapi/error/innererror` | created, Active | created, Active |
| `Webapi/al_outcomecase/*` | created, Active | arrived by solution |

**PROD is untouched and out of scope.** The 2026-09-12 "DEV only" direction was superseded for
TEST by project owner direction today; nothing extends that to PROD.

## 6. Zoe's access, verified

`Basic User` + `Outcome Testing App Admin`. Effective privileges read back through her roles:
read and write on `al_outcomecase`, read on `al_pagepermission` and `al_reviewinstance`, create
on `al_response` and `al_auditevent` — and **no write on `al_auditevent`**, so AD-135's
create-only rule holds for a real user and not only in the role definition (BR-012). The app
was already shared with her.

**Not verified: that she can actually use it.** Nobody has signed in as her. Her first session
is a test of the permission model, not merely of her access — she is the first person ever to
be gated by it, so a refusal she hits is as likely to be a finding as a fault.

## 7. Two tooling traps recorded

- **`pac solution import` hangs and killing it cancels nothing.** No output for over twenty
  minutes and no `importjob` row reads exactly like an import that never reached the server. It
  had. The retry was refused — "a previous [Import] running at this moment" — and the original
  finished on its own at 87.93% → 100%. Check `importjob`, not the CLI.
- **`pac solution unpack` never prunes `src/`.** It reports "N unnecessary files" and declines
  to delete them, so every Code App rebuild strands its previous bundle. Three were stranded
  before this was noticed. `scripts/round-trip-src.ps1` now does the whole round-trip and
  prunes, listing what it removes and refusing to mirror an empty unpack.

## 8. The parked case, closed off (AD-137)

Raised in §8 of this note's first draft and fixed the same day. A sign-off that leaves the
grade at "Leave for a separate regrade" still parks the case at Awaiting Recheck — that is
intended — but it no longer does so silently. `SignoffProgressPlugin.ParkedAtRecheck` asks,
**after** `MoveCase` and `RecordFinalOutcome` have run, whether the case is still sitting at
the recheck; when it is, a `Recheck due` notification goes to the person who signed off.

Read from the case after the move rather than predicted from the sign-off row, which is what
makes it right in every branch: a graded approval has already Closed, a Tax leg owing AQS went
back to Queued, and a Tax-only case closed without a recheck. Keyed on the case, so eighteen
sign-offs on one check queue one reminder. A prompt rather than a refusal — forcing a grade
would remove a choice the project owner asked for on 2026-09-11.

## 9. Who holds what, and why that is a problem waiting

The service account performs admin actions as **System Administrator**, in both environments —
not through any application role. So does Simunye. That role grants every privilege *and*
short-circuits `EnsurePermission`, so neither account ever touches the permission model.

**Nobody holds `Outcome Testing App User`.** That is not harmful today — everyone who needs
access already has System Administrator — but it carries two costs that fall due before PROD:

- **Permission testing cannot be trusted.** Every tester is a sysadmin and bypasses both tiers,
  which is exactly how a gap as large as AD-135's survived. A sysadmin reporting "permissions
  work" is evidence of nothing.
- **Everyone is drastically over-privileged.** System Administrator can delete data, change
  schema and alter security. Tolerable in DEV; unacceptable in PROD, where it would let every
  checker and adviser drop tables.

Before PROD, ordinary users need `App User` **and** to lose System Administrator. That switch
is also when AD-135's grants get their first real test, because `App User` has never been held
by anyone either. Worth doing with one tester well ahead of go-live, not on the day.

## 10. Still open

- **`al_recheckrequired` means nothing to the lifecycle.** It is a form question on the
  remediation action (AD-095), read for display and by no status code. "Recheck required = No"
  cannot suppress Awaiting Recheck, which is not what the field's name leads a user to expect.
- **Nobody has signed in as Zoe.** Her privileges are verified server-side and the app is
  shared with her, but the permission model has still never been exercised by a human.
