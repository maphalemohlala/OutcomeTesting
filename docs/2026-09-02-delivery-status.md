# Delivery status — 2026-09-02

Where the solution stands after a day of work on the front-end gaps identified in
`docs/2026-09-01-front-end-state-and-gaps.md`. That document is now substantially out of
date; this one supersedes it.

Every claim below was verified against `Env_AQ_Dev` or a command output, not inferred.

---

## 1. What was delivered

### Portal submission works without Power Automate (AD-073)

The `SubmitReview` cloud flow is **cancelled, not deferred**. The review page writes one
allowlisted boolean, `al_reviewinstance.al_submitrequested`, over the Portals Web API and a
synchronous plug-in runs the submission — the same mechanic AD-053 already used for answers.

This was not only about avoiding a licence. **The flow design could not have worked.**
`EnsureCaller` compares `InitiatingUserId` to the review's `ownerid`, and a cloud flow runs
under its own connection identity; once allocation began setting `ownerid` to the assignee,
every submit through the flow would have returned `UNAUTHORIZED:`. The authorization model
had to change whichever route was taken.

### Allocation (OD-029, AD-072)

`al_CaseAssignment` gained the assigned-user lookup AD-029 deferred and AD-040 declared
unblocked but nobody added, plus the portal Contact equivalent. `al_AssignCase` resolves
**both** identity systems from one work email and refuses unless both exist — a case that is
allocated in the Code App and invisible in the portal is worse than a refusal.
`/cases/:caseId/allocation` replaces its `NotBuiltYet` stub.

### Remediation loop (PP-12) — both halves, and PP-14 closed

Adviser response, Intelligent Office reference and completion; then T&C approval or
rejection with notes. A rejection reopens the action; an approval advances the case.

The two halves use **different mechanics on purpose**. Completion is an update behind a
trigger column; attestation is a *create* on `al_signoff`. A trigger column for sign-off
would sit behind the one site-wide field allowlist shared with the adviser, so an adviser
could approve their own remediation. A separate table carries a separate permission.

PP-14 is closed by the same change: AD-046 reduced it to a reference field, and that field
now exists and is captured.

### Three Custom APIs registered, OD-026 resolved

`al_UpdateRole`, `al_SetRoleAssignmentActive` and `al_SetPermissionRuleActive` reached DEV,
so the editable-RBAC screens have something to call. The AD-013 round trip then ran: the
export carries 19 Custom APIs and a manifest declaring every plug-in type the assembly
builds, which closes the open half of OD-026.

### Smaller items

- My Work is scoped to the signed-in contact (PP-03), with a fail-closed guard for a null
  user — an unguarded `{{ user.id }}` emits an empty condition and counts every row.
- Working-day ageing on the remediation list (PP-13), matching `app/src/lib/workingDays.ts`.
- The four stale blocker notes corrected.

---

## 2. Issues found, and what they cost

### `pac pages upload --forceUploadAll` deleted nine table permissions

**The most serious thing that happened today, and it was self-inflicted.** The flag is not
additive: it reconciles, and deletes site components missing from the source folder. It was
used while the `table-permissions` folder was deliberately moved aside to dodge a CLI defect
— so the very components being protected were the ones removed.

It presented to the user as `Liquid error: Invalid cast from 'System.Int32' to
'System.Linq.Enumerable+...'` on every navigation page: templates querying tables the
signed-in user could no longer read. Fail-closed, so an outage rather than an exposure.

Recovered in full. The lesson is recorded at the step that uploads.

### `pac pages upload` cannot write table permissions to an Enhanced data model site

Independent of the above, and it will affect TEST and PROD. The CLI addresses the legacy
`adx_entitypermission` table and aborts the entire upload; the error blames `--modelVersion`,
which is a red herring. Three attempts restored nothing.

**Fixed permanently:** `restoretablepermissions` in `plugins/OutcomeTesting.Registration`
writes `powerpagecomponent` rows directly from the same YAML. This is now the only reliable
way to deploy table permissions to any environment.

Worth knowing: the abort happens **before** the web templates, so a run that reports failure
has still changed site settings and permissions while leaving every page on its old content.
Never assume a failed upload changed nothing.

### The security gate caught an over-grant that review did not

The sign-off permission was first authored Global-scope with create.
`Check-PortalSecurity.ps1` refused it — Global must never carry write, create or delete.
Parent scope gives the same reach without the blanket grant. The gate earned its keep.

### `Check-ComponentIds.ps1` never checked site-setting ids

It matched `webrole.yml` and `webpagerule.yml` by filename and nothing else, so despite
AD-059 assigning site settings the `a0`–`af` band, a duplicate there would have silently
destroyed another component. Fixed; coverage went from 171 to 232 identities.

### Smaller traps, all recorded

- `<plugintypeexportkey>` in `src/customapis` is environment-specific and had gone stale
  (AD-071). An import would have bound the API to a plug-in type that no longer exists —
  created, callable, and executing nothing.
- Hand-authoring an `SdkMessageProcessingStep` fails `pac solution pack`; the type must be
  created first and the step brought back by export.
- The Custom API `description` column has a hard 300-character limit that aborts
  `registerall` mid-run.
- Verifying portal content with `Select-String` gives false negatives — a component's
  `content` is one logical line. Use `-Raw` and `.Contains()`.

---

## 3. Gaps

### Blocking demonstration

**Portal identity in DEV.** No contact carries an email, so the AD-047 Entra sync has
evidently never run. Submission, adviser response and attestation are all built and
deployed, and **none can be demonstrated end to end** until a contact exists to sign in as.
This is the security-closure runbook's own step-2 gate; it now blocks four features rather
than one, and it is the single highest-value thing to resolve.

### Blocking build

**PP-15 notifications (OD-030).** Not started, deliberately. Two gaps: the nine events are
not enumerated anywhere — AD-035 names five and the other four appear in no requirement or
design document — and delivery is no longer obviously Power Automate, since AD-073 removed
that dependency entirely. Everything else is ready: the outbox shape is specified and
`al_Notification` is in the approved core model.

### Known and accepted

- **Per-team allocation scoping (OD-029).** Nothing stops a Tax lead allocating an AQS
  check. The screen deliberately offers no team filter rather than implying an enforcement
  that does not exist.
- **Senior Checker needs `command.assign`** granted as a Dataverse row through the Security
  screen; it cannot be expressed in the `DEFAULT_PERMISSIONS` seed matrix.
- **Optimistic concurrency is not sent on the portal submit path.** The state guard and the
  idempotent already-submitted branch cover the double-submit race, which is the one that
  actually occurs.
- **OD-018 clock reset** on a rejected sign-off is not built; it needs each period stored on
  the action rather than a single `createdon`.
- **The recheck/regrade screen** is unbuilt. Nothing blocks it — `al_RegradeCase` is
  registered and `al_Outcome` carries the final outcome; the screen itself is the gap.
- **Import writes still bypass the server-command rule.** `useCaseUpload.ts` creates rows
  directly over OData, so BR-002 validation exists only on the client.

### Unchanged from before

OD-023 (support hours and response targets), OD-025 (signing-key rotation), and AD-062's
standing warning that `src/PluginAssemblies/` holds a committed DLL, so anything packing the
whole of `src` must strip it first.

---

## 4. Verification

| Check | Result |
|---|---|
| Plug-in tests | 171 passed |
| Code App tests | 92 passed, 8 files |
| Code App build | clean |
| Code App lint | 0 problems |
| `pac solution pack --folder src` | Packed Solution |
| `Check-ComponentIds.ps1` | 232 identities, no duplicates |
| `Check-PortalSecurity.ps1` | all assertions pass |
| DEV table permissions | 12, verified by query |

DEV holds 23 plug-in types, 10 plug-in steps and 19 Custom APIs. `src` is round-tripped from
a fresh export, so solution source matches the environment.
