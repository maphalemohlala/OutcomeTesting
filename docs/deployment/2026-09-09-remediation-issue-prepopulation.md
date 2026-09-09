# Remediation "Issue / fail reason" prepopulated with the non-pass items — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Pushed by
`svc.automate.aq` through `plugins/OutcomeTesting.Registration` (Release build; the Release
binary had lagged the Debug one by a day and did not know `pushwebtemplate` until rebuilt).

## The direction

Project owner, 2026-09-09: "The Issue / fail reason fields on the remediation need to be
prepopulated with the items selected as non pass from the checks." Recorded as AD-097.

## What changed

| Where | Change |
|---|---|
| `Remediation.NonPassItems` | Reads every answer of Fail, Insufficient evidence, Potential harm or No on a question version in force today, in section then question order, as "question: answer"; then every File Quality fail point ticked on the review, once each, as "Fail point: category - reason". Labels come from `OptionLabels`, so no wording is hard-coded. |
| `Remediation.Describe(reason, observation, items)` | The description now opens with "Issues found on the check:" and one line per item, then the checker's observation, then the standing instruction. Without items it reads exactly as before, so a flagged Pass with nothing marked down is unchanged. |
| `Remediation.Raise` / `SubmitReviewPlugin.RaiseRemediation` | The items are gathered at submit time and written into `al_description`. Still one action per review (see AD-097 for why). |
| `OT Remediation`, `OT Case Detail` templates | `al_description` rendered with `newline_to_br` so the list reads as lines. |
| App: remediation tab, review detail | The issue cell keeps its line breaks (`white-space: pre-line`). |

Also carried in this push: the question-version effective-date rule in the plug-ins
(`ResponseRules.IsVersionEffective`, its use in `ResponseGuardPlugin` and
`SubmitReviewPlugin`), which had been pushed to DEV earlier today but not committed, and
commit b6fe804's 403 wording in the OT Remediation template, which had not been pushed.

## Verification

| Check | Result |
|---|---|
| `dotnet test` plug-ins | 546 passed, 0 failed (5 new: description order, empty list, items in checklist order with fail points, empty review, items written on raise) |
| App `vitest` / `tsc -b` / `eslint` | 249 passed; clean; 0 errors (2 pre-existing warnings on the remediation page) |

## What ran

| Step | Command | Result |
|---|---|---|
| 1 | `dotnet build plugins/OutcomeTesting.Plugins -c Release` | clean, 0 warnings |
| 2 | `dotnet $T pushassembly $U` | 174592 bytes, modified 2026-09-09 15:43:08Z |
| 3 | `dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000019 ".../ot-remediation/OT-Remediation.webtemplate.source.html"` | 30199 -> 31595 chars, 15:43:17Z |
| 3 | `dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000015 ".../ot-case-detail/OT-Case-Detail.webtemplate.source.html"` | 15923 -> 15939 chars, 15:43:25Z |
| 4 | `npm run build` then `npx pa app push` (app) | built clean, pushed successfully |

No step registration change: the raise runs inside the existing `SubmitReviewPlugin` step.

## Retest

1. Portal, as a checker: on an open AQS review answer at least one E-section question Fail,
   one AML question No, tick a fail point, set File quality outcome Fail, grade Potential
   harm, submit.
2. Portal, as the adviser: `/remediation` shows the new action with "Issue / fail reason"
   reading "Issues found on the check:" followed by one line per non-pass answer in
   checklist order, then the fail point lines, then the checker's observation.
3. App: the case's remediation tab and the review detail's remediation block show the same
   lines.
4. Existing actions are untouched: the description is written once, when the action is
   raised.

## Site cache

The Power Pages cache clear (`/_services/about` → Clear cache) is the project owner's step,
as before. Change tracking (AD-094) covers the next visit either way.
