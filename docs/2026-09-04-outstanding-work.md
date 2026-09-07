# Outstanding work

Started 2026-09-04 at commit `77c1ab7`. **Last reviewed 2026-09-06**, after the users, roles
and exports work — which closed most of item 5, corrected three latent defects and put the
app onto Contact and the Power Pages web roles. Every environment claim below was re-queried
that day rather than carried forward.

This is the register of what is left, not a status report. `docs/2026-09-06-delivery-status.md`
is the current one; `docs/2026-09-03-delivery-status.md` through `docs/2026-09-05-delivery-status.md` and
the deployment records under `docs/deployment/` cover what exists and how it got there. Every item names an owner, the evidence it rests on,
and what "done" looks like, so nothing here needs re-deriving.

**Ordered by what it costs to leave alone, not by effort.** Re-ranked 2026-09-06, after the
users and roles work reduced item 5 to two pieces, both since delivered (AD-089, AD-090) —
item 5 is now closed. **Nothing below is a defect in something already delivered**, and only
one item is an engineering problem at all — OD-034; the rest are decisions, scheduled security
work and environment set-up owned outside Delivery.

**Sequencing, by project owner direction 2026-09-06:** the other environments are set up once
everything is tested and approved in DEV. So no item here is waiting on TEST or PROD, and
promotion readiness is a state to be *ready for*, not a task in flight.

---

## 1. OD-034 — portal deployment has no working CLI path

**Owner:** Delivery. **Status:** OD-035 is CLOSED. OD-034 unsolved.

> **OD-035 closed 2026-09-05.** Both `case-details` web pages were repointed at `…002b` and
> verified; the page renders. Correction to the diagnosis: the pages were **not** carrying a
> null page template — both pointed at `…0022`, the colliding id, which is now the
> `OT Outcome Label` web template. A lookup aimed at the wrong component type projects as
> blank, which is what read as null. See
> `docs/deployment/2026-09-05-portal-repairs-and-drain-enable.md`.

**OD-035.** Both `case-details` web pages (`…032` root, `…042` content) carry a **null** page
template in DEV, so the page returns the generic Power Pages error. The page template
`…002b` exists and is correctly bound to the `OT Case Detail` web template (`…0015`), and
source declares `…002b` on both pages. Re-verified 2026-09-05: page template correct, both
web pages still null. It is a two-row fix.

Repair command: `repointwebpage <orgUrl> case-details "OT Case Detail Page"`.

**OD-034 is why OD-035 is stuck.** `pac pages upload` aborts at ~44 % on a single
`adx_entitypermission` record (`a1000000-…-072`) that is **already correct** in DEV — the site
is on the enhanced data model, `adx_entitypermission` does not exist in the environment, and
`pac` routes that one record down the Standard path regardless of `-mv Enhanced`. Four
attempts now — three on 2026-09-04 (delta twice, `--forceUploadAll` once) and one on
2026-09-05 — all aborting on the same record.

**Corrected 2026-09-05:** the 09-04 note said the abort lands *before web pages are
processed*, so no web page change could deploy at all. That is wrong. The 09-05 run reached
86.4 % (19 of 22 events) and **did** write the web pages — both `case-details` rows carry
that run's timestamp. What never lands is the parent-scoped permission itself and whatever
is ordered after it, and which components those are depends on what happens to be dirty.
The damage is unpredictability, not a blanket block.

`pac pages upload` has no scoping flag — only `--path`, `--deploymentProfile`,
`--forceUploadAll`, `--modelVersion` — so the record cannot be skipped. Removing it from
source was considered and rejected: `pac` deletes components absent from source.

> **2026-09-05: root cause found, and the fix is proven on the export side.** `pac` routes
> **parent-scoped** table permissions down the Standard-model path on an enhanced-data-model
> site. Only two permissions are parent-scoped and the run aborts on the first. Upgrading pac
> is not available (2.11.2 is the latest published version) and the upload has no scoping flag.
>
> The site and its **250 components** are now in the `OutcomeTesting` solution, and an export
> carries all of them — including the parent-scoped permission that breaks the upload, intact
> with its scope, parent and role links. `addsitetosolution` does this; `pac` cannot, because
> 2.11.2 rejects the Power Pages component types by name *and* by number.

**Done when:** the **import** side is proven — export from DEV, import into a second
environment, and confirm the site reconstitutes. That is untested here because only DEV is
authenticated. And a decision is taken on Microsoft documenting Power Pages solution
awareness as a **preview feature**, "not meant for production use": that is a call for the
platform owner before it becomes the PROD promotion path, not a tooling detail.

## 2. Name PP-15's other four events

**Owner:** Product owner. **Effort:** additive once named.

PP-15 says nine events; five are built, being the five AD-035 enumerates. The other four
appear in no requirement, knowledge file or design document (OD-030 gap (a)). Adding option
values later is additive and safe, which is why five shipped — but PP-15 is not met as
written until someone names them.

**Done when:** four events are named, with their recipients, and added to the option set and
the emitters.

## 3. OD-025 — the plug-in signing key is committed to the repository

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

## 4. OD-023 — support model: hours of cover and response targets

**Owner:** Platform owner.

Owning teams are named (AQS and Tax) and escalation is human-triggered by direction, so
**nothing fires on a timer** — which makes the hours of cover the only thing that tells a user
when to expect a response. Still unstated: hours and response targets, split between
portal-down and a single user blocked; and who the hand-off goes to when something needs a
configuration or platform change, since AQS and Tax do not hold Power Pages or Dataverse admin.

## 5. Users and roles rework — delivered

**Owner:** Delivery. **Status: delivered.** Roles and users landed 2026-09-06
(`docs/2026-09-06-delivery-status.md`, AD-085 to AD-088); the two pieces this section used to
carry as owed are delivered too, settled by AD-089 and AD-090 and deployed to `Env_AQ_Dev`.

Delivered: `al_User` is retired in favour of Contact (AD-085), People and Users are one
directory (AD-086), and roles are the Power Pages web roles — read live, assignable and
manageable in the app, with permissions configurable per role (AD-087).

**The first blocker resolved itself on inspection.** Retiring `al_User` was thought to collide
with `al_AssignCase` needing both a `systemuser` and a Contact. It does not: all three DEV
contacts have enabled Read-Write systemusers, so both halves resolve. `al_User` was never the
source of either. The directory did shrink to 3, exactly as predicted — those three are the
only real people in the environment.

Both items this section used to list as still owed are now delivered:

- **A role detail view.** The direction included "selecting a role shows its permissions,
  details and assignees". Built: pick a role, see its description, its rules by resource and
  level, and the people holding it (`al_GetRoleHolders`, `roleDetailPath`), with the existing
  edit and withdraw actions in place, plus a Status column showing where the two sources
  (mapping and association) agree or disagree.
- **A conflict rule for roles managed in two places**, plus a rule for `Authenticated Users`.
  Settled by AD-089 and AD-090. The app writes role assignment through an audited Custom API
  (AD-041, BR-012) *and* associates the contact, so an assignment made in Power Pages
  management used to write the association with no audit event and no mirror row, and the
  client used to disagree with the server about what that meant — `PermissionProvider` now
  asks `al_GetMyRoles` rather than re-deriving the answer from a mapping-table read (AD-089).
  A portal-side assignment grants access and is visible immediately, surfaced on the role
  detail screen and counted on the Security configuration page, and is not authoritative
  until an administrator adopts or revokes it through `al_AdoptRoleAssignment` — each decision
  audited, never a schedule. `Authenticated Users`, and any role sharing its
  `mspp_authenticatedusersrole` flag, is excluded from role resolution server-side (AD-090),
  closing the drift OD-033 found in DEV rather than only confirming the by-name exclusion
  AD-087 already had.

## 6. OD-011 — Code Apps production readiness, tenant availability and licensing

**Owner:** Platform owner. **Status:** partially resolved since 2026-08-26.

Code app operations are enabled on `Env_AQ_Dev` and `pa app push` succeeds there. Still open:
enabling TEST and PROD, and confirming licensing for **every persona**. It was deferred "until
the app is ready to promote", and it previously rode along under the `src/` gap item — it now
stands on its own, because that gap is closed and this is what is left between a working DEV
and a second environment.

Ranked last deliberately, and not because it is unimportant: **project owner direction
2026-09-06 is that the other environments are set up once everything is tested and approved in
DEV.** So this is sequenced behind DEV sign-off rather than blocked on anything technical.

**Done when:** TEST and PROD have code app operations enabled and per-persona licensing is
confirmed.

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

## Closed on 2026-09-06

- **The app's user registry is Contact, and People and Users are one screen** (AD-085,
  AD-086). `al_user` held ten fictional `@example.com` rows against three real contacts, with
  **no overlap at any row**; it now holds none. Safe because authorisation never read it —
  the environment's only role mapping was for an email that had no `al_user` row at all.
- **Roles come from the Power Pages web roles** (AD-087), read live, assignable and
  manageable in the app, with 52 permission rules seeded across the eight business roles. No
  schema changed: web role names travel through the `al_rolecode` column AD-044 already
  reserved for custom roles.
- **Every allocation had been broken since it was written.** `AssignCasePlugin` filtered
  `systemuser` on `internalemailid`, which is not an attribute, so Dataverse rejected the
  query and both the `al_AssignCase` command and the portal claim path failed for every
  caller. Invisible because no case had ever been allocated in DEV, and because the unit test
  seeded the same wrong column — the fake has no metadata to contradict it, so the test
  agreed with the bug. Fixed, with a regression case seeding the real shape.
- **The export Download control was never defective.** No case was `Closed`, so the batch was
  legitimately empty; the app simply never said so. Three cases were driven to `Closed` and
  the export now yields three rows with both graded columns populated.
- **`src/` is the source of truth again.** The AD-013 export-and-replace round trip ran
  against DEV: `src/Entities/al_Notification/` now exists, `src/customapis/` holds 22 APIs
  (adding `al_DrainNotifications`), `src/SdkMessageProcessingSteps/` holds 14 (adding the PP-15
  drain step and three emitter steps), and the plug-in manifest declares **31 types, matching
  the 31 the assembly builds**. `pac solution pack --folder src` succeeds with only the
  expected `CanvasApps` warning (AD-012). See
  `docs/deployment/2026-09-06-ad013-round-trip.md`.
- **A quiet drift the round trip found and corrected:** 30 custom API parameter and response
  files carried `<name>al_AssignCase.Reason</name>`-style qualified names where DEV holds the
  bare name. `uniquename` was already right, so nothing was broken — but it is precisely the
  hand-authored-versus-emitted divergence AD-013 exists to remove.

## Closed on 2026-09-05

- **The registration tool's runtime trap.** It targets `net8.0` against a machine carrying
  only the .NET 10 runtime and failed to launch with a message that reads like a missing SDK.
  `<RollForward>LatestMajor</RollForward>` is now in the csproj; the exe launches with no
  environment variable. The tested target framework is unchanged deliberately.
- **Custom APIs silently staying out of the solution.** `registerall` now reports membership
  rather than leaving it to be noticed by hand.
- **PP-15 is proved, not just switched on.** An allocation raised in DEV travelled emitter
  → outbox row → asynchronous drain → server-side email → **delivered**, twice. Two
  `al_notification` rows sit at `Sent`, each with an Outgoing/`Sent` email activity and an
  Incoming/`Received` copy of the same message tracked back into the service mailbox. The
  `MaxRows: 1` backlog procedure this register carried is now moot: there was never a backlog,
  and there is not one now. Reproduce with `provepp15`; read the standing evidence with
  `pp15evidence`.
- **OD-033 — both halves.** `Administrators` had its flag cleared in the morning;
  **`Checker` was deleted** on project owner direction, after `deletewebrole` confirmed no
  site component referenced it. The pipeline could never have removed it, which is exactly
  why it needed a decision rather than another upload.
- **OD-032 — `SetFailAccountability` has its own value.** `al_command` now carries
  `120910792`, the plug-in writes it, and the cut-over date is **2026-09-05**: pre-cut-over
  rows on `120910788` are identified by `al_name` and `al_targettable`, and no data was
  touched. The `(key, command)` replay scope is restored.
- **OD-028.** Confirmed and marked resolved by Delivery, 2026-09-05. Evidence unchanged since
  2026-09-03; it needed a name and a date, which it now has.

## What is *not* outstanding

Recorded so it is not re-investigated: PP-01 to PP-14, PP-16 and PP-17 are built; **PP-15 is
built, deployed, switched on and proved end to end** for its five enumerated events — only
the other four events (item 3) are outstanding, and they are unnamed rather than unbuilt.
OD-030 is resolved; **the DEV mailbox is approved and tested**; and **DEV sends from the
approved account** `svc.automate.aq@ascotlloyd.co.uk` (project owner direction 2026-09-05),
so the `-dev` mailbox at `Pending Approval` is not a question either.

Also settled 2026-09-05: `Checker` is gone and `Administrators` reads `No`, so the
authenticated-users flag is no longer drift anywhere — `Authenticated Users` is the only role
carrying it, which is what `webrole.yml` declares. `SetFailAccountability` still has **no
caller in the app at all**, which is why OD-032's replay collision was never reachable; the
`(key, command)` scope is restored anyway, because doing it before a caller exists is the
cheap version.

Also settled, from the 2026-09-04 diagnosis: the case detail page's own three FetchXML
queries run clean against DEV and every web template it includes is present, so Liquid,
missing includes and table permissions are all ruled out for OD-035. `pac.exe` **is** present
at `%USERPROFILE%\.dotnet\tools\pac.exe` — re-confirmed 2026-09-05, and every query in this
round ran from it.
