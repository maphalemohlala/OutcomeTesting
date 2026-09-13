# The case tiles, drawn as My work draws them — DEV, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`). **DEV only.** TEST and PROD
are untouched, by project owner direction of 2026-09-12.

Reported by the project owner: "ensure that the cards styling on cases match that of the my
work page."

---

## What was different

Both pages use the same component. `OT My Work` opens with a bare `<ul class="ot-tiles">`;
`OT Case List` had the identical list wrapped in a `<section class="ot-card">`, with a header
bar reading "Cases by stage", a total, and an intro paragraph.

`.ot-tiles` draws its own border and uses a 1px grid gap over a border-coloured background, so
inside a card it became a bordered strip inset within a bordered box — a box in a box, with a
grey gutter down both sides where the card's own padding fell, and the tile row narrower than
the card containing it. The stylesheet even carries `.ot-card .ot-tiles { margin-bottom: 0 }`
to make the nesting survivable, which is how long it had been that way.

The tile styling itself was never the problem. The wrapper was.

## What changed

The wrapper is gone. `OT Case List` now opens with the tile strip exactly as `OT My Work`
does: full width, edge to edge, followed by the filters and then the results card. The two
pages now have the same shape — strip, filters, card.

Removed with it:

- **"Cases by stage" / "My cases by stage"** and its total. The card immediately below names
  the same scope — *My cases*, *All cases*, *Filtered cases* — and carries its own count, and
  every tile still says what it counts, so the page does not lose the number. A bare `<h2>`
  was not an option in its place: nothing in the stylesheet dresses a heading outside a card,
  so it would have arrived as a browser default, which is the fault this change exists to
  remove.
- **"Where each case is waiting. Select a card to open the cases behind it."** `My work` has
  no such line above its tiles, and its own tiles are links in the same way.
- The **`n_all` aggregate query** behind the total, which had no other reader. Leaving it would
  have run a count of every case in the environment on every page load for a number nobody
  sees.

No stylesheet change: `.ot-tiles` and `.ot-tile` are shared with the reference and were already
right.

## How it was checked

The complaint was visual, so the evidence is. A harness page inlines the real
`outcome-testing.css` and renders the Cases opener beside the My work opener, screenshotted in
a browser before and after. Before: a grey header bar, an intro, and the tile grid inset inside
a card with gutters either side. After: the two strips are the same object on both pages.

| | Step | Result |
|---|---|---|
| 1 | Liquid balance | 285 opens / 285 closes; `if` 74/74, `for` 3/3, `comment` 13/13, `fetchxml` 7/7 |
| 2 | No Liquid delimiter inside a comment; `<section>` tags balanced | clean |
| 3 | Every reference to the removed total | gone — asserted by the edit script |
| 4 | `pushwebtemplate … OT Case List` | `a1000000-…-014`, **28891 → 28716 chars** |

## A note on the five tiles

`My work` has four tiles and `Cases` has five, and `.ot-tiles` shows an unfilled grid cell as a
solid grey block. At the page's real width — `.ot-page__inner` is 82rem against a 11rem minimum
per tile — five sit on one row, so no cell is left over. It only appears in a narrow column,
which is where the preview showed it.

## Not done here

- Committed on `feat/checklist-administration`. Merging to `main` is the project owner's call.
