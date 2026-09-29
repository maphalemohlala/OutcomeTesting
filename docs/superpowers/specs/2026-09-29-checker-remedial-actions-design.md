# The checker writes the remedial actions; the adviser says whether they were performed

Date: 2026-09-29
Status: Design approved in conversation 2026-09-29; written spec awaiting review
Source: project owner change request, 2026-09-29
Supersedes: the half of AD-095 that makes the "Remedial action" the adviser's free-text response

## Problem

When a Tax or AQS check is submitted with failures, `Remediation.NonPassItems` builds the list
of things the checker marked down - every Fail or Insufficient evidence on a test point, every
remediable No, and every ticked File Quality fail point - and `Remediation.Raise` turns each one
into an `al_remediationaction` (AD-107). The **adviser** then writes the "Remedial action" for
each row on `OT Remediation`, stored in `al_adviserresponse`, and cannot complete a row while it
is blank.

The project owner's request reverses who writes it. The checker knows what was wrong and what
putting it right looks like, so the checker writes the remedial action, per fail point, before
submitting the check. The adviser receives those actions as recommendations and records, per
row, whether the action was performed.

## Decisions taken with the project owner, 2026-09-29

| # | Question | Answer |
|---|---|---|
| 1 | Where does the checker write the remedial actions? | A fail points list at the bottom of the review page, one remedial action per item. |
| 2 | Who answers "Action performed"? | The adviser, per row. |
| 3 | Can a row answered **No** still be completed and sent for sign-off? | Yes. The supervisor decides, and can reject it back for rework as today. |
| 4 | Is a remedial action mandatory for every listed fail point? | Yes. Submit is refused while any is blank. |
| 5 | Does the adviser keep a free-text box? | Yes, as an **optional note** beside Action performed. |
| 6 | Storage | A new column for the checker's text; the existing `al_adviserresponse` becomes the adviser's note. |

Approach rejected, and why:

- **Keep the checker's text in `al_adviserresponse` and add a column for the note.** Rows raised
  before this change would render unchanged, but the column named "adviser response" would hold
  the checker's words, and the server could not stop the adviser overwriting them: every portal
  write reaches Dataverse as the site's application user (AD-053), so a guard on that column
  cannot tell the adviser from anyone else. A write-once column set by the submit plug-in can be
  protected, because no legitimate update of it exists.

## Scope

In scope: the review page's new card; a pending-text column on the review and the portal
request that writes it; the submit gate and the stamping of each raised action; two new columns
on `al_remediationaction`; the Action performed column and note on all four renderings of the
remediation form; the completion rule; the response guard.

Out of scope: the remediation form's case-level answers (Client contact required?, Recheck
required?, Do the remedial actions change the advice?) and the sign-off panel, which are
unchanged; the Code App review page, which is read-only and does not gain the card; rewriting
actions already raised.

## Data model

### `al_reviewinstance`

| Column | Type | Purpose |
|---|---|---|
| `al_pendingremedialactions` | Multiline text (JSON), 100,000 chars | The checker's remedial actions while the review is open. An object keyed by the item's text exactly as `NonPassItems` produces it, value the remedial action. The key `__overall__` holds the single action for a remediation with no itemised failures. Cleared by the submit once applied. |

### `al_remediationaction`

| Column | Type | Purpose |
|---|---|---|
| `al_remedialaction` | Multiline text, 4,000 chars | The checker's remedial action for this row. Written once, on Create, by `Remediation.Raise`. |
| `al_actionperformed` | Choice: Yes / No | The adviser's answer. Values taken from the free tail of the `1209107xx` band at build time, checked against the existing choices. |
| `al_adviserresponse` | (existing) | Now the adviser's **optional note** on rows that carry `al_remedialaction`. Unchanged meaning on rows that do not. |

### `contact`

| Column | Type | Purpose |
|---|---|---|
| `al_remedialactionsrequest` | Multiline text (JSON) | The portal's trigger column, the same shape as `al_accountabilityrequest`: `{ reviewId, actions: { <item>: <text>, ... } }`. Cleared by the plug-in. |

All four columns go into the OutcomeTesting solution. The portal's Web API allowlists gain one
column each: `Webapi/contact/fields` gains `al_remedialactionsrequest` (written through the
existing Self-scoped contact permission), and `Webapi/al_remediationaction/fields` gains
`al_actionperformed` (written through the existing Contact-scoped action permission).
`al_remedialaction` is kept off the allowlist, so the portal cannot write it at all.

## Components

### 1. Review page card - `OT Review Detail`

A new card, **Fail points and remedial actions**, between "Who carries this fail" and "Submit
this review", rendered on an editable review and shown only while the check owes a remediation.

- **Rows** are the items the submit will raise, in the order `NonPassItems` produces them: each
  non-pass answer on a remediable scale as "question: answer", in section then display order,
  then each ticked File Quality fail point by its `al_name`, in display order. The outcome
  questions (`OutcomeQuestionCodes`) are excluded, as the server excludes them.
- **Overall row.** When a remediation is owed (outcome not a Pass, or "Remedial action
  required?" = Yes) but no item is listed, the card shows a single row labelled **Overall**,
  keyed `__overall__`.
- **Live.** The list is rebuilt client-side as answers and fail-point ticks change, from the same
  autosave events the page already dispatches. No reload.
- **Each row** has a required textarea. Text is saved on blur and after a short idle, by
  PATCHing `al_remedialactionsrequest` on the signed-in contact, carrying the whole map for the
  review. A row that drops out of the list keeps its text in the map, so ticking it again brings
  the words back; the submit ignores keys that are no longer items.
- **Opening state.** The saved map is rendered into a `data-` attribute from
  `al_pendingremedialactions`, and the textareas are filled from it.
- **Submit button.** Before the submit request is sent, the page refuses and names the first
  listed row with no text: "Write the remedial action for '<item>' before submitting."

The page's mirror of the item rule is a convenience. The server's list is the one that counts
(component 3), and a disagreement surfaces as a refused submit naming the item, not as a
missing action.

### 2. `RemedialActionsRequestPlugin`

Synchronous post-operation on Update of `contact`, filtered on `al_remedialactionsrequest`.
Same shape and guard as `AccountabilityRequestPlugin`:

- An empty value returns at once (the plug-in's own clear).
- Unreadable JSON is refused with the validation prefix.
- The guard: the review must be open (not submitted) and carry the calling contact as its
  checker - the rule `AccountabilityRequestPlugin` already applies.
- Writes the map to `al_pendingremedialactions` on the review, trimming each value and dropping
  empty ones, then clears the request column.

### 3. Submit - `SubmitReviewPlugin` and `Remediation`

- **Gate.** Where the submit will raise a remediation (now, or deferred to the AQS submit under
  AD-184), it computes the items with `NonPassItems`, reads the review's pending map, and refuses
  the submit if any item - or `__overall__` when there are none - has no non-blank text. The
  message names the item. This runs before anything is written.
- **Stamping.** `Remediation.Raise` gains a `remedialActions` map parameter. `RaiseOne` sets
  `al_remedialaction` from the map by the item's text (or `__overall__` for the un-indexed
  action). An action already raised is still returned untouched (replay safety, AD-107).
- **Tax deferred to AQS (AD-184).** The Tax review's items are raised at the AQS submit from the
  Tax review's own map, so the Tax checker's words reach the Tax rows. The Tax submit's gate
  checks the Tax map at the Tax submit, when the checker can still fix it.
- **Clearing.** The review's `al_pendingremedialactions` is set to null once its actions are
  raised, in the same write that clears `al_pendingaccountability`. A deferred Tax review keeps
  its map until the AQS submit raises its rows.

### 4. Remediation form - four renderings

`OT Remediation`, `OT Case Detail`, the Code App's `RemediationPage`, and `RemediationDocument`
(the PDF in the para-planner's email, AD-220) draw the same columns:

No. · Issue / fail reason · Remedial action · **Action performed** · Owner · Target date ·
Status · Age · Sign-off

- **Remedial action** shows `al_remedialaction`, read-only, on every row. On a row without it
  (raised before this change) it shows `al_adviserresponse` as before, and that row has no note.
- **Action performed**, on `OT Remediation`, for an action assigned to the signed-in contact and
  not Completed: a Yes / No radio pair and, underneath, a smaller **Note (optional)** textarea
  bound to `al_adviserresponse`. Otherwise it shows the answer ("Yes" / "No" / "—") with the note
  beneath in muted text when there is one. The other three renderings are always read-only.
- Group-heading `colspan` goes from 8 to 9 everywhere.
- The adviser's single form button (AD-128's `otRemediationRows`) sends `al_actionperformed`
  and the note on each request instead of the response text. Its pre-flight check becomes
  "Answer Action performed for issue N before signing off." After a sign-off, `markSaved()`
  disables the Yes / No and makes the note read-only, so the cell shows what was saved in
  place, and no reload is needed (AD-128).

A row without `al_remedialaction` that is still open keeps the old editable response cell and
the old completion rule, so no remediation already in flight becomes impossible to finish.

### 5. Completion - `CompleteRequestPlugin` / `CompleteRemediationPlugin`

- An action carrying `al_remedialaction` completes only with `al_actionperformed` answered.
  Yes and No both complete. The note is optional.
- An action without `al_remedialaction` keeps today's rule: a non-blank `al_adviserresponse`.
- Nothing else about completion changes: per-check `AnyOutstanding` (AD-114), the case moving
  to Awaiting Sign-off, the clock.

### 6. Guard - `RemediationResponseGuardPlugin`

- `al_remedialaction`: refused on any Update. It is set on Create by `Raise` and has no
  legitimate later writer. Refused on presence, for the reason AD-182 gives.
- `al_actionperformed` joins the columns frozen once the action is Completed, alongside the
  note; a rejected sign-off reopens the action (AD-125) and unfreezes both, as today.
- The step's filtering attributes gain both columns.

## Error handling

- Pending save fails (network, guard refusal): the card's status line shows the server's words
  and the textarea keeps the text; the next save carries the whole map again.
- Item text changes between saving and submit (for example a question retired and succeeded):
  the old key no longer matches, the submit is refused naming the new item, and the checker
  writes it again.
- An action raised with no matching text cannot happen after the gate. If one does (a direct
  Dataverse write), it renders "—" and the adviser can still answer Action performed.

## Testing

- **Plug-in unit tests** (`OutcomeTesting.Plugins.Tests`, run locally with
  `DOTNET_ROLL_FORWARD=Major`): the request plug-in's guard and trim; the submit gate - refused on
  a blank item, on a blank Overall, passed when complete, ignoring stale keys; `Raise` stamping
  each action and leaving an existing one untouched; the Tax-deferred path using the Tax map;
  completion requiring Action performed on new rows and the response on old ones; the guard
  refusing `al_remedialaction` updates and freezing `al_actionperformed`.
- **Portal template tests** in the style of `portal*.test.ts`: the card's placement and its
  hidden opening state; the request column; the nine-column header on both remediation
  templates; the Yes/No and note controls; the pre-flight message.
- **Code App tests**: mapping of the two new columns; `ActionsTable` drawing Action performed and
  falling back to the response on old rows; `tsc -b` clean.
- **DEV run-through**: submit an AQS check with fails and remedial actions → confirm each action's
  `al_remedialaction` → adviser answers one Yes and one No with a note → completion → sign-off.
  A Tax-then-AQS case confirms the deferred path.

## Deployment

DEV only (Env_AQ_Dev). Columns and permissions by solution import with
`--activate-plugins`, the plug-in assembly built in Release before `pushassembly`, the new
step registered and added to the solution, the two portal templates uploaded with
`--modelVersion Enhanced` after diffing against DEV, and the Code App built before
`pa app push`. Promotion to TEST is the project owner's call.

A decision-log entry (AD-225) records the change of ownership and the storage choice.
