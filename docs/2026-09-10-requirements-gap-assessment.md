# The 8 September requirements pack, against what is built — 2026-09-10

Assessment of `docs/reference/2026-09-08-solution-requirements.md`, supplied by the project
owner on 2026-09-09, against the solution as it stands on branch
`fix/portal-signin-identity-username`.

**Method, and its limits.** Every claim below was read out of the repository — schema XML,
plug-in source, the router, the Liquid templates — and the two suites were run. **Nothing here
was verified against `Env_AQ_Dev` by query**, which is a departure from how the delivery status
documents are written and is stated rather than glossed: where this document says something is
built, it means the code to do it exists and its tests pass, not that the behaviour was
observed in an environment. The AD-106 note in §5 is the specific place that distinction bites.

Suites on the assessed tree: **301** app (`npx vitest run`), **571** plug-in
(`DOTNET_ROLL_FORWARD=Major dotnet test`). Both green.

**Written across a moving tree, and it says so.** The first pass was taken at `972b700`. Six
commits landed while it was being written, from `93571bf` to `85dcb87`, and they built the
largest thing the pack asks for — one remediation action per failed item. Every affected
paragraph below is corrected to the current HEAD rather than left standing, and §3.2 in
particular now records a resolved decision where the first pass recorded an open one.

**The pack in one line: it is largely a description of what already exists, and the distance
left is not features — it is a second environment and two workflow decisions.**

---

## 1. Where the ten must-haves stand

Six built, two partial, two not started. Detail and IDs: MR-01 to MR-10 in
`knowledge/requirements-index.md`.

| # | Requirement | State |
|---|---|---|
| 1 | V8 structure and ordering | **Built**, pinned by an executable test against `docs/reference/checker-checklist.html` (AD-104), which `85dcb87` then tightened by closing a broken `h2` in the reference itself rather than tolerating it. `checklist-v8.md` and `OT-Review-Detail` still carry uncommitted edits |
| 2 | Failed items populate remediation | **Built.** Raised at all only from 2026-09-09; **one action per failed item since 2026-09-10** (AD-107), which is what makes §6.1's per-item fields expressible |
| 3 | Remediation form redesign | **Built and committed** (AD-095, AD-107) — the three choice columns, each fail item as its own answerable row, and a portal list that opens a case to its form |
| 4 | Supervisor sign-off and regrading | **Partial** — sign-off works, regrade is in the wrong application. OD-041 |
| 5 | Initial and final outcomes separate | **Built and deployed** |
| 6 | Portal, account and role permissions | **Partial** — rebuilt on web roles, but role separation is untested |
| 7 | Route to first authorised page | **Not built.** OD-042 |
| 8 | Remediation alignment and styling | **Built and committed** — `93571bf` draws each fail item as its own row, `06380ec` gives the portal a remediation list that opens a case to its form |
| 9 | Reviewer assignment and submission rules | **Built** |
| 10 | Corrected solution in TEST | **Not started**, sequenced behind DEV sign-off by direction |

Should-haves: the notification mechanism is built, switched on and proved end to end with direct
case links; what is missing is business wording (OD-046). Drill-down is built. Dashboard
simplification is in flight. Defect recording has no mechanism at all (OD-045).

Later/backlog: Power BI and SFTP were closed as out of MVP scope by direction (AD-034); the pack
re-raises them **as backlog**, which is consistent rather than a reversal.

---

## 2. The nine decisions this raised

All are new; no existing OD covered any of them. Full text in `knowledge/decision-log.md`.
**OD-040 was resolved the same day it was raised** — see §3.2.

| OD | Question | Cost of leaving it |
|---|---|---|
| OD-039 | Subsection as a data-model level, or the AD-098 code mapping? | A code change in two languages per checklist restructure, permanently |
| ~~OD-040~~ | One remediation action per review, or one per failed item? | **Resolved 2026-09-10**: one per item, built in `d8ee4c4`, recorded as AD-107 |
| OD-041 | Where does the supervisor record the final regraded outcome? | A portal-only supervisor cannot complete the workflow the pack's §7.1 draws |
| OD-042 | The landing page rule, and the blanket dashboard grant | §10.3 and an acceptance criterion stay unmet |
| OD-043 | Initial and final as two series, or one effective grade? | §3.4 unmet; cheapest to decide before the dashboard change commits |
| OD-044 | Is "rejected for further action" a distinct status? | One of §6.5's six states cannot be told from another |
| OD-045 | Name the defect tracker | An acceptance criterion with no mechanism behind it |
| OD-046 | Notification templates and sender | §9.4's stated business input; the only thing between PP-15 and done |
| OD-047 | Multiple daily uploads; the newer IO extract | Unowned prose in the pack itself (§11.4, §12.3) |

---

## 3. What will fight the existing code

Ranked by cost, not by how the requirement reads. Each names the code the judgement rests on.

### 3.1 Subsections are presentation, not model — §4.1, §4.2, §4.4

The model is two levels: `al_Section` and `al_Question`, each carrying `al_displayorder`. There
is no subsection anywhere in `src/Entities/`.

AD-098 folds sections into the document's blocks by **section code**, in two hand-written
mappings — `formBlocks` in `app/src/features/reviews/checklistForm.ts`, and a matching `case` in
`OT-Review-Detail.webtemplate.source.html`. So:

- §4.2's explicit ordering **is** met for sections and questions, and is **not modelled at all**
  for subsections.
- §4.4's "add questions without changing application code" is true only *within an existing
  block*. Adding a subsection, or moving a section between blocks, is a code change in two
  languages plus a redeploy of both front ends.

AD-098 anticipated exactly this and left the door open: a parent-section column that the mapping
would then read instead. The bill is a schema change, a seed migration, both renderers rewritten,
and `checklistDocument.test.ts` rewritten with them. **Not a defect** — V8 renders correctly
today and is test-pinned. This is a decision about what the *next* checklist version costs.

### 3.2 Remediation was one action carrying a text list — §6.1 — RESOLVED AND BUILT

This was the first pass's headline finding and it is now history. Kept, rather than deleted,
because the *reason* it was hard is the reason the fix had to touch four other rules.

**The problem.** §6.1 asks that each relevant item show seven fields: the failed question, the
recorded response, the failure reason, reviewer comments, the required action, the original
outcome type, and positive context. AD-097 delivered that as **lines of text inside one action's
`al_description`**. Text lines cannot carry seven fields, cannot be filtered or counted, and
cannot be completed one at a time. AD-097 had recorded why it stayed one action, and it was not
a rendering reason:

> completion, sign-off and the OD-038 "remediation approved" gate all count actions, and
> splitting them would change what "done" means for the case

**The answer, built 2026-09-10 in `d8ee4c4` and recorded as AD-107.** One action per failed
item, coded `REM-<case>-<sequence>-<n>`. What settles it is the agreed form: it carries a
remedial action, an owner, a target date and a sign-off against **every numbered row**, and all
four are single-valued columns on `al_remediationaction` — so one action could draw the rows but
never let the adviser answer them separately.

**AD-097's warning was correct, and all three counting rules were changed in the same commit
rather than discovered afterwards:**

1. `CompleteRemediationPlugin.AnyOutstanding` — completing an action no longer completes the
   remediation. The case holds at `Remediation In Progress` until nothing is outstanding.
   Without it, the first item finished put the whole case in front of the T&C Manager.
2. `SubmitReviewPlugin.RemediationApproved` — now requires **every** action on the review to
   carry an approved sign-off, not any one. Without it, approving the first would open the
   OD-038 AQS gate on a file still unremediated.
3. The `Remediation assigned` notification is keyed on the **review**, so a submission raising
   five actions sends one email rather than five.

Replay safety is per item, actions raised before the change are migrated by the
`splitremediation` verb, and a review with no item list keeps its un-indexed code.

**What this leaves.** The seven fields §6.1 lists are now *expressible* per row; whether each is
actually rendered is a smaller, checkable question against the built form, and one this
assessment did not test field by field.

### 3.3 Approve and regrade are two applications — §7.2, §7.3

- Approval: a portal act by `AL Portal - T&C Supervisor`, through the AD-099 contact trigger
  column, rendered in `OT-Remediation.webtemplate.source.html`.
- Regrade: `al_RegradeCase`, reachable only from the Code App at `/cases/:caseId/recheck`
  (`app/src/app/router.tsx`), gated on `page.cases` plus `command.regrade`, sending
  `ExpectedRowVersion`.

§7.2 puts both in one list of supervisor actions. Today they span two front ends, two permission
systems and two licensing positions, and a supervisor holding only the portal cannot finish.

Building regrade into the portal is a **known** shape but not a free one: it would be the fourth
browser-side create on this site, and the first three — answers (AD-053), claims (AD-076),
sign-off (AD-099) — were each refused `90040106` whatever the permissions said, and each ended
as a trigger column plus a server-side plug-in. Note the alternative is legitimate: confirming
that regrading is a back-office act closes OD-041 with no code at all.

### 3.4 Landing routing has no failing case to fix — §10.3

`app/src/app/router.tsx` renders `DashboardPage` at `/` with no `RequirePermission` wrapper, and
`app/src/types/permissions.ts` seeds **every** role with View on `page.dashboard`.

Those two facts together are why this has never been reported: there is currently no role that
lacks dashboard access to be misrouted. The requirement only becomes real once one exists —
which is a business decision, not Delivery's.

The resolver is straightforward (first permitted nav entry, after permissions resolve, before
first paint, in the app and again on portal Home). **Withdrawing the blanket grant is the half
that changes meaning**, because it changes what the 52 seeded web role rules assert. OD-042.

### 3.5 The dashboard collapses initial and final — §3.4

Both grades are stored and neither overwrites the other, so §8.1 is met at the data layer. The
**presentation** collapses them: `useCaseDashboard.ts` reduces to `final ?? initial`, and AD-083
had the portal's five PP-17 cards do the same deliberately.

Showing both means a "which grade" dimension on the card aggregates **and** on the `?outcome=`
drill-down filter, so a card and the list it opens keep agreeing — the part that breaks quietly
if only one side is changed. It lands in the code being redesigned right now, so decide it
before that commits. OD-043.

### 3.6 One of the six remediation states cannot be expressed — §6.5

Five of §6.5's six exist as case statuses. The sixth does not: a rejected sign-off returns the
case to `AwaitingRemediation`, **the same option value** a first-time remediation carries
(`CaseLifecycle.cs`). Nothing in the status distinguishes a first attempt from a returned one.

The data does, though — AD-079 keeps `al_clockstartedon` for the current period and `createdon`
for the original, precisely so both are visible. So this may be answerable by rendering. A new
status value is the more literal reading and the more expensive one: the transition map on both
tiers, every list filter, every dashboard bucket. OD-044.

### 3.7 Every new portal-written field costs allowlist — §6.3, §7.2, §10.4

`Webapi/<table>/fields` is one list per table and governs **reads as well as writes** (AD-099).
Widening it for an adviser field widens it for everyone who can write that table. That constraint
is why sign-off routes through a contact trigger column rather than a direct create, and it
applies to every new field the pack asks for. Budget it per field, not per feature.

### 3.8 The newer Intelligent Office extract reaches further than the parser — §12.3

It touches the parser, the server-side validation (AD-077), the route derivation from "Tax check
required" (AD-093), and the fixtures.

Worth pairing with a real fix rather than treating as version housekeeping: the para-planner is
matched to a Contact **by full name**, because the case carries a name and an IO code and no
address (AD-082). Two J Smiths leave the row unrouted. If the newer extract carries an address,
that is the moment to add `al_paraplanneremail` — AD-082 says as much, and names the consequence
it is guarding against: sending a client's advice outcome to the wrong para-planner is a
data-protection incident. OD-047.

### 3.9 Approved templates will not fit string composition — §9.2

Bodies are composed as C# strings in `NotificationOutbox`. Business-supplied wording either gets
hard-coded — a plug-in redeploy per wording change — or needs somewhere to live that is not code.
Treat "where templates live" as part of OD-046 rather than as a later surprise.

---

## 4. Two things worth doing before TEST exists

Both get more expensive the moment a second environment holds the solution.

1. **OD-025 key rotation.** It changes the `PublicKeyToken`, so Dataverse sees a different
   assembly: 34 plug-in types, 25 Custom APIs and 14 SDK steps rebind. DEV-only is the cheapest
   this will ever be, and the register already records that history removal does not close the
   finding — only rotation does.
2. **The Power Pages import side.** Export from DEV, import into a second environment, confirm
   the site reconstitutes. It has never been run in either direction, and Microsoft documents
   Power Pages solution awareness as a preview feature "not meant for production use". That is a
   platform-owner call before it becomes the PROD promotion path.

---

## 5. The part the tests cannot tell you

Most of the branch is now committed. What remains uncommitted on
`fix/portal-signin-identity-username` is the **dashboard redesign alone** — `DashboardPage.tsx`,
its CSS, `tokens.css` and the untracked `dashboardShares.*` — plus `checklist-v8.md`,
`ContactsMigration.cs` and `OT-Review-Detail`. Both suites are green on that tree.

**The remaining gap is deployment, not commitment**, and it is the one thing in this document
that could make every other claim in it wrong in practice:

- **AD-106 and AD-107 are both plug-in behaviour changes**, and AD-106's own entry says in
  bold that it needs a push to take effect. Neither was verified against `Env_AQ_Dev` for this
  assessment — see the method note at the top.
- Until `registerall` has run against the current assembly, **DEV is applying the previous
  rules**: one action per review, and yes/no answers still becoming remediation lines. The
  repository, the decision log, the register and this document all describe the new ones.
- So the first thing to do with this document is not to read further down it. It is to push the
  assembly and watch one submit raise one action per fail item.
