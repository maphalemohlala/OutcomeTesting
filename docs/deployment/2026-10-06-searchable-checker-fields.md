# Searchable checker fields on the edit case modal - DEV and TEST

Date: 2026-10-06. Branch `feat/otis-product-name`, commit `edf55c6`. Solution 1.0.22.0.

## What changed

The owner asked for the Tax and AQS checker fields on Edit case details to be searchable like
the adviser fields. Each field is now the same `UserPicker`: you type and the list narrows.
- It picks a person by contact id, so text that matches nobody allocates nobody. The empty box
  reads "Leave as it is — *current checker*".
- The allocation still sends the email to `al_AssignCase`.
- The picker no longer empties an id field's box when one character is deleted from a chosen
  name. This also fixes the fail accountability panel.
- The e2e helper `allocateCheck` now types the option text instead of choosing from a select.

Tests: 1,332 app tests passed, and `tsc -b` was clean. The e2e allocation was not run, because
the portal session had expired.

## DEV

`npm run build` produced bundle `index-D2kKsxe_.js`, and `npx pa app push` sent it. After the
push, DEV's `appversion` was 2026-10-06T07:20:29Z.

## TEST (`org37995f36`)

The owner said "push to test".
- DEV was bumped to 1.0.22.0. The membership audit found nothing missing.
- Export: `OutcomeTesting_1_0_22_0_managed.zip`, sha256 `7a472802...`. It carries
  `index-D2kKsxe_.js`.
- Import: `ImportSolutionAsync`, job `ef618833-2efa-4768-9969-8b6962ad1687`, async operation
  `aa3d1e24-58c1-f111-aaaf-6045bd0aeb46`. It succeeded (statuscode 30) at 07:33:43Z.
- Read back from TEST:
  - solution 1.0.22.0, managed;
  - assembly sha256 `583e8b98...`, unchanged;
  - all 67 steps enabled;
  - Code App `appversion` 2026-10-06T07:33:31Z.
- OT Review Detail on TEST now holds the current source, 255,727 chars, the same as DEV. The
  owner's push from the 1.0.21.0 release landed. Its `Active` layer remains, so the next change
  to that template needs another direct push to TEST.

PROD: not deployed. PROD is on 1.0.21.0.
