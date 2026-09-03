# Delivery status — 2026-09-03

Supersedes `docs/2026-09-02-delivery-status.md`, which was written at commit `470fc61` and
missed the four commits that followed it. This document covers those four, plus the work
done on 2026-09-03, and re-states current state as a whole.

Every claim below was verified against `Env_AQ_Dev` or a command output on this machine,
not inferred. Where something was **not** verified, it says so.

---

## 1. What the four uncovered commits delivered

These landed after the 2026-09-02 status document was written, so nothing recorded them.

### Manager MI drill-down and Excel exports (`56dd370` — BR-005, BR-010, AD-039)

The largest of the four. Managers could see counts but could not reach the cases behind
them, and nothing left the system except the screen it was rendered on.

- **A dead column brought back.** `CaseSummary.latestOutcome` was hardcoded `null`, so the
  worklist's "Latest outcome" column had never shown anything. `useCaseWorklist` now reads
  `al_outcome` alongside `al_outcomecase`, preferring the final outcome over the initial
  (BR-007). The read degrades to "not yet graded" rather than blanking the worklist,
  because a caller may hold case read without holding outcome read.
- **Drill-down.** Worklist filters moved into URL search params, which is what lets a
  dashboard card or a person view link to a filtered list — and makes an exported view
  shareable as the link that produced it. Outcome cards count per case using the same
  helper as the worklist, so a card and the list it opens cannot disagree.
- **People.** `/people` and `/people/:role/:name`, derived from the names recorded on the
  case. Deliberately *not* joined to `al_User`: that registry is keyed on work email
  (AD-010) and `al_outcomecase` does not carry it, so a display-name join would assert a
  link nobody made. The page says so rather than implying a directory it is not.
- **Exports.** `app/src/lib/xlsx.ts` writes real `.xlsx` — stored-method zip, CRC32,
  inline strings — written rather than taken from npm because the bundle ships to the
  Power Apps player.

### Tax-only export rows (`33c045a` — AD-075, closes OD-031)

A closed Tax-only case is now exported with AD-039 columns 10 and 15 blank rather than
refusing the batch. Both graded columns are sourced from the AQS review, so on a Tax-only
case they have no source and never will: refusing was a permanent block on any batch
containing one, not a prompt to act. The route decides, falling back to "has an AQS review
instance" for cases predating the route seed, so a genuine AQS case closed ungraded is
still refused. The OD-024 accountability rule is deliberately not relaxed.

The same commit carried a previously uncommitted completeness gate closing two holes: a
Closed case with no Outcome escaped every check, and a missing `Q-FQ-01` answer shipped a
blank column 10.

### Portal self-claim and queue list (`33c045a`, `d02d46f` — AD-076)

A queue section on the Tax and AQS review pages lists cases at `Queued` whose route puts
that discipline next. "Run checks" POSTs an `al_caseassignment` row; `ClaimCasePlugin`
resolves the Dataverse user from the contact's work email (AD-010), opens or attaches to
the review instance, releases any prior active assignment, moves the case
`Queued → Assigned` and writes the audit event.

Modelled on AD-074 rather than AD-073: a create, so no trigger column, and a table of its
own carries a permission of its own. Authorization here cannot be a caller check — a Power
Pages write reaches Dataverse as the site's application user (AD-053) — so Contact scope
through `contact_al_caseassignment` is what supplies identity.

`d02d46f` then gated the whole action behind a new site setting
`OutcomeTesting/Claim/Enabled`, deployed **false**, because `ClaimCasePlugin` could not be
compiled on the authoring machine. Without the guard registered, the Web API create still
succeeds — `al_name`, `al_caseassignmentcode`, `al_assignedon` and `al_isactive` are only
*business* required, which Dataverse does not enforce through the API — so the page would
have reported "Assigned to you" while leaving a bare assignment row that AD-037 forbids
deleting.

That commit also corrected a stale assumption worth carrying forward: **DEV does have
portal contacts with emails.** The 2026-09-02 document's "blocking demonstration" gap is
therefore closed.

### Portal page parameter (`2554ad8`)

`integer` rather than `times: 1` to coerce the page parameter.

---

## 2. What was done on 2026-09-03

### The .NET SDK blocker was stale

`33c045a` and `d02d46f` both recorded "this machine has no .NET SDK" as the reason
`ClaimCasePlugin` was unbuilt, untested and undeployed. **The machine has .NET SDK
10.0.400.** The assembly compiles clean and the full plug-in suite passes, including nine
tests for `ClaimCasePlugin` that had never been run.

This is worth recording as a habit, not just a fact: the blocker was re-stated across two
commits without being re-tested.

### ClaimCasePlugin registered in DEV

Step 1 of the three-step switch-on from `d02d46f`:

| Step | Command | Result |
|---|---|---|
| Assembly | `pac plugin push --pluginId 7b51d0d1-…` | "Plug-in assembly was updated successfully" |
| Plug-in type | `registertype … ClaimCasePlugin` | created `7e4ffad2-b8a7-f111-aaac-e4fade069307` |
| Step | `registerstep … Create al_caseassignment 20` | created `e10a09ed-b8a7-f111-aaac-e4fade069307`, added to the `OutcomeTesting` solution |

The pushed assembly holds 26 plug-in types against the 20 DEV was running — a superset, so
no live type was withdrawn (the AD-052 reasoning).

Two operational notes. `plugins/OutcomeTesting.Registration` targets `net8.0` and only the
.NET 10 runtime is installed, so it needs `DOTNET_ROLL_FORWARD=Major` to run. And `pac` is
not on `PATH`; it lives at `%USERPROFILE%\.dotnet\tools\pac.exe`.

**Steps 2 and 3 are not done.** The `Case Assignment - claim` table permission and the
`OutcomeTesting/Claim/Enabled` setting both remain as `d02d46f` left them, so the queue
list renders and the claim action stays inert. The guard now exists, which is what made
the permission safe to create — that was the stated reason for holding it back.

### OD-028 closed — headless Configuration Migration import

The roles seed had been fixed on 2026-09-01 but never re-imported, so its idempotency was
asserted rather than shown. The CLI gap turned out to be permanent, not a version accident:
`pac data` does not exist in 2.11.2 (re-confirmed against the installed binary's verb list),
and the Configuration Migration Tool is a WPF application whose `/I` and `ImportData`
strings are **control names, not command-line switches**.

So `plugins/OutcomeTesting.DataImport` now drives the tool's own engine —
`Microsoft.Xrm.Tooling.Dmt.ImportProcessor`, the same assembly the GUI calls — through
`ImportCrmDataHandler.ImportDataToCrm(folder, deleteBeforeAdd: false)`. Reusing the engine
is the point: the matching semantics being proved are the real ones, not a hand-rolled
lookalike that could agree with the seed while disagreeing with the tool.

`data/roles-seed` was imported against DEV **twice**, both runs reporting 0 failures:

| | Before | After two imports |
|---|---|---|
| `al_role` rows | 11 | **11** |

The 10 seeded roles plus `ROLE-TEST-ROLE`, every `al_rolecode` and every `al_name` distinct.
The corrected `primaryKey="true"` on `al_rolecode` matches the real alternate key and
updates in place, which is what NFR-REL-01 requires of configuration loading.

Two traps worth carrying to whoever runs this next: the package targets **net48**, not
net462, and `CrmServiceClient` binds ADAL at *runtime*, so omitting
`Microsoft.IdentityModel.Clients.ActiveDirectory` builds clean and then fails on the first
connection with a `FileNotFoundException` that names an assembly nothing references.

### Recheck and regrade built (FR-024)

`/cases/:caseId/recheck` replaces its `NotBuiltYet` stub. The T&C Manager sets the final
outcome with a mandatory reason through `al_RegradeCase`; the initial outcome is preserved
so both survive (BR-007, OD-007, AD-031).

- Gated on `page.cases` for view and `command.regrade` for the action. The screen is an
  affordance, not a boundary — the command re-checks server-side (NFR-SEC-01).
- The outcome's `versionnumber` is passed as `ExpectedRowVersion`, and a `Conflict`
  response reloads rather than retrying: the grade on screen is no longer the grade being
  corrected, so a resubmit would act on a stale reading.
- One idempotency key per `(outcome, grade)` intent, so a retry after a timeout replays
  rather than writing a second correction (NFR-REL-01).
- `al_RegradeCase` had never been registered with the code app. Added via
  `pa app add dataverse-api`, without which the Power Apps client cannot resolve the
  command and the call fails before reaching Dataverse — the defect `operations.test.ts`
  exists to catch.

A Tax-then-AQS case records an outcome per review instance, so the screen asks which one
is being corrected only when there is more than one.

---

## 3. What was done later on 2026-09-03

Five items were taken on together: the two outstanding app gaps, the two outstanding
portal gaps, and PP-15. All five moved. Everything below was run against `Env_AQ_Dev` or
is a local command output on this machine; where something was **not** verified, it says so.

### BR-002 import validation moved server-side (AD-077)

The one place the solution broke its own AD-003 invariant. `useCaseUpload.ts` created
`al_importexception`, `al_importbatch` and `al_outcomecase` rows directly over OData, so
BR-002 lived only in the browser and anyone posting to the Web API met none of it.

`al_ImportCases` now takes the raw extract **as text** and does the whole import in one
transaction — batch, cases, exceptions, audit event. Sending text rather than parsed rows
is the point: if the client parsed and the server only stored, BR-002 would still be a
client rule with a server-shaped wrapper round it.

The Code App now has **no direct Dataverse writes at all** — `grep` for `.create(`,
`.update(` and `.delete(` outside `src/generated/` returns two `Map`/`Set` calls and
nothing else.

Three defects surfaced while moving the rule, each of which had been silently importing
wrong data:

- **Month-first dates.** `01/13/2026` failed the UK pattern and fell through to a
  month-first parser, importing as 13 January instead of being refused. Both copies now
  reject a numeric date that matches neither accepted order, rather than guessing.
- **An advice date a day early.** The client's written-out-date path (`31 Jan 2026`) built
  a local `Date` then called `toISOString()`, which lands on the 30th anywhere east of UTC.
- **Row numbers that slid.** A blank line in the file renumbered every row after it, so a
  rejection pointed at the wrong row in the user's own spreadsheet.

The duplicate check was also one `RetrieveMultiple` per row — 1000 round trips inside a
two-minute plug-in budget on a large file. It is now chunked, and a file over 1000 rows is
refused with a message saying to split it rather than timing out part-written.

### Import exceptions can be closed (AD-078)

`/imports` rendered exceptions read-only, so FR-002/FR-003's "return with a reason" had a
return half and no resolve half. `al_ResolveImportException` closes one as `Resolved` or
`Ignored` with a mandatory note, and the screen grew an inline form behind `page.imports`
Edit.

Two deliberate constraints. The note goes in a new `al_resolutionnote` column, never over
`al_reason`, so closing an exception cannot erase why the row failed. And the command
refuses an exception that is not Open: a second closure would overwrite the first one's
note and timestamp, and AD-037 forbids deleting the row to put it back.

No third status is invented — `Returned` reads like the BR-002 wording but is not a value
the option set holds, and the command refuses it explicitly rather than coercing it.

### PP-13 clock reset (AD-079)

`al_remediationaction.al_clockstartedon` is stamped by `SignoffProgressPlugin` on a
rejected sign-off; `createdon` keeps the original start. The two together give the previous
period and the current one, so a case that has been round twice reads as two timers.

The ten-working-day threshold measures the **current** period. That is the point of a
reset: banding on the merged age reported a freshly reworked action as breached before the
adviser had had a day on it. The Code App reads this through `remediationClock()`
(dashboard breach count, reports ageing bands) and the portal remediation list shows the
previous period beside the current one.

A third round's earlier boundaries live on the `al_signoff` rows, which is where OD-018
puts the audit trail. The helper reports the two periods the columns can answer for and
does not guess at the rest.

### AD-076 self-claim switched on

Steps 2 and 3 of the three-step switch-on, both done: the
`Case Assignment - claim from the queue` permission was created in DEV (verified: created
`2026-09-03 18:23`, Contact scope `756150001`, create and read, both reviewer web roles
attached) and `OutcomeTesting/Claim/Enabled` is now `true`. The plug-in was already
registered earlier in the day, which is what made the permission safe to create.

**Not verified: the claim path end to end.** Nothing has exercised `ClaimCasePlugin`
against a real queued case in DEV. The wiring is now all in place for someone to try it.

### PP-15 outbox built with five events (AD-080)

Project owner direction answered OD-030 gap (a): **build the five AD-035 events only** and
add the rest when someone names them, because adding option values later is additive while
inventing four business events is not.

`al_Notification` was created through the metadata API rather than hand-authored solution
XML, so Dataverse generated the system columns and the definition enters `src/` on the next
export round trip — AD-013's "commit what Dataverse emits". A new
`createnotificationtable` verb on the registration console does it, idempotently, creating
components directly into the `OutcomeTesting` solution rather than the default one.

Emitters are hung off **record creation**, not off the commands:
`NotificationEmitterPlugin` runs synchronous post-operation on Create of
`al_caseassignment` (Allocation) and `al_remediationaction` (Remediation assigned).
Allocation reaches `al_caseassignment` by three routes — `al_AssignCase`, the portal
self-claim, and a manager writing the row — and **nothing in the solution creates
remediation actions at all**, so a command-side emitter would have missed every one of
them. Sign-off approval and rejection come from `SignoffProgressPlugin`, keyed on the
sign-off rather than the action so a case that goes round twice notifies twice.

The deterministic `al_notificationcode` is the table's alternate key, so a retry collides
instead of queueing a second email. That is the duplicate-proofing, not an after-the-fact
scan for near-identical rows.

**PP-15 is not deliverable yet, and two things are why.** Both are recorded on OD-030.

1. **The drain is not built.** Server-side email needs an approved, tested mailbox and that
   is still unchecked on DEV, TEST and PROD. Rows rest at `Pending`, which is the honest
   state: the outbox records that the event happened and nothing claims an email was sent
   that was not.
2. **The `Review submitted` recipient is undecided.** For allocation, remediation and both
   sign-off outcomes the record names the person, so the emitter reads it. For a submitted
   review nothing in any requirement says who hears about it, so that event queues with no
   address rather than a guessed one — the gap sits visible in the outbox instead of buried
   in code.

---

## 4. Deployment to DEV, and what it cost

Everything in section 3 is deployed to `Env_AQ_Dev` and verified there by query, not
inferred. Four tooling corrections came out of it, all of which contradict what the
existing runbooks imply.

| Step | Result |
|---|---|
| `pac plugin push` (assembly, twice) | updated successfully |
| `registerall` | 21 commands registered, including the two new ones |
| Schema import (staged: 3 entities) | imported and published |
| Custom-API-only import | imported; both APIs are now solution members (21 total) |
| `createnotificationtable` | table, 12 columns and the alternate key created in the solution |
| `registertype` + 2 × `registerstep` | both steps Post-operation, Synchronous, Enabled |
| `pac pages upload` | **partially failed — see below**; all four intended changes landed |

Verified by query afterwards: `al_clockstartedon` and `al_resolutionnote` both accept
reads (a control query for a deliberately bogus column errors, so an empty result proves
the column exists rather than being ignored); `ResolveImportException` = 120910791 is in
the `al_command` option set; both custom APIs are solution members; the five notification
events and three statuses carry the intended values.

### Four tooling corrections

1. **`pac plugin push` now needs `--type Assembly`.** Building the plug-in project leaves a
   `.nupkg` in `bin/`, after which `pac` defaults to package mode and fails with
   `Entity 'PluginPackage' ... Does Not Exist`. The earlier runbook's bare
   `pac plugin push --pluginId <id>` no longer works on a machine that has built the project.
2. **Custom API descriptions are capped at 300 characters, and response-property
   descriptions at 100.** Both were discovered by a failed `registerall` that had already
   created the plug-in type and half the parameters. Request-parameter descriptions are
   not capped at 100 — several existing contracts exceed it — so the limit is specific to
   `customapiresponseproperty`.
3. **A schema-only solution import needs its entities listed as RootComponents.** The
   Custom-API-only recipe empties `<RootComponents>`, which works because custom APIs are
   sharded components; doing the same for entities fails the pack with
   `RootComponent validation failed`. Keep exactly the changed entities.
4. **`registerstep` takes the stage before the filtering attributes.** Passing `""` for the
   filter shifts the arguments, and the tool rejects it with "Stage must be 20 or 40" —
   which reads like a bad stage rather than a dropped argument. The code comments say so;
   the runbooks do not.

### The portal upload does not complete against this site

This is the one thing that did not finish cleanly, and it is worth carrying forward.

**The DEV site is on the Enhanced Data Model.** `pac pages upload` without `-mv Enhanced`
refuses outright. With it, the upload runs and then dies:

```
Error: Upload failed for file 'adx_entitypermission'
(record a1000000-0000-4000-8000-000000000072): the target entity was not found.
```

That record is `Response - on a review assigned to me` — a **parent-scoped** table
permission, and one this change did not touch. `pac` maps most components into
`powerpagecomponent` correctly but routes that one down the Standard path, where
`adx_entitypermission` does not exist. A second run reproduced it exactly, reaching further
(12/19 events rather than 10/19) because the earlier records were already current.

**Nothing was lost, and this was checked rather than assumed.** All four intended changes
are live in DEV, each confirmed by query with a `2026-09-03 18:23` timestamp: the claim
site setting (`true`), the claim table permission (created, correct scope and roles), the
`OT Remediation` web template (contains `al_clockstartedon` and `data-created`), and
`outcome-testing.css`. The two permissions the run errored on are both present and updated.

Two follow-ups for whoever picks this up:

- The upload cannot be relied on to run to completion, so anything uploaded needs
  verifying by query afterwards until the parent-scoped permission path is sorted out.
- `.portalconfig/org0b075da8...-manifest.yml` holds at least one stale id
  (`5140384b-f6a3-f111-aaac-e4fade069307`) pointing at a record that no longer exists in
  DEV; every run reports it as a failed update. A `pac pages download` would refresh the
  manifest but rewrites the source folder, so it is a deliberate decision, not a tidy-up.

---

## 5. Gaps

### Awaiting a decision or a prerequisite

**PP-15 (OD-030).** No longer blocked on naming — the five AD-035 events are built. Blocked
on two things instead: the **server-side mailbox**, unchecked on all three environments,
without which the drain cannot be written honestly; and the **`Review submitted`
recipient**, which no requirement names.

**OD-032 — `SetFailAccountability` has no `al_command` value of its own.** Found while
adding `ResolveImportException` to that option set. It reuses `120910788`, which the set
labels `SetRoleAssignmentActive`. Two real consequences: every Audit Event that command
writes is **labelled as a different command**, so the AD-039 accountability trail reads as
a role change; and `CommandHelpers.FindAuditByKey` scopes replay lookups to
`(key, command)` precisely so one command's key cannot replay against another — between
these two, that protection is off. The fix is additive (mint a value, repoint the plug-in)
but it changes what already-written immutable audit rows mean, so it is a call for whoever
owns the accountability trail rather than a silent correction.

**OD-023 support model.** Unchanged: still needs the named person and the response targets,
and naming one person reintroduces the single point of failure that 2026-08-30 direction
was written to avoid. A deputy costs nothing and removes it.

### Known and accepted

- **Per-team allocation scoping (OD-029).** Unchanged.
- **Senior Checker needs `command.assign`** granted as a Dataverse row. Unchanged.
- **Optimistic concurrency is not sent on the portal submit path.** Unchanged.
- **`src/` is behind DEV on three tables and one new one.** `al_Notification` was created
  through the metadata API and `src/Entities/` has no definition for it; the two new
  columns and the new option value are in `src` because they were imported *from* it. The
  AD-013 export round trip is the way this closes, and it is now owed.
- **Lint carries the same 10 warnings.** All `react-hooks/exhaustive-deps`, all
  pre-existing, none introduced by this work. Zero errors.

### Closed since section 3 of this document was first written

- **Import writes bypassing the server-command rule** — closed by AD-077.
- **OD-018 clock reset** — closed by AD-079.
- **AD-076 steps 2 and 3** — done, and the claim action is live in DEV.

---

## 6. Verification

Run on this machine on 2026-09-03, after the work in section 3.

| Check | Before today's second block | Result |
|---|---|---|
| Plug-in tests | 198 | **268 passed**, 0 failed |
| Code App tests | 123 | **148 passed**, 13 files |
| Code App build | clean | clean |
| Code App lint | 0 errors, 10 warnings | 0 errors, 10 warnings (same 10) |
| Registration console build | — | clean |
| `Check-ComponentIds.ps1` | 236 identities | 236 identities, no duplicates |
| `Check-PortalSecurity.ps1` | all pass | all pass, 13 table permissions in source |

**Not verified.** Three things, and none of them can be verified from this machine today:

1. **The claim path end to end.** The permission and the setting are now both live, but
   nothing has taken a queued case through `ClaimCasePlugin`.
2. **The import and exception commands against real Dataverse.** Both are registered and
   are solution members; the validation rules have 43 unit tests behind them, but no
   extract has been pushed through `al_ImportCases` in DEV.
3. **Any notification actually being emitted.** The steps are registered, synchronous and
   enabled, and the outbox has unit tests, but exercising them means creating a real
   `al_caseassignment` or `al_remediationaction` row — business records that AD-037 forbids
   deleting afterwards. That is a deliberate choice not to fabricate records in a shared
   environment, not an oversight.
