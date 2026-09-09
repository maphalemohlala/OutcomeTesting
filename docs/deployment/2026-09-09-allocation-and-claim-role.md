# "Assigned but not on My Work", and the Service Account's 403 on Run checks — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Read and pushed through
`plugins/OutcomeTesting.Registration` as `svc.automate.aq`.

## The reports

1. "After assigning a case to someone and refreshing the portal the case still doesn't show
   on their My Work list."
2. "We could not pick that case up. You may not have permission to run checks." for the
   service account, on IO-000124.

## What the environment said

Queried before changing anything.

| Question | Answer |
|---|---|
| IO-000124 | Queued, route AQS only, owner svc automate aq. **No review instance, no assignment row.** Nothing has ever allocated it. |
| Any `AssignCase` today? | **None** in `al_auditevent`. The last allocation was Sims Rad's own portal claim of IO-000125 at 14:35, submitted at 14:38 (so past My Work's open list by design). |
| What ran at 16:28? | `UpdateCaseDetails` on **IO-100003** (already Assigned): "Checker 'Dev Account' -> 'Service Account'". Same shape at 14:33 on another case (302315ae-…): "Checker 'Aisha Khan' -> 'Service Account'". |
| Service Account contact | Exists, signs in (`adx_identity_username` set). Web roles: **Administrators, AL Portal - T&C Supervisor**. No Tax Reviewer, no AQS Reviewer. |
| The claim's permission | `Contact - claim request (self)`: write, Self scope, bound to web roles 90 (Tax Reviewer) and 91 (AQS Reviewer). |

## Root causes

**1. The case was not allocated; its Checker name was edited.** The case edit panel's
"Checker" field writes `al_outcomecase.al_checkername`, the name printed on the checklist
header. It does not run `al_AssignCase`, so no review instance is created, no
`al_assignedcontactid` is stamped, and My Work — which lists review instances assigned to the
signed-in contact — has nothing to show. IO-100003 is still allocated to Dev Account's review;
its header now says "Service Account".

**2. The Service Account holds no reviewer web role.** The Run checks button PATCHes the
signed-in contact's `al_claimrequest`; that write is granted only to the two reviewer roles,
so Power Pages answers 403 before `ClaimRequestPlugin` runs. The page's message was accurate.
The fault is that the page offered the button at all: it was gated on the site setting alone,
and today's page-permission grants (page.reviews to Adviser Remediation, page.remediation to
T&C Supervisor) put non-reviewers on the queue page for the first time.

## What changed

| Where | Change |
|---|---|
| `OT Review List` template | "Run checks" (button, action column, dialog, script) renders only when the signed-in user holds the queue's reviewer role — Tax Reviewer on the Tax queue, AQS Reviewer on the AQS queue. Otherwise the caption says which role is needed. The server remains the boundary. |
| `CaseEditPanel.tsx` / `.css` | The Checker field carries a help line: "The checker's name as printed on the checklist. Changing it does not allocate the check: use Allocate for that." |

No data was changed. The two things the report actually needs are the owner's:

- **To allocate a case to someone**: Allocate on the case (the `al_AssignCase` command). It
  needs a Dataverse user and a portal contact sharing the work email, creates the review
  instance, stamps the contact, and the case appears on their My Work.
- **For the service account to run checks**: give its contact the AL Portal - AQS Reviewer
  web role (and Tax Reviewer if it should run Tax checks) through the app's roles page. Until
  then the queue page tells it so instead of failing.

## Verification

| Check | Result |
|---|---|
| App `tsc -b` / `eslint` / `vitest` | clean; clean; 257 passed |
| Liquid tag balance (review list) | if 31/31, for 2/2, comment 7/7, capture 1/1, fetchxml 2/2 |

## What ran

| Step | Command | Result |
|---|---|---|
| 1 | `npm run build` then `npx pa app push` | built clean, pushed successfully |
| 2 | `dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000016 ".../ot-review-list/OT-Review-List.webtemplate.source.html"` | 24240 -> 25224 chars, modified 2026-09-09 16:39:16Z; site cache clear is the owner's step |

## Retest

1. Signed in as the service account, open AQS reviews: no Run checks button; the caption
   reads "Running checks needs the AQS Reviewer role, which your account does not hold."
2. Grant the AQS Reviewer web role to the Service Account contact; reload: the button is
   back, and Run checks on IO-000124 assigns it and opens the checklist.
3. In the app, Allocate IO-000124 to a named checker: their My Work shows it.
4. In the app, edit a case: the Checker field shows the help line.
