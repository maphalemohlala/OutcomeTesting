# The AQS checker's notes for the adviser - DEV

Date: 2026-09-30

## What was asked

"Case Notes and Even better if need to show on the remedial page for the adviser." The owner chose to show them on the Remediation page and the case record, in a separate AQS panel.

## What changed

- A new web template, **OT AQS Notes** (`a1000000-0000-4000-8000-0000000000d9`). It is a read-only "AQS check notes" panel showing the AQS checker's **Case Notes** (Q-GR-03) and **Even Better If...** (Q-GR-04), each under its own label.
- It is included on **OT Remediation**, directly under the Tax check notes and above the actions, and on **OT Case Detail**, above Remediation and escalation.
- The rules are the same as the Tax panel's:
  - only submitted AQS checks are read;
  - nothing is drawn when both boxes are empty;
  - when a case has more than one submitted AQS check, each note carries that check's date;
  - plain text is escaped with its line breaks kept.
- It reuses the Tax panel's styling, so the stylesheet did not change.

## A fault found in the Tax panel, fixed

The first DEV check showed both labels over empty boxes. A plain-text answer has no rich text, so that column comes back null, and this site's Liquid treats null as not equal to `''`. The panel therefore took the rich-text branch and drew an empty box.

**OT Tax Notes had the same fault.** On the Remediation page, every Tax "Case notes" (Q-TAX-03) has been an empty box since the panel was built. Only the rich-text "Tax remedial" showed. Both templates now read each column through `default: ''`. The test that pins this failed on both templates before the fix.

## Tests

- `portalAqsNotes.test.ts` (11): the questions, the submitted-only and AQS-only filters, the empty gate, escaping, nothing editable, where each page includes the panel, and the null guard on both templates.
- `e2e/aqs-notes.e2e.ts` (3): the note **text** is on both pages, and every Tax note that is labelled has text under it.
- App suite 1229/1229; `tsc -b` clean.

## DEV

| Step | Result |
|---|---|
| `createwebtemplate ... 0000000000d9 "OT AQS Notes"` | Created, verified by read-back |
| `addcomponent ... 10433 ...d9` | In OutcomeTesting |
| OT Remediation, OT Case Detail, OT Tax Notes | Pushed. DEV's copies were first confirmed identical to `main` |
| Browser, as Service Account | 920929001 and 900000001: the panel with both notes on both pages. 920929001's Tax "Case notes" text now shows. 900000006 (no AQS check): no panel on either page |

## TEST, when the owner says

The new template and the three changed templates travel with the next solution import. Afterwards, compare OT Remediation, OT Case Detail, OT Tax Notes and OT AQS Notes on TEST with the repo by id, as a direct push has masked imports before.
