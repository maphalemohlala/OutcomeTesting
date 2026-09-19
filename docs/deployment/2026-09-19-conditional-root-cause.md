# Item 3 — the primary root cause is asked for only when the file did not pass

**Date:** 2026-09-19
**Environment:** `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`) only. Promotion to TEST/PROD is a separate decision.
**Branch:** `feat/change-batch-sep-2026`
**Commits:** `f1f9755` (the change), `8dd3240` (round-trip)
**Decision:** AD-149

---

## What was wrong

`Q-GR-02` — *Primary root cause* — is seeded **mandatory**, and `SubmitReviewPlugin`
enforces mandatory answers. So every review graded **Pass** was held at submission by a
question with no true answer. The checker had to pick one of the nine causes to get past the
gate, and that invented cause then went out in the trend MI the question exists to feed
(AD-022).

The seed, for the record:

```
Q-GR-01   order=1   rt=120910010   mandatory=True    Advice Quality Grade
Q-GR-02   order=2   rt=120910003   mandatory=True    Primary root cause
Q-GR-03   order=3   rt=120910001   mandatory=False   Case Notes
Q-GR-04   order=4   rt=120910001   mandatory=False   Even Better If...
```

## The rule

`GradingRules` (new, `plugins/OutcomeTesting.Plugins/GradingRules.cs`) holds two predicates
that are **deliberately not each other's negation**:

| | Pass | Any other grade | Grade outside the scale | Ungraded |
|---|---|---|---|---|
| `RootCauseRequired` | no | **yes** | **yes** | no |
| `RootCauseCleared` | **yes** | no | no | no |

The ungraded column is the reason they are two rules. An ungraded review owes no root cause —
the grade is mandatory in its own right, so the review is refused anyway, and naming Q-GR-02
in that refusal would assert something not yet knowable. It also destroys nothing: the grade
and the cause sit one above the other and nothing stops a checker picking the cause first.

## Where it is enforced

Every rule is server-side first, per the batch's working rule, and mirrored in both front ends.

| Surface | File | What it does |
|---|---|---|
| **Server — gate** | `SubmitReviewPlugin.RemoveRootCauseWhenPassing` | Drops Q-GR-02 from the questions a submission owes when the grade does not require one |
| **Server — clearing** | `ResponseProgressPlugin.ClearRootCauseOnPass` | Post-operation on `al_response`: a grade of Pass clears any cause already recorded |
| **Server — guard** | `ChecklistGuards` | Q-GR-02 joins the protected list (8 → **9**); it can no longer be retired or moved |
| **Code App** | `app/src/features/reviews/gradingRules.ts` → `formBlocks` | Leaves the row out of the document when the grade is a Pass |
| **Portal** | `OT Review Detail` (Liquid + script), `outcome-testing.css` | Hides the 3×3 on the stored grade at render, and keeps up with changes live |

### Three choices worth recording

**The gate, not `al_ismandatory`.** Unsetting the flag would have been a one-row data change
and the wrong one: it is per question version, so it excuses Q-GR-02 on *every* review at
once — including the non-pass reviews the root cause actually exists for. Q-GR-02 stays
mandatory and the gate decides who owes it.

**`ResponseProgressPlugin`, not `AnswerWriter`.** `AnswerWriter` is the portal's path only; a
direct Dataverse write never touches it. The post-operation step on `al_response` sees the
write whatever made it. It rides the six `al_response` steps already registered rather than
adding two more — the same choice `StampCheckDate` made this morning. **No new step
registration was needed, so nothing had to be re-activated after import.**

**Clearing, not hiding.** A hidden value is still a stored value: it would export as a live
root cause against a passing case, and read to anyone querying `al_response` as a
contradiction nobody could account for.

### The cheap-then-exact guard

`Pass` (`120910300`) is also the suitability grid's Pass, so most ticks on a checklist reach
the first test. The **response type** (`120910010`, which belongs to Q-GR-01 alone under
AD-055) stops them there. Only then is the **question code** read, because a response type is
a convention checklist administration could hand to another question tomorrow, and the code is
the AD-122 contract. A saved case note costs no retrieve at all.

### One correction the tests forced

The first attempt keyed the Code App's drawing on `RootCauseRequired`, which hides the row on
an **ungraded** review too. `checklistDocument.test.ts` — which compares what the app draws
against the reference Checker Checklist — failed, correctly: that took a question off a blank
checklist the source document asks. The document now draws the row unless the grade is a Pass,
while the gate keeps its own (looser) rule. The divergence is deliberate and tested on both
sides.

---

## Deployment

| Step | Command | Result |
|---|---|---|
| 1 | `dotnet build -c Release` | clean, 0 warnings |
| 2 | `dotnet test` | **1019 passed** (was 986) |
| 3 | `tsc -b` + `vitest run` | clean; **572 passed** (was 553) |
| 4 | `pushassembly` | **243,200 bytes**, modified 2026-09-19 14:42:38Z, v1.0.0.0 (was 241,664) |
| 5 | `pac powerpages download` → diff | **local matched DEV** before uploading — all 69 site settings identical, templates differ only by BOM |
| 6 | `pac powerpages upload --modelVersion Enhanced` | succeeded in 30.65s |
| 7 | re-download → diff | `data-ot-rootcause`, `grade_seen`, `syncRootCause` all present; CSS byte-identical; **69 site settings intact**, `Webapi/contact/fields` still carries `al_accountabilityrequest` |
| 8 | `npm run build` + `npx pa app push` | pushed; bundle `index-CNkiRzCM.js` |
| 9 | `round-trip-src.ps1` | 822 files, 3 changed: the assembly, the OT Review Detail component, the CSS |

Step 5 is there because an upload from this repo silently pushed stale local config over
newer DEV state once before, on 2026-09-19, and still reported success. It is now the
standing precaution before any portal upload.

---

## How to test

### Portal — the reveal (the main path)

1. Sign in as an AQS checker and open a review assigned to you that is **not** submitted.
2. Scroll to **Checker judgement and grading**.
3. With **Advice Quality Grade** unanswered, the **Primary root cause** 3×3 **is shown**. This
   is deliberate — the checklist asks the question.
4. Tick **PASS**. The root cause block **disappears**, and any cause that was ticked is
   unticked as it goes.
5. Tick **POTENTIAL HARM** (or Pass with issues, or Insufficient evidence). The block **comes
   back**, unticked.
6. Reload the page. The block's state matches the grade you left — it is decided in Liquid, so
   there is no flash of the wrong thing on load.

### Portal — the gate

7. Grade the file **PASS**, leave the root cause blank, answer everything else, and
   **Submit**. It should go through. Before this change it was refused with
   `Q-GR-02` in the list.
8. On another review, grade **POTENTIAL HARM**, leave the root cause blank, and Submit. It
   should be **refused**, and the refusal should name `Q-GR-02`.
9. On a third, submit with **no grade at all**. The refusal should name `Q-GR-01` and
   **not** `Q-GR-02`.

### The clearing (the one worth checking in the data)

10. Grade a review **POTENTIAL HARM** and tick a root cause, e.g. *Research / rationale*.
    Wait for *Saved hh:mm*.
11. Now change the grade to **PASS**. Wait for *Saved*.
12. Query the row directly:

    ```
    al_response where al_reviewinstanceid = <the review>
      and the question version is Q-GR-02
    ```

    `al_answerchoice` should be **null**. The page hiding the block is not what you are
    checking here — the stored value is.

13. Change the grade back to **POTENTIAL HARM**. The block returns **empty**, because the
    cause really was let go.

### Tax review — should be untouched

14. Open a **Tax** review. There is no Judgement and Grading section, so there is nothing to
    see and nothing to change. Submit behaves exactly as before.

### Code App — the document

15. Open a case whose AQS review is graded **Pass**. The *Checker judgement and grading* block
    shows the grade, Case Notes and Even Better If…, and **no root cause row**.
16. Open one graded **Insufficient evidence**. The root cause row **is** there, with the ticked
    cause.
17. Open one that is in progress and **ungraded**. The root cause row **is** there, empty.

### Checklist administration — the new guard

18. In the Code App's checklist library, find **Q-GR-02**. It should now show the
    **Required by grading** treatment and offer no Retire control.
19. Call `al_RetireQuestion` against it from the browser console. The server should refuse it
    by name — the UI is advisory, the plug-in is the gate.

---

## Not done here

- **Existing data is untouched.** Reviews already submitted with a Pass and an invented root
  cause keep both. Nothing was rewritten, and nothing should be without a decision: those rows
  are what the checker recorded at the time.
- Item 1's owed data audit (AQS responses holding `120910302` against a `120910006` question
  drawn inline) is still outstanding, and is still report-only.
