# IO extract schema, and the import half released — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

This closes the hold `2026-09-12-paper-case-detail-and-section-shape.md` placed on the whole
import half of `9f4e968`, and the refusal to copy back recorded in
`2026-09-13-tax-outcome-wording.md`. Both had the same single cause.

---

## The cause

Commit `9f4e968` authored **sixteen columns into `src/` that were never created in DEV**.
`src/Entities/al_OutcomeCase/Entity.xml` carried 58 attributes; the exported solution carried
42. That one fact blocked two things at once:

- **The assembly could not ship.** `ImportRules` writes nineteen columns, sixteen of which did
  not exist. A create naming an absent attribute fails the row outright, so every imported
  case would have failed.
- **The AD-013 copy-back could not run.** Copying a 42-attribute export over a 58-attribute
  `src/` would have deleted 696 lines of column definitions the import rebuild depends on.

The columns were authored by hand rather than round-tripped, which is precisely the drift
AD-013 exists to end.

## The tooling gap underneath it

The registration tool could mint a memo, a choice, a date and a bool — but **not an
nvarchar**. The only `StringAttributeMetadata` it built was a private `Str()` helper nested
inside `createnotificationtable`, reachable by that one table. Nine of the sixteen are text,
so they could not be created by command at all.

`adddatecolumn` also hardcoded `DateOnly` for both `Format` and `DateTimeBehavior`.
`al_ChecklistCompletedDate` is `Behavior 3` — **TimeZoneIndependent**, because as its own
description says the IO extract carries no offset, so there is no local time to convert from.
`DateTimeBehavior` is immutable once the column exists: creating it DateOnly would have been
fixable only by dropping and recreating the column, taking any data with it.

Both were closed before any column was created:

| Verb | Change |
|---|---|
| `addtextcolumn` | **New.** `StringAttributeMetadata`, `FormatName.Text`, max 1–4000 with a pointer to `addmemocolumn` beyond that |
| `adddatecolumn` | `--behaviour dateonly\|timezoneindependent\|userlocal`, defaulting to `dateonly` so every existing call is unchanged |

Guards exercised locally before pointing either at DEV: no `--confirm` → usage; `maxLength
5000` → refused; unknown behaviour → refused.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `addtextcolumn` × 9 | all created in solution `OutcomeTesting` |
| 2 | `adddatecolumn` × 3 (default) | behaviour read back **DateOnly** |
| 3 | `adddatecolumn` × 1 `--behaviour timezoneindependent` | behaviour read back **TimeZoneIndependent** |
| 4 | `addmemocolumn al_ChecklistItems 2000` | created, max 2000 |
| 5 | `addchoicecolumn al_IoTaskStatus` | 3 options: 120910620–622 |
| 6 | `addchoicecolumn al_IoOutcome` | 4 options: 120910610–613 |
| 7 | `fetch` probe of six columns across all four types | **returned a row** — the same query returned `0x80041103` before |
| 8 | `setattributedescription al_taxoutcome` | updated and published |
| 9 | `pac solution export` → `unpack` | **1,141,289 bytes**; export now carries **58** attributes, was 42 |
| 10 | **AD-013 copy-back** of `Entities/al_OutcomeCase/` | done, CRLF → LF; only `Entity.xml` differs |
| 11 | `dotnet build -c Release` → `pushassembly` | **214,528 bytes**, matching the local build exactly |
| 12 | `npm run build` → `npx pa app push` | built clean (539.85 kB bundle), **pushed successfully** |

Every column was created through `CreateAttributeRequest` with `SolutionUniqueName`, so
solution membership came with the create rather than needing a separate
`addmetadatatosolution` pass. That is what the original gap lacked, and why it could recur
silently: a column can exist on the table and still not be a component of the solution.

The sixteen: `al_ServiceCaseRef`, `al_ClientRef`, `al_AdviserEmail`, `al_IoCompletedBy`,
`al_IoCreatedBy`, `al_TaskType`, `al_WorkflowName`, `al_ServiceStatus`,
`al_ChecklistCompletedBy` (text); `al_IoCompletedDate`, `al_TaskStartDate`,
`al_IoCreatedDate` (DateOnly), `al_ChecklistCompletedDate` (TimeZoneIndependent);
`al_ChecklistItems` (memo 2000); `al_IoTaskStatus`, `al_IoOutcome` (choice).

---

## What the copy-back changed, and what it did not

Compared **block by block** after normalising line endings, not by line diff — the export is
CRLF and `src/` is LF, which on the first attempt made all 2,855 lines read as changed and
hid the missing sixteen entirely.

Attribute sets match exactly: 58 in `src/`, 58 in the export, neither direction carrying an
attribute the other lacks. Three differences in content, all resolved in DEV's favour under
AD-013:

| | `src/` had | DEV has | Resolution |
|---|---|---|---|
| `IsSearchable` / `IsFilterable` / `IsRetrievable` on the sixteen | `1` | `0` | DEV wins. Non-functional here — `ValidForCreate/Update/Read` are all `1`. The `1`s were hand-authored aspiration that never existed in any environment |
| Option-set names | `al_outcomecase_iooutcome` | `al_outcomecase_al_iooutcome` | DEV wins. The verb derives `{entity}_{logical}`; nothing in the repo references either name (checked) |
| `al_TaxOutcome` description | the AD-055 amendment prose | the original | **Pushed to DEV first** (step 8) so the round trip preserved it rather than discarding it |

`al_ChecklistCompletedDate` survived the round trip as `Behavior 3`, and
`al_taxoutcome` 120910302 still reads `Pass with issues`.

---

## Consequences for the tax wording change

`2026-09-13-tax-outcome-wording.md` recorded that the remediation action's `"Tax check: …"`
reason would keep reading the old wording until the assembly shipped. **It has now shipped**,
so that surface is current too: `SubmitReviewPlugin` reads the label from `al_taxoutcome`,
which reads `Pass with issues`. Nothing in that note needs undoing — its "held back" section
describes a state this deployment ended, hours later the same day.

---

## Still open

- **The reference document.** `docs/reference/checker-checklist.html` still reads INSUFFICIENT
  EVIDENCE and is left as supplied; `checklistDocument.test.ts` carries the divergence as its
  third deliberate difference.
- **`ResponseRules.IsNonPass` does not admit `ChoicePassWithIssues`.** Latent, untouched by
  either change, and worth closing before anything puts 120910303 on a pass/fail scale.
- **The import path is now deployable but not yet exercised end to end.** Schema, assembly and
  Code App are aligned for the first time; an actual IO extract upload against DEV is the
  proof, and has not been run here.
