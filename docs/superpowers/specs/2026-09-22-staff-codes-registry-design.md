# Staff codes on the registry, and the People page as their home

Date: 2026-09-22
Status: Approved for implementation
Supersedes: AD-183 (Trail Light columns B and D carry emails)
Closes: most of OD-050

## Problem

Trailight have said they cannot accommodate the change made on 2026-09-21, which
replaced the codes in Trail Light columns B and D with email addresses (AD-183).
They want codes back.

The project owner's proposal: hold a staff code against each person — adviser,
paraplanner, checker and T&C Manager — alongside the emails being loaded for the
email solution, and use that code to populate the export's code columns. The
ParaplannerEmail import mapping stays. Administrators maintain the codes and the
role mapping themselves, from the People page, moved under administration.

## Evidence

### Why B and D became emails in the first place

The codes were empty, and nothing fills them.

`al_outcomecase` has `al_advisercode` and `al_paraplannercode`, but
`ImportRules.Columns` maps neither — the 82-column extract in
`data/io-task-extract-sample.csv` carries no code column for a person at all.
The only way a code reaches a case today is somebody typing it into
`CaseEditPanel`.

That is OD-050, still open: adviser code and paraplanner code are on the list of
eleven header fields the client said Tax/AQS would "manually complete", and in
practice nobody does. Emails went into B and D because emails were the only thing
actually present.

So reverting to codes is only viable if something fills them. Staff codes on the
registry is that something: it turns per-case data entry into reference data
loaded once per person.

### Every role can reach a Contact reliably

This is what makes the registry lookup safe rather than a return to name matching.

| Role | Join key to the registry | Strength |
| --- | --- | --- |
| Adviser | `al_outcomecase.al_adviseremail`, from the extract | Address — unambiguous |
| Paraplanner | `al_outcomecase.al_paraplanneremail`, from the extract (AD-186) | Address — unambiguous |
| Checker | `al_caseassignment.al_assignedcontactid` | Direct contact lookup — exact |
| Fail-accountable person | `al_outcome.al_fqaccountablecontactid` / `al_aqaccountablecontactid` | Direct contact lookup — exact |

The paraplanner row is why the `ParaplannerEmail` import mapping must stay.
Remove it and the only link from case to registry is the display name, which is
precisely the weak match `MatchPerson` was built to refuse: two active contacts of
one name resolve to nothing. The code would come back blank on exactly the rows
where it is ambiguous — reintroducing the problem this change exists to solve —
and the paraplanner's letter, which AD-165 records is their only sight of the
check, would stop being addressed.

### The fail-accountable columns are blank by construction

`GenerateExportPlugin.FlaggedText` deliberately empties a code column whenever a
specific person is named accountable:

> A CODE column is left empty when a person was named. A contact carries no
> adviser or paraplanner code, and emitting the case's code beside someone else's
> name would attribute the fail to a name and a code belonging to two different
> people — worse than a blank, because it reads as complete.

The reasoning is sound and the premise is about to become false. Once a Contact
carries a staff code, the named person has a code of their own, and those four
columns can carry it.

This is why the change fills six columns, not two.

### The People page is already most of what is wanted

`PeoplePage` already joins three sources — `useUserDirectory` (contacts),
`useSecurityConfig` (role mappings) and `useCaseWorklist` (caseload) — and already
renders Name, Work email, Status, Roles, Positions, caseload figures, Added and
management Actions. It already searches across name, email, code and roles, and
already has create and edit person modals.

Its "Code" column reads `row.load?.code` — the case's hand-typed value, which is
exactly what this change replaces.

### Roles already exist, and already grant access

`al_Role` is seeded with ten roles including **Adviser**, **Paraplanner**, **T&C
Manager**, **Tax Checker**, **AQS Checker** and **Senior Checker**.
`al_userrolemapping` maps `al_useremail` to `al_rolecode` — keyed on the same work
email as the staff code — and `PermissionHelpers` reads a caller's app roles from
it as well as from the portal web roles.

A role on this page is therefore not a label. Setting one grants system access.

## Decisions

| # | Decision | Rationale |
| --- | --- | --- |
| D1 | The staff code lives on `contact`, in one role-agnostic column | A code is a property of a person, not of a case or of a role. One column covers advisers, paraplanners, checkers and T&C Managers with no extra work. |
| D2 | The registry is the only source; the case's code columns are no longer read or written | One place to correct a code, and no way for two answers to disagree. The case columns stay in the database but come off the edit panel. |
| D3 | `ParaplannerEmail` stays mapped on the import | It is the join key that makes the paraplanner's code unambiguous, and it still addresses their letter. |
| D4 | Trail Light stays at 20 columns; the checker's code is not exported | Project owner, 2026-09-22. The contract is read by position and gains nothing from a column Trailight did not ask for. |
| D5 | The People page's Role column writes the real `al_userrolemapping` | It already exists, is already keyed on work email, and is already the authorisation source. A second, descriptive role concept would drift from it and confuse the page that showed both. |
| D6 | The three seeded checker roles stay distinct | Tax Checker, AQS Checker and Senior Checker match the seeded roles and the Tax/AQS review routing, so nothing has to be reconciled. |
| D7 | People moves to `/admin/people`, gated by `page.admin.users` | Required, not cosmetic: D5 makes role editing an access grant, and `/people` is gated by `page.cases` today, which every case-worker holds. |
| D8 | The Role column shows **every** role a person holds, and the filter matches any of them | `al_userrolemapping` is many-to-one by construction and the page already renders `roles` as a list. A single-valued column would have to pick one to show and would misrepresent anyone holding two — a T&C Manager who also advises, for instance. The column heading stays "Role" as asked. |
| D9 | The column is `al_staffcode`, labelled **"Employee code"** on the page | The logical name follows the original request ("load a staff code"); the label follows the column list asked for on the People page. Both names appear in this document deliberately: they are the same thing. |

## Design

### 1. `contact.al_staffcode`

A single text column on `contact`, which this solution already customises.

`MaxLength` is declared explicitly and `Length` is left alone. AD-026 records that
Dataverse reads `Length` as bytes and silently creates the column at half its
advertised size; `al_ChecklistCode` advertised 20 characters, held 10, and failed
in SQL only when a seed wrote a real value.

Held as text, not a number. A staff code that happens to be numeric is formatted
as a number on the way out by the existing `code()` helper; one with letters or
leading zeros survives intact.

### 2. Resolution — one matcher, unchanged in shape

`NotificationOutbox.MatchPerson` is already the single place that decides which
contact a case means. It is already email-first, already fetches two rows to prove
the match is unambiguous, and already returns the contact reference.

Two additions:

- `al_staffcode` joins `emailaddress1` in its `ColumnSet`.
- `PersonMatch` gains `StaffCode`, set only on a match.

Every caller inherits it: the adviser's letter, the paraplanner's letter, the
import's reachability check and the export all continue to get their answer from
the same resolution.

The checker and the fail-accountable person need no matching. Both are held as
contact lookups (`al_assignedcontactid`, `al_fqaccountablecontactid`,
`al_aqaccountablecontactid`), so their code is a plain retrieve.

### 3. The export — six columns

`GenerateExportPlugin` resolves each code at batch-generation time and snapshots it
onto the `al_advisercode` and `al_paraplannercode` columns that `al_exportrecord`
already has. No new export-record columns.

Snapshotted, not read live, for the reason the addresses already are: a later
correction to the registry must not change what an already-delivered batch says it
sent. A code corrected today applies to the next batch, not to last month's.

| Column | Today | After |
| --- | --- | --- |
| B Adviser Code | adviser's email | adviser's staff code |
| D Paraplanner Code | paraplanner's email | paraplanner's staff code |
| L, N FQ Fail Accountability Code | blank whenever a person is named | that person's staff code |
| R, T AQ Fail Accountability Code | blank whenever a person is named | that person's staff code |

`FlaggedText` stops blanking a code column when `namedPerson` is set, and takes
the named person's code alongside their name. `NamedPerson` returns the contact
reference rather than only its `Name`, so the code can be resolved from it.

Where a person has no staff code the column is **blank**, never a fallback to an
email, a name or the case's old value. AD-039 is read by position: column B is the
adviser's code for every row, or the file lies about the rows where it is
something else.

`TRAIL_LIGHT_HEADERS` is unchanged — 20 columns, same order, column 16 still the
intentional blank separator.

### 4. What this deletes

The hand-declared `ExportRecord` intersection type and the
`paraplannerEmailIsGenerated` type-level guard in `trailLight.ts` exist solely to
feed column D an email that the generated model did not yet declare. Column D
becomes a code, so both go, along with the test that asserts the guard.

`al_exportrecord.al_paraplanneremail` and `al_adviseremail` stay as columns. They
are snapshots of what was true when a batch was generated and cost nothing to
keep; they simply stop being read by the file builder.

### 5. The People page

One page, moved, not a second one.

**Route and gate.** `/people` becomes `/admin/people`, gated by
`page.admin.users` — a `ResourceKey` that already exists, is already granted to
Administrators in the bulk grant, and today does nothing but redirect. The
existing `/admin/users` redirect repoints to it.

**Columns** trim to Name, Work email, Role, Employee code.

**Employee code** reads `contact.al_staffcode` via `useUserDirectory`, which gains
`staffCode` on `DirectoryUser`. It is editable by an administrator through the
existing `al_UpdateUser` Custom API, which already enforces `permission.manage`,
already applies optimistic concurrency through `ExpectedRowVersion` and already
writes the Audit Event server-side. It gains a `StaffCode` request parameter.

**Role** writes `al_userrolemapping` through the existing `assignUserRole` and
`setRoleAssignmentActive` commands. No new Custom API. The cell lists every role
the person holds (D8), and the page states plainly that a role grants access —
this is the one control on the page whose effect is not reversible by simply
retyping a value.

**Role filter** offers Adviser, Paraplanner, T&C Manager, Tax Checker, AQS Checker
and Senior Checker. Search already covers name and email and is kept.

**Caseload.** The numeric columns leave the table but stay in the page's export, so
the workload MI is not lost outright.

### 6. The case edit panel

`al_advisercode` and `al_paraplannercode` come off `CaseEditPanel` and off
`UpdateCaseDetailsPlugin.Editables`. The columns remain in the database holding
whatever was typed into them; nothing reads them.

## Non-goals

- **No checker column on Trail Light.** Checker codes are stored and nothing reads
  them yet. This is accepted (D4), not overlooked.
- **No change to the import beyond leaving it alone.** `ParaplannerEmail` and
  `AdviserEmail` stay exactly as they are.
- **No second role concept.** The page edits the roles that already exist.
- **No bulk code load.** Codes are entered by administrators on the People page.
  If a bulk load is wanted later it is a separate piece of work.
- **No re-generation of delivered batches.** Snapshots are immutable by design.

## Risks

| Risk | Mitigation |
| --- | --- |
| **A `StaffCode` parameter added to `al_UpdateUser` is silently discarded.** The Code App drops command parameters `dataSourcesInfo.ts` does not declare, and the command still reports success. | Declare the parameter by hand in `dataSourcesInfo.ts` behind a guard test, rather than waiting for the generator, which lags new Custom API parameters by hours. |
| **Moving People under administration removes it from case-workers**, who see caseload MI there today. | Flagged and accepted. Caseload figures stay in the page export; the `/people/:role/:name` drill-down keeps its `page.cases` gate. |
| **Role editing on People is an access grant.** An administrator tagging someone "Adviser" for reporting also lets them in. | D7's gate, and the page says plainly that a role grants access. |
| **A person with no staff code exports a blank code.** | Intended. A blank is recoverable; a wrong code on a positional file read by an external system is not. The People page makes the gap visible and fixable in one place. |
| **Codes must be loaded before the next batch** or exports regress to blanks where they currently carry emails. | Sequencing note for deployment, not a code change. |

## Testing

- `MatchPerson` returns the staff code on a match and nothing on each of the four
  failure kinds.
- The paraplanner's code resolves by address, and an ambiguous *name* with a good
  address still resolves — the case that proves D3 earns its keep.
- `FlaggedText` emits the named person's code rather than a blank, and still emits
  nothing when that person carries no code.
- `trailLightRow` puts codes in B and D, formats a numeric code as a number and a
  non-numeric one as text, and still emits 20 columns with column 16 blank.
- The deleted guard's test is replaced, not dropped: an assertion that column D is
  the paraplanner's code.
- People page: the role filter, the employee-code edit round-trip, and a
  non-administrator being refused the route.
- A guard test that `dataSourcesInfo.ts` declares `StaffCode`, so the silent-drop
  trap fails loudly if the declaration is ever lost to a regeneration.

## Decision log

One entry superseding AD-183, recording that B and D carry codes again, that the
codes come from `contact.al_staffcode` rather than from the case, and that the
four fail-accountability code columns are populated for the first time. OD-050 is
updated: adviser code and paraplanner code are no longer fields Tax/AQS are
expected to complete.
