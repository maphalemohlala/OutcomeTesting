# The Tax team edits its own header fields — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Project owner, 2026-09-13: Tax checkers need to be able to edit the table headers — to decide
whether an AQS review is required or not.

---

## What the two fields are, and why the Tax team

The Checker Checklist labels them **"Tax check required (Tax team to complete)"** and
**"For Tax team usage"**, so the document already assigns both to the Tax team. Until now only
the Code App could set them, through `al_UpdateCaseDetails`.

"For Tax team usage" is the one that answers the project owner's question directly:
`UpdateCaseDetailsPlugin.DeriveRoute` treats **Return to paraplanner** as ending the case at
Tax with no AQS check owed, overriding what the tax-check answer alone would derive. That
decision now sits with the Tax checker doing the check.

Both fields were already editable server-side and already re-derive the route. What was
missing was a portal path.

## Where it is, and what it refuses

**On the review page**, in the header block's last row, on a **Tax** review that has **not
been submitted**. The Tax checker works there, the header is already drawn there, and the
lock condition only has meaning in a review's context. The case page keeps the read-only
header.

`CaseHeaderRequestPlugin` enforces three rules server-side:

| Rule | Why it is shaped that way |
|---|---|
| Contact holds `AL Portal - Tax Reviewer` | Read from the platform, never from the payload. The contact allowlist is shared with the claim and sign-off columns — one allowlist per table — so a reviewer who can write one of those could otherwise write this |
| The case's Tax review is not Submitted | The guard is the **review's** status, not the case's. The case moves on after a submit — Queued, Awaiting Remediation, Closed — and each would need its own rule; the review answers it directly. A submitted review is immutable (BR-012) and its route was acted on at submit, so moving the disposition afterwards would contradict something already done |
| At least one field actually moves | An edit that changes nothing writes nothing, so the audit line never names a field nobody moved |

A case with **no** Tax review is allowed: that is a case whose Tax check has not been created
yet, where "is a Tax check required?" is precisely what these fields answer.

## Why a trigger column

The same resolution the answer (AD-053), the claim (AD-076) and the sign-off (AD-099) each
reached: a browser write on `al_outcomecase` is refused with `90040106` whatever the table
permissions say. The page PATCHes one JSON column on the signed-in user's **own** contact
row; the plug-in does the work as the application user.

That is also a better boundary than a payload. The site's contact permission is Self-scoped,
so the request can only ever reach the signed-in user's row, and the plug-in takes the editor
from `PrimaryEntityId` — the row it was given — rather than from anything the page sent.

The route is derived by calling `UpdateCaseDetailsPlugin.DeriveRoute`, the same derivation
the command path uses, so the two front ends cannot disagree about what the same answers
mean. The audit event names the contact, using the actor fix from
`2026-09-13-signoff-signatory.md`.

**The controls are deliberately not hidden by web role.** The site has a recorded position on
this, on the sign-off panel: hiding by role in Liquid "would look like access control and
enforce nothing (NFR-SEC-01)". So they render on any unsubmitted Tax review and the plug-in
refuses server-side, in words the page shows. A non-Tax user sees controls that will refuse,
and is told why.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `addmemocolumn … contact al_CaseHeaderRequest 4000` | created in solution `OutcomeTesting` |
| 2 | `dotnet build -c Release` → `pushassembly` | **218,624 bytes**, matching the local build |
| 3 | `registertype … CaseHeaderRequestPlugin` | plugintype `1a0fa190-56af-f111-aaac-e4fade069307` |
| 4 | `registerstep … Update contact 40 al_caseheaderrequest sync` | step `c3013097-56af-f111-aaac-e4fade069307`, added to the solution |
| 5 | `setsitesetting Webapi/contact/fields` | three columns → **four** |
| 6 | `pushwebtemplate … OT Review Detail` | **84951 → 93199 chars** |

Order matters and was followed: the column before the assembly, the assembly before the type,
the type before the step. `registerstep` refuses a type the deployed assembly does not carry.

Checked before the template push: Liquid 445 opens against 445 closes, 33 comment pairs, no
tag delimiters inside any comment, 4 matched `<script>` tags — and the `if` / `for` /
`capture` / `block` stack walked to confirm **nesting**, not merely a balanced count. A
balanced count can still be wrongly nested, and this change added a conditional around an
existing one.

| Suite | Result |
|---|---|
| `dotnet test` (plugins) | **764 passed** (5 new) |

---

## Proved against DEV

Run with `verifytaxheader`, added to the registration tool for this. It writes the same JSON
column on the same contact row the portal PATCHes, so the whole server path runs: the step,
the payload, the guards, the derivation and the audit event. Only the browser half is
untested, and it is the thin half.

**The refusal**, as `angela.houghton@ascotlloyd.co.uk` (holds T&C Supervisor, not Tax Reviewer):

```
Roles held: AL Portal - T&C Supervisor
Holds AL Portal - Tax Reviewer: False
  [PASS] refused a contact without the Tax Reviewer role:
         PRECONDITION: Editing the Tax team's fields needs the
         AL Portal - Tax Reviewer role on your portal account.
  [PASS] names the role in the refusal
  [PASS] left the case untouched: disposition=(none)
```

Worth noting the refused contact holds **T&C Supervisor**, a senior portal role. The guard is
role-specific and not seniority-based, which is what BR-008's separation of duties needs.

**The edit**, as `Simunye.Radingwana@ascotlloyd.co.uk` on seeded case `IO-SEED-TAX-01`:

```
  [PASS] accepted the edit: no refusal
  [PASS] disposition applied: (none) -> 120910570
  [PASS] route re-derived (BR-004): Tax only -> Tax then AQS
  [PASS] trigger column cleared: al_caseheaderrequest is empty
  [PASS] a NEW audit event was written
  [PASS] audit event names the contact, not the application user:
         actor=Simunye Radingwana 2fe7f28b-67a7-f111-aaac-e4fade069307

Restored: disposition=(none), taxCheckRequired=120910560, route=Tax only
```

`Tax only -> Tax then AQS` is the line that matters. It is not a column changing: it is
`UpdateCaseDetailsPlugin.DeriveRoute` running inside the portal path and re-deciding whether
an AQS review is owed, which is the whole point of the feature. The last line closes the loop
with `2026-09-13-signoff-signatory.md` - the event names the person, not
`# PowerPages Data Runtime PROD`.

### How the harness is built, and why

Modelled on `verify` / `verifysignoff` / `verifyregrade`, with three deliberate differences,
each answering something the 2026-08-27 audit found in those:

- **It names its target.** The older verbs take the FIRST row of `al_outcomecase` with no
  filter and no environment guard; the audit's example was `verifyregrade` against production
  leaving "permanent audit events on a real client case recording a regrade that never
  happened". This one takes a case reference and a contact email and refuses anything that
  does not resolve to exactly one row.
- **It restores in a `finally`, then reads back.** An earlier draft printed the before-values
  and called them "Restored", which says the same thing whether or not the write landed. It
  now re-reads the row and says `RESTORE INCOMPLETE - check this case by hand` when the two
  disagree.
- **It proves the audit event is new.** Taking the latest `UpdateCaseDetails` event would pass
  on one an earlier edit left behind — and if this edit wrote none at all, that stale event is
  exactly what the check would find. The newest event id is captured before the write and
  compared after.

### A correction

An earlier query of contacts and web roles, joined through `powerpagecomponent`, returned
seven role assignments and no Tax Reviewer. That reading was **wrong**: it missed six of
`Simunye.Radingwana@ascotlloyd.co.uk`'s roles, including both reviewer roles. The registration
tool's own `RolesOf` reads them correctly, and the plug-in uses
`WebRoleRegistry.RolesForContact`, which is the authority. Nothing was built on the wrong
reading — a role grant was considered and turned out to be unnecessary — but the FetchXML
join is not a reliable way to ask this question on this site.

---

## Still open

- ~~Not exercised end to end.~~ **Done — see "Proved against DEV" above.**
- **The Code App still edits these fields through `al_UpdateCaseDetails`**, unchanged. Two
  front ends now write the same two columns by different doors; both derive the route through
  the same method, which is what keeps them honest.
