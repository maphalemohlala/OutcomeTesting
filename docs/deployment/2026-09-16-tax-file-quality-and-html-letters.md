# The Tax file quality grade, and letters that can be clicked — DEV, 2026-09-16

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. Second deploy of the day; the first is
`docs/deployment/2026-09-16-regrade-pass-export-gate.md`. **TEST was not touched.**

Both changes came out of the project owner reading the export that the morning's fix had
finally let through.

---

## 1. Column 10 was blank for every Tax-only case

Reported as *"and file quality grades are blank as well"*, then, against the first answer,
*"Tax cases have outcomes as well pass or fail, check the tax check form"*. That correction
was right and the first answer was wrong.

`ResolveFileQualityGrade` matched only `Q-FQ-01`, the AQS file quality outcome. The comment
above it said both graded columns *"have no source and never will"* on a Tax-only case. That
is not true: `Q-FQTAX-01` is the Tax checklist's file quality outcome, on the same Pass/Fail
scale, and it is answered.

What DEV was actually exporting:

| Case | Route | Q-TAX-02 | Q-FQTAX-01 | Q-FQ-01 | Column 10 was | Column 10 now |
|---|---|---|---|---|---|---|
| 254397454 | Tax only | Insufficient evidence | **Fail** | — | *blank* | **Fail** |
| IO-300004 | Tax only | Pass | Pass | — | *blank* | Pass |
| 254399947 | Tax only | Pass | Pass | — | *blank* | Pass |
| IO-300001 | Tax only | — | — | Fail | Fail | Fail |
| IO-300005 | Tax then AQS | Insufficient evidence | Fail | Fail | Fail | Fail |
| 254398988 | Tax then AQS | Pass | Pass | Fail | Fail | Fail |
| IO-SEED-TXA-01 | Tax then AQS | Pass | Pass | Fail | Fail | Fail |

254397454 went to Trail Light with an empty File Quality column over an answered **Fail**.

`Remediation.cs` already held the right set — `Q-TAX-02`, `Q-FQ-01`, `Q-FQTAX-01`, described
there as *"the outcome question of each discipline"* — so only the export's resolver was
narrow. The AQS answer still wins where both disciplines graded the file, which is every
Tax-then-AQS case, and the Tax code is only queried when the AQS leg did not grade it.

Commit `5093f69`.

## 2. The three letters are HTML, with the link as a button

Reported as *"when messages are sent, the link comes as a plain text link and not a clickable
link"*.

The bodies were plain text and the case link a bare URL inside a sentence. All three now
render as HTML paragraphs with a styled anchor — "View the case" on the pass letter,
"Confirm remedial action" on the two remedial ones. The style is inline because email clients
drop a style element.

Escaping goes through the paragraph helper rather than sitting at each interpolation, so a
later edit cannot forget it. It has to cover the copy as much as the data: "T&C Manager" is
an unterminated entity written raw, and the adviser and client names are copied off the case —
a client called `<b>Smith & Co</b>` would have been markup in somebody's mailbox. The href is
escaped too, because a portal link joins its parameters with an ampersand.

Where the environment has no portal, `CaseLink` returns null and the button is omitted
entirely, exactly as the link sentence was before.

Commit `80ba8b5`. Suite: **845 passed, 0 failed**.

## 3. Deployed

```
pushed OutcomeTesting.Plugins (7b51d0d1-f5a1-f111-b8dd-e4fade069307): 225280 bytes,
modified 2026-09-16 13:18:38Z, version 1.0.0.0
```

Built `-c Release` immediately before pushing, byte count checked against the file on disk.

**The drain step is enabled and asynchronous in DEV, running as `svc automate aq`**, so DEV
genuinely sends these letters. The HTML change alters mail that actually goes out.

## 4. Not done, and why

**The generated batch still holds the old blank values.** `EXB-15f68b55-…` was generated at
12:47 before this deploy. `al_GenerateExport` refuses to re-run a batch that is not Draft, by
design (AD-042) — a re-run is a new batch, not a second pass over this one. A fresh batch is
needed to see the corrected column 10, and that is an operational act left to the owner.

**Column 15 still reports nothing for a Tax-only case, and that was left alone deliberately.**
It is the *Advice Quality* grade, and a Tax-only case has no advice quality assessment.
`Q-TAX-02` is a tax result on the three-value PassFailInsufficient scale (AD-055), not the
four-value BR-005 advice scale, so writing one into the other's column would put two scales
under one heading. The real gap is that **AD-039 has no Tax outcome column at all** —
`SubmitReviewPlugin` says so in as many words — which makes it a change to what Trail Light
receives, and an agreement with that consumer rather than a code decision.

**The accountability gate still cannot cover a Tax fail.** The four flags live on `al_outcome`,
and a Tax review creates no `al_outcome` row, deliberately: `al_initialoutcome` carries only
the AQS scale. For 254397454 there is nowhere to record who is accountable, so refusing the
row would block the batch without producing accountability. Closing it needs a tax outcome
column on `al_outcome`, or the flags moved off it, or a separate tax accountability record —
all schema changes.

**Underneath all three: `al_SetFailAccountability` still has no caller in the app.** Every
outcome row in DEV has all four flags false, because the only way to set them is to call the
API directly. Tightening any gate before that exists turns a working export into a
permanently blocked one.
