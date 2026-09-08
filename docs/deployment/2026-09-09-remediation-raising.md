# Deployment — the remediation loop had no beginning, and three portal repairs

Date: 2026-09-09
Target: `Env_AQ_Dev` (`org0b075da8`, environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Started as three reported UI faults. The third — "a case is in Awaiting Remediation but the
remediation page shows no cases for an account that now has remediation permissions" — was
not a permissions fault and not a page fault. **Nothing in this solution had ever created an
`al_remediationaction`.**

---

## 1. The gap, and why it read as a permissions problem

The report named an account and a role, so the obvious first move is the permission chain.
That is the wrong end. Queried before changing anything:

| Question | Answer |
|---|---|
| Does `IO-DEV-VERIFY-003` exist, in that status? | Yes — `5e93f514-…`, Awaiting Remediation |
| How many `al_remediationaction` rows does the environment hold? | **Zero.** Unfiltered fetch, no results |
| Is `Remediation Action - read all` deployed? | Yes, **Global** scope, Active |

A Global read permission over an empty table shows nothing to everybody. The page was
correct; there was nothing to show. Every role investigated would have been a dead end,
because the account's access was never the variable.

**How it stayed invisible.** `OutcomeRules.NextCaseStatusForAqs` and `NextCaseStatusForTax`
have always sent a non-pass case to Awaiting Remediation, so the *status* said remediation was
owed and the case lists agreed. Everything downstream was built and registered and waiting:
the adviser's response (FR-020, FR-021), `CompleteRemediationPlugin`, the T&C sign-off
(FR-023, BR-008), the BR-010 ageing clock, and `NotificationEmitterPlugin`'s
`Create of al_remediationaction` step. Each one was correct. None of them could ever fire,
because the row they all hang off had no author.

`app/src/features/cases/caseWorklistMapping.ts` had recorded the assumption in plain sight —
the next action for Awaiting Remediation read **"Raise remediation actions"**, a human task
with nowhere to perform it. `NotificationEmitterPlugin`'s own summary said it plainly too:
*"remediation actions are not created by any plug-in at all"*. It was written as a reason to
hang the notification off the table rather than off a command, not as a defect report.

## 2. What raises one now — project owner direction, 2026-09-09

Two triggers, given as direction:

> the checklist has a remediation action needed, and also any final outcome that isn't a pass
> triggers a remediation

Both map onto questions already on the deployed V8 checklist, and both are **mandatory Yes/No**,
so the flag is always answered by the time a review can be submitted:

| Discipline | Grade / result | Remedial flag | Fail observation |
|---|---|---|---|
| AQS | `Q-GR-01` → `al_outcome` | **`Q-FQ-03`** | `Q-FQ-02` |
| Tax | `Q-TAX-02` | **`Q-FQTAX-03`** | `Q-FQTAX-02` |

```
RequiresRemediation(outcome, flagged) => flagged || outcome != Pass
```

The flag is not a restatement of the outcome. A non-pass already demanded remediation and
still does; the flag adds the case the grade cannot express — a file the checker was content
to pass that nonetheless has something on it the adviser must put right.

An **unanswered** flag is read as No, never Yes. Both questions are mandatory so this is
unreachable through a submit, but defaulting an absent answer to a raise would invent
remediation nobody asked for — a mistake far harder to notice than a missing one, because the
case moves and an adviser is emailed about work that was never flagged.

### The routing conflict the two rules create

Combining them produces a case the old routing could not express: **a Pass that is flagged**.
Put to the project owner as two questions, and settled 2026-09-09:

- **A flagged AQS Pass goes to Awaiting Remediation, not Closed.** Raising an action decides
  the status. Closing it would leave an open action on a case in a terminal state — AD-057
  permits no transition out of Closed, so the adviser could never work it and the sign-off
  could never land. An open action always sits on an open case.
- **A flagged Tax pass is held for remediation before AQS**, rather than handing off to the
  queue. That is the reasoning OD-027 already applies to a Tax non-pass, for the same reason:
  AQS must not review a file with something on it still unaddressed.

The result is one uniform rule — *anything that raises remediation sends the case to Awaiting
Remediation* — which is simpler than the two rules it replaces.

`NextCaseStatusForAqs(outcome)` and `NextCaseStatusForTax(answer, aqsStillToCome)` keep their
signatures and delegate to flag-aware overloads with `false`, so every existing caller and
test is untouched and the change is additive.

## 3. `Remediation.cs`

Kept out of the plug-in for the reason `OutcomeRules` and `CaseTransitions` both record: the
arithmetic and the row's shape are worth testing without a Dataverse service, and a second
caller must not be able to raise an action that differs from this one.

- **Code** `REM-<caseRef>-<sequence>`, on the `al_remediationactioncode` alternate key — the
  replay device `al_outcomecode` already uses on the Outcome.
- **An existing code means do nothing at all.** An upsert would put the status back to Open
  and overwrite `al_adviserresponse`, so a replay would silently undo work the adviser had
  already done and restart their ten days. The existing id is returned instead.
- **Due date** = 10 working days, the starting day counted as day 1 (OD-018), resolved in UK
  local time. Mirrors `addWorkingDays` in `app/src/lib/workingDays.ts` deliberately — the date
  written here and the age the portal renders against it must be the same arithmetic, or a
  case reads as breached before its own due date. Bank holidays are not deducted, per OD-018.
- **Assignee** = the adviser, resolved from `al_advisername` by the same two-row query
  `NotificationOutbox.ParaplannerEmail` uses. The second row is what proves the match was
  unambiguous; a `TopCount` of 1 would return the first of two J Smiths and look certain. An
  unresolved adviser raises the action **unassigned** rather than not at all — the work stays
  on the worklist for a manager to route rather than being lost to a directory gap.
- **`al_clockstartedon` is deliberately left unset.** It holds the start of a period a
  rejected sign-off restarted (OD-018); until then the clock runs from `createdon`, which is
  what both the portal and `workingDays.ts` fall back to. Stamping it would make every action
  look like a reworked one.

Raised from `SubmitReviewPlugin.FinaliseReview`, inside the submit transaction, so a case can
never again reach Awaiting Remediation with nothing on the worklist to explain why. Both entry
points — the `al_SubmitReview` Custom API and the portal's trigger-column path
(`SubmitRequestPlugin`) — share `Submit`, so both raise it.

## 4. Three portal repairs in the same batch

- **Picking a case up now opens the checklist.** "Run checks" used to reload the queue,
  leaving the checker to find the row they were already looking at. The PATCH cannot carry the
  answer — the page writes a *contact*, and the review instance is created downstream by
  `ClaimCasePlugin` — so the id is read back after the write and the page navigates to
  `/review?id=…`. The lookup is narrowed to the queue's discipline and to an unsubmitted
  review, because a Tax-then-AQS case carries two. **A failed lookup is not a failed claim:**
  the case is assigned by that point, so the fallback is the refreshed list, never an error
  about work that was done.
- **"Save as PDF" is gated on submission.** It was offered on any review, and on any case with
  a review instance at all. The file that produced was a draft with no submission date on it —
  indistinguishable, once printed, from the record of a finished check. The review page now
  requires `al_submittedon`; the case page requires at least one submitted review.
- **"Assign a role" picks a person instead of typing an email.** An assignment is keyed on
  work email (AD-010), and a typo produced a row that matched nobody: the grant looked made,
  the person still had no access, and the mistake was visible only by reading the assignments
  table character by character. `UserPicker` gained a `field="email"` mode rather than a second
  person selector being written, so the case person fields and role assignments read one
  registry.

## 5. Deployment

| # | Component | Command | Result |
|---|---|---|---|
| 1 | Plug-in assembly | `dotnet run -- registerall` | `updated pluginassembly: 7b51d0d1-…`, 25 commands |
| 2 | Portal | `Deploy-Portal.ps1` | Upload succeeded 17.53s, **14 of 14** table permissions verified |
| 3 | Code App | `npm run build` then `npx pa app push` | Built clean, pushed successfully |

```
dotnet run -- registerall https://org0b075da8.crm11.dynamics.com/
  Plug-in assembly…
    updated pluginassembly: 7b51d0d1-f5a1-f111-b8dd-e4fade069307
  Done. Registered 25 command(s).
  All 25 Custom API(s) are members of 'OutcomeTesting'.
```

**No new registration was needed**, and that was checked rather than assumed before the code
was written. Both steps were already registered and Enabled:

| Step | State |
|---|---|
| `CustomApi 'al_SubmitReview' implementation` (Main Operation, sync) | Enabled |
| `NotificationEmitterPlugin: Create of al_remediationaction` (Post-operation) | Enabled |

The three changed web templates were verified **by querying the deployed content**, not by
trusting the upload — `Deploy-Portal.ps1` says in its own summary that on this site a
successful-looking upload is not evidence a component landed. `OT Review List` carries
`openChecklist`, `OT Case Detail` carries `submitted_reviews`, and `OT Review Detail` carries
the submitted-only gate.

### A stale `annotationid` makes every upload look half-failed

The run printed, mid-progress:

```
Updating table powerpagecomponent with record ID:f065878a-… FAILED due to
Entity 'powerpagecomponent' With Id = f065878a-… Does Not Exist
```

**Nothing was lost.** `f065878a-…` is the `annotationid` recorded in
`web-files/outcome-testing.css.webfile.yml` and the manifest. This site is on the enhanced
data model, where a web file's bytes live in a `filecontent` file column on
`powerpagecomponent` rather than in an `annotation` — there are no `.css` annotations in the
environment at all. `pac` tried the annotation path first, failed, and then wrote the file
correctly: `outcome-testing.css` (`a1000000-…050`) has `filecontent` `239f3d5d-…` and
`modifiedon` matching this deploy.

It is local drift, not a deployment fault, and it predates this work. Left alone deliberately:
a `pac pages download` rebuilds the id truthfully, and hand-editing it risks the upload path
this site has already been burned by twice (OD-034). Recorded because the message names a
FAILED update in the middle of a run that then reports success, which is exactly the shape of
thing that costs an hour the next time someone sees it.

## 6. The backfill, and the loop proving itself

`IO-DEV-VERIFY-003`'s Tax review was submitted 2026-09-08 and is locked, so there was no
submit left to raise anything. A case stranded that way cannot heal itself.

`backfillremediation` was added to `OutcomeTesting.Registration` for it — deliberately narrow.
It refused a case not in Awaiting Remediation, refused one that already had an action, took
every value from the case and its submitted review rather than from arguments, and repeated
the org URL after `--confirm` for the reason the verify modes state: it writes a real business
record and sends real email.

**It has since been removed** — see section 8.

```
PASS: raised REM-IO-DEV-VERIFY-003-1 (468f32ee-…) on IO-DEV-VERIFY-003.
  review    9c0b091f-… sequence 1, submitted 2026-09-08
  due       2026-09-21 (10 working days, submission day counted as day 1)
  assigned  Sims Rad
```

A second run printed `Nothing to do: REM-IO-DEV-VERIFY-003-1 already exists`.

**The PP-15 chain then proved itself, unasked.** `NotificationEmitterPlugin` fired on the
Create, resolved the adviser's contact, and the async drain delivered it:

```
Remediation required on case IO-DEV-VERIFY-003 → Simunye.Radingwana@ascotlloyd.co.uk → Sent
```

Emitter → outbox → drain → server-side email, on the first row the table has ever held.

The reporting account, `svc.automate.aq-dev`, holds Administrators plus the three
reviewer/remediation web roles, and the read permission is Global — so the action is visible
to it now. It is **read-only** there, because the response panel opens only for the assigned
contact. That is the page working as designed, not a residual permissions fault.

## 7. What this does not prove

**No review has been submitted since the assembly was deployed.** The automatic raise is
covered by unit test (429 pass, 14 new in `RemediationTests`, 9 new in `OutcomeRulesTests`) and
its Create-side half is proved by the backfill above — but the path from a live submit has not
been walked end to end.

The cheapest proof closes the part of the direction the outcome alone could never trigger: a
fresh case, claimed, answered with **"Remedial action required? = Yes" on an otherwise-passing
file**, and submitted. Per the 2026-09-09 direction that case must land in Awaiting
Remediation rather than Closed, and carry an action due ten working days out.

## 8. The backfill verb was removed the same day

A quality pass over the diff asked whether anything in it was already dead. The verb was: a
query for cases in Awaiting Remediation, Remediation In Progress or Awaiting Sign-off without
an action returned **one row, `IO-DEV-VERIFY-003`, and it now has one**. Nothing is stranded,
so the verb had no remaining input.

Leaving it would have kept its `AddWorkingDays` copy alive — a second description of the
BR-010 arithmetic that could not be shared with `Remediation.AddWorkingDays`, because the
plug-in assembly is `net462` and the tool is `net8.0`, so they cannot reference one another
and the `Microsoft.Xrm.Sdk` each binds to is a different assembly identity. Two descriptions
of a date rule that must agree with a third in `workingDays.ts` is the drift this codebase
keeps writing comments about.

It is one `git show` away in history if another environment ever inherits cases mid-remediation
— which is the only scenario that would want it, and not one that exists today.
