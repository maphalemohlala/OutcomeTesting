# Remediation documents, para-planner letter and an open check form - design

Date: 2026-10-05. Owner direction, the same day:

1. "When a remediation is raised include paraplanner in email with pdf of checks and remedial
   actions." The para-planner gets a **separate email**, not a copy.
2. "Include a pdf of the checks whenever sending an email to advisers."
3. "Include the remediation actions on the case details if there are any." The Code App case
   page shows them. On the portal, the actions go into the PDF downloaded there ("Add it to
   the pdf that gets downloaded from the portal, and the one that goes out to advisers and
   paraplanners").
4. "Remove any parts of the form that are automatically greyed out or selected by default."
   Leave them open for users to select, and **allow any answer**: the server drops the
   matching refusals too.

The two open points in the design review were left at their defaults. The para-planner keeps
the existing "review submitted" letter as well, and a Pass with "Remedial action required? No"
closes with no remediation.

## Where things stand

| Area | Today |
|---|---|
| Para-planner email | One letter, `REVIEW-SUBMITTED`, on every submit. Carries `CompletedCheckPdf.Documents`: one PDF per submitted check, plus the "Remediation and escalation" PDF once any action is completed (`RemediationDocument.AnyCompleted`). |
| Adviser emails | `REMEDIATION-*` (raised), `CASE-PASSED`, `SIGNOFF-APPROVED-*`, `SIGNOFF-REJECTED`. None carries a document, unless an administrator ticked "attach completed check" on the template (AD-171). |
| Remediation letter timing | `NotificationEmitterPlugin` queues it on the post-operation Create of the **first** `al_remediationaction`. The outbox code is keyed on the review, so later actions find it already queued. |
| Check PDF | `CompletedCheck` draws the review page's printout. It deliberately leaves the remedial actions out (they were moved to `RemediationDocument` on 2026-09-24). |
| Portal review page | The "Fail points and remedial actions" card is rendered only while the review is `editable`. After submit, the remedial actions are on neither the page nor its "Save as PDF" (`window.print()`). |
| Portal case page | Already draws the whole remediation form inline. Its "Save as PDF" includes it. |
| Code App case page | Shows a link, "Remediation and sign-off →", and no actions. |
| Check form locks | Portal script: `lockOptions` takes Pass and Pass with issues off the grade, Pass off the file quality outcome, and decides "Remedial action required?" from the file quality outcome. `syncFailPoints` greys the fail-point boxes. Server: `ResponseGuardPlugin` refuses a contradicting grade, file quality outcome or remedial answer. `ResponseProgressPlugin` clears a contradicted outcome and writes the implied remedial answer. `SubmitReviewPlugin` refuses both outcomes again at submit. |

## The design

### 1. A separate para-planner letter when a remediation is raised

- A new built-in template, `REMEDIATION-RAISED-PARAPLANNER`. It is seeded and editable like
  the others.
  - Subject: `Remediation raised on case {reference}`.
  - Body: names the adviser and the grading, and says the checks and the remedial actions
    are attached. It has **no portal button**: the para-planner has no access to the case
    (BR-009, AD-020).
- `NotificationEmitterPlugin.QueueRemediationAssigned` queues it beside the adviser's letter.
  - Same event (`EventRemediationAssigned`) and same target (the review).
  - Occurrence `PARAPLANNER`, so it is its own outbox row. A replay still collides, and a
    later review on the same case still sends.
  - Addressed to `NotificationOutbox.ParaplannerEmail` (AD-228). A case without a
    para-planner email still queues the row, with no recipient. That is the outbox's existing
    behaviour, and the row shows up as Failed with a reason.
- Both remediation letters carry the attachments in section 2.
- The `REVIEW-SUBMITTED` letter is unchanged. On a submit that raises remediation, the
  para-planner therefore gets two emails.

### 2. Every adviser letter carries the checks

- `CASE-PASSED`, every `REMEDIATION-*` letter, every `SIGNOFF-APPROVED-*` letter and
  `SIGNOFF-REJECTED` attach `CompletedCheckPdf.Documents`. These are the same files the
  para-planner gets: one PDF per submitted check, plus the remediation form PDF once an action
  is completed.
- Done by passing the case to the outbox at queue time, the way `QueueWithCompletedCheck`
  already does. An adviser letter is queued through a single helper, so a later adviser
  letter cannot forget it.
- Building a document still never costs the letter (AD-164). A failure leaves a letter with no
  attachment.

**Timing.** The remediation letters are queued while the first action is being created, so a
document built then would list one action. `SubmitReviewPlugin` re-attaches the documents to
both remediation rows for the review after `Remediation.Raise` returns. The rows are still
Pending in the same transaction, so nothing has been sent.

`Remediation.AssignOpenActions` fills an adviser later, and by then every action exists. It
needs no re-attach.

### 3. Remedial actions in the check PDF and on the case pages

**Check PDF.** `CompletedCheck.Blocks` ends with a "Remedial actions" table for that check's
`al_remediationaction` rows, in raise order. The columns are No., fail point, remedial action,
owner, target date and status. The section is left out when the check raised none.

**Portal review page.** On a submitted review, a read-only "Remedial actions" section with the
same columns, read through a Liquid fetch of the review's actions. The browser's "Save as PDF"
then matches the emailed PDF. The editable card stays as it is for an unsubmitted review.

**Code App case page.** A "Remedial actions" panel below "Checks on this case", shown only when
the case has actions.
- It reuses `useRemediation` and `remediationMapping`.
- It sits under the same `PermissionGate resource="page.remediation"` as the existing link, so
  nobody sees more than they do today.
- The columns are the check, fail point, remedial action, owner, target date and status.

**Portal case page.** No change.

### 4. The check form is open

**Portal form (`OT-Review-Detail`).**
- No `lockOptions` calls, and `lockOptions` itself goes.
- No automatic tick of "Remedial action required?" (`impliedRemedial` and its sync go).
- No disabling of the fail-point boxes, and no "no fail points to record" note
  (`syncFailPoints` goes).
- The "Grade cleared" and "File quality outcome cleared" messages go.
- Controls are still disabled on a submitted, read-only review. That lock is the review being
  finished, not an automatic answer.
- The root cause question is still hidden on a Pass. It is hidden, not greyed out, and was
  not part of the request.

**Server.**
- `ResponseGuardPlugin` no longer refuses on `GradeRefusal`, `FileQualityRefusal` or
  `RemedialActionRefusal`.
- `ResponseProgressPlugin` no longer runs `ClearOutcomesContradictedByAnswer` or
  `ReconcileRemedialAction`. `ClearRootCauseOnPass` stays, for the reason above.
- `SubmitReviewPlugin` no longer refuses on `FileQualityRefusal` or `GradeRefusal`.
- The now-unused `ChecklistGating` members are deleted, with their tests.
  - `IsNoOrFail`, `IsInsufficient` and the gating-facts read stay only if something else
    still uses them.

**Unchanged.** Whether remediation is raised: `OutcomeRules.RequiresRemediation` (a grade
other than Pass, or "Remedial action required?" Yes). The remedial-action text is still
required for every fail point when remediation is owed (`RemedialActions.EnsureWritten`).

**Consequence, accepted.** A check graded Pass with "Remedial action required? No" closes with
no remediation, even when a point reads Fail. It is the checker's call.

A new decision, AD-231, records this and supersedes the gating rules of 2026-09-19 (item 10),
2026-09-22 and 2026-09-23.

## Testing

- **Plug-in unit tests** (`dotnet test`, `DOTNET_ROLL_FORWARD=Major`):
  - The para-planner letter is queued with its own code, address and template, and is not
    queued twice on replay.
  - Each adviser letter carries attachments.
  - The re-attach after raise lists every action.
  - `CompletedCheck` draws the remedial-actions table, and leaves it out when there are none.
  - Contradicting answers are saved and submitted without refusal.
  - `ResponseProgressPlugin` no longer clears or fills answers.
- **App tests** (vitest, `tsc -b`): the case panel renders actions, hides itself when there are
  none, and sits behind the gate.
- **Portal template tests** (`app/src/features/reviews/portal*.test.ts`):
  - No `lockOptions`, `impliedRemedial` or `syncFailPoints` remain.
  - The read-only remedial section is rendered on submitted reviews.
  - `portalGating.test.ts` is rewritten to the new rule.

## Rollout

DEV only (Env_AQ_Dev). The sequence:

1. Build Release and push the assembly.
2. Seed the new template.
3. Upload the portal with `--modelVersion Enhanced`.
4. Build and push the Code App.

Then prove it on DEV:

1. Submit a failing check with contradicting answers and confirm it saves and submits.
2. Read the `al_notification` rows: the adviser's and the para-planner's remediation letters
   both carry PDFs, and those PDFs hold the remedial-actions table.
3. Print the submitted review page and confirm the same table.
4. Open the case in the Code App and confirm the panel.

The DEV mailbox does not deliver, so the proof is the outbox rows and their attachments, not
received email.
