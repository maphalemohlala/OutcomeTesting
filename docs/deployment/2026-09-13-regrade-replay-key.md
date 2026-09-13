# The portal regrade that reported success and recorded nothing — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Reported by the project owner against case **254398988**: **Record the final outcome** shows
a saved message "but it does not save", and the form shows both that button and
**Save and sign off this remediation**.

---

## What the environment said

Read before changing anything (`fetch`, registration tool):

| Question | Answer |
|---|---|
| `RegradeRequestPlugin: Update of contact` | Enabled, sync post-operation, filtered to `al_regraderequest` — all 14 request steps enabled |
| Plug-in trace | Ran twice for the signed-in contact, 16:58:57Z and 16:59:27Z, depth 1 and 2 each time, no exception |
| `contact.al_regraderequest` | Empty on every contact — the plug-in cleared it, as designed |
| `al_outcome` `OUT-254398988-2` | `al_initialoutcome` Insufficient evidence, **`al_finaloutcome` Pass, `al_regradedon` 16:58:33Z, `al_regradereason` as typed** |
| `al_auditevent` | One `RegradeCase` event, key `portal-regrade-e1d92107…`, actor Service Account, details Pass, 16:58:33Z |
| Case | `Awaiting Remediation`, last modified 16:55:40Z — untouched by the regrade, because only a case at Awaiting Recheck is closed by one |

So the first click **recorded the regrade in full.** What the page then showed was the
portal's render cache: the rows a regrade writes are the outcome and the case, the page
wrote neither (it wrote the contact's request column), and the cache learns of them through
polled change tracking (AD-094). The 2.5 s reload re-drew the old grade beneath the success
message — the IO-300005 report of 2026-09-11 (AD-117), again, and the caveat text added
then was not read as an answer.

The second click, thirty seconds later, is the defect. Its derived replay key was
`portal-regrade-<outcomeId>` — the outcome alone — so it matched the first regrade's Audit
Event and the plug-in returned that result and **wrote nothing**, while the page again
reported success. Every regrade of an outcome after its first was being discarded from the
portal: the AD-031 correction, and the regrade a supervisor owes after sign-off when an
early one was made by mistake. On this case that would have meant a case the portal could
never close. The plug-in's own comment said "one intent per outcome and grade"; the key did
not.

Both buttons showing is by design and unchanged: the regrade panel renders wherever the
case carries a graded outcome, and it is not hidden by role because Liquid cannot enforce
one (NFR-SEC-01, AD-117's "not a fault"). The signed-in account holds both roles, so both
controls are meaningful to it.

## What changed

| Piece | Change |
|---|---|
| `RegradeRequestPlugin.DeriveIdempotencyKey` (new) | `portal-regrade-<outcome>-<grade>[-<rowversion>]`. The same request twice replays; a different grade, or the same grade against a later read, is recorded. |
| `RegradeRequestPluginTests` | `A_second_regrade_to_a_different_grade_is_recorded_rather_than_replayed`: two regrades, two Audit Events, the later grade on the row. The existing replay test still passes. |
| `OT Remediation` regrade panel | No reload on success. The panel rewrites its own summary line to the grade just recorded, disables its controls (its row version is now stale), and says the rest of the page still shows the earlier read — the shape the review page has had since AD-094. |
| `knowledge/decision-log.md` | AD-126, amending AD-117. |

The Code App's `al_RegradeCase` sends its own key and was never affected.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins) | **810 passed** (1 new; it failed before the fix on the equal audit ids) |
| 2 | `dotnet build -c Release` → `pushassembly` | **222,208 bytes**, matching the local build |
| 3 | `pushwebtemplate … OT Remediation` | `a1000000-…-019`, **93822 → 95909 chars** |

Liquid checked before deploying: 472 tag opens against 472 closes, 40 comment pairs; every
`<script>` block parses with the Liquid stripped (the first attempt did not — an apostrophe
in a JS string lost its escape — and was corrected before the push).

## Not done here

- No live regrade was made on 254398988 to prove the new key: that writes a second Audit
  Event on the owner's case. The next regrade of it from the portal — a different grade, or
  the same grade after a reload — will now be recorded; the first one, Pass with the reason
  typed at 16:58:33Z, already stands.
- The two sign-off writes on the same page still reload after 2.5 s with the AD-117 caveat.
  They have the same cache exposure, and the same in-place shape would suit them; left for a
  separate change.
- Committed on `feat/checklist-administration` (`c81730c`) and **merged to `main`** the same day at the project owner's direction. DEV carries it; TEST and PROD do not.
