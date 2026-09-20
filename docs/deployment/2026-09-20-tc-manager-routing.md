# Adviser → T&C Manager routing, and the Sign-off due notification

**Date:** 2026-09-20
**Decision:** AD-162
**Audit item:** Fixes 5
**Environments changed:** DEV only — table, option value, assembly, Code App, three page-permission rows.

---

## The gap this closes

Nothing was sent when a case reached Awaiting Sign-off.

The adviser was told when their remediation was **approved** or **sent back**. The person who
had to do the approving was never told there was anything to approve. A case moved to
Awaiting Sign-off and waited for somebody to notice it.

That is the half of Fixes 5 that mattered. The dropdown was the visible part; the silence was
the defect.

---

## Routing, not access

By the owner's decision of 2026-09-20, the mapping decides **who is told**, not who may act.
Every T&C Manager already reads every case and may sign off any case, and nothing here
narrows that.

The remediation brief asked for a test proving an unmapped T&C Manager cannot read the case.
**That test is deliberately absent**, and both test files say so in their own summaries rather
than leaving a silent omission — it would assert the opposite of the access model Phase 1
built and the owner confirmed.

What is pinned instead:

- `TcManagerRouting.Routing` carries a recipient and a reason and **nothing** a later change
  could mistake for a permission. A test asserts that shape, so adding one means having the
  argument there first.
- Holding `page.admin.advisers` never carries `command.signoff` with it. The Outcome Testing
  Manager maintains the mapping and cannot sign off a remediation.

---

## What was built

| Piece | Detail |
|---|---|
| `al_advisermapping` | Organisation-owned. `al_adviseremail` is the **alternate key**; `al_tcmanagerid` is a lookup to Contact, **Restrict** on delete |
| `TcManagerRouting` | Resolves a case → adviser email → mapping → manager, or says why it could not |
| `Sign-off due` | New notification event `120910807`, queued by `CompleteRemediationPlugin` when the last action on a case is completed |
| `/admin/advisers` | Code App screen, gated on `page.admin.advisers` |

**Keyed on email, not name.** The extract supplies `AdviserEmail` on every row and an email is
exact. The para-planner is matched by name only because nothing better exists on that side —
which is exactly why AD-161 has to make it fail loudly. There was no reason to repeat that
weakness where a strong key was already on the row. Because the key is enforced by the table,
the resolver takes the first row without testing for ambiguity.

**Restrict, not RemoveLink.** Deleting a contact who is somebody's T&C Manager is refused
rather than silently emptying the mapping. A mapping that quietly became blank routes nothing
and says nothing, which is the failure this change exists to end.

**A missing mapping costs the notification and nothing else.** The adviser has just finished
their work and the case has already moved; failing their completion over a configuration row
would punish the wrong person for the wrong thing. The reason goes to the trace log instead,
so an administrator whose manager says *"I was never told"* can find out why — silence and a
missing email look identical without it.

---

## Before this does anything

**No adviser is mapped yet.** The table deploys empty, so in practice this sends nothing until
rows are created on the new screen. That is a quiet failure, not a loud one: the sign-off
still works, because any T&C Manager can perform one.

### Per environment

1. **Run `setpagepermission`** for `page.admin.advisers` at **Manage** for
   `AL Portal - Portal Administrator`, `Administrators` and
   `AL Portal - Outcome Testing Manager`.
   `DEFAULT_PERMISSIONS` is a **seed, not a migration** — an environment that already holds
   permission rows ignores it entirely. Without this the screen is unreachable for everyone
   and nobody can be mapped. **Done in DEV on 2026-09-20.**
2. **Create the mapping rows.** They are data and do not promote.
3. **Confirm the option value.** `Sign-off due` (`120910807`) is part of the solution, so it
   promotes with it — but check it arrived, because a notification written against a missing
   option value is the kind of failure that surfaces as an empty inbox.

---

## Verification

- **1158** plug-in tests, **615** app tests, `tsc -b` clean.
- The notification wiring was **disabled to prove the tests catch it**: two fail, and the two
  negatives correctly keep passing, because they assert the case progresses whether or not
  anyone is told.
- Table, columns and alternate key **read back after the create returned**, because on this
  project a successful-looking write is not evidence.
- Round-trip clean: 12 new files in `src/`, 0 orphaned. The option value and the alternate key
  are both captured there.
- Assembly **252,416 bytes** in DEV; all **21** steps verified present and enabled afterwards.
