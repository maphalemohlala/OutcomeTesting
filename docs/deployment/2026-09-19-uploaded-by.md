# Item 4 — the case owner column reads "Uploaded by"

**Date:** 2026-09-19
**Environment:** `Env_AQ_Dev` only.
**Commits:** `d9141cb` (the change), `10a3d5b` (round-trip)
**Decision:** AD-152

---

## Your question, answered

> "Tell me which views/pages show this column and confirm it actually holds the uploading user
> rather than the Dataverse record owner."

**It is the Dataverse record owner — and it does hold the uploading user, but incidentally
rather than by design.**

`al_outcomecase.ownerid` is set once, when the import creates the row, and **nothing in the
solution ever reassigns it.** I checked every `ownerid` write in the plug-in assembly: they
target `al_reviewinstance` (`AssignCasePlugin`, `ClaimCasePlugin`) or `al_remediationaction`.
Not one targets the case.

So the label is true **by omission**. Three things would quietly make it false, and nothing
would catch any of them:

- assigning a case in a model-driven app,
- a bulk reassign,
- moving the record to an owner team.

A dedicated `al_uploadedby` column stamped at import would be true *by construction*. I did not
build one, because you asked for a label change and the column is correct today — but the
difference is worth a decision rather than an assumption, so it is recorded in AD-152.

## Where it shows — and where it does not

**Renamed** (four places, all the *case's* owner):

| Surface | What changed |
|---|---|
| Code App — case detail summary | `Owner` → **Uploaded by** |
| Code App — worklist column | `Owner` → **Uploaded by** |
| Code App — worklist CSV header | `Owner` → **Uploaded by** |
| Code App — import batch table | `Owner` → **Uploaded by** |
| Dataverse — `al_outcomecase.ownerid` display name | `Owner` → **Uploaded by**, so advanced find and any model-driven view agree |

All four app labels come from one constant, `UPLOADED_BY_LABEL`. Schema name, field names, the
`owner` property and every filter are untouched.

**Deliberately NOT renamed** — these say "Owner" and mean somebody else entirely:

| Surface | Whose owner it really is |
|---|---|
| Case detail → reviews table | the **review's** owner, i.e. the checker |
| Review detail → summary | the **review's** owner, i.e. the checker |
| Case edit panel → "held by" | the **review's** owner |
| Remediation page + `OT Case Detail` + `OT Remediation` | the **action's** owner, i.e. the adviser |

Renaming those would have swapped a vague label for a false one.

**Also unchanged, and this one is a judgement call you may want to overrule:** the people
directory's `Owner` **role**. Two reasons — the role is a URL segment (`/people/:role/:name`),
so renaming the key breaks existing links; and it is read in sentences — *"recorded as the
owner"* — where "Uploaded by" is not grammatical. If you want it changed, **"Uploader"** is the
word that fits, and it needs a key/label split so the URLs keep working. Say the word.

**The portal needs no change at all.** It never reads `ownerid` on a case — the only mention is
a comment recording that a remediation cell used to, and stopped on 2026-09-13.

## One side effect, found and trimmed

Setting `Description` on `ownerid` also rewrites the **`Owner` relationship's** description,
which was `"Owner Id"`. My first pass put a whole paragraph there. It is now one sentence
pointing at AD-152, and the full explanation lives on the attribute where it belongs. Visible
in `src/Other/Relationships/Owner.xml`.

## Verification

| Step | Result |
|---|---|
| `setcolumnlabel` | `'Owner' -> 'Uploaded by'`, read back by the verb |
| `dotnet test` | **1075 passed** |
| `tsc -b` + `vitest run` | clean; **589 passed** (6 new) |
| `pa app push` | bundle `index-Cq4y9XqJ.js` |
| `round-trip-src.ps1` | `<displayname description="Uploaded by" …>` present in `al_OutcomeCase/Entity.xml`, so it **promotes** |

New `caseExport.test.ts` pins the CSV header — the one place the app's column names leave the
app. It covers this rename and item 2's, and asserts the header and the row stay the same
width, because a column added on one side and not the other silently shifts everything after
the break.

---

## How to test

1. Code App → **Cases**. The column header reads **Uploaded by**, and the value is whoever ran
   the import.
2. Open a case. The summary field reads **Uploaded by**.
3. On that same case, scroll to the **reviews** table. Its `Owner` column is **unchanged** —
   that is the checker, and it should still say Owner.
4. Export the worklist to CSV. The header reads **Uploaded by**, not Owner.
5. Code App → **Case intake**. The batch table reads **Uploaded by**.
6. In a model-driven app or advanced find on Outcome Case, the field picker shows **Uploaded
   by**.
7. Portal: nothing to check. It does not show this column.

### The one that proves the caveat

8. Assign a case to a different user from a model-driven app. The column will now read that
   person, under a label saying they uploaded it — which they did not. That is the fragility
   AD-152 records, and the reason a stamped column would be worth doing if cases are ever
   reassigned in practice.
