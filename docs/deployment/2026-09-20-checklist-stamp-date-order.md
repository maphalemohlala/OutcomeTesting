# The checklist stamp was read month-first

**Env_AQ_Dev, 2026-09-20.** Assembly `292,352` bytes, sha256 `cc8738b1592996b9`, verified
byte-identical against the local Release build after the push rather than by byte count.

## What was wrong

`ImportRules.ParseDateTime` carried this assumption in its own summary:

> The Code App resolves the workbook's serials to ISO before posting, so the time arrives as
> `2026-09-04T13:54:00`.

That is true of a cell the workbook stores as a **date-formatted serial**. The supplied
extract — `Pre-Advice Check Required Task 14-09.xlsx` — stores its `CompletionDate` cells as
**text**. Nothing converts a text cell, so the value arrives exactly as Intelligent Office
wrote it: `18/08/2026 13:54`.

None of the accepted formats matched it, and it fell through to `DateTime.TryParse` under
**invariant culture, which is month-first**.

| Extract writes | Was stored as | Now stored as |
|---|---|---|
| `18/08/2026 13:54` | *nothing* — no month 18 | 18 August 2026, 13:54 |
| `06/08/2026 14:49` | **8 June 2026** | 6 August 2026, 14:49 |
| `03/12/2026 10:00` | **12 March 2026** | 3 December 2026, 10:00 |

**The silent half is the worse half.** Above the twelfth the stamp vanished, and a case with
no `al_checklistcompleteddate` reads as a checklist nobody completed. At or below it the
value parsed *successfully* into the wrong month, and nothing downstream could tell.

### The guard that should have caught it

`IsNumericDateForm` exists for exactly this ambiguity — its own comment says `01/13/2026`
must be refused rather than read as 13 January. But it was asked about the **whole value**,
and the space and the colon in a time made it answer *no*. So the one form it was written to
refuse walked straight past it whenever a time was attached.

It now judges the **date part**, and requires a separator so a written-out
`31 Jan 2026 10:00` — which says outright which part is the month — is not caught by it.

## What changed

- `ParseDateTime` tries the UK forms explicitly after the ISO ones.
- `ParseDate` refuses a numeric date carrying a time instead of handing it to the general
  parser. Hardened there **as well**, because `ParseDate` is public and feeds every mapped
  date column: the same misreading was available to any extract that ever wrote a time into
  `StartDate`.

## Evidence

- **5 of the 9 new tests fail without the change**, and the two that matter most are the two
  faces of the defect: the dropped stamp and the silently wrong one.
- 1281 plug-in tests pass with it.
- End to end without touching DEV: the seed workbook was read through **the app's own**
  `workbookToCsv` — the reader the intake page uses — and the resulting CSV through
  `ImportRules.ParseCsv`. Six rows valid, none rejected, every stamp `2026-09-18 09:30`.
  Before the fix that stamp was null, because 18 is not a month.

## How it was found

Not by a test. By building a DEV seed workbook from the real extract the project owner
supplied, and reading how the parser would treat what was actually in its cells.

## Not deployed, and deliberately

The seed workbook itself is **not in the repository**. It names real people, because
`al_paraplanner` only resolves on an exact `fullname` match against an active contact, and
the standing rule is that fixtures and commits carry no personal data. It is at
`Downloads/Outcome Testing - DEV seed, six cases.xlsx`.

Nothing has been imported. The import creates cases, and a created case queues
notifications that DEV's `svc.automate.aq` mailbox — **Active, outgoing status Success** —
will actually deliver.
