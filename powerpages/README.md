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

Upload the **site folder**, not this directory:

```powershell
pac pages upload `
  --path ".\powerpages\outcome-testing---outcometesting" `
  --modelVersion Enhanced
```

> `--modelVersion` takes `Enhanced` or `Standard` on PAC CLI 2.11.2. The `--modelVersion 2` form in the build guide (sections 14, 19 and Appendix A) is not valid syntax for this version. Verify with `pac pages download --help` after any CLI upgrade.

After uploading: refresh the site configuration cache, sync design studio if open, then test in an InPrivate session against each representative role.

## Baseline state

Downloaded 2026-08-29 from a freshly provisioned site. Stock content only:

- Web roles: Administrators, Authenticated Users, Anonymous Users — the seven roles in the design do not exist yet.
- Table permissions: one, granting **Anonymous Users create access on Feedback** at Global scope. PP-01 requires anonymous access removed entirely, so this is deleted in Phase 1.

## Rules

- Security is enforced in table permissions and page permissions, never by hiding UI (NFR-SEC-01).
- No portal role gets Global access to case, review, response, outcome, remediation or audit data. Global read is only for `al_ChecklistVersion`, `al_Section`, `al_QuestionVersion` and `al_FailReason` (AD-047).
- Never commit secrets, tokens, connection strings or environment-specific identifiers here — including in deployment profiles, Liquid, JavaScript or web files.
- The downloaded metadata **is** version controlled. Do not add it to `.gitignore`.

Design and permission model: [`docs/superpowers/specs/2026-08-29-power-pages-portal-design.md`](../docs/superpowers/specs/2026-08-29-power-pages-portal-design.md)
