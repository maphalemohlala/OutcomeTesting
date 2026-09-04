# Outstanding work — 2026-09-04

Written at commit `77c1ab7`, after PP-15 and PP-17 were built and deployed to `Env_AQ_Dev`.

This is a register of what is left, not a status report — `docs/2026-09-03-delivery-status.md`
and `docs/deployment/2026-09-04-pp15-drain-pp17-portal-deployment.md` cover what exists and
how it got there. Every item below names an owner, the evidence it rests on, and what "done"
looks like, so nothing here needs re-deriving.

Ordered by what it costs to leave alone, not by effort.

---

## 1. Approve the service-account mailbox

**Owner:** O365 / tenant admin. **Effort:** minutes. **Blocks:** PP-15, BR-009.

> **Done later the same day.** The mailbox was approved *and* Test & Enable was run;
> the query below now returns `Yes` / `Success` with a real
> `testmailboxaccesscompletedon`. See `docs/2026-09-04-delivery-status.md`. What
> remains of this item is registering the drain step and working the `Pending`
> backlog — the rest of this section is kept for the procedure and the reasoning.

The single highest-value action open. PP-15 and BR-009 are built, tested, deployed and
switched off, waiting on one administrative approval.

Verified on DEV 2026-09-04 by query against `mailbox`:

```
emailaddress                     : svc.automate.aq@ascotlloyd.co.uk
isemailaddressapprovedbyo365admin: No
outgoingemailstatus              : Not Run
testmailboxaccesscompletedon     : 1/1/1900 12:00 AM   (never tested)
outgoingemaildeliverymethod      : Server-Side Synchronization
```

**Done when:** an admin has approved the mailbox and run *Test & Enable*; the query above
returns `Yes` / `Success`; the drain step is registered (exact command in the deployment
doc); and the existing `Pending` backlog has been worked with `al_DrainNotifications`
starting at `MaxRows: 1`.

Two things not to skip. Re-run the query rather than trusting the admin UI. And remember the
step fires on **Create**, so the backlog already queued does not drain by itself — read
`al_failurereason` on anything that lands at `Failed` before running a larger batch.

## 2. OD-032 — `SetFailAccountability` has no `al_command` value of its own

**Owner:** whoever owns the AD-039 accountability trail. **Effort:** small, additive.

The most substantive open defect, and it is not cosmetic. The command reuses `120910788`,
which the option set labels `SetRoleAssignmentActive`. Two live consequences:

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

## 3. Close the gap between `src/` and DEV before anything is promoted

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

## 4. Name PP-15's other four events

**Owner:** Product owner. **Effort:** additive once named.

PP-15 says nine events; five are built, being the five AD-035 enumerates. The other four
appear in no requirement, knowledge file or design document (OD-030 gap (a)). Adding option
values later is additive and safe, which is why five shipped — but PP-15 is not met as
written until someone names them.

**Done when:** four events are named, with their recipients, and added to the option set and
the emitters.

## 5. OD-025 — the plug-in signing key is committed to the repository

**Owner:** Platform owner / IT security. **Aging, and the cost grows.**

`OutcomeTesting.Plugins.snk` is a full private key blob in git, and it reproduces the
`PublicKeyToken=86b764d5a2430b1f` the registration tool expects. Key Vault injection at build
time is agreed; **rotation and history removal are not scheduled**, and the Key Vault does not
close the finding while the committed key still reproduces the current token.

Be clear about the shape of the cost, because it is what keeps deferring this: rotation
changes the public key token and so requires re-registering every plug-in type in every
environment, and history removal means a force-push over commits already published to
`github.com/maphalemohlala/OutcomeTesting`. **Both get more expensive with every push** — two
more landed on 2026-09-04.

This is defence-in-depth, not a live exploit: strong-naming is not a .NET trust boundary and
abusing it needs Dataverse deployment privilege. That is a reason to schedule it, not to keep
deferring it.

## 6. OD-023 — support model: hours of cover and response targets

**Owner:** Platform owner.

Owning teams are named (AQS and Tax) and escalation is human-triggered by direction, so
**nothing fires on a timer** — which makes the hours of cover the only thing that tells a user
when to expect a response. Still unstated: hours and response targets, split between
portal-down and a single user blocked; and who the hand-off goes to when something needs a
configuration or platform change, since AQS and Tax do not hold Power Pages or Dataverse admin.

## 7. Tooling debt

**Owner:** Delivery. Each of these cost time on 2026-09-04 and will cost it again.

- **`pac pages upload` cannot be relied on to finish.** It dies on a parent-scoped
  `adx_entitypermission` (record `a1000000-…-072`) that `pac` routes down the Standard path,
  reproduced identically on 2026-09-03 and 2026-09-04. Every portal deploy therefore needs
  verifying by query afterwards. The manifest also holds a stale id
  (`5140384b-f6a3-f111-aaac-e4fade069307`) reported as a failed update on every run; a
  `pac pages download` would refresh it but rewrites the source folder, so it is a deliberate
  call rather than a tidy-up.
- **Retarget `OutcomeTesting.Registration` off `net8.0`.** The machine now carries only the
  .NET 10 runtime, so `dotnet run` fails with "You must install or update .NET to run this
  application" — which reads like a missing SDK rather than a missing runtime major. Worked
  around with `DOTNET_ROLL_FORWARD=LatestMajor`; retargeting is the real fix.
- **Register custom APIs into the solution directly.** `registerall` leaves a new API in
  `Default` only, so it will not promote. `pac solution add-solution-component -sn OutcomeTesting
  -c <id> --componentType CustomAPI` fixes it and is much lighter than the custom-API-only
  solution import the 2026-09-03 run used. Worth folding into `registerall` so it cannot be
  forgotten — `al_DrainNotifications` was caught only because it was checked.

## 8. OD-028 reads as closable

**Owner:** Delivery.

The evidence already recorded looks complete: `primaryKey="true"` moved from `al_name` to
`al_rolecode` to match the real alternate key, the orphan check was done, and
`data/roles-seed` was imported against DEV twice with 0 failures and 11 `al_role` rows before
and after. Per this log's own recording rules an OD is not moved to resolved without a named
owner and date, so it is flagged here rather than closed in passing.

**Done when:** Delivery confirms and marks it resolved with a date.

---

## Carry — known and accepted, unchanged

- **OD-029** per-team allocation scoping.
- **Senior Checker** needs `command.assign` granted as a Dataverse row.
- **Optimistic concurrency is not sent on the portal submit path.**
- **10 `react-hooks/exhaustive-deps` lint warnings**, all pre-existing, zero errors.

## What is *not* outstanding

Recorded so it is not re-investigated: PP-01 to PP-14, PP-16 and PP-17 are built; PP-15 is
built for its five enumerated events and deployed switched off; OD-030 is resolved; the DEV
mailbox question is answered (it is not approved — item 1, not an unknown).
