# The outcome leaves the issue table, and a swallowed fault stops killing submissions — 2026-09-10

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`, environment
`d50d27e8-cb3b-e718-b6e2-30aa92d944aa`). Pushed by `svc.automate.aq` through
`plugins/OutcomeTesting.Registration` and `powerpages/Deploy-Portal.ps1`. Branch
`fix/portal-signin-identity-username`. Decisions AD-108, AD-109, AD-110.

## The reports

Project owner, 2026-09-10, across the day:

- "Tax check outcome: Insufficient evidence should not be in the fail/issue table as it is
  the outcome, and everything else highlights the outcome reason… Add an outcome column and
  show it there"
- "on the review page the form body's width should be the same size as the ot-card, ot-submit
  and ot-page_header"
- "I am trying to sub a case IO-SEED-AQS-01 … 'We could not submit this review. Nothing has
  been changed…' … The network error shows code 9004010D. Ensure all errors show non-generic
  messages and actual error messages"
- "Case edit now throws 'Something went wrong while processing your request'" — later narrowed
  by the owner to "when I change the Adviser on a case that is already in the remediation
  stage"
- "The app still shows the merged fields instead of how it is in the portal, ensure the
  remediation view on the app matches that of the portal", then "remove this text on both
  platforms", the status filter, and the case details block

## 1. The outcome was numbered as an issue (AD-108)

`Remediation.NonPassItems` listed every non-pass answer on a Pass/Fail or
Pass/Fail/Insufficient scale. Three of those questions record a *result* rather than
something to put right — Q-TAX-02, Q-FQ-01, Q-FQTAX-01 — so the top row of every Tax
remediation read `Tax check outcome: Insufficient evidence`, above the four fail points that
were its reasons.

The grade scale had been excluded for exactly this reason since remediation was first
raised; the outcome questions were missed. They are excluded now, and the outcome is shown
instead as an **Outcome** field beside Client, Adviser and Case.

No schema change and no change to what is stored: `Remediation.Describe` has written the
reason into the standing sentence of every description since the beginning
(`…submitted (Tax check: Insufficient evidence).`), so all four renderers read it back out
from there. Actions raised before today therefore display correctly with no migration.

**Backfill.** `dropoutcomeactions` deletes an already-raised action whose single item is an
outcome answer, and **strips the item list instead when it is the case's only action** — the
dry run found `REM-IO-SEED-TXA-05-1`, and deleting it would have left that case in Awaiting
Remediation with an empty worklist, the exact state the 2026-09-09 record was written about.
Nothing an adviser has responded to, completed, answered the form on, or that carries a
sign-off is touched. Run on DEV: **1 deleted, 1 stripped, 0 left alone**; a re-run reports
`0 to delete and 0 to strip, of 12 read`.

## 2. Why the portal could not say what went wrong (AD-110)

The submit refused with HTTP 400 and error code **9004010D, "A Common Data Service error
occurred"**, and the page could only answer "We could not submit this review… quoting status
400".

`PluginBase` caught only `FaultException<OrganizationServiceFault>`. Power Pages returns an
`InvalidPluginExecutionException`'s message to the browser and reports anything else as that
opaque code with no detail — so any other exception type, in any plug-in, on any write,
reached the user as a page saying nothing, and the templates' careful branching on
`PRECONDITION:` / `CONFLICT:` / `UNAUTHORIZED:` could never fire.

It now wraps whatever it catches as `UNEXPECTED: <plug-in> could not complete. <Type>:
<message>`. The stack is not in it — that goes to the trace log and `InnerException`, which
is where PP-16 wants it.

`PluginBase` had no test at all, its constructor being `internal` to an assembly signed with
a key the tests do not hold. It is `protected` now, and `FakeServiceProvider` lets the
pipeline's exception path be tested rather than assumed.

Client-side, the five portal handlers read their message out of the OData error body instead
of scanning raw JSON for a prefix — one of them had a quote-cutting workaround for exactly
the JSON tail that produced — and show what the server said where they cannot classify a
refusal. The Code App classifies `UNEXPECTED` alongside the rest, and its five write paths
no longer end in `.catch(() => …)` with the error discarded. An unclassified cause is still
withheld: `failures.test.ts` guards that with an internal address in its fixture.

## 3. What was actually failing (AD-109)

Every precondition passed on `IO-SEED-AQS-01`'s data, checked against the live environment:
route *AQS only* so BR-004 is satisfied with no Tax review, case *Assigned* and review
*Review In Progress*, all **36** mandatory AQS question versions in force answered by its 39
responses, grade *Insufficient evidence* recognised, and no existing `al_outcome` row. So it
was never a business refusal.

`NotificationOutbox.Queue` let a duplicate create fail on the `al_notificationcode` alternate
key and **swallowed the fault**. The platform aborts the whole transaction of a plug-in that
absorbs an `OrganizationService` fault and carries on — *"ISV code reduced the open
transaction count"* — the rule `OptionLabels` already records for its own metadata read.

It was harmless while a transaction only ever queued one notification: the duplicate arose on
a replayed submit, which is a fresh transaction, and losing that one was the point of the
catch. **AD-107's one action per fail item changed that.** `NotificationEmitterPlugin` fires
on each create and the outbox code is keyed on the *review* — deliberately, so four actions
send one email — so the second action's create collided inside the same transaction, the
catch hid it, and the platform killed the submit.

`Remediation.AssignUnassignedActions` walks the same loop, which is why the fault also
appeared on a case edit that changed the adviser on a case already in remediation.

The code is read before the row is written. A race between two transactions can still lose to
the key; that fault propagates rather than being absorbed.

**Collateral.** One of those failed edits left `IO-SEED-TAX-01` with no `al_advisername`
(`modifiedon 2026-09-10T07:34:08Z`). Owner therefore reads Unassigned on that case wherever
the name is the fallback. Not restored here — it is the owner's data and their call.

## 4. The two screens now draw the same form

The Code App drew the three adviser answers and the adviser sign-off as **columns on every
row**, plus Outcomes and Sign-off tables underneath. None of that is the form: the three
answers are the adviser's and are answered once per case, and the regrade and the
supervisor's decision belong to the case rather than to any one action.

They are the form's last block now, under the table, as the paper form and the portal draw
it. The table's columns are the portal's — No., issue, remedial action, owner, target date,
status, age, sign-off — plus **Mark complete**, which is the app's own write path and has no
portal equivalent. The details above it are the portal's four, laid out across the card:
Client, Adviser, Outcome, Case, with the case status as a badge beside the reference.

`remediationForm.ts` holds the rules — which action carries the answers, when every action
counts as approved, what the sign-off cell says — so the block cannot drift from the Liquid
without a test failing.

Two smaller things the portal already had right and the app did not: an unassigned action
falls back to the case's adviser rather than reading "Unassigned" against a case that plainly
has one, and the age is the BR-010 clock `workingDays.ts` could always answer.

Also removed on the owner's instruction: the status filter on the app's remediation page, the
checker's observation under the rows, and the footnote explaining the three answers. The
portal's no-item fallback used to print the whole `al_description`, which would have put both
removed texts straight back on screen; it draws a dash now, on both platforms.

## 5. The review form body's width

`.ot-checklist` held a 1000px centred column while `.ot-page__header`, `.ot-card` and
`.ot-submit` all take `.ot-page__inner`'s 82rem, so the form floated narrower than the summary
card above it and the submit card below it. Width is the page's to decide: the print rule
already drops the wrapper to the sheet, and `.ot-checklist table` is `width: 100%`.

## Deployment

| # | Step | Result |
|---|---|---|
| 1 | Plug-in assembly, `registerall <orgUrl>` | `updated pluginassembly: 7b51d0d1-…`, 25 Custom APIs, all members of `OutcomeTesting` |
| 2 | `dropoutcomeactions <orgUrl> --confirm <orgUrl>` | 1 deleted, 1 stripped, 0 left alone; re-run is a no-op |
| 3 | Portal, `Deploy-Portal.ps1 -OrgUrl <orgUrl>` | Upload succeeded; 14 of 14 table permissions written and verified |
| 4 | Code App, `npm run build` then `npx pa app push` | Built clean, pushed successfully |

Suites at the deployed commit: **plug-ins 585/585**, **app 332/332**, `tsc -b` clean.

## What is not proved here

The submit itself. Every precondition was cleared against live data and the mechanism that
was killing the transaction is gone, but **no submit has been put through the portal since
the fix**. The check that settles it is one review submitted by a signed-in checker. If it
still refuses, the page will now name the plug-in and the exception rather than the status
code, which is the difference this record is really about.

Two further notes:

- **Plug-in trace logging is off** in this environment (`plugintracelog` is empty), which is
  why the exception had to be found by reading rather than by reading a log. Worth switching
  to Exception level while anything here is still being chased.
- **The seed advisers have no contact rows.** The environment holds `Dev Account`,
  `Service Account` and `Sims Rad` only, so `Remediation.AdviserContact` correctly refuses to
  resolve `Seed Adviser 01`, `03` or `05` and 9 of 12 actions are legitimately unassigned. The
  name fallback covers it on screen; real assignment needs the contacts seeded.

## Watch out for

`npx tsc --noEmit` in `app/` exits 0 having checked **nothing** — the root `tsconfig.json` is
a solution-style references project with no files of its own. The real check is `npx tsc -b`,
which builds `tsconfig.app.json`. On 2026-09-10 `--noEmit` was clean while `-b` reported seven
errors in the same tree, including a missing import and three unused parameters.
