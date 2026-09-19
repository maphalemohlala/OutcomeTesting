# Item 10 — Insufficient evidence on a Suitability core check restricts the grade

**Date:** 2026-09-19
**Environment:** `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`) only.
**Branch:** `feat/change-batch-sep-2026`
**Commits:** `21f545f` (the change), `a14f951` (round-trip). Item 3's correction is `7c1633a`.
**Decision:** AD-150

---

## The rule

If **any** Suitability core check is answered **Insufficient evidence**, the advice quality
grade may only be **Insufficient evidence** or **Potential harm**. Pass and Pass with issues
are neither selectable nor saveable.

The grade is never overridden to a value nobody chose. Insufficient evidence versus Potential
harm is a judgement only the checker can make; what changes is that the two grades
contradicting their own ticks stop being available.

## The stable key

The batch asked for the section to be identified "by a stable key, not its display name", and
for the rule to work across checklist versions. That key is **`al_sectioncode`**: the name is
editable through `al_UpdateSection` and the id is per row, while the code carries forward.

`GradingRules.IsSuitabilitySection` matches **`S-E` followed by a digit** — a prefix rather
than a list of the five, so an `S-E6` added through AD-123 belongs to the family without a code
change. This is the same reading `isOutcomeLens` already makes client-side.

**`S-CRP` and `S-CD` deliberately do not match.** Both offer Insufficient evidence on their own
scales, and both are separate blocks on the Checker Checklist. The batch named the core checks
alone, and a test pins each of them out.

## Which way round it runs

This is the part worth reading twice.

| The write | What happens |
|---|---|
| Grade ← Pass / Pass with issues, **while** a core check reads Insufficient | **Refused** (`ResponseGuardPlugin`) |
| Core check ← Insufficient, **while** the grade reads Pass / Pass with issues | Grade is **cleared** and the checker prompted (`ResponseProgressPlugin`) |

The tick wins. The checker is recording what they found on the file, and the finding is not the
thing to argue with — so the grade gives way, never the evidence.

## Where it is enforced

| Surface | File | What it does |
|---|---|---|
| **Server — saveable** | `ResponseGuardPlugin.EnsureGradeAgreesWithSuitability` | Refuses the write, so a hand-made PATCH or a stale tab cannot store a contradiction |
| **Server — clearing** | `ResponseProgressPlugin.ClearGradeOnSuitabilityInsufficient` | Clears a grade that can no longer stand, the moment a core check turns Insufficient |
| **Server — completing** | `SubmitReviewPlugin` | Refuses a review still carrying one |
| **Server — shared read** | `ChecklistQueries.HasSuitabilityInsufficient` | The one query all three use |
| **Portal — render** | `OT Answer Options` (`locked` parameter), `OT Review Detail` | Greys the two values out on arrival, from the stored answers |
| **Portal — live** | `OT Review Detail` script | Restricts as soon as the condition is met; clears an invalid grade and says why |
| **Portal — style** | `outcome-testing.css` | `.opt--locked`, greyed rather than removed |

The submit-time check exists because **deploying a guard cannot reach back and re-guard rows
already saved.** The guard protects everything written from now on; the submit command is what
catches anything written before.

### No TypeScript mirror

Unlike AD-149, this rule has no copy in `app/`. The Code App's review page is a **read-only
document** (OD-007) — grading is a portal write path — so there is no grade control there for a
client-side rule to restrict. Worth stating because the batch asks for rules to hold "on both
surfaces", and here one surface has nothing to hold.

### One honest limitation

The portal's own test for a Suitability section is `scode contains 'S-E'` — a substring, not a
prefix-plus-digit. This site's Liquid has no prefix filter worth relying on (see the recorded
filter limits). The server's rule is exact and is the authority; the two agree on every section
that exists, and would differ only on a hypothetical `S-EXTRA`.

### A test-infrastructure change

`FakeOrganizationService` now projects link-entity `Columns` as `AliasedValue`s. It never did —
it filtered on links but returned nothing from them, which is why the first version of the
shared query silently found nothing. The change is **opt-in**: rows are cloned only where a link
asks for columns, so every existing query gets the stored entity it always got. 1019 tests
passed before and after that change alone.

---

## The audit, as asked

> "Tell me whether any already-completed reviews violate this rule; do not change them, just
> report."

**No review in DEV violates the rule. Nothing was changed.**

The combined query returned nothing, so I proved the query works rather than trust a silent
zero:

| Query | Result |
|---|---|
| Suitability answers reading Insufficient evidence | **4** — `Q-E3-02`, `Q-E3-04` (S-E3), `Q-E4-04` (S-E4), `Q-E1-04` (S-E1) |
| Reviews holding them | **2**, both `Submitted` |
| The grade on each of those two | **Insufficient evidence** on both — compliant |
| Every recorded `Q-GR-01` answer | 5 Submitted reviews graded Insufficient evidence; 1 `Review In Progress` graded Pass |
| That in-progress Pass — does it hold a Suitability Insufficient? | **No.** It is unaffected |

So the rule as deployed changes nothing about existing data. The one Pass on the floor is a
legitimate Pass.

---

## Deployment

| Step | Command | Result |
|---|---|---|
| 1 | `dotnet build -c Release` | clean |
| 2 | `dotnet test` | **1063 passed** (was 1019) |
| 3 | `tsc -b` + `vitest run` | clean; **572 passed** |
| 4 | `pushassembly` | **245,248 bytes**, 2026-09-19 15:02:06Z, v1.0.0.0 |
| 5 | `pac powerpages download` → diff | 69/69 site settings identical; the only template differences were my own edits |
| 6 | `pac powerpages upload --modelVersion Enhanced` | succeeded in 28.21s |
| 7 | re-download → diff | both templates identical to local but for the BOM; `data-ot-suitability`, `suitability_insufficient`, `syncGradeOptions`, `is_locked`, `.opt--locked` all present; **69 site settings intact** |
| 8 | `npm run build` + `npx pa app push` | bundle `index-D-1GY6pk.js` |
| 9 | `round-trip-src.ps1` | mirrors the export |

**A note on step order.** Item 3's round-trip ran *before* `pa app push`, so `src/` kept the
bundle from before it and that push never reached the solution. This round-trip picked both up.
Push the app before round-tripping, not after.

---

## How to test

### The reveal, live (the main path)

1. Open an unsubmitted AQS review on the portal.
2. Go to **Checker judgement and grading**. All four grades are offered.
3. Scroll up to **Suitability core checks** and tick **INSUFFICIENT EVIDENCE** against any test
   point in E1–E5.
4. Back at the grade: **PASS** and **PASS WITH ISSUES** are greyed and unclickable. Insufficient
   evidence and Potential harm remain.
5. Change that core check to **PASS**. Both grades become available again.

### Clear and prompt

6. Grade the review **PASS**. Wait for *Saved hh:mm*.
7. Now tick **INSUFFICIENT EVIDENCE** on a core check.
8. The grade is **unticked**, the two options grey out, and the line beside the grade reads:
   *"Grade cleared — a Suitability core check reads Insufficient evidence, so the grade can only
   be Insufficient evidence or Potential harm."*
9. Confirm it really went: query `al_response` for this review's `Q-GR-01`. `al_answerchoice`
   should be **null**. The page unticking it is not what you are checking.
10. Reload. The grade is still empty and the two options still greyed — the state is decided in
    Liquid, so it survives a reload and needs no script.

### Not saveable (the half a checker cannot see)

11. With a core check reading Insufficient evidence, PATCH the grade to `120910300` directly
    from the browser console. It should be refused with
    `PRECONDITION: A Suitability core check has been answered Insufficient evidence, so the
    advice quality grade can only be Insufficient evidence or Potential harm.`

### The submit gate

12. This one needs data written before the rule. If you have a review already graded Pass with a
    core check reading Insufficient, submit it — refused with the same sentence. (There is none
    in DEV; the audit above says so.)

### Scope — what must *not* trigger it

13. On **Consumer Duty overlay**, tick **INSUFFICIENT EVIDENCE**. The grade must stay fully
    available — it is not a Suitability core check.
14. Same on **Centralised Retirement Proposition**.
15. On a core check, tick **FAIL** rather than Insufficient. The grade stays fully available —
    the batch named Insufficient Evidence, which is a statement about the evidence rather than
    about the advice.

### Item 3, re-checked after its correction

16. On an **ungraded** review the **Primary root cause** block is now **hidden**, not shown.
    That is the correction in `7c1633a`: the batch's wording is "visible and required only when
    the grade is anything other than Pass", and nothing is not anything.
17. Grade **POTENTIAL HARM** → the block appears. Grade **PASS** → it goes, and the recorded
    cause is cleared.

### Tax review

18. Unaffected throughout. It has no Suitability core checks and no grading section.
