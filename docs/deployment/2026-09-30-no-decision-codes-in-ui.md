# No decision or requirement codes on screen - DEV

Date: 2026-09-30
Commit: `254d450`

## What was reported

"Most of the app messages show the decision and requirements code like AD_05 etc. Remove all that" (project owner).

## What changed

Codes such as AD-031, BR-004, FR-017, OD-018 and NFR-AUD-01 were removed from everything a user reads. They remain in code comments and the docs.

| Where | Count | Examples |
|---|---|---|
| Code App on-screen text | 33 | Page intros (Exports, Reports, Remediation, People, Roles), recheck and case-edit hints, the Trail Light download caption |
| Plug-in messages | 30 | Refusals ("A regrade must record why the outcome was changed"), case history lines ("queued automatically: route set"), the compiled email wording |
| Portal pages | 13 | Case Detail, Home, Remediation, Review Detail |

Most were a code in brackets at the end of a sentence, and were simply dropped. Seven sat mid-sentence and were reworded. For example, "and BR-004 requires it before AQS" became "and Tax must be completed before AQS".

`app/src/lib/noDecisionCodes.test.ts` scans all three places: the app's strings and JSX text, the plug-ins' string literals, and the portal's templates, snippets and scripts with comments removed. It fails if a code comes back. It was checked by planting a code in the app and in a plug-in: both were caught. Four plug-in tests that had asserted a code now assert the plain wording instead.

Tests: plug-ins 1754/1754, app 1217/1217, `tsc -b` clean.

## DEV

| Step | Result |
|---|---|
| Plug-in assembly | Pushed. sha256 `e9863e42...599ffe18` matches the DLL read back |
| OT Case Detail, OT Home, OT Remediation, OT Review Detail | DEV's copies were first confirmed identical to `main`, then pushed |
| Code App | Bundle `index-Cfg8omD4.js`, pushed |
| Saved email templates (`al_notificationtemplate`) | Three rows still carried codes: REVIEW-SUBMITTED (FR-017), REMEDIATION-OTHER (BR-006) and SIGNOFF-REJECTED (OD-018). Only the codes were removed from each row's current text, so any other edits are kept. Read back: none left |
| Live check | Home, Cases, Case detail, Review and Remediation, loaded as Service Account: no codes in the visible text |

**Not changed:** case history entries written before today keep the wording they were written with, codes included. Audit events are never edited.

**Not checked in a browser:** the Code App, because its sign-in has expired. The source scan covers it.

## TEST, when the owner says

1. The code, plug-ins and portal pages move with the next solution import.
2. TEST's saved email templates carry the same three codes. Removing them is a data write, so the owner runs it. It can run at any time, independently of the import:

   ```powershell
   $env:DOTNET_ROLL_FORWARD='Major'; & plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe webapimany https://org37995f36.crm11.dynamics.com '@artifacts\2026-09-30-notification-codes\strip-codes-org37995f36.json' artifacts\2026-09-30-notification-codes\out-test.json
   ```

   The file lives in `artifacts/`, which is git-ignored. It was built from TEST's current rows on 2026-09-30.

**Run by the owner, 2026-09-30.** All three updates succeeded. Read back from TEST: 12 template rows, **0** with a code. The three sentences now read "...is locked to further edits.", "...and assigned to you." and "...has restarted from today."
