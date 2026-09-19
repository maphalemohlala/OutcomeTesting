# Item 2 — the case header carries a Tax Checker and an AQS Checker

**Date:** 2026-09-19
**Environment:** `Env_AQ_Dev` only.
**Branch:** `feat/change-batch-sep-2026`
**Commits:** `9536b9e` (the change), `3943f44` (round-trip)
**Decision:** AD-151

---

## The defect

A case taking both a Tax check and an AQS check has **two** checkers. The header had **one**
column, `al_checkername`, and both allocation paths wrote it — so whichever discipline was
allocated second overwrote the first. The header named one checker, gave no hint it had ever
named the other, and a worklist search found the case under one person and not the other.

The write side was **already** per-discipline: `CaseEditPanel` allocates per Tax/AQS through
`al_AssignCase`. Only the stamp collapsed them.

## What changed

### Schema — two columns, both in the solution

| Column | Display name | Type |
|---|---|---|
| `al_outcomecase.al_taxcheckername` | **Tax Checker** | text, 100 |
| `al_outcomecase.al_aqscheckername` | **AQS Checker** | text, 100 |

Added with the registration tool's `addtextcolumn`, which puts them in the `OutcomeTesting`
solution — confirmed in the round-trip, so they **will** promote to TEST and PROD. (That check
is deliberate: two components existed only in DEV earlier today and would not have promoted.)

### Server

`CheckerNames.AttributeFor` maps a review type to its column.
`AssignCasePlugin.StampCheckerName` now takes the review's discipline and writes only that
column; `ClaimCasePlugin` calls the same method. A review with no recognised discipline writes
nothing — there is no column that would be right, and putting the name in either would
attribute a check to someone who did not make it.

**Neither column is editable.** `al_checkername` is out of `UpdateCaseDetailsPlugin`'s editable
fields and out of `CaseHeaderRequestPlugin`'s portal allowlist, both pinned by a test. Each
column now *reflects* an allocation, so free text would be a second source of truth for the
same fact — and typing a name into the old field had already been mistaken for allocating the
check once (2026-09-09).

### Three empty states, not one

An absent name means opposite things, and the batch asked for a sensible empty state:

| State | Reads as | When |
|---|---|---|
| `named` | the name | somebody is allocated |
| `unallocated` | **Not yet allocated** | the case takes this check and nobody holds it |
| `not-required` | **No check of this type** | the case takes no such check |

The Code App reads the **route** to tell the last two apart, treating an unknown route as
requiring both — so an unallocated check reads as work outstanding rather than work nobody
owes. `OT Case Detail` reads the **review instances** instead, which is the stronger fact: a
review that exists beats a route that says one is owed. `OT Review Detail` has neither to hand
and shows only *Not yet allocated*.

### `al_checkername` is kept and left dormant

Not dropped. What it holds is the last allocation made under the old rule, which is evidence
about how a case was handled. **Dropping the column is your call**, once the two have run long
enough to be trusted. Nothing writes it, reads it or offers it for editing now.

---

## Everything else that used the single value — as asked

| Where | What happened |
|---|---|
| Shared case header (`caseHeaderFields`) | One field became two. The header now draws **19** fields where the reference Checker Checklist draws 18 — the **fourth** deliberate difference `checklistDocument.test.ts` carries |
| App worklist row | `checker` became `taxChecker` + `aqsChecker` |
| App worklist **search** | Covers both names, so a search finds the case whichever discipline that person holds |
| App **CSV export** | `Checker name` became `Tax Checker` + `AQS Checker` — **a column count change for anyone consuming that file** |
| **People directory** | Positions are now de-duplicated by name. Without that, a case would count **twice** against someone holding both its checks. The role stays `Checker` rather than splitting, because the directory answers "what has this person got on" and that is one workload |
| `OT Case Detail` | Both the summary row and the header row, each showing the two with their empty states |
| `OT Review Detail` | The editable **text box is gone** — read-only on both the editable and the submitted branch |
| `OT Review List` | Comment only; the stale reason it gave was rewritten. The queue still shows no checker column, now because the column for the discipline being claimed is empty by definition |
| **Trail Light export** | **Not affected.** It carries no checker column — verified against `GenerateExportPlugin`'s 20 columns |
| Import | **Not affected.** `al_checkername` stopped being mapped on 2026-09-14 |

---

## The data migration

New verb `backfillcheckernames`, dry-run by default, `--confirm` to write. It reads each active
review instance's assignment — preferring the assigned **contact** (what a portal claim stamps
and what a checker sees themselves called), falling back to the owning user — and writes the
discipline's column on the case.

DEV result: **17 active reviews, 0 already stamped, 0 skipped, 12 cases written.** Five of
those carry both disciplines — `IO-300006`, `IO-300005`, `IO-SEED-TXA-01`, `254398988`,
`254471517` — which are exactly the cases the single column had been collapsing.
**`al_checkername` was left untouched.**

---

## Deployment

| Step | Command | Result |
|---|---|---|
| 1 | `addtextcolumn` ×2 | both created in solution `OutcomeTesting` |
| 2 | `pa app add data-source --table al_outcomecase` | model regenerated; diff was **exactly** the two columns |
| 3 | `dotnet build -c Release` + `dotnet test` | clean; **1075 passed** |
| 4 | `tsc -b` + `vitest run` | clean; **583 passed** |
| 5 | `backfillcheckernames` (dry run, then `--confirm`) | 12 cases |
| 6 | `pushassembly` | **245,760 bytes**, 2026-09-19 15:24:49Z |
| 7 | `npm run build` + `pa app push` | bundle `index-CUh90Ukg.js` |
| 8 | `pac powerpages download` → diff | 69/69 settings identical; DEV-only lines were exactly the ones I replaced |
| 9 | `pac powerpages upload --modelVersion Enhanced` | 32.13s |
| 10 | re-download → diff | all three templates identical but for the BOM; 69 settings intact |
| 11 | `round-trip-src.ps1` | picks up `al_OutcomeCase/Entity.xml` with both columns |

App pushed **before** the round-trip this time, so the bundle reaches `src/`.

---

## How to test

### The defect itself

1. Find a case with **both** a Tax and an AQS review — `IO-300006`, `IO-300005`,
   `IO-SEED-TXA-01`, `254398988` or `254471517`.
2. In the Code App, allocate the **Tax** check to one person and the **AQS** check to a
   *different* person.
3. Open the case. **Tax Checker** and **AQS Checker** each name their own person. Before this
   change, both rows were one row naming whoever was allocated second.
4. Re-allocate only the AQS check to a third person. The Tax Checker is **unchanged**.

### Empty states

5. Open a **Tax only** case. Tax Checker names someone (or *Not yet allocated*); AQS Checker
   reads **No check of this type** — not a dash, and not the same as unallocated.
6. Open a case in the queue that nobody has claimed. Both read **Not yet allocated**.

### Not editable — the half a user cannot see

7. On the portal review page, the header's Checker row is now **text, not an input**.
8. From the browser console, PATCH `al_caseheaderrequest` with `al_checkername`. Refused,
   naming the field.
9. In the Code App, call `al_UpdateCaseDetails` with `al_checkername`. Refused the same way.

### The surfaces that follow it

10. Worklist: search a checker's name. The case appears whichever discipline that person holds.
11. Export the worklist to CSV. Two columns, **Tax Checker** and **AQS Checker**.
12. People directory: a person holding **both** checks on one case shows that case **once**,
    not twice. This is the de-duplication; it is worth checking on `IO-SEED-TXA-01` in DEV,
    where both checks are held by the same account.

### Portal claim

13. Claim an AQS review from the portal queue. The case's **AQS Checker** becomes you; the Tax
    Checker is untouched.
