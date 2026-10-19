# Email Templates: several recipients per letter, and no silent failures - 1.0.28.0

Date: 2026-10-15. Branch `feat/otis-product-name`.

## What was reported

Three letters of your own were added in PROD at "Remediation assigned" and tested on case
256617620: "Remedial needed - Insufficient evidence or Porential Harm" for Ian, the T&C
Manager and Zoe. Only the adviser received an email.

## Cause

Read from PROD: all three templates existed (saved 09:52-09:54Z) before the case's letters were
queued (10:01Z). The outbox held only the adviser's letter and the para-planner's copy.

Each custom letter's outbox code was `REMEDIATIONASSIGNED-<review id>-<template code>`. The
template code was taken whole, and `al_notificationcode` holds 100 characters, so any template
code over 46 characters overran it. These were 59-67 characters, giving codes of 112-120
characters. The create failed inside `QueueCustom`'s catch, and nothing recorded it. DEV's
tests used short codes (`UAT-...`), so it never showed there.

## What changed

- **Codes always fit.** A custom letter's code is now the event code plus two 16-character
  hashes: one of the occurrence and template code, and one of the address. The longest
  possible code is 81 characters.
- **Several recipients per template.** Two new text columns on `al_notificationtemplate`:
  `al_recipientkinds` (comma-separated role values) and `al_recipientcontactids`
  (comma-separated contact ids). They are read together with the old single
  `al_recipientkind` / `al_recipientcontactid`, so no row needs migrating, and a save from the
  page clears the old pair. Each recipient gets their own outbox row. On a built-in letter,
  choosing people replaces the default recipient; choosing nobody keeps it.
- **No silent failures.**
  - The guard refuses a save naming a contact with no email, a contact that no longer exists,
    or text that is not a role.
  - A chosen recipient who cannot be resolved for a case leaves a **Failed** outbox row with
    the reason, e.g. "Not sent to T&C Manager. No T&C Manager with an email address is mapped
    to the case's adviser." Before this, nothing was recorded.
  - The page lists the last 30 days of failed letters under **Not sent**.
- **Letters of your own ride only with a template letter.** They are no longer raised again
  by the para-planner's fixed copy, which has no template code.
- **Rename:** "Notification wording" is now **Email Templates**.

Tests: 1,846 plug-in tests (12 new in `MultipleRecipientsTests`, including the PROD template
code) and 1,373 app tests passed.

## DEV (`org0b075da8`)

- `addtextcolumn` created `al_recipientkinds` (200) and `al_recipientcontactids` (4000) in
  solution OutcomeTesting.
- `pushassembly`: 399,360 bytes, matching the Release build (sha256 `92d8f75d...`).
- Code App: `npm run build` (`index-DjzoFoTp.js`), `pa app push`. The generator dropped the
  hand-declared `StaffCode` parameter again; it was restored before the build.
- **Proved live:** a letter with a 70-character code at Allocation, going to Adviser,
  T&C Manager and two contacts. Reallocating case 900000004 queued five rows: the built-in
  letter to the new checker plus one per recipient, each code 77 characters. The test template
  was then deleted. The guard refused a deleted contact and the role text "adviser", each with
  a plain sentence.
- **Not proved in a browser:** the saved portal session had expired.

## TEST and PROD

The DEV version bump and managed export were refused to the agent as a production deploy
(as on 2026-10-14), so the owner runs the whole runbook below from the repo root in
PowerShell. It needs no `pac`.

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$tool='plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.dll'
$A='artifacts\2026-10-15-email-templates'

# 1. DEV: bump to 1.0.28.0 and export managed.
dotnet $tool webapimany 'https://org0b075da8.crm11.dynamics.com/' "@$A\export.req.json" "$A\export.out.json"
$raw = Get-Content "$A\export.out.json" -Raw
# The response escapes every '/' as '\/'; base64 must have them back.
$b64 = [regex]::Match($raw, '"ExportSolutionFile"\s*:\s*"([^"]+)"').Groups[1].Value.Replace('\/', '/')
if (-not $b64) { throw "No ExportSolutionFile in $A\export.out.json" }
[IO.File]::WriteAllBytes((Join-Path (Get-Location) "$A\OutcomeTesting_1_0_28_0_managed.zip"), [Convert]::FromBase64String($b64))

# 2. The OTIS copy for PROD.
dotnet $tool brandpackage "$A\OutcomeTesting_1_0_28_0_managed.zip" "$A\OTIS_1_0_28_0_managed.zip" OTIS

# 3. Import, poll, read the version back, check the steps.
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
}

Import-Package 'https://org37995f36.crm11.dynamics.com' 'OutcomeTesting_1_0_28_0_managed.zip' 'test'
Import-Package 'https://org3461d426.crm11.dynamics.com' 'OTIS_1_0_28_0_managed.zip' 'prod'
```

Each import should end with version **1.0.28.0** (PROD friendly name **OTIS**) and every step
enabled. No template wording or portal templates change in this release.

### First run (14:28Z): export only

The export worked, so DEV is on 1.0.28.0, but the decode step did not. The response escapes
every `/` in the base64 as `\/` (25,986 of them), the regex took them as written, and
`FromBase64String` failed. No zip was written, so both imports sent `CustomizationFile: null`
and were refused ("Please provide a valid CustomizationFile"). TEST and PROD were untouched:
TEST read back 1.0.27.0 without the new columns. The runbook above is now fixed.

The agent then decoded `export.out.json` to `OutcomeTesting_1_0_28_0_managed.zip` and checked it:
version 1.0.28.0; both new columns; `OutcomeTestingPlugins.dll` 399,360 bytes, sha256
`92d8f75d...`, the same as DEV's Release build; Code App bundle `index-DjzoFoTp.js`.
`brandpackage` made `OTIS_1_0_28_0_managed.zip` (7 labels, 612 files byte-identical). The agent
also wrote both import request files. Starting the TEST import was refused to the agent as a
production deploy, so the owner runs the imports from those files:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$tool='plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.dll'
$A='artifacts\2026-10-15-email-templates'

function Import-Built($org, $name) {
  $out = "$A\import-$name.out.json"
  dotnet $tool webapimany $org "@$A\import-$name.req.json" $out
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
}

Import-Built 'https://org37995f36.crm11.dynamics.com' 'test'
Import-Built 'https://org3461d426.crm11.dynamics.com' 'prod'
```

### Second run: imports

- **TEST** (`org37995f36`), run by the owner, read back by the agent: solution **1.0.28.0**
  (modified 12:41:02Z), `al_recipientkinds` and `al_recipientcontactids` present, assembly
  modified 12:40:29Z, and both NotificationEmitterPlugin steps and both
  NotificationTemplateGuardPlugin steps enabled.
- **PROD / OTIS** (`org3461d426`), run by the owner. The agent's read-back was refused
  ("Production Reads"); see the owner's console output for the version and step check.

## After the import

- The three PROD templates need no change: their single recipient columns are still read,
  and their long codes now fit. They send from the next Remediation assigned onwards. Case
  256617620's letters are not re-sent automatically.
- They can be merged into one template going to the T&C Manager, Ian and Zoe.
