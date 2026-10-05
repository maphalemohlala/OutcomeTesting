# The PROD lifecycle run

Written for: the owner, who runs it after importing 1.0.19.0 into PROD (OTIS).

`prod-lifecycle.e2e.ts` takes **one** test case through the whole lifecycle in PROD. It
drives the real Code App and the real portal, as one signed-in tester:

1. **Preflight.** Checks the session, that the tester is who the variables say, and every
   prerequisite below. It fails and names the first one missing. Nothing is written until
   this passes.
2. **Upload.** Uploads one case through Case intake. The case must be queued on the
   **Tax then AQS** route.
3. **Tax allocation.** Allocates the Tax check to the tester in the Code App.
4. **Tax review header.** Checks the editable header lists adviser name, adviser email,
   paraplanner, paraplanner email, in that order. Both emails must be the tester's.
5. **Tax review.** Answers the Tax review and passes it on to AQS. On the way, it types into
   the rich-text answer (Tax Remedial), presses **Clear**, and checks the answer is empty and
   the empty answer is saved.
6. **AQS allocation.** Allocates the AQS check to the tester.
7. **AQS review.** Checks the outcome lens rows keep their text and draw no tick box. Then it
   answers every test point clean and grades the advice **Pass with issues**,
   a failing grade. If the page has locked that grade, it takes the next failing one. It then
   writes the remedial action and submits.
8. **Remediation assigned.** Checks the remediation action reached the tester by **email**.
   The case names "E2E TEST Adviser", so a match by name could not have done it. The case
   must appear on the tester's Remediation list.
9. **Adviser completion.** As adviser, the tester answers "Action performed" (Yes) and
   "Client contact required?" (No), and presses **Save and sign off this remediation**.
10. **T&C sign-off.** As T&C Manager, the tester approves the remediation, with "Recheck
    required?" set to Yes.
11. **Regrade.** Records the final outcome (Pass). This closes the case.
12. **Code App checks.** The Code App must show the case **Closed**. The People page must list
    the tester once. The adviser mapping must be found by searching the tester's email and by
    searching their name as manager.

The steps run in order and stop at the first failure.

## What it writes, sends and leaves behind

**Writes.** One import batch and one case. Reference `999` + `yyMMddHHmm` (e.g.
`9992610041530`), client **"E2E TEST - do not action"**, IO reference `E2E-<ref>`. On that
case: two checks and their answers, one outcome, one remediation action, one sign-off, one
regrade, and the audit history of each.

**Sends.** Every person on the case is the tester's own address (`OT_E2E_TESTER_EMAIL`):
adviser, paraplanner, both checkers and T&C Manager. So every letter the run causes is
addressed to the tester. That includes:
- the completed-check letter for each submitted check;
- the remediation letter;
- the sign-off request;
- the outcome letters.

All of them leave from the shared mailbox (`tc.outcometesting@ascotlloyd.co.uk`), once
`al_NotificationSenderAddress` is set in PROD and its queue mailbox is approved. Until then
they wait or fail with the reason recorded. A notification marked "Sent" only means queued;
the `email` table is the proof (see "After the run").

**Leaves behind.** One **closed** test case and its records. The agent cannot delete them.
They stay until you remove them:
- **The export.** It collects closed cases. Remove the case, or leave it out, before the next
  export goes to the business.
- **Dashboards and reports.** They count it until it is removed.

**The run log.** `app/e2e/.runs/<ref>.json` (gitignored) is written after every step. It
holds the reference, the case and review ids, the import batch, the roles the portal saw,
what each review was answered with, and each step's result. A run that stops part-way still
says what it created.

**After a failure.** A partly-run case stays where it stopped. A re-run does **not** resume
it: it uploads a new case with a new reference. Use the log to find the old one.

## Prerequisites, in order

Run these from the repo root in PowerShell, one tool process at a time. Every command is a
PROD write, and every one is yours to run.

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
$t='C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Registration\bin\Release\net8.0\OutcomeTesting.Registration.exe'
$p='https://org3461d426.crm11.dynamics.com'
$me='<tester email>'          # the tester's own mailbox, as on their PROD contact
$D="$env:TEMP\ot-prod-e2e"; New-Item -ItemType Directory -Force $D | Out-Null
```

**0. 1.0.19.0 is in PROD.** The import script can report success when `pac` never
connected, so read the version back:

```powershell
& $t webapi $p GET 'solutions?$select=version,ismanaged&$filter=uniquename eq ''OutcomeTesting'''
```

**0a. The E2 outcome lens tick is retired.** Done in PROD on 2026-10-05 (AD-229). Step 7
fails if a lens row still draws a tick box.

**1. Choose the tester.** Pick a PROD user whose contact has `emailaddress1` = their mailbox.
- **Active.** The tester must be **active** and the **only** active contact with that email.
  Otherwise the remediation is raised unassigned.
- **Not a working adviser.** The run maps the tester's email to themselves as T&C Manager
  (step 5 below). A real adviser already has a mapping that routes real sign-offs.

```powershell
& $t webapi $p GET "contacts?`$select=contactid,fullname,emailaddress1,statecode&`$filter=emailaddress1 eq '$me'"
```

Keep the `contactid` for step 5.

**2. Dataverse security roles.** The tester needs Basic User and an OTIS app role. Without
Basic User the app faults on privilege checks. OTIS App Admin is the role the PROD admins
hold. `grantapprole` grants OTIS App User and skips anyone who already holds an OTIS role:

```powershell
& $t grantapprole $p $me --confirm $p
```

For OTIS App Admin, use the Power Platform admin center (PROD → Users → the tester → Manage
security roles).

**3. Roles.** The run needs these roles, granted through `al_AssignUserRole`:

| Role | What it opens |
|---|---|
| `AL Portal - OTIS Manager` | Upload, the case list, Adviser mapping and allocation of both checks, in the Code App |
| `AL Portal - Portal Administrator` | The People page in the Code App. In the seeded page permissions only this role and Administrators open it |
| `AL Portal - Tax Reviewer` | Answering the Tax check |
| `AL Portal - AQS Reviewer` | Answering the AQS check |
| `AL Portal - Adviser Remediation` | Answering the remediation action |
| `AL Portal - T&C Supervisor` | Sign-off and regrade |

`IdempotencyKey` is required. `AppRole` must be sent, empty.

```powershell
foreach ($role in 'AL Portal - OTIS Manager','AL Portal - Portal Administrator','AL Portal - Tax Reviewer','AL Portal - AQS Reviewer','AL Portal - Adviser Remediation','AL Portal - T&C Supervisor') {
  $key = 'prod-e2e-' + ($role -replace '[^A-Za-z]', '').ToLower()
  [IO.File]::WriteAllText("$D\role.json", (@{ UserEmail = $me; AppRole = ''; RoleCode = $role; IdempotencyKey = $key } | ConvertTo-Json -Compress))
  & $t webapi $p POST al_AssignUserRole "@$D\role.json"
}
```

**4. Code App access.** In PROD, open the OTIS app as the tester once. It must already be
shared with them ("Still open: share the app" in the 2026-10-01 deployment note). Copy the
player URL, `https://apps.powerapps.com/play/e/<env>/app/<appId>`. That is
`OT_CODEAPP_URL`. Its app id is not recorded in this repository.

**5. The T&C mapping.** Map the tester's email to the tester as T&C Manager. Without it the
portal withholds the sign-off. Use the `contactid` from step 1:

```powershell
[IO.File]::WriteAllText("$D\map.json", "{""al_adviseremail"":""$me"",""al_TcManagerId@odata.bind"":""/contacts(<contactid>)""}")
& $t webapi $p POST al_advisermappings "@$D\map.json"
```

**6. The sending mailbox.** Optional, but without it no letter is delivered. Check that
`al_NotificationSenderAddress` names the shared mailbox and that its queue is approved and
enabled for server-side email in PROD. One mailbox syncs in one environment only.

**7. Capture the session, after the roles.** The portal reads roles at sign-in. Capture a
session from before the grants and preflight will report the roles as missing. From `app/`:

```powershell
$env:OT_PORTAL_URL='https://otis-ascotlloyd.powerappsportals.com'; npm run e2e:auth
```

Sign in as the tester in the window that opens. The session is saved to
`app/e2e/.auth/portal.json` and its Entra cookies also open the Code App. That file holds
**one** portal's session. A session from DEV or TEST will look signed out to PROD.

`otis-ascotlloyd.powerappsportals.com` is the host recorded on 2026-10-01. Use the one the
site actually serves.

## Running it

From `app/`, in one PowerShell window:

```powershell
$env:OT_PORTAL_URL='https://otis-ascotlloyd.powerappsportals.com'
$env:OT_E2E_TARGET='otis-ascotlloyd.powerappsportals.com'
$env:OT_CODEAPP_URL='https://apps.powerapps.com/play/e/<env>/app/<appId>'
$env:OT_E2E_TESTER_EMAIL='<tester email>'
$env:OT_E2E_WRITE='PROD-LIFECYCLE'
npx playwright test e2e/prod-lifecycle.e2e.ts
```

| Variable | Meaning |
|---|---|
| `OT_E2E_WRITE` | Must be exactly `PROD-LIFECYCLE`. Any other value, and every test skips. |
| `OT_E2E_TARGET` | The portal host you mean to write to. Must equal `OT_PORTAL_URL`'s host, or every test skips. It is a second, deliberate statement of where the writes go. |
| `OT_PORTAL_URL` | The PROD portal. |
| `OT_CODEAPP_URL` | The PROD Code App player URL (step 4). |
| `OT_E2E_TESTER_EMAIL` | The tester's mailbox. Preflight refuses to go on unless the portal session is this person. |
| `OT_E2E_PRODUCT` | Optional. The product name in the manager role, if it is ever not OTIS. "OTIS" and "Outcome Testing" are both accepted anyway. |

A full run takes about 15 to 45 minutes. Most of it is waiting for the portal. The portal
caches each page's data for up to 15 minutes. So when the run must re-read a page after a
change, it requests the page again with the case id in a different letter case. That is a new
query, so the cache is skipped. It retries from 5 seconds up to every 30 seconds, for up to
17 minutes per wait. Each step may run up to 25 minutes.

Leave the variables unset and `npm run e2e` skips all twelve tests and writes nothing.

## Reading a failure

The step that failed names the cause. The first sentence of the error is written for you.

| Failure says | Meaning | Do |
|---|---|---|
| "no saved session", "redirected off the portal", "the portal served its sign-in page", "sent the browser to Entra sign-in", "Enter password" | The session is missing, expired, or from another portal. | Repeat step 7, then re-run. |
| "OT_E2E_TESTER_EMAIL is … but the portal session is …" | You signed in as someone else. | Capture the session as the tester. |
| "the tester's portal session lacks: …" | A role from step 3 is missing, or was granted after the capture. | Grant it, then repeat step 7. |
| "could not read the signed-in email from the portal" | The Access Denied page no longer says who is signed in. | Nothing was written. The template changed; tell the agent. |
| "the Code App shows "No access" - … Permission: page.… " | An app permission is missing. | Check step 3: `page.admin.users` is the Portal Administrator role, the rest OTIS Manager. Then step 2. |
| "People lists … exactly once" | No active contact holds the email, or two do. | Fix the contact (step 1). |
| "a T&C mapping whose adviser is … and whose manager is …" | Step 5 is missing, or maps to someone else. | Add or correct the mapping. |
| "the upload failed: …" / "1 of 1 case imported" | The import refused the row. The message is the server's. | Read the reason. A "TaskID already exists" means the same minute's reference was used; wait a minute and re-run. |
| "is not offered as a … checker" | The tester is not an active user in the app's directory. | Check step 2 and the contact. |
| "the … check cannot be allocated from this account" | The modal said why. Usually the manager role is missing. | Step 3. |
| "the review opens read-only" | The review is not assigned to the session's contact, or the reviewer role is missing. | Steps 1 and 3. |
| "Q-…: the page said "…"" | The portal refused one answer as it saved. The quoted text is the page's own message. | Report it. The run log lists what was answered. |
| "none of … is offered (response type …)" | PROD's checklist has a question this run does not know how to answer cleanly. | Report it with the run log. |
| "the review was not submitted: "…"" | Submit was refused. The run already retried twice, answering the rows the page marked as unanswered. | The quoted message names what is missing. |
| "… still not shown after 17 minutes" | A portal page never showed the expected change. | Check the server side in the Code App (case status), then re-read the portal page with the id in upper case. If Dataverse has the change, it is the cache; if not, the server step failed. Check `plugintracelogs`. |
| "the sign-off is withheld: the tester is not the T&C Manager mapped …" | The portal could not see the mapping. | Check step 5, then wait out the cache and re-run. |

The test's trace (`app/test-results/…/trace.zip`) shows each page as it was. Open it with
`npx playwright show-trace <path>`.

## After the run

To read the result from Dataverse (read-only, with `$p` and `$me` as above and `<ref>` from the
run log):

```powershell
& $t webapi $p GET "al_outcomecases?`$select=al_outcomecaseid,al_casestatus,al_advisername,al_adviseremail&`$filter=al_casereference eq '<ref>'"
& $t webapi $p GET "al_notifications?`$select=al_subject,al_status,al_recipientemail,al_queuedon,al_failurereason&`$filter=al_recipientemail eq '$me'&`$orderby=al_queuedon desc&`$top=20"
& $t webapi $p GET "emails?`$select=subject,statuscode,createdon,torecipients&`$filter=contains(torecipients,'$me')&`$orderby=createdon desc&`$top=20"
```
