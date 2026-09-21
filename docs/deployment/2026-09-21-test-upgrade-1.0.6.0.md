# TEST upgraded to 1.0.6.0

**2026-09-21. AD-205.** The day's work promoted, on the project owner's instruction. TEST was
on 1.0.5.0 managed, installed 2026-09-11, and had none of the managed-list work — the
`al_listoption` table did not exist there at all.

## The audit that had to come first

Asked for explicitly, and it found nothing further after AD-203's six. The check covered every
component type rather than only the portal ones:

| | Environment | Solution | |
|---|---|---|---|
| Power Pages components | 267 | 267 | OK |
| `al_*` Custom APIs | 31 | 31 | OK |
| SDK steps (our assembly) | 24 | 24 | OK |
| Security roles | 2 | 2 | OK |
| Plugin assembly | 1 | 1 | OK |
| Tables | 28 `al_*` | 27 entity components | see below |

**The table count is not a gap and the first reading of it was wrong.** Two of the 28 are N:N
intersects — `al_al_failreason_al_response` and `al_listoption_al_outcomecase_products` — and
solution packager does not carry an intersect as an entity component. It carries the
*relationship*, which was confirmed present in both cases:

```
al_failreason_response                   ManyToMany   al_FailReason / al_Response
al_listoption_al_outcomecase_products    ManyToMany   IntersectEntityName al_listoption_al_outcomecase_products
```

Recorded because a naive membership diff reports those two as missing every time it is run,
and somebody will run it again.

## The import

```
pac solution import --activate-plugins --async
→ completed in 00:02:23, Import ID 1547692a-…
```

**`--activate-plugins` is not optional.** An import that carries step XML without it switches
the registrations off — that is what happened on 2026-09-02 and cost six `al_response` steps.
Verified afterwards rather than trusted: **55 of 55 steps on the assembly are active, none
disabled.**

Verifying that took two wrong queries first. `$expand=plugintypeid($select=assemblyname)`
returns nothing usable, and an unfiltered `plugintypes` page misses most of them — both
reported **0 steps**, which reads identically to "the import disabled everything". The answer
is to fetch the types by `_pluginassemblyid_value` and then the steps by those ids in chunks.

## What arrived, checked one at a time

| | Result |
|---|---|
| Solution | **1.0.6.0 managed** |
| `List Option - read` | present, roles in `content`, scope Global |
| `Adviser Mapping - advisers I supervise` | present, roles in `content`, scope **Contact** |
| `Webapi/al_listoption/*` | present |
| `Webapi/al_questionversion/*` | present |
| `Webapi/al_remediationaction/fields` | the AD-199 list — no `al_recheckrequired`, no `al_changesadvice` |
| `page.admin.lists` | all three rules |
| Steps | 55 active |

The two table permissions carry their web roles **inside the `content` JSON**, which is the
whole point of AD-195 — a managed import that had only populated the legacy intersect would
have looked identical in the maker portal and returned 403 at runtime.

## And the data the solution cannot carry

A solution carries the table, never the rows. `al_listoption` arrived empty, which would have
reproduced AD-192 in TEST exactly: every managed dropdown silent, and the case header falling
back to the free-text column.

**19 options seeded, carrying DEV's own ids** — the same rule the `al_role` seed followed. The
four lookups and the products N:N point at these by id, so fresh guids would make every saved
choice unresolvable the moment case data moved between environments.

```
Product / solution type  5      Case type          4
Sample source            4      Pre or post check  2
Products                 4      (still the four placeholders)
```

TEST now holds 19 list options, 10 roles and 47 questions — matching DEV on all three.

## Open, and not for this note to close

**`al_advisermapping` has one row in TEST.** Sign-off and, since AD-202, regrade both require
the signatory to be the manager mapped to the case's adviser. With one mapping, TEST can
complete a remediation for exactly one adviser and no other. This is F58 and it is the test
team's, but it is now blocking rather than cosmetic.

**The four Products placeholders** are still placeholders, in both environments, awaiting the
client's naming.

## A privilege finding, reported rather than changed

TEST matches DEV exactly, so the promotion is faithful. But the thing it faithfully copied is
worth a look:

```
Outcome Testing App User, al_listoption:
   Read, Append, AppendTo, Create, Write, Delete    — all at Global depth
```

`grantsecurity` grants that role **read, append and appendTo only**; create, write and delete
are the admin role's. So those three were granted outside the tool, and because `GrantTable`
only ever adds, re-running `grantsecurity` will not take them back.

There is a plausible reason not to touch it: the Dropdown options page is offered to the
Outcome Testing Manager and Portal Administrator so the checking team maintains its own lists,
and if those people hold the App User role rather than App Admin, the page needs exactly these
privileges to work. **Tightening it might be correcting a mistake or might break the page for
the team it was widened for, and that is not a call to make from the outside.** Flagged for a
decision; nothing changed.

Note that the page permission (`page.admin.lists`, three roles) closes the UI path but not the
API path — a holder of the App User role can reach the table directly. `CascadeType.Restrict`
still refuses any delete of an option a case holds.
