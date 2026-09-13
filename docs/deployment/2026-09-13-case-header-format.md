# The case header, drawn as the document — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Project owner, 2026-09-13, with a screenshot of the Checker Checklist's header block: the
case details are to be drawn in that table format rather than in the current grouping.

---

## What changed

Both front ends drew the same eighteen fields, and neither drew them as the document does.

| | Was | Now |
|---|---|---|
| Code App | Four themed panels — Client, Adviser and paraplanner, Advice and product, Check and tax — as definition lists in an auto-fit grid | One ruled table, two label/value pairs to a row, the document's order |
| Portal | The right table shape already, but split across three themed sections with headings | The same one table |

The grouping was invented here; the document has no trace of it. A reader holding the paper
form had to hunt across four panels (or three headings) for a field the form puts in one
fixed place.

**The Code App renders `caseHeaderFields`** — the builder the review page's own header
already uses — so the two screens cannot drift on which fields the header carries or what
they are called. That is also why the labels moved: "For Tax team usage" rather than "Tax
team disposition", "Vulnerable client?" rather than "Vulnerable client", "Product / solution
type" rather than "Product/solution type". They are the document's words.

The four `CaseField[]` groups are **removed** rather than left unused. Nothing else read
them, and keeping them would have left a second, divergent set of labels in the codebase for
the next reader to pick by mistake. Nine assertions in `useCaseDetail.test.ts` moved onto
`detail.header` and its document labels.

**Case management keeps its own section on the portal.** Status, route, priority and due date
are this system's operational state, not fields of the paper form.

---

## A defect found while rewriting it

The portal rendered two choice columns by **object truthiness**:

```liquid
{% if c.al_taxcheckrequired %}Yes{% else %}No{% endif %}
{% if c.al_vulnerableclient %}Yes{% else %}No{% endif %}
```

That tests whether the option-set object is present, not what it holds. The object is present
whenever **any** option is set, so:

- **Tax check required** rendered **Yes** for a case answered No.
- **Vulnerable client** carries four options — Yes, No, Potentially vulnerable, N/A — of
  which **three rendered as Yes**, and an unset one as No.

This is the same class of fault as the `al_finaloutcome` cell fixed on 2026-09-11: an option
set read as a boolean, failing quietly and plausibly. Both now render through `.label`, so a
missing formatted value shows a dash — honest — rather than a confident wrong answer.

Neither was reported. Both were on screen.

---

## What ran

| | Step | Result |
|---|---|---|
| 1 | `pushwebtemplate … OT Case Detail` | `a1000000-…-015`, **31813 → 33525 chars** |
| 2 | `npm run build` → `npx pa app push` | built clean, **pushed successfully** |

Checked before the template push: Liquid 273 opens against 273 closes, 13 comment pairs
matched, no tag delimiters inside any comment; `section`, `table`, `tbody`, `tr` and `h2`
all balanced; 36 cells in the header section, being the 18 label/value pairs.

| Suite | Result |
|---|---|
| `npx vitest run` (app) | **464 passed** |
| `npx tsc -b` | clean |

No schema, assembly or solution change: this is rendering only.

---

## Notes for the next reader

The Code App builds its row pairs in the component rather than with CSS columns. A
two-column grid reads *down* one column and then down the other for a screen reader, where
the document reads left to right and then down. An odd final field takes empty filler cells
carrying no rule, so the table does not end on an empty box, and the whole table stacks below
45rem — four fixed columns at phone width squeeze every value to a character or two.
