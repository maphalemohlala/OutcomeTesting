# Deployment — PP-15 proved end to end, OD-032 and OD-033 closed, 2026-09-05

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. Second deployment of the day; it follows
`2026-09-05-portal-repairs-and-drain-enable.md`, which registered the drain step but could
not prove it.

Four writes, each verified by re-query afterwards rather than taken from the command's own
exit code.

---

## 1. PP-15 proved end to end — the first notifications this environment has produced

The morning's deployment left PP-15 **switched on but unproven**: the drain step was
registered, the mailbox was approved and tested, and `al_notification` held **zero rows**.
Every piece of the path had been verified alone and none of it together, so nothing had
travelled emitter → outbox → asynchronous drain → server-side email. That is what this
closes.

```
provepp15 https://org0b075da8.crm11.dynamics.com --confirm <same url>
```

The command causes one qualifying event — the emitter fires on Create of
`al_caseassignment` — and follows it through every hop. It ran twice, once before and once
after a correction to its own last check (§1.2). Both runs are equally valid evidence; the
second is quoted.

```
  [PASS] emitter step registered on Create of al_caseassignment: stage 40, synchronous, enabled
  [PASS] drain step registered, asynchronous, enabled: stage 40, asynchronous, enabled,
         runs as 3c6f441a-2fa1-f111-b8dd-e4fade069307
  [PASS] sending account has a work email: svc.automate.aq@ascotlloyd.co.uk
  [PASS] mailbox approved by the O365 admin: Yes
  [PASS] mailbox outgoing test succeeded: Success
  seeded al_outcomecase 366523be-… (PP15-PROOF-20260905205253)
  created al_caseassignment 386523be-…            <- the qualifying event
  [PASS] emitter wrote an outbox row: ALLOCATION-386523BE6BA9F111AAACE4FADE069307
  [PASS] queued to the allocated person: svc.automate.aq@ascotlloyd.co.uk
  [PASS] event is Allocation
  [PASS] outbox row reached Sent
  [PASS] send timestamped: 2026-09-05 20:53:17Z
  [PASS] email activity created: 1ba6edce-…
PROVE PP-15: PASS
```

**Delivery, not just dispatch.** `Sent` on an `al_notification` row means the drain handed
the message to Dataverse, which is the last thing the drain can see. `pp15evidence` reads the
layer past that, and it is where the proof actually completes:

```
  ALLOCATION-316FE66C…  Sent, queued 20:50:36Z, sent 20:51:02Z
    email 3018dcb1-…: Incoming, Received, created 20:52:25Z
    email 234f2e7a-…: Outgoing, Sent,     created 20:50:56Z

  ALLOCATION-386523BE…  Sent, queued 20:52:54Z, sent 20:53:17Z
    email 90f9d1dd-…: Incoming, Received, created 20:53:39Z
    email 1ba6edce-…: Outgoing, Sent,     created 20:53:13Z
```

Each send has an **Outgoing/Sent** activity and an **Incoming/Received** copy of the same
message, tracked back into the service mailbox by server-side synchronisation. The second is
what makes this a delivery proof rather than a dispatch proof: the message left the mailbox
and arrived.

### 1.1 What the run wrote, and what it left behind

It writes real business records, which is why it takes `--confirm` with the org URL repeated
— the same discipline the `verify` modes use, for the same reason. The blast radius is held
to rows the command created:

- it **seeds its own `al_outcomecase`** (`PP15-PROOF-<stamp>`) rather than allocating a case
  someone is working on;
- it **allocates to the account the drain runs as**, so the email lands in the service
  mailbox and not a colleague's inbox;
- it **deletes the assignment and the case** afterwards.

The two `al_notification` rows are left in place deliberately. They are the evidence, and an
outbox row whose target no longer exists is exactly what a proof run should leave. Note this
also means the environment's outbox is no longer empty: two rows, both `Sent`, neither
representing work anybody has to do.

### 1.2 One correction, from the first run

The first run reported `[FAIL] email activity created: none found` while every other check
passed. The cause is worth carrying: **Dataverse appends a tracking token to the subject it
stores** — `Case PP15-PROOF-… has been allocated to you CRM:0249002` — so an exact match on
the subject the outbox recorded finds nothing, for ever. The email had been created and sent
the whole time.

The lookup now matches on prefix and prints the subject it found, so a genuine subject
mismatch is still a finding rather than being collapsed into a pass. Recorded because the
failure mode is a bad one: a verification that reads "no email was sent" when the email was
sent is worse than no verification at all.

### 1.3 Two new commands

| Command | Writes? | What it is for |
|---|---|---|
| `provepp15 <orgUrl> --confirm <orgUrl>` | Yes | Causes one allocation and follows it through every hop. Checks the emitter step, the drain step and the mailbox **before** writing anything, so a switched-off path is reported without leaving business rows behind to explain. |
| `pp15evidence <orgUrl>` | No | Prints the whole outbox and, for each row, every email carrying its subject with direction and status. This is the standing check, and the one to run before believing the queue is healthy. |

## 2. OD-032 closed — `SetFailAccountability` has its own `al_command` value

```
addcommandvalue https://org0b075da8.crm11.dynamics.com 120910792 SetFailAccountability
  al_command 120910792 = 'SetFailAccountability' inserted and published (24 values).
```

Read back from metadata after the publish rather than trusted from the insert, which reports
success on the definition it just changed.

`SetFailAccountabilityPlugin` now writes `120910792`; the assembly was rebuilt and pushed
(`pac plugin push --type Assembly`), and 301 plug-in tests pass. `120910792` is added to
`src/Entities/al_AuditEvent/Entity.xml`, so a pack of `src/` carries it.

**The decision, which was always the hard half.** The cut-over is **2026-09-05**. Audit rows
on `120910788` created **before** that date are `SetFailAccountability` writes when they carry
`al_name = "SetFailAccountability"` and `al_targettable = "al_outcome"`, and
`SetRoleAssignmentActive` otherwise; on and after it, `120910788` means
`SetRoleAssignmentActive` and nothing else. The five known rows (all created 2026-08-30) fall
under that rule exactly. **No data surgery was done and none was needed** — NFR-AUD-01 makes
those rows immutable, and the fix never proposed to touch them.

The replay-protection half closes with the same change: `CommandHelpers.FindAuditByKey` scopes
on `(idempotency key, command)` again. Restored before a caller exists rather than after,
which is the only cheap moment to do it.

**Deliberately not updated:** `app/src/generated/models/Al_auditeventsModel.ts` and
`app/.power/schemas/dataverse/auditevents.Schema.json`. Both are autogenerated, both already
lag DEV by a value (neither carries `120910791` either), and they are refreshed by
regenerating from Dataverse rather than by hand. Nothing in the app calls this command.

## 3. OD-033 closed — the `Checker` web role is deleted

```
deletewebrole https://org0b075da8.crm11.dynamics.com Checker --confirm <same url>
  Web role 'Checker' (0931e8d2-67a7-f111-aaac-e4fade069307):
    authenticatedusersrole=True, anonymoususersrole=False
  no site component references it - nothing is bound to this role.
  Deleted. No web role named 'Checker' remains.
```

Project owner direction, taken on the evidence: it granted nothing, it was declared nowhere,
and declaring it in source would have meant keeping a role nobody had claimed. With
`Administrators` corrected this morning, both halves of OD-033 are now closed and
`Authenticated Users` correctly keeps the flag, which is what `webrole.yml` declares.

**The guard is the part worth keeping.** `deletewebrole` refuses to delete a role anything is
bound to. Web role bindings in the enhanced data model live inside the `content` JSON of
`powerpagecomponent` rows rather than in link tables, so it scans every component on the site
for the role id. An empty role and a role that grants something are indistinguishable from
the role row alone, and only one of them is safe to remove.

**This did not need `pac pages upload`, and could not have used it.** The role existed only in
the environment and in no manifest, so the pipeline could not see it in either direction —
which is the whole reason this half stayed open while `Administrators` was fixable. OD-034 is
untouched by this.

## 4. OD-028 closed — no environment write

Nothing was run. The evidence has been complete since 2026-09-03 — the source fix, the orphan
check, and two clean `data/roles-seed` imports against DEV with 11 `al_role` rows before and
after — and the row stayed Open only because this project does not move an OD to resolved
without a named owner and a date. Both are now recorded: **Delivery, 2026-09-05.**

---

## Standing controls — unchanged

- `powerpages/Check-ComponentIds.ps1` before every `pac pages upload`.
- Verify every portal write by query afterwards. Every command above does this itself; the
  reason is still OD-034, which stands.
- `pp15evidence` before believing the notification queue is healthy.
