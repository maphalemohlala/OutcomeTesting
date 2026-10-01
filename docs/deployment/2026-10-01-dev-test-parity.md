# DEV and TEST compared, and the solution checked for gaps

**2026-10-01.** Read-only. Every figure here comes from querying both environments through the
registration tool's `webapimany`, not from the repo. Both are at **1.0.16.0** (DEV unmanaged,
TEST managed, imported 2026-09-30 11:36Z).

## Short answer

- **Solution membership: no gaps.** Every DEV component of every type is in `OutcomeTesting`.
- **TEST matches DEV everywhere except the three changes made on DEV after the 1.0.16.0 export.**
  Those are waiting for the next import (1.0.17.0).
- **One problem was found that the next promotion would trip over.** The documented
  `seedlistoptions` step would create 15 duplicate dropdown options on TEST, and 18 TEST cases
  already show a blank "Pre or post check". There is a fix, ready to run (see below).
- The checklist content has been edited separately in each environment. That is user
  administration, not deployment drift, but the two checklists are no longer the same.

## Solution membership (DEV environment against DEV solution)

| Component | In DEV | Outside the solution |
|---|---|---|
| Power Pages components | 298, plus the site and its language | 0 |
| Custom APIs, request parameters, response properties | 32, 140, 115 | 0 |
| Plug-in assembly, table-message steps | 1, 33 | 0 |
| Security roles | 3 | 0 |
| Code App | 1 | 0 |
| `al_` tables | 26, plus `contact` | 0. The two N:N intersects travel with their relationships |
| Custom `contact` columns | 7 | 0 |
| `al_` global choices, web resources, unmanaged flows | none exist | - |
| Environment variables | 1 (`al_PortalBaseUrl`) | 1, deliberately: each environment holds its own portal host |

The 32 "CustomApi … implementation" steps are generated from the APIs and are not separate
components. Both environments hold all 65 steps, and all 65 are enabled in both.

## What is the same in both

| Dimension | Compared on | Result |
|---|---|---|
| Solution components | count by type, and by name | Same, apart from OT AQS Notes (below) |
| Plug-in types | type name | 54 = 54 |
| Steps | message, table, stage, mode, rank, state, filtering columns, images, config | 65 = 65, all enabled |
| Custom APIs | binding, function/private, privilege, plug-in, every parameter and property | 32 = 32 |
| Columns on the `al_` tables and `contact` | type, required level, audit, create/update, length | Same, apart from the Vulnerable client lookup |
| Choice columns | every option value and label | Same, apart from the Vulnerable client list value |
| Keys, relationships, cascades, forms, views | definition | Same |
| Security roles | every privilege and depth, all three roles | 85 / 97 / 133, identical |
| Portal site settings | name, value, state (`mspp_sitesetting`) | 73 = 73, identical |
| Portal table permissions, web roles, pages, snippets, page templates | content and related records | Identical |
| Portal web files (all six text files) | content | Identical in both, and the same as the repo |
| Review routes, checklist, checklist version, fail reasons, notification templates | every column | Identical |
| Page permissions (active rules, by role and resource) | access level | 73 vs 74. TEST alone gives Administrators Manage on `command.assign` |
| The Tax and AQS teams | name, type | Same |

## The difference that should close: DEV is ahead of TEST

Everything here was deployed to DEV after the 1.0.16.0 export. DEV matches `main` in each case:
all 51 DEV web templates are byte-identical to the repo, and nothing under `plugins/` or
`app/src` has changed since the assembly and app were pushed, except one test file.

| Change | DEV | TEST |
|---|---|---|
| Vulnerable client becomes a managed list | lookup column, relationship, list value, 4 options | none of these |
| No decision codes on screen | assembly `e9863e42…`; templates pushed | assembly `26dc92c0…` (1.0.16.0) |
| AQS checker's notes for the adviser | OT AQS Notes (new); OT Remediation, OT Case Detail, OT Tax Notes | not present / older |
| Templates affected | OT Case Detail, OT Home, OT Remediation, OT Review Detail, OT Tax Notes, OT AQS Notes | 5 older, 1 missing |
| Code App | `appversion` 2026-09-30 13:50Z | 2026-09-30 11:36Z |

## Found: TEST's list options have no legacy values

Each managed dropdown option records the old choice value it replaced (`al_legacyvalue`). DEV's
options carry it. **TEST's 15 options for Product / solution type, Sample source, Case type and
Pre or post check do not.** Two consequences:

1. **Now.** 18 of TEST's 48 cases hold "Pre or post check" only in the old choice column, because
   the case import writes only that column. The fallback that labels such a case matches on the
   legacy value. With none to match, those 18 cases show the field as empty.
2. **At the next promotion.** `seedlistoptions` runs over all five lists and treats an option as
   already seeded only when its legacy value matches. On TEST it would therefore create a second
   "Protection", a second "Pre", and so on: 15 duplicates. It would then point cases at the
   duplicates.

**Fix: set the 15 legacy values on TEST before `seedlistoptions` runs.** The values come from
DEV's mapping, and each one was checked against TEST's own choice-column label: 15 of 15 match.
The request file is `artifacts/2026-10-01-parity/test-legacy-values-org37995f36.json`, 15 PATCHes
by row id. It is a data write to TEST, so the owner runs it:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; & plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe webapimany https://org37995f36.crm11.dynamics.com '@artifacts\2026-10-01-parity\test-legacy-values-org37995f36.json' artifacts\2026-10-01-parity\test-legacy-values.out.json
```

**Run by the owner, 2026-10-01: 15 of 15 succeeded.** Read back from TEST: 15 options carry a
legacy value, with no list holding the same value twice. All 18 legacy-only cases now resolve to
an option label (all "Pre").

The fix stands on its own: the 18 blank values show correctly as soon as it runs, before any
import. Afterwards, `seedlistoptions` will report the four existing lists as "already seeded",
backfill the 18 cases' lookups, and create only the four Vulnerable client options. It will also
backfill the 28 TEST cases that hold a Vulnerable client value.

## Different on purpose, or specific to one environment

- **The checklist content.** TEST's checklist administrators have made changes that DEV does not
  have. They added Q-AML-06 and re-versioned the four Consumer Duty outcome questions, Q-TAX-01
  and Q-AML-01. They also changed the answer type of two versions: Q-E1-01 v1, and Q-TAX-03
  "Case note" v2. DEV holds its own test versions instead, such as "[UAT-047 v2]". 47 vs 48
  questions; 68 vs 78 versions. Copying either way would overwrite one environment's
  administration, so it is left alone. **The checklist a checker sees on TEST is not the one
  they see on DEV.**
- **List options created on TEST** ("Decumulation", "Test 1" retired, a Products option "Test"),
  and a DEV-only "Test Role" in `al_role`. Test data from UAT.
- **People:** adviser mappings (DEV 3, TEST 6), user role mappings, cases (DEV 16, TEST 48),
  sending mailboxes. All per environment by design.
- **TEST-only portal records:** the Entra setting `AllowContactMappingWithEmail` and a web role
  called "New Role". Both were known at 1.0.15.0.
- **Plug-in trace log:** All on DEV, Off on TEST.
- **Platform extras on TEST:** first-party apps installed there add `msdyn_*` columns and
  relationships to `contact`, 38 connection references, a sales team template and one
  environment variable. None of them belongs to this solution.

## Ruled out (looked like differences, are not)

- Custom APIs, parameters, contact columns and the Code App have **different record ids** in the
  two environments, so they were compared by name.
- 156 views "differ" only in the per-environment table number embedded in their layout.
- The two queue-membership steps carry an "all tables" message filter on DEV and none on TEST.
  Both fire for every table.
- Four portal site settings differ only in their description text.
- Every configuration row differs in its organisation id. Several sections hold an empty
  "optional" flag on DEV where TEST holds false. Both are the same value.

## Re-check later the same day: the Review page is broken on TEST

At **07:01:44Z** the current OT Review Detail was pushed straight to TEST. It is identical to DEV
and the repo, but 1.0.17.0 has not been imported. TEST's case table therefore still has no
`al_vulnerableclientid`. The page's main query selects that column from the case, so Dataverse
rejects the whole query:

> 'al_OutcomeCase' entity doesn't contain attribute with Name = 'al_vulnerableclientid'

The query was run against TEST as written: it fails with that column and succeeds without it.
**Every review page on TEST has been failing to load its review since 07:01Z.** Nothing else
changed: the import has not happened, and the other four templates still differ.

Either of these fixes it:

- **Roll back:** the owner pushes the 1.0.16.0 template back. It was taken from commit `1110b03`
  and is 272,708 characters, the same as TEST held before the push.

  ```powershell
  $env:DOTNET_ROLL_FORWARD='Major'; & plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe pushwebtemplate https://org37995f36.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b artifacts\2026-10-01-parity\OT-Review-Detail-1.0.16.0.html
  ```
- **Go forward:** import 1.0.17.0, which adds the column. The pushed template is already the
  1.0.17.0 one, so step 4 below is then already done.

**Rolled back by the owner, 2026-10-01 07:06:51Z.** Read back from TEST: OT Review Detail is
272,708 characters, identical to the 1.0.16.0 file, and does not name the new column. The page's
review query now succeeds on TEST. The page was broken for about five minutes. Step 4 below
still applies after the import: TEST keeps the push-only copy, which now holds the old version.

## TEST - 1.0.17.0

The two owed DEV proofs were run first and passed. They are recorded in
`2026-09-30-vulnerable-client-managed-list.md`.

DEV was bumped to 1.0.17.0 and exported managed to `artifacts/2026-10-01-test-promotion/`. The
package was checked before import: version 1.0.17.0, managed, with the Vulnerable client column
and list value, OT AQS Notes, assembly `e9863e42…` and bundle `index-Cfg8omD4.js`. The agent's
import was refused ("Production Deploy"), so **the owner ran it**: job
`38fdfc0e-1a59-41d5-ad49-cbb9283eae67`, operation `248485d2…`, succeeded at 07:37:40Z.

A second break came first. At 07:34:22Z the new OT Review Detail was pushed again before the
import, and `seedlistoptions` ran too. The Review page failed again until 07:36:47Z, when the
1.0.16.0 template was pushed back, together with the import. That `seedlistoptions` run created
no options.

Read back from TEST after the import, with the full comparison run again:

| Check | Result |
|---|---|
| Solution | 1.0.17.0 managed; 643 components, the same as DEV by type and name |
| Assembly | sha256 `e9863e42…`, identical to DEV |
| Steps | 65, all enabled |
| Code App | `appversion` 2026-10-01 07:36:59Z |
| Columns, choices, relationships | Vulnerable client lookup, relationship and list value present; nothing DEV-only remains |
| Roles, site settings, web files | identical |
| Web templates against the repo | 50 of 51 identical; OT Review Detail still holds the 1.0.16.0 copy in its unmanaged layer |
| The new template's review query, run on TEST | succeeds now that the column exists |

### After the import (owner, 07:50Z)

The owner pushed OT Review Detail again and ran `seedlistoptions`. Read back from TEST:

| Check | Result |
|---|---|
| OT Review Detail | identical to the repo (274,122 characters) |
| Web templates against the repo | 51 of 51 identical |
| List options | 81; no list repeats a name or a legacy value |
| Vulnerable client options | Yes 10, No 20, Potentially vulnerable 30, N/A 40, each with its legacy value |
| Cases holding an old value with no option | **0** across all five lists. Before: Pre or post check 18, Vulnerable client 28 |
| Option on the wrong list | 0 |

**Five TEST cases show Post where their old column still says Pre** (300000015, 300000018,
900000002, 900000003, 900000005), and one DEV case does the same. They were not touched today;
the option had already been changed to Post by an edit. Choosing an option writes only the
lookup, and the old column is cleared only when the field is cleared. Every reader takes the
lookup first: the PDF, the export, the Code App and the portal header. Every screen shows Post.
`seedlistoptions` left them alone, so it fills only empty lookups and never overwrites a choice.
This is by design, not a fault.

**TEST now matches DEV** in every component and every deployed setting. The deliberate
differences listed above remain: checklist content, test data, people, mailboxes, trace
logging, and platform extras. TEST keeps push-only copies (unmanaged layers) on OT Review
Detail, OT Layout and `outcome-testing.css`, which all match the repo today. Re-push them after
any import that changes them.

## To bring TEST level with DEV (done 2026-10-01)

1. ~~Owner: the legacy-value PATCH above.~~ Done 2026-10-01.
2. Bump DEV to 1.0.17.0, export managed, import into TEST, and read back the version, the step
   states and the assembly sha256.
3. Owner: `seedlistoptions` on TEST, as in `2026-09-30-vulnerable-client-managed-list.md`. Safe
   now that step 1 is done.
4. **Owner: push OT Review Detail to TEST again after the import.** The component layers on TEST
   (`msdyn_componentlayers`) show an unmanaged "Active" layer on top of the managed one for
   **OT Review Detail, OT Layout and `outcome-testing.css`**. These are left by the direct pushes
   on 2026-09-30 at 12:03Z, after the 1.0.16.0 import. An active layer hides every later managed
   import of that component. OT Review Detail changes in 1.0.17.0, so without a re-push TEST would
   keep the old one. OT Layout and the css do not change, so they need nothing yet. The other four
   changed templates have only the managed layer, so the import reaches them.

   ```powershell
   $env:DOTNET_ROLL_FORWARD='Major'; & plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe pushwebtemplate https://org37995f36.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b powerpages\outcome-testing---outcometesting\web-templates\ot-review-detail\OT-Review-Detail.webtemplate.source.html
   ```
5. Compare the six templates on TEST with the repo by id.

The Vulnerable client note still lists two DEV proofs as owed before TEST: the Code App in a
browser, and a PDF. Either run them or have the owner waive them before step 2.
