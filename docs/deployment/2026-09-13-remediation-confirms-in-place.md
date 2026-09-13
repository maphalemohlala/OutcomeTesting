# The sign-off that showed nothing — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Reported by the project owner, third on case **254398988** in one session: "Supervisor
sign-off (complete remedial task in IO) does not show the updates ... on the remedial page
after it has been saved."

---

## What the environment said

The whole remediation had gone through. Nothing was lost and nothing was refused.

| When | What Dataverse holds |
|---|---|
| 17:34:35 – 17:34:39 | All six remedial actions **Completed**, each carrying the adviser's response and the three answers (client contact No, recheck No, changes advice Yes) |
| 17:35:09 – 17:35:15 | Six **Approved** sign-offs, `al_signedbyname` "Service Account" |
| 17:35:16 | Case moved to **Awaiting Recheck** |
| 17:35:10 – 17:35:16 | Six `SignOffRemediation` audit events, "Signed off from the portal: Approved by Service Account." |

The page then reloaded 2.5 s after each write and re-drew the remediation exactly as it had
been. The rows a sign-off writes are `al_remediationaction` and `al_signoff`, and the page
wrote neither — it PATCHed a trigger column and a synchronous plug-in did the work — so the
portal's render cache is not invalidated by this write path and learns through change
tracking (AD-094), which is polled. The reload lands inside that window.

This is the third report in one session with that single root: the regrade that "does not
save" (AD-126), the remedial actions that vanished when the regrade panel reloaded over
unsaved typing (AD-127), and now the sign-off. The reload is therefore gone rather than
re-tuned.

## What changed

| Piece | Change |
|---|---|
| `window.otRemediationRows` (new shared script block) | `completed(ids)` sets each action's badge to Completed and its decision cell to "Adviser completed &lt;date&gt;; awaiting supervisor". `decided(ids, label, rejected)` writes the decision and date, and on a rejection puts the badge back to **In progress**, because the plug-in reopens the action for rework (AD-125). `offerReload(after)` adds a **Reload the page** button. |
| Row cells | Carry `data-ot-row-status` and `data-ot-row-decision` so the scripts can find them. |
| Adviser's form (`Save and sign off this remediation`) | No reload. Makes the responses read-only, disables the three questions and both buttons, updates the rows, and says how many actions went to the supervisor. `Save draft` is unchanged. |
| Supervisor's panel | No reload. Disables the decision, notes and grade, updates the rows, and says what the decision leaves to do — the case moving to Awaiting Recheck, or the final outcome closing it where the grade was set with the approval. |
| `knowledge/decision-log.md` | AD-128. |

The badge class mirrors `OT Status Badge` rather than improving on it: "Completed" matches
none of that template's words and takes its default, "In progress" matches `progress` and is
the active colour. A row the page rewrites has to be indistinguishable from one the server
drew.

The date is formatted `dd MMM yyyy`, the same as the Liquid filter the cells are rendered
with, for the same reason.

## What ran

| | Step | Result |
|---|---|---|
| 1 | Helper smoke test against a fake DOM | **13 checks passed**, including that no reload fires on its own, that a rejection reopens only the rows it names, and that the reload button is offered once |
| 2 | Liquid balance | 497 opens against 497 closes; `if` 92/92, `unless` 7/7, `for` 20/20, `capture` 11/11, `comment` 41/41. No Liquid delimiter inside any comment |
| 3 | Every `<script>` block parsed with the Liquid stripped | 5 blocks, all parse |
| 4 | `pushwebtemplate … OT Remediation` | `a1000000-…-019`, **98868 → 108070 chars** |

No plug-in change, so no assembly push: this report was entirely about what the page showed,
and the server had already done everything correctly.

## Where case 254398988 now stands

At **Awaiting Recheck**, with every remedial action Completed and approved. The regrade panel
is therefore offered again — AD-127's gate opens at exactly this state — so the final outcome
can be recorded and will close the case.

It still carries the `al_finaloutcome` = Pass written before AD-127's guard existed. Recording
the final outcome now overwrites it under a fresh replay key and closes the case, so nothing
has to be cleaned up first.

## Not done here

- The same in-place treatment has not been applied to any other portal page. The review page
  already works this way (AD-094); the case detail page has no writes.
- Committed on `feat/checklist-administration` (`c81730c`) and **merged to `main`** the same day at the project owner's direction. DEV carries it; TEST and PROD do not.
