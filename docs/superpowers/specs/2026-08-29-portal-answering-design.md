# Portal answering module — design

Status: approved 2026-08-29. Implements PP-07, PP-08 and the write half of PP-11 for the Power Pages portal. Extends `2026-08-29-power-pages-portal-design.md` section 5 (questionnaire rendering) and section 6 (submission and locking), and delivers build sequence step 4.

Requirement IDs: PP-07, PP-08, PP-09, PP-11, FR-010, FR-011, FR-012, FR-013, FR-016, BR-005, BR-012, BR-013, NFR-SEC-01, NFR-ACC-01, NFR-AUD-01, NFR-OBS-01.

## 1. Problem

`OT Review Detail` renders the questionnaire read-only. Across every `ot-*` web template the only input control in the site is the case-list filter form, so the portal has no write path at all. `al_SubmitReview` exists and enforces the mandatory gate server-side, but a checker cannot produce an answer for it to gate.

## 2. Boundary

In scope: the assigned checker answering their own Tax or AQS review at `/review?id=`.

Out of scope, unchanged: the Code App stays read-only manager oversight (AD-044); the portal never writes `al_Question`, `al_QuestionVersion`, `al_Section`, `al_Checklist` or `al_ChecklistVersion` (PP-09); `al_SubmitReview` is not modified.

## 3. Section filtering

Sections are selected by matching `al_Section.al_OwnerRole` to `al_ReviewInstance.al_ReviewType`:

| Review type | Owner role rendered |
|---|---|
| Tax `120910200` | Tax team `120910100` |
| AQS `120910201` | AQS checker `120910101` |

This makes the AD-020 Tax/AQS split structural. A Tax checker's page cannot render an AQS section even before table permissions are consulted, which is what PP-08's "no cross-discipline overwrite" requires; UI convention alone would not satisfy NFR-SEC-01.

## 4. Controls

One control per `al_QuestionVersion.al_ResponseType`, writing the AD-023 typed column.

| Response type | Control | Column |
|---|---|---|
| Text `120910000` | `input type=text` | `al_answertext` |
| Multiline text `120910001` | `textarea` | `al_answertext` |
| Date `120910002` | `input type=date` | `al_answerdate` |
| Single select `120910003` | `select` | `al_answerchoice` |
| Multi select `120910004` | checkbox group | `al_answerchoices` |
| Pass / Fail `120910005` | radio group | `al_answerchoice` |
| Pass / Fail / Insufficient `120910006` | radio group | `al_answerchoice` |
| Yes / No `120910007` | radio group | `al_answerchoice` |
| Yes / No / N/A `120910008` | radio group | `al_answerchoice` |
| Yes / No / Insufficient `120910009` | radio group | `al_answerchoice` |
| Grade `120910010` | radio group | `al_answerchoice` |

Permitted option subsets of the `al_response_answerchoice` union set (AD-023):

| Response type | Permitted values |
|---|---|
| Pass / Fail | 120910300, 120910301 |
| Pass / Fail / Insufficient | 120910300, 120910301, 120910302 |
| Yes / No | 120910305, 120910306 |
| Yes / No / N/A | 120910305, 120910306, 120910307 |
| Yes / No / Insufficient | 120910305, 120910306, 120910302 |
| Grade | 120910300, 120910303, 120910302, 120910304 |
| Single select | 120910320 to 120910328 |
| Multi select | 120910340 to 120910344 (`al_answerchoices`) |

### 4.1 Resolving the Single select ambiguity

`Single select` has no per-question option list, and two seeded questions used it with different vocabularies: Q-TAX-02 (Tax check outcome) and Q-GR-02 (Primary root cause). Nothing in the schema distinguishes them.

Resolution, derived from the questions themselves rather than invented: Q-TAX-02's options in `knowledge/checklist-v8.md` are PASS, INSUFFICIENT EVIDENCE, FAIL, which is exactly the `Pass / Fail / Insufficient evidence` scale `120910006`. The "SingleSelect" label in the V8 transcription described cardinality, not a scale. Q-TAX-02 is retyped to `120910006` in `data/v8-seed`, after which `Single select` maps unambiguously to the Primary root cause block and every response type has a fixed subset with no new column.

This is a correction to unpublished DEV seed data re-imported under AD-027, not an amendment to a version any real review has been issued, so BR-013 and PP-09 immutability are not engaged.

### 4.2 Evidence notes

`al_answertext` doubles as a free-text note held alongside a structured answer. This is established app logic, not a new rule: `app/src/features/reviews/useReviewDetail.ts` `noteOf()` already reads it that way and the Code App renders it. Non-pass structured answers therefore render an optional note `textarea` writing `al_answertext` on the same Response row as the choice, and the guard plug-in must permit text alongside a choice. Rejecting that combination would break the record shape the Code App reads.

Notes stay optional under AD-019. The V8 catalogue flags that a Fail with no observation gives remediation nothing to work from under BR-006; that remains an open content question, not a schema rule to invent here.

## 5. Write path

Portal Web API, direct to `al_response`. The Portals Web API supports create, update and associate; it does not support actions, which is why submission needs a cloud flow but answering does not. Avoiding a flow per answer also avoids a Power Automate run per debounced edit.

Protocol:

1. The page loads a `questionVersionId -> responseId` map from the existing response fetch.
2. A change debounces 800ms, then `PATCH` when the id is known, else `POST` and cache the id from the `OData-EntityId` response header.
3. Per-field status: `Saving...` / `Saved HH:MM` / `Not saved - retry`, in an `aria-live="polite"` region.

Nothing batches, so a failed write loses one answer, not a section.

`al_responsecode` and `al_name` are required on create. Both are stamped by the guard plug-in rather than supplied by the client: `al_responsecode` as `{reviewId}|{questionVersionId}`, which the existing `al_ResponseCodeKey` alternate key then makes unique, so a double-create races into a platform duplicate-key rejection instead of two rival answers. 73 characters fits the AD-026 MaxLength 100.

The review id read from the query string is validated against a strict GUID pattern before it reaches a filter, mirroring `isRecordId` in `app/src/services/odata.ts`.

## 6. Guard plug-in — `ResponseGuardPlugin`

Synchronous pre-operation on `al_response` Create and Update, plus Associate and Disassociate on `al_failreason_response`. Rejects with the existing prefixes the review page already branches on (`CONFLICT:`, `UNAUTHORIZED:`, `PRECONDITION:`):

- a write to a review whose `al_reviewstatus` is Submitted `120910212` — the PP-11 lock, enforced below the Web API so a hand-edited URL cannot bypass it
- an answer in a column that does not match the question version's response type (AD-023), except `al_answertext` accompanying a choice per section 4.2
- an option value outside the section 4 permitted subset for that response type
- a response whose question's section owner role does not match the review's type

Post-operation, the first saved answer moves the review `Assigned 120910210` to `Review In Progress 120910211`. The portal has no write permission on `al_reviewinstance`, so this transition can only happen server-side.

Authorization is deliberately **not** performed here. Power Pages Web API writes reach Dataverse under the site's application user, so `InitiatingUserId` is not the checker and treating it as one would be a security hole. The caller gate is section 7.

## 7. Table permissions and site settings

Current state: 13 permissions named `PROVISIONAL` or `PROVISIONAL-DEV-ONLY`, at Global scope, read-only, granted to 2 of 10 web roles. That is the portal design's unclosed phase-2 security gate. Granting write at Global scope would let any checker edit any other checker's answers, one of the negative tests section 10 of the portal design requires to fail.

This module therefore replaces them for the review path with the AD-047 model:

| Table | Scope | Rights |
|---|---|---|
| `al_reviewinstance` | Contact, via `al_assignedcontactid` (built, AD-050) | Read |
| `al_response` | Child permission of the above | Read, Write, Create, Append |
| `al_failreason` | Global | Read, AppendTo |

Site settings added: `Webapi/al_response/enabled = true` and `Webapi/al_response/fields` listing only the four answer columns.

OD-022 is not touched. It concerns case access; review access is already unblocked by the AD-050 Contact lookup.

## 8. Fail reasons

On any answer of Fail `120910301`, Insufficient evidence `120910302` or Potential harm `120910304`, a multi-select of `al_FailReason` filtered to categories relevant to the section, associated over `al_failreason_response` (AD-025, FR-013).

The V8 document files the 20 seed reasons under "File Quality fail points" but their categories span AML, Breach, Record Keeping and Tax check, which is wider than one question. The any-non-pass trigger is recorded as a new AD: it is what makes the AML, Breach and Tax-check categories reachable, and it follows BR-006, under which every non-pass outcome requires remediation and so needs a recorded reason to work from.

## 9. Accessibility (NFR-ACC-01, WCAG 2.2 AA)

Radio and checkbox groups as `fieldset` with `legend`; every control labelled and associated; save status in a polite live region; full keyboard operation with visible focus; required state conveyed by text, not colour alone; validation messages associated to their control.

## 10. Errors (PP-16, NFR-OBS-01)

The client maps the plug-in's prefixes to user-facing sentences that say what happened, what is still safe and what to do next, and never expose a table name, query, id or stack trace. This reuses the mapping already in the submit script.

## 11. Testing

- xUnit on `ResponseGuardPlugin` for every rejection path and the status transition. This introduces the first plug-in test project; twelve commands currently carry the system's business rules with no unit tests.
- Manual negative tests: a second checker's review, a submitted review, a hand-edited URL, a raw Web API `PATCH` bypassing the page.
- Keyboard-only pass over one Tax and one AQS review.

## 12. Blocked and carried

- S-CRP renders for every AQS review. AD-021's derivation from case product/solution type stays unbuilt; OD-016 remains the gate. Over-asking is recoverable, silently skipping a mandatory section is not.
- End-to-end demonstration still needs the `OutcomeTesting/Flow/SubmitReview` cloud flow, which has never been created: the site setting is empty and `src/Workflows/` holds no flow. Answering can be built and tested without it; reaching the locked state cannot.

## 13. New decisions to record

| ID | Decision |
|---|---|
| AD-053 | Portal answers are written directly to `al_response` over the Portals Web API, with a synchronous guard plug-in enforcing the lock, the AD-023 column match, the option subset and section ownership. Authorization is Contact-scoped table permissions, not plug-in identity. |
| AD-054 | The fail-reason picker appears on any non-pass answer, filtered by section category, not only on the file quality outcome. |
| AD-055 | Q-TAX-02 is retyped to `Pass / Fail / Insufficient evidence`, resolving the Single select option-list ambiguity without a schema change. |
