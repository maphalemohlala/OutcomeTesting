# Role and permission audit — DEV and TEST

**2026-09-16.** Full audit of the two tiers: Dataverse security roles (the real boundary)
and `al_pagepermission` application rules (what the UI gates on). Checked against both
environments live, not against the repo alone.

## Correction to an earlier statement

I reported that `page.reviews` had no rule in TEST and that nobody could complete a review
there. **That was wrong** — it came from a truncated query output. TEST has 35 active rules
covering all 17 resource keys; `page.reviews` grants Tax Reviewer Edit, AQS Reviewer Edit,
T&C Supervisor Edit, Adviser Remediation Manage and Administrators Manage. Reviewers can
complete reviews in TEST.

## Findings

### F1 — Regrade and sign-off have no effective authorization (High)

`al_RegradeCase` and `al_SignOffRemediation` are public Custom APIs (`isprivate=0`) with no
`executeprivilegename`. Neither calls `EnsureAppPermission`. Unlike `al_SubmitReview` and
`al_CompleteRemediation` — which refuse a caller who is not the assigned checker or the
owning adviser — neither has any ownership guard.

Both class comments state the control is the Dataverse privilege: *"the sign-off is created
as the initiating user, so a caller without create-al_signoff privilege is refused."* But
`GrantSecurity` puts `al_outcome` and `al_signoff` in `writeForEveryone`, so **both** app
roles hold create/write on them at Global depth. Verified live in TEST:
`Outcome Testing App User` holds `prvCreateal_Outcome` and `prvCreateal_Signoff`.

So any holder of either app role can regrade a case outcome or sign off a remediation by
calling the Custom API directly. `command.regrade` and `command.signoff` — restricted to
T&C Supervisor by AD-031 — are enforced in the client only. The privilege the design leans
on does not discriminate, because everyone has it.

Fixing it means one of: an ownership/role guard in the two plug-ins (as `SubmitReview`
already has), an `executeprivilegename` on the two APIs, or removing create on `al_outcome`
and `al_signoff` from `Outcome Testing App User`. The first matches the existing pattern.

### F2 — Three app roles grant nothing in TEST (High, TEST only)

`AL Portal - Outcome Testing Manager`, `AL Portal - Planner` and
`AL Portal - Portal Administrator` have **zero** rules in TEST. 31 documented grants are
missing overall. Anyone holding only one of those roles sees no access anywhere — including
the Outcome Testing Manager's own intake and export pages. Zoe Ramwell holds two of them and
is saved only by also holding `Administrators`.

### F3 — Five conflicting duplicate rules in DEV (Medium, DEV only)

Seven (role, resource) pairs have two active rows; five carry **different levels**. Both
tiers take the highest, so editing one row down does not lower access:

| Role | Resource | Rows | Effective |
|---|---|---|---|
| AL Portal - AQS Reviewer | page.cases | View, Manage | Manage |
| AL Portal - Adviser Remediation | remediation.complete | Edit, Manage | Manage |
| AL Portal - T&C Supervisor | command.regrade | Edit, Manage | Manage |
| AL Portal - T&C Supervisor | command.signoff | Edit, Manage | Manage |
| AL Portal - T&C Supervisor | page.remediation | Edit, Manage | Manage |

### F4 — Drift above the documented model (Medium)

DEV grants 12 levels above `DEFAULT_PERMISSIONS`, TEST 23. The segregation-of-duties ones:

- `AL Portal - Adviser Remediation` → `command.signoff` = Manage (**DEV**)
- `AL Portal - Adviser Remediation` → `command.regrade` = Manage (**TEST**)
- `AL Portal - Adviser Remediation` → `page.reviews` = Manage (both)

An adviser holding regrade or sign-off is the same authority AD-031 gives the T&C Supervisor
precisely so the person who did the work is not the person who signs it off. Combined with
F1, these are not merely UI grants.

Also: in TEST, `AL Portal - T&C Supervisor` has **no** `command.regrade` rule — the role
that should own regrade does not have it, while the adviser role does.

### F5 — 17 dead rules in DEV (Low)

DEV holds 17 active rules with an **empty** role code, all at Manage, one per resource key.
They grant nothing: the server's `MaxLevel` matches on `al_rolecode IN (…)` and the client's
`resolvePermissions` skips a rule whose role is not held. They are inert seed rows — but
they still make `stored.length > 0`, which is what switches `DEFAULT_PERMISSIONS` off, and
they show up in the Security configuration page as rules that appear to grant Manage.

### F6 — The `al_role` table is dead (Low)

DEV holds 11 `al_role` records (`ROLE-AQS-CHECKER`, `ROLE-TAX-CHECKER`, …). No plug-in reads
the table and the Code App has no data source for it; `al_AssignUserRole` validates a role
code against `mspp_webrole` by name and writes that name. Assigning someone an `al_role`
grants nothing — a trap for anyone who finds "AQS Checker" and assumes it is the reviewer
role.

### F7 — Only 6 of 17 resource keys are enforced server-side (Information)

Server-enforced: `permission.manage`, `question.retire`, `page.imports`, `page.cases`,
`export.generate`, `command.assign`. The other 11 — including `page.reviews`,
`page.remediation`, `remediation.complete`, `command.regrade`, `command.signoff` — are
client-only. That is the documented design (AGENTS.md: the app layer is advisory, Dataverse
is the boundary), and it holds wherever the underlying privilege discriminates. F1 is where
it does not.

## What is clean

- All 24 solution tables grant read in **both** roles. No gaps.
- The 8 tables the permission gate reads are granted in both environments (AD-142).
- The review path is fully privileged: create/write on `al_response`, `al_reviewinstance`,
  `al_outcomecase`, `al_outcome`, `al_signoff`, `al_remediationaction`, create on
  `al_auditevent` — 12 privileges verified by query on `Outcome Testing App User` in TEST.
- The fail-reason N:N is correct: Append on `al_response` + AppendTo on `al_failreason`.
- `al_auditevent` is create-only — no write, no delete — so BR-012 immutability holds.
- No delete privilege on any business table, in either role.
- 17/17 resource keys have at least one role-bearing rule in both environments, and no rule
  references a resource key the code does not define.
- `al_SubmitReview` and `al_CompleteRemediation` both enforce ownership server-side.

## Not audited

Power Pages table permissions were **inventoried, not audited**: 16 permissions across 13
tables, 10 of them Global-scoped. Confirming those against what each portal page actually
reads, and against the portal's web-page access rules, is a separate pass.

---

# Part 2 — Power Pages table permissions (audited 2026-09-16)

16 permissions across 13 tables, checked against the site's web roles and against what the
portal templates actually read.

## Nothing here is a defect

Two things looked wrong and are not:

**Global read for `Authenticated Users`** on `al_outcomecase`, `al_response`,
`al_reviewinstance`, `al_remediationaction`, `al_outcome`, `al_question`,
`al_questionversion`, `al_section`, `al_reviewroute` and `al_failreason`. This reads as a
contradiction of AD-090, which excludes auto-granted roles from conferring application
access. It is not: **OD-022 is explicit** — *"All authenticated users see all cases; they can
action only what is assigned to them. Project owner direction ... read is Global scope
(756150000)."* The two rules govern different things: AD-090 governs what a web role
contributes to the APPLICATION gate, OD-022 governs portal READ. Left alone.

**Self-scoped write on `contact`** for Tax Reviewer, AQS Reviewer and T&C Supervisor. No
portal template writes a contact field — `emailaddress1` is only ever read for display — so
this looks removable. It is load-bearing. Four browser-driven writes on this site work by
having the page write one text column on the signed-in user's own contact row, with the plug-in
doing the real work as the application user: the answer create (AD-053), the claim (AD-076),
the T&C attestation (AD-099, `al_signoffrequest`) and the regrade (OD-041,
`al_regraderequest`). The Self scope is what confines each to the person who signed in.
Removing write would break all four.

## No functional gaps

`al_checklist` and `al_checklistversion` have no portal permission, and the review detail
template references `al_checklistversionid`. That is a lookup attribute ON `al_reviewinstance`
— its id and name come back with the parent row — so no permission on the target table is
needed. Verified by reading the template's FetchXML rather than assuming.

`al_auditevent`, `al_notification`, `al_importbatch`, `al_importexception`,
`al_pagepermission` and `al_userrolemapping` have no portal permission and no portal template
reads any of them. Intake, exports and security configuration are Code App surfaces.

## The portal half of AD-143 was already closed

`SignoffRequestPlugin` and `RegradeRequestPlugin` check the signing contact's **web roles**
server-side, because a Power Pages write arrives as the site's application user and a caller
check would enforce nothing (AD-053). Sign-off requires `AL Portal - T&C Supervisor`. The gap
in F1 was only ever the Code App route — which is exactly what OD-041 describes as the reason
the portal path had to exist at all.

## One cosmetic note

`al_reviewinstance` carries a Global read permission naming Tax Reviewer, AQS Reviewer,
Administrators **and** Authenticated Users. The first three are redundant: Authenticated Users
already covers every signed-in user. Harmless, and not worth a change on its own.

---

# Part 3 — Fixes applied and verified (2026-09-16)

## Code (AD-143)

`al_RegradeCase` and `al_SignOffRemediation` now call `EnsureAppPermission` on
`command.regrade` / `command.signoff` before doing anything. Written test-first: the tests
failed to compile against the absent constants, then passed against the guards. **864 tests
pass.** `RegradeCasePlugin.Regrade` stays public and ungated so `RegradeRequestPlugin` keeps
reaching it after its own web-role check. Both stale class comments — the "authorization is
enforced by Dataverse" claim that hid the gap — were corrected.

## Configuration (`fixpermissions`)

Run against both environments. Each matched its dry run exactly:

| | DEV | TEST |
|---|---|---|
| created | 0 | 31 |
| re-levelled | 7 | 2 |
| deactivated | 26 | 2 |
| active rules after | 55 | 64 |

DEV lost the 17 no-role rows and 7 duplicate rows, and the adviser lost `command.signoff`
and `page.reviews`. TEST gained every rule for `Outcome Testing Manager`, `Planner` and
`Portal Administrator` — which had none at all — plus the T&C Supervisor's `command.regrade`,
and the adviser lost `command.regrade` and `page.reviews`.

## Verified by re-query, not by the tool's own output

Both environments re-read afterwards and checked for: duplicate (role, resource) pairs, rows
with no role code, the three AD-031 adviser rules, documented grants missing, levels not
matching the model, and app roles with no rules. **All six clean in both.**

Effective access on the rules that changed:

| Resource | DEV | TEST |
|---|---|---|
| `command.regrade` | T&C Supervisor=Edit | Administrators=Manage; T&C Supervisor=Edit |
| `command.signoff` | T&C Supervisor=Edit | Administrators=Manage; T&C Supervisor=Edit |
| `page.imports` | Administrators=Edit; OTM=Edit; Portal Admin=Edit | Administrators=Manage; OTM=Edit; Portal Admin=Edit |

Both satisfy the `AccessEdit` the new guards require, so the T&C Supervisor can still do the
job AD-031 gives them, and the adviser no longer can.

## Assembly

Built `-c Release` first, because `pushassembly` uploads `bin/Release` without building.
226,304 bytes to both environments, matching the freshly built file. Assembly `modifiedon`
confirms DEV 15:13Z and TEST 15:14Z.

**All 51 registered steps are Enabled in both environments** — checked because a solution
import carrying step XML without `--activate-plugins` switched six steps off on 2026-09-02.
`pushassembly` is not an import and did not touch them, and the count confirms it.

## One consequence worth knowing

In DEV, `command.regrade` and `command.signoff` now name only the T&C Supervisor. Every DEV
human holds System Administrator, which short-circuits the gate, so nobody is blocked today —
but an `Administrators` holder who is not a Dataverse System Administrator would be refused
both. That is AD-031 working as written, not a gap.
