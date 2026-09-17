# The owner check is on the assignee, not the caller — AD-144

**2026-09-17.** Reported as "Adam has access, but he's not able to assign cases." Adam's
permissions were complete. The person he was allocating *to* was the one who could not.

## The fault, read properly

```
OrganizationServiceFault: Read Privilege Check For Owner failed with exception:
Principal user (Id=bbaae4b1-1aae-f111-aaac-002248c654cd, type=8, roleCount=1,
privilegeCount=537, ...), is missing prvReadal_ReviewInstance privilege
... context.Caller=6d08a459-d0b1-f111-aaac-002248c654cd.
```

Two different people:

| Field | Id | Who |
|---|---|---|
| `Principal user` | `bbaae4b1-…` | **Clare Hook** — the assignee |
| `context.Caller` | `6d08a459-…` | **Adam Strumidlo** — the allocator, and fine |

`AssignCasePlugin` stamps `ownerid` on the review instance, and Dataverse will not make
somebody the owner of a row they cannot read. Clare held `Basic User` alone — `roleCount=1`
in the fault matches exactly — which grants nothing on `al_reviewinstance`.

**A System Administrator caller does not bypass this.** The service account appeared to "be
able to assign" only because the one allocation it made (case 254397454, 11:05) went to Adam,
who is provisioned. Allocating to Clare would have failed it identically.

## What was audited before the assignee was looked at

Recorded because the wrong half was searched first, and the fault's wording is why. All of
this was verified live against TEST and all of it was already correct:

| Layer | Result |
|---|---|
| `command.assign` rules | Administrators = Manage, T&C Supervisor = Edit |
| Adam's `al_userrolemapping` | 5 roles, all Active |
| Adam's portal web roles | the same 5, via `powerpagecomponent_mspp_webrole_contact` |
| `al_caseassignment` | Create, Write, Append, AppendTo — all Global |
| `al_reviewinstance` | Create, Read, Write, Append, AppendTo — all Global |
| AppendTo on `contact`, `systemuser`, `al_outcomecase`, `al_checklistversion` | all Global |
| `al_AssignCase` step | Enabled |
| Access-level option values | monotonic (None 766 → Manage 769), so Manage satisfies Edit |

The lesson worth keeping: **read the principal id in a "Read Privilege Check For Owner"
fault before auditing the caller.** It is not the caller.

## Who was blocked in TEST

Four of nine application-role holders, every one of them a reviewer or supervisor —
provisioned on the portal side, never on the Dataverse side:

| Person | Application role | Security roles | |
|---|---|---|---|
| Clare Hook | Tax Reviewer | Basic User | blocked |
| Ruth Maxwell | AQS Reviewer | Basic User | blocked |
| Suresh Gautam | AQS Reviewer | Basic User | blocked |
| Angela Houghton | T&C Supervisor | Basic User | blocked |
| Adam, Zoe | several | + App Admin | ok |
| Gill Philpott | Administrators, Tax Reviewer | + App User | ok |
| Simunye, svc automate aq | — | System Administrator | ok |

## Why it does not happen automatically when a role is assigned

Three planes have to agree and nothing reconciles them:

1. **`al_userrolemapping`** — the application plane, keyed on work email
2. **`mspp_webrole` ↔ contact** — the portal plane, keyed on contact
3. **`systemuserroles`** — the Dataverse plane, keyed on `systemuser`

`al_AssignUserRole` writes the first and second. It does **not** write the third, and that is
deliberate:

- **Escalation.** The command is gated on `permission.manage`, an application rule. If it
  could grant Dataverse security roles, an application-level permission would confer platform
  privileges — the same escalation-safe split `GrantSecurity` keeps by making create/write on
  `al_userrolemapping` and `al_pagepermission` admin-only.
- **There may be nobody to grant it to.** A portal-only person has a contact and no
  `systemuser` at all (`docs/reference/portal-access-runbook.md`). Adam himself was in that
  state before he was licensed.
- **Licensing is a tenant action.** A security role on an unlicensed user means nothing, and
  a plug-in cannot licence anyone.

So the planes stay separate. What was missing was anything that *noticed* when they disagreed.

## What changed

### `EnsureCanHoldWork` — a fourth precondition on `ResolveAssignee`

Refuses an assignee holding none of `Outcome Testing App User`, `Outcome Testing App Admin` or
`System Administrator`, naming the person and the role to grant instead of emitting a SecLib
fault that names the wrong one.

Two deliberate choices, both documented at the method:

- **By role name, not by privilege.** Resolving `prvReadal_ReviewInstance` means reading
  `roleprivileges` and `privilege`; neither app role grants those, so the check would throw the
  fault it exists to prevent, and AD-109 forbids the catch that would absorb it. `role` and
  `systemuserroles` are readable at Global depth under AD-142. `PermissionHelpers` hard-codes
  `"System Administrator"` for the same reason.
- **Fail-open on an empty result.** "Cannot see any role" and "holds no role" arrive
  identically; refusing on the first would break allocation for every caller whose privileges
  cannot read the intersect — strictly worse than the fault being replaced.

`ClaimCasePlugin` shares `ResolveAssignee`, so the portal self-claim gains the same message.

### `grantapprole <orgUrl> [<email> ...] --confirm <orgUrl>` — the fix

Grants `Outcome Testing App User` to everyone `checkassignable` flags for the one reason this
verb can fix, over the **Dataverse** connection. Additive (Basic User stays), idempotent (a
role already held is skipped), and it leaves a disabled user or one with no contact alone —
those are different problems with different fixes.

It exists because `pac admin assign-user` resolves the email through **Graph** before it
writes anything — `Failed to get user id for <email>` — so it dies on a revoked Graph token
while the Dataverse connection is still healthy. All four grants failed that way on
2026-09-17 with `AADSTS50173`, the service account's tokens having been invalidated at
11:22:45Z, minutes after this investigation's own `pac env fetch` calls had succeeded. There
is no Graph lookup to do: everyone who can be allocated work already has a `systemuser` row —
that is the precondition being fixed — so the association is `systemuser` → `role`, both ids
readable from Dataverse.

### `checkassignable <orgUrl>` — the batch gate

Audits every holder of an application role against the same rule, reports why each blocked
person is blocked, prints the `pac admin assign-user` line to fix them, and **exits 1** when
anyone is blocked. Run it after each batch of people is onboarded.

## What ran

| | Step | Result |
|---|---|---|
| 1 | New tests, written first | 1 failed against the absent guard, as intended |
| 2 | `EnsureCanHoldWork` implemented | the 4 new tests pass |
| 3 | `dotnet test` (full suite) | **868 passed**, 0 failed — 864 before, +4 |
| 4 | `dotnet build` Registration | 0 errors (12 pre-existing warnings, unrelated) |
| 5 | `checkassignable` against TEST | **4 of 9 blocked** — matched the manual audit exactly |
| 6 | `pac auth create --name AQ-Universal` | project owner, after the revoked grant |
| 7 | `grantapprole … --confirm …` against TEST | **granted 4, skipped 5, failed 0** |
| 8 | `checkassignable` again | **all 9 can be allocated work** |
| 9 | Direct `systemuserroles` query | all four hold App User **and** Basic User — read back independently, not from the tool's own output |
| 10 | `dotnet build -c Release` | **227,328 bytes**, 13:53 — up from the stale 226,304 of 2026-09-16, so the guard is in the binary |
| 11 | `pushassembly` against TEST, first attempt | **did not run** — hung with no output on an interactive sign-in, killed; `modifiedon` still 9/16 17:49, checked rather than assumed |
| 12 | `pushassembly` → TEST and DEV | 227,328 bytes to both, by the project owner — 12:00:14Z and 12:00:45Z |
| 13 | `pluginassembly.modifiedon` re-read | **9/17 12:00** in both, matching the freshly built file |
| 14 | Step states in both | **51 Enabled, 0 Disabled** — checked because a 2026-09-02 import silently disabled six; `pushassembly` is not an import and did not touch them |

## Not done here

- **The checker dropdown still offers everybody.** `CaseEditPanel` lists every active contact
  with no filter on whether they can hold work, so an allocator can still pick a blocked
  person. They now get a sentence naming them and the role to grant rather than a stack trace,
  but not offering them is the better fix. Left as a separate change.
- **The new auth profile defaults to a different environment.** `AQ-Universal` carries
  `Ascot Lloyd (default)` / `org1e8859ab.crm4.dynamics.com` as its default, not DEV or TEST, so
  any `pac` command run **without** an explicit `--environment` now targets that org. Every
  command in this note passes one; anything copied from an older note may not.
- **PROD is untouched** and is a separate decision. `checkassignable` should be run there
  before anyone is expected to receive work.
- Nothing committed.
