# Outstanding work

Started 2026-09-04 at commit `77c1ab7`. **Last reviewed 2026-09-05**, after the case detail
incident, the DEV verification pass, and both deployments that day — the portal repairs and
drain switch-on, then the PP-15 proof run that closed OD-032, OD-033 and OD-028 with it.
Every environment claim below was re-queried that day rather than carried forward.

This is the register of what is left, not a status report. `docs/2026-09-05-delivery-status.md`
is the current one; `docs/2026-09-03-delivery-status.md`, `docs/2026-09-04-delivery-status.md` and
the deployment records under `docs/deployment/` cover what exists and how it got there. Every item names an owner, the evidence it rests on,
and what "done" looks like, so nothing here needs re-deriving.

**Ordered by what it costs to leave alone, not by effort.** Re-ranked on 2026-09-05 after the
evening deployment closed four items. What is left is genuinely different in kind from what
went: **nothing below is a defect in something already delivered.** One is a tooling gap
(OD-034), one is an ALM debt that blocks promotion, and the rest are decisions and scheduled
work owned outside Delivery. The register is shorter and slower-moving than it has been.

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

## 2. Close the gap between `src/` and DEV before anything is promoted

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

## 3. Name PP-15's other four events

**Owner:** Product owner. **Effort:** additive once named.

PP-15 says nine events; five are built, being the five AD-035 enumerates. The other four
appear in no requirement, knowledge file or design document (OD-030 gap (a)). Adding option
values later is additive and safe, which is why five shipped — but PP-15 is not met as
written until someone names them.

**Done when:** four events are named, with their recipients, and added to the option set and
the emitters.

## 4. OD-025 — the plug-in signing key is committed to the repository

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

## 5. OD-023 — support model: hours of cover and response targets

**Owner:** Platform owner.

Owning teams are named (AQS and Tax) and escalation is human-triggered by direction, so
**nothing fires on a timer** — which makes the hours of cover the only thing that tells a user
when to expect a response. Still unstated: hours and response targets, split between
portal-down and a single user blocked; and who the hand-off goes to when something needs a
configuration or platform change, since AQS and Tax do not hold Power Pages or Dataverse admin.

## 6. Users and roles rework

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
  `Authenticated Users`, which is auto-granted and so has no per-person membership at all.
  Narrower than it was on 2026-09-04: `Administrators` no longer carries the flag and
  `Checker` no longer exists (OD-033), so `Authenticated Users` is the only case left — but
  it is the stock role, so the rule is still owed.

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
