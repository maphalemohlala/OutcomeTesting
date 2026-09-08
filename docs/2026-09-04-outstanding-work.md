# Outstanding work

Started 2026-09-04. **Last reviewed 2026-09-07**, after the portal fixes batch, the AD-089
write-path proof, the AD-013 round trip and the sign-in repair. Every environment claim below
was re-queried that day rather than carried forward.

This is the register of what is left, not a status report. `docs/2026-09-06-delivery-status.md`
is the current one; the earlier delivery statuses and the deployment records under
`docs/deployment/` cover what exists and how it got there. Every item names an owner, the
evidence it rests on, and what "done" looks like, so nothing here needs re-deriving.

**Ordered by what it costs to leave alone, not by effort.** Re-ranked 2026-09-07: **OD-034 and
OD-035 are closed**, item 5's users-and-roles work is closed, and the four small engineering
leftovers this register carried are closed or mechanised. **Nothing below is a defect in
something already delivered.** What is left is four decisions, one blocked command, scheduled
security work, and environment set-up owned outside Delivery.

**Sequencing, by project owner direction 2026-09-06:** the other environments are set up once
everything is tested and approved in DEV. So no item here is waiting on TEST or PROD, and
promotion readiness is a state to be *ready for*, not a task in flight.

---

## 1. Four decisions, all cheap, all blocking something visible

**Owner:** Project owner. These are the only items standing between DEV and a full walk-through
by a real person.

### 1.1 Tax writes as a named person — SETTLED 2026-09-07

`AL Portal - Tax Reviewer` is granted to the `Sims Rad` contact, through `al_AssignUserRole`
and verified by reading the association back.

**The grant on its own would not have finished it.** The write scope is Contact-anchored
through `contact_al_reviewinstance`, so holding the role admits nobody to a review that is not
assigned to them — and the only Tax review was on `Dev Account`. A second fixture,
`IO-DEV-VERIFY-002`, was routed `Tax only` and allocated, so there are now two Tax reviews, one
per contact, and both sign-ins can exercise the Tax path end to end.

```
roles: AL Portal - Tax Reviewer, AL Portal - AQS Reviewer,
       AL Portal - Outcome Testing Manager, AL Portal - Portal Administrator
reviews: 11 — 9 AQS, 2 Tax (Dev Account, Sims Rad)
```

**Carry this, because it is the cost of the decision:** one contact now holds reviewer and
manager authority at once. That is what makes a one-person walk-through possible, and it is
also what would hide a role-separation defect — anything that ought to be refused to a reviewer
will be permitted to this account by its manager roles, and nothing will say which grant
allowed it. Any test of the separation itself (PP-08, the Tax/AQS boundary, AD-020's owner
filter) needs a contact holding one role, not this one.

### 1.2 The crossed external identity — now unblocked

`adx_externalidentity` binds the Entra object id of **`svc.automate.aq`** to the **`Dev Account`**
contact (`svc.automate.aq-dev`). Two different accounts. The consequence is live: the
`Service Account` contact holds `Administrators` and **is reachable by no sign-in at all**, so
the one role that reads every page belongs to nobody who can log in.

It was left alone earlier on 2026-09-07 because the Tax Reviewer path ran through that binding,
and repointing it would have removed the only way to write a Tax check. **1.1 removed that
constraint**: `Sims Rad` now holds Tax Reviewer and has a Tax review of its own, so nothing is
lost by repointing the crossed binding or by leaving `Dev Account` without one.

The remaining question is only what the *service* accounts should be able to do. Adding a
binding for the `Service Account` contact is the additive option and would make `Administrators`
reachable by a sign-in for the first time; repointing the existing one is the tidier option and
takes portal access away from `svc.automate.aq` as `Dev Account`.

**Decided 2026-09-07: repoint it** onto the `Service Account` contact, which is the contact
`svc.automate.aq` is actually named after and which holds `Administrators`. That makes the
binding truthful and makes `Administrators` reachable by a sign-in for the first time.

`bindidentity` grew a `--repoint` flag for it — a separate act from granting a binding, because
it takes a sign-in away from whoever holds it today, so it is not what happens when an "add" is
re-run. The refusal without the flag is verified:

```
e044a8e9-… is already bound to Dev Account.
Re-run with --repoint to move it, which takes that sign-in away from them.
```

**Not yet run — the call was blocked by the session's permission classifier.**

```
bindidentity <orgUrl> e044a8e9-34ac-4503-8da4-e9573ccd234b svc.automate.aq@ascotlloyd.co.uk --repoint --confirm <orgUrl>
```

**One consequence to expect, not a fault:** `Dev Account` then holds `AL Portal - Tax Reviewer`
and a Tax review on `IO-DEV-VERIFY-003` while being reachable by no sign-in. Nothing is lost —
`Sims Rad` holds Tax Reviewer and its own Tax review since 1.1, and its manager roles read every
review including that one. Reassign or leave it as a fixture; it does not need deciding now.

**Done when:** the repoint has run and `identities` shows no `NOTE` line.

### 1.3 OD-037 — delete or keep `al_User` / `al_Role`

**Corrected 2026-09-08. "Nothing reads it" is wrong, and acting on it would take the app down.**
This register and the 2026-09-06 status both described this as purely a destructive ALM change
on empty tables. Two **live** reads remain in the deployed assembly:

| Where | Reads | What a missing table does |
|---|---|---|
| `PermissionHelpers.IsRegisteredActive` | `al_user` on **every permission check** | `RetrieveMultiple` throws, so **every command fails** |
| `AssignUserRolePlugin.CustomRoleExists` | `al_role` on role assignment | throws on assign |

`al_user` being empty is exactly why this is invisible: `IsRegisteredActive` treats "no row" as
permitted, so a table with no rows and a table that is gone look identical right up until the
query itself faults. Deleting the tables first would be a self-inflicted outage on the
authorisation path.

Two further references are **dead code** and can go regardless: `CreateRolePlugin.RoleEntity`
and `UpdateRolePlugin.RoleEntity` both declare `"al_role"` while the code uses
`WebRoleRegistry.RoleEntity` (`mspp_webrole`).

**So the order is fixed, and it is not one step:**

1. Remove the two live reads, with a decision on what replaces `IsRegisteredActive` (below).
2. Rebuild and `pac plugin push` — the app must be running code that does not read the tables
   **before** they go.
3. Delete `al_User` and `al_Role`.
4. AD-013 round trip so `src/Entities/al_User` and `src/Entities/al_Role` go with them.

**The decision this needs is not "delete or keep".** It is what happens to deactivation.
`IsRegisteredActive` is the only thing `al_SetUserActive` acts on: withdrawing it removes the
ability to deactivate a person in the app at all, unless that moves to the Contact's own
`statecode`. Pick one:

- **Move it to Contact `statecode`** — the registry is Contact since AD-085, so this is where it
  belongs; `al_SetUserActive` keeps working and starts meaning something again.
- **Drop deactivation** — smaller change, but `al_SetUserActive` becomes a command that reports
  success and does nothing, which is worse than not having it.

**Done when:** the four steps above have run, or the tables are recorded as retained-and-empty
with the two live reads left in place so nobody re-raises it.

### 1.4 The unused `al_contact_al_outcomecase` intersect

OD-022 is resolved — neither N:N nor per-persona lookups; all authenticated users read all
cases. The N:N built on 2026-08-30 to test the alternative is therefore unnecessary, and
OD-022's own text says it **should be deleted** so an unused intersect does not outlive the
question it was built to answer.

Verified 2026-09-07: **the intersect holds 0 rows.** The command exists and is guarded — it
refuses unless the relationship is custom, unmanaged and its intersect is empty:

```
deleterelationship <orgUrl> al_contact_al_outcomecase --confirm <orgUrl>
```

**Status:** the run was **blocked by the session's permission classifier**, correctly — it is
an irreversible schema delete. It needs to be run with that permission granted, and
`src/Other/Relationships.xml` and `src/Other/Relationships/Contact.xml` updated in the same
change so `src/` does not drift from DEV.

**Done when:** the relationship is gone from DEV and from `src/`.

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
eight more landed across 2026-09-04 to 2026-09-07.

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

## 5. OD-011 — Code Apps production readiness, tenant availability and licensing

**Owner:** Platform owner. **Status:** partially resolved since 2026-08-26.

Code app operations are enabled on `Env_AQ_Dev` and `pa app push` succeeds there. Still open:
enabling TEST and PROD, and confirming licensing for **every persona**.

Ranked last deliberately, and not because it is unimportant: **project owner direction
2026-09-06 is that the other environments are set up once everything is tested and approved in
DEV.** So this is sequenced behind DEV sign-off rather than blocked on anything technical.

**Done when:** TEST and PROD have code app operations enabled and per-persona licensing is
confirmed.

## 6. The import side of Power Pages solution promotion

**Owner:** Platform owner. Carried over from OD-034, which is otherwise closed.

The site and its components are in the `OutcomeTesting` solution and an export carries them.
**The import side is unproven** — export from DEV, import into a second environment, confirm
the site reconstitutes — because only DEV is authenticated. And Microsoft documents Power Pages
solution awareness as a **preview feature**, "not meant for production use": that is a call for
the platform owner before it becomes the PROD promotion path, not a tooling detail.

Sequenced behind item 5 for the same reason.

---

## Standing controls — run these, they are not optional

- **`powerpages/Check-ComponentIds.ps1` before every `pac pages upload`.** Power Pages
  component ids are **one keyspace**, not one per component type: a web template and a page
  template minted on the same id are the same `powerpagecomponent` row, and one silently
  destroys the other (AD-084). This has now happened twice — `…0021` broke `/cases`, `…0022`
  broke `/case-details`. The id-band comments in source are not enforced anywhere, so this
  guard is the only thing standing between a hand-minted id and a deleted component. It
  currently passes at 237 identities.
- **`powerpages/Deploy-Portal.ps1` is the only sanctioned upload path.** Not `pac pages upload`
  directly, which either aborts partway or reconciles away every table permission the manifest
  lists and source no longer holds. The script refuses to run if either gate fails, and
  verifies by query afterwards.
- **`plugins/deploy/Pack-Schema-Solution.ps1` is the only sanctioned way to pack `src/`.** Not
  `pac solution pack --folder src` directly — that puts the committed plug-in assembly in the
  zip, and an import replaces the live assembly with whatever was last committed, at the same
  version and token (AD-062). New 2026-09-07; see "Closed on 2026-09-07" for why a one-off
  deletion could not fix this.
- **Verify a portal deployment by query afterwards.** Kept, with its reason updated: this used
  to be justified by OD-034's partial aborts. OD-034 is closed and uploads now run to
  completion, but the site still holds components `pac` does not own — table permissions are
  written by `restoretablepermissions`, not by the upload — so an exit code still is not
  evidence that a component landed.
- **Check Custom API solution membership after `registerall`.** Automatic: `registerall`
  reports any API missing from `OutcomeTesting` and prints the command that adds it.

## Carry — known and accepted, unchanged

- **OD-029** per-team allocation scoping.
- **Senior Checker** needs `command.assign` granted as a Dataverse row.
- **Optimistic concurrency is not sent on the portal submit path.**
- **9 `react-hooks/exhaustive-deps` lint warnings**, all pre-existing, zero errors.

## Closed on 2026-09-07

- **OD-034 — portal deployment has a working CLI path** (`eb52fb3`). `Deploy-Portal.ps1` does
  not work around the fault; it removes what makes it reachable. The `adx_entitypermission`
  and `adx_entitypermission_webrole` sections come out of the manifest before `pac` runs, so
  there is nothing to route down the Standard-model path and nothing to reconcile away — the
  second of which deleted 11 of 13 table permissions on 2026-09-06. First run: upload
  succeeded, 13 of 13 permissions written and verified. The diagnosis it rests on is in
  `docs/deployment/2026-09-05-portal-repairs-and-drain-enable.md`.
- **The stale `5140384b-…` manifest id went with it.** This register and the 2026-09-03 status
  both carried it as an open tidy-up needing a deliberate `pac pages download`. It is not in
  the tree — `eb52fb3` removed it as a side effect of stripping the manifest. Recorded because
  it was listed as outstanding twice after it had already gone.
- **AD-089 and AD-090 — the role conflict rule, proved.** A portal-only grant surfaces as
  unadopted, can be adopted, and once withdrawn in the app while the association is put back
  reads "withdrawn, still granted". It failed on its first run: `AssignUserRolePlugin` caught
  the duplicate-key fault from `Associate` and carried on, which the platform refuses — so
  Adopt could never have worked against the state it exists for, and no unit test could see it
  because the fake modelled the call and not the constraint. Re-run passes 5 of 5.
- **The portal fixes batch.** "Not saved – retry" and the refused-submission message were one
  cause — a Contact-anchored 403 the autosave path never read the status of. Both now name
  what is missing. The submit message's promise that "your answers are still saved" was false
  and is gone. `File Quality` is a section per team (`S-FQTAX` / `S-FQOUT`), fail reasons are
  emitted from the record rather than revealed by a script that only runs for an editable
  review, and every "Restrict read" rule gained the `Administrators` role.
- **The sign-in reaches the right contact.** The 2026-09-07 addendum had the conclusion right
  and the account wrong: the bound object id is `svc.automate.aq`'s, not `svc.automate.aq-dev`'s,
  and `Simunye.Radingwana@ascotlloyd.co.uk`'s own object id had **no binding at all** — the
  actual root cause. Two of the three ways out that addendum listed were unavailable
  (`LocalLoginEnabled` and `OpenRegistrationEnabled` are both `false`). A fourth option was
  taken instead: bind the person's own object id to their own contact, additive, so no service
  account impersonates a named person. `AL Portal - Portal Administrator` and
  `AL Portal - Outcome Testing Manager` now belong to a contact. See
  `docs/deployment/2026-09-07-identity-binding-and-tax-routing.md`.
- **There is Tax data in DEV.** The recorded reason there was none — "nothing has been routed
  to Tax" — was weaker than the fact: **all 13 cases carried the `AQS only` route**, so no case
  could ever have produced a Tax review. One fixture now carries `Tax only` and holds a Tax
  review; both Tax-owned sections (`S-TAX`, `S-FQTAX`) render on it.
- **The `annotationid` question is settled, and the answer is "leave them".** The removal was
  made and then **reverted the same day, on evidence.** The reasoning for removing them was
  sound as far as it went: no `annotation` row exists for any web file on this site — checked
  for all nine ids and by filename — because the content lives in a file column on
  `powerpagecomponent` under the enhanced data model. So all nine ids point at nothing.
  **`pac` needs the key anyway.** With it gone the upload does not merely warn, it dies:

  ```
  Record skipped: missing primary key 'annotationid' for entity 'annotation'
  Sorry, the app encountered a non-recoverable error … System.InvalidCastException
  ```

  `pac` reads a `.webfile.yml` as declaring an `annotation` record as well as an
  `adx_webfile` — `filename`, `mimetype`, `isdocument`, `objectid` and `objecttypecode` are
  all annotation columns — and it needs a primary key for it whether or not the row exists.
  So the `FAILED … Does Not Exist` line is the *cheaper* of the two failure modes, and the
  right treatment for a familiar error hiding a real one is not to delete the key.

  The crash happens while loading the manifest, before any component is uploaded. Verified
  afterwards: 13 of 13 table permissions still deployed, `Check-ComponentIds` clean at 237,
  portal security assertions all pass. Nothing was damaged, which is the one useful thing
  about failing that early.
- **AD-062 is mechanised rather than closed, and the distinction matters.** The decision said
  the durable fix was "removing `src/PluginAssemblies/` entirely". It cannot be, on its own:
  **the AD-013 round trip is what puts the DLL back**, by construction, every time `src/` is
  refreshed from an export. So a one-off deletion fixes it until the next round trip and then
  silently stops. `plugins/deploy/Pack-Schema-Solution.ps1` strips the folder from a staged
  copy on every pack and then **reads the zip back** to confirm no assembly is in it, deleting
  the output if one is. Verified: 209 entries, 0 DLLs, `pac` reporting the assembly root
  component as "not defined in customizations" beside the long-standing `CanvasApps` line.
  **What is still owed if full removal is wanted:** dropping `RootComponent type="91"` from
  `src/Other/Solution.xml` interacts with the 14 `type="92"` SDK step components that bind to
  plug-in types in that assembly, so it is a promotion-architecture decision, not a tidy-up.

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
  the 31 the assembly builds**. Re-run 2026-09-07 at 25 Custom APIs. See
  `docs/deployment/2026-09-06-ad013-round-trip.md`.
- **A quiet drift the round trip found and corrected:** 30 custom API parameter and response
  files carried `<name>al_AssignCase.Reason</name>`-style qualified names where DEV holds the
  bare name. `uniquename` was already right, so nothing was broken — but it is precisely the
  hand-authored-versus-emitted divergence AD-013 exists to remove.

## Closed on 2026-09-05

- **OD-035 — the `case-details` page renders.** Both web pages were repointed at `…002b` and
  verified. Correction to the diagnosis: the pages were **not** carrying a null page template
  — both pointed at `…0022`, the colliding id. A lookup aimed at the wrong component type
  projects as blank, which is what read as null.
- **The registration tool's runtime trap.** It targets `net8.0` against a machine carrying
  only the .NET 10 runtime and failed to launch with a message that reads like a missing SDK.
  `<RollForward>LatestMajor</RollForward>` is now in the csproj.
- **Custom APIs silently staying out of the solution.** `registerall` now reports membership
  rather than leaving it to be noticed by hand.
- **PP-15 is proved, not just switched on.** An allocation raised in DEV travelled emitter
  → outbox row → asynchronous drain → server-side email → **delivered**, twice. Reproduce with
  `provepp15`; read the standing evidence with `pp15evidence`.
- **OD-033 — both halves.** `Administrators` had its flag cleared; **`Checker` was deleted** on
  project owner direction, after `deletewebrole` confirmed no site component referenced it.
- **OD-032 — `SetFailAccountability` has its own value.** `al_command` now carries `120910792`
  and the cut-over date is **2026-09-05**; no data was touched.
- **OD-028.** Confirmed and marked resolved by Delivery, 2026-09-05.

## What is *not* outstanding

Recorded so it is not re-investigated: PP-01 to PP-14, PP-16 and PP-17 are built; **PP-15 is
built, deployed, switched on and proved end to end** for its five enumerated events — only
the other four events (item 2) are outstanding, and they are unnamed rather than unbuilt.
OD-030 is resolved; **the DEV mailbox is approved and tested**; and **DEV sends from the
approved account** `svc.automate.aq@ascotlloyd.co.uk` (project owner direction 2026-09-05).

Settled 2026-09-05: `Checker` is gone and `Administrators` reads `No`, so the
authenticated-users flag is no longer drift anywhere. `SetFailAccountability` still has **no
caller in the app at all**, which is why OD-032's replay collision was never reachable.

Settled 2026-09-07, and each was checked rather than assumed before the sign-in fault was
diagnosed: the contact-to-role association, the associated component's type and state, every
web role's website, the AQS page's access rule and the cascading Home rule, every page's
publishing state, all 13 table permissions and every `Webapi/*` site setting. **None of them is
at fault** — the empty `mspp_entitypermission_webrole` and `mspp_webpageaccesscontrolrule_webrole`
intersects included, which look exactly like a failed deployment and are not: on the enhanced
data model the roles live inside the component's `content` JSON.

`pac.exe` is at `%USERPROFILE%\.dotnet\tools\pac.exe` and `dotnet` at
`C:\Program Files\dotnet\dotnet.exe`; neither is on `PATH` in a non-interactive shell, which
reads as "not installed" and is not.
