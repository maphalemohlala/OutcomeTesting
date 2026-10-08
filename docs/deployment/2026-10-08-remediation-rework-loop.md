# The remediation rework loop: sign-off trail, links and letter styling - DEV

Date: 2026-10-08. Branch `feat/otis-product-name`. DEV only; TEST and PROD are still to do.

## What was reported

UAT feedback on a remediation that the T&C Coach sent back and the adviser then redid:

1. **The coach's earlier decision vanished.** Once the reworked remediation was approved, the
   document's "Supervisor sign-off" row read only "Approved, Adam Strumidlo, 08 Oct 2026". The
   rejection before it, and what the coach had asked for, were gone.
2. **The "sent back" email had no link.** The adviser was told what to redo but had no way back
   into the case, unlike the email that first raised the work.
3. **The coach was not told the rework was done.** No email reached the coach when the adviser
   finished the second round.
4. **The coach's "sign-off needed" email had no link.** They had to open OTIS, go to Remediation
   and find the case under "Awaiting T&C sign-off".

## Causes and fixes

1. Every decision was still stored as its own `al_signoff` row with its notes. The PDF
   (`RemediationDocument`) and both portal pages (OT Remediation, OT Case Detail) drew only the
   newest row and, in the PDF, no notes at all. All three now draw every decision, oldest
   first, each with its notes. Rows from one sitting (same decision, signatory, day and notes)
   draw once.
2. and 4. SIGNOFF-REJECTED and SIGNOFF-DUE carried no link token. Both now carry a button to
   `/remediation?case=<id>`: **Update remedial action** for the adviser, and **Review and sign
   off** for the coach. The page shows the sign-off panel to the mapped T&C Supervisor.
3. The SIGNOFF-DUE outbox row was keyed on the case alone, so the second "sign-off due" for a
   case collided with the first and was dropped without an error. The same fault silenced the
   AQS leg of a Tax-then-AQS case. The key now carries the round: the number of decisions
   already recorded on the case's actions (`CompleteRemediationPlugin.SignoffRound`). A replay
   of the same round still collides.

**Letter styling (owner, same day).** Every HTML letter is now sent in a frame applied at send
time by `NotificationDrain`: the product name in a blue header, one typeface, a white card, and
the footer "This is an automated message from <product>. Please do not reply to this email."
(`NotificationFrame`). Plain-text letters are sent as before. The frame is code, not wording,
because a stored template is cleaned to plain tags on save.

The two sign-off letters were reworded to match the remedial ones:

- **SIGNOFF-REJECTED:** "Dear {{adviser}}," and then the coach's notes in a shaded panel
  (`{{notesPanel}}`, new). Then the button and "Kind regards".
- **SIGNOFF-DUE:** "Dear {{recipient}}," (new: the mapped T&C Manager), then "{{adviser}} has
  completed every remediation action on case ...". Then the button and "Kind regards".

The Code App's template editor mirrors both letters and the two new tokens.

Tests: 1,828 plug-in tests and 1,364 app tests passed, and `tsc -b` was clean.

## DEV (`org0b075da8`)

- The plug-in Release build was pushed with `pushassembly`: 390,144 bytes, sha256
  `21262c06...`. DEV's `pluginassembly.content` hashes the same.
- The two template rows were updated with `2026-10-08-signoff-letter-links.json`. Before the
  update they held the original built-in wording, unedited. The read-back matches the request
  byte for byte, so the save-time cleaning left them alone.
- OT Remediation and OT Case Detail were pushed with `pushwebtemplate`. Before the push, DEV's
  copies matched the repository's HEAD exactly.
- The Code App was built (`index-BqTF9QPV.js`) and pushed with `npx pa app push`.
- **Not proved live.** DEV holds no rejected sign-off, and DEV delivers no email. The portal
  session and its Entra sign-in had both expired, so the new trail was not rendered on the
  pages. `app/e2e/.auth/trail-check.mjs <caseId>` renders both pages' "Supervisor sign-off"
  row once a session is captured again (`npm run e2e:auth`).

## TEST and PROD

In this order, for each environment:

1. Import the solution carrying this assembly, both web templates and the Code App.
2. Check that the SIGNOFF-REJECTED and SIGNOFF-DUE rows still hold the original wording.
3. Run `2026-10-08-signoff-letter-links.json` against that environment. A row naming
   `{{notesPanel}}` or `{{recipient}}` is refused until the new assembly is there.
