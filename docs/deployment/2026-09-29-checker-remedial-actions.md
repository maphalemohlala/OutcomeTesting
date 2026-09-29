# The checker writes the remedial actions; the adviser says whether they were performed

Written for: whoever promotes this to TEST, and whoever reads back what DEV holds.

**Date:** 2026-09-29
**Environment:** `Env_AQ_Dev` (`org0b075da8`) only. TEST and PROD untouched.
**Decision:** AD-225. **Spec:** `docs/superpowers/specs/2026-09-29-checker-remedial-actions-design.md`.
**Code:** branch `feat/change-batch-sep-2026`, `aac1965` to `cd6cf9e` (Tasks 1-9).

## What was wrong

When a Tax or AQS check was submitted with failures, `Remediation.NonPassItems` listed what
the checker had marked down and `Remediation.Raise` turned each item into an
`al_remediationaction` (AD-107). The **adviser** then wrote the "Remedial action" for each row
on `OT Remediation`, stored in `al_adviserresponse` (AD-095), and could not complete a row
while it was blank. The project owner asked for this to be the other way round: the checker
knows what was wrong and what putting it right looks like, so the checker writes the remedial
action per fail point before submitting, and the adviser records, per row, whether it was
performed.

## The rule

- Before a check that owes remediation can be submitted, **every item the submit will raise
  carries the checker's remedial action** - or one **Overall** action when nothing is
  itemised. At a Tax submit this is checked even when raising is deferred to the AQS submit
  (AD-184).
- Each raised action carries those words in `al_remedialaction`, **written once** by the
  submit and never changeable afterwards.
- The adviser answers **Action performed** (Yes `120910815` / No `120910816`) per row, with
  `al_adviserresponse` as an optional note. A **No still completes**; the supervisor decides.
- Rows raised before this change keep the adviser-written remedial action and the old
  completion rule (a non-blank response).

## Where it is enforced

| Surface | What it does | Authority |
|---|---|---|
| `OT Review Detail`, card "Fail points and remedial actions" | Lists the items live (mirror of `NonPassItems`), a required box per item; Submit refuses and names the first empty row | Convenience - the server's list is the one that counts |
| `contact.al_remedialactionsrequest` -> `RemedialActionsRequestPlugin` (sync post-op, Update of contact) | Parks the map on `al_reviewinstance.al_pendingremedialactions`; the review must be open and assigned to the calling contact; values trimmed, empties dropped | Server |
| `al_SubmitReview` -> `SubmitReviewPlugin` / `RemedialActions.EnsureWritten` | Refuses the submit, naming the item, while any item (or Overall) has no words; `Remediation.Raise` stamps `al_remedialaction`; the pending map is cleared once raised | Server |
| `RemediationResponseGuardPlugin` (pre-op, Update of al_remediationaction) | `al_remedialaction` refused on any update; `al_actionperformed` frozen once Completed, with the note | Server |
| `CompleteRequestPlugin` / `CompleteRemediationPlugin` | A row carrying `al_remedialaction` completes only with Action performed answered; old rows keep the response rule | Server |
| Web API allowlists (site settings) | `al_actionperformed` and `al_remedialactionsrequest` added; `al_remedialaction` deliberately **not** listed, so the portal cannot write it at all | Portal |
| `OT Remediation` | Yes / No radios and an optional note for the adviser's own open rows; read-only elsewhere | Rendering |
| `OT Case Detail`, Code App `RemediationPage`, `RemediationDocument` (PDF) | The nine-column form, read-only: No. · Issue · Remedial action · **Action performed** · Owner · Target date · Status · Age · Sign-off | Rendering |

## What ran

All with the registration tool (`plugins/OutcomeTesting.Registration`, Debug build, as
`svc.automate.aq`), against `https://org0b075da8.crm11.dynamics.com/`, in the order the brief
set: columns, assembly, type, step, step filter, site settings, templates, Code App.

| # | Command | Result |
|---|---|---|
| 1 | `webapi GET EntityDefinitions(LogicalName='al_remediationaction')/Attributes/...PicklistAttributeMetadata` | Picklists hold `120910600-602`, `120910793-799` only; **`120910815`/`120910816` free** |
| 2a | `addmemocolumn ... al_remediationaction al_RemedialAction "Remedial action" 4000 ... --confirm` | `Created al_remediationaction.al_remedialaction (memo, max 4000) in solution OutcomeTesting.` |
| 2b | `addchoicecolumn ... al_remediationaction al_ActionPerformed "Action performed" "120910815:Yes;120910816:No" ... --confirm` | `created with options 120910815=Yes, 120910816=No.` |
| 2c | `addmemocolumn ... al_reviewinstance al_PendingRemedialActions ... 100000 ... --confirm` | `Created al_reviewinstance.al_pendingremedialactions (memo, max 100000) in solution OutcomeTesting.` |
| 2d | `addmemocolumn ... contact al_RemedialActionsRequest ... 100000 ... --confirm` | `Created contact.al_remedialactionsrequest (memo, max 100000) in solution OutcomeTesting.` |
| 3a | `dotnet build plugins/OutcomeTesting.Plugins/OutcomeTesting.Plugins.csproj -c Release` | 0 warnings, 0 errors; DLL 376,832 bytes, 13:36:27 SAST |
| 3b | `pushassembly` | `pushed OutcomeTesting.Plugins (7b51d0d1-...): 376832 bytes`; **sha256 of `pluginassembly.content` = local DLL = `12bc6d0c8a0e5de653caed162110077a3c8df2b4dd3b3065bb0da1d7a7ae3f60`**; the decoded bytes contain `RemedialActionsRequestPlugin` |
| 3c | `registertype ... OutcomeTesting.Plugins.RemedialActionsRequestPlugin` | `created plugintype: f88e4098-fabb-f111-aaad-e4fade0775c0` |
| 3d | `registerstep ... RemedialActionsRequestPlugin Update contact 40 al_remedialactionsrequest sync` | `created sdkmessageprocessingstep: ce0e73a5-fabb-f111-aaad-e4fade0775c0`, `added to the OutcomeTesting solution`; read back **Enabled**, post-op, sync, filter `al_remedialactionsrequest` |
| 3e | `setstepfilter ... "RemediationResponseGuardPlugin: Update of al_remediationaction" <seven columns> --confirm` | five columns -> `al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice,al_actionperformed,al_remedialaction`; step still Enabled |
| 4a | `fetch mspp_sitesetting` (both allowlists) | DEV held exactly what `sitesetting.yml` held before this change - no DEV-only drift |
| 4b | `setsitesetting ... Webapi/al_remediationaction/fields "<DEV value>,al_actionperformed" --confirm` | `... al_changesadvice -> ... al_changesadvice,al_actionperformed` |
| 4c | `setsitesetting ... Webapi/contact/fields "<DEV value>,al_remedialactionsrequest" --confirm` | `... al_accountabilityrequest -> ... al_accountabilityrequest,al_remedialactionsrequest` |
| 5 | Diff, then `pushwebtemplate` x3 | Before pushing, each DEV template's `content.source` equalled the source at `4459947` (before this batch) after CRLF/BOM normalisation - nothing in DEV would be lost. `OT Review Detail` **252,277 -> 266,652** chars; `OT Remediation` **142,177 -> 148,180**; `OT Case Detail` **57,284 -> 58,460**. Re-fetched afterwards: all three equal `HEAD` |
| 6a | `npx pa app add data-source --connector dataverse --table al_remediationaction --non-interactive` | Model and schema gained `al_remedialaction` and `al_actionperformed`. It also **dropped the hand-declared `StaffCode` parameter** of `al_UpdateUser` from `dataSourcesInfo.ts` (the generator does not know a parameter declared by hand, and the app then drops it in silence); that file was restored from `HEAD` before building |
| 6b | `npm run build` | `dist/assets/index-CSIADLZ9.js` (631,755 bytes, 13:47:59), contains `Action performed` and `al_actionperformed` |
| 6c | `npx pa app push` | `App pushed successfully` - `.../app/5d9fc475-...?...&sourcetime=1790682547944` |

Solution membership read back: `al_remediationaction` and `al_reviewinstance` are in
OutcomeTesting with **Include Subcomponents**, so their new columns travel with them;
`contact.al_remedialactionsrequest` (`ff0131f7-...`) and step `ce0e73a5-...` are listed as
components in their own right; the assembly (and so the new type) is in the solution.

No `--confirm` verb was refused this time.

## What was proved live

**In the browser** means a headless Chromium on the DEV portal
(`outcometesting.powerappsportals.com`) with the saved Service Account session
(`app/e2e/.auth/portal.json`), clicking and typing on the served pages. **Server-side** means
the registration tool's `webapi` / `webapimany` / `callapi` as `svc.automate.aq`.

### AQS-only case 900000001, review `0c46f88a-2db7-f111-aaac-e4fade069307` (assigned to Service Account)

| Step | How | Observed |
|---|---|---|
| 1. Two test points marked down, one fail point ticked -> three rows | Browser | Card hidden before answering; after answering, visible with **3 rows**: `ID verification completed and retained for all relevant clients/parties.: No`, `Financial situation and needs sufficiently captured: Fail`, `Record Keeping - Standard of file quality`. Fail points are locked while AML/CRA are all Yes, so one of the two marked-down points is an AML **No** and the other a Suitability **Fail** |
| 1. Untick then re-tick keeps the words | Browser | Words written on the fail point row; unticked -> 2 rows; re-ticked -> row back with its words intact |
| 2. Submit with one row empty | Browser | Page refused: `Write the remedial action for "Financial situation and needs sufficiently captured: Fail" before submitting.`; after reload the review was still open and `data-ot-remedial-saved` held the two written rows |
| 2. The same, at the server | Server-side (`callapi al_SubmitReview`) | `PRECONDITION: Write the remedial action for 'Financial situation and needs sufficiently captured: Fail' under 'Fail points and remedial actions' before submitting.` - and it did **not** name the AML row, so the page's key for it (with the `/` in "clients/parties") matched the server's |
| 3. Fill every row and submit | Browser | `Review submitted. This review is now locked...`; after reload no Submit button and no card |
| 3. Query | Server-side | Three actions `REM-900000001-2-1..3`, each `al_remedialaction` holding the words written against **its own row** (issue text in `al_description` matched); review `al_pendingremedialactions` **null**; case **Awaiting Remediation** |
| 4. Adviser answers one Yes and one No with a note, then signs off | **Server-side**, as the portal's own PATCH (`al_adviserresponse`, `al_actionperformed`, `al_clientcontactrequired`, `al_completerequested: true`) | The actions are assigned to Simunye Radingwana, whose portal session had expired (and impersonation does not work here), so on this case the adviser's clicks could not be made in the browser; they were on case 920929001 below. Row 1 Yes + note, row 2 **No** + note, row 3 Yes without a note: all three **Completed**, case **Awaiting Sign-off** (`120910589`); `al_actionperformed` and `al_adviserresponse` hold exactly what was sent |
| 4. Completion rule | Server-side | Before answering, `al_completerequested: true` on a row with no Action performed: `PRECONDITION: Answer 'Action performed' (Yes or No) for this action before marking it complete.` |
| 4. Frozen once completed | Server-side | `al_actionperformed` PATCH on the completed No row: `CONFLICT: This response has been submitted for sign-off and can no longer be changed...` |
| 4. Rendering | Browser (read-only, as Service Account) | `OT Remediation` and `OT Case Detail` both draw the nine-column header `No. \| Issue / fail reason \| Remedial action \| Action performed \| Owner \| Target date \| Status \| Age \| Sign-off`, with the checker's words under Remedial action and `Yes`/`No` plus the adviser's note under Action performed; no editable radios (not the assigned contact) |
| 5. Write-once | Server-side | `PATCH al_remediationactions(fb9aac97-...) {"al_remedialaction":"x"}`: `PRECONDITION: The remedial action is the checker's, set when the check was submitted, and cannot be changed here.` |

### Tax-then-AQS case 920929001, created for this run

Imported through `al_ImportCases` (one row modelled on the sample file's "Tax Check" row, TaskID
`920929001`, adviser **Service Account** so the adviser's form could be driven in the browser),
`al_UpdateCaseDetails` for its case/product/pre-post lists, then `al_AssignCase` to Service
Account for the Tax check (review `e6072954-ffbb-f111-aaad-e4fade0775c0`) and later the AQS
check (review `ca1a4d64-02bc-f111-aaad-e4fade0775c0`). Route **Tax then AQS**.

| Step | How | Observed |
|---|---|---|
| Tax 1. Tax Fail, two fail points ticked | Browser | Card appeared with **2 rows** (the Tax page has no remediable test points other than its two outcome questions, which are excluded): `Record Keeping - Missing documents (unable to provide)`, `Tax check – insufficient evidence to complete the check or to pass`. Untick -> 1 row; re-tick -> the row back with its words |
| Tax 2. Submit with one row empty | Browser | `Write the remedial action for "Tax check – insufficient evidence to complete the check or to pass" before submitting.` |
| Tax 2. The same, at the server | Server-side (`callapi al_SubmitReview`) | `PRECONDITION: Write the remedial action for 'Tax check - insufficient evidence to complete the check or to pass' under 'Fail points and remedial actions' before submitting.` (the tool's console prints the name's en dash as a hyphen; the stored name is U+2013) - the gate runs at the Tax submit although raising is deferred |
| Tax 3. Fill and submit | Browser | `Review submitted.` The en-dash item key the page wrote matched the server's (the submit went through) |
| Tax 3. Deferred | Server-side | Case back to **Queued**, `al_taxoutcome` **Fail**, **no actions raised**, and the Tax review **kept** its `al_pendingremedialactions` |
| AQS 1-3 | Browser | Same as on 900000001: three rows, untick/re-tick keeps the words, refused naming `Financial situation and needs sufficiently captured: Fail`, then submitted |
| **Tax words on Tax rows** | Server-side query | Five actions `REM-920929001-2-1..5`. **2-1 `Record Keeping - Missing documents (unable to provide)` and 2-2 `Tax check – insufficient evidence ...` carry the Tax checker's words**; 2-3..2-5 carry the AQS words, each against its own item. **Both** reviews' `al_pendingremedialactions` are now null; case **Awaiting Remediation** |
| 4. Adviser answers and signs off | **Browser**, on `OT Remediation` as Service Account (the assigned adviser) | Five editable Action performed cells. Row 1 Yes + note, row 2 **No** + note, rows 3-5 Yes, Client contact required? No, then the form's button. After it: every row **Completed**, "Adviser completed 29 Sep 2026; awaiting supervisor", **0 radios enabled and 0 notes editable** (shown in place, no reload). Read back: `al_actionperformed` Yes/No/Yes/Yes/Yes, the two notes exactly as typed, case **Awaiting Sign-off** (`120910589`) |

The form's pre-flight message ("Answer "Action performed" for issue N before signing off.") was
exercised by the script but not captured - its text match did not allow for the quotes - so it
is not claimed here.

### Not proved live

- **The Code App's served bundle.** The push succeeded and `dist/` was freshly built and
  checked, but the saved session's Entra cookies no longer open the Power Apps player (it
  lands on the Microsoft sign-in page), so the served `index-<hash>.js` and the remediation
  page were not observed in a browser. Open
  `https://apps.powerapps.com/play/e/d50d27e8-cb3b-e718-b6e2-30aa92d944aa/app/5d9fc475-ee75-4386-917e-fc182307b0c2?tenantId=4abde4fc-68ae-44b4-8e80-b575a8c3d5b8&hint=0eeb1568-9283-489f-b209-f5e1f9fc0df2&sourcetime=1790682547944`
  and check it serves `index-CSIADLZ9.js`.
- **The PDF** (`RemediationDocument`) - covered by its unit tests; no completed-remediation
  email was generated and read in this run.

## Things found on the way

- **The page follows the portal cache on a reopened review.** Straight after the answers
  were saved, a reload drew the checklist from Power Pages' server-side cache, which had not
  yet seen the new `al_response` rows (AD-094, up to 15 minutes), so the card showed one
  empty Overall row. Within about eight minutes the reopened page drew all three rows with
  their words. Not new, and not this change's: the card draws from whatever answers the page
  shows. The server gate is unaffected.
- **`OutcomeTesting.Registration` no longer builds at `HEAD`.** `RemediationDocument.cs`, which
  the tool links for `renderpdf`, now references `RemedialActions`, and the tool's `.csproj`
  does not link `RemedialActions.cs` (5 x CS0103). The deployment used the Debug build of
  10:04 today, whose `Program.cs` is the current one (last changed `b09a4a2`, 2026-09-25), so
  every verb used here is current; only `renderpdf` is affected. Left for a code fix, not
  made here.
- **`portalHeaderAutosave.test.ts` fails in this working copy (5 tests).** Its regex needs LF
  line endings; the `OT Review Detail` working copy was rewritten at 13:03 with CRLF. The
  committed blob is LF and matches. The deployed template is the LF content (the push
  normalises line endings), identical to `HEAD`.
- **One registration tool process hung and was killed.** Mid-run, while seeding case
  920929001, a tool process sat at "Connecting…" after the sign-in window had closed. It was
  killed (pid 26592); it had never connected, so nothing it was asked to do had run. The same
  step (`al_UpdateCaseDetails`) was then re-run once and succeeded. This is the known
  one-process-at-a-time token contention, not a fault in anything this change deployed.
- **The regenerated Code App model and schema** (`Al_remediationactionsModel.ts`,
  `remediationactions.Schema.json`) are left uncommitted in the working tree for the owner of
  the branch to commit; the code reads both columns without them, and the bundle was built
  with them.

## Test data left in DEV

- Case **900000001** (AQS only): three actions completed, **Awaiting Sign-off**.
- Case **920929001** (Tax then AQS, created for this run, adviser Service Account): five actions
  completed, **Awaiting Sign-off**.
- Both can be signed off or rejected by a T&C Supervisor to exercise the sign-off leg, which
  this change did not alter.

## Fix wave redeploy (2026-09-29, afternoon)

The final review's fixes (`d3d28c6`..`8a10e91`, listed in
`.superpowers/sdd/2026-09-29-checker-remedial-actions/final-fix-report.md`) were redeployed to
`Env_AQ_Dev` only, from `8a10e91`, as `svc.automate.aq`. The registration tool builds at `HEAD`
again (`dotnet build plugins/OutcomeTesting.Registration -c Debug`: 0 errors), so the
"no longer builds" note above is resolved and `renderpdf` works.

### What ran

| # | Artefact | Command | Result |
|---|---|---|---|
| 1 | Plug-in assembly | `dotnet build ...Plugins.csproj -c Release --no-incremental`, then `pushassembly` | `378368 bytes`, modified 13:34:20Z. **sha256 of `pluginassembly.content` = local DLL = `f05b55d13d88cf4e249f9bd25c29f4e5489c3aef300bc14873dba064caf71a46`**; the decoded bytes contain `RemediationLeg`, `SectionDrawn` and `ReviewTypeOf` (I1, I2). No new types or steps |
| 2 | `OT Review Detail` | diff, then `pushwebtemplate ... a1000000-...-00000000001b` | Before: DEV equalled the source at `bcf926e` (the first deployment). **266,652 -> 272,672 chars** (272,700 bytes UTF-8, no BOM). Read back: equals `HEAD` |
| 3 | `OT Layout` | diff, then `pushwebtemplate ... a1000000-...-000000000010` | Before: DEV equalled the source at `b56425c`. **2,156 -> 2,156 chars** (2,156 bytes): the one change is `outcome-testing.css?v=23` -> `?v=24`. Read back: equals `HEAD` |
| 4 | `outcome-testing.css` | diff, then `pushwebfile ... a1000000-...-000000000050` | Before: DEV equalled the source at `7ac209c`. Pushed the `HEAD` blob with LF endings (what DEV already held): **63,582 -> 64,124 bytes** (63,575 -> 64,117 chars). Read back (`filecontent/$value`): equals `HEAD` |

Nothing in DEV was overwritten that source did not already hold. OT Remediation, OT Case
Detail, the Code App and the site settings were not touched (unchanged in the wave).

### What was proved, and how

| Check | How | Observed |
|---|---|---|
| The portal serves the new layout and css | Browser (read-only, saved Service Account session) | Every page links `/outcome-testing.css?v=24`; the served css holds `.ot-performed .opt { display: inline-flex ... }` |
| The portal serves the new `OT Review Detail` | Read-back only | DEV's stored source equals `HEAD`. The card's script renders only on an **editable** review, and no open review is assigned to Service Account in DEV, so the new I3/I4/M1 code was not seen executing in a browser |
| **The remediation PDF draws Action performed** | Server-side: `renderpdf https://org0b075da8.crm11.dynamics.com/ 920929001 <folder>` (reads only; renders locally) | `Remediation 920929001.pdf` (11,337 bytes) has the nine-column header with **Action performed**: row 1 `Yes` + "Done 29 Sep: documents received from the client and filed.", row 2 **`No`** + "Not done: the tax evidence is still with the provider; chased 29 Sep.", rows 3-5 `Yes` with no note. Status Completed, "Adviser completed 29 Sep 2026; awaiting supervisor". This is the pre-sign-off state |

### Not proved in this run

- **The sign-off leg (review finding I5, parts a and b).** Case 920929001's adviser is
  `svc.automate.aq@ascotlloyd.co.uk`, and no `al_advisermapping` names a T&C Manager for that
  email, so `SignoffRequestPlugin.EnsureMappedToCase` refuses every sign-off on it. Creating
  that mapping (Service Account's contact `5ac28998-68a7-f111-aaac-e4fade069307` as the
  manager) was **refused by the permission gate** as a permission grant, and further work
  towards the same sign-off was then refused too. So no approval, no rejection, no reopen and no
  rework was made, and nothing changed on either case. The owner's runbook is below.
- **The Code App's served bundle.** Opening the `/app/` URL with `sourcetime=1790682547944`
  headlessly with the saved session landed on `login.microsoftonline.com` (the Entra cookies
  have expired again), so the served bundle and the Action performed column there are still
  not observed. Re-capturing the session needs the user to sign in (`npm run e2e:auth` from
  `app/`).
- **The I1 smoke test** (a fresh Tax-then-AQS case with a Tax Fail carrying only an Overall
  action beside AQS items). Skipped: it needs a new case imported, detailed, assigned twice,
  answered and submitted twice, which is the heavy seeding the brief said to skip. The unit
  tests in `RemedialActionsSubmitTests` are the evidence for I1.
- The PDF after a rejection and rework - it depends on the sign-off leg.

### The sign-off runbook for the owner

Run in PowerShell. Each line of the tool is one process; run them one at a time.

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$T='C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe'
$U='https://org0b075da8.crm11.dynamics.com/'
$D="$env:TEMP\ot-signoff"; New-Item -ItemType Directory -Force $D | Out-Null

# 1. Map Service Account as the T&C Manager for the adviser on case 920929001 (the refused write).
[IO.File]::WriteAllText("$D\map.json", '{"al_adviseremail":"svc.automate.aq@ascotlloyd.co.uk","al_TcManagerId@odata.bind":"/contacts(5ac28998-68a7-f111-aaac-e4fade069307)"}')
& $T webapi $U POST al_advisermappings "@$D\map.json"

# 2. Approve REM-920929001-2-1 and reject REM-920929001-2-2, as the portal's own PATCH on the
#    signatory's contact row (SignoffRequestPlugin creates the al_signoff).
[IO.File]::WriteAllText("$D\approve.json", '{"al_signoffrequest":"{\"actionId\":\"5818eb29-03bc-f111-aaad-e4fade0775c0\",\"decision\":120910720,\"notes\":\"I5 run: approved.\",\"finalOutcome\":0,\"recheckRequired\":120910796,\"changesAdvice\":120910799}"}')
& $T webapi $U PATCH "contacts(5ac28998-68a7-f111-aaac-e4fade069307)" "@$D\approve.json"
[IO.File]::WriteAllText("$D\reject.json", '{"al_signoffrequest":"{\"actionId\":\"5c18eb29-03bc-f111-aaad-e4fade0775c0\",\"decision\":120910721,\"notes\":\"I5 run: the tax evidence is still outstanding - obtain it and answer again.\",\"finalOutcome\":0,\"recheckRequired\":0,\"changesAdvice\":0}"}')
& $T webapi $U PATCH "contacts(5ac28998-68a7-f111-aaac-e4fade069307)" "@$D\reject.json"

# 3. Read back: 2-2 should be In progress (120910601), the case Awaiting Remediation.
& $T webapi $U GET 'al_remediationactions?$filter=_al_outcomecaseid_value eq 281e5a2a-ffbb-f111-aaad-e4fade0775c0&$select=al_remediationactioncode,al_actionstatus,al_actionperformed,al_adviserresponse'
& $T webapi $U GET 'al_outcomecases(281e5a2a-ffbb-f111-aaad-e4fade0775c0)?$select=al_casestatus'
```

After step 3, the adviser's rework (Service Account is the adviser on 920929001, so it can be
done in the browser on `OT Remediation`: change row 2 from No to Yes, add a note, press the
form's button) and a second `renderpdf ... 920929001` finish I5. That can go back to an agent
once the mapping exists. Instead of step 1, case 900000001 already maps its adviser (Simunye
Radingwana) to Service Account, but the portal's form records one decision for every action,
so approving one row and rejecting another there also needs the per-action PATCH of step 2,
with that case's action ids.

## Left for the owner

- **The sign-off runbook just above** (I5 a/b), and a fresh portal session for the Code App
  check.
- **Promotion to TEST** - the owner's call each time. What it needs: a solution export
  carrying the four columns, the assembly, the new step, the three templates, and (from the fix
  wave) `OT Layout` and the `outcome-testing.css` web file;
  import **with `--activate-plugins`**; then diff the three templates against source (a
  direct push to TEST masks a managed import); then widen TEST's two allowlists **from TEST's
  own values** (read them first) with `setsitesetting`. Agent imports to TEST are refused
  since 2026-09-28, so that is an owner runbook. After the import, tell TEST's checkers that
  **a review page left open from before the deploy must be reloaded** to show the "Fail
  points and remedial actions" card: the open page has no card, so its Submit is refused by
  the new server gate with nowhere on screen to write the words.
- **Open the Code App URL above** and confirm the served bundle.
- In the first run no `--confirm` verb was refused; in the fix wave redeploy one write was
  refused (the `al_advisermapping` create in the runbook above).
