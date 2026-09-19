# Item 6 — the due date defaults to three days, and only a manager may move it

**Date:** 2026-09-19
**Environment:** `Env_AQ_Dev` only.
**Branch:** `feat/change-batch-sep-2026`
**Decisions:** AD-153, AD-154

---

## Your question, answered first

> "This is calendar time unless I say otherwise; flag it in your plan so I can confirm
> whether it should be working days."

**It is calendar time, and that is now pinned by a test rather than left to a comment.** You
said "3 days", so a file uploaded on **Thursday morning is due on Sunday morning**, not the
following Tuesday.

It also keeps the **time of day**: uploaded at 4pm, due at 4pm, which is what gives a checker
the whole of the third day rather than losing the last afternoon of it.

Remediation counts **working** days for its own deadline (`AddWorkingDays`, five days). The
two are deliberately not reconciled — they are different deadlines for different people. If
you want this one to count working days too, it is one change, inside `ImportRules.DueDateFor`.

---

## The default was already right. The editing was not.

The 72-hour default has been in place since `dcaf167` and **nothing here changed it**. It
applies to newly imported cases only; nothing recalculates a case that already exists.

What was wrong was the other half of the sentence — *"It remains editable."*

### Three defects, all live in DEV until today

**1. The Due date box on the case edit panel could not be saved.**

The panel has offered it since before it was locked. Editing it put `al_duedate` into the
`Fields` payload, and the command refused the whole payload with:

> Field 'al_duedate' cannot be edited.

Not just the due date — **the entire save**, including every other field the user changed in
the same modal, with a message that named a field they had been invited to fill in.

**2. The due date was editable anyway, by anyone, through a second door.**

`al_UpdateCaseDetails` carries a legacy scalar `DueDate` parameter that wrote the column with
**no check at all** beyond `page.cases` Edit — which four roles hold in DEV, two of them
checkers. The comment in the code said the field was locked "not by a checker, not by a
manager, not by an administrator". That was true of one path and false of the other.

**3. Item 8's refusal was worded differently on the two tiers — in September only.**

`caseHeaderDates.ts` formatted the date through `Intl` with `en-GB`, which abbreviates
September as **"Sept"**. .NET's invariant `"MMM"` gives **"Sep"**. So the app and the server
produced different sentences for the same rule, for one month of the year. Every test that
pinned that wording used January, where the two happen to agree. Found because item 6's own
tests were written in September.

It now formats from a fixed month table. `Intl` output also moves with the browser's ICU
version, so a table is the only way the two stay identical. All twelve months are pinned.

---

## What changed

### One door instead of two

The legacy `DueDate` scalar is now **folded into the `Fields` payload** before anything else
runs. One gate, one audit line, one value for item 8's comparison to read. An explicit
`Fields` entry wins if a caller sends both.

### A new capability: `case.duedate`

| Role | May move a due date |
|---|---|
| AL Portal - T&C Supervisor | **Yes** |
| AL Portal - Outcome Testing Manager | **Yes** |
| Administrators | **Yes** (break-glass) |
| AL Portal - Tax Reviewer | No |
| AL Portal - AQS Reviewer | No |
| AL Portal - Adviser Remediation, Planner, Portal Administrator | No |

**Its own key, not a higher level on `page.cases`.** `page.cases` Edit is what lets a Tax or
AQS reviewer complete the header fields the IO extract does not carry — your direction of
2026-09-12. Raising that bar would have taken the whole header away from the people who are
meant to fill it in. A separate key moves one field instead.

**Portal Administrator is deliberately absent**: it holds `page.cases` at View, so it cannot
reach the command at all, and a grant that can never be exercised reads as an authority
somebody has.

### The portal refuses it for everyone

That is the *"in codeapps"* half of your instruction. A **T&C Manager signed into the portal
is refused; the same person in the Code App is allowed.** Already the behaviour, now the
documented reason and a test that says so.

### A deadline cannot be pulled back under the meeting

Item 8 says the meeting cannot be later than the due date. Making the due date editable
opened a way to break that rule **without touching the date item 8 guards** — by moving the
deadline instead. `CaseHeaderRules.ValidateDueDate` closes it:

> Due date cannot be earlier than Date of meeting - Client contact (10 Sep 2026).

Only when the due date is actually being moved. A case whose stored dates already disagree —
imported before the rule — stays editable, which is what makes fixing it possible.

### In the panel, for everyone else

Shown and explained, not hidden. A deadline somebody cannot move is still a deadline they
need to see, and an empty space would read as a case with no due date.

> **Due date**
> 21/09/2026
> *Set to three days after the case was uploaded. Only a manager can move it.*

---

## A finding I did not act on

**DEV holds duplicate permission rules.** `al_pagepermission` rows are keyed two different
ways: `seedwebroles` slugs the role name, `al_SetPagePermission` — what the security page
calls — does not. So T&C Supervisor has **two active `command.signoff` rules**:

```
PP-AL-PORTAL--T-C-SUPERVISOR-command.signoff      (from the seed)
PP-AL Portal - T&C Supervisor-command.signoff     (from the security page)
```

This matters because `MaxLevel` takes the **highest** level across active rows. A shadow row
the security page cannot see **keeps granting what an administrator believes they have just
revoked.**

The new rows I wrote use the raw form, so the security page can manage them. **I have not
touched the existing duplicates** — deactivating a rule someone may be relying on is your
call. Worth a look before the next promotion.

---

## Deployment

| Step | Command | Result |
|---|---|---|
| 1 | `dotnet build -c Release` + `dotnet test` | clean; **1091 passed** (16 new) |
| 2 | `pushassembly` | **246,784 bytes**, 2026-09-19 15:49:56Z |
| 3 | `setpagepermission` ×3 (dry run, then `--confirm`) | three rules created, verified Active |
| 4 | `tsc -b` + `vitest run` | clean; **606 passed** (23 new) |
| 5 | `npm run build` + `pa app push` | bundle `index-D44K-EQ6.js` |
| 6 | `round-trip-src.ps1` | 822 files; the new bundle is in `src/`, the old one gone |

App pushed **before** the round-trip, so the bundle reaches `src/`.

**Nothing to upload to the portal.** Its behaviour is unchanged — the only edits were a
comment and a test.

### These permission rules do NOT promote

`al_pagepermission` rows are **data, not solution components**. When this reaches TEST and
PROD, each environment needs:

```powershell
cd plugins\OutcomeTesting.Registration
dotnet run -- setpagepermission <orgUrl> "AL Portal - T&C Supervisor" case.duedate Edit --confirm
dotnet run -- setpagepermission <orgUrl> "AL Portal - Outcome Testing Manager" case.duedate Edit --confirm
dotnet run -- setpagepermission <orgUrl> "Administrators" case.duedate Edit --confirm
```

Without them, **nobody** can move a due date in that environment — a key with no rows
resolves to None for everyone.

---

## How to test

### The default

1. Import a file. Every case in it shows a **Due date exactly three days after the upload**,
   at the same time of day — including the cases whose `DueDate` cell in the sheet said
   something else. That column is Intelligent Office's deadline for the paraplanner's task
   and is deliberately not read.
2. Check an **existing** case. Its due date is **unchanged**. Nothing recalculates.

### As a manager (T&C Supervisor or Outcome Testing Manager)

3. Open a case → **Edit case details**. The **Due date** box is editable.
4. Change it and save. It saves, and the case history records
   `Due date 2026-09-21 -> 2026-09-25`.
5. Change the due date **and** another field in the same save. Both save. Before today this
   combination failed entirely and lost the other change.

### As a checker (Tax or AQS Reviewer)

6. Open the same panel. **Due date is read-only**, with *"Only a manager can move it."*
   underneath — and the value is still visible.
7. Every other field on that form is still editable. This is the part worth checking: the fix
   must not have cost checkers the header.
8. From the browser console, call `al_UpdateCaseDetails` with `Fields` naming `al_duedate`.
   **Refused**, naming `case.duedate`.

### The date rule

9. As a manager, on a case whose date of meeting is **10 Sep**, set the due date to **5 Sep**.
   Refused before the round trip:
   *"Due date cannot be earlier than Date of meeting - Client contact (10 Sep 2026)."*
10. Set it to **10 Sep** — the same day. **Accepted.** Same-day is not late.
11. Send the same failing pair by hand through the command. Refused with the **same sentence**,
    word for word. That equality is what the month-table fix restored.

### The portal

12. Sign into the portal as a **T&C Manager** and open a case header. There is no due date
    field, and a hand-made payload naming `al_duedate` is refused. Allowed in the app, refused
    here — that is *"only editable by managers in codeapps"* working as written.
