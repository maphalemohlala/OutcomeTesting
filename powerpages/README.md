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

Record ids here are hand-minted and banded by component type (AD-059). Every
component — web template, page template, web page, table permission, web link —
is a row in the single `powerpagecomponent` table, so two components sharing an
id are one row and the upload silently replaces one with the other. Nothing
reports the loss; the component simply stops existing and every record pointing
at it resolves to the wrong type, which the portal answers with a generic error
page. This has already taken out `/cases` once and the primary navigation once.

Upload the **site folder**, not this directory:

```powershell
pac pages upload `
  --path ".\powerpages\outcome-testing---outcometesting" `
  --modelVersion Enhanced
```

> `--modelVersion` takes `Enhanced` or `Standard` on PAC CLI 2.11.2. The `--modelVersion 2` form in the build guide (sections 14, 19 and Appendix A) is not valid syntax for this version. Verify with `pac pages download --help` after any CLI upgrade.

After uploading: refresh the site configuration cache, sync design studio if open, then test in an InPrivate session against each representative role.

## Current state

Shell and data wiring built 2026-08-29 and verified round-tripped from DEV.

**Pages** — `/my-work`, `/cases`, `/case-details?id=`, `/tax-reviews`, `/aqs-reviews`, `/remediation`, all children of Home and in the primary navigation.

**Web templates** — `ot-layout` is the shell; every page template extends it and fills the `content` block. `ot-status-badge` and `ot-empty-state` are shared partials. `ot-review-list` is shared by the Tax and AQS pages, parameterised by `al_reviewtype` (Tax `120910200`, AQS `120910201`).

**Data** — bound with `{% fetchxml %}`, server-side paged at 25 rows with `returntotalrecordcount`. No unbounded retrieval, no client-side filtering of records the user should not have.

**Styling** — `outcome-testing.css`, derived from `app/src/styles/tokens.css`. No second colour system.

### Not yet built

- **Web roles.** Only the three stock roles exist (Administrators, Authenticated Users, Anonymous Users). The seven in the design are Phase 2.
- **Assignment filtering.** Every list is unfiltered and says so on the page. Filtering needs `al_assignedcontactid` (AD-047), which does not exist yet.
- **Status and route filters.** Deferred until Phase 3.
- **Working-day ageing.** Remediation shows calendar days, labelled as such, pending OD-018.

### ⚠ Provisional table permissions — DEV only

Nine permissions named `PROVISIONAL DEV ONLY - *` grant **Global read** to Authenticated Users so pages render during development. In Power Pages a list with no permission returns nothing, so data wiring cannot be built without them.

**They must be deleted before TEST or PROD**, and replaced by the Contact-scoped matrix in the design (AD-047). Treat their presence in a release artefact as a release blocker.

The stock `Feedback` permission granting **Anonymous Users create access** also remains and is removed in Phase 1 under PP-01.

## Rules

- Security is enforced in table permissions and page permissions, never by hiding UI (NFR-SEC-01).
- No portal role gets Global access to case, review, response, outcome, remediation or audit data. Global read is only for `al_ChecklistVersion`, `al_Section`, `al_QuestionVersion` and `al_FailReason` (AD-047).
- Never commit secrets, tokens, connection strings or environment-specific identifiers here — including in deployment profiles, Liquid, JavaScript or web files.
- The downloaded metadata **is** version controlled. Do not add it to `.gitignore`.

Design and permission model: [`docs/superpowers/specs/2026-08-29-power-pages-portal-design.md`](../docs/superpowers/specs/2026-08-29-power-pages-portal-design.md)
