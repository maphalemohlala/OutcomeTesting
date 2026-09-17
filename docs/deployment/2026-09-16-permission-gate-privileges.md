# The permission gate's own privileges, granted in DEV and TEST

**2026-09-16. AD-142.** Adam Strumidlo could not import cases in TEST. The fault named
`prvReadRole` on entity `role` (OTC 1036), and it came from the FIRST line of
`PermissionHelpers.EnsureAppPermission` — the break-glass System Administrator probe —
not from anything in `ImportCasesPlugin`.

## What was wrong

The gate reads eight tables **as the caller**: every command behind it is a Custom API, a
Custom API plug-in type has no run-as user, so `PluginUserService` resolves to the
initiating user. Neither shipped role granted `prvReadRole`, so all 27 gated commands
refused every caller who was not already a Dataverse System Administrator.

Confirmed against the environments, not inferred:

- Adam holds **`Outcome Testing App Admin` only** — no Basic User. That is the fault's
  `roleCount=1`, and the role had exactly 125 privileges, its `privilegeCount=125`.
- The fault's missing-privilege id `222a920a-2778-4564-85cb-e78dde8e4276` is `prvReadRole`'s
  own id, which is identical in DEV and TEST (a core platform privilege).
- Before the fix, `prvReadRole` was granted by `Basic User` (depth 2) and
  `System Administrator` (8), and by neither app role. Adam is the one person holding an app
  role *without* Basic User, which is why he hit it first.
- `prvReadpowerpagecomponent` was granted by `System Administrator` **only** — not even Basic
  User — so the contact-to-web-role join was the next fault for everyone else.

Every DEV human is a System Administrator, which is why DEV never showed it. Same blind spot
as the 2026-09-14 note in `GrantSecurity`.

## What was deployed

`grantsecurity` now grants `role` and `powerpagecomponent` (read, Global) to both roles. Run
against DEV and TEST; no solution import, so no step registrations were touched.

Verified by query afterwards in both environments — four rows each, depth 8:

| Environment | Outcome Testing App User | Outcome Testing App Admin |
|---|---|---|
| Env_AQ_Dev  | prvReadRole, prvReadpowerpagecomponent | prvReadRole, prvReadpowerpagecomponent |
| Env_AQ_Test | prvReadRole, prvReadpowerpagecomponent | prvReadRole, prvReadpowerpagecomponent |

In TEST the solution is managed, so the two `AddSolutionComponent` calls were skipped as
expected; the privileges landed as an unmanaged layer on the role, and the role XML carries
the same grant for the next import.

`prvReadpowerpagecomponent` has a **different privilege id per environment** (DEV
`300a0699…`, TEST `b963da9d…`) because it belongs to a managed solution. Role XML and
`GrantTable` both resolve by name, so neither is affected — but a query written against the
id is only valid for the environment it came from.

## No other role has the same gap

All 24 solution tables already granted read in both roles; the only gaps were the two
platform tables above. The roles real people hold are `Basic User`, `System Administrator`,
`Outcome Testing App User` and `Outcome Testing App Admin` — all now sufficient for the gate.

## What this does NOT fix: reviews in TEST

`page.reviews` and `page.remediation` have **no `al_pagepermission` rule at all in TEST**.
TEST holds 12 rules, every one of them for role code `Administrators`, and the client uses
stored rules INSTEAD of `DEFAULT_PERMISSIONS` as soon as any row exists
(`app/src/types/permissions.ts`, `stored.length > 0 ? stored : DEFAULT_PERMISSIONS`), with a
resource that has no rule resolving to `None`.

So no one in TEST can open a Tax or AQS review — not Adam, Zoe or Gill, whatever web roles
they hold. The Dataverse layer is ready (App User holds create/write on `al_response`,
`al_reviewinstance`, `al_outcomecase`, `al_outcome`, `al_signoff`, `al_remediationaction`,
create on `al_auditevent`, and the `al_response` Append / `al_failreason` AppendTo pair the
fail-reason N:N needs — all 12 verified by query). The block is configuration only.

DEV is configured correctly: `page.reviews` grants `AL Portal - Tax Reviewer` and
`AL Portal - AQS Reviewer` Edit, as `DEFAULT_PERMISSIONS` intends.

Seeding TEST's missing rules is a decision about who gets access, so it is left to the
project owner rather than taken here.

---

**Superseded in part (2026-09-16).** The closing section above — that `page.reviews` and
`page.remediation` have no rule in TEST, so nobody there can open a review — is no longer true.
TEST now grants `page.reviews` Edit to both Tax Reviewer and AQS Reviewer, as DEV does. See
`2026-09-16-dev-test-consistency-audit.md`.
