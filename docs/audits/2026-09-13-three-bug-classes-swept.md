# Audit: the three bug classes from 2026-09-13, swept across the codebase

Asked for by the project owner after three reports on case 254398988 in one session, each of
which turned out to be a different defect class. This sweeps the whole codebase for more of
each, rather than waiting for them to be reported one at a time.

Target for anything fixed: `Env_AQ_Dev` only.

---

## The three classes

| | Class | Where it was found |
|---|---|---|
| **A** | **A page reloads after a write it did not make**, into a render cache the write does not invalidate, and re-draws the old state under a success message | `OT Remediation`, three writes (AD-126, AD-128) |
| **B** | **A derived replay key collapses two different intents**, so a genuine second act is answered with the first one's result and nothing is written | `RegradeRequestPlugin` (AD-126) |
| **C** | **A command has no lifecycle guard**, so a front end can invoke it at a point where it means nothing | `RegradeCasePlugin.Regrade` (AD-127) |

---

## Class A — pages that write, then reload

Every portal template that writes was checked. There are three.

| Page | Write | Verdict |
|---|---|---|
| `OT Remediation` | Adviser sign-off, supervisor decision, regrade | **Was the report.** Fixed: all three confirm in place (AD-126, AD-128) |
| `OT Review Detail` | Tax team header edit | **Found: the same defect.** Reloaded *immediately* after the write — no delay at all — and the route this can re-derive (BR-004) is drawn in the Review progress table on the same page. **Fixed** in this audit |
| `OT Review List` | Claim a case from the queue | Navigates to `/review?case=…` rather than reloading. See below |

### Fixed here: the Tax header edit

Same remedy as the remediation page. The write PATCHes `contact.al_caseheaderrequest` and
`CaseHeaderRequestPlugin` writes `al_outcomecase` and, where the route re-derives, the review
instances — rows the portal did not write, so its cache learns of them by polling (AD-094).
The two selects already show what was saved because the reader chose them; what the page
cannot know is whether the route moved, so it now says that and offers a **Reload the page**
button instead of taking one.

`OT Review Detail`: 97876 → 99982 chars, pushed to DEV.

### Reported, not fixed: the claim navigation

`OT Review List` claims a case and immediately sends the browser to the review page. That
page decides whether the checklist is editable from `rv.al_assignedcontactid.id == user.id`
— the contact `ClaimCasePlugin` had just written on the review instance, and a row the portal
did not write. If the render cache has not caught up, the page loads read-only and reads as
though the claim did not work.

**Confirmed by reading, not reproduced.** The mechanism is identical to the three reports, and
the claim on case 254398988 at 16:46 was followed by successful answering three minutes later,
which neither proves nor disproves it. The fix is not obvious — the page genuinely needs a
server render — so it is left for a decision rather than guessed at. Options are to have the
review page tolerate a missing assignment it can attribute to the cache, or to pass the claim
forward in the URL so the page can trust it for one render.

---

## Class B — every derived replay key

Keys supplied by a caller are not at risk: the Code App sends a fresh GUID per intent. Only
keys the server derives can collapse two intents, because the browser cannot supply a stable
one. There are six.

| Key | Shape | Verdict |
|---|---|---|
| `portal-regrade-…` | outcome + grade + row version | **Was the report.** Fixed (AD-126) |
| `portal-complete-…` | action + clock start | **Already fixed**, for this exact defect: it was the action alone, and the completion after a rejected sign-off replayed the first one. The precedent that makes this a class |
| `portal-submit-…` | review instance | **Sound.** A review is submitted once; nothing in the codebase clears `al_submittedon`, so there is no second intent to collapse |
| `ReplayKeyFor(signoffId)` | the sign-off row | **Sound.** Unique per decision, which is what AD-125 changed it to |
| `ASG-…` (assignment code) | case + review + assignee | **Sound.** No code clears a review instance's assigned contact, and `ResolveOrCreateReview` refuses to reuse an assigned instance, so the same triple cannot legitimately recur |
| `caseheader-…-<ticks>` | case + timestamp | **Not an idempotency key** — never equal twice, so it can never replay. Harmless in practice: the plug-in returns early when the edit changes nothing, so a retry writes nothing and no duplicate audit event. Left alone; noted so nobody reads it as a working replay guard |

### One latent risk, not currently reachable

`SignoffProgressPlugin.RecordFinalOutcome` keys on `signoff-regrade-<outcomeId>` — the outcome
alone, the shape that failed for the regrade. It would swallow a second sign-off-driven grade
on the same outcome. That needs a case to reach Awaiting Recheck twice, which needs a closed
case to reopen, which `CaseTransitions` does not allow. **Not a bug today.** It becomes one the
moment a reopen path is added, and should be keyed on the sign-off row like its sibling if that
ever happens.

---

## Class C — lifecycle guards on everything a front end can invoke

| Command | Guard | Verdict |
|---|---|---|
| Regrade | Awaiting Recheck or Closed | **Was the report.** Fixed (AD-127) |
| Claim | Case Queued; refuses an instance another checker holds | Guarded |
| Submit review | Refuses a resubmit; refuses AQS while Tax is owed (AD-112) | Guarded |
| Complete remediation | Status and a recorded response | Guarded |
| Sign off | `SignoffGuardPlugin.AlreadySettled`; notes on a rejection; refuses a grade with a rejection | Guarded |
| Tax header edit | `EnsureTaxReviewOpen` refuses once the Tax check is submitted | Guarded |

The regrade was the only one missing a guard. The affordances match: each control is rendered
under the same condition its command enforces, and the header edit is drawn only for an
unsubmitted Tax review.

### A regression risk in this session's own fix, closed

AD-127's guard sits in the shared `Regrade`, which `SignoffProgressPlugin` also calls when a
supervisor grades as they approve. That path was only safe because `MoveCase` runs first and
leaves the case at Awaiting Recheck — true in the code, but nothing tested the two together:
the existing tests drive each step on its own with a seeded status.

`The_grade_recorded_with_the_last_approval_still_lands_after_the_case_moves` now composes the
real sequence and asserts the grade lands and the case closes. It passes.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins) | **816 passed** (1 new) |
| 2 | Liquid balance, `OT Review Detail` | 455 opens / 455 closes; `if` 64/64, `comment` 36/36; no delimiter inside a comment |
| 3 | Script blocks parsed with Liquid stripped | 4 blocks, all parse |
| 4 | `pushwebtemplate … OT Review Detail` | `a1000000-…-01b`, **97876 → 99982 chars** |

No plug-in change, so no assembly push.

## Not done here

- The claim navigation above, which needs a decision before a fix.
- The Code App was not swept for class A: it reads Dataverse directly rather than through the
  portal's render cache, so the class does not apply to it. Classes B and C reach it through
  the shared commands, which this audit covered.
- Committed on `feat/checklist-administration` (`c81730c`) and **merged to `main`** the same day at the project owner's direction. DEV carries it; TEST and PROD do not.
