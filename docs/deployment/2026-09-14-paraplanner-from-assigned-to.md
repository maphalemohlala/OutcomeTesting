# The paraplanner is AssignedTo, and no checker is imported — DEV deployment, 2026-09-14

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.**

Reported by the project owner, 2026-09-14: "on the new import, the paraplanner is the Assigned
to and not Assigned by column", then "the checker will be set manually".

---

## What was wrong

`ImportRules.Columns` and the Code App's `caseUpload.ts` both mapped:

| Extract | Attribute |
|---|---|
| `AssignedTo` | `al_checkername` |
| `AssignedBy` | `al_paraplanner` |

Both halves were wrong, and they were wrong because of the sample file.

`data/io-task-extract-sample.csv` is **synthetic**: its two names are literally `Paraplanner 1`
and `Checker 4`. The 2026-09-12 design read the mapping off those placeholder names, and the
assumption went into two implementations and a spec.

The real 2026-09-13 DEV import shows it plainly:

| Case | `al_checkername` (←AssignedTo) | `al_paraplanner` (←AssignedBy) |
|---|---|---|
| 254471891 | Matheau Frith | Jessica Bell |
| 254471517 | Matheau Frith | Jessica Bell |

Matheau Frith is the paraplanner, sitting in the column named for the checker.

## What confirms it, beyond the report

The extract's own structure agrees, which is what raises this above one person's say-so:

- The **checklist stamp equals `AssignedTo`** on every sample row that carries one, and the
  design's own §5 says *paraplanners select the reasons for the check inside the IO task*. The
  person stamping the checklist is therefore the paraplanner — so `AssignedTo` is.
- On 254471517 and 254471891, `AssignedTo`, `CreatedBy` and `CompletedBy` are **all one
  person**. A task raised, worked and assigned-to by the same name is the paraplanner's, not a
  checker's.

## What changed

| Piece | Change |
|---|---|
| `ImportRules.Columns` | `AssignedTo -> al_paraplanner`. `AssignedBy` no longer mapped. `al_checkername` **removed from the table entirely**. |
| `caseUpload.ts` `CSV_COLUMNS` | The same, on the preview side. |
| `ImportRulesTests` | `Takes_the_paraplanner_from_assigned_to`, `Does_not_import_a_checker_name`. Fixture's AssignedTo renamed `Pat Paraplanner`. |
| `caseUpload.test.ts` | The same two, mirrored. |
| `2026-09-12-io-task-extract-import-design.md` | §4 mapping corrected; the two paragraphs that asserted the old reading struck through rather than deleted, and the `al_checklistcompletedby` conclusion in §5 corrected — it **is** normally the paraplanner. |
| `knowledge/decision-log.md` | AD-134. |

**`al_checkername` is absent rather than remapped.** Only columns listed in the table are
written, so absence is what guarantees a re-import of the same TaskID cannot overwrite whoever
has since been allocated. Mapping it to any other column would reintroduce AD-113's complaint —
a name the file carried, standing in for an allocation it never proved. The checker is set by
`AssignCasePlugin` (allocation), `ClaimCasePlugin` (claim), or the case edit's "Checker" field.

## The sample file was deliberately not rewritten

It was first swapped so `AssignedTo` held `Paraplanner 1`, and then **reverted**. The swap was
wrong: it broke the file's internal consistency, because the checklist stamps match the
*original* `AssignedTo`. The column positions and the relationships between them are faithful
to the real extract; only the invented names mislead. A note in the design doc says to read the
positions, not the names.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins), before the fix | **2 failed** — the two new tests, as intended |
| 2 | `dotnet test` (plugins), after | **817 passed** |
| 3 | `npx vitest run` (app) | **485 passed**, 37 files |
| 4 | `npx tsc -b` | clean (`--noEmit` checks nothing in this project) |
| 5 | `dotnet build -c Release --no-incremental` | **222,720 bytes** |
| 6 | `pushassembly` | 222,720 bytes, 2026-09-14 09:44:40Z |

The Release build came out **byte-identical** to the previous one, which is the stale-`bin/Release`
trap this repo has hit before. Resolved rather than assumed: a clean `--no-incremental` rebuild
produced the same 222,720 bytes with a current timestamp, so the size is coincidence.

## Not done here

- **The Code App is not redeployed.** `caseUpload.ts` is the *preview* side; the server writes
  the data and is deployed. Until the app ships, the preview will still show a Checker column
  that the import no longer writes.
- **`al_checkername`'s schema description is now wrong.** It reads "…which is the task's
  AssignedTo rather than the paraplanner who raised it." That is Dataverse metadata, so
  correcting it is a schema write and was not made.
- Nothing committed.
