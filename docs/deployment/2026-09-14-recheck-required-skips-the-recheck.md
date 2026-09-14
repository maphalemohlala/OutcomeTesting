# "Recheck required: No" now skips the recheck — DEV and TEST, 2026-09-14

Targets: `Env_AQ_Dev` and `Env_AQ_Test`, as `svc.automate.aq@ascotlloyd.co.uk`.

Asked for by the project owner: "al_recheckrequired skip the recheck." Closes the item this
morning's report opened — "recheck required marked as No, yet status shows awaiting recheck"
(case 254398988) — which was answered at the time with *why* it did nothing, not with a fix.

---

## What was wrong

`al_recheckrequired` was a form question and nothing else (AD-095). It was captured on each
remediation action, displayed on the case page, the remediation page and the app, and locked
with the adviser's response by `RemediationResponseGuardPlugin`.

It was read by **no code that decides anything.** Answering "No" changed nothing, and the case
went to Awaiting Recheck regardless — which is not what a field called "Recheck required"
leads anyone to expect.

## The rule

`SignoffProgressPlugin.RecheckWaived` reads the answers across the check's actions. The recheck
is waived only when **nothing asks for one and something declines one**:

| The check's actions | Recheck |
|---|---|
| At least one **No**, no **Yes** | **Waived** — case closes on the approval |
| Any **Yes** | Kept — a check is remediated as a whole, and one thing worth another look is enough |
| All **unanswered** | Kept |
| No actions at all | Kept |
| A **No** beside an unanswered action | **Waived** |

**Absence is not a No.** Every action raised before AD-095 added the column carries no answer,
and a case must not close itself on a question nobody was asked. The opposite rule — requiring
*every* action to answer No — was rejected for the mirror reason: it would make the waiver
unusable on any check that predates the column.

## Where it is evaluated, and why that matters

**After `MoveCase` and `RecordFinalOutcome`, not inside `MoveCase`.**

Inside `MoveCase` the case would be closed before `RecordFinalOutcome` ran, and that method
gates on the case still being at Awaiting Recheck — so a supervisor who **both graded and
waived** would have their grade silently discarded. Evaluating afterwards means the grade is
recorded when one is given, and the case closes either way.

It also composes with AD-137, which was deployed an hour earlier: the same branch now either
closes the case (waived) or queues the `Recheck due` prompt (not waived). A parked case is
therefore either finished or announced — never silent.

## What a waived case carries

Whatever grade it already has. The export reads `Outcomes.EffectiveOutcomeLabel`, which falls
back from `al_finaloutcome` to `al_initialoutcome`, so a case closed without a regrade exports
the grade its check gave. That is what "no recheck needed" asserts — the original grade stands.

Checked before writing the rule, because the alternative would have been a silent export gap.
OD-041's concern (a remediated case sitting at Awaiting Recheck still showing its
pre-remediation grade) does not apply to a case nobody intends to regrade.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins) | **827 passed** (5 new) |
| 2 | `dotnet build -c Release --no-incremental` | **224,256 bytes** (223,744 before) |
| 3 | `pushassembly` (DEV) | 224,256 bytes, 11:34:04Z |
| 4 | Round-trip, 1.0.3.0 → **1.0.4.0**, managed export, TEST import | See the delivery status |

## Not done here

- **`OT Remediation`'s pre-recheck message is now slightly over-promising.** It says the final
  outcome "is recorded here once every remedial action above has been signed off and the case
  reaches Awaiting Recheck" — a waived case never reaches it. The message only renders *before*
  sign-off, when it is not yet known whether the recheck will be waived, so it is imprecise
  rather than wrong. Left alone to keep this change to one thing.
- **No waived case has gone through end to end.** The rule is unit-tested across all five
  states; nothing has exercised it against a real check in either environment.
