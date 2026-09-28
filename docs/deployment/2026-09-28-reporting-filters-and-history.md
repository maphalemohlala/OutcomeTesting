# Reporting filters, remediation by case, question and section history - DEV and TEST

**Date:** 2026-09-28
**Environments:** `Env_AQ_Dev` (`org0b075da8`) and `Env_AQ_Test` (`org37995f36`), both deployed
and verified.
**Commits:** `6471123` (question and section history), `fbc557c` (reporting, worklist, exports,
edit-modal overflow), pushed to `feat/change-batch-sep-2026`.

## What changed

Code App only. No plug-in, portal, table or data-source change.

- **Case worklist:** Remediation filter (Open / Complete / None raised) and column, per case.
  Complete means every action on the case is Completed - the sign-off rule.
- **Management reporting:** "Open remediation" read 84 on TEST and was taken as 84 cases. It
  was 84 actions - one per test point marked down - on 11 cases. The tile now leads with
  cases, actions beneath; a new "remediation complete" tile; both open the filtered worklist.
- **Dashboard and Management reporting:** imported from/to, route, adviser and checker filters,
  kept in the URL and carried into the worklist.
- **Exports:** a check-date range applied to every Trail Light download. The file name is
  unchanged. The records' Batch column, blank before, now reads the lookup annotation.
- **Edit case details:** Products no longer runs out of the modal.
- **Question Library:** History on every question and section - what changed, when, by whom.
  Questions are compared version to version; sections read the before/after
  UpdateSectionPlugin already writes. Names come from the lookup annotation, as `getAll` leaves
  `createdbyname` empty.

## Audit

| Check | Result |
|---|---|
| App unit tests | **1,148 passed** |
| `tsc -b` | clean |
| `eslint` on the changed features | clean (the known `UserPicker.tsx` error is untouched) |

## DEV

Pushed with `npx pa app push` after `npm run build`; seen in the browser as Service Account:
worklist Complete 5 cases, Open 1; Management reporting 1 case / 1 action open, 5 complete;
filters narrow as expected; Trail Light range 23 Sep gives 1 of 8 rows; edit modal overflow 0;
question and section history name the author.

## TEST - 1.0.12.0

DEV's solution bumped 1.0.11.0 -> **1.0.12.0** and exported managed through the Web API; staged at
`artifacts/2026-09-28-test-promotion/`. Its Code App bundle `index-DadIO9MX.js` is byte-identical
to the build of HEAD. Imported with `scripts/Import-Solution.ps1 -Managed` (run at the owner's
instruction; `--activate-plugins`, `--publish-changes`).

| Check | Result |
|---|---|
| Solution | 1.0.12.0 managed |
| Plug-in steps | **24 of 24 present and enabled** (the script's own verification) |
| Code App `appversion` | 2026-09-28T14:19:07Z (was 2026-09-25T08:18:18Z); serves `index-DadIO9MX.js` |
| Portal components vs DEV, by content | 297 identical. Differ as intended: TEST's five `Authentication/*` sign-in settings. Also a web role **"New Role"** exists only in TEST - not in the package, created there by hand; left alone |
| Code App in a browser | Management reporting **11 cases in open remediation, 84 open actions, 6 complete** - the figures a direct query gives; worklist Open 11, Complete 6; filters, Trail Light range, edit modal (overflow 0), question and section history all render |
| Portal in a browser (TEST session minted through SSO) | Cases, case detail and Remediation render signed in, no page errors |

**Not run on TEST:** the editable portal specs. Their review, `177db2f1`, was submitted on
2026-09-25 and now opens read-only; a fresh review assigned to Service Account is needed to run
`checker-feedback`, `checklist-gating` and `managed-lists` again. Nothing in this change touches
the portal.

TEST's `Q-TAX-01` history shows v1 created a few seconds *after* v2 and v3 (16 Sep, 18:04) - an
artefact of how TEST's versions were loaded. The history orders by version, so it reads
correctly; only the timestamps look odd.
