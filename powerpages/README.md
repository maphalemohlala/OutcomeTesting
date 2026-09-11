# Power Pages portal

Source-controlled metadata for the Outcome Testing portal. This directory is **the portal and nothing else** — no Code App source, no Dataverse solution files.

## Separation

| Directory | Owns | Deploys with |
|---|---|---|
| `powerpages/` | Portal site metadata (this folder) | `pac pages upload` |
| `app/` | Power Apps Code App | `pa app push` |
| `src/` | Dataverse solution root, **packaged in full** | solution import |
| `brand/` | Authoritative colours, fonts, logos | consumed, never copied |

Portal metadata must never be placed under `src/` — that directory ships wholesale inside the managed solution. It must never be placed under `app/`, and Code App source must never be placed here; the two have separate build and release paths and mixing them breaks both (AD-048).

The front ends share the schema in `src/`, the server-side commands in `plugins/`, and the brand tokens in `brand/`. Sharing those prevents divergent business logic. Sharing UI directories would only cause deployment collisions.

## Site

| Property | Value |
|---|---|
| Friendly name | Outcome Testing - outcometesting |
| Website Id | `b4cfe195-fd15-42e8-94e5-f27bcceaf5fc` |
| URL | https://outcometesting.powerappsportals.com/ |
| Data model | Enhanced |
| Type | Traditional metadata-driven, not SPA |
| DEV environment | Env_AQ_Dev (`org0b075da8.crm11.dynamics.com`) |

## Working here

Always confirm the active environment before any download or upload:

```powershell
pac auth select --name AscotLloyd-DEV
pac org who
```

Download the latest DEV metadata before editing, so maker changes made in design studio are not overwritten:

```powershell
pac pages download `
  --path ".\powerpages" `
  --webSiteId "b4cfe195-fd15-42e8-94e5-f27bcceaf5fc" `
  --modelVersion Enhanced `
  --overwrite
```

Before uploading, check that no two components claim the same record id:

```powershell
powershell -NoProfile -File .\powerpages\Check-ComponentIds.ps1
```

`Deploy-Portal.ps1` runs this and `Check-PortalSecurity.ps1` for you and refuses
to upload if either fails, so run them by hand only when you want the answer
without deploying.

Record ids here are hand-minted and banded by component type (AD-059). Every
component — web template, page template, web page, table permission, web link —
is a row in the single `powerpagecomponent` table, so two components sharing an
id are one row and the upload silently replaces one with the other. Nothing
reports the loss; the component simply stops existing and every record pointing
at it resolves to the wrong type, which the portal answers with a generic error
page. This has already taken out `/cases` once and the primary navigation once.

Bands in use, minted under `a1000000-0000-4000-8000-0000000000NN`:

| Band | Component type |
|---|---|
| `10`–`1d` | web templates |
| `20`–`2a` | page templates |
| `30`–`36`, `40`–`46` | web pages |
| `50` | web files |
| `60`–`6f` | table permissions |
| `70`, `80`–`88` | web links and weblink sets |
| `90`–`97` | web roles |
| `a0`–`af` | site settings |
| `b0`–`bf` | page access control rules |

The table-permission band is narrower than it looks: ids `71`–`75` predate this table
and stay where they are — the band describes where *new* ids are minted, not a
relocation of existing components.

> **Filename trap.** This repository's table permission files are named differently
> from what `pac pages download` emits — for example `Review-Instance-All-Read.tablepermission.yml`
> here versus `Review-Instance---read-all.tablepermission.yml` from `pac`. A
> `--overwrite` download into `powerpages/` therefore leaves **two files per
> permission**, each claiming the same id. `Check-ComponentIds.ps1` catches this as a
> duplicate id — the point of this note is so the next person understands the
> duplicate rather than deleting the wrong copy.

Deploy with `Deploy-Portal.ps1`. It runs both gates, uploads, deploys the table
permissions and verifies the result by query:

```powershell
powershell -NoProfile -File .\powerpages\Deploy-Portal.ps1 `
  -OrgUrl https://org0b075da8.crm11.dynamics.com
```

`-OrgUrl` is mandatory — a deploy script must never guess its target. `-VerifyOnly`
queries the environment and reports without changing anything; run it after any
portal work, whoever did it. `-ForceUploadAll` reconciles, and is safe **only**
through this script.

> **Do not run `pac pages upload` by hand.** Both ways of running it damage this
> site, and there is no third way.
>
> With `table-permissions/` present the run **aborts** on the first parent-scoped
> permission (OD-034): `pac` 2.11.2 routes `adx_scope: 756150003` down the
> Standard-model path and writes to `adx_entitypermission`, which does not exist
> on an enhanced data model site. Components ordered after it are silently not
> deployed, and which ones those are varies with what is dirty on the day.
>
> With `table-permissions/` moved aside — the workaround used until 2026-09-06 —
> the upload succeeds and `pac` **deletes every table permission the manifest
> lists and the folder no longer contains**. This is not limited to
> `--forceUploadAll`. It fired on 2026-09-02, and again before 2026-09-06, which
> left DEV holding **2 of 13** permissions: the only survivors were the two the
> manifest did not know about. With the Global read grants gone every list
> template queries a table the user cannot read, and with `Review Instance -
> assigned to me` gone there is no reviewer write path at all — Submit cannot
> succeed for anyone. Fail-closed, so an outage rather than an exposure.
>
> `Deploy-Portal.ps1` takes the second path and removes what makes it dangerous:
> it moves the folder aside **and** strips the `adx_entitypermission` and
> `adx_entitypermission_webrole` sections out of the manifest, so `pac` has
> nothing to upload and nothing to reconcile away. It deploys the permissions
> with `restoretablepermissions` instead, then proves by query that all sixteen
> landed.
>
> It leaves the manifest stripped on purpose. `pac` does not own table permissions
> here, so that section is not tracking — it is the trigger above, armed for the
> next run. `pac pages download` rebuilds it from the environment, truthfully,
> whenever someone next downloads. **A download re-arms it**, so deploy through
> the script from then on, and never with a raw `pac pages upload`.

> `--modelVersion` takes `Enhanced` or `Standard` on PAC CLI 2.11.2. The `--modelVersion 2` form in the build guide (sections 14, 19 and Appendix A) is not valid syntax for this version. Verify with `pac pages download --help` after any CLI upgrade.

After uploading: refresh the site configuration cache, sync design studio if open, then test in an InPrivate session against each representative role.

## Current state

Shell and data wiring built 2026-08-29; the security model closed 2026-08-31 (sub-project B
of the portal security closure, PP-01, PP-02, NFR-SEC-01, NFR-SEC-02) and verified locally
against both gates below. The DEV upload of this branch's changes, the round-trip check and
the deletion verification are not yet run — see `docs/deployment/2026-08-31-portal-security-closure-deployment.md`,
which is a runbook to be executed, not a record of a completed deployment.

**Pages** — `/my-work`, `/cases`, `/case-details?id=`, `/tax-reviews`, `/aqs-reviews`, `/remediation`, all children of Home and in the primary navigation. The starter pages (`contact-us`, `search`, `pages`, `subpage-1`, `subpage-2`), the basic form and nine sample media web files have been removed; `Search/Enabled` is `false` and the `Search` site marker is gone with it, because deleting the search page otherwise left a dead search box in the header of every page.

**Web templates** — `ot-layout` is the shell; every page template extends it and fills the `content` block. `ot-status-badge` and `ot-empty-state` are shared partials. `ot-review-list` is shared by the Tax and AQS pages, parameterised by `al_reviewtype` (Tax `120910200`, AQS `120910201`).

**Data** — bound with `{% fetchxml %}`, server-side paged at 25 rows with `returntotalrecordcount`. No unbounded retrieval, no client-side filtering of records the user should not have.

**Styling** — `outcome-testing.css`, derived from `app/src/styles/tokens.css`. No second colour system.

**Web roles** — the seven `AL Portal - *` roles exist and are bound to real permissions: `Tax Reviewer`, `AQS Reviewer`, `Adviser Remediation`, `T&C Supervisor`, `Outcome Testing Manager`, `Portal Administrator` and `Planner` (added for OD-019; `Regional Manager` was retired for OD-021 — see AD-069). Read-all for case, review and response data stays bound to the stock **Authenticated Users** role rather than the purpose-built roles, per OD-022: every authenticated portal user can read every case, and the ability to act is carried by the review assignment, not by role membership. Write scope binds to the purpose-built roles: `Review Instance - assigned to me` and its child `Response - on a review assigned to me` bind Tax and AQS Reviewer; the Contact-scoped `Remediation Action - assigned to me` binds Adviser and Planner.

**Page permissions** — one Restrict Read rule on Home (scoped to exclude direct child web files, so `outcome-testing.css` still serves on the sign-in page), plus per-page rules on the Tax, AQS, Review and Remediation branches, each a subset of Home's role list. `Access Denied`, `Page Not Found` and `Default Offline Page` are exempted **in the gate** from needing a Restrict Read rule of their own; they are not public in the deployed site. All three are children of Home, so they inherit Home's Restrict Read rule like every other page under it — which is why the deployment runbook warns of a possible redirect loop for a role-less contact who is sent to Access Denied, itself behind the same rule. **`Grant Change to Administrators` on Home (All content scope) overrides Restrict Read site-wide for Administrators** — do not write a test or a rule that assumes an Administrator can be denied a page.

**Release gate** — `powerpages/Check-PortalSecurity.ps1` runs alongside `Check-ComponentIds.ps1` before every upload and asserts: no table permission grants an anonymous role anything; no `PROVISIONAL` permission survives; every business page is covered by a Restrict Read rule on itself or an ancestor; a child page's rule is a subset of its nearest ancestor's; at most one Restrict Read rule per page; no page rule binds Anonymous Users; self-registration and external login are both off; every web role a permission or a page rule references still exists in `webrole.yml`; no Global-scope table permission carries write, create or delete; a Grant Change page rule may bind only the Administrators role; and every table a web template queries has a read permission. Both scripts read the repository, not the deployed site — a green gate does not by itself prove a row was deleted from DEV; see the deployment runbook's step on verifying deletions. `Deploy-Portal.ps1 -VerifyOnly` is the one that reads the **deployed** site: it queries `powerpagecomponent` type 18 and names every table permission that source declares and the environment does not have. That check is what caught DEV holding 2 of 13 on 2026-09-06, three weeks after the gates started passing.

**Assertion 11 — table read coverage.** Table permissions fail closed, so a table the site queries with no permission on it does not raise anything: the query returns nothing and the feature built on it renders as though there were no data. Nothing else in this gate noticed, and it went wrong twice. `al_outcome` was queried by the Home outcome tiles, the case list grade column, the case detail, the regrade panel and the sign-off final outcome, with no permission — every one of them drew empty, and the regrade panel (AD-111) was believed built and deployed for as long as it had never once rendered. `al_reviewroute` was the same shape: the route filter on the case and review lists silently offered no options, and because the AQS claim queue joins it `link-type="inner"`, that queue was empty for every user and no AQS checker could claim a case at all. An inner join is the dangerous case — an unreadable table in an outer join blanks a column, but in an inner join it collapses the whole result set. The assertion scans every `*.webtemplate.source.html` for `<entity>` and `<link-entity>` names, stripping `{% comment %}` blocks first so a table named in prose is not counted, and exempts many-to-many intersect tables, which Power Pages permissions through each end of the relationship rather than in their own right. A scan finding no templates, or no table references at all, fails as a broken scan rather than passing as a clean site.

### Not yet built

- **Assignment filtering.** Every list is unfiltered and says so on the page. Filtering needs `al_assignedcontactid` (AD-047), which exists on `al_ReviewInstance` and `al_RemediationAction` but is not yet used to filter a query.
- **Status and route filters.** Deferred until Phase 3.
- **Working-day ageing.** Implemented on the remediation report (OD-018); the reset-and-preserve behaviour across a rejected sign-off is not yet built.

## Rules

- Security is enforced in table permissions and page permissions, never by hiding UI (NFR-SEC-01).
- Global **read** is granted to Authenticated Users on `al_OutcomeCase`, `al_ReviewInstance`, `al_Response`, `al_RemediationAction` and `al_Outcome` (OD-022, AD-056, AD-069, AD-115) and on the reference tables `al_Section`, `al_QuestionVersion`, `al_Question`, `al_FailReason` and `al_ReviewRoute` (AD-047, AD-115). `al_ChecklistVersion` is not among them — its PROVISIONAL permission was deleted in this branch, since no `ot-*` web template queries it (see the security closure design, section 3). No portal role gets Global **write**: every write path is reachable only through a Contact-scoped permission (`Review Instance - assigned to me`, `Response - on a review assigned to me`, `Remediation Action - assigned to me`) bound to the purpose-built `AL Portal` roles.
- Never commit secrets, tokens, connection strings or environment-specific identifiers here — including in deployment profiles, Liquid, JavaScript or web files.
- The downloaded metadata **is** version controlled. Do not add it to `.gitignore`.

Design and permission model: [`docs/superpowers/specs/2026-08-29-power-pages-portal-design.md`](../docs/superpowers/specs/2026-08-29-power-pages-portal-design.md)
