# The gating corrections, the header reversal, and a fix that had never been deployed

Written for: whoever picks this batch up next, or has to undo part of it.

**Date:** 2026-09-23
**Environments:** `Env_AQ_Dev` (`org0b075da8`) and `Env_AQ_Test` (`org37995f36`)
**Shipped:** plug-in assembly + the OT Review Detail web template. **No solution import.**

## Why no solution import

This batch changes no schema, adds no plug-in type and registers no step. The three new
classes — `ChecklistGating`, `ChecklistDocument`, `PortalSite` — are helpers, not `IPlugin`s.
The 24 changed files under `src/SdkMessageProcessingSteps/` are additive `StateCode` /
`StatusCode` stamps (the 2026-09-02 guard), not functional changes.

So the deployable surface is one assembly and one web template, and both were pushed
directly. `src/Other/Solution.xml` is still `1.0.6.0`, which is what TEST already holds — a
solution import would have needed a version bump and would have carried far more than these
changes.

| Artefact | DEV | TEST |
|---|---|---|
| `OutcomeTesting.Plugins.dll` (321,536 bytes) | 08:29:57Z | 08:30:43Z |
| `OT Review Detail` (229,974 → 228,640 chars) | 08:30:17Z | 08:06:15Z |
| Steps enabled afterwards | **55 / 55** | **55 / 55** |

Both pushed with the registration tool's `pushassembly` and `pushwebtemplate`, which target a
single component rather than `pac powerpages upload` — the full upload would have pushed the
whole local site config over each environment, and `powerpages/` is hand-authored rather than
a mirror.

## What changed

Three rules given on 2026-09-22 were corrected on 2026-09-23, and one rule from that day was
reversed. `AD-208`, `AD-209` and `AD-210` in the decision log carry the reasoning; in short:

- **The case header no longer freezes when the Tax check is submitted.** Both teams edit it
  throughout. The Tax team's own two route-deriving fields keep their existing rule.
- **The Tax fail points lock on `Q-TAX-02`** (the Tax *check* outcome, S-TAX), not on
  `Q-FQTAX-01` (the tax *file quality* outcome, S-FQTAX).
- **The AQS fail points lock when every AML and CRA point reads Yes**, rather than staying
  locked until a finding arrives.
- **"Remedial action required?" is a lock, not a default** — the answer the file quality
  outcome does not imply is disabled, refused server-side, and reconciled if the outcome
  moves under an answer already saved.

## The AML and CRA count is scoped to the review's own checklist version

Worth naming because it fails **open** and so would never have been reported.
`ChecklistQueries.AmlCraQuestionCodes` counts the S-AMLCRA questions in force **on the
checklist version the review was issued**, the way `ResponseGuardPlugin` already scopes
whether a section may be answered at all. Counting every S-AMLCRA question in the environment
instead would mean a later version adding a sixth point put a code in the expected set that an
older review could never answer — "all Yes" becomes unreachable and the AQS lock quietly stops
working, with nothing to see.

## The products report, and what it actually was

Reported as *"the products do not appear after submitting a check"*. It was not a defect in
the page: `ListOptionRules.AppliedByAssociation` and its caller — which keep `al_productids`
out of the `ColumnSet`, because Products is a many-to-many naming no column — existed **only
in the working tree**. They are in no commit before this batch, so no environment had ever
run them.

Without the guard, every header edit touching Products faulted on the READ
(`'al_OutcomeCase' entity doesn't contain attribute with Name = 'al_productids'`), and because
the submit button flushes the header first (`window.otHeader.flush`), the fault took the
submit with it. The products were never written, so there was nothing to appear.

**Proved on TEST after the push:** ticking one product on case 900000004 through the portal
wrote the association; confirmed against Dataverse, then removed again to leave the case as
found.

**Do not re-test this by eye.** The portal's render cache is eventually consistent (AD-094, up
to fifteen minutes): immediately after that write the page still showed the checkbox unticked
while Dataverse held the association. Verify against the Web API, not the page.

## Verification

| | DEV | TEST |
|---|---|---|
| Plug-in tests | 1,618 passed | (same assembly) |
| App tests | 1,006 passed across 77 files | (same build) |
| Portal e2e | **could not run** | **5 passed, 7 skipped, 0 failed** |

On TEST the suite ran against case 900000004's unsubmitted Tax review. The three gating specs
pass, including the answering script loading with no console error, and both managed-list
specs pass — the second of which is the Products tick list.

Checked directly on that review, all three corrected rules behave:

- `Q-TAX-02` unanswered → fail points **open** (20 boxes), note empty
- Remedial → **both** Yes and No open, because the outcome is unanswered
- AML and CRA rows on the Tax form → **0**, which is why the count needs its `> 0` guard

And on the two submitted TEST reviews, the server-rendered lock is right in both directions:
`Q-FQTAX-01` Pass locks Yes, `Q-FQ-01` Fail locks No.

## Why DEV could not be tested through the browser

Two independent reasons, both environmental:

1. **The DEV portal session has expired.** The e2e guard reports *"redirected off the portal —
   the session has expired, re-run `npm run e2e:auth`"*, which is the guard working. Re-auth
   is interactive.
2. **DEV holds no case or review data at all.** `al_outcomecase` and `al_reviewinstance` are
   both **empty**; `al_listoption` (19), `al_question` (47) and `contact` (9) are seeded. The
   environment has been refreshed and never re-seeded with working data, so even signed in,
   every review spec would skip for want of anything to point at.

The DEV deployment itself is verified — assembly pushed, template pushed, 55/55 steps
enabled. It is the browser-level proof that is missing, and it is missing for want of a
session and a case, not for want of a working build.

## Known-open

**The AQS all-Yes lock withdraws the Breach and Record Keeping reasons too.** The File Quality
fail points are one undivided block of twenty reasons across four categories, so locking it on
a clean AML and CRA section also removes reasons that have nothing to do with AML. A file that
passes AML and fails on record keeping has no reason to tick. Raised with a live instance —
TEST review `8cb7fb72`, `Q-FQ-01` Fail with AML and CRA 5/5 Yes — and **confirmed to stand**
(project owner, 2026-09-23). Recorded against `AD-209` rather than left in a chat log, because
it is the same shape as the defect the 2026-09-09 direction reversed.
