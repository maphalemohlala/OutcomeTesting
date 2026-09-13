# The regrade that recorded, closed the case, and changed nothing on screen — DEV, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Reported by the project owner on **IO-300001**: "I've submitted a final outcome, yet it still
shows *Not recorded yet* on the Regraded outcome and *Awaiting Recheck* on the Case status."

---

## What Dataverse holds

The regrade succeeded completely, at **18:14:01**:

| Column | Value |
|---|---|
| `al_initialoutcome` | Insufficient evidence, preserved (BR-007) |
| `al_finaloutcome` | **Pass** |
| `al_regradedon`, `al_finalisedon` | 18:14:01 |
| `al_regradereason` | as typed |
| Case `al_casestatus` | **Closed**, 18:14:01 |

The plug-in ran clean, with no exception. A second click at 18:14:27 carried the same outcome,
grade and row version, so it replayed and wrote nothing — the AD-126 key behaving exactly as
intended.

## What was wrong

**This was a gap in AD-128, made by me earlier the same day.** That change stopped the page
reloading after a write, because the reload re-rendered from a cache the write does not
invalidate. It replaced the reload with the page drawing what it knows — but only for the
regrade panel's own hint line and the sign-off's row cells.

The Regraded outcome cell in the table above, and the case's status badge in the heading,
report the same two facts and are server-rendered. Nothing updated them, and nothing reloaded,
so they kept saying "Not recorded yet" and "Awaiting Recheck" while Dataverse held Pass and
Closed.

**Removing a reload obliges the page to draw everything that reload used to fetch.** That is
the rule this missed, and it is now written down as AD-130.

## What changed

| Piece | Change |
|---|---|
| `otRemediationRows.regraded(label)` (new) | Rewrites the Regraded outcome cell to the grade plus the regraded-on date, as its own note, matching what the server renders into that cell; and sets the case status badge to **Closed** |
| `badgeClass(label)` (extracted) | Mirrors the whole of `OT Status Badge`'s cascade in its own order, so a rewritten badge cannot disagree with a rendered one. Previously it knew two labels; it now knows the same eight the include does |
| Cells named | `data-ot-regraded-cell` on the table cell, `data-ot-case-status` on the heading badge |
| Regrade success message | Says the two cells above now show it and the case is closed, and offers the same **Reload the page** button the other writes do |

**Closed is certain, not assumed.** The panel is drawn only at Awaiting Recheck or at Closed
(AD-127); a regrade closes the first and the second is already closed. There is no third case
in which the panel could be on screen.

## What ran

| | Step | Result |
|---|---|---|
| 1 | Helper smoke test against a fake DOM | **19 checks passed**, six of them new: the cell before and after, the date note, the Closed badge and its class, and that a second grade replaces rather than appends |
| 2 | The same test against the **deployed** template | Fails outright — `rows.regraded is not a function`. The page had nothing to update those cells with |
| 3 | Liquid balance | 497 opens / 497 closes; `if` 92/92, `comment` 41/41; no delimiter inside a comment |
| 4 | Script blocks parsed with the Liquid stripped | 5 blocks, all parse |
| 5 | `pushwebtemplate … OT Remediation` | `a1000000-…-019`, **108070 → 110692 chars** |

## Where IO-300001 stands

Closed, graded Pass over an initial Insufficient evidence, with the reason in its audit
history. Nothing needs re-doing; reloading the page will now show what was already true, and a
regrade from here on updates the page as it goes.

## Not done here

- `OT Case Detail` shows the same two facts and has no regrade control, so nothing on it goes
  stale from this write. It is only ever read after a fresh render.
- The sign-off's own final-outcome path records the grade on the server in the same way, but
  the page cannot know at that moment whether the last action has just moved the case, so its
  message still points at a reload rather than redrawing the cells. Unchanged here.
- Committed on `feat/checklist-administration`. Merging to `main` is the project owner's call.
