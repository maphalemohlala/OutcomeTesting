# Checker feedback batch, 2026-09-24

Project owner direction, 2026-09-24, with three choices settled the same day:
N/A is a per-row column; "assigned to" goes on portal and Code App tables; the
remediation attachment is the whole Remediation and escalation form.

| # | Change | Where |
|---|---|---|
| 1 | Every case table shows who the case is assigned to | Portal: My Work, Tax/AQS review lists (queue and allocated), case detail reviews table. App: case worklist. The case list already carries Tax checker / AQS checker. |
| 2 | Suitability subsections headed by name only ("Client Objectives & Information (COBS 9.2)", not "E1. ..."); S-CD help text removed | Portal, app, emailed PDF, reference file; S-CD `al_helptext` cleared in seed and DEV |
| 3 | CRP takes N/A on every row | New response type `PassFailInsufficientNa` (120910012) on Q-CRP-01..04 |
| 4 | "Back to your queue" at the foot of a check | Portal review page: Tax review -> /tax-reviews, AQS -> /aqs-reviews |
| 5 | E4 concessions (Q-E4-03) takes N/A | Same new type; Suitability grid gains an N/A column, ticked only where the row offers it |
| 6 | Primary root cause takes several ticks | New response type `MultiSelectRootCause` (120910013) on a successor version of Q-GR-02, answered in `al_answerchoices`; the nine causes added to that option set |
| 7 | Emailed PDF matches the page's own printout | PdfWriter gains ruled tables with tick boxes; CompletedCheck draws the whole form in force for the review, answered or not |
| 8 | One file per check, plus remediation | Tax check and AQS check each their own PDF; a Remediation PDF when any action is completed. Several files ride one outbox row. |

Out of scope: TEST/PROD promotion (owner's call each time).

## Order

1. Plug-ins, test first: ResponseRules, GradingRules, ResponseProgress root-cause clear,
   PdfWriter tables, CompletedCheck form, remediation document, multi-attachment outbox and drain.
2. Portal templates. 3. Code App. 4. Schema XML and seed. 5. Decision log and catalogue.
6. DEV: option values, question-version types, Q-GR-02 successor, S-CD help text, assembly,
   templates, app. Verify on DEV.
