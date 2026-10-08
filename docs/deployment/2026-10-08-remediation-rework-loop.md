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

## Second round, same day: earlier answers kept, every email framed

- **The adviser's earlier answers.** A rejection reopened the action and the adviser wrote
  over `al_adviserresponse`, so the answer the coach rejected was lost. A new memo column
  `al_remediationaction.al_responsehistory` (20,000 characters) now keeps it. Before the action
  reopens, `SignoffProgressPlugin` appends "<completed date> - Action performed: <Yes/No>" and
  the answer. The PDF prints it under "Earlier responses:" in the Action performed cell. Both
  portal pages draw it under every cell that shows the answer, the adviser's rework box
  included.
- **Only that reopen may write the history.** `RemediationResponseGuardPlugin.HistoryRefusal`
  refuses every other write. The write must set a Completed action back In progress, may only
  add to the history, and must follow a rejection recorded since the action was completed. The
  first version asked whether the write came from inside a Create of `al_signoff`. DEV refused
  the sign-off's own write on the first live run, so the rule now works from the record's state.
  The guard's step filter now includes `al_responsehistory`.
- **Plain-text emails are framed too.** The body becomes one paragraph with its line breaks kept
  and its bare web address made a link. This covers allocation, approval, recheck and the
  para-planner's note.

DEV:

- `addmemocolumn ... al_remediationaction al_ResponseHistory "Response history" 20000` created
  the column in solution OutcomeTesting.
- `setstepfilter` added `al_responsehistory` to "RemediationResponseGuardPlugin: Update of
  al_remediationaction".
- The assembly was pushed (sha256 `3eb1e066...`), and both web templates were pushed. Before
  the push, DEV's copies matched the last commit.

Proved live on DEV case 900000001:

- **Rejection.** `al_SignOffRemediation` rejected action `fb9aac97-...` with notes. The action
  went back In progress and kept the 29 Sep answer in `al_responsehistory`. The case went back
  to Awaiting Remediation.
- **Rejection email.** The outbox row greets the adviser and shows the notes panel and the
  **Update remedial action** button to `/remediation?case=632409fe-...`. It carries both PDFs.
  The drain sent it: the email activity's body is framed ("Outcome Testing" header, the footer),
  and it renders as designed.
- **Guard.** A direct PATCH of `al_responsehistory` was refused with the guard's message.
- **Rework.** The answer was rewritten and `al_completerequested` was set (the portal's path).
  The case then **closed** rather than returning to Awaiting Sign-off. That is correct: the case
  is graded Pass with issues, which has closed on completion without T&C sign-off since
  2026-10-05 (5c59725, AD-230). It sat at Awaiting Sign-off only because it got there before
  that rule. So the second "sign-off due" email could not be shown on this case. That needs an
  Insufficient evidence or Potential harm case, and none in DEV belongs to a mapped adviser. It
  is covered by `SignoffDueNotificationTests.Rework_after_a_rejection_tells_the_manager_again`.
- **PDF.** `renderpdf` from the DEV data shows the reworked answer with the rejected one under
  "Earlier responses:". The "Supervisor sign-off" row shows the rejection and its notes. It
  carries no signatory name, because an API sign-off by Service Account stamps none; a portal
  sign-off does.

## Packaged for TEST and PROD (1.0.26.0)

The owner said "promote to both test and prod".

- **DEV membership audit:** `al_remediationaction` is carried with its subcomponents, so
  `al_responsehistory` travels. The guard step, both templates and the assembly were already
  members.
- **Version:** DEV bumped from 1.0.25.0 to 1.0.26.0.
- **TEST package:** `artifacts/2026-10-08-remediation-rework/OutcomeTesting_1_0_26_0_managed.zip`
  (1,399,612 bytes, sha256 `e168cc64...`). It carries:
  - DLL `3eb1e066...`, the one live in DEV;
  - the column, and the guard filter with `al_responsehistory`;
  - bundle `index-BqTF9QPV.js`;
  - both templates with the trail and the history.
- **PROD package:** `OTIS_1_0_26_0_managed.zip` (sha256 `0ba9083b...`), from `brandpackage`
  (7 labels). It carries the same DLL.
- **Imports not run by the agent.** Writing the import request files was refused as a
  production deploy, and a TEST read was refused as a production read. The runbook below goes
  to the owner.
- **Masked components:** per the 1.0.25.0 record, OT Remediation and OT Case Detail have only
  their managed layer in TEST and PROD, so the import updates them. TEST's direct-push layer is
  on OT Review Detail, which this release does not change.

## Promoted - TEST and PROD, 2026-10-08 (run by the owner)

- **First attempt: no import.** `Import-Solution.ps1` could not connect: `pac`'s grant was
  revoked (AADSTS50173, tokens valid from 2026-10-06T07:11:16Z). It still printed "Import
  complete", and both version reads said 1.0.25.0. The template updates were refused by the
  old guard (unknown `{{adviser}}`, `{{notesPanel}}` and `{{caseButton}}`), so nothing changed.
  The same reads showed both rows unedited in both environments: TEST last modified
  2026-09-30/09-25, PROD 2026-10-01.
- **Second attempt:** the registration tool's `ImportSolutionAsync` route, with the async
  operation polled to completion.
  - **TEST:** import job `ca6fe7e1-...`, async operation `8cae8bf1-...`, statuscode 30.
    Afterwards: solution 1.0.26.0, all 35 solution steps present and enabled, both template
    rows updated (two 204s, then the new wording read back).
  - **PROD (OTIS):** import job `3507e90c-...`, async operation `65b57a36-...`, statuscode 30.
    Afterwards: solution 1.0.26.0, friendly name OTIS, all 35 steps enabled, both template rows
    updated (two 204s at 18:17:50Z).
  - The 204s also prove the new assembly is live in each: the old one refused the same rows.
- **Rendered live in DEV** after the owner re-captured the portal session (20:26). On case
  900000001, both OT Remediation and OT Case Detail render without a Liquid error. The
  "Supervisor sign-off" row shows the rejection with the coach's notes. Action 1's cell shows the
  reworked answer with "Earlier responses" (the 29 Sep answer) under it.
- **Not read back:** the assembly hash, the portal templates as served, and the new column.
  The import's success and the guard accepting the new tokens stand in for them.

### The import function that worked

Run it from the repo root in PowerShell. It needs no `pac`.

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$tool='plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.dll'
$A='artifacts\2026-10-08-remediation-rework'

function Import-Package($org, $zip, $name) {
  $req = Join-Path (Get-Location) "$A\import-$name.req.json"
  $out = Join-Path (Get-Location) "$A\import-$name.out.json"
  $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path "$A\$zip").Path))
  $body = @{ CustomizationFile = $b64; PublishWorkflows = $true; OverwriteUnmanagedCustomizations = $false; ImportJobId = [guid]::NewGuid().ToString() }
  [IO.File]::WriteAllText($req, (ConvertTo-Json -InputObject @(@{ verb = 'POST'; path = 'ImportSolutionAsync'; body = $body }) -Depth 5 -Compress))
  dotnet $tool webapimany $org "@$req" $out
  $op = (Get-Content $out -Raw | ConvertFrom-Json)[0].body.AsyncOperationId
  if (-not $op) { Write-Host "NO IMPORT STARTED - see $out"; return }
  Write-Host "Import running: async operation $op"
  do {
    Start-Sleep -Seconds 20
    $r = dotnet $tool webapi $org GET "asyncoperations($op)?`$select=statecode,statuscode,message" | Select-String '^\{' | ForEach-Object { $_.Line | ConvertFrom-Json }
    Write-Host ("  state {0}, status {1}" -f $r.statecode, $r.statuscode)
  } while ($r.statecode -ne 3)
  if ($r.statuscode -ne 30) { Write-Host "IMPORT FAILED: $($r.message)"; return }
  dotnet $tool webapi $org GET "solutions?`$select=version,friendlyname&`$filter=uniquename eq 'OutcomeTesting'"
  dotnet $tool verifysteps $org 'src\SdkMessageProcessingSteps'
  dotnet $tool webapimany $org '@docs\deployment\2026-10-08-signoff-letter-links.json'
}

Import-Package 'https://org37995f36.crm11.dynamics.com' 'OutcomeTesting_1_0_26_0_managed.zip' 'test'
Import-Package 'https://org3461d426.crm11.dynamics.com' 'OTIS_1_0_26_0_managed.zip' 'prod'
```

For the next release, change `$A`, the zip names and the template-row file. Drop the last line
inside the function when the release changes no template wording.

## TEST and PROD runbook (as first planned)

In this order, for each environment:

1. Import the solution carrying this assembly, both web templates, the Code App, the
   `al_responsehistory` column and the guard step's new filter. Export it from DEV after a
   membership audit, so the column is in it.
2. Check that the SIGNOFF-REJECTED and SIGNOFF-DUE rows still hold the original wording.
3. Run `2026-10-08-signoff-letter-links.json` against that environment. A row naming
   `{{notesPanel}}` or `{{recipient}}` is refused until the new assembly is there.
