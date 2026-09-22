# Deployment — the staff code registry, 22 September 2026

Date: 2026-09-22
Target: `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`) **only**. Promotion to TEST or PROD is
the project owner's call and has not been made.
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Implements `docs/superpowers/plans/2026-09-22-staff-codes-registry.md` (AD-207): the staff code
moves off the case and onto the person, and six Trail Light columns carry it again.

**One defect was found by testing the deployed build and fixed here.** It is the substance of
this note, because it had passed every unit test.

---

## What was deployed

| Step | What | Evidence |
|---|---|---|
| 1 | `dotnet build -c Release` | 305,664 → **307,712 bytes**, built immediately before the push |
| 2 | `addtextcolumn contact al_StaffCode` | created text(50) "Employee code", added to solution `OutcomeTesting` |
| 3 | `POST customapirequestparameters` → `al_UpdateUser.StaffCode` | HTTP 204, then `AddSolutionComponent` type **10039** |
| 4 | `pushwebtemplate` OT Case Detail | 56,469 → 56,627 chars |
| 5 | `pushwebtemplate` OT Review Detail | 196,470 → 196,894 chars |
| 6 | `pushassembly` | 307,712 bytes, matching the Release build exactly |
| 7 | `npm run build` then `pa app push` | bundle `index-D4ZeqXhw.js` |
| 8 | **fix** + `npm run build` then `pa app push` | bundle `index-BC-ck_aH.js` |

No `pac solution import`. DEV is the authoring environment and the only metadata this work adds
is the column and the request parameter, both applied directly; `git diff` over `src/` confirms
nothing else changed, so there were no step definitions to carry. `verifysteps` afterwards:
**0 missing, 0 disabled** across 24 steps.

### Why `pushwebtemplate` rather than `Deploy-Portal.ps1`

The plan called for `Deploy-Portal.ps1`. Two templates changed, and `pushwebtemplate` writes
exactly those two and reads each back, where the script uploads the whole tree — and
`powerpages/` here is hand-authored source, not a mirror, so a whole-tree upload can push stale
local config over newer live state and still report success (AD-196). `pushwebtemplate` is also
what every deployment note since 2026-09-13 has actually used.

Before pushing, both live templates were downloaded and diffed against the branch's base commit
(`a21ece5`). **Byte-identical** apart from a BOM, so nothing newer existed in DEV to overwrite.
That check is the reason the narrower verb is safe here, and it is the check to repeat next time.

---

## The defect: the People page was wired to a retired table

The Role filter returned **"0 of 12 people"** for every role, and Grant was refused server-side
with `The role code does not match an active role.`

Both had one cause. The page was built against `al_Role` — the six labels in `data/roles-seed`:
Adviser, Paraplanner, T&C Manager, Tax Checker, AQS Checker, Senior Checker. What
`al_userrolemapping` actually carries is a Power Pages **web role name**: `AL Portal - Planner`,
`AL Portal - T&C Supervisor`, `Administrators`. The two vocabularies share no member, so:

- `matchesRole` compared `"Paraplanner"` against `"AL Portal - Planner"` and matched nobody.
- `assignUserRole` sent `RoleCode=ROLE-PARAPLANNER`, and `AssignUserRolePlugin` resolves
  `RoleCode` against the web role registry **only**.

`al_role` is retired, and deliberately so. `RoleCodeExists` records that the `al_role` fallback
was removed under OD-037, verified against DEV on 2026-09-08: no `ROLE-*` code appears in
`al_userrolemapping` or `al_pagepermission`, the eleven rows are referenced by nothing, and
dropping the last read of the table is what allows it to be deleted. A query of DEV during this
deployment confirmed all 26 mappings carry web role names and not one carries a `ROLE-*` code.
The roles themselves are still there and active — `ROLE-PARAPLANNER` exists — which is what
makes the failure read like an environment problem rather than a design one.

### Why the tests did not catch it

They pinned `ROLE_CODES['Paraplanner'] === 'ROLE-PARAPLANNER'` and `canWithdraw` against
`ROLE_FILTERS`. Every assertion compared the module to itself or to the seed file. Nothing
compared it to a vocabulary the server accepts, so a self-consistent and entirely wrong map
passed 35 tests.

The replacements use real live web role names, and one asserts that **every offered role matches
something** — the shape of the original defect, which was an option that could never match.

### The fix

Both the filter and Grant now read `useRoles`, the live `mspp_webrole` list, exactly as Security
configuration already did. The web role's name *is* the code `al_rolecode` carries, so there is
no translation and no second list. `canWithdraw` takes the grantable set as an argument for the
same reason: Grant and Withdraw offer the same roles by construction, and offer none while the
list is still loading.

**This drops Senior Checker.** No web role corresponds to it, so it could never have been
granted. The other five map onto live roles — including Paraplanner, which is
`AL Portal - Planner`. If a Senior Checker role is wanted it has to be created as a web role
first, which is a portal access decision and not one this deployment took.

---

## Verified live in DEV

**The registry write path, end to end.** `al_UpdateUser` with `StaffCode=ADV-REG-001` returned
`Conflict=False`; the contact read back `al_staffcode = "ADV-REG-001"`; the audit row's
`al_details` read `Name … ; Employee code (none) -> ADV-REG-001`. Then the same through the
page's own Edit dialog, saved, **full page reload**, and the value still there. That reload is
the step that catches the silent-drop trap: an undeclared parameter reports success and vanishes.

**The six code columns, and the Critical this work exists to fix.** Two export batches were
generated against a fixture carrying deliberately planted stale case codes.

| Column | Case 900000004 (nobody named) | Case 900000001 (Zoe Ramwell / Gill Philpott named) |
|---|---|---|
| B `al_advisercode` | `ADV-REG-001` | `PP-REG-002` |
| D `al_paraplannercode` | `PP-REG-002` | `ADV-REG-001` |
| R `al_aqfailadvisercode` | **`ADV-REG-001`** | blank — named person holds no code |
| N `al_fqfailparaplannercode` | blank | blank — named person holds no code |

The cases carried `al_advisercode = "STALE-CASE-ADV"` and `"STALE-NAMED-ADV"` throughout.
**Neither string appears in any column of either batch.** That is the 2026-09-22 Critical proved
from both directions: the *unnamed* branch (900000004, which reaches accountability by
derivation because its final outcome is Pass with issues) emits the registry code, and the
*named* branch emits the named person's own code or nothing — never the case's.

Name and code agree on every row: `al_aqfailadvisername = "Simunye Radingwana"` beside
`al_aqfailadvisercode = "ADV-REG-001"`.

**Roles.** Filter returns 3 of 12 for `AL Portal - T&C Supervisor` — exactly the three holders —
and 1 of 12 for `AL Portal - Planner` after granting it. Grant reported success and wrote
`al_userrolemapping` with `al_approle` explicitly null. Withdraw reported success and the row
**survives at `statecode: 1`**: audit evidence, not an erasure.

**Search** finds a person by employee code (`ADV-REG-001` → Simunye Radingwana).

**Portal.** Case detail for 900000004 (Closed) renders Adviser name, Adviser status and
Paraplanner with **no Adviser code or Paraplanner code row**; 900000002 (Assigned) likewise, with
no text inputs at all. Both templates read back byte-identical to source after the push, and
`data-ot-hdr="al_advisercode"` appears nowhere in `powerpages/`.

**The clearing branch.** `StaffCode=""` cleared both test codes back to null, which is how the
"absent leaves unchanged, empty clears" rule was confirmed rather than assumed.

## Not proved live

- **A non-administrator refused `/admin/people`.** DEV silently ignores `--as` (OD: no
  impersonation), so this needs a second signed-in identity. The route is gated by the
  pre-existing `page.admin.users` and covered by unit tests.
- **The editable portal branch.** No open case is assigned to the deploying account, so the
  header edit form never rendered. Covered by the source grep and the byte-identical read-back.

## Fixtures restored

Both cases are back to `al_advisercode`/`al_paraplannercode` null, case 900000004's paraplanner
back to `Service Account` with no email. **Both test employee codes were cleared**: no contact
in DEV holds a staff code, so nothing fabricated can reach a Trail Light file.

Two export batches from the verification remain in DEV in `Generated` state. They hold the test
codes, which no longer match any contact.

## Before the next Trail Light batch

Employee codes must be loaded onto contacts, on `/admin/people`. Between this deployment and
that load, **six** columns are blank where they previously carried email addresses — B, D and
the four accountability code columns L, N, R and T. That is operational, not a code change, and
it is the one way this work could land worse than what is in TEST today.
