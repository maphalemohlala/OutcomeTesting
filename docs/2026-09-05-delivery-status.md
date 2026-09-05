# Delivery status — 2026-09-05

Written at commit `2296423`. Supersedes nothing; it sits alongside
`docs/2026-09-04-outstanding-work.md`, which remains the register of what is left and which
was re-ranked and trimmed today. This records what changed across the day and what state
`Env_AQ_Dev` is actually in.

Every environment claim below was verified by query against `Env_AQ_Dev` after the fact.
Where something did **not** land, it says so.

**The day in one line: the register went from ten items to six, and PP-15 stopped being a
claim.**

---

## 1. PP-15 is proved end to end — the headline

This is the one that changes the delivery position, so it goes first.

Yesterday PP-15 was *built*. This morning it was *switched on* — drain step registered,
mailbox approved and tested. Neither of those is the same as working, and the gap was
visible in the data: **`al_notification` held zero rows**. Not a drained outbox, an empty
one. The emitters had never produced a notification in DEV since the table was created on
2026-09-03, so no message had travelled emitter → outbox → asynchronous drain →
server-side email, and every component had been verified alone and none of it together.

This evening one qualifying event was caused deliberately and followed through every hop:

```
seeded al_outcomecase        PP15-PROOF-20260905205253
created al_caseassignment    386523be-…            <- the qualifying event
al_notification              ALLOCATION-386523BE…  Pending -> Sent at 20:53:17Z
  email 1ba6edce-…           Outgoing, Sent,     created 20:53:13Z
  email 90f9d1dd-…           Incoming, Received, created 20:53:39Z
```

It ran twice, and both runs delivered. **The `Received` rows are the point.** `Sent` on an
`al_notification` row means the drain handed the message to Dataverse, which is the last
thing the drain can observe; the inbound copy of the same message, tracked back into the
service mailbox by server-side synchronisation, is what says it actually arrived. Reading
only the outbox is how a queue that is quietly not sending looks healthy — the failure
OD-030 warned about, one layer further out.

**Two new commands** in `plugins/OutcomeTesting.Registration`:

| Command | Writes? | Purpose |
|---|---|---|
| `provepp15 <orgUrl> --confirm <orgUrl>` | Yes | Causes one allocation and follows it through every hop. Checks the emitter step, the drain step and the mailbox **before** writing anything. |
| `pp15evidence <orgUrl>` | No | Prints the whole outbox and, per row, every email carrying its subject with direction and status. The standing check. |

`provepp15` writes real business records, which is why it takes `--confirm` with the org URL
repeated — the same discipline the `verify` modes use. The blast radius is held to rows it
created: its own seeded case rather than one somebody is working, allocated to the account
the drain runs as so the mail lands in the service mailbox and not a colleague's inbox, and
both deleted afterwards. The two `al_notification` rows are left in place deliberately: they
are the evidence, and DEV's outbox is now two `Sent` rows rather than empty.

### 1.1 One correction, worth carrying

The first run reported `[FAIL] email activity created: none found` while every other check
passed. **Dataverse appends a tracking token to the subject it stores** —
`Case PP15-PROOF-… has been allocated to you CRM:0249002` — so an exact match on the subject
the outbox recorded finds nothing, ever. The email had been created and sent the whole time.

The lookup now matches on prefix and prints the subject it found, so a genuine mismatch stays
a finding rather than collapsing into a pass. Recorded because the failure mode is a bad one:
a verification that reports "no email was sent" when the email was sent is worse than no
verification at all.

## 2. OD-033 is closed — both halves

`Administrators` had its authenticated-users flag cleared this morning and verified reading
`No`. **`Checker` was deleted this evening** on project owner direction, taken on the
evidence: it granted nothing, it was declared nowhere, and declaring it in source would have
meant keeping a role nobody had claimed.

```
deletewebrole … Checker --confirm …
  Web role 'Checker' (0931e8d2-…): authenticatedusersrole=True, anonymoususersrole=False
  no site component references it - nothing is bound to this role.
  Deleted. No web role named 'Checker' remains.
```

`Authenticated Users` correctly keeps the flag, which is what `webrole.yml` declares. It is
now the only role carrying it, so the drift is gone from the environment entirely.

**The guard is the reusable part.** `deletewebrole` refuses to delete a role anything is bound
to, and web role bindings in the enhanced data model live inside the `content` JSON of
`powerpagecomponent` rows rather than in link tables — so it scans every component on the site
for the role id. An empty role and a role that grants something are indistinguishable from the
role row alone, and only one of them is safe to remove.

**This could not have gone through `pac pages upload`.** The role existed only in the
environment and in no manifest, so the pipeline could not see it in either direction. That is
precisely why this half stayed open while `Administrators` was fixable, and it is a second
illustration of OD-034 rather than a new problem.

## 3. OD-032 is closed — `SetFailAccountability` has its own value

```
addcommandvalue … 120910792 SetFailAccountability
  al_command 120910792 = 'SetFailAccountability' inserted and published (24 values).
```

Read back from metadata after the publish rather than trusted from the insert, which reports
success on the definition it just changed. `SetFailAccountabilityPlugin` now writes
`120910792`; the assembly was rebuilt and pushed, and **301 plug-in tests pass**.
`src/Entities/al_AuditEvent/Entity.xml` carries the value too — source and DEV both read 24
options, so they agree.

**The decision was always the hard half, and it needed no data surgery.** The cut-over is
**2026-09-05**. Audit rows on `120910788` created before that date are `SetFailAccountability`
writes when they carry `al_name = "SetFailAccountability"` and `al_targettable = "al_outcome"`,
and `SetRoleAssignmentActive` otherwise; on and after it, `120910788` means
`SetRoleAssignmentActive` and nothing else. The five known rows (all created 2026-08-30) fall
under that rule exactly. NFR-AUD-01 makes them immutable and the fix never proposed to touch
them.

The replay-protection half closes with the same change: `CommandHelpers.FindAuditByKey` scopes
on `(idempotency key, command)` again. Restored *before* a caller exists, which is the cheap
moment.

**One caveat downgraded on inspection.** `app/src/generated/models/Al_auditeventsModel.ts` and
`app/.power/schemas/dataverse/auditevents.Schema.json` are autogenerated and lag DEV by two
values now (neither carries `120910791` either). That is cosmetic: `choiceLabel` prefers
Dataverse's own formatted `al_commandname` and only falls back to the generated map, so case
history renders `SetFailAccountability` correctly regardless. It clears on the next
regeneration and is not a defect.

## 4. OD-028 is closed — no environment write

Nothing was run. The evidence has been complete since 2026-09-03 — source fix, orphan check,
and two clean `data/roles-seed` imports against DEV with 11 `al_role` rows before and after —
and the row stayed Open only because this project does not move an OD to resolved without a
named owner and a date. Both are now recorded: **Delivery, 2026-09-05.**

Worth saying plainly, because it recurs: an item can be finished and still open here. That is
the recording rule working, not a backlog.

## 5. What DEV actually looks like now

| | State | Verified |
|---|---|---|
| Mailbox `svc.automate.aq@ascotlloyd.co.uk` | Approved `Yes`, outgoing test `Success` | Queried in the proof run's preconditions |
| Drain step | Registered, post-operation, **asynchronous**, enabled, runs as `3c6f441a-…` | Same |
| Emitter step | Registered on Create of `al_caseassignment`, post-operation, synchronous, enabled | Same |
| `al_notification` | **2 rows, both `Sent`**, each with a delivered email | `pp15evidence` |
| `al_command` | 24 values, including `120910792` | `addcommandvalue` read-back |
| Plug-in assembly | Pushed with the repointed command value | `pac plugin push --type Assembly` |
| Web roles | `Checker` gone; `Authenticated Users` the only role with the flag | `deletewebrole` re-query |

## 6. What did not change today, and is not claimed to have

- **OD-034 stands.** No `pac pages upload` was run this evening, and nothing here needed one.
  The morning's fourth reproduction and its correction — the abort does *not* land before web
  pages are processed — remain the current position.
- **`src/` is still not the source of truth.** `al_Notification` has no definition in
  `src/Entities/` at all, and the AD-013 export round trip is still owed. This blocks every
  promotion and is now item 2 of the register.
- **TEST and PROD are untouched.** Everything above is DEV. The per-environment mailbox gate
  is unchanged: no approved, tested mailbox means no drain step and rows resting at `Pending`.
- **The four unnamed PP-15 events are still unnamed.** Five of nine are built and now proved;
  the rest are a product owner question, not an engineering one.

## 7. Register movement

Closed today: **PP-15 proof** (was item 2), **OD-033** (was item 3), **OD-032** (was item 4),
**OD-028** (was item 10). Also closed this morning: **OD-035**.

The register is now six items, and its character changed: **nothing left in it is a defect in
something already delivered.** One tooling gap (OD-034), one ALM debt that blocks promotion,
and four decisions and scheduled items owned outside Delivery.

Detail for the evening's writes: `docs/deployment/2026-09-05-pp15-end-to-end-proof.md`.
Detail for the morning's: `docs/deployment/2026-09-05-portal-repairs-and-drain-enable.md`.
