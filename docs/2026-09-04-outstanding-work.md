# Outstanding work

Started 2026-09-04 at commit `77c1ab7`. **Last reviewed 2026-09-05**, after the case detail
incident and the DEV verification pass — every environment claim below was re-queried that
day rather than carried forward.

This is the register of what is left, not a status report. `docs/2026-09-03-delivery-status.md`,
`docs/2026-09-04-delivery-status.md` and `docs/deployment/2026-09-04-pp15-drain-pp17-portal-deployment.md`
cover what exists and how it got there. Every item names an owner, the evidence it rests on,
and what "done" looks like, so nothing here needs re-deriving.

**Ordered by what it costs to leave alone, not by effort.** The order changed on 2026-09-05:
two live faults arrived and the item that had been first was unblocked.

---

## 1. OD-033 — a live privilege over-grant on DEV

**Owner:** Delivery. **Effort:** one field. **Status:** fix written, not yet applied.

`Administrators` carries `mspp_authenticatedusersrole = Yes` in DEV. That flag auto-grants
the role to **every authenticated portal user**, so every authenticated contact currently
holds Administrators. `webrole.yml` declares `false`. Re-verified 2026-09-05 by query — still
set, and two uploads since have left it set, so it needs a deliberate correction rather than
another upload.

Separately, a `Checker` web role exists in DEV (created 2026-09-03), appears in no source
file, and is **also** flagged as an authenticated-users role. An upload will never remove it.

**Done when:** the flag is cleared on `Administrators`, and `Checker` is either declared in
source or deleted. The first half is mechanical; the second needs someone to say which it is.

`registerstep`-style repair command now exists for the first half:
`setwebroleauth <orgUrl> "Administrators" false`.

## 2. OD-035 and OD-034 — `/case-details` is broken and cannot be deployed

**Owner:** Delivery. **Status:** OD-035 fix written, not yet applied. OD-034 unsolved.

**OD-035.** Both `case-details` web pages (`…032` root, `…042` content) carry a **null** page
template in DEV, so the page returns the generic Power Pages error. The page template
`…002b` exists and is correctly bound to the `OT Case Detail` web template (`…0015`), and
source declares `…002b` on both pages. Re-verified 2026-09-05: page template correct, both
web pages still null. It is a two-row fix.

Repair command: `repointwebpage <orgUrl> case-details "OT Case Detail Page"`.

**OD-034 is why OD-035 is stuck.** `pac pages upload` aborts at ~44 % on a single
`adx_entitypermission` record (`a1000000-…-072`) that is **already correct** in DEV — the site
is on the enhanced data model, `adx_entitypermission` does not exist in the environment, and
`pac` routes that one record down the Standard path regardless of `-mv Enhanced`. The abort
lands **before web pages are processed**, so no web page change can be deployed by CLI at all.
Three attempts on 2026-09-04, delta twice and `--forceUploadAll` once.

`pac pages upload` has no scoping flag — only `--path`, `--deploymentProfile`,
`--forceUploadAll`, `--modelVersion` — so the record cannot be skipped. Removing it from
source was considered and rejected: `pac` deletes components absent from source.

**Done when:** OD-035's two rows are repointed and `/case-details` renders; and a portal
deployment path exists that runs to completion, or the repair commands are accepted as the
interim path with the risk written down.

## 3. Register the PP-15 drain step

**Owner:** Delivery, after one confirmation. **Blocks:** PP-15, BR-009 actually delivering.

> **The mailbox blocker is closed.** Approved *and* Test & Enable run; re-verified 2026-09-05:
> `isemailaddressapprovedbyo365admin = Yes`, `outgoingemailstatus = Success`,
> `testmailboxaccesscompletedon = 9/4/2026 7:31 AM`. The AD-081 gate is met.

Worth keeping, because it nearly went wrong: immediately after approval the mailbox read
`Yes` / **`Not Run`**. Approval records permission, it does not test anything. Registering the
step in that window would have stamped the whole `Pending` backlog `Failed`, which is the
outcome the gate exists to prevent. The gate requires `Yes` **and** `Success`.

**One thing to confirm first.** A second mailbox, `svc.automate.aq-dev@ascotlloyd.co.uk`,
sits at `Pending Approval`. If DEV was meant to send from that identity, registering the step
now means DEV email starts leaving under the production-named account. Settle which identity
DEV sends from **before** registering, not after.

**Done when:** the sending identity is confirmed; the step is registered (command in the
deployment doc); and the existing `Pending` backlog is worked with `al_DrainNotifications`
starting at `MaxRows: 1`, reading `al_failurereason` on anything that lands at `Failed`
before running a larger batch. The step fires on **Create**, so the backlog does not drain
by itself.

Note `al_DrainNotifications` is already live and callable (Manage on `permission.manage`).
Invoking it before the identity question is settled sends real email.

## 4. OD-032 — `SetFailAccountability` has no `al_command` value of its own

**Owner:** whoever owns the AD-039 accountability trail. **Effort:** small, additive.

The most substantive open defect in the code, and it is not cosmetic. The command reuses
`120910788`, which the option set labels `SetRoleAssignmentActive`. Two live consequences:

- Every Audit Event that command writes is **labelled as a different command**, so the
  accountability trail reports a fail-accountability change as a role change.
- `CommandHelpers.FindAuditByKey` scopes a replay lookup to `(idempotency key, command)`
  precisely so one command's key cannot replay against another. With two commands sharing a
  value **that protection is off between them**, and a key first used by one can return the
  other's result for work that never ran.

The fix is additive — mint a value, repoint `SetFailAccountabilityPlugin`, leave existing
rows alone since they are immutable (NFR-AUD-01). It is open only because it changes what
already-written audit rows mean, which is a decision rather than a silent correction.

**Done when:** the trail owner has agreed how existing rows are to be read, a new value is
minted, and the plug-in points at it.

## 5. Close the gap between `src/` and DEV before anything is promoted

**Owner:** Delivery. **Blocks:** every promotion to TEST and PROD.

`al_Notification` has **no definition in `src/Entities/` at all** — it was created through the
metadata API — plus the table changes made on 2026-09-03. Until the AD-013 export round trip
runs, `src/` is not the source of truth and a managed promotion cannot be trusted to carry
what DEV actually has.

**Done when:** an export round trip has brought `al_Notification` and the 2026-09-03 changes
into `src/`, and a pack of `src/` reproduces the DEV solution.

Related and still only **partially resolved: OD-011** — Code Apps production readiness, tenant
availability and per-persona licensing. It was deferred "until the app is ready to promote".
It is close to being that, so it should stop being deferred by default.

## 6. Name PP-15's other four events

**Owner:** Product owner. **Effort:** additive once named.

PP-15 says nine events; five are built, being the five AD-035 enumerates. The other four
appear in no requirement, knowledge file or design document (OD-030 gap (a)). Adding option
values later is additive and safe, which is why five shipped — but PP-15 is not met as
written until someone names them.

**Done when:** four events are named, with their recipients, and added to the option set and
the emitters.

## 7. OD-025 — the plug-in signing key is committed to the repository

**Owner:** Platform owner / IT security. **Aging, and the cost grows.**

`OutcomeTesting.Plugins.snk` is a full private key blob in git, and it reproduces the
`PublicKeyToken=86b764d5a2430b1f` the registration tool expects. Key Vault injection at build
time is agreed; **rotation and history removal are not scheduled**, and the Key Vault does not
close the finding while the committed key still reproduces the current token.

Be clear about the shape of the cost, because it is what keeps deferring this: rotation
changes the public key token and so requires re-registering every plug-in type in every
environment, and history removal means a force-push over commits already published to
`github.com/maphalemohlala/OutcomeTesting`. **Both get more expensive with every push** —
five more landed across 2026-09-04 and 2026-09-05.

This is defence-in-depth, not a live exploit: strong-naming is not a .NET trust boundary and
abusing it needs Dataverse deployment privilege. That is a reason to schedule it, not to keep
deferring it.

## 8. OD-023 — support model: hours of cover and response targets

**Owner:** Platform owner.

Owning teams are named (AQS and Tax) and escalation is human-triggered by direction, so
**nothing fires on a timer** — which makes the hours of cover the only thing that tells a user
when to expect a response. Still unstated: hours and response targets, split between
portal-down and a single user blocked; and who the hand-off goes to when something needs a
configuration or platform change, since AQS and Tax do not hold Power Pages or Dataverse admin.

## 9. Users and roles rework

**Owner:** Delivery, on project owner direction 2026-09-04. **Not started.**

Direction: the app's user table shows only contacts holding an AL Portal web role; `al_User`
is retired in favour of Contact; roles are managed in both Power Pages management and the
app; selecting a role shows its permissions, details and assignees.

Two points need resolving before design:

- **Retiring `al_User`** collides with `al_AssignCase`, which resolves a work email to **both**
  a `systemuser` and a Contact and refuses if either is missing. Contact cannot take that
  over — Dataverse ownership and audit need the system user. DEV holds 3 active contacts
  against 10 seeded `al_User` rows, so the directory would shrink to 3.
- **Managing roles in both places** is the only option that keeps BR-012's audit trail
  (AD-041 requires role assignment to be written by an audited Custom API; Power Pages
  management writes outside that). It needs a stated conflict rule, plus a rule for
  `Authenticated Users`, `Administrators` and `Checker`, which are auto-granted and have no
  per-person membership at all.

## 10. OD-028 reads as closable

**Owner:** Delivery.

The evidence already recorded looks complete: `primaryKey="true"` moved from `al_name` to
`al_rolecode` to match the real alternate key, the orphan check was done, and
`data/roles-seed` was imported against DEV twice with 0 failures and 11 `al_role` rows before
and after. Per the decision log's own recording rules an OD is not moved to resolved without a
named owner and date, so it is flagged here rather than closed in passing.

**Done when:** Delivery confirms and marks it resolved with a date.

---

## Standing controls — run these, they are not optional

- **`powerpages/Check-ComponentIds.ps1` before every `pac pages upload`.** Power Pages
  component ids are **one keyspace**, not one per component type: a web template and a page
  template minted on the same id are the same `powerpagecomponent` row, and one silently
  destroys the other (AD-084). This has now happened twice — `…0021` broke `/cases`, `…0022`
  broke `/case-details`. The id-band comments in source are not enforced anywhere, so this
  guard is the only thing standing between a hand-minted id and a deleted component. It
  currently passes at 237 identities.
- **Verify every portal upload by query afterwards.** While OD-034 stands the upload aborts
  partway, so an exit code says nothing about what landed.
- **Check Custom API solution membership after `registerall`.** Now automatic: `registerall`
  reports any API missing from `OutcomeTesting` and prints the command that adds it.

## Carry — known and accepted, unchanged

- **OD-029** per-team allocation scoping.
- **Senior Checker** needs `command.assign` granted as a Dataverse row.
- **Optimistic concurrency is not sent on the portal submit path.**
- **10 `react-hooks/exhaustive-deps` lint warnings**, all pre-existing, zero errors.

## Closed on 2026-09-05

- **The registration tool's runtime trap.** It targets `net8.0` against a machine carrying
  only the .NET 10 runtime and failed to launch with a message that reads like a missing SDK.
  `<RollForward>LatestMajor</RollForward>` is now in the csproj; the exe launches with no
  environment variable. The tested target framework is unchanged deliberately.
- **Custom APIs silently staying out of the solution.** `registerall` now reports membership
  rather than leaving it to be noticed by hand.

## What is *not* outstanding

Recorded so it is not re-investigated: PP-01 to PP-14, PP-16 and PP-17 are built; PP-15 is
built for its five enumerated events and deployed, switched off pending item 3; OD-030 is
resolved; and **the DEV mailbox is approved and tested** — item 3, not an unknown, and no
longer a blocker.

Also settled, from the 2026-09-04 diagnosis: the case detail page's own three FetchXML
queries run clean against DEV and every web template it includes is present, so Liquid,
missing includes and table permissions are all ruled out for OD-035. `pac.exe` **is** present
at `%USERPROFILE%\.dotnet\tools\pac.exe` — re-confirmed 2026-09-05, and every query in this
round ran from it.
