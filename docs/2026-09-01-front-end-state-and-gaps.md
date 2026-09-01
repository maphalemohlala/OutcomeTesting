# Front-end state and gaps — 2026-09-01

Where the two front ends actually stand, and what is missing, as of `21ff635`. Written so
the next person can pick the work up without re-deriving any of it.

Method: read against `knowledge/requirements-index.md` (BR/FR/PP ids) and verified against
the code and, where it matters, against `Env_AQ_Dev` directly. Every claim below has a file
or a query behind it.

**Summary.** The Code App is broad, with two named holes. The portal is deep in exactly one
place — the answering module — and shell everywhere else, and **its submit path is wired to
a cloud flow that does not exist**, so a reviewer can answer a full checklist and then
cannot submit it.

---

## 1. Power Apps Code App (`app/`)

Managers and administrators (AD-044).

### Built

Dashboard; case worklist and detail (edit panel, history tab, outcome summary); read-only
review oversight; imports; reports including the working-day SLA clock; exports; admin
(question library, users, security/RBAC).

### Not built — and the app says so

`app/src/app/router.tsx` marks its own gaps with a `NotBuiltYet` component. Two routes:

| Route | Blocker the route states | Is that still true? |
|---|---|---|
| `/cases/:caseId/allocation` | Case Assignment writes (assign/reassign commands, AD-040) | **Yes.** This is OD-029 |
| `/cases/:caseId/recheck` | "Recheck and Outcome tables, OD-007" | **No — stale.** See §3 |

### Gaps not marked anywhere

**Import writes bypass the server-command rule.** `app/src/features/imports/useCaseUpload.ts`
creates `al_importbatches` and then `al_outcomecase` rows directly over OData. There is no
import command among the 18 Custom APIs in `src/customapis/`, so BR-002 validation exists
only on the client, and a caller reaching Dataverse another way is unvalidated. AD-003 puts
business logic in shared server-side commands; this is the one write path that does not
follow it. Whether to close it is a decision, not an oversight to fix silently — the
client-side validation does work for the intended user.

**The RBAC screens cannot work in DEV** until the three Custom APIs from the 2026-09-01
reconciliation are registered — see `docs/deployment/2026-09-01-dev-reconciliation.md`.
That is a deployment gap, not a code gap: the code is on `main` and correct.

---

## 2. Power Pages portal (`powerpages/`)

Tax checkers, AQS checkers, advisers, T&C Managers (AD-044) — the surface where checks are
actually performed.

### Built, and genuinely finished

**The review answering module.** `web-templates/ot-review-detail/` (786 lines): typed answer
controls per response type, autosave, fail reasons, and a submitted review rendering
read-only with every control disabled (PP-11). This is the one portal feature that is not a
shell.

**The security model.** 10 web roles, 11 table permissions, 6 page rules, registration
settings off, deny-by-default from Home. Both gates pass — `Check-ComponentIds.ps1`
(171 identities, no duplicates) and `Check-PortalSecurity.ps1` (all assertions). **It has
never been uploaded**: the runbook `docs/deployment/2026-08-31-portal-security-closure-deployment.md`
stopped at step 2 because DEV holds one contact with no name and no email, so every
`AL Portal - *` role has zero usable members.

### Shell stage — the pages say so themselves

Four templates carry an explicit "Shell stage" notice: `ot-home`, `ot-my-work`,
`ot-review-list` (serving both `/tax-reviews` and `/aqs-reviews`), and `ot-remediation`.
They render real data from Dataverse, unfiltered.

### The biggest hole: nobody can submit a review

The submit path is **portal page → cloud flow → `al_SubmitReview` → Dataverse**. A Custom
API cannot be invoked from a page directly; a cloud flow with the "When Power Pages calls a
flow" trigger is the supported route, and `ot-review-detail` implements the client half
against it.

The plug-in is registered in DEV. The button is wired. But:

- **`src/Workflows/` does not exist.** There are zero cloud flows in the solution.
- **`sitesetting.yml` line 109**: `OutcomeTesting/Flow/SubmitReview` has `adx_value: ''`.

The template degrades honestly — with no trigger id it renders a warning in place of the
button. The effect is that a reviewer can answer a complete checklist, have it autosaved,
and have no way to submit it. **This is the highest-value portal fix, and it is
well-specified**: the client half already exists and names exactly what it expects.

### Genuinely unbuilt

- **PP-12 remediation** — `ot-remediation` is a read-only list. Adviser response and T&C
  attestation are marked "arrive in Phase 6". AD-045 puts the whole remediation loop in the
  portal, so this is the portal's reason for existing and it is absent.
- **PP-15 notifications — nothing, anywhere.** No `al_Notification` table in
  `src/Entities/`, no outbox, no flow. Nine required events, zero built. This table is in
  the approved core model in `knowledge/project-context.md` and is not recorded as deferred
  anywhere.

---

## 3. Stale blockers — work that looks blocked and is not

Four places claim to be waiting on something that has since landed. Each one will cost the
next person time if they believe it.

| Where | Claim | Reality |
|---|---|---|
| `ot-my-work` template, lines 6-7 | Per-user filtering "needs `al_assignedcontactid`, which arrives with the Phase 2 schema delta (AD-047)" | **That column now exists** on `al_ReviewInstance`. PP-03 scoping is immediately buildable |
| `ot-remediation` template, lines 5-8 and its on-page notice | Working-day ageing pending the business calendar (OD-018) | **OD-018 resolved 2026-08-30.** The clock is implemented in `app/src/lib/workingDays` and used by the app's reports; the portal simply does not call it |
| `router.tsx`, `/cases/:caseId/recheck` | Blocked by "Recheck and Outcome tables, OD-007" | **`al_Outcome` exists (AD-032), OD-007 is resolved (AD-031), and `al_RegradeCase` is registered in DEV.** The separate Recheck table is deliberately deferred and the final outcome is carried on `al_Outcome`, so its absence is not a blocker |
| `knowledge/requirements-index.md`, PP-13 row | "Blocked by OD-018" | Same as above — resolved |

**These are left uncorrected in place on purpose**, so this document is the one thing to
read rather than a set of edits scattered across four files. Correcting them is a small
task worth doing, but it should be done deliberately, not folded into other work.

---

## 4. Cross-cutting

- **There are no cloud flows at all.** `src/Workflows/` does not exist, yet both the portal
  submit path and all of PP-15 assume Power Automate. This is one missing capability with
  two dependants, which is an argument for building it once and properly.
- **Two tables from the approved model are absent**: `al_Notification` (not recorded as
  deferred) and `al_Recheck` (deliberately deferred — AD-032 carries the final outcome on
  `al_Outcome`).

---

## 5. Suggested order

1. **The submit flow.** The portal's one built feature is unusable without it, and the
   client half already defines the contract.
2. **Register the three Custom APIs in DEV** (needs a bearer token — see
   `docs/deployment/2026-09-01-dev-reconciliation.md`), then the AD-013 export-and-replace
   round trip, in that order.
3. **Scope My Work to the signed-in contact.** Small, and the shell notice currently
   misleads.
4. **Allocation, OD-029.** The largest genuine gap: no assigned-user lookup on
   `al_CaseAssignment`, no assign command among the 18, no screen. Until it exists a
   Tax-then-AQS case parks at `Queued` with no supported way forward, and allocation in DEV
   happens by editing `al_casestatus` directly, bypassing BR-003 history and BR-012 audit.
5. **Correct the four stale notes in §3.**

Then the two large bodies of unbuilt work: the remediation loop (PP-12) and notifications
(PP-15).

## Also outstanding, unchanged by this analysis

- Portal deployment blocked on DEV portal identity (runbook step 2).
- OD-025 — plug-in signing key in git history, now on `main`; deferred to the Key Vault path.
- OD-026 — the AD-013 round trip; `src/PluginAssemblies/` still holds a DLL (AD-062).
