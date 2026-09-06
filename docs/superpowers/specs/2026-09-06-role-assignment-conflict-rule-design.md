# The conflict rule for roles managed in two places

Date: 2026-09-06
Status: Approved for implementation
Environment probed: `Env_AQ_Dev` (`org0b075da8`)
Follows: `2026-09-06-web-roles-as-the-role-model-design.md`
Closes: the two items left owed by AD-087 (see `docs/2026-09-04-outstanding-work.md` §5)

## Problem

A role can be granted in two places. The app grants it through an audited Custom API
(AD-041, BR-012), which writes an `al_userrolemapping` row **and** associates the contact
with the web role. Power Pages management grants it by writing the association alone.

AD-087 settled that role resolution is the **union** of both sources. What it did not
settle is what a portal-side assignment *means*: whether it is legitimate, reconciled on a
schedule, or refused. Until that is settled, an assignment made outside the app carries no
audit event, no mirror row, and no visibility.

`Authenticated Users` is the adjacent question: auto-granted with no per-person membership,
excluded from the app's vocabulary by AD-087, and so far a decision to confirm rather than
a rule.

## Evidence

### The server unions; the client does not

`PermissionHelpers.GetActiveRoles` resolves a caller as active mappings **∪** web role
associations. `PermissionProvider.loadPermissions` reads mappings only. The two disagree,
and the disagreement has consequences worse than the missing audit event:

1. **A portal-only assignment locks the person out of the UI it authorises them for.**
   Their mapping list is empty, so the client resolves no roles and `RequirePermission`
   renders "No access" on every screen, while every server command would accept their
   writes. They hold access they cannot reach.

2. **The app's Withdraw can be silently undone.** Withdraw does disassociate the web role
   (`SetRoleAssignmentActivePlugin`), which is correct. But a re-association made in Power
   Pages afterwards restores server-side access while the mapping stays `Withdrawn`. The
   security screen would read "Withdrawn" over live access. For an FCA-facing trail that is
   the serious one.

3. **A portal-side removal is equally invisible** in the other direction: an active mapping
   whose association has been deleted still displays as a live assignment.

### The app cannot see associations without a plug-in

The generated client exposes no `$expand`, `IGetAllOptions` has no expand option, and
`ContactsModel` carries no web-role navigation property. `mspp_webrole` has no
many-to-many of its own — the intersect `powerpagecomponent_mspp_webrole_contact` is
reachable only from the contact side, and only by FetchXML
(`WebRoleRegistry.RolesForContact`).

So **every** option that makes the app tell the truth needs a server-side read. This is the
constraint that decides the shape of the solution, not a preference.

### A structured read already has a precedent

`al_ImportCases` returns a JSON array as a String response property (type 10). Custom API
response properties here are otherwise only String, Boolean and Integer. A holders list
therefore travels as JSON in a String, following that precedent rather than inventing an
EntityCollection contract.

### `IsSystemRole` is dead code, and OD-033 shows what that costs

`WebRoleRegistry.IsSystemRole` is defined and called from nowhere. `GetActiveRoles` unions
in whatever web roles a contact holds, unfiltered.

OD-033 records that `Administrators` carried `mspp_authenticatedusersrole = Yes` in DEV on
2026-09-04. In Power Pages that flag auto-grants the role to every authenticated contact.
Unfiltered union means every contact then resolved as `Administrators` server-side. This is
not a hypothetical: it happened, and nothing in the resolver contained it.

That evidence moves the guard past a name check. The rule that matters is that **a role
auto-granted to everyone can never confer application permissions**, whatever it is called.

## Decisions

**AD-089 — A portal-side assignment grants access, is visible immediately, and is not
authoritative until a person adopts it.**

The union stands (AD-087): a portal-side assignment keeps working, so nothing is silently
revoked. But it is surfaced as an unreconciled state that an administrator resolves by an
explicit decision, and that decision — not a schedule — is what writes the audit event.

Rejected: **auto-reconcile on a schedule.** It writes audit events recording decisions
nobody made, attributed to a job, and leaves a latency window in which the app still lies.
In a trail that exists to show who decided what, an automated adoption is worse than a
visible gap.

Rejected as a *substitute*: **refusing portal-side assignment** by stripping web-role
membership rights. It is an organisational control that system administrators route
around, it heals nothing that already exists, and it cannot fix the client/server split. It
remains worth doing as a later complement, and is recorded as a non-goal here rather than
as a rejection on the merits.

**AD-090 — Role resolution ignores any web role that is auto-granted to everyone.**

`GetActiveRoles` excludes a web role carrying `mspp_authenticatedusersrole = true`, and
excludes `Anonymous Users` by name. This confirms AD-087's exclusion of `Authenticated
Users` and generalises it: the exclusion follows the flag, so drift of the OD-033 kind
cannot grant application permissions to every signed-in contact. The app's role vocabulary
keeps excluding both system roles by name, unchanged.

## Design

### 1. `al_GetMyRoles` — the caller's effective roles

No request parameters; the caller is the subject. Returns `RoleCodes`, a JSON array of the
role codes `GetActiveRoles` will enforce, after the AD-090 exclusion.

`PermissionProvider` calls this instead of reading `al_userrolemapping` and re-deriving the
union. It keeps reading `al_pagepermission` for the rules, and keeps the existing bootstrap
and fail-open behaviour: a failed call still stands the permissive-for-view set in, because
every write is gated server-side.

The client stops owning a rule the server also owns. That is the point of the API.

No permission is enforced: the caller is asking what they themselves hold, and refusing
that would make the sign-in path depend on a permission the caller cannot yet resolve.

### 2. `al_GetRoleHolders` — who holds one role, and from where

Request `RoleCode` (required). Returns `Holders`, a JSON array of
`{ email, name, mappingId, mappingActive, associated }`.

Scoped to one role deliberately: resolving your own roles must never pull the whole
organisation's assignments into a browser.

Built from two reads joined on work email — the `al_userrolemapping` rows for the role, and
one FetchXML from the contact side filtered on the linked role name. Enforces
`permission.manage` at `View`.

### 3. The four states the client derives

| Mapping | Association | Meaning | Actions |
| --- | --- | --- | --- |
| Active | Yes | Assigned in app | none — consistent |
| None | Yes | Granted in Power Pages, unadopted | Adopt / Revoke |
| Withdrawn | Yes | Withdrawn in app, still granted | Adopt / Revoke |
| Active | No | Assigned in app, association missing | Adopt (repair) / Revoke |

Derived by a pure `classifyHolder` in `app/src/features/admin/roleDetail.ts`, unit-tested
cell by cell.

### 4. `al_AdoptRoleAssignment` — the write

Request `UserEmail`, `RoleCode`, `Decision`, `IdempotencyKey`. Two decisions cover all four
rows because each converges the two sources rather than patching a case:

- **Adopt** — converge on granted: write or reactivate the mapping, associate if missing.
- **Revoke** — converge on not granted: disassociate, and withdraw the mapping if one exists.

Both write an Audit Event naming the administrator who decided, the role, the person, and
which state was reconciled. Reuses `AssignUserRolePlugin.AssociateWebRole` and
`DisassociateWebRole`. Enforces `permission.manage` at `Manage`, as the other role commands
do.

### 5. App

- `useRoleHolders(roleCode)` calling `al_GetRoleHolders`.
- The role detail holders panel gains a Status column and the Adopt / Revoke actions. The
  caveat line written on 2026-09-06 — that portal-side grants would not appear — is
  replaced, because they now do.
- A discrepancy count on the Security configuration **Role assignments** tab, which is
  where an administrator looks first.
- `operations.ts` gains the three operation names; `operations.test.ts` then mechanically
  fails until each is added to the code app.

### 6. Server hardening

`GetActiveRoles` applies the AD-090 exclusion. The flag is read by resolving each name
through `WebRoleRegistry.FindByName` and reading `AuthenticatedAttr`, rather than by
selecting the attribute on the linked `powerpagecomponent` row — the typed surface is known
to carry `mspp_authenticatedusersrole`; the underlying component row is not known to expose
it, and this design does not assume it does. The extra reads are bounded by the number of
roles one contact holds.

`IsSystemRole` stops being dead code and is extended to take the flag into account.

### 7. Deployment

All three are declared `isfunction: false`, as every existing Custom API here is: the
client reaches them through `executeCommand`, whose `dataverseRequest` uses
`action: 'customapi'`. Two of them are reads, and that does not change the binding.

Per new API: contract JSON in `plugins/customapi/`, plugin class in
`OutcomeTesting.Plugins`, registration through `plugins/OutcomeTesting.Registration`,
solution folder under `src/customapis/`, then `npx pa app add dataverse-api --api-name
<name>`. Finally one `pa app push`. `DOTNET_ROLL_FORWARD` is required for the registration
console.

## Non-goals

- **Scheduled reconciliation.** Rejected above; not deferred.
- **Restricting who may edit web role membership in Power Pages.** Worth doing as a later
  control, out of scope here, and explicitly not a precondition for this work.
- **Removing the `al_approle` picklist read.** AD-087 keeps it read so nothing already
  assigned is silently revoked. Unchanged.
- **Deleting the `Checker` web role drift** recorded in OD-033. Separate.
- **Optimistic concurrency on adopt.** The command converges to a known target state and is
  idempotent by key; a lost update reconverges on the next read.

## Risks

**Every app load becomes dependent on a Custom API.** Today a failed permission read falls
back to permissive-for-view; that behaviour is preserved, but its trigger widens from "the
mapping table is unreadable" to "a plug-in is broken". Accepted because the alternative is
keeping a client that provably disagrees with the server, and because writes stay gated
server-side regardless.

**Three registrations and a deploy.** Nothing here is testable end to end until it reaches
DEV. The registration path is proven, but it is the step most likely to bite.

**The `mspp_authenticatedusersrole` read costs a query per held role.** Bounded and small,
but it is on the sign-in path. If it proves slow, the fix is to read the flag once per
role into a per-request cache, not to drop the guard.

**A contact with no work email cannot be a holder.** `AssociateWebRole` already reports
this rather than failing silently; `al_GetRoleHolders` inherits the same limit, and such a
person appears with an empty email rather than being dropped.

## Testing

**C#** (`OutcomeTesting.Plugins.Tests`): the four-cell truth table against
`al_GetRoleHolders`; Adopt and Revoke from each of the three inconsistent states;
idempotent replay returning the same result; permission enforcement on all three APIs; and
AD-090 — a contact holding a role flagged `mspp_authenticatedusersrole` resolves without
it, including when that role is not named `Authenticated Users`.

**TypeScript**: `classifyHolder` per cell; `buildRoleHolders` unchanged and still passing;
`operations.test.ts` covering registration automatically.

**In DEV, after deploy**: associate a contact with a role in Power Pages management only,
confirm it surfaces as unadopted, adopt it, and confirm the mapping and audit event exist.
Then withdraw in the app, re-associate portal-side, and confirm the row reports "withdrawn
in app, still granted" rather than "Withdrawn".
