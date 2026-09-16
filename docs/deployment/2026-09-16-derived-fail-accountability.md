# Fail accountability derives from the case, and OD-024's gate retires — DEV, 2026-09-16

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. Third deploy of the day. **TEST was not touched.**

Project owner, asked how to close the two accountability gaps:
*"The assigned paraplanner and adviser should be accountable respectively."*
Then, on whether that replaces the recorded judgement: **derived as a default, still
overridable**, with the pairing confirmed as paraplanner → File Quality, adviser → Advice
Quality.

---

## 1. The rule

| Discipline | Who carries a fail | Columns |
|---|---|---|
| File Quality | The paraplanner — they compile the file | 11-14 |
| Advice Quality | The adviser — they give the advice | 17-20 |

A recorded judgement still wins outright. `IsAccountable` consults the four flags first and
takes them exactly as recorded where any is set; only an untouched row derives. A row of four
falses is "nobody has said yet", not "nobody is responsible", so it derives — which means a
deliberate *nobody* is not expressible. It was not expressible before either: the OD-024 gate
refused a row that named no one.

A File Quality fail is read from the answer **value** rather than its label, so renaming the
option cannot quietly stop attributing anyone.

## 2. Why this closes the Tax gap

The four flags live on `al_outcome`, and a Tax review creates no such row — deliberately,
since `al_initialoutcome` carries only the AQS scale. So there was nowhere to record who
carried a Tax fail. 254397454 exported a file quality **Fail** with nobody named, and the gate
could not object because it only ever inspected an `al_outcome` row that did not exist.

Deriving from the case needs no Outcome row. Every case names an adviser and a paraplanner.

## 3. Why the OD-024 gate had to go with it

The gate refused a non-pass that recorded no accountability, on the grounds that four blank
pairs read as "nobody is responsible". A derived default cannot be blank on a fail, so the gate
could only ever have refused rows that are in fact complete — and since nothing in the app
calls `al_SetFailAccountability`, every outcome row in DEV has all four flags false, so it
would have refused all of them. Keeping it would have converted a working export into a
permanently blocked one.

It is removed as a consequence of the decision, not as an oversight. Three tests asserting the
refusal are retired; two of them become coverage of the rule that replaced it.

## 4. What DEV now exports

| Case | File Quality | Advice Quality | Paraplanner named | Adviser named |
|---|---|---|---|---|
| 254397454 | Fail | — | **yes** | no |
| IO-300001 | Fail | Pass | **yes** | no |
| IO-300005 | Fail | Pass | **yes** | no |
| 254398988 | Fail | Pass | **yes** | no |
| IO-SEED-TXA-01 | Fail | Pass | **yes** | no |
| IO-300004 | Pass | — | no | no |
| 254399947 | Pass | — | no | no |

No adviser is named anywhere, because every advice quality outcome in DEV is a Pass. That is
the rule working, not a gap.

## 5. Deployed

```
pushed OutcomeTesting.Plugins (7b51d0d1-f5a1-f111-b8dd-e4fade069307): 226304 bytes,
modified 2026-09-16 13:41:22Z, version 1.0.0.0
```

Built `-c Release` immediately before pushing, byte count checked. Suite: **848 passed, 0
failed**.

## 6. Known incoherence, not fixed here

`SetFailAccountabilityPlugin` still refuses a case whose effective outcome is a Pass —
*"This case passed, so there is no fail to attribute."* With the pairing above, a case can fail
on file quality while passing on advice quality, and that is **five of the seven closed cases
in DEV**. For those, the derived paraplanner is correct but an override cannot be recorded,
because the command refuses the case outright.

Closing it means the command consulting the file quality answer as well as the outcome, which
means sharing the Q-FQ-01 / Q-FQTAX-01 resolution that currently sits private to
`GenerateExportPlugin`. It has no practical effect while nothing in the app calls the command.

## 7. Still open

- The batch generated at 12:47 predates all three of today's deploys. A re-run is a new batch
  by design (AD-042).
- `al_SetFailAccountability` has no caller in the app. It is no longer a prerequisite for the
  export, which now attributes without it, but it remains the only way to override.
- AD-039 has no Tax outcome column, so `Q-TAX-02` still reaches Trail Light nowhere.
- `Env_AQ_Test` has none of today's four changes.
