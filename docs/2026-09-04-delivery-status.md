# Delivery status — 2026-09-04 (afternoon)

Written at commit `a590a98`. Supersedes nothing; it sits alongside
`docs/2026-09-04-outstanding-work.md`, which remains the register of what is left.
This document records what changed today after that register was written, and what
state `Env_AQ_Dev` is actually in.

Every environment claim below was verified by FetchXML query against `Env_AQ_Dev`
after the fact. Where a deployment did **not** land, it says so.

---

## 1. PP-15 is unblocked — the mailbox is approved and tested

Item 1 of the outstanding-work register is closed on DEV.

```
svc.automate.aq@ascotlloyd.co.uk
  isemailaddressapprovedbyo365admin : Yes
  emailrouteraccessapproval         : Approved
  outgoingemailstatus               : Success
  incomingemailstatus               : Success
  testmailboxaccesscompletedon      : 9/4/2026 7:31 AM
```

Both halves were verified separately, and it is worth recording why. After approval
the mailbox read `Yes` / **`Not Run`** with `testmailboxaccesscompletedon` still at
`1/1/1900` — approval records permission, it does not test anything. The AD-081 gate
requires `Yes` **and** `Success`; registering the drain step in between would have
stamped the whole `Pending` backlog `Failed`, which is the outcome the gate exists to
prevent. Test & Enable was then run and the second query returned `Success`.

**The drain step is still not registered.** Nothing sends yet. The registration
command and the `MaxRows: 1` backlog procedure are unchanged in
`docs/deployment/2026-09-04-pp15-drain-pp17-portal-deployment.md`.

Also found: a second mailbox, `svc.automate.aq-dev@ascotlloyd.co.uk`, sits at
`Pending Approval`. The runbook targets the approved production-named account, so the
two are consistent today — but a `-dev` account existing at all suggests someone meant
DEV to send from a separate address, and that should be confirmed before the step is
registered rather than after DEV email starts leaving under the other identity.

## 2. `/case-details` was broken, and is fixed in source but not in DEV

The portal case detail page returned the generic Power Pages error
(`Error ID # fb3b015d-…`, 07:36:59 UTC).

**Cause: a component id collision.** `OT Case Detail Page` (page template) and
`OT Outcome Label` (web template, added by PP-17 the previous day) were both minted as
`a1000000-0000-4000-8000-000000000022`. In the enhanced data model every component is
one row in `powerpagecomponent` keyed by id, so the two were the same row — the web
template won and the page template ceased to exist, leaving both `case-details` web
pages pointing at nothing. A web page with no page template cannot render.

This is the **second** occurrence of this exact failure. `Check-ComponentIds.ps1` was
written after the first (`OT Case List Page` / `OT Answer Options` on `...0021`, which
broke `/cases`) and was not run before the 2026-09-04 upload. See AD-084.

What was ruled out first, and how — recorded because the ruling-out is the reusable
part: the page's three FetchXML queries were run verbatim against DEV and all three
succeed; every web template the page includes (`OT Layout`, `OT Status Badge`,
`OT Empty State`) was confirmed present. So it was neither Liquid, nor a missing
include, nor a table permission.

**State now:** source is fixed and pushed (`a590a98`) — page template reminted to
`...002b`, both web pages repointed, guard passes at 237 identities. In DEV the page
template `...002b` **was** created and is bound to `OT Case Detail`. The two web pages
**were not** updated and still carry a null page template, so **the page is still
broken**. Tracked as OD-035.

## 3. `pac pages upload` can no longer deploy web pages at all

Escalated from a tooling note to OD-034, because it now blocks a fix rather than
costing time.

Three upload attempts today — delta twice and `--forceUploadAll` once, all with
`-mv Enhanced` — aborted at ~44% on the same single record:

```
Error: Upload failed for file 'adx_entitypermission'
       (record a1000000-0000-4000-8000-000000000072): the target entity was not found.
Error: The entity with a name = 'adx_entitypermission' ... was not found in the MetadataCache.
```

The site is on the **enhanced data model**; `adx_entitypermission` does not exist in
the environment, so `pac` is routing this one record down the Standard path despite
`-mv Enhanced`. The record — `Response - on a review assigned to me`, parent-scoped
under `...071` — is **already correct in DEV**, verified by query. The upload is
failing on a record that needs no change.

The consequence is what matters: the abort happens **before web pages are processed**,
so no web page change can be deployed by CLI. That is why item 2 above is half-applied.

Removing the file to get past it was considered and rejected: `pac` deletes components
absent from source — it attempted exactly that on `...0022` during the 08:49 run — so
excluding the permission risks deleting a live permission.

## 4. A live over-grant on DEV

`Administrators` carries `mspp_authenticatedusersrole = Yes`, which auto-grants it to
**every authenticated portal user**. `webrole.yml` says `false`. It was modified at
07:37, one minute after the case detail error at 07:36:59, so it reads as an attempted
workaround for that error — and it did not fix it.

Two uploads since touched the web role rows and **left the flag set**, so this needs a
deliberate correction, not another upload.

Separately, a `Checker` web role exists in DEV (created 2026-09-03) that appears in no
source file, also flagged as an authenticated-users role. An upload will never remove
it. Both are OD-033.

## 5. Users and roles rework — direction given, design not yet written

Project owner direction 2026-09-04, in answer to four questions:

1. The app's user table shows **only contacts holding an AL Portal web role**.
2. `al_User` is **retired** and replaced by Contact.
3. Roles are managed in **both** Power Pages management and the app.
4. Selecting a role shows its permissions, details, and the people assigned to it.

Not started, and two points need resolving before it can be:

- **Retiring `al_User`** collides with `al_AssignCase`, which resolves a work email to
  **both** a `systemuser` and a Contact and refuses if either is missing. Contact cannot
  take that over — Dataverse ownership and audit need the system user. DEV currently
  holds **3 active contacts** against 10 seeded `al_User` rows, so the directory would
  shrink to 3.
- **Managing roles in both places** is the mirrored option, and the only one that keeps
  BR-012's audit trail (AD-041 requires role assignment to be written by an audited
  Custom API; Power Pages management writes outside that). It needs a stated conflict
  rule, plus a rule for `Authenticated Users`, `Administrators` and `Checker`, which are
  auto-granted and have no per-person membership at all.

## 6. Corrections to the runbooks

- **`pac` is not at `%USERPROFILE%\.dotnet\tools\pac.exe`.** That path does not exist on
  this machine. The working binary ships with the VS Code Power Platform extension.
- **`pac org fetch` rejects `top` on the fetch element** — "The top attribute can't be
  specified with paging attribute page". Drop `top` from any saved query.
- **Portal tables are `mspp_*`, not `adx_*`.** `adx_webrole` and `adx_entitypermission`
  do not resolve against this environment at all. The repo's docs and manifest still
  speak in `adx_` terms.

---

## What is verified working

Recorded so it is not re-investigated: the mailbox is approved and tested; the page
template `...002b` exists in DEV bound to the right web template; the component id
guard passes on current source; and the case detail page's own queries and includes are
all sound.

## What is open from today

- **OD-033** — `Administrators` over-grant and the undeclared `Checker` role.
- **OD-034** — `pac pages upload` cannot complete; blocks all web page deployment.
- **OD-035** — the two `case-details` web pages still need repointing at `...002b`.

Everything in `docs/2026-09-04-outstanding-work.md` still stands except item 1, whose
blocking half (mailbox approval) is now done and whose remaining half is registering
the drain step.
