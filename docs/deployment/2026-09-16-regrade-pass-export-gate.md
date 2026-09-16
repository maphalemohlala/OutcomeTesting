# A regrade to a final Pass blocked the export — DEV, 2026-09-16

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **TEST was not touched**; promotion is a separate decision
and the project owner kept this one to DEV.

Reported by the project owner: *"I get this error when I try to generate an export — Case
IO-300001 has a non-pass outcome with no fail accountability recorded."* Then, against the
first explanation: *"check the case to see if this is true, or a bug, as a final outcome has
been recorded for this case."*

It was a bug. The case had passed.

---

## 1. What IO-300001 actually carried

One `al_outcome` row, `a652e014-9daf-f111-aaac-e4fade069307`:

| Column | Value |
|---|---|
| `al_initialoutcome` | Insufficient evidence |
| `al_finaloutcome` | **Pass** |
| all four accountability flags | false |

Case Closed, review route **Tax only**. A regrade that landed on a Pass. By BR-007 the final
outcome is the one in force, so there was no fail to attribute and the gate should never have
fired.

## 2. Why it fired

`al_initialoutcome` and `al_finaloutcome` are **separate option sets** — `1209107_0_x` is the
grade a check gave, `1209107_1_x` the grade it ended on. `OutcomeRules` has said so in a
comment since the BR-005 constants were written.

`Outcomes.EffectiveOutcome` applied the BR-007 precedence and returned the raw value off
whichever column won. Every caller then compared that against the `OutcomeRules` grade
constants, which are the `1209107_0_x` band:

```csharp
return outcome != OutcomePass;   // 120910710 != 120910700  →  true
```

A final Pass was measured against the initial scale, matched nothing, and read as a non-pass.
Nothing translated between the two bands anywhere; `IsFinalOutcome` existed but was only ever
used as a validity check in `SignoffGuardPlugin`.

**Only a final Pass was wrong.** A final Insufficient or Pass-with-issues also failed to match
the initial scale, but that yields *non-pass*, which is the right answer by accident.

### The same fault, pointing the other way

`SetFailAccountabilityPlugin` pairs the same two calls in its "this case passed, so there is no
fail to attribute" guard. It would have **let** a fail be attributed on a case that passed —
writing an adviser's name into AD-039 columns 11-14 and 17-20 for a clean case. Both sites are
fixed by the one change, because both go through `EffectiveOutcome`.

## 3. Why the tests did not catch it

Every `DescribeIncompleteRow` test built its row through a helper that only ever set
`al_initialoutcome`. The final column was never exercised through that gate.

Worse, the precedence test in `OutcomesTests` wrote an **initial**-scale value into the final
column — data that cannot occur — so the one test that looked like it covered a regrade was
covering nothing. It now uses `FinalOutcomePass`.

## 4. The fix

`OutcomeRules.ToGradeScale` maps the four final-scale values onto the common scale, and
`EffectiveOutcome` applies it before the value leaves. A value on neither scale passes through
untouched rather than defaulting, for the reason `TryGradeFromAnswer` refuses to default: the
most favourable grade is the one least likely to be questioned.

Commit `a1fe9e7`. 30 lines of production code across two files; 7 new tests. Suite: **834
passed, 0 failed** (827 before).

## 5. Deployed

```
pushassembly https://org0b075da8.crm11.dynamics.com/ .../bin/Release/net462/OutcomeTesting.Plugins.dll
pushed OutcomeTesting.Plugins (7b51d0d1-f5a1-f111-b8dd-e4fade069307): 224768 bytes,
modified 2026-09-16 12:38:22Z, version 1.0.0.0
```

Built `-c Release` immediately before pushing and the byte count checked against the file on
disk, because `pushassembly` uploads `bin/Release` without building it and will otherwise send
whatever was there last.

## 6. The audit alongside it

Every reader of the two outcome columns was checked for the same class of fault. The one
defect is the one fixed. Specifically clean:

- `ImportRules.IoOutcome*` (`1209106_1_x`, a **third** band) is used only for label lookup,
  never compared against a grade constant.
- `al_signoff.al_finaloutcome` is checked with `IsFinalOutcome` — same band, consistent.
- `NotificationOutbox.InitialOutcome` reads only the initial grade, which is correct: it runs
  at submit, before any regrade exists.

One tidiness item, not a defect and not changed here: `RegradeCasePlugin.ParseOutcome`
hardcodes `120910710`-`713` as literals rather than referencing the `OutcomeRules.FinalOutcome*`
constants. The values agree, so nothing is broken.

## 7. Still open

**IO-300001 has not been re-exported.** The assembly is in place and the gate will now pass it,
but generating an export batch writes a record for every closed case and marks the batch
Generated, so it was left for the owner to run deliberately.

**Notifications from `tc.outcometesting@ascotlloyd.co.uk` — not done.** There is no sender
address anywhere in the codebase by design (AGENTS.md rule 7); the sender is whatever account
the drain step is registered to run as, set by the run-as argument to `registerstep`. **No
systemuser with that address exists in DEV** — the only match on `%outcometesting%` is the
Power Pages application user. The account has to be created and its mailbox approved and
tested for server-side email (OD-030) before the step can point at it; re-registering against
a missing mailbox would land every notification at `Failed`. The drain step was left alone.

**Adam Strumidlo cannot yet sign in to TEST.** See the next section of the runbook work below.
