# A final outcome recorded before the remediation was done — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Reported by the project owner on case **254398988**, immediately after the replay-key fix
(`2026-09-13-regrade-replay-key.md`): "the buttons are showing at the same time, even before
the initial outcome is saved. The form still editable, not showing the recorded remedial
actions."

---

## What the environment said

| Question | Answer |
|---|---|
| Case status | **Awaiting Remediation** |
| Its six remedial actions | **All Open.** No `al_adviserresponse` on any of them, no `al_completedon`, none of the three answers set |
| Sign-off rows | **None** |
| Plug-in trace, last two hours | `RegradeRequestPlugin` six times, `UpdateCaseDetailsPlugin`, `GetMyRolesPlugin`. **No `CompleteRequestPlugin`, no `SignoffRequestPlugin`** |
| Action descriptions | Well formed, one issue line each, so the six numbered rows do render with an editable cell |

So **nothing had ever been recorded on this remediation** — the form was editable and empty
because it was empty in Dataverse, and no save had ever been submitted. That part is the page
working correctly.

What was not correct: the page offered **Record the final outcome** on a case in that state,
and accepted a Pass twice. A grade on work not yet done, against six remedial actions nobody
had attested to. It stuck because nothing refused it: `CloseAfterRecheck` correctly declines
to move a case that is not at Awaiting Recheck, so the outcome ended up carrying a final grade
while the case sat in remediation — a combination the lifecycle has no name for.

The adviser's lost typing has the same root as the previous fix. The regrade panel reloaded
2.5 s after a successful save, which discarded everything typed into the form but not yet
submitted. That reload is gone as of the earlier deployment today.

## Why the guard belongs in the plug-in

`SignoffProgressPlugin.RecordFinalOutcome` has always gated its own call on the case being at
Awaiting Recheck, and `CloseAfterRecheck` moves no case from anywhere else — the rule was
already the codebase's, written twice. The two front ends were the gap: the portal panel and
the Code App's `/cases/:caseId/recheck` both call `RegradeCasePlugin.Regrade` directly, and
neither asked where the case was.

| Piece | Change |
|---|---|
| `RegradeCasePlugin.EnsureCaseIsAtTheRecheck` (new) | Allows Awaiting Recheck (the recheck itself) and Closed (the AD-031 correction). Refuses everything else, naming the state the case is in. Called from the shared `Regrade`, so both front ends and the Custom API obey it. |
| `RegradeRequestPluginTests` | A theory over Submitted, Awaiting Remediation, Remediation In Progress and Awaiting Sign-off expecting a refusal and no `al_finaloutcome` written; and a test that a Closed case can still be corrected. |
| `OT Remediation` | Reads the case status. Before the recheck the panel is replaced by a line naming the state, saying the final outcome comes once every remedial action is signed off, and reporting any final outcome already recorded rather than hiding it. |
| `knowledge/decision-log.md` | AD-127. |

Hiding by state is not the role-hiding NFR-SEC-01 forbids. The role question is who you are,
which Liquid must not appear to decide; this is whether the case has reached the step at all,
and the server refuses it either way.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins) | **815 passed** (5 new; the four state cases failed before the guard) |
| 2 | `dotnet build -c Release` then `pushassembly` | **222,720 bytes**, matching the local build |
| 3 | `pushwebtemplate … OT Remediation` | `a1000000-…-019`, **95909 → 98868 chars** |

Liquid checked before deploying: 497 opens against 497 closes, and every block type balanced
(`if` 92/92, `unless` 7/7, `for` 20/20, `capture` 11/11, `comment` 41/41, `fetchxml` 6/6). No
Liquid delimiter inside any comment. Every `<script>` block parses with the Liquid stripped.
The state test is written as an `unless` rather than a comparison against `false`, which this
template uses nowhere.

## Live confirmation of the earlier fix

The owner regraded again at 17:24, after the replay-key deployment. It was **recorded**, not
swallowed: a second Audit Event with key `portal-regrade-e1d92107…-pass-3518566` alongside the
original `portal-regrade-e1d92107…`, and `al_regradedon` moved to 17:24:17. Before that fix it
would have returned the first regrade's result and written nothing.

## Not done here

- **Case 254398988 still carries `al_finaloutcome` = Pass**, recorded before the guard
  existed, while the case sits at Awaiting Remediation. Nothing is stuck: when the remediation
  is signed off, the sign-off records the grade the supervisor picks under its own key and
  closes the case. Clearing the stale grade is a data write on the owner's case and was not
  made.
- The two sign-off writes on this page still reload after 2.5 s with the AD-117 caveat, and
  can still discard unsaved typing the same way. Same shape, separate change.
- Committed on `feat/checklist-administration` (`c81730c`) and **merged to `main`** the same day at the project owner's direction. DEV carries it; TEST and PROD do not.
