# People are identified by email - DEV deployed and proved (AD-228)

Date: 2026-10-04. Branch `feat/otis-product-name`, code at `b13fb6b`.
Spec: `docs/superpowers/specs/2026-10-02-email-person-identity-design.md`.
Plan: `docs/superpowers/plans/2026-10-04-email-person-identity.md`.

## What was reported

PROD case **256497798** names its adviser "Adam Strumidlo". His contact was onboarded as "Adam
Strumdlio", a misspelling in the source workbook. Remediation was assigned by matching
`al_advisername` to `contact.fullname`, so nothing matched and the action was raised unassigned.
His email was right everywhere. The owner's direction (2026-10-02): "This should use email
instead of name for matching. The whole system needs to do that as 2 people can have the same
name."

## What changed

| Part | Change |
|---|---|
| Resolver | `NotificationOutbox.MatchPerson` resolves by `emailaddress1` only: active contacts, `TopCount 2`, so two contacts holding one email resolve to nobody. The name branches in `Remediation.AdviserContact` and `CaseAdviser` are gone. `al_advisername` and `al_paraplanner` are labels. |
| Reads | Remediation assignment, adviser access (`al_advisercontactid`), the "Case passed" letter, T&C routing, paraplanner letters and export staff codes all read the stored email. |
| Import | `ImportRules` rejects a row without `AdviserName`, `AdviserEmail`, `AssignedBy` or `ParaplannerEmail`, or with an email that is not an address. The Code App's `caseUpload.ts` mirrors it. |
| Edits | `UpdateCaseDetailsPlugin` (Code App) and `CaseHeaderRequestPlugin` (portal header) refuse a name sent without its email. An adviser email change moves the open actions, re-runs access and re-routes sign-off, and writes "Re-pointed ..." or "Unassigned ..." in the audit line. A name-only change moves nothing. |
| New step | `AdviserContactPlugin`, synchronous post-operation on `contact` Create, and on Update of `emailaddress1,statecode`. When a contact appears, or its email or state changes, the step assigns the open actions on cases whose adviser email is that contact's email. Completed actions never move. |
| Code App | The adviser and paraplanner pickers write a name and an email together. People, workloads, the worklist and report filters group by email. |
| Portal | `OT Review Detail`: the header has an email field beside each person, sent as a pair. `OT Remediation`: the hint names the email ("no portal contact for ..." / "two contacts share ..."). |
| Registration tool | New `backfillpeopleemail <org> [--confirm <org>] [--include-mismatch]`. `setcasepeople`, `repointremediation` and `setcaseadviser` take emails. |

## Why the order matters

The backfill must run, be reviewed and be confirmed while the OLD plug-ins are still live,
before the new assembly or solution lands. The old plug-ins already read the email first, so
running the backfill under them is safe. Some legacy cases have a blank adviser or paraplanner
email. Without the backfill, those cases would lose remediation access, T&C routing and letters
the moment the new email-only plug-ins, portal and Code App land, because those refuse a name
without an email. There would be a window in which those cases had neither a usable name match
nor a filled email. Filling the emails first, under the old code, closes that window before the
new code reads them.

Two further rules:
- The templates and the Code App never go before the plug-ins. The old allowlists refuse
  `al_adviseremail`.
- The second backfill after the deployment is idempotent, and catches cases created in between.

## DEV (`https://org0b075da8.crm11.dynamics.com`)

| Step | Command | Result |
|---|---|---|
| 2. Backfill dry run | `backfillpeopleemail <DEV>` | 16 cases read. **FILL 6, NO FILL 0, MISMATCH 0**, case-level **UNASSIGN 3**. No per-action MOVE or UNASSIGN lines |
| 3. Backfill confirm (old plug-ins live) | `backfillpeopleemail <DEV> --confirm <DEV>` | "Filled 6 email(s); re-pointed 0 open action(s)". Read back: all six cases (900000001-006) hold the paraplanner email |
| 4. Assembly | `dotnet build OutcomeTesting.Plugins -c Release`, then `pushassembly` | 381,952 bytes. sha256 `9aceefc64b5db42b40480a48f13f0190c7123ef396374665d4932dc9d8052fba` for both the local DLL and the `pluginassembly.content` read back from DEV |
| 4. Plug-in type | `registertype <DEV> OutcomeTesting.Plugins.AdviserContactPlugin` | `2e5c3fd7-e3bf-f111-aaad-70a8a5b3561e`. `registerstep` refuses a type that is not registered |
| 4. Steps | `registerstep ... Create contact 40 "" sync` and `... Update contact 40 "emailaddress1,statecode" sync`, then `addcomponent 92` | Create `a52fa2e2-e3bf-f111-aaad-70a8a5b3561e`, Update `6efc79ea-e3bf-f111-aaad-70a8a5b3561e`. Both read back as enabled (statecode 0), stage 40, sync, and members of OutcomeTesting |
| 5. Templates | DEV's copies diffed first, then `pushwebtemplate` | DEV held exactly the pre-branch versions: OT Review Detail matched `2066e9c` apart from a leading BOM, and OT Remediation matched `8a37b2b`. Pushed: OT Review Detail (`...001b`) 275,265 to 276,546 chars; OT Remediation (`...0019`) 148,487 to 149,812 chars. Read back, both equal the repo at `b13fb6b` |
| 5. Code App | `npm run build`, then `npx pa app push` | Bundle `index-6HpkwRLj.js` holds `al_adviseremail`. Pushed: `canvasapp.lastpublishtime` 11:13:41Z. Play URL: `.../app/5d9fc475-ee75-4386-917e-fc182307b0c2?...&sourcetime=1791112420979` |
| 6. Backfill again | `backfillpeopleemail <DEV> --confirm <DEV>` | 0 to fill, 0 re-pointed. The same 3 UNASSIGN lines |
| 6. Access | `reconcileaccess <DEV> --confirm <DEV>` | 16 active cases. 0 changed, 0 failed. 5 "UNMATCHED supervisor" (910000001, 910000003, 910000004, 900000003, 920929001): these advisers have no T&C mapping in DEV, which was already the case |

The three UNASSIGN lines are 920929001, 920929002 and 920930001. Their paraplanner email
`paraplanner.3@ascotlloyd.co.uk` belongs to no contact. Paraplanners need not be contacts
(BR-009), and letters use the email directly, so only the export staff code is affected. They
were accepted as they are.

## DEV proof (Step 7)

Every value below was read back from DEV after the action. Everything ran as
`svc.automate.aq`, server-side, through the same Custom APIs the Code App and the portal call.

**1. Import.** `al_ImportCases` with a two-row extract, `email-proof-two-rows.csv`, batch
`BATCH-20261004112423-54C341`:

| Row | Result |
|---|---|
| 941004001, AdviserEmail `email.proof.twin.a@example.com` | Imported. The report row also says "para-planner unmatched", because no contact holds the proof paraplanner email |
| 941004099, AdviserEmail blank | **Invalid**: `"AdviserEmail" is empty. People are identified by email, so a case cannot be created without it.` |

Totals: 2 rows, Imported 1, Failed 1. No case 941004099 exists.

**2. Misspelled name.** Case 900000003 (Awaiting Remediation) has one open action,
`01d09ada-58b8-f111-aaad-0022481b59dd`. Through `al_UpdateCaseDetails`, its adviser was set to
"Adam Strumidlo", `email.proof.adam@example.com`. That email belongs to the contact
**"Email Proof Adam Strumdlio"** (`c4571875-…`), whose name is spelled differently.

- Read back: the action's `al_assignedcontactid` is `c4571875-e5bf-f111-aaad-70a8a5b3561e`, and
  so is the case's `al_advisercontactid`.
- Audit: `…; Re-pointed 1 open remediation action(s) to the adviser email now on the case`.

**3. Contact appears later.**
- The same case was set to "Email Proof Later", `email.proof.later@example.com`. No contact held
  that address. The action's `al_assignedcontactid` read back **null**. Audit: `Unassigned 1 open
  remediation action(s): no single active contact holds the adviser email`.
- The contact "Email Proof Later" (`a273b08e-e8bf-f111-aaad-70a8a5b3561e`) was then created. The
  case was not edited. The action read back `al_assignedcontactid` =
  `a273b08e-e8bf-f111-aaad-70a8a5b3561e`, modified 11:41:38Z, the contact's creation second. The
  case's `al_advisercontactid` followed as well.

**4. Two people with one name.**
- Two contacts were created, both named **"Email Proof Twin"**: A `c0571875-…`
  (`email.proof.twin.a@example.com`) and B `c2571875-…` (`email.proof.twin.b@example.com`).
- Cases 941004001 (adviser email A) and 941004002 (adviser email B) were each assigned
  (`al_AssignCase`), answered as a fail, and submitted (`al_SubmitReview`). So the remediation
  was raised by the new code.

| Case | Action | `al_assignedcontactid` | `al_advisercontactid` |
|---|---|---|---|
| 941004001 | `7ba0821f-e8bf-f111-aaad-70a8a5b3561e` | A `c0571875-e5bf-f111-aaad-70a8a5b3561e` | A |
| 941004002 | `84a0821f-e8bf-f111-aaad-70a8a5b3561e` | B `c2571875-e5bf-f111-aaad-70a8a5b3561e` | B |

The "Remedial needed" letters were queued to `email.proof.twin.a@…` for 941004001 and to
`email.proof.twin.b@…` for 941004002. DEV delivers no email, so they rest queued.

**The contact Update step**, on 941004002:
- Another contact was given B's email, so two contacts held it. The action read back null.
- That contact's email was put back. The action stayed null, as designed: the step looks up
  cases by the contact's new email.
- B's own email was then re-saved (a correction). The action read back B `c2571875-…`.

**5. Portal.** The page's save is a PATCH of `contact.al_caseheaderrequest`. That PATCH was
replayed server-side on the Service Account contact, which holds the unsubmitted Tax check on
920929002, with the payload shape `OT Review Detail` sends:
- `fields` holding the adviser name alone was **refused**: `VALIDATION: Give the adviser's email
  as well as their name. People on a case are identified by email, because two people can share
  a name.` The case was unchanged.
- Name and email together: accepted. The case read back "Dev Account" /
  `svc.automate.aq-dev@ascotlloyd.co.uk`. Audit: `Case header edited from the portal: Adviser
  'Service Account' -> 'Dev Account'; Adviser email '…aq@…' -> '…aq-dev@…'.`
- The case was then restored the same way, and the restore read back. The contact's request
  column read back null.

There was no "Re-pointed" line on that case, because it has no open action. **A portal header
save cannot produce "Re-pointed" in the normal lifecycle.**
- `CaseHeaderRequestPlugin.EnsureAssignedToCase` lets only a checker with an **unsubmitted**
  review on the case edit its header.
- A case has open actions only after its reviews are submitted.

The "Re-pointed" wording comes from `Remediation.ApplyAdviserEmailChange`, which both edit paths
share. It was observed on the Code App path in items 2 and 3.

**Not observed in a browser.** Both stored browser sessions had expired
(`app/e2e/.auth/portal.json` redirects to `/SignIn`; the Code App player redirects to Entra
sign-in), and recapturing one is interactive. These checks are the owner's:
- the Code App upload screen with a two-row file. The client-side check should reject the
  blank-email row before sending;
- the People page showing "Email Proof Twin" as **two** rows;
- that the Code App player serves `index-6HpkwRLj.js`;
- the portal header email fields on a review page.

**Left in DEV as proof data**, for the owner's browser checks and for cleanup later:
- contacts "Email Proof Twin" ×2, "Email Proof Adam Strumdlio" and "Email Proof Later";
- cases 941004001 and 941004002, with their reviews, responses, two open actions and six queued
  letters;
- import batches `BATCH-20261004112423-54C341` and `BATCH-20261004112450-7868E2`.

Case 900000003 was restored to "Service Account" / `svc.automate.aq@ascotlloyd.co.uk`, and its
action read back held by Service Account again. Case 920929002 was restored too. Audit events
are immutable and remain.

## TEST, 2026-10-04 - imported, three owner steps left

The agent did the following:
- **Solution check in DEV:** every component type was checked against the solution, and none
  is missing. That covers 298 portal components, 32 custom APIs with 140 parameters and 115
  response properties, 35 steps (all enabled), the Code App, 3 roles, the assembly, every
  `al_` table, and `al_ProductName` / `al_NotificationSenderAddress`. `al_PortalBaseUrl` stays
  out on purpose, because it is per environment.
- **Packages:** DEV was bumped to 1.0.19.0 and exported managed through the Web API (`pac`'s
  sign-in was revoked). The two packages are in
  `artifacts\2026-10-04-email-identity\` (gitignored):
  - `OutcomeTesting_1_0_19_0_managed.zip`, sha256 starting `e2d19db6`;
  - `OTIS_1_0_19_0_managed.zip`, sha256 starting `e70daa07`, branded with 7 labels.
  - Both carry assembly `9db97b8d…`, the two AdviserContactPlugin steps with the PreImage, and
    Code App bundle `index-Bhii7iBv.js`.
- **TEST dry run (read only):** 48 cases.
  - 25 FILL lines, all paraplanner, mostly `adam.strumidlo@`.
  - 23 NO FILL lines: paraplanners with no contact, mainly Jessica Bell and Matheau Frith.
  - 1 MISMATCH: test case 900000004.
  - 20 case-level UNASSIGN notes: `matthew.hall@` and `svc.automate.aq-dev@` match no TEST
    contact.
  - No MOVE or UNASSIGN action lines, because TEST has no held open action. No adviser email
    is blank.
- **Import into TEST:** the import ran through `ImportSolutionAsync` (job
  `d0d1ef6c-1b73-4ee9-a508-faddc5875d21`) and succeeded. Read back from TEST:
  - solution 1.0.19.0, managed;
  - assembly sha256 `9db97b8d…`;
  - all our steps enabled, with the Update step carrying `PreImage` / `emailaddress1`;
  - Code App `appversion` 2026-10-04T13:53:36Z.
- **Templates:** every TEST web template and page equals DEV's, except **OT Review Detail**.
  The direct push of 2026-10-01 still masks it.

The agent's attempts at the backfill `--confirm` and the template push were both refused as
shared-resource writes, so these are the owner's:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$t='C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Registration\bin\Release\net8.0\OutcomeTesting.Registration.exe'
$org='https://org37995f36.crm11.dynamics.com'
# 1. Fill the 25 paraplanner emails (moves no action in TEST)
& $t backfillpeopleemail $org --confirm $org
# 2. Unmask the review page (Clear fix, email fields)
& $t pushwebtemplate $org a1000000-0000-4000-8000-00000000001b C:\Users\rsimu\OutcomeTesting\powerpages\outcome-testing---outcometesting\web-templates\ot-review-detail\OT-Review-Detail.webtemplate.source.html
# 3. Reconcile access with the new rules
& $t reconcileaccess $org --confirm $org
```

The Update step arrived with its image in the same import, so it should not need the step
touch that DEV needed. Prove it after the first contact email change in TEST: the newest
`AdviserContactPlugin` row in `plugintracelogs` should read "Pre-image present".

## 2026-10-05 - deployed to all three, with the owner's permission

The owner said "deploy to all 3 environments. You have permissions to do so", and the agent
ran every step.

- **DEV:** `Q-E2-LENS` retired (AD-229, audit `f41f52d0-86c0-f111-aaad-70a8a5b3561e`).
- **TEST:**
  - Backfill `--confirm`: 25 paraplanner emails filled, 0 actions moved.
  - OT Review Detail pushed (274,224 -> 276,546 characters), so the masked copy is gone.
  - `Q-E2-LENS` retired.
  - `reconcileaccess`: 5 changed, 15 released with no adviser contact (`matthew.hall@` and
    `svc.automate.aq-dev@` match no TEST contact), 0 failed.
- **PROD (OTIS):**
  - **Dry run:** 19 FILL lines (12 Adam Strumidlo, 7 Zoe Ramwell, all paraplanner) and one
    MISMATCH, case 256497798. Its adviser name reads "Adam Strumdlio", but its email belongs to
    contact "Adam Strumidlo", and all six of its actions were already completed by Adam.
  - **Backfill `--confirm`:** 19 filled, 0 moved.
  - **Import:** OTIS 1.0.19.0 imported (job `54e038cd-f93d-44d8-a165-e16356543ad5`), succeeded.
    Read back:
    - 1.0.19.0, managed, OTIS;
    - assembly `9db97b8d…`;
    - all 67 steps enabled, with the Update step carrying `PreImage` / `emailaddress1`;
    - Code App 2026-10-05T06:44:11Z;
    - `al_ProductName` OTIS.
  - **Portal:** all 60 web templates and pages equal DEV's, so nothing is masked.
  - **`reconcileaccess`:** 2 changed, 0 released, 0 failed. Case 256497798 now has Adam as
    `al_advisercontactid`, by email, and its T&C supervisor.
  - `Q-E2-LENS` retired.
- **Not done:** the agent did not "touch" the PROD or TEST Update step to refresh its image,
  because writing a managed step leaves an unmanaged layer on it. The image came with the step
  in the same import, unlike DEV. Prove it after the first contact email change: the newest
  `AdviserContactPlugin` trace row should read "Pre-image present".
- `al_NotificationSenderAddress` is still empty in TEST and PROD, so email leaves as before
  until the shared-mailbox queue is set up (AD-227).

## TEST and PROD - the owner's steps

Writes there are the owner's. Run them from the repo root, in this order. TEST is
`https://org37995f36.crm11.dynamics.com`; PROD (OTIS) is `https://org3461d426.crm11.dynamics.com`.

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$t='C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe'
$org='https://org37995f36.crm11.dynamics.com'   # then again with https://org3461d426.crm11.dynamics.com for PROD
```

The exe must be rebuilt from this branch first
(`dotnet build C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Registration -c Debug`). An
older exe does not know `backfillpeopleemail`, and falls through to `pushassembly`.

**1. Backfill dry run, BEFORE any assembly push or import.** Review every NO FILL, MISMATCH,
UNASSIGN and MOVE line. Fix those by hand, or accept them knowingly.

```powershell
& $t backfillpeopleemail $org
```

**2. Backfill confirm, while the OLD plug-ins there are still live.** A MISMATCH case's actions
are left alone unless `--include-mismatch` is added.

```powershell
& $t backfillpeopleemail $org --confirm $org
```

**3. Managed export from DEV, then import.** Done on 2026-10-04: both packages are in
`artifacts\2026-10-04-email-identity\`, as listed above. Use those files. The commands below
are only for rebuilding them:

```powershell
pac solution online-version --solution-name OutcomeTesting --solution-version 1.0.19.0 --environment https://org0b075da8.crm11.dynamics.com/
New-Item -ItemType Directory -Force C:\Users\rsimu\OutcomeTesting\artifacts\2026-10-04-email-identity
pac solution export --name OutcomeTesting --managed --overwrite --environment https://org0b075da8.crm11.dynamics.com/ --path C:\Users\rsimu\OutcomeTesting\artifacts\2026-10-04-email-identity\OutcomeTesting_1_0_19_0_managed.zip
```

TEST imports the export as it is. PROD imports the OTIS-branded copy:

```powershell
& $t brandpackage C:\Users\rsimu\OutcomeTesting\artifacts\2026-10-04-email-identity\OutcomeTesting_1_0_19_0_managed.zip C:\Users\rsimu\OutcomeTesting\artifacts\2026-10-04-email-identity\OTIS_1_0_19_0_managed.zip OTIS
```

The import script always passes `--activate-plugins`:

```powershell
# TEST
& C:\Users\rsimu\OutcomeTesting\scripts\Import-Solution.ps1 -Environment https://org37995f36.crm11.dynamics.com/ -ZipFile C:\Users\rsimu\OutcomeTesting\artifacts\2026-10-04-email-identity\OutcomeTesting_1_0_19_0_managed.zip -Managed
# PROD
& C:\Users\rsimu\OutcomeTesting\scripts\Import-Solution.ps1 -Environment https://org3461d426.crm11.dynamics.com/ -ZipFile C:\Users\rsimu\OutcomeTesting\artifacts\2026-10-04-email-identity\OTIS_1_0_19_0_managed.zip -Managed
```

The script can report "Import complete" when `pac` never connected. So read the version back,
and check that both `AdviserContactPlugin` steps arrived **enabled**:

```powershell
& $t webapi $org GET 'solutions?$select=version,ismanaged&$filter=uniquename eq ''OutcomeTesting'''
& $t webapi $org GET 'sdkmessageprocessingsteps?$select=name,statecode,filteringattributes&$filter=startswith(name,''AdviserContactPlugin'')&$expand=sdkmessageprocessingstepid_sdkmessageprocessingstepimage($select=name,attributes)'
```

You should see 1.0.19.0, managed, and two steps with statecode 0. The Update step must carry
one image, `PreImage`, with `attributes` `emailaddress1`. Without it the step follows only a
contact's new email, and cases still carrying the old address keep that contact's actions.
`verifysteps` in the import script reads `src/SdkMessageProcessingSteps`, which carries both
steps since 8b18ae4. It does not check images, so this query stays the check for the pre-image.
- If the steps arrived disabled:
  `& $t setstepstate $org enable "AdviserContactPlugin: Create of contact" "AdviserContactPlugin: Update of contact"`.
- If the import did not carry them, register them as DEV did:

```powershell
& $t registertype $org OutcomeTesting.Plugins.AdviserContactPlugin
& $t registerstep $org OutcomeTesting.Plugins.AdviserContactPlugin Create contact 40 "" sync
& $t registerstep $org OutcomeTesting.Plugins.AdviserContactPlugin Update contact 40 "emailaddress1,statecode" sync
```

  then give the Update step its pre-image, with the step id the query above returns (DEV's is
  `6efc79ea-e3bf-f111-aaad-70a8a5b3561e`, its image `37a4deea-efbf-f111-aaad-70a8a5b3561e`):

```powershell
Set-Content -Encoding utf8 $env:TEMP\preimage.json '{"name":"PreImage","entityalias":"PreImage","imagetype":0,"messagepropertyname":"Target","attributes":"emailaddress1","sdkmessageprocessingstepid@odata.bind":"/sdkmessageprocessingsteps(<updateStepId>)"}'
& $t webapi $org POST sdkmessageprocessingstepimages "@$env:TEMP\preimage.json"
```

  **Then touch the step**, or the image is not delivered. In DEV on 2026-10-04 the image row
  was correct, but the running step went on without it until the step itself was written. The
  step's trace line read "Pre-image absent". Any write to the step refreshes it, for example its
  description:

```powershell
Set-Content -Encoding utf8 $env:TEMP\stepdesc.json '{"description":"Follows a contact email change to the cases carrying the new and the old address. PreImage: emailaddress1."}'
& $t webapi $org PATCH 'sdkmessageprocessingsteps(<updateStepId>)' "@$env:TEMP\stepdesc.json"
```

  Check it after any contact email change: the newest `AdviserContactPlugin` row in
  `plugintracelogs` should read "Pre-image present". It records no addresses.

**4. Templates and Code App.** Both arrive with the same import, never before the plug-ins: the
old allowlists refuse `al_adviseremail`. `pa app push` only ever reaches DEV.

On TEST, `OT Review Detail` still holds a TEST-only copy from an earlier direct push
(2026-10-01). A pushed copy hides managed imports of it. After the import, diff TEST's
`OT Review Detail` (`a1000000-0000-4000-8000-00000000001b`) and `OT Remediation`
(`a1000000-0000-4000-8000-000000000019`) against the repo. Push any that differ:

```powershell
& $t pushwebtemplate $org a1000000-0000-4000-8000-00000000001b C:\Users\rsimu\OutcomeTesting\powerpages\outcome-testing---outcometesting\web-templates\ot-review-detail\OT-Review-Detail.webtemplate.source.html
& $t pushwebtemplate $org a1000000-0000-4000-8000-000000000019 C:\Users\rsimu\OutcomeTesting\powerpages\outcome-testing---outcometesting\web-templates\ot-remediation\OT-Remediation.webtemplate.source.html
```

**5. Backfill again, then reconcile access.** The backfill is idempotent, and catches cases
created in between.

```powershell
& $t backfillpeopleemail $org --confirm $org
& $t reconcileaccess $org --confirm $org
```

**6. PROD only: case 256497798.** Its open action should now be held by Adam Strumidlo's
contact:

```powershell
$p='https://org3461d426.crm11.dynamics.com'
& $t webapi $p GET 'al_outcomecases?$select=al_advisername,al_adviseremail,_al_advisercontactid_value&$filter=al_casereference eq ''256497798'''
# then, with the al_outcomecaseid that returns:
& $t webapi $p GET 'al_remediationactions?$select=al_actionstatus,_al_assignedcontactid_value&$filter=_al_outcomecaseid_value eq <caseId>'
```

`_al_assignedcontactid_value` should be the contact that holds `adam.strumidlo@…`. Adam still
needs the **AL Portal - Adviser Remediation** web role before he can open the action on the
portal.

## Known limits

- Closed 2026-10-04 (second wave): a contact whose email changes **away** from a case's adviser
  email now releases that case's open actions. The Update step's pre-image gives the old email,
  and its cases are followed as well: to whoever holds that address now, or to nobody. Proved in
  DEV: moving "Email Proof Twin" A from `email.proof.twin.a@` to `...twin.a2@` unassigned case
  941004001's open action and cleared its adviser access. Moving it back returned both, and the
  data is as it was.
- Closed 2026-10-04 (second wave): the contact step skips cases with no remediation action, and
  reads the invariant access settings once per save rather than once per case. A closed case
  with remediation keeps its access check, so a deactivated contact still loses it.
- See Step 7 item 5: in the normal lifecycle, the "Re-pointed" audit line comes from the Code
  App edit, not from the portal header.
