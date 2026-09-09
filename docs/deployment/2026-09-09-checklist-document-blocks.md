# The review pages in the document's blocks — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Pushed by `svc.automate.aq`
through `plugins/OutcomeTesting.Registration` (Release).

## The report

Project owner, after the AD-096 push: "The form still doesn't match the attached pdf e.g the
Suitability test point section is not there", then "ensure all the pdf sections are included,
and appear in the order they are shown".

## What was wrong

Both review pages rendered one card per `al_section` under the section's own name. The
Checker Checklist lays its sections out under its own headings, which are not the section
names: E1 to E5 sit together under **Suitability core checks** as one table headed
**Suitability test point**; the Tax check is **File Quality - Tax check section**; AML and
CRA is **File Quality - AML and CRA checking points**; the outcome is **File Quality
Outcome**. The portal page also had no case header block and no remediation block. Recorded
as AD-098.

## What changed

| Where | Change |
|---|---|
| `app/.../checklistForm.ts` `formBlocks` | Folds the sections into the document's blocks by section code, in section order: title, intro, layout, column heading and scale per block; a subsection heading and outcome lens per E section; the fail points block before File Quality Outcome. An unknown code keeps its own name and lays out by its rows. 8 tests in `formBlocks.test.ts`. |
| `app/.../ReviewDetailPage.tsx` / `.css` | Renders blocks: one table per block, the grid's column heading as the document has it, subsection rows, lens rows. A row not on the block's scale spans the tick columns with its own control. |
| `OT Review Detail` template | The same block reading in Liquid. A grid block is one `ot-table` with a radio per tick column; the `<tr>` is the autosave root so `bind()` is unchanged. The case header block (all 18 fields, from the case link-entity) opens the form; a read-only Remediation and escalation block (actions, sign-offs, final outcome, three fetches) closes it before the submit panel. |
| `outcome-testing.css` | Grid tick columns, subsection and lens rows, no inner scroll on the questionnaire tables. |
| `knowledge/decision-log.md` | AD-098. |

## Verification

| Check | Result |
|---|---|
| App `vitest` / `tsc -b` / `eslint` | 257 passed (8 new); clean; clean |
| Liquid tag balance | if 60/60, for 12/12, unless 9/9, comment 17/17, capture 4/4, fetchxml 8/8 |

## What ran

| Step | Command | Result |
|---|---|---|
| 1 | `npm run build` then `npx pa app push` (app) | built clean, pushed successfully |
| 2 | `dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-00000000001b ".../ot-review-detail/OT-Review-Detail.webtemplate.source.html"` | 57262 -> 75413 chars, 2026-09-09 16:24:08Z |
| 3 | `dotnet $T pushwebfile $U a1000000-0000-4000-8000-000000000050 ".../web-files/outcome-testing.css"` | 33945 bytes, 16:24:16Z |

Site cache clear (`/_services/about` → Clear cache): project owner's step. The stylesheet
is a web file, so a hard refresh may also be needed on a browser that cached the old one.

## Retest

AQS review, portal and app, top to bottom:

1. Outcome Testing – Checker Checklist (case header, 18 fields).
2. File Quality - AML and CRA checking points: Check | Yes | No | N/A.
3. File Quality – Fail points.
4. File Quality Outcome.
5. Suitability core checks: intro line, then one table headed Suitability test point | Pass |
   Fail | Insufficient evidence, with E1 … E5 as subsection rows and "Outcome lens: …" under
   each.
6. Centralised Retirement Proposition, with its intro line.
7. Consumer Duty overlay: Outcome | Yes | No | Insufficient evidence.
8. Checker judgement and grading.
9. Remediation and escalation.

Tax review: header, File Quality - Tax check section, fail points, File Quality Outcome,
remediation. On the portal, tick a radio in the Suitability grid: the row's status shows
"Saving… / Saved" and the tick holds on reload.

## Not in this push

The other discipline's blocks on a review page: an AQS review does not show the Tax check
block, because each review instance holds its own team's answers (AD-020). A read-only view
of the sibling review's blocks is a follow-up.
