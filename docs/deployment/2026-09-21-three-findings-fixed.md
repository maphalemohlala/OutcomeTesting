# A dropped judgement, a panel shown too late, and a platform exception on the page

**2026-09-21.** Three fixes, deployed to **DEV only**, for findings F48, F49 and F50 raised
during UAT. Promotion to TEST and PROD is a separate decision.

One change is a plug-in (`OutcomeTesting.Plugins`). Three are web templates. Nothing in this
note changes a security rule, a role, or a privilege.

---

## A correction to F50 before anything else

F50 was recorded as *"a failed case can be graded with nobody carrying the fail"*, and said
that the fallback the portal panel promises does not exist. **That second half was wrong, and
the correction matters because it changes what needed fixing.**

The panel says:

> Left alone, the extract names the people the case itself does: the paraplanner for a File
> Quality fail, the adviser for an Advice Quality fail.

It says **the extract**, and the extract does exactly that.
`GenerateExportPlugin.IsAccountable` derives the pair whenever all four flags are false —
"nobody has said yet", not "nobody is responsible" — and
`app/src/features/cases/failAccountability.ts` is a deliberate client-side mirror of it, so the
case screen shows the same derived pair rather than four empty boxes. Both carry the project
owner's 2026-09-16 decision, and the retired OD-024 gate is documented in both.

So case 900000004 is **not** a Potential harm case with nobody carrying it. Its extract names
the adviser for the Advice Quality fail, by derivation.

**What was real, and is what this fix addresses:** the Tax checker on that case *used* the
panel and named somebody, and their answer was discarded. It is still sitting on the Tax review
in `al_pendingaccountability`, unread. That is worse than it sounds, because the derivation
does not name the same person the checker did — see below.

---

## 1. F50 — a checker's judgement was dropped when their leg deferred

`al_outcome` is created by `SubmitReviewPlugin.CreateOutcome`. At the moment a checker is asked
"who carries this fail", that row does not exist yet, so the portal parks the answer on the
review and the submit applies it to the outcome it creates.

On a **Tax-then-AQS** case the Tax leg defers: it stamps `al_taxoutcome`, returns the case to
the queue and creates **no outcome at all** (correct, and deliberate — project owner,
2026-09-20). So a Tax checker's parked answer has nothing to be applied to.

The AQS submit is the moment the outcome first exists — and it read `al_pendingaccountability`
from **the review being submitted** only. That is the AQS review, which has none. The Tax
checker's answer stayed parked and the export fell back to its derivation.

**The derivation names a different person.** For a File Quality fail it names the
**paraplanner**; the Tax checker on 900000004 had named the **adviser**. So the judgement was
not merely lost — it was silently replaced with an answer about somebody else.

### The change

`SubmitReviewPlugin.ParkedAccountability(service, reviewId, caseId)` now answers which review
holds the judgement belonging to this outcome:

- the review being submitted, whenever it has one of its own — so nothing recorded about
  *this* submission is ever overwritten;
- otherwise the most recently submitted active review on the **same case** that carries one.

Whichever it came from is the row that gets cleared afterwards, so a later regrade cannot
re-apply a judgement made about a grade that no longer stands.

### What was deliberately not decided here

Where **both** legs recorded a judgement, this leg wins and the other is left parked rather
than merged. Merging a File Quality pair from one leg with an Advice Quality pair from another
is a rule nobody has given, and inventing one would put a combination in the extract that no
checker chose. **If that case should merge, it needs a ruling.**

---

## 2. F49 — the AQS checker saw the Tax notes only after they no longer needed them

`OT Tax Notes` exists to put the Tax check's Q-TAX-03 note and Q-TAX-04 remedial text in front
of the two people AD-020 filters it away from: the adviser reading a remediation, and the AQS
checker reading the case.

On the review page the include was guarded by:

```liquid
{% if rv.al_submittedon and rv.al_reviewtype.value != 120910200 %}
```

`rv` is the review **being viewed**. So the panel appeared on an AQS review only once the AQS
checker had already submitted it — after the only moment it could have changed their
assessment.

The guard's stated reason was that the Tax note is still its author's to change before
submission. That reason is sound and is **fully preserved**: `OT Tax Notes` already restricts
its own fetch to `al_reviewtype eq 120910200` **and** `al_submittedon not-null`. The condition
was guarding the right thing on the wrong review. `rv.al_submittedon` is removed; the
discipline test stays.

This matters more since 2026-09-20, when a Tax fail began being carried into **one** combined
remediation raised after the AQS check — which makes the AQS checker the person the Tax checker
is writing for.

This is the third time this panel's wiring has defeated its purpose (audit finding 14 was the
same panel fetching the wrong question code), and for the same structural reason: an empty
answer is how it is told there is nothing to show, so it fails silently.

---

## 3. F48 — a malformed id put a platform exception on the page

`?id=not-a-guid` reaches a `fetchxml` `eq` on a uniqueidentifier column. Dataverse throws, and
Liquid renders:

```
Liquid error: Exception has been thrown by the target of an invocation.
```

above the page's own empty state. Nothing leaked and the page still rendered, but a platform
exception was put in front of a reader who can do nothing with it.

The id is now checked **by shape before the fetch**, because asking Dataverse is the thing that
throws: dashes and the braced form are stripped, every hex digit is removed, and what survives
must be empty with exactly 32 hex characters.

An id that fails is treated as **no id at all**, so the page falls into the state it already has
for arriving without one. That is deliberate over a message of its own: someone who mistyped a
link and someone probing for ids see the same thing, so neither learns whether an id was
malformed or merely unknown.

**Applied to three pages, not one.** F48 was found on `OT Case Detail`, but `OT Review Detail`
(`id`, and the claim-by-case `case`) and `OT Remediation` (`case`) take ids the same way and had
the same exposure.

---

## Verification

- **1,391 plug-in tests pass** (was 1,385; six cases added).
- **Proved by reverting, twice.** The first attempt was not good enough and is worth recording:
  five unit tests around `ParkedAccountability` stayed **green** with the call site reverted,
  because they proved the lookup and not that anything called it — the exact shape of the defect
  F50 describes. `TaxFailReachesAqsTests.The_tax_checkers_judgement_reaches_the_outcome_the_aqs_submit_creates`
  drives the real `Submit` through both legs and asserts the flags on the created `al_outcome`.
  With the call site reverted, that one test fails and only that one.
- **Deployed to DEV** after `-c Release`: 296,960 bytes, sha256 `f9fb47fb…`, and the push
  reported the same byte count.
- **F48 verified live** on all three pages as Simunye Radingwana: `?id=not-a-guid` renders the
  page's ordinary empty state with no Liquid error, on `case-details`, `review` and
  `remediation`. A well-formed unknown id still answers "Case not available", and a real case
  (900000003) still renders in full — the regression that mattered.
- **F50 verified live end to end** on case 900000006, driven through the portal: the Tax check
  failed with **the adviser** ticked for File Quality; the Tax submit deferred (case back to
  Queued, `al_taxoutcome` Fail, zero `al_outcome` rows, judgement still parked); the AQS leg was
  graded Pass with its own panel correctly hidden, and submitted. `OUT-900000006-2` carries
  `al_fqadviseraccountable: true` and `al_fqparaplanneraccountable: false`. That pairing is the
  proof: the derivation for a File Quality fail names the **paraplanner**, so a record naming
  the **adviser** can only have come from the carried-forward judgement. The Tax review's
  `al_pendingaccountability` is now null. The case reached Awaiting Remediation with
  `REM-900000006-2` reading "(Pass; Tax check: Fail)", so the combined-remediation behaviour is
  unchanged.
- **F49 verified live** on case 900000003's AQS review, confirmed by query to be
  `al_submittedon: null`, status Assigned: the panel now renders the Tax checker's Q-TAX-04 text
  ("Bold kept") while the AQS check is still open.

### Fixture changes made for verification

Case **900000006**'s Tax review was reassigned from Dev Account to Simunye Radingwana via
`al_AssignCase` (assignment `ASG-22c85650f8b4-eb9bf59216b5-1fae3cf32ea1`) so the F50 journey
could be driven through the portal by a signed-in tester. The case carries
`svc.automate.aq@ascotlloyd.co.uk` as its adviser email, so lifecycle mail from it reaches the
service mailbox rather than a person.
