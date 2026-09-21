# Deployment — four change requests, 21 September 2026

Date: 2026-09-21
Target: `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`) **only**. Promotion to TEST or PROD is
the project owner's call and has not been made.
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Four requests, in the project owner's words:

1. "The Recheck required? and Do the remedial actions change the advice? should only be
   editable to T&C manager only"
2. "replace column B & D on the trail light exports with emails. rather than codes show emails"
3. "The check date has to be uneditable as it is automatically updated on submit"
4. "for tax -> Aqs, if a tax has remediation it should not appear on the Advisers queue and
   remediation page until the aqs check has been submitted"

Three of the four were built. **The fourth was already built** — on 2026-09-20 — and
confirming that found a hole in it, which is the change made under (4).

Two questions were put to the project owner before anything was written, because both had two
readable answers that lead to different files. Their answers are recorded with each item.

---

## 1. The two remediation-form answers belong to the T&C Manager (AD-182)

### What changed

`al_recheckrequired` and `al_changesadvice` move off the adviser's form and onto the sign-off.
The adviser keeps "Client contact required?", which is the only one of the three that was ever
theirs to judge.

| Piece | File |
|---|---|
| The refusal | `RemediationResponseGuardPlugin.TcOnlyRefusal` |
| The write | `SignoffRequestPlugin.RecordFormAnswers`, from two new payload fields |
| The origin test | `CommandHelpers.IsWithinMessageOn` |
| The adviser's grid | `OT-Remediation`, two cells now read-only |
| The T&C Manager's controls | `OT-Remediation`, two selects on the sign-off panel |

### Why it is enforced on the sign-off rather than by hiding a control

AD-053 is the whole reason. A Power Pages write reaches Dataverse **as the site's application
user**, so no guard on `al_remediationaction` can ask who the portal caller was — which is why
hiding the radios would have enforced nothing at all.

The sign-off is the one write on this site where the role is already proved: the page PATCHes
the signed-in user's **own contact row**, which the Self-scoped contact permission pins to
them, and `SignoffRequestPlugin` checks the T&C Supervisor web role server-side (AD-099). So
the answers travel with the attestation, and the guard's test becomes "did this write happen
inside a contact Update" — a shape a browser cannot produce, because a direct PATCH of a
remediation action has no parent context at all.

**T&C Manager and T&C Supervisor are one role and one person.** That is OD-020, resolved
2026-08-30; `webrole.yml` defines a single `AL Portal - T&C Supervisor`.

### One thing that is not obvious

`al_recheckrequired` was never decoration. `SignoffProgressPlugin.RecheckWaived` reads it to
decide whether the case **closes on this approval** or stops at Awaiting Recheck (AD-138). So
until today the adviser was deciding whether their own remediation needed looking at again.
That is the substance of this request, not the form layout.

It is also why `RecordFormAnswers` runs **before** the `al_signoff` is created: the progress
plug-in fires on that Create and reads the column, so writing the answers afterwards would
route the case on the previous answer.

### Two deliberate differences from the guard next door

- **Refused on presence, not on change.** The completion freeze beside it allows a write that
  restates the stored value, because that is what a dropped-response retry looks like.
  Allowing a restatement here would let the column be probed for what the supervisor answered.
- **Zero means "leave as recorded".** A rejection does not ask these questions and must not
  erase what an earlier approval attempt gave.

### Proved live in DEV

```
PATCH al_remediationactions(f528d4a6…)  {"al_recheckrequired":120910796}
  -> PRECONDITION: 'Recheck required?' and 'Do the remedial actions change the advice?'
     are the T&C Manager's answers and are recorded when they sign this remediation off.
PATCH al_remediationactions(f528d4a6…)  {"al_changesadvice":120910798}
  -> the same refusal
PATCH al_remediationactions(f528d4a6…)  {"al_clientcontactrequired":120910794}
  -> HTTP 204 No Content          (the control: the adviser's own answer still writes)
```

Made as `svc.automate.aq`, a **system administrator**, which is the point: there is no bypass.
The fixture was restored afterwards — all three columns back to null, status still Open.

The payload half was probed without side effects, by sending a request carrying both new
fields from a contact that does **not** hold the role:

```
PATCH contacts(ecdef71e…)  al_signoffrequest = {actionId, decision, recheckRequired, changesAdvice}
  -> PRECONDITION: Signing off needs the AL Portal - T&C Supervisor role on your portal account.
```

That refusal is the proof the extended payload **parsed** and reached the role gate — a parse
failure gives a different sentence. The contact's request column read back `null` with an
unchanged etag, so the refused write rolled back whole.

### What is NOT yet proved live

**A successful sign-off actually writing the two columns.** That needs a contact holding the
T&C Supervisor role and a Completed remediation action, and running it would move a UAT
fixture case through its lifecycle — which the F53 guard makes irreversible. It is covered by
six tests driving the real `SignoffRequestPlugin.Apply`, including the role refusal, the
ordering before the Create, and the scoping to one leg of a Tax-then-AQS route. **It is the
first thing a tester should exercise on the portal.**

---

## 2. Trail Light columns B and D are emails (AD-183)

### The question that was asked first

Column 21 held `Adviser Email`, appended on 2026-09-19. With column B carrying it, 21 only
repeats the value. Keeping it, dropping it and converting all six code columns were put to the
project owner; they chose **drop column 21**. The file is back to twenty columns.

### What changed

| Before | After |
|---|---|
| B `Adviser Code` | B **`Adviser Email`** |
| D `Paraplanner Code` | D **`Paraplanner Email`** |
| 21 `Adviser Email` | *(gone)* |

Positions are unchanged, so nothing downstream shifts. What changed is what B and D **mean**,
and a positional reader cannot detect that for itself — which is why it is in the decision log
and in the header comment rather than only in the code.

The four fail-accountable code columns (L, N, R, T) are **deliberately unchanged**. Only B and
D were asked for, and those four name a person picked out of a fail rather than the case's own
adviser and para-planner.

### Column D had to be resolved, not read

The adviser's email is on the case. **The para-planner's is not, and never has been** — the
import carries their name and nothing else (AD-160). So the address is resolved at generation
time from the Contact that name matches, through `NotificationOutbox.MatchParaplanner`, which
is the one place that decides and decides strictly: no name, no active contact of that name,
**two** active contacts of that name, or one with no work email all come back unmatched.

An unmatched name leaves the cell **empty**, with no fallback to the name or the code. AD-039
reads by position, so column D is "Paraplanner Email" on every row or the file lies about the
rows where it is something else — and a wrong address in a file that leaves this system is
worse than a blank one. A gap here is one the import report already raised on the day of the
upload.

### The new column

```
addtextcolumn <DEV> al_exportrecord al_ParaplannerEmail "Paraplanner Email" 320 …
  -> Created al_exportrecord.al_paraplanneremail (text, max 320) in solution OutcomeTesting.
```

In the solution, so it promotes. **It is not on the generated Code App model yet** — the
generator has not been re-run — so `trailLight.ts` declares it as an intersection, with a
type-level guard (`GeneratedHasParaplannerEmail`) that stops compiling the moment a
regeneration adds the column. That is what says "delete the hand declaration now", rather than
leaving it in place for ever. This is the AD-158 situation again: the runtime accepts a value
the generated types do not yet know about.

---

## 3. The check date is stamped by the submit, and editable by nobody (AD-181)

### The question that was asked first

The request gives a reason — "as it is automatically updated on submit" — that was **not true
when it was written**. The column was stamped on the **first answer saved** (project owner,
2026-09-19: "the check date should default to when someone starts checks on the case") and a
checker could correct it afterwards.

Three readings were put to the project owner. They chose **stamp on submit, latest submit
wins**: on a Tax-then-AQS case the Tax submit dates the case and the AQS submit re-dates it, so
the column holds the day the checking **finished**.

### What changed

- `ResponseProgressPlugin.StampCheckDate` — **removed**, with its call.
- `SubmitReviewPlugin.StampCheckDate` — new, called from `FinaliseReview` inside the submit
  transaction. Writes unconditionally.
- `UpdateCaseDetailsPlugin.Editables` — `al_checkdate` removed.
- `CaseHeaderRequestPlugin.CheckerEditable` — `al_checkdate` removed.
- `CaseEditPanel.tsx` — field removed, and removed from `CaseEditValues` so nothing can send it.
- `OT-Review-Detail` — the date box is now a read-only cell.

**Writing unconditionally is what overwrites a date the import carried.** That is deliberate,
and it is why the column had to come off the editable lists in the same change: a field on a
form that the next submit silently overwrites is worse than no field.

### Proved live in DEV

```
al_UpdateCaseDetails  Fields={"al_checkdate":"2026-01-01"}
  -> VALIDATION: Field 'al_checkdate' cannot be edited.
al_UpdateCaseDetails  Fields={"al_clientname":"…"}
  -> succeeded            (the control: the command still works)
```

The control edited case 900000003 and **was restored** — client name back to
`Test Client Charlie`, check date untouched at 2026-09-20.

The stamp itself is proved by test rather than by a live submit, because a live submit moves a
UAT fixture case irreversibly under the F53 guard. The test that covers it drives the **real**
`Submit` through both legs and asserts the date moves; removing the one line in `FinaliseReview`
reddens that test and nothing else, which was checked by actually removing it.

---

## 4. Tax → AQS remediation waits for the AQS submit (AD-184)

### This was already built, on 2026-09-20

`OutcomeRules.TaxRaisesRemediationNow` defers it, `SubmitReviewPlugin` raises **one** action set
at the AQS submit covering both checks, and `TaxFailReachesAqsTests` has asserted since then
that a Tax fail "returns the case to the queue and raises nothing". Nothing reaches the
adviser's queue or the remediation page, because no action exists to reach them.

So the honest answer to request (4) is: **already done, and here is the proof.**

### But one path through it was broken, and it lost work silently

A Tax submit defers `taxRequiresRemediation || remedialFlagged` — the **Q-TAX-02 verdict OR
the Q-FQTAX-03 tick**, which are two different questions. A checker can pass the tax outcome
and still tick "Remedial action required?".

`DeferredTaxFail` rebuilt the deferral from `al_taxoutcome` **alone**. So on that path:

- the Tax submit raised nothing, because AQS was still to come — correct;
- the AQS submit read `al_taxoutcome`, found a Pass, and carried nothing forward;
- the case closed clean, and **the adviser was never asked for the thing the Tax checker had
  flagged**.

It now reads Q-FQTAX-03 off the Tax review when the outcome asks for nothing. The reason reads
"Tax check: remedial action required" rather than the outcome's label, because telling an
adviser "Tax check: Pass" as the reason they have work to do says the opposite of what
happened.

One test reproduces it —
`A_tax_pass_that_still_flags_a_remedial_action_reaches_the_adviser` — and it failed with
`Assert.NotEmpty() Failure: Collection was empty` before the change.

---

## Repaired on the way past: two red app tests that predate this work

`checklistDocument.test.ts` had **two failing tests** before any of the above was written,
confirmed by stashing. They are fallout from yesterday's F56 seed fix (`7b0b2a3`), which
back-ported `Q-TAX-04 "Tax Remedial"` into `data/v8-seed/data.xml` and took it from 46
questions to 47. The test pins the seed against
`docs/reference/checker-checklist.html`, and the reference document has never carried Q-TAX-04
— it was added to DEV on 2026-09-19, after the document was supplied.

Recorded as a **fourth deliberate difference** in that test's own list, the way the three
existing ones are, and asserted by name and position so a *second* undocumented question cannot
hide behind it. The reference document is left as supplied, for the reason difference (3)
already gives: the provenance every other assertion rests on stays intact.

---

## What was deployed to DEV, and what was verified

| Step | Result |
|---|---|
| `addtextcolumn al_exportrecord al_ParaplannerEmail` | created, in solution `OutcomeTesting` |
| `pushassembly` | 299,008 bytes, `version 1.0.0.0`, re-pushed after the final source state |
| `pushwebtemplate` OT Remediation | 127,150 → 132,742 chars |
| `pushwebtemplate` OT Review Detail | 166,520 → 166,947 chars |
| `npm run build` then `pa app push` | bundle `index-Bv4XVqMa.js`, pushed with a `sourcetime` URL |
| Plug-in tests | **1,418 passing**, 0 failing |
| App tests | **801 passing**, 64 files, 0 failing |
| `tsc -b` | clean |

Step registrations were checked rather than assumed:

| Step | State |
|---|---|
| `RemediationResponseGuardPlugin: Update of al_remediationaction` | Enabled, stage 20, sync, filtered to all **five** attributes |
| `SignoffRequestPlugin: Update of contact` | Enabled, stage 40, sync, filtered to `al_signoffrequest` |

**All five filtering attributes on the guard are still required**, and now for two reasons: the
first three are the adviser's response it freezes at completion, and the last two are the pair
it refuses outright. A step narrowed to three would let the refused pair through unseen.

## Fixtures touched, and restored

| Row | Touched by | Restored |
|---|---|---|
| Case 900000003 `al_clientname` | the CR-3 control | yes, to `Test Client Charlie` |
| Action `REM-900000004-2-1` `al_clientcontactrequired` | the CR-1 control | yes, to null |
| Contact `ecdef71e…` `al_signoffrequest` | the payload probe | never written — the refusal rolled it back |

## Before anyone promotes this

- **TWO new columns must reach the target environment as metadata**, and they share a name:
  `al_exportrecord.al_paraplanneremail` (the Trail Light snapshot) and, from AD-186,
  `al_outcomecase.al_paraplanneremail` (the address the extract carries). Both are in the
  solution, so a managed import carries them; a hand-built environment will have neither, and
  column D will be blank for every row.
- **The extract must carry `ParaplannerEmail`.** An older file without that header still
  imports - every column is optional but TaskID - and the para-planner then falls back to
  being matched by name, which is the behaviour AD-186 replaced.
- **The Code App must be pushed wherever the export is produced**, because the column order
  lives in `trailLight.ts` and nowhere else. A stale bundle writes the old twenty-one-column
  file with codes in B and D, and the receiving system cannot tell.
- **A tester should sign a remediation off on the portal**, which is the one thing above that
  is proved by test and not live.
- Nothing here has been promoted. TEST still carries 1.0.5.0 and has **no case data** since
  the 2026-09-21 purge, so none of this can be exercised there until cases are imported.

---

## Follow-up the same day: the portal showed a stale checklist (AD-185)

The project owner retyped the five **File Quality - AML and CRA checking points** questions to
Yes / No / Insufficient evidence and reported that the portal still drew Pass / Fail /
Insufficient evidence, then: *"the changes should show immediately after they are made"*.

### The change was correct, and so was every renderer

All five questions carry a **v3** on `120910009`, effective 2026-09-21. Running the exact
in-force filter the renderers use returns five versions, all agreeing, none mixed. Both
surfaces handle that scale, and both were already taught that a section's **questions**
override its declared scale - using S-AMLCRA as the worked example. What the page was showing
was **v2**, a version that existed only from 19 to 21 September.

### Where the staleness is, and where it is not

- **The Code App is already immediate.** `useReviewDetail` reads Dataverse on mount with no
  cache, so a change shows the moment a review is opened.
- **A submitted review is pinned, correctly and permanently.** It is read as of its submission
  day (BR-013), so a review submitted on 20 September will always draw v2.
- **The portal was the problem.** Power Pages renders from a server-side cache and learns
  about a write through polled change tracking. Change tracking was re-verified enabled on
  `al_question`, `al_questionversion` and `al_section`; it makes the portal able to notice,
  not prompt about it. There is no site setting for it — the only cache settings on this site
  are `Header/OutputCache/Enabled` and `Footer/OutputCache/Enabled`.

### What was done

The review page now re-reads its own checklist through the **Web API**, which is not the
Liquid render cache — the same move `OT Remediation` already makes for its stale case-status
badge. Where a question has been re-versioned since the render, the page repoints the row at
the live version, redraws its options from the live scale, re-heads the grid where every row
agrees, and says what happened.

Three things it deliberately does not do:

- **It does not touch a submitted review.** Refreshing one would relabel answered history.
- **It does not keep a tick the new scale has no option for.** It clears it and says so;
  coming back to an empty row with no explanation is how a reviewer concludes work was lost.
- **It does not re-head a grid whose rows disagree.** Leaving the old heading beats inventing
  one, which is the same test the server makes.

`Webapi/al_questionversion/enabled` was turned on for it. **It grants nothing new:** the
`Question Version - read` table permission is read-only, and the Web API cannot exceed a table
permission.

### A claim this note made an hour ago, and the correction

**Retracted: "DEV's portal stylesheet was never pushed, and is missing yesterday's rules."**
That was written from a reading that does not support it. `curl` on
`https://outcometesting.powerappsportals.com/outcome-testing.css` returns **302 to
`login.windows.net`** — the DEV portal requires sign-in for web files — and following the
redirect lands on a Microsoft "Sign in to your account" page. Grepping that HTML for
`ot-due--overdue`, `ot-unassigned` and `ot-stale` returns zero for all of them, which says
nothing whatever about the stylesheet. The response size drifting between fetches (51,567 →
51,642 → 51,704 bytes) was the login page's own per-request tokens, not a stylesheet.

**What is actually known.** `pushwebfile` reported writing 58,238 bytes, and the
`outcome-testing.css` component reads back `modifiedon 2026-09-21T14:36:54Z` — so the file
reached Dataverse. Whether DEV was behind before that push is **unknown and now
unknowable**, and nothing here should be read as saying it was.

**Why the earlier TEST check was valid and this one was not:** the 2026-09-21 TEST note
fetched the same file from the TEST portal and got a real 200 with the three rules in it.
TEST serves that file anonymously; DEV does not. Two portals, two different answers to the
same command — worth knowing before anyone diagnoses a stylesheet by fetching it.

### Deployed to DEV

| Step | Result |
|---|---|
| `setsitesetting Webapi/al_questionversion/enabled` | `true`, created |
| `setsitesetting Webapi/al_questionversion/fields` | seven read columns, created |
| `pushwebtemplate` OT Review Detail | 166,947 → 181,754 chars |
| `pushwebfile` outcome-testing.css | 58,238 bytes |
| App tests | **814 passing**, 65 files |

### Not verified live, and why

**The Web API read itself needs a signed-in portal session, which this session does not
hold.** The configuration is right — the site settings read back, the table permission grants
read, and change tracking is on — but whether Power Pages serves
`/_api/al_questionversions` to a checker has not been exercised. Thirteen tests pin the
script's shape and the scale table against both rendering sources, and a deliberate drift was
injected into `OT Answer Options` to prove they catch it. **The first tester on the portal
should open an unsubmitted review, have someone retype a question, and reload.** If the Web
API is refused, the page simply stays as it renders today — the check fails quietly by design.
