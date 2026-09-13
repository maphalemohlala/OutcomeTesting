# The sign-off's signatory — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Reported by the project owner: the Supervisor sign-off read
"Approved, # PowerPages Data Runtime PROD, 13 Sep 2026", and records elsewhere showed user
GUIDs, "especially on the logs".

---

## What was actually wrong

`al_signoff` **recorded no signatory at all**. Its columns were the outcome, name, notes,
date, code, decision and two lookups — nothing said who signed. So:

- The Code App fell back to `owneridname`. A portal write reaches Dataverse as the site's
  application user (AD-053), so the owner of every portal sign-off is
  `# PowerPages Data Runtime PROD`. That is what the form was showing.
- `SignoffProgressPlugin` could not name an actor on the Audit Event even if it had wanted
  to, so `WriteAuditEvent` fell through to its default of `context.InitiatingUserId` — the
  same application user.

Confirmed against live rows before changing anything:

```
al_actorname : "# PowerPages Data Runtime PROD"
al_actorid   : ef4223af-169e-f111-b8dd-e4fade065423    <- the site's application user
al_command   : SignOffRemediation
al_details   : "Signed off from the portal: Approved. Sign-off 3a8e654a-52af-..."
```

The same day's `CompleteRemediation` events were already correct — actor a **contact** id,
name the person — because `CompleteRequestPlugin` passes the contact explicitly. Sign-off
was the outlier, and `WriteAuditEvent`'s own comment had warned about exactly this case:

> The initiating user unless a caller names someone better. A portal path does: its write
> reaches Dataverse as the site's application user, so the context names the site and the
> signed-in Contact is the actual actor (AD-053).

For a system whose purpose is evidencing who approved what, a sign-off that cannot name its
signatory is a gap against BR-008 and AD-031, not a display defect.

## What changed

| | |
|---|---|
| Schema | `al_signoff.al_signedbycontactid` (text 100) and `al_signedbyname` (text 200) |
| `SignoffRequestPlugin` | Stamps both from `contactId` |
| `SignoffProgressPlugin` | Names that contact as the Audit Event's actor; drops the raw GUID from the details line |
| Code App | `toSignoff` reads `al_signedbyname`, falling back to `owneridname` |
| Portal | Supervisor sign-off renders the signatory between the decision and the date |

**An id and a name, not a lookup**, mirroring `al_auditevent.al_ActorId` / `al_ActorName`.
A signatory is a Contact on the portal path and a systemuser on a command path, so no one
lookup spans both. The name is *stored* rather than resolved on read, so the record still
says who signed after a contact is renamed or deactivated — BR-012 wants an immutable record.

**The signatory is `contactId`, never anything the payload carried.** It is
`PrimaryEntityId` — the contact row the Self-scoped permission limits to the signed-in user,
and the same value `EnsureSupervisorRole` was checked against a few lines earlier. A page
cannot sign in someone else's name.

**The details line no longer carries a GUID.** It read
`"Signed off from the portal: Approved. Sign-off 3a8e654a-…"`; the event already carries the
target id in its own column, and a person reading a case history wants the name. It now
reads `"Signed off from the portal: Approved by <name>."`, and falls back to the bare
decision where no signatory is named — an unnamed signatory reads better as unnamed than as
sixteen bytes of hex.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `addtextcolumn … al_signoff al_SignedByContactId 100` | created in solution `OutcomeTesting` |
| 2 | `addtextcolumn … al_signoff al_SignedByName 200` | created in solution `OutcomeTesting` |
| 3 | `pac solution export` → `unpack` | `al_Signoff` now 27 attributes, was 25 |
| 4 | **AD-013 copy-back** of `Entities/al_Signoff/` | 27 = 27, **zero content differences** |
| 5 | `dotnet build -c Release` → `pushassembly` | **215,040 bytes**, matching the local build |
| 6 | `pushwebtemplate … OT Remediation` | **90555 → 91343 chars** |
| 7 | `npm run build` → `npx pa app push` | built clean, **pushed successfully** |

Liquid checked before deploying the template: 464 tag opens against 464 closes, 37 comment
pairs matched, and no tag delimiters inside any comment — the fault that took the remediation
page down on 2026-09-11 with "unknown tag 'endcomment'".

| Suite | Result |
|---|---|
| `dotnet test` (plugins) | **759 passed** (5 new) |
| `npx vitest run` (app) | **464 passed** |
| `npx tsc -b` | clean |

---

## Still open

- **Existing sign-offs are not backfilled, and will not be.** Their rows never recorded a
  signatory, so nothing can be read back from them; a backfill from the Audit Events was
  possible but is not needed. Project owner direction, 2026-09-13: **TEST holds no data yet**,
  so every sign-off that will exist there is written by the fixed code. The DEV rows keep
  their application-user owner and are left as the history they are.
- **A Tax-only case shows "—" for the regraded outcome, correctly.** Reported as a fault
  against case 254397454 (route "Tax only", disposition "Return to paraplanner"). That case
  carries no `al_outcome` row because AD-055 gives a remediated Tax-only case no BR-005
  grade, so there is nothing to show. The cell says nothing about *why*, which is what made
  it read as a fault. Left alone rather than given invented wording: explaining it needs the
  route on that page, and the phrasing is the project owner's to choose.
