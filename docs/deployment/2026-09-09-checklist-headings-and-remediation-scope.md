# The portal's section headings, and remediation's scope — 2026-09-09

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). Pushed by `svc.automate.aq`
through `plugins/OutcomeTesting.Registration`. Branch `fix/portal-signin-identity-username`,
uncommitted at the time of writing. Decisions AD-104, AD-105, AD-106.

## The report

Project owner, 2026-09-09, across several rounds:

- "ensure the checklist matches exactly the html, headers, section splitting and everything,
  fix the deformed headers, ensure all the sections are there and the questions are in the
  database as well"
- "on the portal, File Quality - Tax check section and File Quality Outcome are merged into a
  single table section when they are separate sections, and File Quality fail reason is not
  showing"
- "Headers like 'Checker judgement and grading' and 'Consumer Duty overlay' are missing on the
  portal, ensure they're not missing on the app as well"
- "remediation is its own standalone form under remediation prefilled with fail responses. This
  only applies to PASS INSUFFICIENT EVIDENCE FAIL or just pass and fail, not yes or no
  questions"

Supplied with it: the reference build file, and PDFs of the Tax and AQS checks from both the
portal and the Code App. **The PDFs are what solved this.** Two earlier rounds went looking at
caching and stale deployments, because the live template was byte-identical to the repository
and the DEV data was correct, and neither was the cause.

## What was wrong

1. **The portal rendered one heading for the entire form.** The questionnaire's block key was a
   variable named `block` — a name this engine will not let you assign, the whole page body
   being inside a `block content` tag under `extends 'OT Layout'`. It therefore never varied:
   the "a new block starts here" branch fired once and never again, so every section after the
   first poured into the first section's table with no heading, and the File Quality fail
   points block — which is gated on that key's value — never rendered at all. AD-105.
2. **The rows arrived in the wrong order.** The questions fetch asks for section-then-question
   order with two root-level `order` elements carrying an `entityname`. The SDK honours that;
   the page's Liquid fetch applied only the question order, so every section's question 1 came
   back before any section's question 2. Visible in the owner's PDFs as
   `File quality outcome, Tax check reason, Fail observation, Tax check outcome, …`. AD-105.
3. **Remediation's prepopulated issue list included the yes/no scales.** A No on an AML and CRA
   checking point or a Consumer Duty outcome became a line the adviser was told to put right,
   and so did an Insufficient evidence recorded on Consumer Duty. AD-106.
4. **One seeded value did not match the document**: S-E1's outcome lens held an ASCII
   apostrophe where the document sets U+2019. Found by machine comparison, not by eye. AD-104.

The Code App had none of these. It was correct throughout, headings included, which is what the
owner's app PDFs show.

## What changed

| Where | Change |
|---|---|
| `OT Review Detail` | `block` → `blk` (with `blk_title`, `blk_layout`, `blk_rt`, `blk_subsections`, `current_blk`). New `sections` fetchxml, ordered by a plain root-entity order; the questionnaire iterates sections outer and that section's rows inner, matched on section code. `forloop.first` → a `first_row` flag; the last block is closed after both loops. |
| `Remediation.cs` | New `IsRemediableScale`; `IsNonPassAnswer` now takes the response type as well as the answer; `NonPassItems` reads `al_responsetype` off the question version to apply it. **Behaviour change — assembly pushed.** |
| `data/v8-seed/data.xml` | S-E1 `al_helptext` apostrophe to U+2019. |
| `docs/reference/checker-checklist.html` | The reference build file, checked in. |
| `app/.../checklistDocument.test.ts` | Reads the reference file and the seed and asserts the app's blocks are the document's — titles, intros, column headings, E1–E5 subsections, lenses, every question, the inline option runs, all twenty fail reasons. |
| `app/.../checklistRender.test.tsx` | Renders the page and reads its headings and band rows back out of the markup, including one assertion that names every heading. |
| `knowledge/` | AD-104, AD-105, AD-106; the Power Pages Liquid traps section. |

## Verification

| Check | Result |
|---|---|
| App `vitest` | 280 passed (13 new) |
| App `tsc -b --noEmit` / `eslint` | clean / 0 errors (9 pre-existing warnings, untouched files) |
| Plugin `dotnet test` | 563 passed (9 new) |
| Liquid tag balance, OT Review Detail | if 51/51, for 11/11, unless 10/10, capture 5/5, comment 29/29, fetchxml 7/7, block 1/1 |
| Template rendered off the real DEV rows | Tax and AQS both correct in three different row orders — section order, question order, and the exact broken order from the owner's PDFs |
| Mutation checks | Reverting the seed apostrophe, renaming a block title, and letting the Consumer Duty scale back into remediation each failed a test naming what moved |

## What ran

| Step | Command | Result |
|---|---|---|
| 1 | `dotnet build -c Release` (OutcomeTesting.Plugins) | clean, 176640 bytes |
| 2 | `pushassembly` | 176640 bytes, v1.0.0.0, 21:12:01Z |
| 3 | `pushwebtemplate … 00000000001b` | 72647 → 76172 chars, 21:12:22Z |
| 4 | `pushwebtemplate … 00000000001b` (again, corrected comment) | 76172 → 76540 chars, 21:15:14Z |
| 5 | `importseed … --confirm` — **run by the project owner** | **0 created, 126 updated** |

Read back afterwards by `fetch`: the stored template source is byte-identical to the repository
file, and `al_section` S-E1's `al_helptext` now holds U+2019 while E2 to E5 stay ASCII-only,
which is what the document sets.

Site cache clear (`/_services/about` → Clear cache) is the project owner's step.

## A claim withdrawn

The first push carried a comment asserting that the `default` filter does not return its input
in this engine. That was over-claimed: the same template uses `| default:` half a dozen times in
the summary card that renders correctly, and the stock Microsoft templates use it throughout.
The `block` collision explains the symptom on its own. The comment, AD-105 and the checklist
notes were corrected and the template re-pushed (step 4) so the deployed file does not send the
next person after the wrong filter. The fallback is still written as an explicit `if`, for
legibility rather than suspicion.

## Retest

**Both disciplines, portal:** every section heading is present and each has its own table — Tax
shows `File Quality - Tax check section`, `File Quality – Fail points`, `File Quality Outcome`;
AQS shows those less the Tax one, plus `Suitability core checks` with `E1.`–`E5.` band rows and
an outcome lens under each, `Centralised Retirement Proposition`, `Consumer Duty overlay` and
`Checker judgement and grading`. The fail points table lists all twenty reasons on both.

**Row order:** within `File Quality - Tax check section`, the rows read
`Tax check reason, Tax check outcome, Case notes` — not interleaved with the outcome section.

**E2 only:** the outcome lens row ends in a single tick box, and it saves.

**Remediation:** submit a review with a Fail on a suitability test point and a No on an AML
checking point. The raised action's "Issue / fail reason" lists the suitability Fail and any
ticked fail points, and does **not** list the AML No. A Consumer Duty outcome of Insufficient
evidence must not appear either — that is the case the scale check exists for, since
Insufficient evidence is one option value shared with the suitability scale.

**Grading:** the Advice Quality Grade is not listed as an issue; it is already the reason the
action gives.

## Known gaps carried forward

- **Remediation and escalation is not a block of the checklist form.** The document carries it
  marked "(PICKED UP ON ANOTHER FORM)" and it is built as one (AD-095). Unchanged.
- **S-CRP still renders on every AQS review.** OD-016 gates the applicability mapping.
- **Reviews submitted before this push** had their remediation actions prepopulated under the
  old rule, so an existing action may list a No from a yes/no question. Nothing was rewritten;
  `Raise` deliberately does not touch an action that already exists.
