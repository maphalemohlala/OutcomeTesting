# Decision log

Single register for open decisions and resolved architecture decisions. Agents must read this before implementing anything that depends on an open item, and must not invent a resolution. Record a resolution only when a named owner has confirmed it, with the date and the evidence source.

## Open decisions
| ID | Decision required | Owner | Build impact | Status | Resolution and date |
|---|---|---|---|---|---|
| OD-001 | Confirm exact V8 sections, questions, response types, mandatory flags and role ownership | Advice Quality Manager | Blocks checklist seed data and UAT | Open | |
| OD-002 | Confirm Tax queue versus skills-based named allocation | Tax Team Manager | Allocation model | Open | |
| OD-003 | Confirm canonical cross-system identifier: employee ID, work email, or both | Ascot Lloyd IT/business | Import and export keys | Resolved | 2026-08-26: **work email** is the canonical cross-system identifier. See AD-010. |
| OD-004 | Provide final Trail Light template, formats and validation rules | Trail Light/Ascot Lloyd | Export build | Open | |
| OD-005 | Confirm whether automated SFTP is in MVP and approve connection, DLP and authentication | IT/security | Integration scope | Open | |
| OD-006 | Confirm CRP visibility rule | Advice Quality | Conditional rendering | Open | |
| OD-007 | Define override, reopen and regrade roles and mandatory reason | Advice Quality/Compliance | Security and command logic | Open | |
| OD-008 | Confirm Tax wrong-route/no-check-required status and whether replacement cases link to the original | Product owner | Status model | Open | |
| OD-009 | Confirm notification channels per event and whether Teams is required | Product owner | Flow templates | Open | |
| OD-010 | Confirm retention, audit retention and data subject policies | Compliance/DPO | Data lifecycle | Open | |
| OD-011 | Confirm Code Apps production readiness, tenant availability and licensing for all personas | Platform owner | Architecture gate | Partially resolved | 2026-08-26: code app operations enabled on Env_AQ_Dev by an environment admin; `pa app push` now succeeds (app `5d9fc475-ee75-4386-917e-fc182307b0c2`). Still open: enabling TEST and PROD, and confirming licensing for every persona. |
| OD-012 | Confirm Power BI audience and RLS segmentation | MI owner | Reporting security | Open | |
| OD-013 | Resolve the Light Teal conflict in the brand palette: the sheet states hex `CCDFF3` but RGB `204/253/243`, which is `#CCFDF3`. The printed swatch renders mint, matching the RGB. | Brand owner | Blocks the Light Teal design token | Open | Working assumption is `#CCFDF3`; the token must not be treated as verified until confirmed. |
| OD-014 | Approve or replace the two non-brand outcome colours in AD-008 (`#C26A00` amber, `#C22B21` red) and confirm whether Tertiary Purple may carry the Insufficient evidence meaning. | Brand owner | Outcome and status tokens across every review screen | Open | Implemented on instruction pending brand sign-off. |
| OD-015 | Supply licensed Roundo font files, or approve Outfit as the permanent heading typeface. | Brand owner | Heading typography throughout the app | Open | Outfit is in use under AD-009. |

## Resolved architecture decisions
| ID | Decision | Rationale | Date |
|---|---|---|---|
| AD-001 | Dataverse is the system of record | Signed target solution supersedes earlier fixed-column Excel/Word designs | 2026-08-26 |
| AD-002 | The frontend is a single Power Apps Code App; Power Pages is superseded for operational users | Explicit direction recorded in the build package assumptions | 2026-08-26 |
| AD-003 | Privileged lifecycle transitions run as server-side commands, not unrestricted client updates | Security must not depend on UI enforcement | 2026-08-26 |
| AD-004 | Checklist content is data-driven and versioned; responses reference an immutable question version | Protects historic submissions and MI when questions change | 2026-08-26 |
| AD-005 | Publisher prefix is `al`; the unpacked solution root is `src` and the Code App lives in `app/` | Matches `AscotLloydOutcomeTesting.cdsproj` `SolutionRootPath` and keeps app build output out of the solution | 2026-08-26 |
| AD-006 | A completed check with a non-pass outcome enters remediation; only invalid or wrong-route cases are returned or cancelled | Resolves the conflict between the early stop-and-restart description and later process confirmation | 2026-08-26 |
| AD-007 | `app/power.config.json` may contain `environmentId` and `appId` as an approved exception to the no-hardcoded-identifiers rule | The Power Apps CLI generates and owns this file, and both values are required for `pa app run` and `pa app push`. See the exception note below. | 2026-08-26 |
| AD-008 | Outcome status colours for BR-005 are `Pass #00A800` (brand Accessible Green), `Pass with issues #B87A3D`, `Insufficient evidence #8758E4` (brand Tertiary Purple), `Potential harm #B65A52` | The brand palette contains no warning or danger colour, but BR-005 requires four visually distinct outcomes. The amber and red are deliberately desaturated to sit alongside the brand teals. Measured contrast on white: 3.57, 4.59, 4.62 and 3.18, all above the 3:1 required for non-text indicators. Each outcome also carries a distinct shape and its text label, so colour is never the sole signal. | 2026-08-26 |
| AD-009 | Outfit is used as the heading typeface in place of Roundo, and Inter is self-hosted through `@fontsource-variable` | No font files exist in `brand/` and Roundo is licence-restricted. Self-hosting avoids an external CDN, which a code app host may block by CSP. Both are declared only in `--font-heading` and `--font-body`. | 2026-08-26 |
| AD-010 | Work email is the canonical cross-system identifier between Intelligent Office, Outcome Testing and Trail Light | Resolves OD-003. Alternate keys on Outcome Case and Export Record are built on it, which is what makes imports and exports idempotent under NFR-REL-01. Employee ID may still be stored, but must not be used as a matching key. | 2026-08-26 |

## Approved exceptions to the no-hardcoded-identifiers rule
`AGENTS.md` rule 7 forbids hardcoding tenant or environment identifiers. AD-007 grants one narrow exception.

| Item | File | Why it is allowed | Boundaries |
|---|---|---|---|
| `environmentId` | `app/power.config.json` | Written by `pa app init`; the CLI has no environment-variable indirection for it | DEV identifier only. TEST and PROD values must be supplied at deployment time, never committed |
| `appId` | `app/power.config.json` | Written by `pa app push`; identifies the published app for subsequent pushes | Same as above |

Boundaries that still apply:
- Neither value is a secret, but both are environment-specific, so `power.config.json` must not be copied between environments.
- No other file may hardcode an environment ID, tenant ID, org URL, connection ID or group ID.
- Runtime configuration such as URLs, queue identifiers and export locations continues to use Dataverse environment variables, not this file.
- The DEV values currently recorded are environment `d50d27e8-cb3b-e718-b6e2-30aa92d944aa` (Env_AQ_Dev), solution `c7f447ab-35a1-f111-b8dd-e4fade069307` (OutcomeTesting) and app `5d9fc475-ee75-4386-917e-fc182307b0c2`.
- Revisit this exception if the CLI later supports token substitution for `power.config.json`.

## Recording rules
- Never move an OD to resolved without a named owner, a date and a source reference.
- When an OD is resolved, update `project-context.md` and the affected skill in the same change.
- If implementation is blocked by an open OD, state the OD ID in the work output rather than assuming a rule.
