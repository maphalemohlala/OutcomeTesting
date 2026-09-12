# In-app checklist administration — design

Status: approved design, pre-implementation
Date: 2026-09-11
Requirements: FR-030, FR-031, BR-013, PP-09
Decisions applied: AD-003, AD-015, AD-016, AD-041, AD-043, AD-091, AD-098
Decisions raised: AD-122 (question administration; amends AD-016 and PP-09),
AD-123 (section effective dates, optional sections, and a Both owner role; amends AD-019
and AD-020)

## 1. Purpose

An administrator can change a question's wording today, and nothing else. Adding a
question, taking one out of service, or changing anything at all about a section is a
data operation performed outside the application: an edit to `data/v8-seed/data.xml`
followed by `pac data import --data data\v8-seed`, which needs the `pac` CLI and
environment access that the people who own the checklist do not have.

This design makes the checklist administrable from the Question library:

- **Questions** — add, retire, move between sections, and edit through one modal
- **Sections** — add with their questions, retire, set optional or required, and set the
  team that owns them: Tax, AQS, or both

It does not change how a review is answered, graded or exported.

## 2. What the model already guarantees

Read before designing, and it decides most of what follows.

| Fact | Evidence |
|---|---|
| Every question read path filters versions by effective date | `ResponseRules.IsVersionEffective`; the `SubmitReviewPlugin` mandatory query; portal FetchXML at `OT-Review-Detail.webtemplate.source.html:294` |
| A review is bound to one checklist version for its whole life | `al_reviewinstance.al_checklistversionid`, set once at claim by `ClaimCasePlugin.ResolveChecklistVersion` |
| `statecode` is deliberately not consulted anywhere | AD-091 — a retired version stays Active so the answers it holds keep resolving |
| Wording, response type, mandatory and display order are **version**-scoped | `al_questionversion` carries all four (AD-015) |
| Section is **question**-scoped, and is not on the version at all | `al_question.al_sectionid`; `al_questionversion` has no section column |
| Question codes are unique | alternate key `al_questioncodekey` on `al_questioncode` |
| Mandatory answers are scoped to the section's owner role | `SubmitReviewPlugin` links `al_section` and filters `al_ownerrole` (AD-020) |
| **A new section renders in both front ends with no code change** | `formBlocks` falls through to a generic block at `checklistForm.ts:589`; the portal assigns `blk_title = sname` and `layout = 'inline'` before its dispatch chain, at web template line 511 |
| **`al_isconditional` is dead metadata** | Read in exactly one place, `useQuestionLibrary.ts:101`, to draw a chip. No renderer and no plug-in consults it |
| `al_section` carries no effective dates | Its only columns are code, name, help text, owner role, conditional flag, display order and the checklist-version lookup |
| `al_ownerrole` has no "Both" value | Its values are Tax team (120910100), AQS checker (120910101), Adviser, T&C Manager, Manager / Admin |
| **Owner role is matched by strict equality in five places** | `ResponseGuardPlugin` (refuses the answer outright), the `SubmitReviewPlugin` mandatory query, two portal FetchXML conditions at web template lines 323 and 356, and `useReviewDetail.ts:238` |

Two consequences shape the design.

**Dating a version out is a complete, audit-safe removal; changing a question's section
is not.** The first is scoped to a version, so frozen history keeps pointing at what it
always pointed at. The second applies to every version ever written, so it re-files
historic answers under a section they were never answered in.

**Sections have no equivalent of that protection**, because they have no dates. Giving
them dates is what lets a section be retired without rewriting what a submitted review
was answered against.

## 3. Decision: change the version in force, do not reissue the checklist

AD-016 states that a published checklist version is structurally immutable, and that
`al_QuestionVersion` exists to cover "in-flight corrections to a single question without
reissuing the whole checklist". Read strictly, adding or retiring anything means issuing
V9.

That reading is rejected here, and AD-122 records why. AD-016's concern is the rewriting
of history, and effective dating answers that concern completely: nothing a submitted
review said can change when a row is added carrying an `al_effectivefrom` of today,
because every read path is date-scoped and a submitted review reads as of its submission
day (AD-091). Reissuing a version would mean cloning roughly a hundred rows, with tooling
that does not exist, to obtain a protection the date columns already give.

What in-place change does cost is real, and is designed for rather than dismissed: a
**mandatory** question in force today is owed by every unsubmitted review of that
discipline the next time a reviewer submits. That is `SubmitReviewPlugin` reading
versions in force *now*, which is correct behaviour and not a defect. The control is the
effective-from date, which every command here exposes to the administrator. Dated
forward, in-flight reviews finish against the set they started with.

## 4. Schema changes

Three columns on `al_section`, and nothing else anywhere.

| Column | Type | Required | Meaning |
|---|---|---|---|
| `al_effectivefrom` | datetime, date only | no | In force from this day. Null means in force since always, so existing rows need no backfill |
| `al_effectiveto` | datetime, date only | no | Retired from this day. Null means still in force |
| `al_isoptional` | bit, nullable | no | Its questions are not owed at submit |

Nullable dates are deliberate: `ResponseRules.IsVersionEffective` already treats a null
`from` as in force, so the 12 existing sections keep working untouched and no data
migration is needed.

**`al_isoptional` is null on every existing row, not false.** Dataverse applies a boolean
column's default to new rows only; it does not backfill. Every reader in this design must
therefore treat *absent* as required — which is the safe direction, and is pinned by a
test. The trap it leaves for later is a query written as `al_isoptional eq false`, which
silently matches none of the 12 sections. Filter on `ne true`, or judge it in memory.

`al_isconditional` is left alone. It is dead today and this design does not revive it;
conditional display needs a condition model the schema does not carry, and that is
separate work (section 14).

### 4.1 A "Both" owner role

`al_ownerrole` gains one value, **Both (120910105)**, so a section can be owed by the Tax
and the AQS review alike. The domain already contains the idea: AD-020 records the File
Quality fail points as owned by "Both — each team ticks against its own File Quality
outcome".

A Both section is rendered to both disciplines and answered separately by each, because
responses hang off the review instance, not off the section. Each team answers its own
copy; neither sees the other's.

This is the most invasive change in the design, because owner role is currently matched by
**strict equality** in five places, and every one of them must become set membership —
the discipline's own role, or Both:

| Reader | Consequence of missing it |
|---|---|
| `ResponseGuardPlugin` | Every answer to a Both section is refused as "another discipline's section" |
| `SubmitReviewPlugin` mandatory query | A mandatory Both section is never owed, so submit passes with it blank |
| Portal FetchXML, web template lines 323 and 356 | The section does not render in the portal at all |
| `useReviewDetail.ts:238` | The section does not render in the Code App |

`ResponseGuardPlugin` is the dangerous one: change only the renderers and a Both section
appears correctly and then refuses every answer typed into it.

## 5. Question commands

Three new Custom APIs, each following `al_RetireAndSucceedQuestion` exactly: a JSON
contract in `plugins/customapi/`, a plug-in in `OutcomeTesting.Plugins`, a typed wrapper
in `app/src/services/commands/questions.ts`. Each checks the caller, writes an immutable
Audit Event, and replays through `CommandHelpers.FindAuditByKey` so a double-click cannot
write twice.

| Command | Audit value | Effect |
|---|---|---|
| `al_AddQuestion` | 120910793 | Creates `al_question` + `al_questionversion` v1 |
| `al_RetireQuestion` | 120910794 | Stamps `al_effectiveto`, creates no successor |
| `al_MoveQuestion` | 120910795 | Retires in the old section, adds in the new, one transaction |

`al_RetireAndSucceedQuestion` keeps its purpose and gains one optional input,
`DisplayOrder`, carried forward when absent exactly as `ResponseType` and `Mandatory`
already are. Display order is frozen on the version by AD-015, so reordering is a new
version, never an in-place update.

### 5.1 `al_AddQuestion`

Inputs: `SectionId`, `QuestionCode`, `Name`, `Wording`, `ResponseType`, `Mandatory`,
`DisplayOrder` (optional), `EffectiveFrom` (optional, default today), `IdempotencyKey`.
Outputs: `QuestionId`, `VersionId`, `AuditEventId`, `Conflict`.

Preconditions, each refused with `CommandHelpers.PreconditionPrefix`:

- the section exists and is in force — a question cannot be added to a retired section;
- the question code is not already in use — the alternate key enforces this, and the
  collision is caught and returned as a sentence about the code rather than as a platform
  error;
- `EffectiveFrom` is not before today, because a question cannot retrospectively have
  been owed by a review that has already been answered.

### 5.2 `al_RetireQuestion`

Inputs: `QuestionId`, `EffectiveTo` (optional, default today), `Reason` (**required**),
`IdempotencyKey`. Outputs: `QuestionId`, `RetiredVersionId`, `AuditEventId`, `Conflict`.

The mandatory reason follows the AD-031 and AD-043 precedent for privileged actions and
is written to the Audit Event, because "why is this question no longer asked" is exactly
what a regulator asks.

Retiring stamps the current version's `al_effectiveto` and creates no successor. The
version stays Active, so every answer already recorded against it keeps resolving
(AD-091).

### 5.3 `al_MoveQuestion`

Inputs: `QuestionId`, `TargetSectionId`, `NewQuestionCode`, `EffectiveFrom` (optional),
`Reason` (**required**), `IdempotencyKey`. Outputs: `RetiredVersionId`, `NewQuestionId`,
`NewVersionId`, `AuditEventId`, `Conflict`.

A move is a retire plus an add, not a lookup update. The original question is dated out
in its old section and a new question is created in the target carrying the same wording,
response type and mandatory flag. History stays attached to the section it was answered
in, which an in-place change to `al_question.al_sectionid` would destroy for every
submitted review at once.

Both writes happen in one plug-in execution and therefore one transaction, so a move
cannot half-complete and leave a question retired in one section and absent from the
other. The new code is supplied by the administrator, because codes are unique.

A move between sections owned by different roles changes which discipline owes the
question. That is the point of the action and is not guarded, but the modal states it.

## 6. Section commands

| Command | Audit value | Effect |
|---|---|---|
| `al_AddSection` | 120910796 | Creates `al_section` under the checklist version in force |
| `al_RetireSection` | 120910797 | Stamps `al_effectiveto` on the section |
| `al_UpdateSection` | 120910798 | Name, help text, display order, optional flag |

### 6.1 `al_AddSection`

Inputs: `SectionCode`, `Name`, `HelpText` (optional), `OwnerRole` (Tax team, AQS checker
or Both), `DisplayOrder` (optional), `IsOptional` (optional, default false),
`EffectiveFrom` (optional, default today), `Questions` (optional), `IdempotencyKey`.
Outputs: `SectionId`, `QuestionIds`, `AuditEventId`, `Conflict`.

The section is created under the checklist version in force, resolved the same way
`ClaimCasePlugin` resolves it, so the administrator never picks a version. A section code
already in use is refused.

**`Questions` creates the section's questions in the same call**, so a section arrives
complete rather than empty. It is a JSON array carried in a string parameter — Custom API
parameters are scalars, and the codebase already passes numbers as strings — each element
being `{ code, name, wording, responseType, mandatory, displayOrder }` and validated by
the same rules as `al_AddQuestion`. All of it commits in one transaction, so a section is
never created with half its questions. Omitted, the section is created empty and questions
are added afterwards.

A new section renders in both front ends through their fallback paths, in the generic
inline layout, carrying its own name and help text. It will not be folded into one of the
document's designed blocks — that mapping is hand-written and stays so (section 14).

### 6.2 `al_RetireSection`

Inputs: `SectionId`, `EffectiveTo` (optional, default today), `Reason` (**required**),
`IdempotencyKey`. Outputs: `SectionId`, `AuditEventId`, `Conflict`.

Stamps `al_effectiveto`. The section's questions are not touched: the section falling out
of force is what removes them from the form and from the gate, and leaving their versions
alone keeps every recorded answer resolving.

Refused when the section holds a protected question code (section 7), because retiring
the section takes that question out of the gate and the export just as surely as retiring
the question would.

### 6.3 `al_UpdateSection`

Inputs: `SectionId`, `Name` (optional), `HelpText` (optional), `OwnerRole` (optional),
`DisplayOrder` (optional), `IsOptional` (optional), `Reason` (**required**),
`IdempotencyKey`. Outputs: `SectionId`, `AuditEventId`, `Conflict`.

An in-place update, following the `al_UpdateCaseDetails` precedent (AD-043): a mandatory
reason, and an Audit Event recording the before and after of each changed field. Sections
are not versioned, so this is the only shape available; the audited before/after is what
preserves the trail instead.

**Owner role is editable**, by project owner direction of 2026-09-11. An earlier draft of
this design excluded it; that exclusion is withdrawn and the consequence is recorded here
instead, because it is real and the modal must state it.

Changing a section's team is retroactive in a way nothing else in this design is. Owner
role is not versioned and carries no dates, so the change applies to every review that
ever answered the section: a Tax section switched to AQS stops rendering in submitted Tax
reviews and starts rendering in submitted AQS ones, which never answered it. The answers
are not lost — they hang off the review instance and keep resolving — but which discipline
appears to have been asked does change after the fact.

Two things keep that honest rather than silent. The before/after is on an immutable Audit
Event, so the change is evidenced. And **Both** is almost always the better answer when a
section turns out to concern two teams: it adds a discipline without taking one away, and
nothing already answered changes meaning.

Switching a section to optional likewise takes effect on every unsubmitted review
immediately — there is no effective date on the flag. That is intended: relieving
reviewers of a section is not something anyone wants to schedule. Retiring the section is
the dated action.

## 7. Protected question codes

Eight codes are load-bearing in compiled C#. Taking one out of the form does not degrade
the checklist, it stops the system producing outcomes:

| Code | What breaks |
|---|---|
| `Q-GR-01` | The case outcome — `OutcomeRules` maps this answer to the grade |
| `Q-FQ-01` | Trail Light column 10; `GenerateExportPlugin` refuses the row without it |
| `Q-FQTAX-01` | The Tax file-quality outcome |
| `Q-TAX-02` | Whether a Tax case routes to remediation |
| `Q-FQ-03`, `Q-FQTAX-03` | "Remedial action required?", which drives BR-006 |
| `Q-FQ-02`, `Q-FQTAX-02` | The checker observation carried into the remediation description |

`ChecklistGuards.ProtectedQuestionCodes` in the plug-in assembly holds each code against
the sentence above. `al_RetireQuestion`, `al_MoveQuestion` and `al_RetireSection` refuse a
protected code and return that sentence.

`al_RetireAndSucceedQuestion` is not affected: rewording a protected question is safe,
because the guards match on code, and the code lives on `al_question`, which no version
edit touches.

Making a section **optional** is also refused where it holds a protected code, for the
same reason — an unanswered `Q-GR-01` is a case that cannot be graded.

The app mirrors the list to hide the controls, with a test pinning the two lists together.
Consistent with AD-041, the client copy is advisory and the plug-in refusal is the gate.

## 8. Readers that must learn the new rules

The commands are the smaller half of this work. These are the paths that currently ignore
section dates and the optional flag, and all of them must honour both.

| Reader | Change |
|---|---|
| `SubmitReviewPlugin` mandatory query | Filter sections by effective date; exclude sections where `al_isoptional` is true; match owner role as membership, not equality |
| `ResponseGuardPlugin` | Refuse an answer to a question whose section is out of force, mirroring its existing version check; accept a Both section for either discipline |
| Portal review FetchXML | Add the section date conditions beside the question-version ones already there; widen both owner-role conditions; render "Optional" and the owning team |
| `useReviewDetail` / `reviewSections` | Same date filter and the same widened owner-role filter; surface optional so the form can mark it |
| `useQuestionLibrary` | In-force computation for both sections and questions (section 9) |

The owner-role half of this is set out in 4.1, and is the part where a partial change is
worse than no change.

A section that falls out of force must disappear from the form for reviews not yet
submitted, and must still render for a review submitted while it was in force — which is
the same as-of rule the question versions already use, applied one level up.

## 9. User interface

All changes are in `app/src/features/admin/`.

**Edit becomes a modal.** The current in-row editor is replaced by `Modal`
(`components/feedback/Modal.tsx`), matching how `SecurityModals` already handles
administrative edits. The modal edits wording, section, response type, mandatory and
display order in one place. On save it dispatches by what changed: section changed →
`al_MoveQuestion`; otherwise any of the other four → `al_RetireAndSucceedQuestion`. Both
paths create a new version, and the modal says so before saving.

**Add question** sits in each section header, opening the same modal in add mode with the
section fixed and a code field.

**Retire** sits on each question row beside Edit, opening a confirm that takes the
mandatory reason and the stops-from date. A protected question shows a "Required by
grading" chip carrying its sentence instead of the control.

**Add section** sits at the top of the library, opening a modal that takes the section
name, its **team** — Tax, AQS or Both — and a repeatable list of questions, each with
wording, response type and mandatory flag. The whole thing is created in one command, so
a section arrives with its questions rather than as an empty shell to fill in afterwards.

**Edit section** and **Retire section** sit in each section header. The edit modal carries
name, help text, team, display order and an **Optional / Required** toggle; the retire
confirm carries the reason and the stops-from date.

Changing the team is the one edit here that reaches back into submitted reviews (6.3), so
the modal says so where the field is, and says that Both adds a discipline without taking
one away.

Every section header shows its team and, where set, an Optional marker, because "who
answers this" is the first thing an administrator needs to see and is invisible today.

**Retired content becomes visible as retired.** `currentVersionByQuestion` picks the
highest version number and never reads the effective dates, so a correctly retired
question is listed today as though it were live, with a working Edit button. It is changed
to compute in-force status, and both retired questions and retired sections are grouped
into collapsed **Retired** blocks. Administrators need to see what was retired; they must
not mistake it for what is asked.

An optional section is labelled as such in the library and in both review front ends, so a
reviewer can tell what they are not obliged to complete.

## 10. Error handling

Every refusal is a `PreconditionPrefix` message naming the thing that is wrong and the
reason it is wrong, surfaced by the existing `commandClient` handling and rendered in the
modal. The conditions are enumerated in 5.1 to 6.3 and in section 7.

A replayed idempotency key returns the original result rather than writing again, which is
what makes every modal safe to double-submit.

## 11. Testing

Test-driven. Plug-in tests in `OutcomeTesting.Plugins.Tests` run locally with
`DOTNET_ROLL_FORWARD=Major`.

Questions:

- `al_AddQuestion` writes both rows, with v1 dated from `EffectiveFrom`
- `al_AddQuestion` refuses a duplicate code, an unknown section, a retired section, and a
  past date
- `al_RetireQuestion` stamps `al_effectiveto` and writes no successor
- `al_RetireQuestion` refuses each of the eight protected codes by name
- `al_MoveQuestion` retires and adds in one transaction, and refuses protected codes
- `al_RetireAndSucceedQuestion` carries `DisplayOrder` forward when absent

Sections:

- `al_AddSection` creates under the version in force and refuses a duplicate code
- `al_AddSection` with `Questions` creates the section and every question in one
  transaction, and creates **neither** when one question is invalid
- `al_RetireSection` stamps the date, leaves its questions alone, and refuses a section
  holding a protected code
- `al_UpdateSection` audits before/after per field, owner role included
- making a section optional is refused where it holds a protected code

The Both owner role, whose five readers are the likeliest thing in this design to be half
done:

- a Both section is owed by a Tax review and by an AQS review, and appears in the
  mandatory gate for each
- `ResponseGuardPlugin` accepts an answer to a Both section from either discipline, and
  still refuses one from a discipline the section does not name
- each discipline's answers to a Both section stay separate, and neither is visible in the
  other's review
- a section switched from Tax to AQS stops being owed by Tax reviews and starts being owed
  by AQS ones

Readers — the half most likely to be got wrong:

- a question in a retired section drops out of the mandatory gate
- a question in an **optional** section drops out of the mandatory gate
- a retired section's existing answers still resolve, and still render for a review
  submitted while it was in force
- `ResponseGuardPlugin` refuses an answer into a retired section
- all six commands replay idempotently

App tests: in-force grouping for sections and questions in `useQuestionLibrary`, and the
protected-code list pinned against the C# source.

## 12. Build order

Four phases, each leaving the system working. The readers come before the commands
deliberately: teaching them the new columns while nothing yet writes those columns is a
safe, separately verifiable step, and it means no command can ever produce a state the
form does not understand.

| Phase | Contents | Done when |
|---|---|---|
| 1 | The three `al_section` columns and the Both owner-role value, carried into the solution | Columns and value exist in DEV; nothing reads them; every existing test still passes |
| 2 | Readers honour section dates, the optional flag and Both (sections 4.1 and 8) | A section dated out by hand disappears from both front ends and the gate; a submitted review still renders it; a Both section set by hand renders and accepts answers in both disciplines |
| 3 | The six commands, plus `DisplayOrder` on `al_RetireAndSucceedQuestion` | Plug-in tests green; each command exercised against DEV |
| 4 | The Question library UI (section 9) | An administrator can do every action without the `pac` CLI |

Phase 2 is the phase to be slow in. It is the only one where a partial change is worse
than none: a Both section that renders and refuses answers, or a retired section still
demanded at submit, are both states the system has no way to explain to the person hitting
them.

## 13. Deployment

1. Add the three `al_section` columns and carry them into `src/Entities/al_Section`
2. Mint **Both (120910105)** on `al_ownerrole`, and 120910793–798 on `al_command`, with
   `addcommandvalue`, reading each value back from metadata rather than trusting the
   insert — the OD-032 lesson
3. Register the six Custom APIs
4. `build -c Release` **before** `pushassembly`, which uploads `bin/Release` without
   building; check the byte count
5. Seed no permission rows — `question.retire` already exists
6. Import into TEST with `--activate-plugins`, or the steps arrive switched off

Nothing here regenerates `app/src/generated`. Those models lag DEV already and are
refreshed from Dataverse, not by hand.

## 14. Out of scope

- **Un-retire.** Anything dated out stays out; putting it back is an add.
- **Folding a new section into a designed document block, and subsections.** AD-098's
  block mapping is hand-written in `checklistForm.ts` and the portal web template. A new
  section renders through the fallback in the generic inline layout, which is why adding
  one needs no code — but giving it the Suitability grid treatment still does. Recorded in
  the 2026-09-10 gap assessment, section 3.1.
- **Conditional display.** `al_isconditional` stays dead. Reviving it needs a condition
  model — driven by which case field, or which earlier answer? — that nothing in the schema
  carries. Separate work.
- **Reissuing the checklist as V9.** Section 3 explains why it is not required.
- **The portal as an administration surface.** Questions and sections are administered in
  the Code App only. PP-09 already says so, and this design keeps it true.
