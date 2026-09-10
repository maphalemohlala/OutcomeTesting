# The portal regrade, and a case no route could move — 2026-09-10

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Read through
`plugins/OutcomeTesting.Registration` (`fetch`) as `svc.automate.aq`.

**Status: built, 625 plug-in tests passing.** Deployment steps at the foot; results recorded there.

## 1. The regrade had no portal control at all (OD-041 → AD-111)

Signing off does not regrade. Approving every remedial action moves the case to Awaiting
Recheck, which is the state that exists so the T&C Manager can then set the final outcome,
and setting that outcome is what closes the case. Every mention of "Regraded outcome" in
the portal templates was read-only display; the only control anywhere was the Code App at
`/cases/:caseId/recheck`. A supervisor holding only the portal could therefore approve a
remediation and then have no way to finish the case they had just approved.

A Custom API cannot be invoked from a Power Pages page, so `al_RegradeCase` was never
reachable from the portal. This takes the shape the site's three previous browser-driven
writes settled on (AD-053 answers, AD-076 claims, AD-099 sign-off).

| Piece | What it does |
|---|---|
| `contact.al_regraderequest` (new memo column, in the OutcomeTesting solution) | The regrade as JSON `{outcomeId, finalOutcome, reason, expectedRowVersion}`, written by the supervisor onto their own contact row. |
| `RegradeRequestPlugin` (new; sync post-operation Update of `contact`, filter `al_regraderequest`) | Parses the request, **checks the contact's web roles server-side for `AL Portal - T&C Supervisor`**, clears the column, calls `RegradeCasePlugin.Regrade`. 10 tests. |
| `RegradeCasePlugin.Regrade` (extracted) | The regrade's rules, now called by both the Custom API and the portal path, so the two front ends cannot drift on the mandatory reason (AD-031), the preserved initial outcome (BR-007), the refusal to regrade an ungraded outcome, the audit event, or the close of a case at Awaiting Recheck. |
| `OT Remediation` | The control, beside the sign-off: the four BR-005 grades in BR-005 order, and a mandatory reason. Renders wherever the case carries a graded outcome; a remediated Tax-only case (AD-055) has no outcome row and gets none. |
| `Webapi/contact/fields` | `al_claimrequest,al_signoffrequest,al_regraderequest`. |
| `outcome-testing.css` | `.ot-form-submit__title`, `.ot-field__required`. |

The Code App's `/cases/:caseId/recheck` is untouched and still works. This adds a front
end; it does not move one.

Authorization is the contact's web role, not the caller's privileges: a Power Pages write
reaches Dataverse as the site's application user, so `InitiatingUserId` names the site and a
caller check would enforce nothing (AD-053). The control is deliberately **not** hidden by
role — hiding it in Liquid would look like access control and enforce nothing (NFR-SEC-01),
so it renders for anyone and a user without the role is refused on save.

## 2. IO-SEED-AQS-04 could not progress by any route (AD-112)

Reported as a BR-004 breach: the case is Tax-then-AQS, but the portal showed only the AQS
check and let the checker work it.

### What the environment said

| Question | Answer |
|---|---|
| `IO-SEED-AQS-04` route | **Tax then AQS** (`al_taxcheckrequired` = Yes, `al_taxteamdisposition` = Submit to AQS). The reporter was right. |
| Its review instances | **One: AQS, sequence 2, status Assigned, never submitted.** No Tax instance exists. |
| `IO-SEED-TXA-04`, for comparison | Tax, sequence 1 — correct. |
| `ROUTE-TAX-AQS` flags | `al_requirestaxreview` true, `al_requiresaqsreview` true. Correct. |
| Plug-in steps | All enabled, `SubmitRequestPlugin: Update of al_reviewinstance` included. |

`seedcases` creates that case as **AQS-only** (the `AQS` tag is `ROUTE-AQS`; the
Tax-then-AQS set is tagged `TXA`). It was allocated at seed time, which opened the AQS
instance, and only then edited so a Tax check was required —
`UpdateCaseDetailsPlugin.DeriveRoute` re-derived the route to Tax-then-AQS. The route
changed; the instance did not.

### Root cause

`ClaimCasePlugin.ResolveOrCreateReview` treated **the earliest unsubmitted review instance**
as the check that was due, and consulted `NextDiscipline` — which reads the route and is the
actual authority — only when no unsubmitted instance existed. So once any instance was open,
the route stopped governing the order.

Three consequences, compounding:

1. The Tax check the route now required was never created, so it appeared nowhere on the
   case and Review progress showed the AQS check alone.
2. The AQS check was open and fully answerable, and nothing on the page said Tax was owed.
3. The premature AQS instance already carried an assigned contact, so a claim was refused
   as *"another checker has already started checks on this case"* before `NextDiscipline`
   was ever reached — no Tax checker could pick the case up either.

**BR-004 itself was never breached in the data.** A reproduction test drives
`SubmitReviewPlugin.Submit` against this exact shape and confirms the AQS submit is refused
(*"This case requires a Tax check and none has been created yet"*), and the review in DEV is
still at Assigned — the reporter had worked the check, not completed it. The defect is a
deadlock, not a bypass: the case owed a Tax check, was not in the queue, and its only open
check could not be submitted.

### The fixes

| Piece | What changed |
|---|---|
| `ClaimCasePlugin.ResolveOrCreateReview` | Asks `NextDiscipline` **first** and confines the instance search to the discipline it names. A case carrying no `al_reviewrouteid` keeps the old behaviour, because `NextDiscipline` refuses a routeless case outright and would otherwise make pre-route cases unclaimable. 6 tests; 3 of them fail against the old code. |
| `OT Case Detail` | Review progress is one line per check the case **owes**, in BR-004 order, rather than one line per instance that exists. A required check with no instance shows as *Not started*; an instance the route no longer requires is still listed, because it is real work. A case whose Tax check is missing while its AQS check is open carries a note saying the AQS submit will be refused until Tax is in. |
| `outcome-testing.css` | `.ot-row--owed`, `.ot-note`, `.ot-note--blocked`. |

### Also checked, as asked: Tax → remediation → AQS

That route is sound. A Tax non-pass sends the case to remediation; an approved sign-off
returns it to `Queued` when `AqsStillOwed` says AQS is still owed (OD-038); the claim then
finds no *unsubmitted* instance — the Tax one is submitted and so excluded — reaches
`NextDiscipline`, which sees Tax submitted and returns AQS, and opens the AQS check. The
fix above does not disturb it, and the existing `RouteProgressTests` and
`ClaimCasePluginTests` cover it.

## 3. Nothing requeued a case whose route changed mid-flight (OD-048 → built)

The claim fix means a Tax checker *can* now pick up such a case — but only once it is back
at `Queued`, and a case edited mid-flight sits at `Assigned`. Project owner direction
2026-09-10: return it to the queue automatically.

`UpdateCaseDetailsPlugin.RequeueAfterRouteChange` runs after the save, and only when all of
this holds: the edit actually changed the route, the caller did not set the status
themselves, the case is at Assigned or Review In Progress, and **the discipline now due has
no open review instance**. It releases the active assignment (the row is preserved, never
deleted — AD-037) and moves the case to `Queued` through the AD-057 edge that already exists
for the Tax-to-AQS handoff, writing the reason into the case's history. A route change that
did not change what comes next leaves the checker holding their work.

`ClaimCasePlugin.TryNextDiscipline` is the ordering decision, extracted so `NextDiscipline`
and this share one copy of it; `NextDiscipline` keeps its three distinct refusal messages by
re-deriving the reason only on the path that is about to throw. 11 tests.

This does not repair `IO-SEED-AQS-04`, whose route changed before the rule existed — that
case needs its status set to `Queued` once, which is step 9 below.

## Deployment steps — run 2026-09-10, all green

`$U = https://org0b075da8.crm11.dynamics.com/`. One process at a time.

| # | Command | Result |
|---|---|---|
| 1 | `addmemocolumn $U contact al_RegradeRequest "Regrade request" 4000 "<description>" --confirm $U` | created `contact.al_regraderequest` (memo, max 4000) in solution OutcomeTesting |
| 2 | `pushassembly $U` | **run twice.** The first push sent `bin/Release`, which was stale at 179,200 bytes — the local build had been Debug. Rebuilt with `-c Release` and re-pushed: 184,320 bytes. **Check the byte count against a fresh Release build; the verb does not build for you.** |
| 3 | `registertype $U OutcomeTesting.Plugins.RegradeRequestPlugin` | plugintype `06f4c7e3-0ead-f111-aaac-e4fade069307` |
| 4 | `registerstep $U OutcomeTesting.Plugins.RegradeRequestPlugin Update contact 40 al_regraderequest sync` | step `4ecc30ee-0ead-f111-aaac-e4fade069307`, added to the solution |
| 5 | `setsitesetting $U Webapi/contact/fields al_claimrequest,al_signoffrequest,al_regraderequest --confirm $U` | `al_claimrequest,al_signoffrequest` -> `al_claimrequest,al_signoffrequest,al_regraderequest` |
| 6 | `pushwebtemplate $U a1000000-0000-4000-8000-000000000019 <OT-Remediation path>` | 68,857 -> 79,483 chars. The verb takes the **component id**, not the name. |
| 7 | `pushwebtemplate $U a1000000-0000-4000-8000-000000000015 <OT-Case-Detail path>` | 24,748 -> 30,169 chars |
| 8 | `pushwebfile $U a1000000-0000-4000-8000-000000000050 <outcome-testing.css path> text/css` | 44,213 bytes |
| 9 | `requeuecase $U IO-SEED-AQS-04 --confirm $U` | `120910584 -> 120910583 (Queued)` |

Step 9 uses a **new verb** added in this change. It moves one case back to the queue through
`al_UpdateCaseDetails`, so the hop is lifecycle-checked (AD-057 allows `Assigned -> Queued`)
and audited rather than being a silent direct write — `routecase` sets the route, not the
status, and `queueroutedcases` only covers Imported and Ready for Allocation. `Status` is
passed as the option-set **number**; the command rejects the label with
*"Status must be a whole number"*. The active assignment is deliberately left alone: a case
returned to `Queued` for the BR-004 handoff carries one too, and `ClaimCasePlugin` releases
it on the next claim.

### Verified after

| Check | Result |
|---|---|
| `RegradeRequestPlugin: Update of contact` | Enabled, Post-operation (40), Synchronous, filter `al_regraderequest` |
| `IO-SEED-AQS-04` | `Queued`, route **Tax then AQS** — a Tax checker can now claim it, and the fixed `ResolveOrCreateReview` will open the Tax check rather than attaching to the AQS one |
| Plug-in tests | 625 passing |

Power Pages serves web templates from a server-side cache a push does not clear, so steps
6–8 need the usual cache clear before the change is visible (see the 2026-09-09 note).
