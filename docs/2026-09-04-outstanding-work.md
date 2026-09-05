# Outstanding work

Started 2026-09-04 at commit `77c1ab7`. **Last reviewed 2026-09-05**, after the case detail
incident and the DEV verification pass — every environment claim below was re-queried that
day rather than carried forward.

This is the register of what is left, not a status report. `docs/2026-09-03-delivery-status.md`,
`docs/2026-09-04-delivery-status.md` and `docs/deployment/2026-09-04-pp15-drain-pp17-portal-deployment.md`
cover what exists and how it got there. Every item names an owner, the evidence it rests on,
and what "done" looks like, so nothing here needs re-deriving.

**Ordered by what it costs to leave alone, not by effort.** Re-ranked on 2026-09-05 after a
verification pass: `/case-details` is broken for users and moved to the top, the drain step
became fully unblocked, and OD-033 moved *down* — it was ranked first on the assumption
that the authenticated-users flag conferred privilege, and the query evidence says it
confers nothing.

---

## 1. OD-035 and OD-034 — `/case-details` is broken and cannot be deployed

**Owner:** Delivery. **Status:** OD-035 fix written, not yet applied. OD-034 unsolved.

The only item on this list that users are hitting right now.

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

## 2. Register the PP-15 drain step

**Owner:** Delivery. **Status:** fully unblocked as of 2026-09-05. **Delivers:** PP-15, BR-009.

> **Both preconditions are now closed.** The mailbox is approved *and* tested — re-verified
> 2026-09-05: `isemailaddressapprovedbyo365admin = Yes`, `outgoingemailstatus = Success`,
> `testmailboxaccesscompletedon = 9/4/2026 7:31 AM`. And the sending identity is settled by
> project owner direction 2026-09-05: **DEV sends from the approved account**,
> `svc.automate.aq@ascotlloyd.co.uk`. The `-dev` mailbox at `Pending Approval` is not used.

Worth keeping, because it nearly went wrong: immediately after approval the mailbox read
`Yes` / **`Not Run`**. Approval records permission, it does not test anything. Registering the
step in that window would have stamped the whole `Pending` backlog `Failed`, which is the
outcome the gate exists to prevent. The gate requires `Yes` **and** `Success`.

**Done when:** the step is registered (command in the deployment doc) and the existing
`Pending` backlog is worked with `al_DrainNotifications` starting at `MaxRows: 1`, reading
`al_failurereason` on anything that lands at `Failed` before running a larger batch. The step
fires on **Create**, so the backlog does not drain by itself.

Note `al_DrainNotifications` is already live and callable (Manage on `permission.manage`), and
now that the mailbox is good, invoking it sends real email.

## 3. OD-033 — undeclared portal roles carrying the authenticated-users flag

**Owner:** Delivery, plus whoever created `Checker`. **Effort:** one field, then one decision.

**Corrected 2026-09-05 — this is drift, not a live over-grant.** It was previously ranked
first here on the assumption that the flag conferred privilege. It does not, and the
correction matters because it changes the urgency: verified by query, **both** roles have
**zero table permissions and zero web page access control rules** bound to them, and
`mspp_webrole` on this site carries no website-manager flag at all. Every authenticated
contact does hold both roles — and holding them currently grants nothing.

- **`Administrators`** has `mspp_authenticatedusersrole = Yes` in DEV; `webrole.yml` declares
  `false`. Two uploads have left it set, so it needs a deliberate correction rather than
  another upload. Repair command: `setwebroleauth <orgUrl> "Administrators" false`.
- **`Checker`** (created 2026-09-03 07:19 by the service account) appears in **no source file
  and in no pac manifest**. That second half is why an upload will never remove it: `pac`
  deletes components it has tracked and is now missing from source, and it has never tracked
  this one. It is invisible to the deployment pipeline in both directions.

**Why it still matters while granting nothing:** both are auto-granted to every authenticated
user, so the moment anyone attaches a table permission to either — in the maker portal, where
`Checker` sits with an inviting name — every authenticated contact silently receives it. This
is a loaded gun rather than a live wound, and it is cheap to unload now.

**Done when:** the flag is cleared on `Administrators`, and `Checker` is either declared in
source or deleted. The first is mechanical. The second needs intent from whoever created it
on 2026-09-03 — the same day AD-076 self-claim was switched on, so it may be an abandoned
step of that work.

## 4. OD-032 — `SetFailAccountability` has no `al_command` value of its own

**Owner:** whoever owns the AD-039 accountability trail. **Effort:** small, additive.

`SetFailAccountabilityPlugin` writes `al_command = 120910788`, the value the option set
labels `SetRoleAssignmentActive`. Two consequences, and 2026-09-05 evidence separates them:

- **The mislabelling is real and already in the data.** DEV holds five audit rows on
  `120910788`, all created 2026-08-30, and all five are genuinely `SetFailAccountability`
  writes against `al_outcome`. Anything reading the accountability trail by its choice column
  reports them as role changes. Under NFR-AUD-01 these rows are immutable, so the count only
  grows while this stands.
- **The replay-protection loss is latent, not live.** `CommandHelpers.FindAuditByKey` scopes
  a lookup to `(idempotency key, command)` precisely so one command's key cannot replay
  against another, and two commands sharing a value removes that between them. But a
  collision needs the same key sent to both commands, and keys are `crypto.randomUUID()`
  minted per intent (`intentKey.ts`) — and **nothing in the app calls `SetFailAccountability`
  at all**. There is no path to it today. It is a removed safety net, which is worth
  restoring before someone adds a caller, not an active defect.

**The fix is easy; what needs deciding is the history.** Minting a value and repointing the
plug-in is additive. What the five existing immutable rows then *mean* is the actual
question — and the good news is that they are not ambiguous in practice: each carries
`al_name = "SetFailAccountability"` and `al_targettable = "al_outcome"`, so they can be
identified exactly. A documented cut-over date is enough; no data surgery is required.

**Done when:** the trail owner has agreed how pre-fix rows are to be read, a new option value
is minted, and `SetFailAccountabilityPlugin` points at it.

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
built for its five enumerated events and deployed, switched off pending item 2; OD-030 is
resolved; **the DEV mailbox is approved and tested**; and **DEV sends from the approved
account** `svc.automate.aq@ascotlloyd.co.uk` (project owner direction 2026-09-05), so the
`-dev` mailbox at `Pending Approval` is not a question either.

Also settled 2026-09-05, and the reason OD-033 was re-ranked: neither `Administrators` nor
`Checker` has a single table permission or web page access control rule bound to it, and
`mspp_webrole` on this site has no website-manager flag. The authenticated-users flag is
therefore drift, not a privilege grant — do not re-raise it as a live over-grant. And
`SetFailAccountability` has **no caller in the app at all**, with keys minted as random
UUIDs per intent, so OD-032's replay-collision has no reachable path today.

Also settled, from the 2026-09-04 diagnosis: the case detail page's own three FetchXML
queries run clean against DEV and every web template it includes is present, so Liquid,
missing includes and table permissions are all ruled out for OD-035. `pac.exe` **is** present
at `%USERPROFILE%\.dotnet\tools\pac.exe` — re-confirmed 2026-09-05, and every query in this
round ran from it.
