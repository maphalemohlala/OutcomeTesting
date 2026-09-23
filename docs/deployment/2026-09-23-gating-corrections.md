# The gating corrections, the header reversal, and a fix that had never been deployed

Written for: whoever picks this batch up next, or has to undo part of it.

**Date:** 2026-09-23
**Environments:** `Env_AQ_Dev` (`org0b075da8`) and `Env_AQ_Test` (`org37995f36`)
**Shipped:** plug-in assembly + the OT Review Detail web template. **No solution import.**

## Why no solution import

This batch changes no schema, adds no plug-in type and registers no step. The three new
classes — `ChecklistGating`, `ChecklistDocument`, `PortalSite` — are helpers, not `IPlugin`s.
The 24 changed files under `src/SdkMessageProcessingSteps/` are additive `StateCode` /
`StatusCode` stamps (the 2026-09-02 guard), not functional changes.

So the deployable surface is one assembly and one web template, and both were pushed
directly. `src/Other/Solution.xml` is still `1.0.6.0`, which is what TEST already holds — a
solution import would have needed a version bump and would have carried far more than these
changes.

| Artefact | DEV | TEST |
|---|---|---|
| `OutcomeTesting.Plugins.dll` (321,536 bytes) | 08:29:57Z | 08:30:43Z |
| `OT Review Detail` (229,974 → 228,640 chars) | 08:30:17Z | 08:06:15Z |
| Steps enabled afterwards | **55 / 55** | **55 / 55** |

Both pushed with the registration tool's `pushassembly` and `pushwebtemplate`, which target a
single component rather than `pac powerpages upload` — the full upload would have pushed the
whole local site config over each environment, and `powerpages/` is hand-authored rather than
a mirror.

## What changed

Three rules given on 2026-09-22 were corrected on 2026-09-23, and one rule from that day was
reversed. `AD-208`, `AD-209` and `AD-210` in the decision log carry the reasoning; in short:

- **The case header no longer freezes when the Tax check is submitted.** Both teams edit it
  throughout. The Tax team's own two route-deriving fields keep their existing rule.
- **The Tax fail points lock on `Q-TAX-02`** (the Tax *check* outcome, S-TAX), not on
  `Q-FQTAX-01` (the tax *file quality* outcome, S-FQTAX).
- **The AQS fail points lock when every AML and CRA point reads Yes**, rather than staying
  locked until a finding arrives.
- **"Remedial action required?" is a lock, not a default** — the answer the file quality
  outcome does not imply is disabled, refused server-side, and reconciled if the outcome
  moves under an answer already saved.

## The AML and CRA count is scoped to the review's own checklist version

Worth naming because it fails **open** and so would never have been reported.
`ChecklistQueries.AmlCraQuestionCodes` counts the S-AMLCRA questions in force **on the
checklist version the review was issued**, the way `ResponseGuardPlugin` already scopes
whether a section may be answered at all. Counting every S-AMLCRA question in the environment
instead would mean a later version adding a sixth point put a code in the expected set that an
older review could never answer — "all Yes" becomes unreachable and the AQS lock quietly stops
working, with nothing to see.

## The products report, and what it actually was

Reported as *"the products do not appear after submitting a check"*. It was not a defect in
the page: `ListOptionRules.AppliedByAssociation` and its caller — which keep `al_productids`
out of the `ColumnSet`, because Products is a many-to-many naming no column — existed **only
in the working tree**. They are in no commit before this batch, so no environment had ever
run them.

Without the guard, every header edit touching Products faulted on the READ
(`'al_OutcomeCase' entity doesn't contain attribute with Name = 'al_productids'`), and because
the submit button flushes the header first (`window.otHeader.flush`), the fault took the
submit with it. The products were never written, so there was nothing to appear.

**Proved on TEST after the push:** ticking one product on case 900000004 through the portal
wrote the association; confirmed against Dataverse, then removed again to leave the case as
found.

**Do not re-test this by eye.** The portal's render cache is eventually consistent (AD-094, up
to fifteen minutes): immediately after that write the page still showed the checkbox unticked
while Dataverse held the association. Verify against the Web API, not the page.

## Verification

| | DEV | TEST |
|---|---|---|
| Plug-in tests | 1,618 passed | (same assembly) |
| App tests | 1,006 passed across 77 files | (same build) |
| Portal e2e | **5 passed, 7 skipped, 0 failed** | **5 passed, 7 skipped, 0 failed** |

On TEST the suite ran against case 900000004's unsubmitted Tax review. The three gating specs
pass, including the answering script loading with no console error, and both managed-list
specs pass — the second of which is the Products tick list.

Checked directly on that review, all three corrected rules behave:

- `Q-TAX-02` unanswered → fail points **open** (20 boxes), note empty
- Remedial → **both** Yes and No open, because the outcome is unanswered
- AML and CRA rows on the Tax form → **0**, which is why the count needs its `> 0` guard

And on the two submitted TEST reviews, the server-rendered lock is right in both directions:
`Q-FQTAX-01` Pass locks Yes, `Q-FQ-01` Fail locks No.

## DEV, and the two things that were not what they looked like

DEV ran green in the end — **5 passed, 7 skipped, 0 failed**, the same shape as TEST — but
only after two wrong diagnoses, both worth recording because each would mislead the next
person the same way.

**"DEV holds no case data" was wrong.** A `$count=true` query returned 0 against an
environment that holds cases 900000001–900000005. Ask for the rows themselves
(`$select=...&$top=5`); do not conclude an environment is empty from a count.

**What was actually missing was a review, not a case.** All five DEV cases sit at
**Queued** (`120910583`) and `al_reviewinstance` was genuinely empty — a review is created
when a case is claimed or assigned, so there was nothing for `OT_REVIEW_URL` to point at.
Created without a browser:

```
callapi <devUrl> al_AssignCase TargetId=<caseId>   AssigneeEmail=svc.automate.aq@ascotlloyd.co.uk IdempotencyKey=<unique>
```

That made review `0c46f88a-2db7-f111-aaac-e4fade069307` — an unsubmitted **AQS** check on
900000001. Usefully it is the opposite discipline to TEST's Tax review, so between the two
environments both sides of the fail points rule are exercised in a browser.

**The portal session expires separately from `pac auth`.** `app/e2e/.auth/portal.json` is
its own browser session; `pac auth create` does nothing for it. Re-captured interactively
with `npm run e2e:auth`, signed in as the account the review is assigned to — the Review
Instance table permission is contact-scoped, so any other account opens it read-only and
every gating spec skips rather than runs.

### What DEV proved that TEST did not

With `OT_ALLOW_WRITES=1`, *takes Pass away without a reload when a finding is ticked*
passes on the AQS review — three runs, three passes. This is the **client-side** half
(AD-041): the rule runs on `change`, with no save landing. Confirmed by query afterwards —
the review still holds **zero `al_response` rows**, so the spec writes nothing despite the
flag's name, and DEV was left exactly as found.

TEST carries the **server-side** half instead: its two submitted reviews render the lock in
the raw HTML in both directions. Neither environment proves both, and they are different
failures — a page where only the script works shows a checker Pass for the moment before it
runs.

### The two specs that still skip on DEV, correctly

- *renders the lock server-side* skips because the review holds no stored finding — there is
  nothing that ought to be locked, so the absence of a lock is the right render. TEST covers
  this one.
- *takes Pass away…* skips unless `OT_ALLOW_WRITES=1` is set. Playwright's list reporter does
  not print skip reasons, and this one was briefly misread as "Pass is already locked" — the
  two skips look identical in the output and mean opposite things.

## Second pass: the three audit findings, and a fourth found closing them

The 2026-09-23 audit reported four findings. A was fixed and deployed in the first pass. B, C
and D were reported and left, and are fixed here. Closing them turned up a fifth thing, which
is not in the product at all but in the test harness.

| Artefact | DEV | TEST |
|---|---|---|
| `OutcomeTesting.Plugins.dll` (321,536 bytes) | 09:58:38Z | 10:00:28Z |
| `OT Review Detail` (228,640 -> 232,982 chars) | 09:59:28Z | 10:01:38Z |
| Steps declared in `src/`, present and enabled | **24 / 24** | **24 / 24** |
| ALL steps registered for the assembly, enabled | **55 / 55** | **55 / 55** |

**Two counts, because they measure different things**, and reading one as the other is how a
disabled step gets missed. `verifysteps` checks the 24 steps `src/SdkMessageProcessingSteps/`
declares - the repo's own claim about itself. The 55 is every step registered against the
assembly's 49 plug-in types in the environment, which is the one an import can quietly switch
off. Both were checked here; the first pass on this page recorded only the second.

The byte count is unchanged because the assembly is padded to a 512-byte boundary and the
change is small; the DLL is newer than the edited source, which is the check that matters.

### B - a lock is per option, and rebuild() made it per row (AD-212)

`rebuild()` read `querySelector('input[disabled]')` into ONE boolean and applied it to every
option it then drew. Right while a lock was all or nothing - a read-only form disables the row
entire - and wrong from 2026-09-23, when the gating rules started disabling a SINGLE option
and leaving the rest live. A checker whose checklist was reissued mid-answer got the row
redrawn with **every** option dead and could not answer the question at all.

Not caught by the gating tests, and would not have been: the two run in **different scripts**,
and nothing re-runs the gating rules when a row is redrawn under them.

### C - the page wrote to the server when somebody merely opened it (AD-213)

The worst of the three. Opening a review whose file quality outcome was a Pass and whose
"Remedial action required?" was simply **unanswered** ticked No and dispatched `change`, which
the autosave turned into a PATCH. **Reading a page answered a mandatory question and recorded
it against whoever opened it** - including someone who had come only to look.

`syncRemedialAction` now takes `userDriven`. The load path locks the forbidden option, says so
where it had to untick a stored answer, and returns before it can write. The repair was never
the page's job: `ResponseProgressPlugin.ReconcileRemedialAction` moves a contradicting answer
when the outcome is saved, and `ResponseGuardPlugin` refuses a new one.

### D - two round trips per save to answer a question that was already settled (AD-214)

The checklist-version read and the AML and CRA question count ran on **every** review, inside
the transaction holding a checker's save open. Every Tax review - which has no AML and CRA
section and never will - paid both to be told a section it does not have is not complete.

They now run only where that section has been answered, which returns the **same** answer:
with nothing answered, a non-empty expected set cannot be a subset of an empty clean set, and
an empty expected set fails the count. The new tests assert the round-trip **count**, because
the result was already right; what was wrong was the cost of reaching it.

### The fifth thing: DEV and TEST are different portals (AD-215)

Found by pointing a DEV session at TEST. They are genuinely separate sites -
`outcometesting.powerappsportals.com` and `outcometestingtest.powerappsportals.com`,
distinguishable by the Entra client id each redirects with. The TEST **site record** carries
DEV's primary domain, copied by an import (PRT-114), so the row cannot be used to tell them
apart. `e2e/.auth/portal.json` holds one session at a time, so running both suites means
capturing each in turn.

**`expectSignedIn` did not catch it.** Its host check passes: a Power Pages site with a local
sign-in page answers an unauthenticated request with `/SignIn` on its OWN host rather than
redirecting away, and that sign-in page still renders the Main Navigation landmark the second
check looks for. Every spec then failed on whatever it asserted first - *"the case list page
has an .ot-page__inner"* - which reads as a broken page rather than a signed-out one. The
guard now tests the path as well as the host, and says which `OT_PORTAL_URL` to re-auth
against.

### Verification of the second pass

| | DEV | TEST |
|---|---|---|
| Plug-in tests | 1,620 passed | (same assembly) |
| App tests | 1,011 passed across 77 files | (same build) |
| Typecheck (`tsc -b`) | clean | clean |
| Portal e2e | **6 passed, 6 skipped, 0 failed** | **not run - needs a TEST sign-in** |

DEV ran with `OT_ALLOW_WRITES=1`, so the dynamic gating spec is included. The deployed
template was confirmed **live rather than cached** by reading the raw review HTML back and
finding `userDriven`, `lockedValues`, `allLocked` and the load-time guard in it - the render
cache took it immediately this time, which AD-094 says cannot be relied on.

## Third pass: the Products catalogue, and why the selections never showed

Products held **four placeholders**. It now holds the **55 products** the project owner
gave on 2026-09-23, in DEV and TEST, created with `webapimany` in one connection.

The four placeholders are **deactivated, not deleted**: three TEST cases genuinely hold
them, and the review page already renders a held-but-no-longer-offered option ticked and
marked "(retired)". Deleting them would have taken those cases' history with them.

### Why the selected products were not showing

Three separate causes, which is why it looked intermittent:

1. **The save was faulting, so nothing was ever written.** This is `AD-210`, and it is the
   main one. `ListOptionRules.AppliedByAssociation` - the guard that keeps `al_productids`
   out of the `ColumnSet`, because Products is a many-to-many naming no column - existed
   **only in the working tree and in no commit**. Every header edit touching Products
   faulted on the READ, and because the submit flushes the header first, the fault took the
   submit with it. Deployed 08:29Z on 2026-09-23.
2. **There was nothing recognisable to select.** Even a save that landed could only record
   "Product 1 (placeholder)".
3. **The render cache lags (`AD-094`, up to fifteen minutes).** A correct save can still
   leave the page showing the box unticked, which looks identical to a save that did
   nothing. Verify against the Web API, never by eye.

And a fourth thing that is not a defect but made the other three harder to see: on an
**editable** review the header had no summary at all, only the tick boxes themselves. With
everything unticked there was nothing on screen to read as "no products selected" rather
than "this field is broken". The collapsed picker now **names what is ticked**.

Proved end to end on DEV after this deployment, by associating one product and reading the
review page back:

| | Result |
|---|---|
| Shut, cell height | **45px** - one line, summary reads "Offshore Bond" |
| Open, cell height | **329px** - capped and scrolled, not 55 rows tall |
| Search "bond" | 5 of 55 shown |
| Search "zzzz" | **1** shown - the ticked one, never filtered away |

### The three names that were normalised

Whitespace only, no words changed:

| As given | As created |
|---|---|
| `Lump Sum Allowance & Death Benefit Allowance ` (trailing space) | `Lump Sum Allowance & Death Benefit Allowance` |
| `Existing  ISA/GIA Fund Switch` (double space) | `Existing ISA/GIA Fund Switch` |
| `Personal Pension  - Stakeholder/GPP/ PP` | `Personal Pension - Stakeholder/GPP/PP` |

Say the word if any of those three was deliberate and it is one PATCH each.

### Deployed in this pass

| Artefact | DEV | TEST |
|---|---|---|
| `OT Review Detail` (232,982 -> 239,041 chars) | 10:33:12Z | 10:45:54Z |
| `outcome-testing.css` (65,237 bytes) | 10:33:25Z | 10:46:09Z |
| `OT Layout` (stylesheet `?v=22` -> `?v=23`) | 10:46:47Z | 10:47:34Z |
| 55 products created, 4 placeholders retired | yes | yes |

`OT Layout` reports 2,211 -> 2,156 chars, which is **not** content being lost: the file is
55 lines and the upload normalises CRLF to LF. Checked against TEST before pushing it
there, and the only difference from the live copy was the `?v=` bump.

### Portal e2e, both environments, for the first time

| | DEV | TEST |
|---|---|---|
| Portal e2e | **6 passed, 6 skipped, 0 failed** | **6 passed, 6 skipped, 0 failed** |
| App tests | 1,019 passed across 77 files | (same build) |

TEST needed its own `npm run e2e:auth` (AD-215). One sign-in covered both because the DEV
session was copied aside first - `e2e/.auth/` now holds `portal.dev.json` and
`portal.test.json` beside the live `portal.json`, so switching environments is a copy
rather than a sign-in. They are live sessions and the directory is gitignored.

**`pa app push` could not run**: `pa auth` reports the sign-in session expired and needs an
interactive login. The app CHANGE is committed and its tests pass, but the built bundle has
not been pushed.

## Fourth pass: the export name, and the app fold done properly

### Trail Light downloads (`AD-217`)

A Trail Light download is now **`DFALIN1_outcometesting_yyyy_mm_dd`**, exactly as given.
`DFALIN1` is the receiving end's name for this feed, so the filename is part of the
interface the same way `AD-039`'s twenty columns are.

Two things follow that are deliberate rather than oversights:

- **Underscores in the date**, where every other export stamps `stem-YYYY-MM-DD`. A
  convention is not improved by being made consistent with something it is not part of.
- **Two downloads on one day share a name.** The convention describes the day's file, not
  the click that made it; the browser suffixes the second copy.

The day is the **UK** day, via the same `ukToday` the case header uses. 23:30Z on 1 June is
already 2 June in London under British Summer Time, so a machine reading UTC would name a
**daily** feed for yesterday. There is a test at exactly that hour.

**One thing to decide.** The *Download filtered rows* control on the same page is a
deliberate SUBSET, and it now carries the feed's name too, because the instruction said
"the trail light exports". So a partial extract is indistinguishable from the day's feed
by its name alone. If anything downstream picks files up by name, that wants separating -
it is a one-line change and the test would come with it.

### The app's Products field (`AD-216`)

Reported as *"the app still shows the long list on the products"*, and right on two counts.

It had not been deployed - `pa auth`'s session had expired - but it would not have looked
much better if it had. The first attempt capped the height and used two columns, which is
**not what the portal does** and not what "do the same for the app" asked for. The portal
shuts the list behind a line naming what is ticked; the app now does the same, past twelve
options, with the same search box and capped panel behind it. One control, not two that
merely rhyme.

Below twelve it stays a plain list: the fold costs a click, worth paying only when the
alternative is scrolling past dozens of options to reach the next field.

`describeTickSelection` is pure and tested, because it holds the rule that makes the fold
worth anything - the summary **names** what is ticked rather than counting it. "3 selected"
sends somebody back into the panel to find out which three, which is the whole cost the
fold was meant to save. It reads in catalogue order rather than tick order, so the line
does not rewrite its own beginning as somebody works.

### Deployed in this pass

| Artefact | Where | When |
|---|---|---|
| Code App bundle (`index-EgQPVkaJ.js`, 611,261 bytes) | environment `d50d27e8` | 2026-09-23, `sourcetime=1790161060448` |

**Use the long play URL the push printed, with its `sourcetime`.** The short `/a/{appId}`
URL keeps serving the previous bundle after a push:

```
https://apps.powerapps.com/play/e/d50d27e8-cb3b-e718-b6e2-30aa92d944aa/app/5d9fc475-ee75-4386-917e-fc182307b0c2?tenantId=4abde4fc-68ae-44b4-8e80-b575a8c3d5b8&hint=0eeb1568-9283-489f-b209-f5e1f9fc0df2&sourcetime=1790161060448
```

`pa app push` needed `pa auth login` first, which is interactive - the CLI cannot renew
that session silently and says so plainly rather than failing oddly.

App tests: **1,030 passed across 77 files**, typecheck clean.

## Known-open

**The AQS all-Yes lock withdraws the Breach and Record Keeping reasons too.** The File Quality
fail points are one undivided block of twenty reasons across four categories, so locking it on
a clean AML and CRA section also removes reasons that have nothing to do with AML. A file that
passes AML and fails on record keeping has no reason to tick. Raised with a live instance —
TEST review `8cb7fb72`, `Q-FQ-01` Fail with AML and CRA 5/5 Yes — and **confirmed to stand**
(project owner, 2026-09-23). Recorded against `AD-209` rather than left in a chat log, because
it is the same shape as the defect the 2026-09-09 direction reversed.

**The AML and CRA points still offer N/A, and the owner said they offer Insufficient
evidence.** Checked on 2026-09-23: all five question versions read `120910008` (`YesNoNA`)
in **DEV and in TEST**, and `data/v8-seed/data.xml` agrees with both. So there is no seed
drift - nothing anywhere carries the change described on 2026-09-23 ("we've changed the
options to yes, no, and insufficient evidence"), and an earlier note in `checklist-v8.md`
saying it had been made in the environment was wrong.

It matters because of how the two scales fail. The gating rule asks whether every point
reads **Yes** and never which values are not a Yes, so it works on either - but a point a
checker marks **N/A** today leaves the AQS fail points **open for ever** on that file,
because "all Yes" can no longer be reached and nothing reports it. That is the same
fails-open shape as AD-211. Under the described scale the equivalent answer is Insufficient
evidence, which is visible in the grade.

Cost to close: one PATCH per question version per environment, and **nothing to migrate** -
there are no stored `al_response` rows against any AML or CRA question in either
environment. It is held for the project owner because it changes what a checker is offered,
not because it is difficult.

**The TEST portal e2e has not been run against this build.** TEST is a different host from
DEV (AD-215) and needs its own `npm run e2e:auth`. The plug-in and template were both pushed
to TEST and its 24 steps verified enabled, and the assembly and template are byte-identical
to the ones DEV proved - so what is missing is the browser-level confirmation on TEST, not
the deployment. TEST also currently has **no unsubmitted review assigned to the Service
Account** (all four were submitted earlier on 2026-09-23), so a case needs assigning before
the editable specs can do anything but skip.
