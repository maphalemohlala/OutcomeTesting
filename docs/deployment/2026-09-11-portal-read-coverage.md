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
