# Deployment — TEST upgrade to 1.0.5.0, and the three things a solution cannot carry

Date: 2026-09-21
Target: `Env_AQ_Test` (`org37995f36.crm11.dynamics.com`, environment `017a6f77-f0b1-e26f-b6df-97a8d64685ac`)
Source: `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

**This is an upgrade, not a first install.** TEST already carried `OutcomeTesting` **1.0.4.0
managed** from 2026-09-11. APP-166 asks for a clean environment; that was not available and the
row is recorded as an upgrade rather than reworded to fit.

Carries the five UAT fixes made the same day: the AD-057 lifecycle guard (F53), radio naming
for screen readers (F54), the unassigned-action label (F51), overdue target dates (F52) and the
phone-width checklist table (F55).

## What was run

| # | Step | Result |
|---|---|---|
| 1 | `pac solution online-version … --solution-version 1.0.5.0` (DEV) | 1.0.4.0 → 1.0.5.0 |
| 2 | `pac solution export --managed` | 1,267,304 bytes |
| 3 | Audit the export **before** importing | see below |
| 4 | `pac solution import --activate-plugins --publish-changes --async` | async job completed in 1m54s, published |

`--activate-plugins` is not optional and never has been: an import carrying step XML without it
lands every step disabled in silence. That is what disabled the six `al_response` steps on
2026-09-02.

### The pre-import audit, and why it is done before rather than after

A step whose plug-in type never reached the solution imports as a broken reference. Registering
a type and registering a step are two commands, and only the second says "added to the
OutcomeTesting solution" — so the type is the one that can be quietly left behind.

`customizations.xml` was read directly and carries `CaseStatusGuardPlugin` twice, as a
`PluginType` and as an `SdkMessageProcessingStep` with `FilteringAttributes="al_casestatus"`,
`Stage="20"`, `Mode="0"`. `solution.xml` carries 26 Entities, 24 SdkMessageProcessingSteps,
2 Roles, 1 PluginAssembly and 1 CanvasApp.

## Verification in TEST

| Check | Result |
|---|---|
| Solution | `OutcomeTesting` **1.0.5.0**, managed |
| New guard step | present, **`statecode 0` (Enabled)**, stage 20, sync, filtered to `al_casestatus` |
| `al_` Custom APIs | 31 — same as DEV |
| Web roles | 10 — same as DEV |
| Table permissions | 17 — same as DEV, six writable, **no Global-scope write** |
| Security roles | 2 |
| Power Pages components | 262 against DEV's 261 — one more in TEST, predating this run |
| Web templates | four changed today are **byte-identical** to DEV |

### Two comparisons that lie if you read them straight

**Web template `content` differs by exactly 2 characters on every template, and that is not
drift.** `content` is itself a JSON document `{"source": "…"}`, and the export/import round-trip
rewrites that wrapper's line endings from CRLF to LF. Compare the inner `source`: OT Answer
Options is sha256 `dae98702e5ff1152` in both. Anyone diffing `content` lengths will chase this.

**Reading table-permission role bindings faults, in both environments.** `$expand` on
`mspp_entitypermission_webrole`, and the equivalent FetchXML, both return `Object reference not
set to an instance of an object` from a platform plug-in — **in DEV as well as TEST**, which is
what rules out a promotion defect. This site uses the enhanced data model, so the authoritative
rows are `powerpagecomponent` type 18 and the bindings are the `adx_entitypermission_webrole`
array inside each component's content. Read that way, every writable permission is bound to a
named `AL Portal - *` job role and none to Authenticated Users.

## Three things the solution cannot carry, and what was done about each

All three are **rows, not components**. A managed install cannot bring them and nothing
reconciles them afterwards.

### 1. Q-TAX-04 is missing from the repository seed (F56)

TEST held 54 questions with no `Q-TAX-04 "Tax Remedial"`; DEV holds 56. The cause is not a
skipped step — `data/v8-seed/data.xml` **does not contain the question at all**. It was added
straight to DEV on 2026-09-19 and never back-ported, so any environment seeded from the
repository comes up without it, **including PROD**.

Created in TEST with `al_AddQuestion` against the existing `S-TAX` section (same GUID in both).
**The seed file is still wrong and that is the fix that matters.**

A promotion consequence worth knowing: `al_AddQuestion` refuses a past effective date — *"a
question cannot have been owed by a review already answered"* — which is correct. So a promoted
question takes the date it was promoted: Q-TAX-04 is effective **2026-09-21** in TEST against
2026-09-19 in DEV. Only a seed package that writes the row directly can preserve the original.

### 2. Page permissions had drifted (F57)

TEST held 66 `al_pagepermission` rows against DEV's 84; restricted to active rows both read 64,
with nine differing each way. TEST granted **nobody** `case.duedate`, `page.admin.advisers` or
`page.admin.templates`.

Those nine were created with `setpagepermission` at the levels read back from DEV. The
**residual drift was left alone deliberately**: eight active grants still differ by level and
TEST carries seventeen DEV does not. Levelling them is a decision about which environment is
authoritative — and quietly making TEST match DEV could remove access somebody is relying on.

One row read like a serious finding and was not: TEST holds
`AL Portal - Adviser Remediation / command.regrade`, which looks like an adviser being able to
regrade. It is `statecode 1`, **inactive**, and grants nothing. Checking the state rather than
the name is the difference between a finding and a false alarm.

### 3. No adviser-to-T&C-Manager mappings (F58)

`al_advisermapping` was **empty** in TEST, so `TcManagerRouting.ForCase` would find nobody and
no T&C Manager would be told a case is waiting for sign-off.

One demonstration row was created, mirroring DEV's, which proves the table and the lookup work.
**The real rows are business data and were not invented.** TEST's cases carry real advisers'
addresses; mapping each to their actual T&C Manager is the firm's supervision structure, and
guessing at it in a UAT run would be wrong on the facts and wrong about personal data.

## Not verified, and why

**The TEST portal has no host of its own.** Its site record carries
`outcometesting.powerappsportals.com` — DEV's hostname, copied by the import — so there is no
second site serving it. Everything portal-side above is verified at the data layer: component
content, permissions, bindings and rows. Nothing was confirmed by loading a page in TEST.

**`outcome-testing.css` is not demonstrated to have travelled.** It is a web *file*; its
component row carries only a 36-byte `filecontent` pointer and the two environments' values
differ. Push it with `pushwebfile` against TEST before anyone reads the portal there — today
that matters, because F52's overdue styling and F55's phone-width rule both live in it.

## One consequence of F53 that will surprise somebody

The lifecycle guard has **no bypass, including for an administrator**. Fixture data can no
longer be rewound by writing `al_casestatus`; a case moves through legal hops or not at all.
That is the point of it, and it is why the UAT fixture restore in DEV was done *before* this
build was deployed. Anyone resetting UAT data in TEST or PROD needs to move cases through the
lifecycle, or deactivate and re-create them.

## Case data purged from TEST (same day, at the project owner's direction)

`purgecasedata` was dry-run first, then run with `--confirm`. **628 of 628 rows deleted**
across the thirteen case-scoped tables:

| Table | Rows | | Table | Rows |
|---|---|---|---|---|
| `al_response` | 208 | | `al_importbatch` | 6 |
| `al_signoff` | 5 | | `al_exportrecord` | 2 |
| `al_remediationaction` | 38 | | `al_exportbatch` | 1 |
| `al_outcome` | 4 | | `al_notification` | 51 |
| `al_reviewinstance` | 17 | | `al_auditevent` | 213 |
| `al_caseassignment` | 21 | | `al_outcomecase` | 19 |
| `al_importexception` | 43 | | | |

The orphan check reported nothing outside the purge list pointing at those rows, before and
after. **`al_auditevent` was included deliberately and confirmed explicitly** — it is the
BR-012 / NFR-AUD-01 record, it is case-scoped, and it does not survive the cases it describes.
None of this is recoverable.

Verified empty afterwards: cases 0, review instances 0, remediation actions 0, audit events 0.

**Reference data and configuration were untouched, as intended**: 55 questions (including
Q-TAX-04), 16 sections, 3 review routes, 1 adviser mapping, 73 active page permissions, 10 web
roles, 17 table permissions, 23 web-role assignments, 23 user-role mappings — and the
`CaseStatusGuardPlugin` step still registered and Enabled.

### One thing noticed while verifying, which the purge did not cause

`al_role` reads **0 in TEST** against 11 in DEV. It is not in the purge list and was not
touched. It does not break anything: `PermissionHelpers` resolves a caller's grants through
`al_userrolemapping` → `al_pagepermission`, matching on the **`al_rolecode` text column**, and
the role names those rows carry are the portal web roles, which are all present. What is
missing is the catalogue, so the Security configuration page would list no custom roles in
TEST. Worth seeding before anyone tests role administration there.

### TEST now has no cases

That is the point of the purge, but it is also the next prerequisite: nothing portal-side or
app-side can be exercised until cases exist again. `importcases` is the verb, and
`data/outcome-case-upload-sample-valid.csv` and `data/six-route-cases.csv` are in the
repository for exactly this.
