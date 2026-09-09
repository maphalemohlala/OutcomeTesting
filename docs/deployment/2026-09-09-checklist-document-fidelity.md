# The checklist as the document draws it - 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Pushed by `svc.automate.aq`
through `plugins/OutcomeTesting.Registration`. Commit `0b304dc`, branch
`fix/portal-signin-identity-username`. Decisions AD-100, AD-101.

## The report

Project owner, 2026-09-09: "Ensure that the checklist form matches what we have in the pdf
exactly", "remove the Evidence or observation fields on every fail as it is not on the form or
a requirement", and then, on ownership: "Tax Sections: Tax Check, File quality Outcome Tax and
AQS (each time needs its own), Fail quality reasons (Both Tax and AQS). Everything else is
AQS." A Power Pages build spec transcribing the document was supplied the same day.

## What was wrong

1. **Fail reasons were scoped by category** - Tax check to the Tax team, the other three to
   AQS. That was an inference from the prefixes, not something the document says, and it lost
   data at the edge: a Tax reviewer who found a record-keeping failure had nowhere to record
   it. AD-100.
2. **A per-answer "Evidence or observation" box** opened under every non-pass answer, writing
   `al_answertext` beside the choice. It was never on the form.
3. **Presentation drifted from the document** - inline outcome scales in title case where the
   document sets them in upper case, the tax check outcome in the grid's order rather than the
   document's, Primary root cause as one run rather than a 3x3 grid, round radios, row-only
   cell rules, the whole lens row italic, the portal's fail points as a flat checkbox list. No
   print stylesheet, despite a "Save as PDF" button. AD-101.
4. **Three seeded fields did not match the document**, found by auditing column metadata
   rather than only the seeded rows.

## What changed

| Where | Change |
|---|---|
| `checklistForm.ts` | `failPoints()` drops the category filter and its `reviewType` parameter. New `inlineOptionsFor()` (document casing and order, inline path only) and `optionGridColumns()` (3 for root cause). |
| `ReviewDetailPage.tsx` / `.css` | Inline vocabulary, 3x3 root cause, lens label emphasis, ruled cells; the note span removed. |
| `reviewAnswer.ts` / `useReviewDetail.ts` | `noteOf()` and `ReviewResponse.note` removed. |
| `OT Review Detail` | Note block, reveal rule, `NON_PASS` and the note write path removed; fail points as the document's two-column table; the `failreasons` fetch no longer filters by category; lens label emphasised. |
| `OT Answer Options` | Upper-case inline labels, tax check outcome ordered PASS / INSUFFICIENT EVIDENCE / FAIL, `data-ot-columns="3"` on the root cause list. |
| `OT Layout` | Stylesheet cache-buster `?v=7` to `?v=8`. |
| `outcome-testing.css` | Square tick boxes scoped to `[data-ot-questionnaire]`, ruled checklist cells, root cause grid, lens label, and an `@media print` block. |
| `ResponseRules.cs` / tests | Comment only. `ValidateAnswer` still accepts text beside a choice so a review answered under the old page stays saveable. **No assembly push needed.** |
| `data/v8-seed/data.xml` | S-E4 name to a hyphen; S-CRP gains its intro line; S-CD help text in full including the broken "section H" reference; Q-CRP-04 `al_Name` to the document's full wording. |
| `knowledge/checklist-v8.md`, `knowledge/decision-log.md` | Column audit, presentation rules, AD-100, AD-101. |

## Verification

| Check | Result |
|---|---|
| App `vitest` | 264 passed (7 new) |
| App `tsc -b --noEmit` | clean |
| App `eslint` | 0 errors (9 pre-existing warnings, in untouched files) |
| Plugin `dotnet test` | 554 passed |
| Liquid tag balance, OT Review Detail | if 60/60, for 11/11, capture 3/3, unless 7/7, case 2/2, fetchxml 9/9, block 1/1 |
| Seed XML parses | 12 sections, 45 questions, 45 versions, 20 fail reasons |

## What ran

| Step | Command | Result |
|---|---|---|
| 1 | `npm run build` (app) | built clean, 3.26s |
| 2 | `npx pa app push` | pushed successfully |
| 3 | `pushwebtemplate ... 00000000001b` (OT Review Detail) | 77071 to 74365 chars, 17:44:04Z |
| 4 | `pushwebtemplate ... 000000000021` (OT Answer Options) | 2459 to 3624 chars, 17:44:19Z |
| 5 | `pushwebtemplate ... 000000000010` (OT Layout) | 1874 to 1823 chars, 17:44:45Z |
| 6 | `pushwebfile ... 000000000050` (outcome-testing.css) | 37904 bytes, 17:45:27Z |

Site cache clear (`/_services/about`, Clear cache) is the project owner's step. The stylesheet
is a web file and its `?v=` moved, so a hard refresh should not be needed, but the cache clear
is.

## Seed import - run by the project owner

`importseed` is a `--confirm` data write and this session's permission gate refuses those, as
it did for `queueroutedcases`, so the command was handed over. The project owner ran it:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.dll importseed https://org0b075da8.crm11.dynamics.com data\v8-seed --confirm https://org0b075da8.crm11.dynamics.com
```

Result: **0 created, 124 updated** - the shape expected of a seed keyed on the code alternate
keys, which updates in place rather than adding rows.

Confirmed live by `fetch` afterwards. Every record kept its GUID, so these were updates and not
replacements:

| Record | Before | Now | |
|---|---|---|---|
| `al_section` S-E4 `al_name` | `... Consumer Duty [em dash] Price & Value` | `... Consumer Duty - Price & Value` | ok |
| `al_section` S-CD `al_helptext` | `Short yes/no judgements only.` | `Short yes/no judgements only. Record any detail once in section H.` | ok |
| `al_section` S-CRP `al_helptext` | *(empty)* | `Complete this section where retirement income planning or decumulation advice is in scope.` | ok |
| `al_question` Q-CRP-04 `al_name` | `Annuity / drawdown discussion completed` | the document's full 120-character wording | ok |

No duplicates: active `al_section` count is 12 and active `al_question` count is 45, unchanged.

DEV now matches the repository. Every part of this change is live.

## Retest

**Both disciplines:** the File Quality - Fail points block is a two-column table headed
`File Quality fail reason | Tick`, with **all twenty** reasons, on a Tax review as well as an
AQS one. Tick one on a Tax review and one on an AQS review of the same case: each holds on
reload and neither disturbs the other.

**Anywhere a tick appears:** the box is a square, not a round radio, and no "Evidence or
observation" textarea sits under any answer.

**Tax check section:** the outcome row reads `PASS  INSUFFICIENT EVIDENCE  FAIL`, in that
order.

**File Quality Outcome:** `PASS  FAIL` and `YES  NO` in upper case.

**Checker judgement and grading:** the grade reads
`PASS  PASS WITH ISSUES  INSUFFICIENT EVIDENCE  POTENTIAL HARM`; Primary root cause is three
rows of three, title case.

**Suitability core checks:** grid columns stay title case (`Pass | Fail | Insufficient
evidence`); each lens row reads *Outcome lens:* in italic followed by the question upright.

**Print:** on a submitted review, "Save as PDF" produces the checklist without site chrome,
breadcrumb or save-status lines, and the ticks are visible in the preview.

## Known gaps carried forward

- **E2's stray lens tick box** is on the document and not built. Recorded as a formatting
  artefact; the build spec asks for it to be rendered and queried. Needs an owner decision - it
  is a new question, and the owner's standing instruction is not to add fields.
- **Fail point 20's en-dash** where the other nineteen use a hyphen. Reproducing it needs a
  per-reason separator column; all twenty labels otherwise match verbatim.
- **`al_TaxCheckRequired`'s label** is `Yes` where the document reads
  `Yes - complete tax check section`. That is an option-set label every consumer reads, so it
  was left for the owner.
- **S-CRP still renders on every AQS review**; OD-016 still gates the applicability mapping.
- **Tax reviews submitted before this push** were answered while only two fail reasons were on
  screen, so their fail points may be under-recorded. Nothing was deleted - ticks are additive
  and `ReconcileFailReasons` only touches rendered ids - but those reviews were never offered
  the other eighteen. Worth a spot-check if any Tax review has been signed off.
