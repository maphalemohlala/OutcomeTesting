# The tax check's wording, a block's own scale, and the date of meeting — AD-145 to AD-147

**2026-09-19.** Items 1 and 9 of the September fixes batch, deployed to `Env_AQ_Dev` only.
Every claim below was verified by query after the fact, not from a command's exit code.

---

## 1. Reported

> Change a question's type from Yes/No to Pass/Fail/Insufficient Evidence. The question then
> shows Pass/Fail/Pass with Issues.

The response type was right. `ResponseRules.PermittedChoices` was right. The values saved
were right. Only the words drawn over them were wrong — which is the worse half to get
wrong, because nobody reading the screen can tell.

## 2. Root cause — AD-145

Each surface has **two** option-rendering paths, a grid one and an inline one, and only the
grid ones tested the review's discipline.

| Path | Before |
|---|---|
| Portal grid — `OT Review Detail` | gated on `rv.al_reviewtype.value == 120910200` — correct |
| Portal inline — `OT Answer Options` | ungated: `120910006` always drew `PASS, PASS WITH ISSUES, FAIL` |
| Code App grid — `optionsFor(rt, isTaxReview)` | gated — correct |
| Code App inline — `inlineOptionsFor(rt)` | no discipline argument at all |

AD-055 amended reworded `120910302` to "Pass with issues" for the Tax check alone, and put
the rename on the inline path rather than on the scale — correctly, since `120910006` is
shared with S-E1 to S-E5 and S-CRP. That placement rested on the inline path being
Q-TAX-02's alone. **AD-123 checklist administration had already made that false.**

Why a type change is what surfaces it: a section renders as a grid only while its questions
agree on one scale. Retype one and the section falls to the inline path — which applied a
tax rename to an AQS question.

**The consequence that matters:** a checker on an AQS review who ticked what read "PASS WITH
ISSUES" saved `120910302` — Insufficient evidence, a non-pass that raises remediation. Those
answers are reported, never rewritten.

## 3. What the screenshot then showed — AD-146

With the labels fixed, the project owner's retyped AML/CRA section still headed itself
**Yes / No / N/A**, describing only the two questions that had not moved.

A seeded block names its scale and tick columns in code rather than deriving them, so the
reference document can pin them (AD-098, AD-104). That holds only while the questions still
answer on it. The declared scale is a default, not a promise.

Now: questions that agree re-head the grid on their own scale; questions that disagree take
the block to a meta table where each row draws its own options and no column header can
misdescribe it. Only a declared **grid** is re-read, so S-TAX and S-GRADE — which declare no
scale — cannot reach it and the pinned reference blocks stay put.

| S-AMLCRA's questions | Headers |
|---|---|
| all on `120910008` | Yes / No / N/A — the declared path, unchanged |
| all on `120910006` | Pass / Fail / Insufficient evidence — derived |
| mixed | none; a meta table |

Four blocks have declared headers and so are subject to this: **AML and CRA**, **Suitability
core checks** (S-E1…S-E5), **Centralised Retirement Proposition**, **Consumer Duty overlay**.
Suitability is one block built from five sections, so one section leaving the scale takes the
whole table off its headers — it cannot head half of itself.

### A regression caught before it shipped

The first cut of this rule tested **every** row against the declared scale. **S-E2 carries
its outcome-lens tick on `120910007`**, so Suitability was taken off its grid on the strength
of a row that was never in it. It survived only because the block opens at S-E1 and captures
`blk_layout` there — a change of section order would have broken the suitability table
outright. Both surfaces now read the **tick-scale questions and those alone**. Commit
`309ce88`.

## 4. Item 9 — the date of meeting, AD-147

`al_advicedate` now reads **"Date of meeting - Client contact"** on every surface. Display
name only: schema name, type, DateOnly behaviour and required level are untouched, so
everything that names the column keeps working.

The metadata change needed a verb that did not exist. `setcolumnlabel` retrieves the
attribute and sends it back with only the label replaced — an `UpdateAttributeRequest`
carrying a partly-filled attribute overwrites what it omits, which would have silently reset
the behaviour.

**The date can never be in the future.** The rule sits on `CaseHeaderRules.ValidateAdviceDate`
and both front ends reach it through `UpdateCaseDetailsPlugin.ApplyFields`, which
`CaseHeaderRequestPlugin` already called — **one enforcement point for the Code App and the
portal**, rather than the matched pair that has needed correcting twice this week.

Compared on the date and in the **UK's** day. `al_advicedate` is DateOnly, so there is no
time of day in the value; the only clock in the comparison is the one deciding what today is,
and at 23:30 UTC on a British Summer Time evening it is already tomorrow in London. That day
was already defined for the BR-010 remediation clock (OD-018), so `Remediation.UkDate` now
delegates rather than keeping a second copy.

### Two questions the batch asked, answered by looking

- **Trail Light: no change, and none was possible.** It carries *Check date*, not this
  column. The fixed twenty-column contract was never at risk.
- **The import needs nothing.** The live IO task extract carries no advice date at all.
  `data/outcome-case-upload-sample-valid.csv` has an "Advice date" header but is a **stale
  sample from a superseded format** — it implies an import path that does not exist.

### Caught in audit, not in use

The panel read the date off the form and validated it whatever the user had touched. The
command does not — `ApplyFields` walks the changed fields alone. A case already carrying a
future date could not have been edited **at all** in the Code App: changing the client name
would have been refused over a value nobody touched, and refused by the page alone. Commit
`8f679da`.

---

## 5. Deployed to Env_AQ_Dev

| What | Evidence |
|---|---|
| Plug-in assembly | 241,152 bytes, built `-c Release` immediately before the push, byte count matched on arrival |
| Dataverse | `al_outcomecase.al_advicedate`: `'Advice date' -> 'Date of meeting - Client contact'`, read back after the publish |
| Portal | 7 × "Date of meeting", `ukToday`, `data-ot-no-future` present; **0 × "Advice date"** — queried from `powerpagecomponent` |
| Portal | `is_tax` = 8, `is_tax_review` = 4, `known and layout` = 1, `sec_other` = 0 |
| Code App | bundle `index-CWTHwghs.js` |
| `src/` | round-tripped; assembly 241,152 bytes matches the push |

Tests: **548** app (`tsc -b` clean), **981** plug-in. The C# and TypeScript suites cover the
same date cases deliberately, so a change made to one and not the other shows up as two
suites disagreeing rather than as a rule holding on one surface only.

---

## 6. Still open

**A regression this deployment caused, fixed the same hour.** The first portal upload pushed
a **stale local `sitesetting.yml` over newer DEV state**: `Webapi/contact/fields` listed four
trigger columns where DEV had five, and `al_accountabilityrequest` — which
`OT Review Detail` writes from the accountability card — was dropped. Restored, re-uploaded,
verified. Commit `58330b8`.

The cause is worth keeping: **the accountability work was configured directly in DEV and
never written back to `powerpages/`**, so this repo's portal source had been a column short
since then. Anything else configured that way is still only in DEV, and the next upload will
overwrite it the same way. Found by diffing the round-trip, not by the upload, which reported
success.

**`outcome-testing.css` has not been deploying.** Every upload fails one record:

```
Updating table powerpagecomponent with record ID:f065878a-… FAILED
due to Entity 'powerpagecomponent' With Id = f065878a-… Does Not Exist
```

The local manifest holds a record id the environment no longer has. Any CSS change since that
id went stale never reached DEV. Not touched here; unrelated to these commits.

**`OT Tax Notes` is missing from DEV.** The template's source is still in this repo and both
`OT Review Detail` (line 262) and `OT Remediation` (line 356) include it by name, so the Tax
notes panel renders as nothing. It was already absent before this deployment — uploads do not
delete.

The cause: its yml claims seeded id `…000022`, which the site manifest assigns to **OT Case
Detail Page**. Under the Standard Data Model web pages and web templates were separate tables,
so the same seeded GUID was legal in each; under the **Enhanced Data Model both are rows in
`powerpagecomponent`** and collide. No record holds `…022` now, and the upload will not create
it — `--forceUploadAll` processed 300 records and still did not. The fix is one line: give it
a fresh id in `OT-Tax-Notes.webtemplate.yml`. Every id in the seeded `a1000000-…` range is
taken, so the choice breaks a convention the project maintains deliberately and is left for
the project owner.

**`app/.power/schemas/dataverse/outcomecases.Schema.json` still reads "Advice date".** A
generated build artifact; nothing renders from its `title`. It will correct itself the next
time the data source is regenerated, which was not worth the unrelated churn mid-deployment.

**Two AML/CRA questions are still on Yes / No / N/A.** The project owner asked for all five on
Pass / Fail / Insufficient evidence; three have moved. Until the last two do, the section
renders as a meta table with no headers — which is correct for a mixed section, and not the
end state asked for.

**The data audit owed from item 1.** AQS responses holding `120910302` against a `120910006`
question drawn inline — checkers who ticked "PASS WITH ISSUES" and saved Insufficient
evidence. To be reported, not rewritten. Not yet run.
