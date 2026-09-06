# Contacts as the user registry, one People directory, and honest exports

Date: 2026-09-06
Status: Approved for implementation
Environment probed: `Env_AQ_Dev` (`org0b075da8`)

## Problem

Three complaints, one of which turned out to rest on a false premise.

1. Application users should come from the `contact` table. Anyone not in Contacts
   should go. Existing cases should be reassigned to the three people who are in
   Contacts, with a few left unassigned.
2. The application has both a "People" view and a "User" table. Only one is wanted,
   keeping the functionality of both.
3. After generating a Trail Light export batch the Download control stays inactive,
   and pressing Generate again returns an unreadable error.

## Evidence

Read-only FetchXML probe of `Env_AQ_Dev`, via a `fetch` verb added to
`plugins/OutcomeTesting.Registration`.

### Identity

`contact` holds 3 active rows; `al_user` holds 10. **The two sets do not
intersect.**

| `contact` | email |
| --- | --- |
| Dev Account | `svc.automate.aq-dev@ascotlloyd.co.uk` |
| Service Account | `svc.automate.aq@ascotlloyd.co.uk` |
| Sims Rad | `Simunye.Radingwana@ascotlloyd.co.uk` |

`al_user` is 10 fictional `@example.com` seed rows (Alex Adminki, Casey Checker,
Jane Adviser, Jordan Quality, Morgan Manager, Quinn Reader, Riley Senior, Robin
Reviewer, Sam Paraplanner, Taylor Trainer), sourced from `data/users-seed`.

All three Contacts have a matching **enabled, Read-Write `systemuser`**, so
`AssignCasePlugin.ResolveAssignee` — which demands both halves of the identity
(OD-003, AD-010) — will resolve each of them.

### The fact that makes this migration tractable

`al_UserRoleMapping` keys on **`al_useremail`, a text field**, not a lookup to
`al_user`. The environment's only mapping is
`Administrator -> svc.automate.aq@ascotlloyd.co.uk`, and that email has **no
`al_user` row at all**.

`PermissionHelpers.IsRegisteredActive` returns `true` when no `al_user` row is
found, by deliberate design. Authorisation therefore already runs independently of
the `al_user` table, and removing that table cannot withdraw anyone's access.

### Cases

13 cases, all owned by `svc automate aq`, and `al_caseassignment` holds **0 rows** —
no case has ever been formally allocated.

| Status | Count |
| --- | --- |
| Imported | 11 |
| Ready for Allocation | 1 |
| Review In Progress | 1 |
| **Closed** | **0** |

### Exports

One batch, `EXB-59585fce…`, status `Generated`, `al_rowcount: 0`, and **0
`al_exportrecord` rows**.

## Root causes

### The Download control is not defective

`GenerateExportPlugin` selects only cases at `al_casestatus == Closed`. There are
none, so the batch is legitimately empty, and `ExportMenu` disables its trigger on
`rows.length === 0`. The control is correct; the **application never explains
itself**. It reports "Generated 0 export record(s)" and then presents a dead
button with no stated cause.

### The unreadable error is a slicing defect

`app/src/services/commands/commandClient.ts:84`:

```js
message.slice(message.indexOf(prefix) + prefix.length).trim()
```

`extractErrorMessage` yields the whole JSON fault body. Slicing at `PRECONDITION:`
keeps everything after it — the sentence *and* every trailing
`@Microsoft.PowerApps.CDS.*` diagnostic. That is character-for-character what the
user saw.

Behind it sits a second defect: **Generate stays enabled on a batch that is already
Generated**, so the plug-in's correct precondition guard is reachable by an
ordinary click rather than being prevented by the UI.

### "People" is not a table

There is no `al_Person` entity. `app/src/features/people/peopleDirectory.ts` builds
the People page in memory from name strings already carried on `al_OutcomeCase`
(adviser, paraplanner, checker, owner). Its own comment states it "does not claim to
be a user directory", because the case carries no work email to join on.

So the request is not "delete one of two tables". It is **give the two views one
keyed source** — which is exactly what moving to Contacts provides.

## Decisions

| # | Decision |
| --- | --- |
| D1 | `contact` becomes the single application user registry. |
| D2 | `al_user` is **decommissioned, not deleted**. See "Why not drop the table". |
| D3 | `al_UserRoleMapping` is left untouched; it already keys on email. |
| D4 | `/people` becomes the one directory, carrying both views' functionality. |
| D5 | Case adviser/paraplanner text is rewritten to the three real people. |
| D6 | Cases are allocated through `al_AssignCase`, never by direct field writes. |
| D7 | A subset of cases is driven to `Closed` so the export yields real rows. |

### Why not drop the table

`al_User` ships inside the managed solution. Deleting a table that managed
deployments have already carried to TEST and PROD is a destructive ALM change, and
`data/users-seed`, AD-010 and OD-010 still reference it. Decommissioning reaches the
same end state for the application with no ALM landmine. Physically deleting the
table is a separate, later change once a managed deploy has been through.

## Design

### 1. Contact as the registry

Add `contact` as a Code App data source (`pac code add-data-source`), regenerating
`app/src/generated/`. It is absent from `app/power.config.json` today.

| `al_User` | `contact` |
| --- | --- |
| `al_workemail` | `emailaddress1` — remains the AD-010 key |
| `al_name` | `fullname` |
| `al_isactive` | `statecode` |

`PermissionHelpers.IsRegisteredActive` is repointed at `contact.statecode`, keeping
its permissive "no row means allowed" fallback so the change cannot lock anyone out.

Consumers to repoint:

- `app/src/hooks/useUserDirectory.ts`
- `app/src/features/cases/useAllocationCandidates.ts`
- `app/src/components/form/UserPicker.tsx`
- `app/src/app/permissions/PermissionProvider.tsx`
- `app/src/features/admin/useSecurityConfig.ts`
- `app/src/features/reports/fullExtract.ts`
- `app/src/services/commands/users.ts`

### 2. One directory at `/people`

`/people` becomes the single People directory: per-person caseload and outcome
breakdown with drill-down (today's People page), plus create, edit and
activate/deactivate for holders of `permission.manage` (today's Users page).
`/admin/users` redirects to it, and the Administration nav entry is removed.

Because the directory and the case-derived roll-up now share `emailaddress1` as a
key, the page joins them instead of guessing by display name.

### 3. Custom APIs repointed

`al_CreateUser`, `al_UpdateUser` and `al_SetUserActive` retarget `contact`:

- `CreateUserPlugin` — `contact.fullname` is **calculated and not writable**, so a
  supplied name must be split into `firstname` / `lastname`.
- `SetUserActivePlugin` — becomes a `SetStateRequest` on the Contact.
- Contract JSON in `plugins/customapi/` updated to match; parameter names are kept
  so the client surface does not churn.

`al_AssignUserRole` and `al_SetRoleAssignmentActive` are unchanged (D3).

### 4. Data migration

Against `Env_AQ_Dev` only, through a `migrate` verb on the Registration tool so it
is repeatable and auditable.

1. Delete the 10 `@example.com` `al_user` rows.
2. Rewrite `al_advisername` / `al_advisercode` / `al_paraplanner` /
   `al_paraplannercode` across all 13 cases to the three Contacts, round-robin with
   an offset so a case's adviser and paraplanner are never the same person.
3. Allocate **9** cases (3 each) via the `al_AssignCase` Custom API, so
   `al_CaseAssignment` rows and the `Assigned` transition are genuine.
4. Leave **4 unassigned**: `IO-100010` and the three `IO-DEV-VERIFY-*` fixtures.

Allocation walks `Imported -> Ready for Allocation -> Queued -> Assigned` via
`CaseTransitions.MoveThrough`, which is why direct status writes are forbidden.

### 5. Driving cases to Closed

To make the export produce rows, a subset of the 9 allocated cases is taken
`Assigned -> Review In Progress -> Submitted -> Closed` through `al_SubmitReview`.

`GenerateExportPlugin.DescribeIncompleteRow` refuses an incomplete row, so each
closed case must satisfy:

- an `al_Outcome` carrying an initial or final grade; and
- where an AQS review was due, an answer to `Q-FQ-01`; and
- where the outcome is a non-pass, at least one accountability flag.

Target a **Pass** outcome with `Q-FQ-01` answered, which satisfies all three without
needing remediation. Exact seeding is confirmed against `SubmitReviewPlugin` during
implementation.

### 6. Export fixes

Independent of the migration and shippable on their own.

- **Message trimming.** `classify` in `commandClient.ts` cuts the classified message
  at the JSON boundary, yielding one sentence. Covered by unit tests using the real
  fault body captured above.
- **Generate disabled unless Draft.** `ExportsPage` disables Generate for any batch
  whose status is not `Draft`, so the precondition guard is never reached by
  clicking.
- **Honest empty state.** A batch generating 0 rows says so — "0 closed cases, so
  there is nothing to export" — and the disabled Download control carries a title
  explaining why, rather than presenting a dead button.

## Non-goals

- Physically deleting the `al_User` table from Dataverse (D2).
- Any change to `al_UserRoleMapping` or the authorisation model (D3).
- TEST or PROD. DEV is the only authoring environment.
- Changing the AD-039 20-column export contract.

## Risks

| Risk | Mitigation |
| --- | --- |
| Regenerating `app/src/generated/` overwrites hand edits | Generated tree is autogenerated only; diff reviewed before commit |
| `contact.fullname` is not writable | `CreateUserPlugin` splits into `firstname`/`lastname` |
| Deleting `al_user` rows withdraws access | Cannot: authorisation keys on `al_useremail` and tolerates a missing row (evidenced above) |
| Closing cases writes immutable Audit Events | DEV only, on seed cases; NFR-AUD-01 accepted |
| Case allocation refused by lifecycle | Allocation uses `MoveThrough`, never direct status writes |

## Testing

- Unit: `classify` against the captured CDS fault body; the round-robin
  adviser/paraplanner assignment; `IsRegisteredActive` against `contact.statecode`.
- Plug-in: existing `OutcomeTesting.Plugins.Tests` suite stays green; new cases for
  the retargeted user commands.
- Evidence: re-run the read-only probe after migration and record before/after
  counts for `contact`, `al_user`, `al_caseassignment`, case status and
  `al_exportrecord`.
- End to end: create a batch, generate it, confirm a non-zero row count and an
  enabled Download producing the 20-column file.
