# Deployment — table read coverage, and a web template that had been emptied

Date: 2026-09-11
Target: `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

Follows the `al_outcome` read permission deployed earlier the same day. That permission
unblocked the regrade panel (AD-111), the sign-off final outcome and the grade columns;
auditing *why* it had been missing found two more faults of the same class, recorded as
AD-115 and AD-116.

## What was deployed, in order

| # | Step | Command | Result |
|---|---|---|---|
| 1 | Restore `OT Review List` | `pushwebtemplate <orgUrl> …016 <source>` | 31177 chars written; content column had been null |
| 2 | Table permissions | `restoretablepermissions <orgUrl> <sitePath>` | 16 written — `Review Route - read` **created**, 15 updated unchanged |
| 3 | Solution membership | `addsitetosolution <orgUrl> OutcomeTesting` | site 1, languages 1, 254 components |

## Verification

Read back from the environment, not from the tool's own counts.

| Check | Method | Result |
|---|---|---|
| Table permissions, source vs deployed | `fetch` `powerpagecomponent` type 18, compared field by field including role bindings | 16, identical |
| Table read coverage | every `<entity>`/`<link-entity>` in `web-templates/` vs permissions granting read | all 12 queried tables covered |
| Web templates, source vs deployed | `fetch` type 8, `content.source` compared to `*.webtemplate.source.html` | all 16 byte-identical |
| Solution membership | `solutioncomponent` count, and the new id by query | 511 → 512; `…063` present as component type 10433 |
| Web roles and page rules | `fetch` types 10 and 11 vs `webrole.yml`, `webpagerule.yml` | 10 roles, 6 rules, identical |
| Permission relationships | `relationships` on the five tables the permissions name | all five resolve to real relationships |
| Release gates | `Check-ComponentIds.ps1`, `Check-PortalSecurity.ps1` | 242 ids clean; 11 assertions pass |
| Plug-in suite | `dotnet test` | 682/682 |

The new assertion was proved in both directions rather than only observed passing: with
`Review-Route---read.tablepermission.yml` moved aside the gate exits 1 and names
`al_reviewroute` and the three templates that query it; restored, it exits 0.

## Impact of what was fixed

`al_reviewroute` had no table permission at any point. Three consequences, all silent:

- **The AQS claim queue was empty for every user.** `OT Review List` joins the route
  `link-type="inner"` to test `al_requiresaqsreview`, so with the route unreadable the join
  returned nothing, `unassigned.size` was 0, and the claim card never rendered. No AQS
  checker could claim a case through the portal. Case IO-300002 (Desmond Achebe, AQS only)
  was waiting in that queue when this was found.
- **The route filter was inert** on the case and review lists. It fails safe: the page
  validates the requested route id against the fetched list, and an always-empty list means
  the filter is dropped rather than misapplied.
- **The route column was blank** wherever it is joined `outer`.

`OT Review List` holding a null `content` meant `/tax-reviews` and `/aqs-reviews` were both
drawing the layout around an empty body, since one shared template renders both.

## Still open, deliberately not changed

- **`al_signoff` read is Parent-scoped to `AL Portal - T&C Supervisor` alone.** The sign-off
  history on `/case-details` and on the remediation page therefore renders empty for the
  other six roles, including `AL Portal - Outcome Testing Manager`, whose role is oversight.
  BR-008 is still met — a rejection reopens the action and `SignoffProgressPlugin` emits a
  notification carrying the notes — so what is missing is the history view, not the telling.
  Widening a read is OD-022 territory and is the project owner's call.
- **`AL Portal - Planner` has no holders in DEV.** Seeding, most likely, but it means that
  path has never been exercised.
- **Two cases sit at Awaiting Recheck with a grade and no final outcome** — IO-300005 and
  IO-SEED-TXA-01. They cannot be closed in bulk: the final outcome is the supervisor's
  judgement under BR-005 and the reason is mandatory under AD-031, so neither is derivable
  from the data. One supervisor action each, on the regrade panel that now renders.

## Full `Deploy-Portal.ps1` run

Run after the targeted steps above, as the reconcile. Both gates passed (242 ids; 11
assertions), 16 permission files were moved aside and 0 permission records needed stripping
from the manifest — it was already stripped from an earlier run, which is the state the
README says to leave it in. Upload succeeded in 41.7s; `restoretablepermissions` wrote 16;
verification by query reported **16 in source, 16 deployed, and nothing else**. Exit 0.

Re-checked afterwards rather than assumed: all 16 `OT *` web templates are still
byte-identical to source, `OT Review List` included. An upload is the thing most likely to
undo the restore in step 1, so that check is the point.

### One failed record, and why it is not a fault

The upload printed:

```
Updating table powerpagecomponent with record ID:f065878a-f8a9-f111-aaac-e4fade069307
FAILED due to Entity 'powerpagecomponent' With Id = f065878a-... Does Not Exist
```

That id is the `annotationid` in `web-files/outcome-testing.css.webfile.yml`, and the
manifest still tracks it under the web file content records. It is a **standard-model
leftover**: on this enhanced-model site the CSS bytes live in the component's `filecontent`
file column (`4977a36d-…`, `filecontent_name: outcome-testing.css`), not in an `annotation`.
Querying `annotation` for `objectid = …050` returns nothing at all, which is the enhanced
model behaving correctly rather than a missing file.

The stylesheet itself deployed: the component's `modifiedon` is `2026-09-11T08:40:31Z`,
written by this run. Nothing was lost, and the upload reported success overall.

It will print the same line on every future deploy. The fix is a manifest rebuild, which
`pac pages download` does truthfully — but a download also **re-arms the table-permission
section that `Deploy-Portal.ps1` deliberately strips** (see the README), so it is not worth
doing for cosmetics alone. Do it as part of the next deliberate download, not on its own.
