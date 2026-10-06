# Remediation documents, para-planner letter and the open check form - DEV deployed and proved

Date: 2026-10-05. Branch `feat/otis-product-name`, code at `ee54806` (HEAD at deploy time;
`c48ee6a` retired the withdrawn gating e2e checks first).
Spec: `docs/superpowers/specs/2026-10-05-remediation-documents-and-open-form-design.md`.
Task brief: `.superpowers/sdd/2026-10-05-remediation-documents-and-open-form/task-8-brief.md`.

## Step 0: retired the withdrawn gating e2e checks (AD-231)

`app/e2e/checklist-gating.e2e.ts` pinned three checks for lock behaviour the open form
withdrew. Deleted `draws the fail points with a reason why they are locked`, `renders the lock
server-side, before any script runs` and `takes Pass away without a reload when a finding is
ticked`, the now-unused `env` import, and updated the file's doc comment to record the
retirement date and reason. `npx tsc -b` in `app/` was clean; `npx playwright test --list
e2e/checklist-gating.e2e.ts` listed exactly the two kept tests (`the case list asks for more
width` and `loads the answering script without a console error`). Committed separately
(`c48ee6a`) as the brief required.

## Step 1: full local verification

| Suite | Command | Result |
|---|---|---|
| Plug-in unit tests | `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests` | **1796 passed, 0 failed** |
| App unit tests | `npx vitest run` (in `app/`) | **1318 passed** across 104 files |
| App type check | `npx tsc -b` (in `app/`) | clean, no errors |

## A build break found and fixed before Step 2

`dotnet build plugins/OutcomeTesting.Registration -c Release` failed with `CS0103: The name
'CheckRemedialActions' does not exist in the current context` in the linked copy of
`CompletedCheck.cs`. Tasks 1-7 added `CheckRemedialActions.cs` (the check PDF's remedial-actions
table) and `CompletedCheck.cs` now calls it, but the registration tool's `.csproj` only linked
`CompletedCheck.cs` itself, not its new dependency - the Debug test build never touches this
project, so nothing caught it until the Release build that `pushassembly` needs. Fixed by adding
the missing `<Compile Include="..\OutcomeTesting.Plugins\CheckRemedialActions.cs" ...>` beside
its neighbours. Committed separately (`ee54806`), since it is a prerequisite for deployment
rather than part of the task brief's named file.

Re-run after the fix, `dotnet build plugins/OutcomeTesting.Registration -c Release` **succeeded**:

```
    154 Warning(s)
    0 Error(s)

Time Elapsed 00:00:10.92
```

The 154 warnings are pre-existing nullable-reference-type warnings in `Program.cs`, unrelated to
this fix; the build that follows (Step 2's `pushassembly`) is the one that matters, and it is 0
errors.

## Artefacts pushed to DEV (`https://org0b075da8.crm11.dynamics.com/`)

| Artefact | Command | Result |
|---|---|---|
| Plug-in assembly | `dotnet build ... -c Release`, then `pushassembly` | 381,952 bytes. sha256 `485457290fc4c47022670e7b4c55571c2cd80c8a3d4280fb0db207b5c74c9725` for both the local DLL and `pluginassembly.content` (`7b51d0d1-f5a1-f111-b8dd-e4fade069307`) read back from DEV via `webapi GET`. The decoded bytes contain the UTF-16LE strings "Remedial actions" and "Checks and remedial points". No new plug-in type or step: every change is inside existing steps, as the brief expected |
| Review template | `pushwebtemplate <DEV> a1000000-0000-4000-8000-00000000001b OT-Review-Detail.webtemplate.source.html` | 276,546 -> 255,261 chars. Read back via `webapi GET mspp_webtemplates(...)?$select=mspp_source`: length matches exactly (255,261), and the live source has 0 occurrences of `lockOptions`/`syncFailPoints`/`impliedRemedial` and 11 of `data-ot-remedial`, the same counts as the local file that was pushed |
| Code App | `npm run build`, then `npx pa app push` | Bundle `index-NaEE56cM.js` (643,321 bytes), built fresh at 21:44 local; contains the string "Remedial actions" (the case panel's heading). Push reported success at `sourcetime=1791229499552`. Play URL: `https://apps.powerapps.com/play/e/d50d27e8-cb3b-e718-b6e2-30aa92d944aa/app/5d9fc475-ee75-4386-917e-fc182307b0c2?tenantId=4abde4fc-68ae-44b4-8e80-b575a8c3d5b8&hint=0eeb1568-9283-489f-b209-f5e1f9fc0df2&sourcetime=1791229499552` (the `/app/` form, not the short `/a/{appId}` form, per `code-app-play-url-serves-stale`) |

## Step 5: proof on DEV

DEV held no case ready to assign (900000004/5 already carried unsubmitted Tax reviews from
earlier sessions; 920930001 was the one case still Queued with no review). Two checks were
driven through the full lifecycle, server-side, via the registration tool's `webapi`/
`webapimany` verbs (`svc.automate.aq`'s own already-authenticated identity - no portal browser
session involved for this part):

1. `al_AssignCase` on case **920930001** created an unsubmitted **Tax** review
   (`b748d403-f6c0-f111-aaad-70a8a5b3561e`), assigned to contact `5ac28998-...` (Service
   Account - the DEV portal account).
2. Answered it with the brief's exact contradiction, adapted to what a Tax review can actually
   hold (a Tax review has no "grade" scale at all - `app/e2e/checklist-gating.e2e.ts`'s own
   comment says so, and `ResponseRules.PermittedChoices` confirmed it: `TypePassFail` permits
   only Pass/Fail, so "Pass with issues" is not a shape-valid answer to Q-FQTAX-01):
   - Q-TAX-02 (Tax check outcome) = **Fail**
   - Q-FQTAX-01 (File quality outcome) = **Fail**
   - Q-FQTAX-03 (Remedial action required?) = **No**
   All four `al_response` writes (plus Q-TAX-01's reason) returned HTTP 204 with **no refusal**
   - the first proof that `ResponseGuardPlugin` no longer blocks a contradicting answer. Reading
     the rows back afterwards showed Q-FQTAX-03 still held **No**, unchanged by
     `ResponseProgressPlugin` - the second proof: the server no longer auto-corrects a
     contradicted answer either.
   - The one answer this scenario needed that genuinely doesn't exist on Tax (`al_responsetype`
     120910005, Pass/Fail only) was **Pass with issues** itself - that half of the brief's
     scenario was completed on the AQS review instead (below), where the grade scale exists.
3. `al_SubmitReview` succeeded (`Status: "Submitted"`, `al_submittedon` 2026-10-05T20:17:50Z).
   The outbox then held exactly **one** row (`REVIEWSUBMITTED-...`, no remediation letters) -
   correct per `Remediation.cs`'s own doc comment: a Tax fail defers raising remediation to the
   AQS submit (AD-184), so there was nothing to prove yet about the new letters from this review
   alone.
4. A second `al_AssignCase` on the same case (new `IdempotencyKey`, no `ReviewInstanceId`)
   resolved the case's next-due check and created the **AQS** review
   (`c75d875b-fac0-f111-aaad-70a8a5b3561e`), also assigned to Service Account.
5. Answered all 36 currently-mandatory AQS question versions in one `webapimany` batch (0
   failures), including the brief's scenario literally this time:
   - Q-GR-01 (Advice Quality Grade) = **Pass with issues**
   - Q-E3-02 ("Reasonable alternatives considered or explained", a real test point) = **Fail**
   - Q-FQ-03 (Remedial action required?) = **No**
   - every other mandatory question answered Pass/Yes so the submit would not refuse on an
     unrelated unanswered row.
6. Parked the remedial-action text via `PATCH al_reviewinstances(...).al_pendingremedialactions`
   and called `al_SubmitReview`. The first attempt (with the wrong item key) was refused with
   `PRECONDITION: Write the remedial action for 'Reasonable alternatives considered or
   explained: Fail' under 'Fail points and remedial actions' before submitting.` - which is
   itself a proof that `RemedialActions.Refusal` still gates an itemised fail point correctly.
   Corrected and resubmitted: **Status: "Submitted"**.
   - **Timing.** The Tax submit (step 3) took ~2m17s wall-clock; the AQS submit (step 6) took
     **15.6s**. Both numbers are dominated by the registration tool's own interactive-OAuth
     sign-in from a cold process (`dataverse-registration-token-gap`/
     `registration-tool-token-contention`), not by the Dataverse message itself - the second
     call reused a warm sign-in. Neither number is a clean measurement of `SubmitReviewPlugin`'s
     own execution time.
7. **The outbox** (`al_notifications?$filter=al_targetid eq 'c75d875b-...'`) held exactly the
   rows the brief predicted, plus the unchanged submitted-review letter:

   | `al_notificationcode` | Recipient | Subject | `al_attachmentname` |
   |---|---|---|---|
   | `REMEDIATIONASSIGNED-C75D875B...` | `svc.automate.aq@ascotlloyd.co.uk` (adviser) | "Remedial needed - Pass with issues: 920930001" | `Tax check 920930001.pdf\|AQS check 920930001.pdf` |
   | `REMEDIATIONASSIGNED-C75D875B...-PARAPLANNER` | `paraplanner.3@ascotlloyd.co.uk` | **"Checks and remedial points: 920930001"** | `Tax check 920930001.pdf\|AQS check 920930001.pdf` |
   | `REVIEWSUBMITTED-C75D875B...` | `paraplanner.3@ascotlloyd.co.uk` | "Review submitted on case 920930001" | `Tax check 920930001.pdf\|AQS check 920930001.pdf` |

   The para-planner's new letter is its own outbox row (not a copy of the adviser's), addressed
   through `NotificationOutbox.ParaplannerEmail`, with the exact subject format the spec names,
   and carries the same attachments as the adviser's - proving design sections 1 and 2 together.
8. **Decoded both attachments** (`al_attachmentbody`, pipe-separated per
   `NotificationOutbox.AttachmentSeparator`, base64 each) from the para-planner's row:
   - `Tax check 920930001.pdf` - 13,347 bytes, valid `%PDF-`, **no** "Remedial actions" section
     of its own. Correct: this check's Fail was deferred to the AQS submit (AD-184), so it owns
     no `al_remediationaction` row under its own review id.
   - `AQS check 920930001.pdf` - 38,462 bytes, valid `%PDF-`. The **last** content block in the
     page stream (immediately before `ET`/`endstream`, nothing drawn after it) is the "Remedial
     actions" table, with headers `No. | Fail point | Remedial action | Owner | Target date |
     Status` exactly as the spec names them, and two rows:
     1. `Tax check: Fail` - the item deferred from the Tax review, drawn under the AQS check as
        `CheckRemedialActions`'s own doc comment says it would be.
     2. `Reasonable alternatives considered or explained: Fail` - the AQS review's own item.
     Both rows carry the text written at step 6, owner "Service Account", target date "16 Oct
     2026", status "Open".
9. **Check date (fix round 1, proof for `81694b9` - `StampCheckDate` moved before the letters
   are built).** Read `al_checkdate` back from the case via `webapi GET`:
   `al_outcomecases(cd34f49c-...)?$select=al_checkdate` -> **`"al_checkdate":"2026-10-05"`** -
   today, UK date, matching the system clock at submit time. Decoded the case-header "Check
   date" cell out of **both** check PDFs attached to **all three** outbox rows for this review
   (adviser `REMEDIATIONASSIGNED`, para-planner `REMEDIATIONASSIGNED-...-PARAPLANNER`, and
   para-planner `REVIEWSUBMITTED`):

   | Outbox row | `Tax check 920930001.pdf` | `AQS check 920930001.pdf` |
   |---|---|---|
   | Adviser `REMEDIATIONASSIGNED` | Check date: **05 Oct 2026** | Check date: **05 Oct 2026** |
   | Para-planner `REMEDIATIONASSIGNED-...-PARAPLANNER` | Check date: **05 Oct 2026** | Check date: **05 Oct 2026** |
   | Para-planner `REVIEWSUBMITTED` | Check date: **05 Oct 2026** | Check date: **05 Oct 2026** |

   All six readings agree with each other and with `al_checkdate` - no stale or blank date
   anywhere. The byte counts are identical across all three rows for the same filename
   (`Tax check 920930001.pdf` 13,347 bytes every time; `AQS check 920930001.pdf` 38,462 bytes
   every time), which says the three outbox rows carry the *same* generated documents rather
   than three independently-rebuilt copies - consistent with the design's "the remediation
   letters are queued while the first action is being created... `SubmitReviewPlugin`
   re-attaches the documents to both remediation rows for the review after `Remediation.Raise`
   returns", and with `REVIEWSUBMITTED` picking up `CompletedCheckPdf.Documents` the same way.
   Nothing here suggests `StampCheckDate` ran late or the PDF renderer read a stale cached row.
10. **5(c), best effort.** Both actions were completed (`al_CompleteRemediation`, after patching
   `al_adviserresponse`/`al_actionperformed`), then **sign-off approve** was called
   (`al_SignOffRemediation {Decision:"Approved"}`, targeting one action's id) - **succeeded**
   (`Status: "Approved"`), and signed off **both** of the case's actions in the one call (case
   moved to `al_casestatus` 120910589). This did *not* hit the T&C-mapping refusal recorded in
   `dev-signoff-needs-tc-mapping` (2026-09-29), because this case's adviser email is the signed-in
   identity's own (`svc.automate.aq@ascotlloyd.co.uk`), not a separately-mapped third party -
   worth re-testing on a case with a genuinely different, mapped adviser, but that is a new case
   to find or seed, not something this run worked around.
   An **adviser-email edit** (`al_UpdateCaseDetails`, re-saving `al_adviseremail` to its current
   value - the harmless-write pattern from `no-impersonation-in-dev`) on this same case, now
   carrying signed-off actions, also **succeeded** with no privilege fault. Both results are
   consistent with the new attach-PDFs-inside-the-transaction code not faulting the surrounding
   command.
11. **5(d) and the rest of Step 5 - BLOCKED, not worked around.** Steps 5.1 (confirm nothing is
    greyed out or pre-ticked on the open page), 5.6 (reload the submitted review and confirm the
    read-only "Remedial actions" section and Save-as-PDF) and 5.7 (open the case in the Code App
    and confirm the panel) all need a live portal/Entra browser session.
    `app/e2e/.auth/portal.json` (captured 2026-10-02) no longer carries one: navigating to the
    review redirected to `/SignIn`; driving the portal's own "Microsoft Entra ID" link redirected
    to `login.microsoftonline.com` and rendered **"svc.automate.aq@ascotlloyd.co.uk / Enter
    password / Forgot my password"** - an interactive credential prompt this agent cannot
    satisfy. Per the task's hard limit this was not pushed further (no typing, no guessing at a
    password). The Code App player reuses the same Entra cookies
    (`portal-session-opens-code-app`), so it is blocked for the identical reason, and 5(d)
    (whether the submitted page needed the upper-case-GUID fetch-cache workaround) could not be
    observed either. **Fix:** run `npm run e2e:auth` interactively from `app/` with
    `OT_PORTAL_URL=https://outcometesting.powerappsportals.com` (a human types the password;
    Entra's own SSO will likely skip the rest), then reopen:
    - review `https://outcometesting.powerappsportals.com/review?id=c75d875b-fac0-f111-aaad-70a8a5b3561e`
      for 5.6 (and 5.1 against a fresh, unanswered review if that still matters once this one is
      submitted);
    - the Code App play URL above, case reference **920930001**, for 5.7.

    What is already proven without the browser: the template actually live on DEV carries no
    gating script and 11 `data-ot-remedial` markers (Step 3's readback), and the Code App bundle
    actually live on DEV contains the "Remedial actions" panel text (Step 4) - so the code that
    would render these sections is confirmed deployed, even though its on-screen rendering is
    not confirmed in this run.

## DEV mailbox

Unchanged from `dev-drains-the-outbox-itself`: `NotificationDrainPlugin` is still enabled, so
every `al_notification` row above reached `al_status` 120910811 (`StatusSent` -
`NotificationOutbox.StatusSent`, meaning drained into an `email` activity, not delivered) within
seconds. The `email` activities it created (including a bonus one, "Remediation approved on case
920930001", queued by the sign-off at step 9) all sit at `statuscode` **6 - Pending Send**: the
sending mailbox still has server-side sync disabled, so nothing left DEV. The proof is the
outbox rows and their attachments, decoded above, not received mail.

## TEST and PROD

Not deployed. Promotion is the owner's call.

## Follow-up fixes, same evening (DEV only)

Four small follow-ups from the branch review, pushed to DEV at 21:13Z:

| Fix | Commit | Pushed |
|---|---|---|
| Code App case page leaves out deactivated actions, and its table now shares the checks table's styling | `30794a3` | Code App bundle `index-4cQH5Fk1.js`, play URL `…/app/5d9fc475-ee75-4386-917e-fc182307b0c2?…&sourcetime=1791234839556` |
| Portal review page treats remedial text, owner or status of only spaces as empty, as the emailed document does | `c4458e8` | `OT Review Detail` 255,261 → 255,727 chars |
| Both PDFs draw an action's text, owner and status from one set of helpers (no behaviour change) | `5dd313b` | Assembly 381,952 bytes, sha256 `583e8b981127ced3f3f1b0ce876f4fd1dc9a5b587f9a5259bb24c27d9a18eb63` |

Plug-in tests 1796/1796, app tests (cases, remediation, reviews) green, `tsc -b` clean. Not seen in a browser yet, for the same expired-session reason as above.

TEST and PROD: not deployed.

## TEST (`org37995f36`), 2026-10-05 - imported; one owner step left

The owner said "deploy to TEST".

- **Package:** DEV bumped to **1.0.21.0** and exported managed through the Web API
  (`ExportSolution`). `artifacts\2026-10-05-remediation-documents\OutcomeTesting_1_0_21_0_managed.zip`
  (gitignored), sha256 `3012264549e73444064a9ef26dfdce5da9eb88439e86ce4e1e67be74a00cfacd`. It
  carries assembly sha256 `583e8b98…` (equal to the local Release build), Code App bundle
  `index-4cQH5Fk1.js` and the current OT Review Detail. This work added no component, so the
  1.0.20.0 membership audit still holds.
- **Before the import:** OT Review Detail on TEST has two layers, `Active` over `OutcomeTesting` -
  the direct push of 2026-10-01 still masks managed imports of it.
- **Import:** `ImportSolutionAsync`, `PublishWorkflows: true`, `OverwriteUnmanagedCustomizations:
  false`, job `d6b84e9d-30fa-4db7-9507-3833166a2cb8`, async operation
  `21fbeacc-02c1-f111-aaaf-6045bd0aeb46`. Succeeded (statuscode 30) at 21:23:20Z.
- **Read back from TEST:**
  - solution 1.0.21.0, managed;
  - assembly sha256 `583e8b981127ced3f3f1b0ce876f4fd1dc9a5b587f9a5259bb24c27d9a18eb63`, as DEV;
  - all 67 steps of `OutcomeTesting.Plugins` enabled;
  - Code App `appversion` 2026-10-05T21:23:01Z;
  - **OT Review Detail does not equal the repo** (276,546 chars, the masked copy): the import
    landed beneath the `Active` layer, as expected.
- The agent's `pushwebtemplate` to TEST was refused ("Production Deploy"). Until it runs, TEST's
  review page still greys out and pre-ticks answers (the server no longer refuses them) and a
  submitted review does not list its remedial actions. Every other change is live in TEST.

### The owner's step

Run from the repo root. The template reads only columns TEST already has, so it is safe after
the import:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; $t='C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Registration\bin\Release\net8.0\OutcomeTesting.Registration.exe'; & $t pushwebtemplate https://org37995f36.crm11.dynamics.com/ a1000000-0000-4000-8000-00000000001b C:\Users\rsimu\OutcomeTesting\powerpages\outcome-testing---outcometesting\web-templates\ot-review-detail\OT-Review-Detail.webtemplate.source.html
```

It should print `pushed web template 'OT Review Detail' … 276546 -> 255727 chars`.

## PROD (`org3461d426`, OTIS) - 2026-10-06

The owner said "promote solution to prod, ensure all the components are in the solution".

- **Membership audit in DEV** (`audit-dev.req.json` plus `metadatamembership`). Every component
  in DEV is in `OutcomeTesting`:
  - the site, all 298 portal components;
  - 32 custom APIs, their 140 parameters and 115 response properties;
  - the assembly and its 35 registered steps. The other 32 steps are the platform's custom-API
    implementation steps, which travel with their APIs.
  - the Code App, the 3 security roles, and all 28 `al_` tables.
  - Not gaps: `powerpages_sharedwithusers_...` is Power Pages' own sharing variable, and
    `al_PortalBaseUrl` is set per environment.
- **Package:** the same `OutcomeTesting_1_0_21_0_managed.zip` that went to TEST (sha256
  `30122645...`, unchanged; nothing changed in DEV after that export). It went through
  `brandpackage ... OTIS`, which rewrote 7 labels. The result is
  `artifacts/2026-10-06-prod-promotion/OTIS_1_0_21_0_managed.zip`, sha256 `d785d37e...`.
- **Before the import:** PROD was at 1.0.20.0 OTIS, `al_ProductName` = OTIS, and no steps were
  disabled. OT Review Detail had only the managed `OutcomeTesting` layer.
- **Import:** `ImportSolutionAsync`, job `98635dbf-715f-4178-868c-883a70155e91`, async operation
  `ea876b40-53c1-f111-aaaf-7c1e5279965e`. It succeeded (statuscode 30) at 06:59:11Z.
- **Read back from PROD:**
  - solution 1.0.21.0, managed, OTIS;
  - assembly sha256 `583e8b98...9a18eb63`, the same as DEV and the local Release build;
  - all 67 steps enabled;
  - Code App OTIS, `appversion` 2026-10-06T06:58:43Z;
  - roles OTIS Team Manager, App User and App Admin;
  - OT Review Detail still has one managed layer. Its stored source is identical to DEV's
    (255,727 chars), with the remedial actions section and without the old gating script.
- No behaviour test was run in PROD, because raising or answering a check there would change
  real case data.
