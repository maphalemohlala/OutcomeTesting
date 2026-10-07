# Next-step messaging and sign-off notes - DEV, TEST and PROD (1.0.25.0)

Date: 2026-10-07. Branch `feat/otis-product-name`, not yet committed. Solution 1.0.25.0.
PROD went from 1.0.23.0 straight to 1.0.25.0, so it also received the 1.0.24.0 audit fixes in
`2026-10-06-duplicate-emails.md`.

## What changed

The owner reported that "the wording sometimes suggests that a case is going to T&C or adviser
even when that isn't the case". The pages predicted the destination; now they read the case's
status back from Dataverse after the write and report it.
- **OT Review Detail, after a submit:**
  - "The case passed and is now closed.", or "...goes back to the queue for its AQS check.",
    or "...goes to the adviser to put right the points you marked down."
  - The T&C Manager line now reads as a permission: "only a T&C Manager can reopen or
    regrade it". The same wording is on the locked notice.
- **OT Remediation, after the adviser signs off:** "now with your T&C Manager for sign-off",
  "this grading needs no sign-off, so the case is now closed", or "other remedial actions are
  still open". The hint and the two "answered at sign-off" cells follow `ot_signoff_due`.
- **The page's copy of the sign-off rule:** it now counts an AQS check still owed as due
  (route `al_requiresaqsreview` with no `al_outcome` yet), as
  `CompleteRemediationPlugin.SignoffRequired` does.
- **OT Remediation, after a T&C decision:** an approval says closed, at Awaiting Recheck,
  back to the queue for AQS, or other actions still waiting, from the live status.
- **Code App:** "awaiting supervisor" shows only while the case is at Awaiting Sign-off.

The owner also asked for the notes to show on the remediation and case details:
- **T&C sign-off notes** (`al_signoff.al_notes`) were shown nowhere. They now appear beside each
  decision and in the supervisor row on OT Remediation, OT Case Detail, the Code App
  remediation page and the Code App case page.
- **Adviser notes** were already on both portal pages. In the Code App:
  - the case page gained "Action performed" (with the note) and "Sign-off" columns;
  - the remediation page no longer hides an approved check on a Tax-then-AQS case. It is drawn
    with "(approved)" in its heading, as the portal has done since 2026-09-11.

Tests: 1,364 app tests passed, 33 of them new, and `tsc -b` was clean. The Liquid tag balance
was checked statically. The pages were **not rendered live**, because the portal session and
its Entra cookies had expired and `npm run e2e:auth` is interactive.

## DEV (`org0b075da8`)

- `pushwebtemplate` for OT Review Detail, OT Remediation and OT Case Detail. DEV's copies matched
  git HEAD first.
- Code App `index-DaHH9aQc.js`.

## TEST (`org37995f36`)

- The DEV membership audit was clean, and DEV was bumped from 1.0.24.0 to 1.0.25.0.
- Export: `artifacts/2026-10-07-messaging-and-notes/OutcomeTesting_1_0_25_0_managed.zip`,
  sha256 `bb008661...`. It carries DLL `cd02037c...`, the bundle and all three templates.
- Import: job `68d2f18d-220e-49db-81e6-2351f3ff0e03`, async operation
  `c76accce-1fc2-f111-aaaf-6045bd0aeb46`. It succeeded at 07:23:17Z.
- Read back from TEST:
  - solution 1.0.25.0;
  - assembly `cd02037c...`;
  - all 67 steps enabled;
  - Code App 07:22:55Z.
- OT Review Detail had an `Active` layer, so it was pushed directly after the import. All three
  templates now match source. The template rows already held the new wording from 1.0.24.0.

## PROD (`org3461d426`, OTIS)

- Package: `OTIS_1_0_25_0_managed.zip`, from `brandpackage` (7 labels rewritten). sha256
  `91e7d96f...`. It carries the same DLL.
- Before the import: PROD was at 1.0.23.0 OTIS, with no steps disabled and only the managed layer
  on all three templates.
- Import: job `fbbdbc7a-eaab-4f89-a6ec-ac9a6803c7a1`, async operation
  `be07385d-20c2-f111-aaaf-7c1e5279965e`. It succeeded at 07:27:46Z.
- Read back from PROD:
  - solution 1.0.25.0, managed, OTIS;
  - assembly `cd02037c...`;
  - all 67 steps enabled;
  - Code App OTIS 07:26:58Z;
  - `al_caseassignment.al_reviewinstanceid` exists;
  - all three templates match source.
- Template rows: they still held the 1.0.23.0 bracket wording, unchanged since 2026-10-06
  17:28Z. `2026-10-06-check-named-templates.json` returned three 204s, then the new wording read
  back.

## Still to do

- Render OT Review Detail, OT Remediation and OT Case Detail once a portal session exists again
  (`npm run e2e:auth`, interactive).
- None of the new sign-off rules (one letter per sitting, the AQS-next letter, the case lock)
  has been exercised live in any environment.
