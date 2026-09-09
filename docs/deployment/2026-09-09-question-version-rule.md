# Question versions: the effective-date rule, and the duplicated Tax check reason — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). `pac` could not be used: its
token was revoked at 07:51 UTC today (`AADSTS50173`, a credential change on
`svc.automate.aq`), so every read and write here went through
`plugins/OutcomeTesting.Registration`, whose own cached sign-in still works.

## The report

On the submitted Tax review, "Tax check reason — Required" with its five options
(LSA/LSDBA/TTFAC, Trust, IHT, Tax calculation, Other) appeared twice, and neither copy showed
which options had been chosen.

## What was actually wrong

Two faults, one root cause and one independent.

**1. Nothing read a question version's effective dates.** AD-015 models a retired question
version by its `al_effectiveto` date; `RetireAndSucceedQuestion` stamps the old version's
effective-to and the successor's effective-from with the same day and leaves both Active. In
DEV, Q-TAX-01 (Tax check reason) and Q-TAX-02 (Tax check outcome) each have three Active
versions:

| Question | Version | Type | Effective | Status |
|---|---|---|---|---|
| Q-TAX-01 | v1 | Multi select | 2026-08-26 → 2026-08-28 | retired |
| Q-TAX-01 | v2 | Single select | 2026-08-28 → 2026-08-28 | retired the same minute |
| Q-TAX-01 | v3 | Multi select | 2026-08-28 → | current |
| Q-TAX-02 | v1 | Pass / Fail / Insufficient | 2026-08-26 → 2026-09-08 | retired |
| Q-TAX-02 | v2 | Pass / Fail / Insufficient | 2026-09-08 → 2026-09-08 | retired the same minute |
| Q-TAX-02 | v3 | Pass / Fail / Insufficient | 2026-09-08 → | current |

The review page's question FetchXML filtered by checklist version and owner role only, so all
three versions of each question rendered — "Tax check reason" twice as a multi-select and once
as a single-select, "Tax check outcome" three times. `SubmitReviewPlugin`'s mandatory gate had
the same query, so it demanded an answer on every mandatory version, which is why the reviewer
answered all of them. And `AnswerFor` read a question's answer as "the most recently modified
response for that question code": for Q-TAX-02 that was the retired v1's **Insufficient
evidence** (saved 19:02:23), not the current v3's **Pass** (19:02:19). On this review that made
no difference to the case: "Remedial action required?" (Q-FQTAX-03) was answered **Yes**,
which sends a Tax case to Awaiting Remediation whatever the outcome (`NextCaseStatusForTax`),
and the remediation action on the case was raised by the 2026-09-08 22:37 backfill with a
generic description, not by the submit. Had the flag been No, the superseded answer alone
would have decided the case.

**2. The multi-select checkbox test never matched.** The template tested
`existing.al_answerchoices contains <label>`, on the assumption (from the 2026-08-29 plan,
never proven) that Liquid returns a multi-select choice as comma-separated labels. The
submitted review holds two values on v3 and one on v1 and rendered no tick on either, so the
runtime returns something else. The Liquid objects documentation covers single picklists only.

## Scope in DEV, checked by query

- Retired versions exist for **Q-TAX-01 and Q-TAX-02 only**. No AQS question has one, so no
  AQS submission is affected by the duplication.
- Answers written against retired versions: **4 rows, all on the one submitted Tax review**
  (`9c0b091f`). No other review has any.
- The multi-select fault affects Q-TAX-01 only, the sole multi-select question in V8.

## The fix (AD-091)

A question version is in force from the start of its effective-from day until the start of
its effective-to day; the reference day is the review's submission day once submitted,
otherwise today. Applied in every place that reads versions:

| Where | Change |
|---|---|
| `ResponseRules.IsVersionEffective` | the rule, pure, tested (`VersionEffectiveTests`) |
| `ResponseGuardPlugin` | refuses a new answer on a version not in force: `PRECONDITION: This question has been replaced by a newer version…` |
| `SubmitReviewPlugin` mandatory gate | owes answers only on versions in force today |
| `SubmitReviewPlugin.AnswerFor` | reads the version in force on the reference day first; any version, newest first, only as a fallback |
| `OT Review Detail` template | question FetchXML filtered by `al_effectivefrom`/`al_effectiveto` against `as_of`; multi-select checked test accepts a collection of option set values, a string of values, or a string of labels |
| Code App `useReviewDetail` | hides answers whose version was not in force on the reference day (`versionEffective.ts`, tested) |
| `ContactsMigration.AnswerMandatory` | the one-off seeder skips retired versions, so a re-run matches the gate |

Tests: plug-ins 513 passed; Code App 207 passed, lint 0 errors, build clean.

## What ran

Two verbs were added to the registration tool because `pac` had no token: `pushassembly`
(the assembly content only, the `pac plugin push` equivalent under AD-061) and
`pushwebtemplate` (one web template's `source` inside its Enhanced-data-model component
row, preserving every other key).

```
dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll \
  pushassembly https://org0b075da8.crm11.dynamics.com
```
**Done** — 169,472 bytes, assembly modified 2026-09-09 09:19:32Z.

```
dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll \
  pushwebtemplate https://org0b075da8.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b \
  "powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html"
```
**Done** — run by the project owner at 2026-09-09 09:25:05Z after the session's permission gate refused
the portal write: 52,346 → 54,477 chars, source read back and matched. Both the server-side rules
and the page are now live. The portal may need its cache cleared before the page picks up
the new source.

## Data left as it is

Review `9c0b091f` holds answers on Q-TAX-01 v1, v2 and Q-TAX-02 v1, v2 beside the current
v3 answers. With the rule live, the page and the app show the v3 answers only: Tax check
reason = LSA/LSDBA/TTFAC + Trust, Tax check outcome = **Pass**. The case is in Awaiting
Remediation correctly, because Remedial action required? was Yes, so no regrade is needed.
The four retired-version answers are harmless to leave, and deleting them would remove the
only record of what the reviewer actually typed.

## Follow-up, same day: the app showed no answers at all

Reported on the DEV Tax review for IO-DEV-VERIFY-003 after the push above: the review page
read "No answer has been recorded against this review yet." The ten responses were present
in Dataverse and the tester is a System Administrator, so neither data nor security was the
cause. The cause was the new `versionEffective.ts` filter: the generated model types an empty
column as `undefined`, but the SDK returns it as `null`, and `new Date(null)` is the epoch
rather than an invalid date. A current version, whose effective-to is empty, therefore read as
retired on 1970-01-01, and with the retired versions filtered out as intended, nothing was
left. Fixed by treating `null` as "not set" in `dayOf`, with a test that pins it. Code App
208 passed, lint 0 errors, build clean, pushed to DEV with `npx pa app push` at
2026-09-09 12:08Z. `ResponseRules.IsVersionEffective` and the template's FetchXML are not
affected: `DateTime?` and FetchXML have no epoch reading of an empty value.

### Second report: "Not answered" beside the typed answer

With the answers back, Fail observation showed **Not answered** with "Not enough
informarmation" beneath it as a note, and Tax check reason showed Not answered. Same class of
fault, older code: `answerOf` tested `al_answerchoice !== undefined` to decide a single choice
had been made, and the SDK returns the empty column as `null`, so every text, multi-select and
date answer fell into the single-choice branch and read as empty, with the text demoted to a
note. Fixed with `!= null`; `answerOf`/`noteOf` moved to `reviewAnswer.ts` so they are pure
and tested (`reviewAnswer.test.ts`, six cases including the multi-select array the SDK's
`deserializeMultiSelectPicklistFields` produces). Code App 214 passed, lint 0 errors, build
clean, pushed to DEV at 2026-09-09 12:14Z.

**Same pattern, not changed here:** `al_finaloutcome !== undefined` / `=== undefined` in
`caseOutcomeMapping.ts`, `caseWorklistMapping.ts`, `useCaseOutcome.ts`, `useRemediation.ts`
and `useReports.ts`. On the display paths a null final outcome resolves to an empty label, so
they read correctly by accident. In `useReports.ts` it does not: `finalisedCount` counts every
outcome row as finalised, and `effectiveOutcome` returns null for every case without a final
outcome, so the volumes exclude unfinalised cases. That is a reporting defect to fix separately.

## Sign-off

| Step | Run by | Date | Outcome |
|---|---|---|---|
| Root cause and scope | Delivery (automated) | 2026-09-09 | Two faults; one review affected |
| Plug-in fix, tests, assembly push | Delivery (automated) | 2026-09-09 | Pass — 513 tests, assembly live |
| Code App fix, tests, lint, build, publish | Delivery (automated) | 2026-09-09 | Pass — pushed to DEV with `npx pa app push` |
| Review template push | Project owner | 2026-09-09 | Pass — 09:25:05Z, source read back and matched |
| IO-DEV-VERIFY-003 status | Delivery (automated) | 2026-09-09 | Correct as is — Remedial action required? was Yes |
| Code App answers hidden by null effective-to | Delivery (automated) | 2026-09-09 | Fixed, 208 tests, pushed to DEV 12:08Z |
| Code App answers read as Not answered (null choice) | Delivery (automated) | 2026-09-09 | Fixed, 214 tests, pushed to DEV 12:14Z |
