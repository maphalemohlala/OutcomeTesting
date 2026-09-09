# Portal cache: change tracking on the portal tables, and no reload after submit — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Reads and writes through
`plugins/OutcomeTesting.Registration`; `pac` remains token-revoked.

## The report

After submitting a review, the reviewer was shown the review as editable with no recorded
answers, although every answer had been captured.

## What was wrong

Dataverse held the submission: IO-100001's AQS review, `al_submittedon` 2026-09-09T11:49:16Z,
`al_submitrequested` true, ten answers, case at Awaiting Remediation. No other review instance
was created or touched. The page was rendering from the Power Pages server-side cache: a
website write clears that cache only for the record it wrote, and every write that matters here
is made by a plug-in (`SubmitRequestPlugin` stamps `al_submittedon`, `AnswerWriter` creates the
`al_response` rows). The page reloaded 800 ms after the submit PATCH, so it re-rendered the
cached, pre-submit review: `al_submittedon` empty, so editable; cached answer set, so none.
Change tracking was off on every custom table, which is the condition Microsoft's Site Checker
guidance names for stale data on specific tables.

## The fix (AD-094)

| Where | Change |
|---|---|
| `src/Entities/*/Entity.xml` | `ChangeTrackingEnabled` 1 on the ten tables the portal reads (case, review instance, response, outcome, remediation action, question version, question, section, fail reason, review route) |
| `setchangetracking` verb | enables change tracking on named tables in a live environment and publishes them; idempotent |
| `OT Review Detail` template | on submit success the page disables its own controls and shows the confirmation in place instead of reloading, so the page in hand is right whatever the cache holds |

Also in this push, the two minors deferred from the AD-093 review: the lifecycle walk trusts
the status its caller just read (`CaseTransitions.MoveThrough(service, caseId, from, hops)`),
so queueing a case no longer re-reads the row per hop, pinned by a test; and the plan document
notes the post-fix-wave `CreateRoutedCase` signature.

Tests: plug-ins 537 passed, 0 failed.

## What ran

| Step | Command | Result |
|---|---|---|
| Assembly push | `pushassembly https://org0b075da8.crm11.dynamics.com` | 172032 bytes, modified 2026-09-09 12:14:59Z |
| Change tracking | `setchangetracking https://org0b075da8.crm11.dynamics.com al_outcomecase al_reviewinstance al_response al_outcome al_remediationaction al_questionversion al_question al_section al_failreason al_reviewroute` | all 10 enabled and published, read back enabled |
| Template push | `pushwebtemplate https://org0b075da8.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b "powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html"` | 54477 -> 56042 chars, modified 2026-09-09 12:16:23Z |
| Site cache | `/_services/about` → Clear cache, signed in with a web role holding all website access permissions | Project owner |

## Retest

Open a review assigned to you, answer the mandatory questions, submit. Expected: the page stays
put, every control goes grey, the button reads Submitted, and the status line confirms. Navigate
away and back: the review renders locked with its answers.

## Sign-off

| Step | Run by | Date | Outcome |
|---|---|---|---|
| Walker minor, template, verb, solution flags, tests | Delivery (automated) | 2026-09-09 | Pass — 537 tests |
| Assembly push | Delivery (automated) | 2026-09-09 | Pass — 12:14:59Z |
| Change tracking on ten tables | Delivery (automated) | 2026-09-09 | Pass — 10 of 10 |
| Template push | Delivery (automated) | 2026-09-09 | Pass — 12:16:23Z |
| Cache clear and retest | Project owner | | Pending |
