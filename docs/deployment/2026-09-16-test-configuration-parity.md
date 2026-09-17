# TEST had no configuration data, and the import said so

**2026-09-16.** After the Basic User fix let Adam Strumidlo's import run in TEST, every row
came back an exception. The validation report
(`BATCH-20260916151357-AAECAD-validation-report.xlsx`) held 14 rows:

| Count | Reason |
|---|---|
| 10 | `Review route 'ROUTE-TAX-AQS' is not configured.` |
| 3 | `Review route 'ROUTE-AQS' is not configured.` |
| 1 | `No checklist items are selected, so the review route cannot be determined.` |

## What was wrong

The 13 route failures were literal. `al_reviewroute` held **zero rows in TEST**; DEV held the
three the derivation expects. `DeriveRoute` (BR-004) resolves a route code per row and refuses
the row when the code does not resolve, so an empty route table fails every row.

Not a privilege false-negative: checked as a System Administrator, the table is genuinely
empty, and both app roles already grant read on `al_reviewroute` at Global depth.

The 14th row (TaskID `253925362`) has no checklist items ticked, so there is nothing to derive
a route from. That row is a source-data problem and fails in DEV too. It is the only row where
the file is at fault.

TEST was missing far more than routes. The managed solution carries schema, not data, so none
of the configuration had ever been seeded:

| Table | DEV | TEST before | TEST after |
|---|---|---|---|
| al_reviewroute | 3 | 0 | 3 |
| al_checklist | 1 | 0 | 1 |
| al_checklistversion | 1 | 0 | 1 |
| al_section | 16 | 0 | 16 |
| al_question | 54 | 0 | 54 |
| al_questionversion | 60 | 0 | 60 |
| al_failreason | 20 | 0 | 20 |

Seeding routes alone would have moved the failure rather than fixed it: BR-013 refuses a claim
when no checklist version is in force, and TEST had none.

## Why not data/v8-seed

`data/v8-seed` has drifted behind DEV — it packages 12 sections and 46 questions against DEV's
16 / 54 / 60, the checklist-administration work having moved on. Importing it would have given
TEST an *older* checklist than DEV rather than a copy of it.

So `data/config-seed` was generated from DEV's live rows instead, and carries routes, fail
reasons and the whole checklist tree in one package. Two things the v8 schema lacks:
`al_questionversion.al_effectiveto` (set on 8 of the 60 rows — superseded versions) is now
declared, and the routes travel with the checklist rather than in a separate package.

Every row in DEV is Active, so no state handling was needed. Record ids are DEV's own;
`ImportSeed` matches on the `<table>code` business key and remaps lookups through its id map,
so parents resolve before children in one pass.

## What was deployed

```
importseed https://org37995f36.crm11.dynamics.com data/config-seed \
  --confirm https://org37995f36.crm11.dynamics.com
```

`Done. 155 created, 0 updated.` **TEST only.** Verified afterwards by row count: all seven
tables now match DEV exactly.

## Solution components: nothing is missing

Checked because it looked like a gap. DEV's `OutcomeTesting` solution and TEST's hold **588
components each, every type matching**:

Entity 24, Attribute 4, Role 2, PluginAssembly 1, SdkMessageProcessingStep 20, CanvasApp 1,
CustomAPI 31, request parameters 135, response properties 111, Power Pages components 257 + site
1 + language 1. The Power Pages types report as 10433-10435 in DEV and 10443-10445 in TEST
because those are the unmanaged and managed type codes for the same components.

Three things are outside the solution, all correctly so:

- **The 31 `CustomApi '…' implementation` steps.** Only the 20 table-message steps are explicit
  components; a Custom API's implementing step is generated from the API definition on import.
  Proven rather than assumed: TEST holds all 51 steps and all 51 are Enabled.
- **`al_al_failreason_al_response`** — an N:N intersect, carried by its relationship, not as an
  Entity component of its own.
- **`allowedmcpclient`** — a platform table that only matched the audit because `_` is a
  single-character wildcard in a FetchXML `like`.

Configuration rows are data, not components: they belong in a seed package, which is what
`data/config-seed` now is.

## Still different, deliberately

- **`al_pagepermission`** — DEV 81 rows, TEST 66. Not touched here: these rules decide who can
  open what, so changing them is an access decision rather than parity housekeeping. Audited in
  full afterwards and left as it stands by direction — see
  `2026-09-16-dev-test-consistency-audit.md`, which also corrects the permission-gate note's
  claim that reviews are blocked in TEST. They are not: TEST grants `page.reviews` Edit to both
  Tax Reviewer and AQS Reviewer, as DEV does.
- **`al_outcomecase`** — DEV 22, TEST 0. Transactional case data, deliberately not copied;
  TEST's cases should come from a real import.
