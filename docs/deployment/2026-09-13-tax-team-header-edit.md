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

## Still open

- **Not exercised end to end.** The plug-in's rules are unit tested and the page renders, but
  no Tax checker has saved a change against DEV through the portal. That is the proof, and it
  has not been run here.
- **The Code App still edits these fields through `al_UpdateCaseDetails`**, unchanged. Two
  front ends now write the same two columns by different doors; both derive the route through
  the same method, which is what keeps them honest.
