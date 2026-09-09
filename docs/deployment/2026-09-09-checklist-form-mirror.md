# The review pages mirror the Checker Checklist, and the fail points are one block — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Portal pushes go through
`plugins/OutcomeTesting.Registration` while the `pac` token stays revoked (see
`2026-09-09-question-version-rule.md`).

## The direction

The project owner supplied the Checker Checklist (V8, the PDF) and asked that the review
pages mirror it — "the sections and everything else", in the document's order — and that the
File Quality fail reasons be treated as their own standalone section rather than a picker
under every non-pass answer. Recorded as AD-096, superseding AD-054.

## What was wrong before

- **App review detail.** A flat "Recorded answers" table sorted by question display order.
  That order is numbered within each section in the seed (Q-TAX-01 and Q-E1-01 are both 1),
  so the answers of every section interleaved. The page showed only answered questions, no
  case header, and the fail reasons under each answer.
- **Portal review page.** Sections were already in document order, but the fail-reason
  checkboxes appeared under every non-pass answer (AD-054) — an AML "No", an E-section
  "Fail", a Consumer Duty "No" each got the full list.

## What changed

| Where | Change |
|---|---|
| `app/src/features/reviews/reviewSections.ts` | `buildSections`: the team's sections in display order, every question in force in each, with its answer or none. Orphaned answers go to a trailing "Other answers" group rather than vanishing. |
| `app/src/features/reviews/checklistForm.ts` | The document's vocabulary: option scales per response type (mirrors `ResponseRules.PermittedChoices`), grid-or-inline layout per section, the case header fields in document order, the standalone fail points scoped by team category, and the remediation block summary. |
| `app/src/features/reviews/useReviewDetail.ts` | Reads the case, the team's sections for the review's checklist version, the question and fail reason catalogues, and the linked reasons; builds the form. |
| `app/src/features/reviews/ReviewDetailPage.tsx` / `.css` | One card per block of the document, in its order: case header; Tax check / AML and CRA; **File Quality – Fail points**; File Quality outcome; E1–E5 with the "Outcome lens" footer; CRP; Consumer Duty overlay; Checker judgement and grading; Remediation and escalation. Tick grids where a section shares one scale, inline ticks or values elsewhere. Cards do not split across printed pages. |
| `OT Review Detail` template | The per-answer reasons block is removed. A "File Quality – Fail points" card is emitted at the boundary into `S-FQOUT` / `S-FQTAX`, bound by `data-ot-failpoints-for` to the File quality outcome question version; the autosave for that answer reads its ticks from the card, so the payload shape (`failReasons`, `renderedReasons`) and `AnswerWriter.ReconcileFailReasons` are unchanged. The evidence note per answer stays. |
| `knowledge/decision-log.md` | AD-096 added; AD-054 marked superseded. |
| `knowledge/checklist-v8.md` | Fail points block described as standalone, recorded on the File quality outcome response. |

No schema, plug-in, table permission or CSS change: the block reuses the `ot-answer__reasons`
and `ot-answer__option` rules already in `outcome-testing.css`.

## Verification

| Check | Result |
|---|---|
| `npx vitest run` (app) | 22 files, 249 tests passed — including 10 for `buildSections` and 24 for `checklistForm` |
| `npx tsc -b` (app) | clean |
| `npx eslint src/features/reviews` (app) | clean |
| Liquid tag balance on the template | if/for/unless/comment/capture/fetchxml all balanced |

## Deployment

Not pushed in this session. To deploy the portal change:

```
plugins/OutcomeTesting.Registration: pushwebtemplate https://org0b075da8.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b "powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html"
```

then clear the site cache from `/_services/about` as before. The app change ships with the
next Code App push.

## Retest

1. Portal, an open AQS review assigned to you: the page runs AML and CRA → **File Quality –
   Fail points** → File Quality: AQS → E1 … E5 → CRP → Consumer Duty → grading. Tick a fail
   point; the File quality outcome answer shows "Saving… / Saved". Reload: the tick holds.
   No fail-reason list appears under any other answer.
2. Portal, a Tax review: Tax check → Fail points (Tax check reasons only) → File Quality: Tax.
3. App, the same review: the case header block, then the same run of sections, every
   question listed with its tick or an empty box, the fail points block ticked to match,
   remediation and escalation last. Save as PDF pages by block.

## Existing data

Reasons attached under AD-054 to answers other than the File quality outcome stay in the
intersect. The app shows them ticked in the fail points block (it reads the union across the
review). The portal shows only the outcome answer's ticks, so on a review answered before
this change a checker may need to re-tick; nothing is deleted.
