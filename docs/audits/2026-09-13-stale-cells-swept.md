# Audit: every cell a write leaves behind

Asked for by the project owner after IO-300001, where a regrade recorded a final outcome and
closed the case while the page went on showing "Not recorded yet" and "Awaiting Recheck"
(AD-130). This sweeps the same class across every portal write.

Target for anything fixed: `Env_AQ_Dev` only.

---

## The class

A write changes something in Dataverse. The page shows that same fact in more than one place.
The write updates the control that was used, and the other places keep showing what the server
rendered before it. Because these pages no longer reload — deliberately, since the reload
re-rendered from a cache the write does not invalidate (AD-094, AD-128) — nothing else
refreshes them either.

**The rule:** dropping a reload obliges the page to redraw everything that reload used to
fetch, and to say so where it cannot.

## Method

For each write: what it changes in Dataverse, then every cell on the page that reports any of
those facts, then whether anything updated it.

## Findings

| # | Page | Write | Cell left stale | Certain? | Done |
|---|---|---|---|---|---|
| 1 | `OT Remediation` | Adviser signs the form off | **Adviser sign-off** summary row — "(complete remedial task in IO)" | Yes | **Fixed** |
| 2 | `OT Remediation` | Supervisor decides | **Supervisor sign-off** summary row | Yes | **Fixed** |
| 3 | `OT Remediation` | Supervisor decides | **All remedial actions checked and approved?** | Yes | **Fixed** |
| 4 | `OT Remediation` | Adviser submit, supervisor decide | Case **status badge** in the heading | **No** | **Marked**, not guessed |
| 5 | `OT Remediation` | Supervisor decides carrying a grade | **Regraded outcome** cell | **No** | Left; covered by 4's marker |
| 6 | `OT Review Detail` | Submit review | Review **status badge** | Yes | **Fixed** |
| 7 | `OT Review Detail` | Submit review | **Submitted** date, showing an em dash | Yes | **Fixed** |
| 8 | `OT Remediation` | Supervisor decides | The success message **promised where the case went** | — | **Corrected** |

Findings 1 and 2 are the rows the project owner was watching when they reported the sign-off
"does not show the updates" earlier the same day. That report was answered by removing the
reload; these two cells are what the reload had been refreshing.

### Where certainty ran out

**The case status cannot be computed by the page.** Completing or approving can move a case to
Awaiting Sign-off, Awaiting Recheck, Closed, or back to **Queued** where the route still owes
another discipline (OD-038), and which one depends on rules that live in the plug-ins. So the
badge is not rewritten with a guess. It is marked `· may have moved, reload to confirm`, which
is the honest thing a page can say about a value it knows may have changed and cannot
recompute. A regrade is the exception and sets **Closed** outright: that panel is drawn only at
Awaiting Recheck or Closed (AD-127), so the result is certain, and it clears the marker.

The same uncertainty covers finding 5. A grade chosen at sign-off is recorded only once the
case reaches its recheck, which the page cannot know it has.

### The promise that was not the page's to make

The sign-off's success message said "the case moves to Awaiting Recheck" or, with a grade, "the
final outcome is recorded with it, which closes the case". Both are false on a Tax leg that
still owes an AQS check, where approving returns the case to the queue and records no outcome.
Written earlier the same day, in the change that removed the reload. It now says what is true
either way and leaves the state to the badge.

## Not stale, checked

- `OT Review Detail`'s Tax header edit already confirms in place and points at a reload, because
  the route it can re-derive is a server decision the page cannot predict. Its message says so.
- `OT Review Detail`'s autosave writes answers and updates each question's own status line;
  nothing else on the page reports an answer.
- `OT Case Detail` has no writes, so nothing on it goes stale from one.
- `OT Review List`'s claim navigates away rather than staying; its exposure is the destination
  page rendering from cache, reported in the earlier audit and still open.

## What ran

| | Step | Result |
|---|---|---|
| 1 | New checks for the four added cells and the marker | **10 passed** |
| 2 | Existing helper checks | **19 passed** |
| 3 | Both new suites against the **deployed** templates | Fail outright — the functions do not exist there |
| 4 | Liquid balance, both templates | 497/497 and 455/455; every block type matched |
| 5 | Script blocks parsed with the Liquid stripped | 5 and 4 blocks, all parse |
| 6 | `pushwebtemplate` | OT Remediation **110692 → 114814**, OT Review Detail **100940 → 102945** |

No plug-in change: every one of these was the page failing to draw what the server had already
done correctly.

## Not done here

- The claim navigation, carried over from the earlier audit and still needing a decision.
- The Code App is not exposed to this class: it reads Dataverse directly rather than through a
  render cache, and refetches after its own commands.
- Committed on `feat/checklist-administration`. Merging to `main` is the project owner's call.
