# The adviser email follows the adviser - DEV and TEST (1.0.16.0)

Date: 2026-09-30
Commit: `1110b03`

## What was reported

On TEST, case **256617620**: Adam Strumidlo is its adviser and its T&C Manager, but he could not
sign it off.

## Root cause

A case names its adviser twice. `al_advisername` routes remediation (Remediation.AdviserContact
resolves it to one contact by full name) and gives the adviser their own access.
`al_adviseremail` is the key `al_advisermapping` is looked up by, so it decides the T&C Manager:
who may sign off and regrade (SupervisorMapping), the supervisor who can read the case
(CaseAccessReconciler), the portal's sign-off and regrade panels (OT Remediation), and who is
told (NotificationRecipients, CompleteRemediationPlugin).

TEST's 29 Sep import carried adviser names and no addresses. **24 TEST cases** (23 Adam
Strumidlo, 1 Zoe Ramwell, 373925362) had an adviser who could complete their actions and a T&C
Manager nobody could resolve. Adam is correctly mapped to himself and holds the T&C Supervisor
role; the case simply had no email for the mapping to match.

The same gap had a second face: changing a case's adviser moved the open actions to the new
person but **kept the old adviser's email**, so sign-off would have gone to the previous
adviser's manager. The portal header's adviser edit moved neither.

## The fix - one rule, `CaseAdviser`

The case's adviser email is the stored email, or else the email of the one active contact the
adviser name resolves to - "email first, name second", as `NotificationOutbox.MatchPerson`
already does. Never a guess between two people of the same name.

| Where | Change |
|---|---|
| `TcManagerRouting.ForCase` (sign-off, regrade, T&C notices) | Uses the rule |
| `CaseAccessReconciler` (supervisor's access) | Uses the rule |
| `UpdateCaseDetailsPlugin.ApplyFields` (Code App and portal header) | A new adviser brings their own email; a name matching nobody clears it |
| `CaseHeaderRequestPlugin` (portal header) | An adviser change now moves the open actions, as the Code App's already did |
| `ImportCasesPlugin` | A row with a name and no email gets the matched contact's email |
| Registration tool | `backfilladviseremail <org> [--confirm]` for cases created before |

Tests: `CaseAdviserTests` (12, including the TEST case as it was), `CaseAdviserEditTests` (the
portal header end to end, and the shared applier), and one in `CaseAccessReconcilerTests`.
Plug-in suite 1744/1744.

## Audit of the other adviser checks

| Logic | Keyed on | State |
|---|---|---|
| Remediation assignment, the adviser's own access (`al_advisercontactid`, "Case - my clients") | Name, resolved to one contact | Unchanged; consistent with the rule |
| Adviser notifications, export adviser match | `MatchAdviser`: email, then name | Already the rule |
| T&C Manager: sign-off, regrade, completion notice, recipients | `TcManagerRouting.ForCase` | Fixed |
| Supervisor access (`al_tcsupervisorcontactid`, "Case - advisers I supervise") | Reconciler | Fixed |
| Portal sign-off and regrade panels (OT Remediation) | The **stored** `al_adviseremail` | Correct once the email is stored: the import and the edits now store it, and the backfill fills existing cases |
| Adviser Mapping page (Code App) | The mapping table itself | No change |

One notice was lost: when Adam completed the four actions at 10:44Z, the "awaiting sign-off"
notice to the T&C Manager could not be routed.

## Proved in DEV (plug-in assembly sha256 `26dc92c0...57415`)

Case **920930001** was imported for this test with adviser "Service Account" and a blank email:

| Step | `al_advisername` | `al_adviseremail` |
|---|---|---|
| Import | Service Account | `svc.automate.aq@...`, filled from the contact |
| `al_UpdateCaseDetails` adviser -> Simunye Radingwana | Simunye Radingwana | `Simunye.Radingwana@...` |
| -> a name matching nobody | Nobody Known Here | null |
| -> back | Service Account | `svc.automate.aq@...` |

DEV had no other case missing an email (`backfilladviseremail` dry run: 0).

## TEST - 1.0.16.0

Exported managed (`artifacts/2026-09-30-test-promotion-c/`), imported by the agent through
`ImportSolutionAsync` (pac's token is still revoked); job `be93a20f-c3bc-f111-aaae-002248c654cd`
succeeded. Read back: **1.0.16.0 managed, 33 steps all enabled, assembly sha256 `26dc92c0...`**.

**Still to run on TEST (owner):** the backfill. The dry run lists exactly the 24 cases, all
resolving cleanly. The agent's own write to those cases was refused ("Modify Shared Resources").

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe backfilladviseremail https://org37995f36.crm11.dynamics.com --confirm
```

Each write fires CaseAccessPlugin, which re-resolves the supervisor on that case. After it runs,
Adam's sign-off form appears on 256617620.

**Run by the owner, 2026-09-30.** Read back from TEST:

| Cases | State |
|---|---|
| All 24 | adviser email filled; **0** TEST cases now name an adviser with no email |
| 256617620, 256497637 (Awaiting Sign-off, adviser Adam) | adviser contact **Adam**, supervisor **Adam**: the portal's sign-off gate (T&C Supervisor role, mapping, stored email, actions awaiting sign-off) is now met for him |
| 373925362 (Awaiting Sign-off, adviser Zoe Ramwell) | supervisor **Adam** (Zoe's mapped T&C Manager) |
| The other 21 (not yet in remediation) | email filled; the adviser and supervisor access columns are set only once a case is released to remediation (`CaseAccess.IsReleased`), and will resolve then |

Not driven in a browser as Adam: the agent has no session for his account.
