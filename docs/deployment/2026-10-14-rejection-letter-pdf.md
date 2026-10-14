# The rejection letter's PDF shows the whole sitting - DEV, TEST and PROD (1.0.27.0)

Date: 2026-10-14. Branch `feat/otis-product-name`, fix `fe95102`.

## What was reported

The Remediation PDF on the "sent back" email disagreed with the portal. Only the top action
showed its new status. Every other action still read "Adviser completed ...; awaiting
supervisor", while the portal showed them all sent back.

## Cause and fix

The portal records a rejection per action, one request after another. `SignoffProgressPlugin`
queued the rejection letter on the FIRST of them, and the letter's documents are drawn when it
is queued (`NotificationOutbox.QueueWithCompletedCheck`). So the PDF caught one action rejected
and the rest not yet reached. The async drain sends within seconds, so redrawing the documents
later would have raced the send.

The letter now goes on the LAST rejection of the sitting: the one that leaves no action on the
check Completed and not yet approved (`EarnsSignoffLetter`, `AnyAwaitingSignoff(...,
completedOnly: true)`). The portal's panel puts every such action in one sitting under one
decision. The old rule, which grouped rejections with the same notes inside 10 minutes, is
removed.

Known limit: a sitting that stops partway on a refusal sends no letter until the rest are
rejected. A rejection of one action with the others approved, which only a direct API call can
make, sends none.

Tests: 1,834 plug-in tests passed. `No_rejection_letter_goes_until_the_sitting_has_reached_every_action`
pins the fault.

## Deployed

- **DEV** (`org0b075da8`): `pushassembly`, 392,192 bytes. DEV's `pluginassembly.content` hashes
  `77090fde...`, the same as the Release build.
- **Package:** DEV bumped from 1.0.26.0 to 1.0.27.0 and exported managed to
  `artifacts/2026-10-14-rejection-letter-pdf/OutcomeTesting_1_0_27_0_managed.zip`.
  `brandpackage` made `OTIS_1_0_27_0_managed.zip` (7 labels). The DLL in both hashes `77090fde...`.
- **TEST** (`org37995f36`), run by the owner: async operation `8f5267ab-...`, statuscode 30.
  Solution 1.0.27.0, all 35 steps present and enabled.
- **PROD / OTIS** (`org3461d426`), run by the owner: async operation `ba6ae5ef-...`,
  statuscode 30. Solution 1.0.27.0, friendly name OTIS, all 35 steps enabled.
- No template wording or portal templates changed.
- **Not proved live:** no rejection has been made since. The next rejection's email should
  arrive after its last action is rejected, with every action sent back in the PDF.

The import function is the one in `2026-10-08-remediation-rework-loop.md`, without the
template-row line.
