# Grading, final outcome and notes in every letter - DEV

Commit `edb6546`.

- Every letter, built-in or your own, may use `{{grading}}`, `{{finalOutcome}}` and `{{notes}}`.
  Before, only three built-in letters offered grading or final outcome, and only the sign-off
  letters offered notes. Letters you create offered none of them.
- Where the event that sends a letter supplies no value, the server reads it off the case. Grading is
  the latest grade, final outcome falls back to the latest sign-off's, and notes are the latest
  sign-off's notes. A value the event supplies wins. Each token comes out empty where the case
  has not got that far.

Tests: 1,854 plug-in tests and 1,384 app tests passed.

## DEV (`org0b075da8`)

- `pushassembly`: 403,456 bytes, sha256 `12EF9ED5509BCE41...`, read back identical.
- Code App: `index-DoTkHlAu.js`, `pa app push`. The bundle carries the new token help.
- **Real letter.** A temporary letter of our own on "Case passed", addressed to Service Account,
  using all three tokens. The DEV guard accepted it, which the previous assembly refused. Case
  **951019001** was imported, assigned, answered as a pass and submitted. The letter was queued as:
  - subject `Token proof 951019001: Pass / `
  - body `Grading: [Pass]`, `Final outcome: []`, `Notes: []`

  A straight pass records no final outcome and has no sign-off, so those two are rightly empty.
  The temporary letter was then deleted and read back as gone. Case 951019001 stays in DEV as
  proof data. DEV delivers no email, so its four letters rest queued.
- **Not checked on screen:** the saved session's Entra cookies have expired. The player sends the
  browser to Microsoft sign-in.

## TEST

Not promoted. The DEV version bump and managed export were refused to the agent ("Production
Deploy"). The steps are those of `2026-10-19-fail-points.md` with version **1.0.30.0**.
