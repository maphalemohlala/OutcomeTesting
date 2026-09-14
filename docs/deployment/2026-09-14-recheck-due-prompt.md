# The case parked at the recheck now says so — DEV and TEST, 2026-09-14

Targets: `Env_AQ_Dev` and `Env_AQ_Test`, as `svc.automate.aq@ascotlloyd.co.uk`.

Asked for by the project owner after the day's delivery status listed it as open: "A sign-off
that leaves the grade at 'Leave for a separate regrade' parks the case silently."

---

## The defect, precisely

The parking is **correct**. "Leave for a separate regrade" is a real choice on the sign-off
panel, asked for by the project owner on 2026-09-11, and a case that takes it is meant to wait
at Awaiting Recheck for its final outcome.

The silence was the defect. `SignoffProgressPlugin.RecordFinalOutcome` returns at its first
gate when the sign-off carries no `al_finaloutcome`, so only `MoveCase` runs. Nothing closes
the case and nothing tells anyone it is theirs to close. Case **254398988** sat there from
17:35Z on 2026-09-13 until the project owner noticed it the next morning — and because the
export collects Closed cases only, it had not reached Trail Light either.

## What changed

| Piece | Change |
|---|---|
| `SignoffProgressPlugin.ParkedAtRecheck` (new) | True when an **approval** has left the case at Awaiting Recheck. Read from the case **after** `MoveCase` and `RecordFinalOutcome` have run. |
| `SignoffProgressPlugin.QueueRecheckDue` (new) | Queues a `Recheck due` notification to the signatory, naming the case and saying the final outcome is what closes it. |
| `NotificationOutbox.EventRecheckDue` | New `al_notification_event` value **120910806**, "Recheck due". Additive, as 120910805 was. |
| `SignoffProgressPluginTests` | Five: parked at the recheck; not parked when Closed; not parked when Queued; never parked on a rejection; and the body names the case and what is owed. |
| `knowledge/decision-log.md` | AD-137. |

## The three choices worth defending

**Read after the fact, not predicted.** The condition is asked of the case as it now stands
rather than derived from the sign-off row. That is what makes it correct in every branch
`MoveCase` has: a graded approval has already **Closed** the case, a Tax leg that still owes
AQS has gone back to **Queued** (OD-038), and a remediated Tax-only case with no Outcome was
closed without a recheck at all (AD-055). Only the parked case is still at Awaiting Recheck by
the time this runs, so no branch needs to be enumerated and none can be missed.

**Keyed on the case, not the sign-off.** A check raises one action per thing marked down and
the page signs them off one at a time — an AQS check can carry eighteen. Keyed on the sign-off,
the supervisor would get eighteen identical reminders. The outbox's alternate key collapses
them to one.

**A prompt, not a refusal.** Making the grade mandatory at sign-off would have closed the hole
too, and would have removed a choice the project owner explicitly asked for. The case is
allowed to wait; it is not allowed to wait unannounced.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins) | **822 passed** (5 new) |
| 2 | `addoptionvalue … al_notification al_event 120910806` ×2 | Inserted and published in DEV and TEST — 7 values each |
| 3 | `dotnet build -c Release --no-incremental` | **223,744 bytes** (222,720 before, so the change is real) |
| 4 | `pushassembly` (DEV) | 223,744 bytes, 11:11:16Z |
| 5 | `scripts/round-trip-src.ps1` | 3 changed, 0 orphaned — its first real use |
| 6 | Solution 1.0.2.0 → **1.0.3.0**, managed export, TEST import | See below |

## An honest note on the tests

The five tests were written before the implementation, but `ParkedAtRecheck` did not exist at
that point, so they failed to **compile** rather than failing red. That is a weaker signal than
a proper red-green cycle: a test that cannot compile has not demonstrated it is testing the
right thing. They pass now, and the four state cases would each have failed against the old
behaviour, but the cycle was not observed.

## Not done here

- **Nobody has received one of these yet.** The notification is queued to the outbox; the drain
  that sends it is a separate concern (OD-030), and no case has been parked since the deploy.
- Recipient routing follows the signatory. A sign-off row that names none queues a row with no
  recipient, which the drain will not send — deliberate, and visible in the outbox rather than
  silently dropped.
