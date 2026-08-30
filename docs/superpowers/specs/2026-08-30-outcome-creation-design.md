# Outcome creation — design

**Sub-project 1 of the outcome and remediation spine (E4, Phase 5).**

Submitting an AQS review produces the initial Outcome, and the case moves to the state
its grade demands. This is the piece everything downstream waits on: no Outcome exists
today, so remediation, sign-off, closure and the Trail Light export all have nothing to
work from.

Requirements: BR-004, BR-005, BR-006, BR-007, PP-07, PP-08, PP-10, FR-016, FR-017.
Decisions: AD-003, AD-013, AD-020, AD-023, AD-031, AD-036, AD-039, AD-042, AD-055, AD-057.

## 1. What exists, and what is missing

`al_Outcome`, `al_RemediationAction` and `al_Signoff` exist as tables. Both ends of the
remediation loop are built as commands — `al_CompleteRemediation` and
`al_SignOffRemediation`. `al_SubmitReview` enforces the mandatory-answer gate, flips
`al_reviewstatus` to Submitted and writes an Audit Event.

Nothing creates an Outcome. Nothing creates a Remediation Action. Nothing moves a case
through its lifecycle — `al_casestatus` only ever changes when a manager edits it by
hand through `al_UpdateCaseDetails`. The commands at the far end of the loop have
therefore never been reachable.

Two defects block the work and are fixed here rather than worked around:

**The mandatory gate ignores discipline.** `EnsureMandatoryQuestionsAnswered` filters
mandatory question versions by checklist version alone. It demands all 42 mandatory
answers from every section whatever the review's discipline, so a Tax review can never
submit — it is held by AQS questions its reviewer cannot see — and an AQS review is held
by `Q-TAX-01` and `Q-TAX-02`. The portal already filters sections by owner role
(AD-056), so client and server disagree, which is the same shape as audit finding C6.

**`al_ReviewRoute` has no seed rows.** The table defines `al_requirestaxreview` and
`al_requiresaqsreview` but holds no data, so `al_OutcomeCase.al_reviewrouteid` is null on
every case in DEV and the Tax branch has nothing to read.

## 2. Decisions this design rests on

### 2.1 An Outcome belongs to an AQS review

Settled by the schema, not by preference. `al_Outcome.al_reviewinstanceid` is
**required**, so an Outcome is per review instance rather than per case, and
`al_initialoutcome` carries only the BR-005 four-value scale — Pass `120910700`, Pass
with issues `120910701`, Insufficient evidence `120910702`, Potential harm `120910703`.
A Tax review's result is the three-value `PassFailInsufficient` scale of `Q-TAX-02`
(AD-055) and does not fit that option set. AD-039 confirms it from the other side: the
Trail Light contract has no Tax column.

So a Tax review creates no Outcome. Its grade stays on its `Q-TAX-02` response. A Tax
non-pass still enters remediation, which is possible because `al_RemediationAction`
points at the case and the review instance, never at an Outcome.

### 2.2 Tax hands off through the queue

On a Tax-then-AQS route, a Tax submit returns the case to `Queued` so a manager assigns
the AQS checker. Allocation is manual by a manager with no auto-routing (BR-003,
AD-040), so the handoff is real work to allocate and is made visible as such rather than
leaving the case parked in `Review In Progress` with nobody working it.

### 2.3 Fail accountability is four flags on the Outcome

Resolves OD-024. AD-039 attributes a File Quality or Advice Quality fail to the adviser
**and/or** the paraplanner, which is a judgement, and nothing in the model recorded it —
so eight of the twenty contracted export columns were never populated. Four booleans on
`al_Outcome` record the judgement; the export takes the name and code from the case
where a flag is set and leaves the pair blank where it is not. This expresses "and/or"
exactly, stores no duplicate name or code data, and keeps the judgement on the graded
record.

Copying the case's adviser and paraplanner into all four pairs whenever the grade is a
fail was rejected: it asserts an attribution nobody made and would show every
paraplanner accountable for every adviser fail in trend MI. Deriving accountability from
`al_FailReason` categories was rejected because no approved document maps a reason
category to a person.

### 2.4 Accountability is captured after the Outcome exists, and gates the export

The Outcome does not exist until submit, so the checker cannot tick accountability while
reviewing. The flags are therefore set afterwards, through a small server-side command,
and `al_GenerateExport` refuses to produce a row for a non-pass Outcome that records no
accountability. Nothing incomplete reaches Trail Light, and the gap is reported at
generation time rather than discovered in a delivered file.

Adding the judgement to the checklist as two S-GRADE questions would capture it at the
right moment, but V8 content is transcribed verbatim from the signed document and is
owned by OD-001's owner. Questions are not invented here.

## 3. Schema delta

### 3.1 `al_Outcome` — four accountability flags

| Column | Type | Meaning |
|---|---|---|
| `al_fqadviseraccountable` | Two Options, default No | Adviser accountable for the File Quality fail |
| `al_fqparaplanneraccountable` | Two Options, default No | Paraplanner accountable for the File Quality fail |
| `al_aqadviseraccountable` | Two Options, default No | Adviser accountable for the Advice Quality fail |
| `al_aqparaplanneraccountable` | Two Options, default No | Paraplanner accountable for the Advice Quality fail |

Authored by importing a first draft and replacing the local file with what Dataverse
emits on export (AD-013). Hand-written entity XML is rejected for subtle reasons.

### 3.2 `al_ReviewRoute` — three seed rows

| `al_routecode` | `al_name` | Tax | AQS | `al_displayorder` |
|---|---|---|---|---|
| `ROUTE-TAX` | Tax only | Yes | No | 1 |
| `ROUTE-AQS` | AQS only | No | Yes | 2 |
| `ROUTE-TAX-AQS` | Tax then AQS | Yes | Yes | 3 |

Seeded through the existing `data/` package pattern, keyed on the `al_routecode`
alternate key so the import is idempotent (AD-014, NFR-REL-01).

## 4. The mandatory gate, scoped to discipline

`EnsureMandatoryQuestionsAnswered` gains one link and one condition: from
`al_questionversion` through `al_question` to `al_section`, filtering
`al_section.al_ownerrole` by the discipline of the review being submitted.

| `al_reviewinstance.al_reviewtype` | `al_section.al_ownerrole` |
|---|---|
| Tax `120910200` | Tax team `120910100` |
| AQS `120910201` | AQS checker `120910101` |

In Checker Checklist V8 that resolves to `S-TAX` for Tax and the other ten sections for
AQS. The mapping is a lookup rather than a section-code list, so a future section is
covered by its owner role without a code change.

A review whose type maps to no owner role is refused with `PRECONDITION:` rather than
being allowed to submit against an empty mandatory set — an unrecognised discipline is a
configuration fault, not a review with nothing to answer.

## 5. `OutcomeRules` — the pure decision logic

A static class with no Dataverse dependency, unit-tested without a fake organisation
service. Mirrors `ResponseRules` and `CaseLifecycle`; the plug-in stays thin wiring over
it, which is what keeps `SubmitReviewPlugin` readable as it grows.

| Function | Behaviour |
|---|---|
| `GradeFromAnswer(int answerChoice)` | `Q-GR-01` answer to `al_initialoutcome`. Pass `120910300`→`120910700`, Pass with issues `120910303`→`120910701`, Insufficient evidence `120910302`→`120910702`, Potential harm `120910304`→`120910703`. Any other value is unmapped. |
| `RequiresRemediation(int outcome)` | True for every non-Pass (BR-006). |
| `TaxResultRequiresRemediation(int answerChoice)` | True for `Q-TAX-02` Fail `120910301` and Insufficient evidence `120910302`; false for Pass. A completed Tax check with a non-pass result enters remediation. |
| `NextCaseStatusForAqs(int outcome)` | Pass → `Closed` `120910591`; any non-pass → `Awaiting Remediation` `120910587`. |
| `NextCaseStatusForTax(int answerChoice, bool aqsStillToCome)` | `Queued` `120910583` when AQS is still to come, whatever the Tax result. Otherwise `Awaiting Remediation` on a non-pass and `Closed` on a Pass. |
| `TaxMustPrecedeAqs(int reviewType, bool taxReviewOutstanding)` | False — refuse — when an AQS review is submitted while a Tax review on the same case is unsubmitted (BR-004). |

Every transition it returns is one `CaseLifecycle.IsAllowed` already permits, and the
plug-in asks `CaseLifecycle` before writing regardless — the two are checked against each
other by test rather than by assumption.

## 6. Wiring in `SubmitReviewPlugin`

Approach A: outcome creation runs inside the existing submit command, in the same
transaction, under its existing concurrency, idempotency and audit machinery. A review
can then never be `Submitted` without its Outcome. A post-operation plug-in on
`al_reviewinstance` update was rejected because it fires on any update path including
migrations and introduces a second transaction boundary, and a failure there would leave
a submitted review with no Outcome — the BR-007 record that most needs to be reliable. A
separate `al_FinaliseReview` command was rejected because the portal cannot call actions
over the Web API at all, so it would need a second cloud flow and would leave the case
inconsistent between the two calls.

Order of work inside the command, after the existing mandatory gate and status update:

1. Read the review's `al_reviewtype`, `al_sequence` and `al_outcomecaseid`.
2. **AQS:** resolve the `Q-GR-01` answer by linking Response → QuestionVersion →
   Question and filtering `al_question.al_questioncode`, the same traversal
   `GenerateExportPlugin.ResolveFileQualityGrade` already uses. Matching on the business
   code rather than a GUID means a retired-and-succeeded question keeps working
   (BR-013, AD-004). Map it through `OutcomeRules.GradeFromAnswer` and create the
   `al_Outcome` with `al_initialoutcome`, both required lookups, and a code and name.
3. **Tax:** resolve `Q-TAX-02` the same way. Create no Outcome.
4. Compute the next case status, confirm `CaseLifecycle.IsAllowed`, and update the case.
5. The existing Audit Event records the submission; its details gain the grade so the
   trail shows what the submission produced.

`al_Outcome` is keyed on `al_outcomecode`, so the code is derived deterministically as
`OUT-<case reference>-<review sequence>`. A replayed submit therefore upserts the same
row rather than creating a second, which keeps the command idempotent under NFR-REL-01
even if the audit replay path is ever bypassed.

### Route fallback

`al_reviewrouteid` is null on every case currently in DEV. Where it is absent, whether
AQS is still to come is taken from the review instances that actually exist on the case:
a Tax submit with no sibling AQS instance finalises as Tax-only. Where the route is set,
the route decides. The fallback is documented behaviour, not a silent guess, and it
stops the seed rows in 3.2 becoming a hard prerequisite for existing data.

### Tax before AQS

Tax and AQS run sequentially when both are required (BR-004, and the architecture
invariant in `project-context.md`). Nothing enforced that, so an AQS review could be
submitted and graded while its Tax review was still open — producing an Outcome for a
case whose Tax check had not happened, and closing it on a Pass.

An AQS submit is therefore refused while any Tax review instance on the same case is not
yet Submitted. The check reads sibling instances on `al_outcomecaseid` rather than
trusting `al_sequence`, because sequence is data a caller can set and the invariant must
hold regardless. A Tax submit is never refused for this reason: Tax is always first.

## 7. Capturing accountability

A new command `al_SetFailAccountability` sets the four flags on an Outcome. It takes the
Outcome id, the four booleans, a mandatory idempotency key, and writes an Audit Event
like every other command (AD-003, BR-012). It enforces the same `page.cases` Edit
permission as the case edit path, refuses an Outcome that does not exist, and refuses to
record accountability against a Pass — there is no fail to attribute.

`al_GenerateExport` then gains one precondition: a non-pass Outcome with all four flags
false is refused with `PRECONDITION:` naming the case, rather than exporting a row whose
accountability columns are blank. A Pass exports unchanged, because AD-039's
accountability pairs only ever describe a fail.

## 8. Export population

`GenerateExportPlugin` fills the eight AD-039 columns it currently leaves empty:

| Columns | Source |
|---|---|
| 11, 12 File Quality fail — adviser name, code | `al_advisername`, `al_advisercode` from the case, when `al_fqadviseraccountable` |
| 13, 14 File Quality fail — paraplanner name, code | `al_paraplanner`, `al_paraplannercode`, when `al_fqparaplanneraccountable` |
| 17, 18 Advice Quality fail — adviser name, code | `al_advisername`, `al_advisercode`, when `al_aqadviseraccountable` |
| 19, 20 Advice Quality fail — paraplanner name, code | `al_paraplanner`, `al_paraplannercode`, when `al_aqparaplanneraccountable` |

A pair whose flag is false is written empty. Column 10, File Quality Grade, is already
populated from `Q-FQ-01`; column 15, Advice Quality Grade, already comes from the
Outcome. With this change every one of the twenty contracted columns is written.

## 9. Error handling

All refusals use the established prefixes the portal branches on (PP-16): `PRECONDITION:`
for a state the caller can fix, `UNAUTHORIZED:` for permission, `CONFLICT:` for
concurrency. Technical detail stays in the Audit Event and the plug-in trace, never on
screen (NFR-OBS-01).

| Condition | Behaviour |
|---|---|
| `Q-GR-01` unanswered on an AQS submit | `PRECONDITION:`. Unreachable while Q-GR-01 is mandatory and the gate runs first, but refused rather than assumed. |
| `Q-GR-01` answer outside the mapped set | `PRECONDITION:` naming the value. Never defaults to Pass — a grade the model does not recognise must not silently become the most favourable one. |
| Review type maps to no owner role | `PRECONDITION:`. |
| AQS submitted while a Tax review on the case is open | `PRECONDITION:` naming the outstanding Tax review (BR-004). |
| Computed transition not permitted by `CaseLifecycle` | `PRECONDITION:` carrying `DescribeRefusal`, so the message names both states and what is reachable. |
| Submit replayed with the same idempotency key | Existing replay path returns the original result; the deterministic Outcome code means no second Outcome either way. |
| Non-pass Outcome with no accountability, at export | `PRECONDITION:` naming the case. |

## 10. Testing

`OutcomeRules` is pure, so it carries the detail, in the manner of `CaseLifecycleTests`:
every grade mapping including the unmapped-value refusal, both disciplines, the
Tax-then-AQS handoff, Tax-only finalisation, the null-route fallback, and the refusal of an AQS submit while Tax is outstanding. A test asserts
that every status `OutcomeRules` can return is one `CaseLifecycle` permits from the
state it would be returned in, so the two tables cannot drift apart unnoticed.

The plug-in wiring is covered by the existing `pac`-authenticated verification harness
rather than by unit tests, since it needs a Dataverse org. It runs against a named case
with `--confirm`, per the guard added for audit finding I7.

Manual verification in DEV: a Tax-only case through submit to closure, a Tax-then-AQS
case through both submits and an AQS-first attempt refused, an AQS Pass closing, an AQS Potential harm reaching Awaiting
Remediation, and an export refused for a non-pass with no accountability then succeeding
once it is set.

## 11. Deliberately not in this design

- **Remediation Action generation.** Sub-project 2. This design stops at the Outcome and
  the case status; BR-006's actions are created there.
- **Notifications.** Sub-project 3. `al_Notification` does not exist.
- **The SubmitReview cloud flow.** Still absent, and still only creatable in the maker
  portal (`docs/submit-review-flow.md`). Everything here is reachable through the command
  directly and through the verification harness; the portal path needs the flow.
- **S-CRP conditional rendering.** Blocked by OD-016. CRP renders for every AQS review.
- **Regrade.** `al_RegradeCase` already exists and writes the final outcome (AD-031).
- **A portal control for the accountability flags.** The command is server-side and
  reachable from the Code App; the portal surface is a later slice.

## 12. Open decisions

| ID | Effect here |
|---|---|
| OD-016 | CRP applicability. Does not block: CRP questions are mandatory and answered like any other. |
| OD-018 | Remediation SLA clock. Sub-project 2. |
| OD-026 | `src/PluginAssemblies/` still declares 11 plug-in types against 16 built. Affects managed promotion, not DEV runtime. |
