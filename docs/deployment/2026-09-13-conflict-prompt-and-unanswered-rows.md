# The conflict prompt that never showed, and the unanswered rows marked where they are — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Project owner, 2026-09-13, two reports: updating the adviser on case 254398988 failed with
"UpdateCaseDetailsPlugin could not complete. OrganizationServiceFault: The version of the
existing record doesn't match the RowVersion property provided"; and submitting a review
with a required field empty gives a generic message that changes nothing on the page
instead of marking the missing fields.

---

## 1. The refusal was right; the message was the defect

Case 254398988 was changed at 16:49:16 by the portal runtime — its second review was
submitted at 16:49:14 and that moved the case to Awaiting Remediation. The case page in the
app had been loaded before that, so the adviser edit carried the earlier version number
and `al_UpdateCaseDetails`' optimistic-concurrency check refused to overwrite a case that
had moved on. That is the check doing its job.

What it should have said is "CONFLICT: This case changed since you loaded it. Reload and
try again", which the app renders as its reload prompt. `CommandHelpers.IsConcurrencyFault`
missed the fault on both of its tests: it compared the error code against **0x80060892**
where ConcurrencyVersionMismatch is **0x80060882**, and its text fallback looked for
"row version" with a space where the platform writes "RowVersion". So the fault fell through
to the UNEXPECTED wrapper and the raw sentence reached the screen. The same matcher guards
every command that takes an expected row version, so every genuine edit conflict in the app
has read as an unexpected failure until now.

Corrected code, spacing-insensitive text, and three tests on a constructed fault.

## 2. The unanswered rows, named and marked

`SubmitReviewPlugin` refused with "Complete all questions marked Required before submitting.
3 of 42 required questions are unanswered." — a count, under the button, leaving the checker
to scan forty-two rows. The refusal now names the questions by code and carries their
question version ids in a bracketed tail:

```
PRECONDITION: Complete all questions marked Required before submitting. 2 of 42 required
questions are unanswered: Q-E1-02, Q-GR-01. [unanswered:<id>,<id>]
```

`OT Review Detail`'s submit handler reads the tail, strips it from the text it shows, adds
`ot-answer--missing` to each row that carries one of those ids in `data-question-version`,
writes "Required - not answered" into the row's own status line, and scrolls the first into
view. The autosave clears a row's mark when its answer next saves. The mandatory query is
ordered by display order, so the codes read in form order. Presentation only: the plug-in
still decides (NFR-SEC-01).

The stylesheet gains the row rule and `OT Layout` links `?v=13`, as the layout's own comment
requires on every stylesheet change.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins) | **809 passed** (5 new) |
| 2 | `dotnet build -c Release` → `pushassembly` | **222,208 bytes**, matching the local build, 17:00:45Z |
| 3 | `pushwebfile … outcome-testing.css` | `a1000000-…-050`, **46,190 bytes** |
| 4 | `pushwebtemplate … OT Layout` | `a1000000-…-010`, `?v=13` |
| 5 | `pushwebtemplate … OT Review Detail` | `a1000000-…-01b`, **95510 → 97876 chars**; Liquid pairs balanced, nesting walked, 4/4 scripts |

## Still open

- **Not seen in a browser.** A submit with required answers missing is the proof for §2; an
  edit to a case another user has just moved is the proof for §1. Neither has been run since
  the push.
- The adviser edit on 254398988 still has to be made: reload the case page and re-apply it.
