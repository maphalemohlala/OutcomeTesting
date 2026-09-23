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
| Portal e2e | **5 passed, 7 skipped, 0 failed** | **5 passed, 7 skipped, 0 failed** |

On TEST the suite ran against case 900000004's unsubmitted Tax review. The three gating specs
pass, including the answering script loading with no console error, and both managed-list
specs pass — the second of which is the Products tick list.

Checked directly on that review, all three corrected rules behave:

- `Q-TAX-02` unanswered → fail points **open** (20 boxes), note empty
- Remedial → **both** Yes and No open, because the outcome is unanswered
- AML and CRA rows on the Tax form → **0**, which is why the count needs its `> 0` guard

And on the two submitted TEST reviews, the server-rendered lock is right in both directions:
`Q-FQTAX-01` Pass locks Yes, `Q-FQ-01` Fail locks No.

## DEV, and the two things that were not what they looked like

DEV ran green in the end — **5 passed, 7 skipped, 0 failed**, the same shape as TEST — but
only after two wrong diagnoses, both worth recording because each would mislead the next
person the same way.

**"DEV holds no case data" was wrong.** A `$count=true` query returned 0 against an
environment that holds cases 900000001–900000005. Ask for the rows themselves
(`$select=...&$top=5`); do not conclude an environment is empty from a count.

**What was actually missing was a review, not a case.** All five DEV cases sit at
**Queued** (`120910583`) and `al_reviewinstance` was genuinely empty — a review is created
when a case is claimed or assigned, so there was nothing for `OT_REVIEW_URL` to point at.
Created without a browser:

```
callapi <devUrl> al_AssignCase TargetId=<caseId>   AssigneeEmail=svc.automate.aq@ascotlloyd.co.uk IdempotencyKey=<unique>
```

That made review `0c46f88a-2db7-f111-aaac-e4fade069307` — an unsubmitted **AQS** check on
900000001. Usefully it is the opposite discipline to TEST's Tax review, so between the two
environments both sides of the fail points rule are exercised in a browser.

**The portal session expires separately from `pac auth`.** `app/e2e/.auth/portal.json` is
its own browser session; `pac auth create` does nothing for it. Re-captured interactively
with `npm run e2e:auth`, signed in as the account the review is assigned to — the Review
Instance table permission is contact-scoped, so any other account opens it read-only and
every gating spec skips rather than runs.

### What DEV proved that TEST did not

With `OT_ALLOW_WRITES=1`, *takes Pass away without a reload when a finding is ticked*
passes on the AQS review — three runs, three passes. This is the **client-side** half
(AD-041): the rule runs on `change`, with no save landing. Confirmed by query afterwards —
the review still holds **zero `al_response` rows**, so the spec writes nothing despite the
flag's name, and DEV was left exactly as found.

TEST carries the **server-side** half instead: its two submitted reviews render the lock in
the raw HTML in both directions. Neither environment proves both, and they are different
failures — a page where only the script works shows a checker Pass for the moment before it
runs.

### The two specs that still skip on DEV, correctly

- *renders the lock server-side* skips because the review holds no stored finding — there is
  nothing that ought to be locked, so the absence of a lock is the right render. TEST covers
  this one.
- *takes Pass away…* skips unless `OT_ALLOW_WRITES=1` is set. Playwright's list reporter does
  not print skip reasons, and this one was briefly misread as "Pass is already locked" — the
  two skips look identical in the output and mean opposite things.

## Known-open

**The AQS all-Yes lock withdraws the Breach and Record Keeping reasons too.** The File Quality
fail points are one undivided block of twenty reasons across four categories, so locking it on
a clean AML and CRA section also removes reasons that have nothing to do with AML. A file that
passes AML and fails on record keeping has no reason to tick. Raised with a live instance —
TEST review `8cb7fb72`, `Q-FQ-01` Fail with AML and CRA 5/5 Yes — and **confirmed to stand**
(project owner, 2026-09-23). Recorded against `AD-209` rather than left in a chat log, because
it is the same shape as the defect the 2026-09-09 direction reversed.
