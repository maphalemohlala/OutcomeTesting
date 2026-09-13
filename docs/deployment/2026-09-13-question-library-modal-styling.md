# The question library modals, styled like the rest of the app — DEV, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). **DEV only.** TEST and PROD
are untouched, by project owner direction of 2026-09-12.

Reported by the project owner, with a screenshot of **Add a section**: "the styling on the
question library modals seems off", and then: "ensure it matches that of the edit case".

---

## What was wrong

`QuestionLibraryPage.css` styled **only the select**:

```css
.library__field select { padding: … border: … border-radius: … }
```

There was no rule for `input` or `textarea`, and the app has no global control styling. So in
every question library modal the text inputs, the number input and the textarea fell through
to the browser's own controls — square corners, a different border, and in the textarea's case
a **monospace face** — sitting directly above and below a select that was styled. The labels
were `--colour-text-muted` at normal weight, where the rest of the app uses 600 in the text
colour.

Four implementations of the same idea had drifted apart:

| | Controls styled | Padding | Border | Label |
|---|---|---|---|---|
| `case-edit__field` | select, input, textarea | `--space-2` | `--colour-border-strong` | 600, text |
| `security__field` | input, select | `--space-2 --space-3` | `--colour-border` | 600 |
| `recheck__field` | select, textarea | `--space-2 --space-3` | `--colour-border-strong` | 600, text |
| `library__field` | **select only** | `--space-1 --space-2` | `--colour-border` | normal, muted |

The security modals are in the same folder and looked right, which is why only the library's
looked wrong.

## What changed

`.library__field` now carries exactly what `.case-edit__field` carries, on the project owner's
instruction to match Edit case details:

- `select`, `input` and `textarea` share `padding: var(--space-2)`, `font: inherit`,
  `--colour-border-strong` and the standard radius; `textarea` resizes vertically only.
- The label span is 600 in `--colour-text`.
- `.library__draft-row`'s own inputs and select get the same treatment — they were unstyled
  too, and the checkbox is left alone, as `.library__check` expects.
- The questions fieldset takes the section radius and a 600 legend, like `.case-edit__section`.
- `.library__form-actions` gains `justify-content: flex-end` and top margin.
- `.library__btn` takes the reference's `var(--space-2) var(--space-4)`, and the ghost button
  is outlined in `--colour-border-strong` rather than the action colour — two action-coloured
  outlines side by side read as two primary buttons.
- **Cancel now comes before the action** in `SectionModal`, `QuestionModal` and `RetireModal`,
  so the primary sits right-most as it does on Edit case details.

A comment in the CSS names the reference and asks that the two be kept in step.

## How it was checked

The complaint was visual, so the evidence is visual. A harness page inlines the real
`tokens.css`, `base.css`, `Modal.css`, `QuestionLibraryPage.css` and `CaseEditPanel.css` and
renders **Add a section** beside **Edit case details**, screenshotted in a browser before and
after. Before: raw browser inputs, a monospace textarea, muted labels, small buttons,
left-aligned actions. After: the two dialogs are indistinguishable in treatment.

| | Step | Result |
|---|---|---|
| 1 | `npx tsc -b` | clean |
| 2 | `npx vitest run` | **484 passed**, 37 files |
| 3 | `npx eslint src/features/admin` | clean |
| 4 | `npm run build` → `npx pa app push` | built clean, **pushed successfully** |

## Not done here

- The four field treatments are not unified behind one shared style. That is the reason this
  drifted and is worth doing, but it touches every feature's CSS and would risk visual
  regressions well outside what was reported. The library now matches the reference the
  project owner named.
- The **Add a question** button still spans the questions box. It is an add-a-row affordance
  with no equivalent on the reference, so it was left; making it size to its content is a
  one-line change if preferred.
- Committed on `feat/checklist-administration`. Merging to `main` is the project owner's call.
