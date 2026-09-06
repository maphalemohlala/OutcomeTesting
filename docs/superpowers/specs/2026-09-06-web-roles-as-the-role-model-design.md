# Web roles as the application role model

Date: 2026-09-06
Status: Approved for implementation
Environment probed: `Env_AQ_Dev` (`org0b075da8`)
Follows: `2026-09-06-contacts-registry-design.md`

## Problem

Application roles should come from Power Pages web roles, and be manageable,
assignable and configurable inside the app.

## Evidence

### Web roles are `mspp_`, not `adx_`

This environment runs the Power Pages **enhanced data model**. `adx_webrole` does
not exist. Roles are `mspp_webrole`, a typed surface over `powerpagecomponent`.
Ten exist:

| Web role | Kind |
| --- | --- |
| AL Portal — Tax Reviewer | business |
| AL Portal — AQS Reviewer | business |
| AL Portal — T&C Supervisor | business |
| AL Portal — Outcome Testing Manager | business |
| AL Portal — Adviser Remediation | business |
| AL Portal — Planner | business |
| AL Portal — Portal Administrator | business |
| Administrators | business |
| Anonymous Users | Power Pages plumbing |
| Authenticated Users | Power Pages plumbing |

### Assignment already exists, and already matches the registry

The contact-to-role intersect is **`powerpagecomponent_mspp_webrole_contact`**
(`contact.contactid` to `powerpagecomponent.powerpagecomponentid`). There is no
many-to-many on `mspp_webrole` itself; it is reached from the contact side.

Current assignments line up exactly with the three Contacts:

| Contact | Web role |
| --- | --- |
| Dev Account | AL Portal — Tax Reviewer |
| Sims Rad | AL Portal — AQS Reviewer |
| Service Account | Administrators |

### The seam

`al_PagePermission` and `al_UserRoleMapping` both carry **`al_rolecode`**, a
free-text stable role code introduced for AD-044 custom roles, which already takes
precedence over the `al_approle` picklist. A web role name flows through that field
with **no schema change**. The extension point needed already exists.

### Feasibility, proved not assumed

A throwaway probe (`probewebrole`) created a web role, renamed it, associated it to
a contact, disassociated it and deleted it. All succeeded. Web roles are fully
manageable through the supported API, so "managed, assigned and configured in the
app" is achievable.

`mspp_webrole` was added as a Code App data source and generated a typed model, so
the app can read roles directly.

### Two quirks worth recording

- `mspp_webrole` does not answer a plain `QueryExpression`, and ignores `top` in
  FetchXML. Query it with FetchXML and no `top`.
- The website table is not directly queryable; take the `mspp_websiteid` reference
  from an existing role, or use the known website id.

## Decisions

| # | Decision |
| --- | --- |
| D1 | Web role names replace the six AD-020/AD-031 role names as the permission vocabulary. |
| D2 | Assignment writes the intersect **and** mirrors into `al_UserRoleMapping`. |
| D3 | Role resolution is the **union** of intersect roles and mirror role codes. |
| D4 | `Anonymous Users` and `Authenticated Users` are excluded from the app's role list. |
| D5 | No schema change: web role names travel in the existing `al_rolecode` field. |

### Why the union (D3)

The user chose to replace the vocabulary outright, and I flagged that as the
lockout risk: `DEFAULT_PERMISSIONS` keys on the six old names, so the moment the
vocabulary changes, every default rule stops matching. `PermissionProvider` fails
open only while the mapping table is *empty*, not while it is populated and
mismatched.

Resolving roles as the union of both sources is what makes the replacement safe.
The intersect is authoritative and satisfies "directly from web roles"; the mirror
carries the same facts for reporting and keeps any legacy mapping working. Neither
alone would be safe during the cut-over.

## Design

### 1. Vocabulary

`APP_ROLES` is replaced by the eight business web roles. `DEFAULT_PERMISSIONS` is
re-authored against those names, carrying the existing intent across:

| Old role | Web role |
| --- | --- |
| Tax Checker | AL Portal — Tax Reviewer |
| AQS Checker | AL Portal — AQS Reviewer |
| Adviser | AL Portal — Adviser Remediation |
| T&C Manager | AL Portal — T&C Supervisor |
| Outcome Testing Manager | AL Portal — Outcome Testing Manager |
| Administrator | AL Portal — Portal Administrator, and Administrators |

**AL Portal — Planner has no predecessor.** No requirement describes it, so rather
than invent authority (AGENTS.md rule 4) it gets dashboard and case *View* only.
Flagged for a decision-log entry.

`al_approle` is left in the schema and still read server-side, so nothing that
already depends on it breaks. It is simply no longer offered in the app.

### 2. Resolution

`PermissionHelpers.GetActiveRoles` returns the union of:

- web roles associated to the caller's contact, via the intersect; and
- `al_userrolemapping.al_rolecode` and `al_approle` rows, as today.

`PermissionProvider` mirrors the same union client-side.

### 3. Assignment

- `al_AssignUserRole` — resolves the contact by work email, associates it to the
  web role, and upserts the `al_UserRoleMapping` mirror with `al_rolecode` set to
  the web role name.
- `al_SetRoleAssignmentActive` — disassociates and deactivates the mirror row.

Both keep writing their Audit Event.

### 4. Management

- `al_CreateRole` — creates an `mspp_webrole` on the site instead of an `al_role`.
- `al_UpdateRole` — renames, re-describes or deactivates the web role.

### 5. Configuration

`al_PagePermission` rows key on `al_rolecode` = web role name. The Security
Configuration screen offers web roles in its dropdowns. No schema or plugin change
is needed for this; it already works by code.

### 6. App

- `mspp_webrole` data source (added).
- `useWebRoles` hook, excluding the two system roles.
- Security Configuration: role dropdowns from web roles, plus create / rename /
  retire.
- People: assign and withdraw a person's roles.

### 7. Migration

Sync the intersect into `al_UserRoleMapping` so the mirror is populated before the
vocabulary changes, then seed `al_PagePermission` defaults for the web roles.

## Non-goals

- Changing Power Pages table permissions or web page access rules.
- Deleting `al_Role`, `al_approle` or the `al_User` table.
- TEST or PROD.

## Risks

| Risk | Mitigation |
| --- | --- |
| Lockout when the vocabulary changes | D3 union, plus the migration populates the mirror before the switch |
| A web role renamed outside the app orphans its rules | Rules key on name; the rename path is the app's own command, and a rename is reported |
| `mspp_webrole` query quirks | FetchXML without `top`, recorded above |
| Planner has no requirement behind it | View-only, flagged for the decision log |

## Testing

- Unit: the web role vocabulary and re-authored matrix resolve as intended;
  `Anonymous`/`Authenticated` excluded; union resolution.
- Plug-in: existing suite green; new cases for the intersect resolution.
- Evidence: probe assignments before and after; confirm the service account keeps
  `permission.manage` throughout.
