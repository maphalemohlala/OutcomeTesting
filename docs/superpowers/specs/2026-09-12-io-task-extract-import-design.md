# IO task extract import — design

Date: 2026-09-12
Status: approved for build (project owner, 2026-09-12)

Replaces the 18-column CSV case template with Intelligent Office's native
**Pre-Advice Check task extract**, supplied as `.xlsx`. Sample:
`Pre-Advice Check Required Task 04-09.xlsx`, one sheet `Tasks (12)`, 85 columns,
7 data rows.

## 1. What changes and why

The current template was derived from `knowledge/checklist-v8.md` and never
existed in IO. BR-001 has always said *"Intelligent Office **Excel** extracts"*;
the CSV template was a stand-in. The real extract is a task report, so three
things move at once: the file format, the column set, and the row grain.

Of the 18 columns the importer reads today, **five** survive in some form
(client name, adviser name, a checker-ish name, a due date, a reference) and
**thirteen** have no counterpart at all.

## 2. Decisions

| # | Decision | Source |
|---|---|---|
| D1 | The IO task extract **replaces** the CSV template entirely. No dual format. | Project owner, 2026-09-11 |
| D2 | **One row = one case**, keyed on `TaskID`. A service case carrying two tasks becomes two cases. | Project owner, 2026-09-11 |
| D3 | `Outcome`, `CompletedBy`, `CompletedDate` are **stored as reference**, never as the app's grade. BR-005 grading stays with the checker. | Project owner, 2026-09-11 |
| D4 | The repeated `CompletedBy`/`CompletionDate` collapse to **one stamp per case**. Item **names are kept in full** — they are the routing input. | Project owner, 2026-09-11, refined 2026-09-12 |
| D5 | The browser converts `.xlsx` to a **faithful CSV with IO's own headers** and posts it to the unchanged `al_ImportCases`. The server keeps all mapping and validation. | Approach 1, approved 2026-09-12 |
| D6 | Route is derived from the checklist items by setting `al_taxcheckrequired`. The existing `DeriveRoute` engine is **not touched**. | Client, 2026-09-11 |
| D7 | Checklist items are matched by **name**, and only count when they carry a completion stamp. | This design, §5 |
| D8 | Client address, postcode, phone and email are **not imported** pending confirmation that the checking process needs them. | Recommendation, 2026-09-12 |
| D9 | The orphaned columns are **kept** on the entity and completed by hand. | Client, 2026-09-12 |
| D10 | Tax and AQS reviewers get `page.cases` **Edit**, because they are the ones who complete those fields. | Project owner, 2026-09-12 |

## 3. Why the server keeps the mapping (D5)

`al_ImportCases` takes a `Csv` string deliberately — *"sent as text rather than
as parsed rows so BR-002 validation is applied server-side and cannot be
bypassed"* (AD-003, AD-077). An `.xlsx` is a zip, not text, so something had to
give.

The browser's job is narrowed to **transcription**: unzip, read cells, resolve
Excel date serials to ISO using the cell styles — the only place that
information exists — and emit CSV under IO's own headers. It invents no
mapping and applies no rule. `ImportRules` still owns every column mapping,
every derivation and every rejection, so the boundary AD-003 drew is intact.

Rejected alternatives: parsing the workbook in the plug-in (a new request
parameter, a registration redeploy, and `System.IO.Compression` inside the
Dataverse sandbox is unverified); a staging table (largest change, and ~30 of
the 85 columns are empty on every row).

## 4. Column mapping

Key: `TaskID` → `al_casereference` **and** `al_name`.

**Mapped to existing columns**

| Extract | Attribute |
|---|---|
| `Client` | `al_clientname` |
| `AdviserName` | `al_advisername` |
| `AssignedTo` | `al_checkername` |
| `AssignedBy` | `al_paraplanner` |
| `DueDate` | `al_duedate` |

`al_checkername` continues to mean "the name the file carried", not "the
allocated checker" — AD-113 already established that, and `al_caseassignment`
stays the source of truth for allocation.

`AssignedBy` is the paraplanner who raised the pre-advice check task (project
owner, 2026-09-12), which is why it feeds `al_paraplanner` rather than a column
of its own — it is one of the header fields the client asked be pre-populated
from IO rather than typed in. Note that the checklist stamp is `AssignedTo`, not
`AssignedBy`, on every sample row that carries one, so `al_checklistcompletedby`
is **not** the paraplanner.

**New columns on `al_outcomecase`**

| Extract | Attribute | Type |
|---|---|---|
| `ServiceCaseSequentialRef` | `al_servicecaseref` | text |
| `ClientRef` | `al_clientref` | text |
| `AdviserEmail` | `al_adviseremail` | text |
| `Status` | `al_iotaskstatus` | choice |
| `Outcome` | `al_iooutcome` | choice |
| `CompletedBy` | `al_iocompletedby` | text |
| `CompletedDate` | `al_iocompleteddate` | date |
| `StartDate` | `al_taskstartdate` | date |
| `CreatedDate` | `al_iocreateddate` | date |
| `CreatedBy` | `al_iocreatedby` | text |
| `TaskType` | `al_tasktype` | text |
| `WorkflowName` | `al_workflowname` | text |
| `ServiceStatus` | `al_servicestatus` | text |
| `ChecklistItem1..10` | `al_checklistitems` | memo, one per line |
| `CompletedBy1..10` | `al_checklistcompletedby` | text |
| `CompletionDate1..10` | `al_checklistcompleteddate` | datetime |

`al_iooutcome` uses its own option values and is deliberately distinct from
`al_outcome` (D3).

**Derived**

| Attribute | Rule |
|---|---|
| `al_taxcheckrequired` | Yes when a Tax item is selected; No when items are selected but none is a Tax item |
| `al_preorpostcheck` | `Pre` when `TaskType` is "Pre-Advice Check Required" |
| `al_casestatus` | `Imported` (unchanged, BR-001) |

**Not imported**: `ClientAddress`, `Postcode`, `ContactNumber`, `ClientEmail`
(D8). **Dropped**: `ActivityType`, `LegalEntity`, `Group1`–`Group5`,
`TaskCategory`, `Subject`, `PercentComplete`, the four duration columns,
`VisibleToClient`, `MigrationRef`, `SecondaryReference`, `JointClient`,
`RelatedOpportunity`, `Notes`, the three `Campaign*`, `JointCRMContactId`, the
four `RelatedPlan*`, `SellingAdviser`, `BillingRatePerHour`, `AppointmentID`,
`Priority` — each is constant or empty on every row of the sample.

## 5. Checklist reading (D7)

Paraplanners **select** the reasons for the check inside the IO task; the
extract reports the selected items across `ChecklistItem1..10` with a
`CompletedBy`/`CompletionDate` beside each.

We cannot yet tell from the data whether a partial selection **packs** into the
first free slots or stays in a **fixed** slot with the others blank — every
completed row in the sample has all seven selected. So the reader is written to
be correct either way:

> An item counts as selected when it has a **name we recognise** and a
> **completion stamp**. Its column number is never consulted.

Under packing, every item present is stamped and counted. Under fixed slots, an
unselected item carries no stamp and is skipped. The outstanding question with
the client therefore confirms the reader rather than shaping it.

**Item names.** The client named the items as "Tax" and "Trust documentation";
the file says `Tax Check` and `Trust Documentation Check`. Both forms are
accepted as the same item, matched case-insensitively after trimming. Five
others match exactly.

| Item (and accepted aliases) | Discipline |
|---|---|
| `Tax Check`, `Tax` | Tax |
| `Trust Documentation Check`, `Trust documentation` | Tax |
| `High Risk Item 1` | AQS |
| `High Risk Item 2` | AQS |
| `Enhanced Supervision` | AQS |
| `Pre-CAS Adviser` | AQS |
| `Leaver` | AQS |

**The stamp.** All selected items in a row carry the same `CompletedBy` and
`CompletionDate` — the checklist is completed in one action — so one stamp is
kept per case (D4). If they ever disagree, the earliest completion wins and the
row is still imported; the item list is what matters.

The stamp names the task's `AssignedTo`, which is not the paraplanner who raised
it (`AssignedBy`), so `al_checklistcompletedby` is recorded as what it is —
whoever worked the checklist — and is not conflated with `al_paraplanner`.

## 6. Routing (D6)

`DeriveRoute` in `UpdateCaseDetailsPlugin` is unchanged. It already implements
exactly what the client described:

| `al_taxcheckrequired` | Disposition | Route |
|---|---|---|
| Yes | — | `ROUTE-TAX-AQS`, Tax first |
| No | — | `ROUTE-AQS` |
| — | Return to paraplanner | `ROUTE-TAX` |

So the import sets `al_taxcheckrequired` and nothing else:

- **any Tax item selected** → `Yes` → Tax-then-AQS. The Tax team then decides
  whether AQS is genuinely owed, which is the existing disposition override —
  "Return to paraplanner" collapses the case to Tax-only.
- **only AQS items selected** → `No` → AQS-only.

A case with Tax items and no AQS items still starts as Tax-then-AQS. That is
correct: the client's rule is that Tax is always the starting point and Tax
decides what follows.

## 7. Rejections (BR-002)

Two new row-level rejections, both carrying a reason a person can act on:

| Condition | Reason |
|---|---|
| No checklist item carries a stamp | *"No checklist items are selected, so the review route cannot be determined."* |
| A stamped item's name is not recognised | *"Checklist item \<name\> is not recognised, so the review route cannot be determined."* |

Both are deliberate. A case with no derivable route would otherwise be created
at `Imported` with no route and sit there silently — `DeriveRoute` returns early
on a null tax answer and writes nothing. Failing the row puts it in front of a
person instead, through the `al_importexception` path that already exists.

Failing loudly on an unknown name is what protects §5's open question: if IO's
wording differs from this sample, routing changes silently otherwise.

Existing rejections are unchanged except for their key: a missing or duplicate
`TaskID` replaces a missing or duplicate IO reference.

## 8. Testing

- `ImportRulesTests` — rewritten against the new headers: key, duplicates,
  each route derivation, both new rejections, the stamp rule under packed and
  fixed-slot layouts, alias matching.
- `caseUpload.test.ts` — the same rules on the preview side, plus the xlsx
  reader: shared strings, inline strings, date serials, fractional datetimes,
  missing cells.
- Fixture: `data/io-task-extract-sample.csv`, transcribed from the supplied
  workbook, with the client's personal columns removed (D8).

## 8a. Who fills in the rest

Eleven case-header columns have no counterpart in the extract: adviser code,
adviser status, paraplanner code, products, case type, advice date, product /
solution type, sample source, check date, vulnerable client, tax team
disposition. The client's direction is that these "are on the form header and
are all needed and will need to be manually completed by Tax/AQS -- this was
always the intention (aside from what could be pre-populated from IO such as
Adviser name/Paraplanner)".

They are already editable on `CaseEditPanel` and written through
`al_UpdateCaseDetails`, so nothing is built for them. What did have to change is
access: both reviewer roles held `page.cases` at View, which renders the panel
read-only and makes the command refuse the write (D10).

`al_taxcheckrequired` stays editable alongside its derivation. The import sets
it from the checklist; a checker correcting it re-runs `DeriveRoute` through the
normal edit path, which is the same mechanism the Tax team's disposition already
uses.

**`DEFAULT_PERMISSIONS` is a seed, not a migration.** Stored rules replace it
wholesale rather than overlaying it, so an environment that already holds
permission rows needs the two Edit rules added through the Security
configuration screen; the seed only governs an environment with none.

## 9. Out of scope

- **Remediation ownership.** The client says AQS confirm both teams' remedial
  actions; AD-114 has them separate, with Tax's signed off before AQS starts.
  A real difference, but it belongs to the review flow, not the import.
- **Renaming the extract's columns.** Cosmetic; internal names are ours.
- **Update-on-resend.** A repeated task stays idempotent (ignored), as now.
- **Post-advice checks**, whether completed rows should be sent at all, and
  adviser code — all open with the client, none blocking.
