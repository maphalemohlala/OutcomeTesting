# People are identified by email, never by name - design

Date: 2026-10-02. Owner direction, the same day:

- "This should use email instead of name for matching. The whole system needs to do that, as
  two people can have the same name."
- An import row with an adviser or paraplanner name but no email is **rejected**.
- Existing cases get a **one-off backfill with a report**.
- Approach A: **email is the only key**. Names are labels.
- People outside the contacts list can still be entered by hand, as a name **and** an email.

## The problem

PROD case 256497798 names its adviser "Adam Strumidlo". His contact was onboarded as "Adam
Strumdlio", from a misspelling in the source workbook. Remediation is assigned by matching
`al_advisername` to `contact.fullname`. No contact matched, so the action was raised
unassigned, and nobody could complete it. His email, `adam.strumidlo@`, was right everywhere.

The same matching would also give two advisers with the same name nobody: `TopCount 2`
refuses to guess. A rename to a name that matches nobody clears the email and blocks sign-off.

The system is inconsistent about who a person is:

| Resolves a person by | Where |
|---|---|
| **Name only** | Remediation assignment (`Remediation.AdviserContact`, used at raise and by `AssignOpenActions`). Adviser portal access (`CaseAccess` → `al_advisercontactid`). The "Case passed" letter (`NotificationEmitterPlugin.QueueCasePassed`). Writing the adviser email on a rename (`CaseAdviser.FollowName`) and on import (`CaseAdviser.FillMissingEmail`). Registration verbs `SetCasePeople`, `RepointRemediation` and `BackfillAdviserEmail`. Code App picker for adviser and paraplanner (stores the name only). Code App workloads and filters (group by lower-cased name). |
| **Email, else name** | T&C routing (`CaseAdviser.EmailFor` → `TcManagerRouting.ForCase`, so sign-off, regrade and T&C notices). Adviser template letters (`NotificationRecipients`). Paraplanner letters (`NotificationOutbox.ParaplannerEmail`). Export staff codes and paraplanner address (`GenerateExportPlugin`). The `NotificationOutbox.MatchPerson` resolver behind most of these. |
| **Email or a lookup** | Checker allocation (`AssignCasePlugin.ResolveAssignee`). Role holders, web-role registry, permission checks. The adviser mapping (`al_advisermapping.al_adviseremail` → `al_tcmanagerid`). Completing an action (`al_assignedcontactid`). Every portal "mine" check (`user.id`). |

As a result, remediation can go to one person while sign-off routes by another. A paraplanner
change also never updates `al_paraplanneremail`, so the previous paraplanner keeps getting
the letters and stays on the export.

The data supports email already: `al_outcomecase` has `al_adviseremail` and
`al_paraplanneremail`, and the IO task extract carries `AdviserEmail` and `ParaplannerEmail`.

## The design

### 1. The rule

- **One resolver**, `PersonByEmail(service, email)`. It trims the email, and Dataverse
  equality ignores case. It considers active contacts only and reads `TopCount 2`. The
  outcome is one of: `NoEmail`, `NoContact`, `Ambiguous` (two or more contacts with that
  email) or `Matched` (contact ref, email, staff code). It replaces the name branch of
  `NotificationOutbox.MatchPerson`, all of `Remediation.AdviserContact`, and the name
  fallbacks in `CaseAdviser`.
- **On a case, the email is the identity.** `al_adviseremail` and `al_paraplanneremail`
  decide who someone is. `al_advisername` and `al_paraplanner` are labels: always written
  together with the email, and never used to find anyone.
- **Paraplanners** may not be contacts (BR-009). Their email is used directly as the
  letter address. The resolver is used only for the export's staff code.
- **No guessing.** If there is no email, no contact or two contacts:
  - the remediation action is raised unassigned;
  - the case's existing "adviser unmatched" notice (`AdviserUnmatched`, shown on the Code App
    case page) and the portal remediation hint say "no portal contact for <email>" or "two
    contacts share <email>";
  - letters land at Failed with the reason, as they do today.
  - "Two contacts share an email" means a duplicate onboarding to clean up.

### 2. Writes

- **Import** (`ImportRules`, mirrored in `app/src/features/imports/caseUpload.ts`).
  `AdviserEmail` and `ParaplannerEmail` become required, and each must look like an email
  address. A failing row is rejected, with its reason in the import report, and no case is
  created. The name columns stay required, as labels. The "adviser unmatched" and
  "para-planner unmatched" report rows now describe the email, not the name.
- **Code App case edit** (`CaseEditPanel`, `UserPicker`). The adviser and paraplanner
  fields each hold a name and an email:
  - picking a contact from the list fills both;
  - someone not in the list is entered as a name plus an email;
  - a name without an email is refused.
- **Portal case header** (`OT Case Detail` and `CaseHeaderRequestPlugin`). Each person gets
  an email field next to their name, with the same rule.
- **Server rule, in both edit paths** (`UpdateCaseDetailsPlugin` and
  `CaseHeaderRequestPlugin`):
  - Changing the adviser email moves the open remediation actions, re-runs case access and
    re-routes T&C sign-off.
  - Changing the paraplanner email re-routes their letters.
  - Changing a name alone is a label change: nothing moves.
  - A name sent without its email is refused.
  - `CaseAdviser.FollowName` and `FillMissingEmail` are removed.
- **Registration tool:**
  - `SetCasePeople` and `RepointRemediation` take emails.
  - `BackfillAdviserEmail` is replaced by the backfill command in §4.
  - `SetCaseAdviser` writes a name and an email.

### 3. Reads

| Area | After |
|---|---|
| Remediation raised (`SubmitReviewPlugin` → `Remediation.Raise`) | adviser email → `PersonByEmail` → `al_assignedcontactid` |
| Open actions follow the adviser (`Remediation.AssignOpenActions`) | Runs when the case's adviser email changes, **and** from a new synchronous post-operation step on `contact` Create, and Update of `emailaddress1` / `statecode`. That step finds open actions on cases whose `al_adviseremail` equals the contact's email and assigns them, if the email now matches exactly one active contact. Completed actions never move (BR-007). An action already held by that contact is not rewritten. |
| Adviser portal access (`CaseAccess`, `CaseAccessReconciler`) | `al_advisercontactid` from the adviser email |
| "Case passed" letter | `al_adviseremail` directly |
| Adviser template letters, T&C routing (`CaseAdviser.EmailFor`) | stored `al_adviseremail` only |
| Paraplanner letters, export address | stored `al_paraplanneremail` only |
| Export staff codes (`GenerateExportPlugin.AdviserCode`, paraplanner code) | `PersonByEmail` |
| Code App People page, person workloads, worklist and report filters (`peopleDirectory.ts`, `PeoplePage`, `PersonCasesPage`, `worklistFilters.ts`, `reportFilters.ts`) | grouped and filtered by email, with the name shown |
| T&C mapping reads (`TcManagerRouting.ForAdviserEmail`, `CaseAccessReconciler.SupervisorFor`) | both ignore inactive mappings and both detect two active rows |

**Unchanged:**
- Free-text searches a person types and reads the results of (for example the portal case
  list's adviser search) keep matching names.
- Letter greetings, PDFs and export name columns keep printing names.
- Checker allocation and the accountability picker already use email or a contact id.

Stale comments saying the paraplanner has no email column are corrected where they are
touched: `GenerateExportPlugin`, `NotificationOutbox`, `SubmitReviewPlugin`,
`TcManagerRouting`, `Program.cs` and `peopleDirectory.ts`.

### 4. Backfill and rollout

- **Backfill**, a new registration verb `backfillpeopleemail <org>`. By default it only
  reports. The report lists:
  - every case with a blank adviser or paraplanner email, and what its name would fill it
    with;
  - every case whose stored email and name point at **different** contacts (Adam-style
    typos);
  - every name that matches no contact, or two.

  `--apply --confirm <org>` then:
  - fills only the unambiguous blanks;
  - re-points every open remediation action to `PersonByEmail(al_adviseremail)`;
  - runs `al_ReconcileCaseAccess`.

  It never touches a completed action.
- **Rollout:**
  - DEV: the agent deploys, runs the backfill and runs the proof below.
  - TEST and PROD: a managed import (PROD through `brandpackage ... OTIS`). The owner imports
    and runs the backfill with `--confirm`, because writes there are the owner's.
- **PROD case 256497798:**
  - Once released, the case is fixed by its own data, if it holds `adam.strumidlo@`: the
    contact step or the backfill assigns the action.
  - Adam still needs the **AL Portal - Adviser Remediation** web role.
  - He is blocked today, so the manual fix (correct the contact's surname, add the role,
    re-save the adviser) is done now rather than waiting for this release.

## Testing

- **Plug-in unit tests:**
  - `PersonByEmail`: all four outcomes, case and spacing.
  - Each place in §3, including two contacts with the same name and different emails
    routing correctly, and a misspelled contact name being irrelevant.
  - Import rejection for a missing or malformed email.
  - Both edit paths: email change moves actions, name-only change moves nothing, a name
    without an email is refused.
  - The contact step: create, email correction, reactivation, ambiguous, completed actions
    left alone.
- **App tests:**
  - The picker writes a name and an email.
  - A name without an email is refused.
  - Workloads and filters key on email: two people with the same name stay apart.
- **Template tests:** the portal header's email fields and their payload.
- **DEV proof:**
  - Import a file with and without emails.
  - Raise remediation for an adviser whose contact name is misspelled.
  - Onboard a contact after the action is raised and see it assigned.
  - Two contacts with the same name each receive their own case's work.

## Not changing

- `al_advisermapping` is already keyed by email.
- Checker allocation already resolves by email.
- The portal's "mine" checks already use the contact id.
- No new columns on `al_outcomecase`. The email columns exist. `al_advisercontactid` stays,
  as the cached result of the email lookup.
- No PROD writes by the agent. TEST and PROD steps go to the owner as commands.
