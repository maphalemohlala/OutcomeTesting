# Deployment — first managed install into TEST

Date: 2026-09-11
Target: `Env_AQ_Test` (`org37995f36.crm11.dynamics.com`, environment `017a6f77-f0b1-e26f-b6df-97a8d64685ac`)
Source: `Env_AQ_Dev` (`org0b075da8.crm11.dynamics.com`)
Deployed by: `svc.automate.aq@ascotlloyd.co.uk`

The first deployment to anything other than DEV. Every earlier record in this folder is a DEV
deployment, so there was no TEST runbook to follow; this is that runbook, written from the run.

TEST was a clean slate beforehand — no `OutcomeTesting` solution, no `AscotLloyd` publisher and
no Power Pages site — so this is a first install, not an upgrade.

## Before starting

Code Apps had to be enabled on TEST first (project owner, 2026-09-11). That is not optional
housekeeping: the solution **carries the Code App** as `al_ascotlloydoutcometesting_72932`, and
the import fails on an environment that cannot host one.

`pac` needed no new auth profile. The existing profile is UNIVERSAL, so `--environment` accepts
the TEST URL directly.

## Completeness audit, before export

Run against DEV, because a solution is only as complete as what was added to it — and this
project has twice found components live in DEV but absent from the solution.

| Component | In DEV | In solution |
|---|---|---|
| `al_` tables | 24 | 24, **subcomponents** behaviour |
| Custom columns on the system `contact` table | 3 | 3 |
| Standalone SDK steps | 19, all Enabled | 19 |
| Custom APIs | 25 | 25, plus their 25 implementation steps |
| Plugin assembly | 1 | 1, MD5-identical to the current Release build |
| Power Pages components | 254 | 254 |
| Security roles | 2 | 2 |
| Code App | 1 | 1 |
| Model-driven apps | 0 | — none exist |
| Power Automate flows | 0 | — none exist |
| Connection references, environment variables | 0 | — none, so no settings file is needed |

Two of those needed reconciling rather than counting.

**44 SDK steps in DEV against 19 in the solution is not a gap.** 25 of the 44 are
`CustomApi '…' implementation` steps, which belong to their Custom API and travel with it.
19 standalone + 25 implementations = 44.

**The three `contact` columns are the ones worth checking by hand.** `metadatamembership`
reports `al_` tables, so by construction it cannot see custom columns on a *system* table —
and `al_claimrequest`, `al_signoffrequest` and `al_regraderequest` carry the portal's entire
write path, since claim, sign-off and regrade each PATCH a contact column and let a plug-in do
the work. Had they been missing, TEST would have imported cleanly, registered the plug-ins, and
failed every portal write against a column that does not exist. Confirmed present by reading
the exported XML, which carries an `<Entity><Name>Contact</Name>` block whose `<attributes>`
hold all three.

**Build from DEV, not from `src/`.** `src/` has no `CanvasApps` folder — the Code App source
lives in `app/` and only becomes a solution component once pushed into an environment. A
solution built from `src/` through the cdsproj would ship without the Code App. Anyone wiring
up CI needs to know this.

## What was run

| # | Step | Command | Result |
|---|---|---|---|
| 1 | Export managed from DEV | `pac solution export --name OutcomeTesting --managed` | 1,093,637 bytes |
| 2 | Import into TEST | `pac solution import --activate-plugins --publish-changes --async` | Exit 0; customizations published |

`--activate-plugins` is not optional. An import that carries step XML without it lands every
step **disabled**, silently — that is what disabled the six `al_response` steps on 2026-09-02,
and nothing reports it. The flag is the reason step 2 needs no follow-up `setstepstate`.

## Verification, by query against TEST

| Check | Result |
|---|---|
| Solution | `OutcomeTesting` 1.0.0.0, **managed**, installed 09:57:18Z |
| Standalone SDK steps | **19, all Enabled** — none disabled by the import |
| Custom APIs | 25 |
| Power Pages components | 254 |
| Power Pages site | `Outcome Testing - outcometesting`, Active, same id as DEV |
| Table permissions | 16, including `Review Route - read`; no Global-scope write |
| Web templates | 16, **no null content**, all byte-identical to source |
| Web roles | 10 — the seven `AL Portal - *` plus Administrators, Authenticated and Anonymous |
| Security roles | `Outcome Testing App User`, `Outcome Testing App Admin` |
| Code App | `al_ascotlloydoutcometesting_72932`, managed |
| `contact` columns | all three resolve — the query returns rows rather than faulting |

The web-template check is not ceremony. `OT Review List` was found holding **null** content in
DEV earlier the same day, which no gate detects, so a fresh environment is worth reading back
the same way.

## Not done — TEST is not yet usable

Everything below is outside the solution by nature, not by oversight.

1. **Reference data.** All six reference tables are empty: `al_reviewroute`, `al_section`,
   `al_question`, `al_questionversion`, `al_failreason`, `al_checklist` all read 0. Load with
   the `importseed` verb, which is idempotent:
   `data/v8-seed` (the checklist), `data/route-seed` (the three review routes),
   `data/roles-seed`, `data/users-seed`.
   The routes matter more than they look: the AQS claim queue inner-joins `al_reviewroute`, so
   with no routes no case is claimable — the same failure shape as the missing read permission
   found earlier today (AD-115).
2. **Portal provisioning and authentication.** The site record imported and is Active, but the
   Entra ID configuration is environment-specific and deliberately not in source. It needs
   setting up in TEST before anyone can sign in.
3. **The Code App's Dataverse connection** will need authorising in TEST.
4. **Contacts and web role assignment.** The roles exist; nobody holds them.
