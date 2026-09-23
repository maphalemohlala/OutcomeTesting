# Role-scoped access: each person sees their own work

Date: 2026-09-23
Status: Design approved section by section; written spec awaiting review
Source: `docs/reference/2026-09-23-access-requirements.md` (AR-01 to AR-04)
Supersedes: OD-022 (every authenticated portal user reads every case), AD-056 (reviewers read
everything, write their own), and AD-083's organisation-wide Home counts
Closes: OD-029(c) (nothing stops a Tax lead allocating an AQS check)

## Problem

Since OD-022 (2026-08-31) the portal is **read-everything, write-own**. `Outcome Case - read
all`, `Review Instance - read all`, `Response - read all` and `Remediation Action - read all`
are Global read bound to Tax Reviewer, AQS Reviewer, Adviser, T&C Supervisor and Planner, and
`Outcome - read all` is Global read bound to Authenticated Users. Only writes are scoped.

So today a Tax specialist can open unassigned cases and colleagues' cases, and the Tax page
lists every Tax review by default. An adviser can open every case in the system, including
cases still in Tax or AQS review, draft answers, other advisers' clients and their remedial
actions. Every signed-in user can read every grade.

The Code App has the same shape one layer down. `Outcome Testing App User` grants Global-depth
read on the business tables (AD-135, "Depth is Global throughout, per OD-022"), and allocation
is held by T&C Supervisor and Outcome Testing Manager, both across both disciplines. Nothing
server-side stops a Tax lead allocating an AQS check (OD-029(c)).

The 2026-09-23 requirements reverse all of this: each role sees only its own work, and the
two review teams are managed separately.

## Decisions taken with the project owner, 2026-09-23

| # | Question | Answer |
|---|---|---|
| 1 | Do these requirements override OD-022 and AD-056? | Yes. |
| 2 | Which statuses release a case to the adviser? | Remedial action only. Pass cases are never released. |
| 2a | Does the adviser keep the case after sign-off? | Yes, they keep seeing cases assigned to them. |
| 3 | Planners? | They still get emails and have **no access to the system**. |
| 4 | Where does the Tax Team Manager work? | A new role in the Code App. |
| 4a | Who manages AQS? | A second new role, AQS Team Manager. Outcome Testing Manager and Administrators keep the organisation-wide view. T&C Supervisor loses allocation. |
| 4b | What does each manager see? | Their own team's cases only. |
| 4c | Tax-then-AQS cases? | Both managers see them for the whole life of the case. |
| 5 | Route completed Tax reviews to AQS? | The existing automatic routing on submit is enough. |
| 6 | What do AQS reviewers see? | All unassigned cases waiting for AQS (AQS-only, and Tax-then-AQS once Tax is submitted), and their own. Never another checker's. |
| 6a | AQS queue order? | Two groups, Tax-reviewed first, each oldest first. Age in working days (the PP-13 business calendar), chosen by default and open to change. |
| 7 | T&C Supervisor in the portal? | Only remediation cases for the advisers they supervise, on the same release rule as advisers. |
| 8 | Enforcement approach? | Stored access lookups kept correct by one reconciler (below), not Power Pages Custom access and not read-through Custom APIs. |

Approaches rejected, and why:

- **Power Pages Custom (FetchXML) table permissions.** They would need fewer columns, but the
  access type is still marked Preview on Microsoft Learn (page updated 2026-07-07) and requires
  the site to be switched to enhanced authorization. That would put a production security
  boundary on preview behaviour.
- **Server-side read APIs for every list and detail.** This is the tightest option, but it
  rewrites every read on both front ends for the same result.

## Scope

In scope: portal table permissions and the Tax, AQS, My Work, Remediation and Home pages; five
access columns on `al_outcomecase`; the `CaseAccess` rule and its reconciler; two web roles,
two Dataverse owner teams, one Dataverse security role; the allocation guard; a Team workload
page; the backfill verb; decision-log and requirements-index records.

Out of scope: changing the case lifecycle or routing rules (AR-01/AR-02's "route to AQS" is
already automatic, answer 5); Code App access for checkers or advisers (they are portal
users, AD-044, and get no Code App data role); deleting the Planner web role; TEST or PROD
deployment, which is the project owner's call each time.

## 1. Access columns and the reconciler

### Columns on `al_outcomecase`

Lookups used for access only. No page displays them.

| Column | Target | Set when | Cleared when |
|---|---|---|---|
| `al_taxcheckercontactid` | contact | The Tax review is allocated | Replaced on reallocation |
| `al_aqscheckercontactid` | contact | The AQS review is allocated or claimed | Replaced on reallocation |
| `al_aqsqueueaccountid` | account (AQS Team) | Case is `Queued`, route requires AQS, and any Tax review is submitted | The AQS review is allocated or claimed, or the case leaves `Queued` |
| `al_advisercontactid` | contact | Release (below) | Never |
| `al_tcsupervisorcontactid` | contact | Release, from `al_advisermapping.al_tcmanagerid` matched on `al_adviseremail` | Never; re-resolved if the mapping changes before closure |

Each is a new N:1 with `RemoveLink` on delete, so deleting a leaver's contact never deletes a
case (the AD-072 rule).

**Release** is: case status is `Awaiting Remediation` or any later status, **and** no review
instance on the case is unsubmitted. The second condition is what keeps a Tax fail on a case
that still owes an AQS check away from the adviser (AR-04: "Cases where a Tax or AQS review is
still in progress"). Once set, the adviser and supervisor columns stay set through `Closed`
(answer 2a). A Pass case never reaches `Awaiting Remediation`, so it is never released
(answer 2).

The adviser is resolved with the resolver that already sets `al_assignedcontactid` on remedial
actions (`Remediation.cs`), so the case and its actions always name the same person.

### `CaseAccess`: the rule

A pure function in the plug-in assembly. Input: case status, the route's
`al_requirestaxreview` and `al_requiresaqsreview`, each review instance's type, assigned contact
and submitted state, the resolved adviser and supervisor, and the AQS Team account reference.
Output: the five lookup values and the set of teams the case is shared with (the Tax team if
the route requires Tax or a Tax review exists; the AQS team if the route requires AQS). No I/O.

### `CaseAccessReconciler`: applying it

It reads the case and its reviews, calls `CaseAccess`, writes only the columns whose value
differs, and grants or revokes Read on the case for the two owner teams to match. It is called
as the last step of the commands that already change the inputs:

- `AssignCasePlugin`: allocation and reallocation
- `ClaimCasePlugin`: AQS self-claim
- `SubmitReviewPlugin`: Tax submitted, entry to the AQS queue, entry to remediation
- `UpdateCaseDetailsPlugin`: route and status changes
- `SignoffProgressPlugin`: closure

It runs in the calling command's transaction, so a failure rolls the command back rather than
leaving access half-applied.

### Failure modes

- **Adviser cannot be matched** (unknown or ambiguous). The case still moves to remediation,
  but the adviser column stays empty and nobody gains adviser access. The Code App case detail
  shows "Adviser not matched". This is the AD-082 stance: never guess a person.
- **No adviser mapping.** The supervisor column stays empty; the case is flagged the same way.
- **The AQS Team account or an owner team is missing.** The reconciler throws a `PRECONDITION:`
  naming it and the command rolls back. Silently skipping it would leave a queued case that no
  AQS reviewer can see.

### Backfill

The registration tool gets a new verb, `reconcileaccess`, which runs the reconciler over every
case and prints what changed, plus every case whose adviser or supervisor could not be matched.
It is idempotent and safe to re-run.

## 2. Portal permissions and pages

### Removed

The Global-read permissions on case, review instance, response and remediation action lose
Tax Reviewer, AQS Reviewer, Adviser, T&C Supervisor and Planner. `Outcome - read all` loses
Authenticated Users. Global read remains only for **Administrators** and **Outcome Testing
Manager**.

### Added

New read-only parent permissions on `al_outcomecase`:

| Permission | Scope | Relationship | Role |
|---|---|---|---|
| Case - my Tax checks | Contact | `al_taxcheckercontactid` | Tax Reviewer |
| Case - my AQS checks | Contact | `al_aqscheckercontactid` | AQS Reviewer |
| Case - AQS queue | Account | `al_aqsqueueaccountid` | AQS Reviewer |
| Case - my clients | Contact | `al_advisercontactid` | Adviser |
| Case - advisers I supervise | Contact | `al_tcsupervisorcontactid` | T&C Supervisor |

Each Contact permission carries read-only children: review instance, response (under review
instance), outcome, remediation action and sign-off. A person therefore reads case detail only
on a case they can already read. **Case - AQS queue has no children:** a reviewer sees a
waiting case's summary row and cannot read its answers until they take it.

Unchanged: `Review Instance - assigned to me` and `Response - on a review assigned to me`
(answering), `Remediation Action - assigned to me` (completion, minus Planner),
`Signoff - T and C attestation`, `Case Assignment - claim`, `Contact - directory read`, and the
Global reads on configuration tables.

AQS reviewers are given the AQS Team account as their contact's parent account, which is what
Account scope resolves through.

### Planner

Removed from every table permission and page rule, and from the Code App's permission rules.
The web role row stays so existing assignments do not break, and it grants nothing. Paraplanner
email is unaffected: it resolves the paraplanner's contact and never depended on the role
(BR-009, AD-082).

### Pages

- **Tax reviews.** Only reviews allocated to me, grouped as *Allocated*, *In progress* and
  *Completed* by review status. The "all reviews" option is removed.
- **AQS reviews.** Five labelled groups:
  1. *Tax review completed – awaiting allocation*, with a visible "Tax reviewed" flag
  2. *New – awaiting allocation*
  3. *Allocated to me*
  4. *In progress*
  5. *Completed*

  Groups 1 and 2 are the claimable queue, each ordered oldest first, and show **Days in OTIS**
  (working days since import) and **Waiting for AQS** (working days since the Tax review was
  submitted, or since import for AQS-only cases), plus the due date. The working-day
  calculation reuses the portal's existing remediation ageing.
- **Adviser: My Work and Remediation.** *Remedial action required*, *Awaiting T&C sign-off*,
  *Closed*.
- **Case list, case detail, Home outcome cards.** No query change; permissions narrow them.
  The Home caption changes from organisation-wide to "your cases". A case the viewer cannot
  read renders the existing non-disclosing not-found page.

**Verify first:** that Liquid `fetchxml` aggregates (the Home counts) are filtered by table
permissions like row queries. If they are not, the counts become row queries.

## 3. Code App roles, teams and allocation

### Roles

Two new web roles (AD-087 makes web roles the application's role vocabulary):
**AL Portal - Tax Team Manager** and **AL Portal - AQS Team Manager**. They carry no portal
permissions. Their seeded application rules: `page.dashboard`, `page.cases`, the allocation
screen, `command.assign`, and the new `page.workload`. T&C Supervisor loses `command.assign`
(answer 4a).

### Data scoping in Dataverse

- **Owner teams:** "Outcome Testing - Tax Team" and "Outcome Testing - AQS Team". The
  reconciler shares each case with the team or teams its route has needed, so both see a
  Tax-then-AQS case for its whole life (answer 4c).
- **Cascade.** The 1:N relationships from `al_outcomecase` to review instance, remediation
  action, outcome, sign-off, case assignment and audit event, and from review instance to
  response, change `CascadeShare` and `CascadeReparent` from `NoCascade` to `Cascade`. All of
  these tables are user-owned, so share and reparent cascade apply, and children created later
  inherit the share.
- **Security role "Outcome Testing Team Manager".** User-depth read on those business tables,
  which covers what the user owns and what is shared with their teams. Organisation-depth read
  on the checklist and configuration tables. Create and write only where the allocation
  command needs it. Always paired with Basic User.
- **Membership follows the grant.** When an administrator grants or withdraws a manager role
  on the People page, `AssignUserRolePlugin` adds or removes the person's systemuser, matched
  on work email (AD-010), in the matching team, and assigns or removes the security role. Teams
  are resolved by name, never by a hard-coded id. No matching systemuser means the grant is
  refused with a `PRECONDITION:` reason, as `al_AssignCase` does.

The two managers must **not** hold `Outcome Testing App User` or System Administrator; either
restores organisation-wide read.

### Allocation guard

`AssignCasePlugin` refuses a Tax Team Manager allocating anything but a Tax review, and an AQS
Team Manager anything but an AQS review. Outcome Testing Manager and Administrators may
allocate either. The assignee must hold the discipline's reviewer web role, mirroring
`ClaimCasePlugin.EnsureDisciplineRole`. The allocation screen offers only the caller's
discipline and the checkers holding it, but the plug-in is the enforcement.

### Team workload page, `/workload`

- **Team totals:** awaiting allocation, allocated, in progress, completed this month.
- **Per checker:** allocated not started, in progress, completed in the last 30 days, and the
  oldest open item in working days.
- **Drill-down:** every figure opens the matching case list.

It reads only what the team share returns, so it needs no filter of its own.

## Rollout (DEV only)

Every step before 5 adds access, so nobody is locked out mid-way.

1. **Schema.** Columns, relationships, cascade changes, AQS Team account, owner teams, security
   role. No behaviour change.
2. **Reconciler.** Wired into the five commands and registered (`build -c Release` before
   `pushassembly`), then `reconcileaccess` in DEV, with the unmatched list reported.
3. **AQS reviewers' parent account** set, after checking nothing in DEV already uses it on
   those contacts.
4. **New portal permissions added** and each role verified. Everyone still has Global read.
   The Liquid aggregate check runs here.
5. **Global read removed**, then the page changes. This step is reversible by rebinding.
   Upload with `--modelVersion Enhanced`, after diffing against DEV.
6. **Code App.** Roles, rules, allocation guard, workload page; `npm run build`, then push.
7. **The project owner** grants the two named managers their role on the People page, and
   decides on TEST promotion.

## Testing

- **Plug-ins, test first:** the `CaseAccess` decision table covers every status × route ×
  allocation × adviser-match combination, including "Tax fail with AQS still owed stays
  hidden" and "adviser keeps the case after Closed". The reconciler writes only diffs. The
  allocation guard refuses across disciplines and to non-holders. Team membership follows
  grant and withdraw.
- **App, vitest:** allocation choices per role; workload totals and drill-down filters.
- **Portal e2e, positive and negative per role** (the Phase 2 gate):
  - a Tax specialist opening a colleague's case by URL, or through `/_api/`, is refused
  - an AQS reviewer sees the queue and their own cases, not another checker's
  - an adviser sees nothing before release and nothing of another adviser's
  - a T&C Supervisor sees only the advisers they supervise
  - a Planner sees nothing
  - outcomes are unreadable to an ordinary signed-in user
- **Code App:** a Tax Team Manager cannot see an AQS-only case or allocate an AQS review.

**Prerequisite:** one non-admin test identity for each of Tax specialist, AQS reviewer,
adviser, T&C Supervisor, Tax Team Manager and AQS Team Manager. Today one contact holds several
roles (MR-06), every DEV user is System Administrator (AD-135), and impersonation is ignored in
DEV, so negative tests cannot be run without them.

## Risks

- Liquid aggregates may not honour table permissions (checked at rollout step 4).
- A contact may already use its parent account for something else (checked at step 3).
- Adviser names that match nobody leave cases unreleased (the backfill lists them, and the
  Code App flags them).
- A person holding both reviewer roles belongs to the AQS Team account while also checking
  Tax; that is correct, because Account scope grants only the queue.

## Records

- `knowledge/requirements-index.md`: a new AR band, AR-01 to AR-04, one per source section.
- `knowledge/decision-log.md`: one new AD recording the eight answers above, superseding
  OD-022, AD-056 and AD-083's organisation-wide counts, closing OD-029(c), and amending
  AD-135's "Global throughout" for the two manager roles.
