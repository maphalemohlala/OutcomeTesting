# DEV and TEST compared, dimension by dimension

**2026-09-16.** After seeding TEST's configuration, both environments were compared across
everything queryable, to find what else had drifted. Every figure below is from a query against
the environment, not from the repo.

## Identical

| Dimension | DEV | TEST | Compared on |
|---|---|---|---|
| Solution components | 588 | 588 | count per component type |
| Plugin assembly | 1 | 1 | version, isolation mode, source type |
| Plugin types | 46 | 46 | type name |
| Plugin steps | 51 | 51 | stage, mode, statecode, rank, filtering attributes, impersonating user |
| Custom APIs | 31 | 31 | binding type, is-function, is-private, execute privilege |
| API request parameters | 135 | 135 | name, type, optionality |
| API response properties | 111 | 111 | name, type |
| App role privileges | 218 | 218 | privilege name and depth, both app roles |
| Web roles | 10 | 10 | name, authenticated-users flag |
| Portal site settings | 69 | 69 | name, value |
| Configuration tables | — | — | all seven, after the same-day seed |

All 51 steps are Enabled in both, so the 2026-09-02 "import disabled the steps" failure is not
present. The assembly matching on version and source type matters because `pushassembly` uploads
`bin/Release` without building it.

Power Pages components report as type 10433-10435 in DEV and 10443-10445 in TEST. Those are the
unmanaged and managed type codes for the same 259 components, not a difference.

## The one real divergence: al_pagepermission

Active, role-coded rules: **DEV 55, TEST 64.** Nine rules exist only in TEST, seven differ in
level, and none exist only in DEV — so the drift runs one way, with **TEST looser than DEV**.

| Resource | Role | DEV | TEST |
|---|---|---|---|
| page.reviews | T&C Supervisor | Manage | Edit |
| page.remediation | Tax Reviewer | — | View |
| page.remediation | AQS Reviewer | — | View |
| page.exports | Tax Reviewer | — | View |
| page.exports | AQS Reviewer | — | Edit |
| page.cases | Adviser Remediation | — | View |
| page.imports | Administrators | Edit | Manage |
| page.dashboard | Administrators | View | Manage |
| page.reports | Administrators | View | Manage |
| export.generate | Administrators | Edit | Manage |
| question.retire | Administrators | Edit | Manage |
| remediation.complete | T&C Supervisor | Manage | Edit |
| command.assign / regrade / signoff | Administrators | — | Manage |

**Left as it stands, by direction (2026-09-16).** Recorded rather than changed: whoever reads
this next should know TEST is the more permissive of the two and that this was a decision, not
an oversight.

## Correction to the permission-gate note

`2026-09-16-permission-gate-privileges.md` closes by saying `page.reviews` and
`page.remediation` have no rule at all in TEST, so nobody there can open a Tax or AQS review.
**That is no longer true.** TEST now grants `page.reviews` Edit to both `AL Portal - Tax
Reviewer` and `AL Portal - AQS Reviewer`, matching DEV, and grants both View on
`page.remediation` where DEV grants neither. TEST held 12 rules when that note was written and
holds 66 now. Reviews in TEST are not blocked.

## Ruled out, not gaps

- **17 Inactive `al_pagepermission` rows in DEV** carrying a null `al_rolecode` and an
  `al_approle` value — leftovers of the older role model, all deactivated. TEST not having them
  is correct; DEV is the environment with the dead config.
- **33 extra environment variables in TEST**, every one a Microsoft platform variable
  (`msdyn_*`, `powerpages_*`) from first-party apps installed there. Every variable belonging to
  this solution matches.
- **`al_outcomecase`: DEV 22, TEST 0.** Transactional data, deliberately not copied.

## Method note

Two audit queries gave false results before they gave true ones, both worth remembering:

- A FetchXML `like 'al_%'` treats `_` as a single-character wildcard, so it matched
  `allowedmcpclient`. Writing `al!_%` without declaring an escape character matched nothing at
  all. Neither form is safe; check the row count against something known.
- Comparing `customapirequestparameter` on `uniquename` alone is meaningless — parameter names
  such as `UserId` repeat across APIs, and an environment with more first-party apps installed
  carries thousands of them. The key has to be (API, parameter), scoped to this solution's APIs.
