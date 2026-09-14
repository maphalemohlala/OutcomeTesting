# The remediation page stops trusting its own render for the case status — DEV deployment, 2026-09-14

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Reported by the project owner on case **254398988**: "On Case page, it shows closed but still
awaiting recheck on the remediations page."

---

## What the environment said

Read before changing anything (`pac env fetch`):

| Question | Answer |
|---|---|
| Case `254398988` | **Closed**, modified 2026-09-14 **08:02Z** |
| Outcome `e1d92107…` | `al_finaloutcome` **Pass**, `al_regradedon` 08:02Z, reason "good enough", initial **Insufficient evidence** preserved (BR-007) |
| Regrade audit events | three: `portal-regrade-<outcome>` 16:58Z and `…-pass-3518566` 17:24Z on 09-13, and **`…-pass-3518675` 08:02Z** today |
| Clock when the report came in | **08:04Z** |

So Dataverse held one answer, Closed, and had held it for **two and a half minutes**. Both
pages were reading the same row. The case page was current and this one was not.

That is AD-094 exactly. The regrade is written by `RegradeRequestPlugin`, not by the website —
the website PATCHed `contact.al_regraderequest` — so Power Pages clears its render cache for
the contact and for nothing else. `al_outcomecase` and `al_outcome` wait on polled change
tracking, inside a fifteen-minute SLA. The two pages run **different Liquid `fetchxml`
queries**, so they are separate cache entries that expire independently: one had refreshed,
one had not.

## Why marking the cell was not enough

AD-130 and AD-131 already cover this class: a page that drops its reload must redraw what the
reload used to fetch, and mark what it cannot recompute. `otRemediationRows.regraded` sets the
badge to **Closed** outright after a regrade, and `caseStatusMayHaveMoved` marks it
`· may have moved, reload to confirm` after a sign-off.

Both only run **in the browser session that made the write**. Navigate away and come back —
which is what the owner did — and the server re-renders the stale value with no marker on it,
because no write happened in that page load. A stale badge carrying no marker reads as
authoritative. Marking the value was not enough; reading the value is.

This is the third report of this one cache costing the project owner a false defect
(IO-300005/AD-117, IO-300001/AD-130, and this).

## What changed

| Piece | Change |
|---|---|
| `Webapi/al_outcomecase/enabled` (new) | `true`. Exposes the case to the browser for reading. |
| `Webapi/al_outcomecase/fields` (new) | `al_casestatus`. One column. |
| `OT Remediation` | The badge carries `data-ot-case-id`; the row module exposes `caseStatusIs(label)`; a new IIFE GETs `/_api/al_outcomecases(<id>)?$select=al_casestatus` on load with the FormattedValue annotation and rewrites the badge **only where the live label differs** from the rendered one. Any failure leaves the server-rendered badge alone. |
| `knowledge/decision-log.md` | AD-133. |

**Read-only by construction.** The sole permission on the table is `Outcome Case - read all`
(`a1000000-…-060`), with `adx_read: true` and `adx_write`, `adx_create` and `adx_delete` all
**false**. So enabling the Web API here grants the browser nothing beyond the read-all OD-022
already gives every authenticated user, and no PATCH can succeed whatever the allowlist says.
Checked before the setting was created, not after.

The FormattedValue annotation rather than the raw option-set integer, so the page keeps no
second copy of the lifecycle values to drift from `CaseLifecycle.cs`.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `setsitesetting … Webapi/al_outcomecase/enabled true --create` | Created, row `a53b48a2-13b0-f111-aaac-e4fade069307` |
| 2 | `setsitesetting … Webapi/al_outcomecase/fields al_casestatus --create` | Created, row `5633a2ab-13b0-f111-aaac-e4fade069307` |
| 3 | `pushwebtemplate … a1000000-…-019` | 114814 → 118708 chars, 08:10:39Z |
| 4 | `pushwebtemplate … a1000000-…-019` (comment correction: the decision is AD-133, AD-131 was taken) | 118708 → **118886** chars, 08:12:45Z |
| 5 | `pac env fetch` on `mspp_sitesetting` | Both rows **Active**, values `true` and `al_casestatus` |

Liquid checked before each deploy: 497 opens against 497 closes, every block type balanced
(`if` 92/92, `unless` 7/7, `for` 20/20, `capture` 11/11, `comment` 41/41, `fetchxml` 6/6), no
Liquid delimiter inside any comment. The counts are unchanged from the 09-13 pushes because
this change adds no Liquid. The pushed character count matches the local file exactly at both
pushes.

`setsitesetting --create` lets Dataverse assign the row id rather than honouring a minted one,
so `sitesetting.yml` was corrected to the two ids above. Left as the invented
`a1000000-…-af`/`-b0` they would have been duplicated by the next upload.

## Not verified here

- **That the Web API bypasses the Liquid render cache.** The whole fix rests on it. They are
  different data paths and AD-094's staleness is specifically about server-rendered content,
  but this has not been proven, and if it is wrong the fix is inert rather than harmful. It
  cannot be proven on 254398988 any more: the case closed at 08:02Z and the fifteen-minute SLA
  has since passed, so the staleness is no longer reproducible on demand. **The next case
  through sign-off is the proof.**
- **The site cache clear is the owner's step**, and it matters more than usual here: a new
  *site setting* needs the site restarted before the Web API will answer at all. Until then
  the GET 404s and the page simply keeps the server-rendered badge — the failure is quiet, as
  designed, but it is indistinguishable from the fix working.
- Nothing was committed. The working tree carries `sitesetting.yml`,
  `OT-Remediation.webtemplate.source.html` and `knowledge/decision-log.md`.

## Still open

Neither of these was touched, and both are the project owner's call:

- **Should `recheck required = No` skip the recheck step?** `al_recheckrequired` is a form
  question on `al_remediationaction` (AD-095), read by the templates and the app for display
  and by no lifecycle code at all. It cannot suppress Awaiting Recheck today, which is not
  what the field's name leads a user to expect.
- **Should a sign-off that leaves the grade at "Leave for a separate regrade" park the case
  silently?** That is what left 254398988 at Awaiting Recheck: the six sign-offs at 17:35Z
  carried no `al_finaloutcome`, so `RecordFinalOutcome` returned at its first gate, while the
  grade that would have closed the case had already been spent at 17:24Z — eleven minutes
  before the case reached the recheck, where `CloseAfterRecheck` correctly declined it.
