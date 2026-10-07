# Duplicate emails and the remediation link - DEV, TEST and PROD

Date: 2026-10-06. Branch `feat/otis-product-name`, not yet committed. Solution 1.0.23.0.

## What changed

Users reported getting duplicate emails. The notification templates were not the cause. DEV,
TEST and PROD each hold only the built-in rows, with no custom letters and no recipient
overrides, so no event sends two templates. The outbox showed two other causes.

1. **One sign-off letter per action.** The portal signs a check's actions off one at a time, and
   the letter was keyed on each sign-off row. On TEST on 2026-09-30, case 256643210 sent its
   adviser 12 identical "Remediation approved" emails in 21 seconds. Now:
   - an approval sends one letter, from the sign-off that finishes the check;
   - a rejection sends one letter, from the rejection that sends the check back. Later
     rejections in the same sitting send nothing more, and their notes stay on the
     remediation page.
   See `SignoffProgressPlugin.EarnsSignoffLetter`.
2. **Tax and AQS letters that read the same.** A Tax-then-AQS case is allocated and submitted
   once per check, and the two letters had identical subjects. The 13 such pairs on TEST read as
   duplicates. A new `{{check}}` token gives "Tax check", "AQS check", or "check" where it cannot
   be told:
   - ALLOCATION and ALLOCATION-NO-LINK: "Case X has been allocated to you (AQS check)";
   - REVIEW-SUBMITTED: "Review submitted on case X (AQS check)".

   The allocation finds its review from the assignment code, which carries the first 12 hex
   digits of the review id.
3. **The remedial letter's button opens the remediation.** It now links to
   `/remediation?case=<id>` instead of `/case-details?id=<id>`.

The Code App's template editor mirrors the new wording and token.

Tests: 1,809 plug-in tests and 1,332 app tests passed, and `tsc -b` was clean.

## DEV (`org0b075da8`)

- The plug-in Release build was pushed with `pushassembly`: 383,488 bytes, sha256
  `8c3d102e...`. DEV's `pluginassembly.content` hashes the same.
- The three template rows were updated with `docs/deployment/2026-10-06-check-named-templates.json`.
  Before the update they held the original wording, unedited. The server-side guard accepted
  `{{check}}`.
- The Code App was built (`index-DzTEEjH0.js`) and pushed with `npx pa app push`.
- Proved live: `al_AssignCase` allocated case 900000005 to Service Account (AQS review
  `f148036e-...`). The queued letter's subject was "Case 900000005 has been allocated to you
  (AQS check)".
- The sign-off change was not exercised live. Its five tests drive `SignoffProgressPlugin.Progress`
  through a three-action check.

## TEST (`org37995f36`)

- The owner ran the template update first, before the import. TEST's guard refused all three
  rows ("does not supply {{check}}"), and nothing was written.
- Membership audit on DEV found no gaps:
  - 298 portal components;
  - 32 `al_` custom APIs, with 140 parameters and 115 response properties;
  - 67 steps, none disabled;
  - the Code App, the site, its language and the assembly;
  - every `al_` table (`metadatamembership`).
- DEV was bumped from 1.0.22.0 to 1.0.23.0.
- Export: `artifacts/2026-10-06-duplicate-emails/OutcomeTesting_1_0_23_0_managed.zip`,
  1,393,289 bytes, sha256 `99225e8f...`. It carries DLL sha256 `8c3d102e...` and bundle
  `index-DzTEEjH0.js`. It changes no portal component, so no Active layer on TEST can mask it.
- Import: `ImportSolutionAsync`, job `255a91c4-5d9f-46d7-81ee-09509e8af829`, async operation
  `5731eeec-a8c1-f111-aaad-002248c654cd`. It succeeded (statuscode 30) at 17:12:07Z.
- Read back from TEST:
  - solution 1.0.23.0, managed;
  - assembly sha256 `8c3d102e...`;
  - all 67 steps enabled;
  - Code App `appversion` 2026-10-06T17:11:53Z.
- The three template rows were then updated with `2026-10-06-check-named-templates.json`. All
  three PATCHes returned 204, and the read-back shows the `{{check}}` wording (17:13:52Z).

## PROD (`org3461d426`, OTIS)

The owner said "deploy to test and prod".
- Package: the 1.0.23.0 export run through `brandpackage ... OTIS`, which rewrote 7 labels. The
  result is `artifacts/2026-10-06-duplicate-emails/OTIS_1_0_23_0_managed.zip`, sha256
  `f32959f8...`. It carries the same DLL, `8c3d102e...`.
- Before the import: PROD was at OTIS 1.0.22.0, with no steps disabled.
- Import: `ImportSolutionAsync`, job `18ddb714-c197-4bfe-9ffe-36566029bab9`, async operation
  `f136dc90-aac1-f111-aaaf-7c1e5279965e`. It succeeded (statuscode 30) at 17:23:56Z.
- Not done by the agent: the read-back after the import was refused as a production read. So
  were the three template rows, which are not overwritten without first checking that nobody
  had edited them. Both are handed to the owner below.
- The owner ran the handed-over commands and pasted their output:
  - Solution: 1.0.23.0, OTIS.
  - The rows were unedited before the update. All three subjects were the originals, and the
    etags (`14858338`, `14858340`, `14858351`) were the same as in the owner's read earlier that
    day.
  - Row update: three PATCHes returned 204, and the read-back shows the `{{check}}` wording.
    The guard accepting `{{check}}` also proves the new assembly is live. TEST's old assembly
    refused the same rows.
- Assembly read back from PROD: sha256 `8c3d102e...`, 383,488 bytes, modified 17:23:21Z. It is
  the build that was tested and pushed to DEV and TEST.
- Not read back in PROD: the step state and the Code App version. The import's success stands
  in for them.

## Applying the template rows elsewhere

**Stored wording overrides the compiled copy.** Promoting the solution changes the sign-off
letters and the remediation link, but not these subject lines, because every environment
holds stored rows of the three templates. After the import has landed, and not before
(until then the guard refuses `{{check}}` as unknown), run:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.dll webapimany https://org37995f36.crm11.dynamics.com '@docs\deployment\2026-10-06-check-named-templates.json'
```

That is the TEST command. For PROD, use `https://org3461d426.crm11.dynamics.com`, and take the
package through `brandpackage ... OTIS` first. The last line of the file reads the three rows
back.

PROD has no REMEDIATION-OTHER row, which DEV and TEST have had since 2026-09-30. PROD sends
that letter with the compiled wording. It does not cause duplicates.

## 1.0.24.0 - fixes from the audit (DEV and TEST)

An audit of 1.0.23.0 found one regression and six smaller problems. All are fixed:

1. **A rejection could send no email.** On a graded fail the sign-off panel is also offered at
   Awaiting Remediation, where the "case was at Awaiting Sign-off" rule sent nothing. A
   rejection is now announced unless another rejection on the same check, with the same notes,
   was recorded in the last 10 minutes. That window covers one sitting, because the portal posts
   one set of notes for every action it sends back.
2. **Tax check handed on to AQS.** A new letter, `SIGNOFF-APPROVED-AQS-NEXT`, replaces the
   recheck letter that promised a step that was not coming.
3. **Tests:** rejection at Awaiting Remediation, later sittings, different notes, the Tax
   handback, AQS scoping, and the case lock.
4. **The allocation reads its check from a new lookup**, `al_caseassignment.al_reviewinstanceid`,
   which AssignCase and ClaimCase now set. Rows written earlier fall back to the code. A
   source test pins both writers.
5. **"(check)" is gone.** The subjects are now "Case X: AQS check allocated to you" and
   "Case X: AQS check submitted". Where the check is unknown they say "review".
6. **Simultaneous sign-offs.** Each sign-off first writes the case reference back unchanged,
   which takes the row lock. That column is in no step's filtering attributes. A second sign-off
   waits and then reads the first one's rows.
7. **Fewer reads.** `MoveCase` returns whether it moved the case, and that decides the
   approval letter. `CommandHelpers.ReviewTypeOf` replaces the copies.

Tests: 1,818 plug-in tests and 1,332 app tests passed, and `tsc -b` was clean.

### DEV

- Added `al_caseassignment.al_reviewinstanceid` with `addlookupcolumn`, in solution
  OutcomeTesting.
- Pushed the assembly: 385,024 bytes, sha256 `cd02037c...`.
- Updated the three template rows to the new wording.
- Pushed the Code App (`index-B7RoLa6G.js`).
- Proved live: `al_AssignCase` allocated case 900000004. The assignment holds review
  `91c4c50c-...`, and the letter reads "Case 900000004: AQS check allocated to you".
- The sign-off rules were not exercised live.

### TEST

- The membership audit was clean, and `al_caseassignment` is carried with its subcomponents.
- Export: `OutcomeTesting_1_0_24_0_managed.zip`, sha256 `227b439e...`. It carries the new
  relationship.
- Import: job `dd99995d-beaa-4439-81eb-a75d3cfe11d7`, async operation
  `45d14039-bac1-f111-aaad-002248c654cd`. It succeeded at 19:17:24Z.
- Read back from TEST:
  - solution 1.0.24.0, managed;
  - assembly `cd02037c...`;
  - all 67 steps enabled;
  - Code App 19:16:10Z;
  - the column exists.
- The template rows were updated: three 204s, then the new wording read back.

### PROD - done on 2026-10-07 as part of 1.0.25.0 (see `2026-10-07-messaging-and-notes.md`)

Originally:

PROD is still on 1.0.23.0, with the 1.0.23.0 bracket wording in its rows. Preparing the OTIS
package was refused as a production deploy, because "fix all" was not an instruction to promote.
On "deploy to prod":
1. `brandpackage` the 1.0.24.0 zip as OTIS.
2. Import it.
3. Read back the version and the assembly.
4. Run `2026-10-06-check-named-templates.json`, now at the new wording, against PROD.
