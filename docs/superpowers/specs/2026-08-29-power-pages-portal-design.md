# Outcome Testing Power Pages portal — design

Status: approved design, pre-implementation
Date: 2026-08-29
Supersedes: AD-002
Requirements: PP-01 to PP-17, BR-003 to BR-013, NFR-SEC-01/02, NFR-AUD-01, NFR-ACC-01, NFR-PERF-01

## 1. Purpose and boundary

The Outcome Testing solution runs on two front ends over one Dataverse dataset.

| Audience | Front end | Surface |
|---|---|---|
| Managers and administrators | Power Apps Code App (`app/`) | Oversight, allocation and reassignment, imports, question administration, security administration, audit investigation, MI and export |
| Tax checkers, AQS checkers, advisers, T&C Managers | Power Pages portal (`powerpages/`) | The surface where checks and remediation are performed |

The split is by persona, not by feature. The Code App screens under `app/src/features/` for cases, dashboard and reviews are re-framed as management oversight views; they are not retired, and they read the same tables the portal writes. Business logic lives in server-side commands shared by both front ends, never duplicated per front end.

This reverses AD-002, which recorded Power Pages as superseded for operational users. The signed scope (`OPP-41186`, item 13, "Power Pages access for operational teams") is the approving authority.

## 2. Site

| Property | Value |
|---|---|
| Friendly name | Outcome Testing - outcometesting |
| Website Id | `b4cfe195-fd15-42e8-94e5-f27bcceaf5fc` |
| Portal Id | `9e5b496c-daee-4bee-a3f0-80f381fb9d45` |
| URL | https://outcometesting.powerappsportals.com/ |
| Data model | Enhanced (version 2) |
| Type | Traditional metadata-driven, not SPA |
| Environment | Env_AQ_Dev (`org0b075da8.crm11.dynamics.com`) |

Traditional metadata-driven is a deliberate choice. The portal depends on standard Power Pages authentication, lists, forms, web roles and table permissions. `pac pages download-code-site` and `upload-code-site` are out of scope unless a separate architecture decision is approved.

**CLI syntax correction.** On PAC CLI 2.11.2, `--modelVersion` takes `Enhanced` or `Standard`. The `--modelVersion 2` form used throughout the build guide (sections 14, 19 and Appendix A) is not valid and fails. Verify with `pac pages download --help` after any CLI upgrade. There is no `pac pages create`; site provisioning is maker-portal only.

## 3. Identity and provisioning

Contacts are provisioned by **Entra group sync**. Portal identity and record ownership stay separate:

- `systemuser` remains the owner of every operational row, so all Code App management views keep working unchanged.
- `Contact` is the portal identity only.
- The two are joined on **work email**, already canonical under OD-003 / AD-010 and already carried by `al_User.al_workemail` and `al_UserRoleMapping.al_useremail`.

No second source of truth for identity is introduced. Web role membership derives from Entra group membership; the exact sync mechanism must be verified against current Microsoft Learn before the Phase 1 build, not assumed.

## 4. Permission model

### 4.1 Verified platform mechanics

Confirmed against Microsoft Learn `power-pages/security/table-permissions`, page updated 2026-07-07:

- Access types are **Global, Contact, Account, Self, Custom**. Custom access filters records by a FetchXML `filter` element, and only `filter` is evaluated. It is **preview** and requires enhanced authorization.
- **Parent is not a design-studio access type.** It is implemented as *child permissions* on a parent permission and runs **parent to child only**. A child permission cannot reach a record's parent.
- Every web role on a child permission must also exist on its parent permission.
- **Polymorphic lookups are not supported** in parent-child permission chains.

Custom access is excluded from the production design while it remains preview. A regulated build does not rest its access control on a preview feature.

### 4.2 Schema deltas

Three Contact lookups, populated server-side at assignment time from the work-email join:

| Table | New column | Meaning |
|---|---|---|
| `al_ReviewInstance` | `al_assignedcontactid` | The checker who owns this Tax or AQS instance |
| `al_RemediationAction` | `al_assignedcontactid` | The adviser responsible for the action |
| `al_OutcomeCase` | case-access mechanic, see 4.4 | Case readability for PP-05 |

### 4.3 Permission matrix

| Table | Web roles | Access | Basis | Privileges |
|---|---|---|---|---|
| `al_ReviewInstance` | Tax Reviewer, AQS Reviewer | Contact | `al_assignedcontactid` | Read, Write |
| `al_Response` | inherited | Child of `al_ReviewInstance` | `al_reviewinstanceid` | Create, Read, Write |
| `al_Outcome` | inherited | Child of `al_ReviewInstance` | `al_reviewinstanceid` | Read |
| `al_RemediationAction` | Adviser | Contact | `al_assignedcontactid` | Read, Write |
| `al_RemediationAction` | T&C Manager | scoped oversight | see 4.4 | Read, Write |
| `al_Signoff` | T&C Manager | Contact | sign-off author | Create, Read |
| `al_ChecklistVersion`, `al_Section`, `al_QuestionVersion`, `al_FailReason` | all portal roles | Global | reference configuration | Read |
| `al_AuditEvent`, `al_ExportBatch`, `al_ExportRecord`, `al_ImportBatch`, `al_ImportException`, `al_User`, `al_UserRoleMapping`, `al_Role`, `al_PagePermission` | none | none | no portal permission exists | none |

Global read is granted only to the four reference and configuration tables. Every operational table is relationship-scoped. No portal role receives Global access to case, review, response, outcome, remediation or audit data, and the Anonymous web role receives no table permission at all.

This enforces PP-08 structurally. A Tax checker's Contact access reaches only their Tax review instance. The AQS instance on the same case is a separate row carrying a different assigned contact, so cross-discipline access is impossible by construction rather than by convention.

### 4.4 Open mechanic: case readability

`al_OutcomeCase` must be readable by the currently assigned checker, the adviser working remediation, and the overseeing T&C Manager. A lookup holds one contact, and child permissions cannot traverse upward from review to case.

Candidate resolutions, in preference order:

1. **N:N Contact relationship** on `al_OutcomeCase`. Cleanest if Contact access accepts N:N. The documentation does not state this either way.
2. **Per-persona lookups** — `al_checkercontactid`, `al_advisercontactid`, `al_tccontactid`. Certain to work, but three columns to keep in step.
3. **Custom access FetchXML.** Rejected for production while preview.

Resolution is an empirical test in DEV: create the N:N relationship, publish, and check whether it appears in the Contact-access relationship picker. Tracked as OD-022. Phase 2 cannot complete until it is settled.

## 5. Questionnaire rendering

The portal reads the `al_ChecklistVersion` stamped on the review instance, walks `al_Section` then `al_QuestionVersion` in configured order, and writes `al_Response` into the typed column matching each question version's response type (AD-023).

Rendering uses a **single web template**, not one basic form per questionnaire version. This is the only approach that satisfies PP-09's historical-integrity rule without regenerating form metadata whenever a version is published, and it matches build guide section 17.4.

Question administration stays in the Code App. The portal never writes `al_Question`, `al_QuestionVersion`, `al_Section`, `al_Checklist` or `al_ChecklistVersion`. Published versions are immutable; amendments create a new version through the existing `al_RetireAndSucceedQuestion` command.

## 6. Submission, locking and concurrency

The 11 custom APIs in `src/customapis/` cover remediation completion, sign-off, regrade, export and administration. **There is no submit-review or save-draft command**, and PP-07, PP-08 and PP-11 require one.

Portal pages cannot invoke a Dataverse custom API directly. The path is:

```
portal page -> secured Power Automate flow (run-only) -> Dataverse custom API -> Dataverse
```

The flow re-checks assignment, status and editability server-side before writing. This makes submission idempotent (build guide 8.3) and makes the PP-11 lock unbypassable through the Portals Web API or a hand-edited URL.

Locking rules:

- A submitted review renders read-only and rejects writes at the command, not only in the UI.
- Reopen, correction and regrade remain privileged T&C Manager commands under AD-031, each requiring a mandatory reason on an immutable `al_AuditEvent`.
- The initial outcome is preserved as a separate record and never edited in place (BR-007).

## 7. Notifications

`al_Notification` is in the approved model but not yet built. PP-15's nine events require it as an **outbox**: a row written in the same transaction as the state change, drained by flows. This is what makes retries safe and duplicate sends impossible (build guide 8.3).

Channel is email only, per OD-009 / AD-035. Recipients are environment-specific; DEV and TEST must never resolve production addresses.

## 8. Repository layout

**The two front ends stay in separate directory trees. Neither ever contains the other's files.**

```
AscotLloydOutcomeTesting/
├── app/                  Power Apps Code App        (managers)      — no portal files
├── powerpages/           Power Pages site metadata  (checkers)      — no code app files
│   └── outcome-testing/    pac pages download target
├── src/                  Dataverse solution root, packaged in full  — shared, front-end agnostic
├── brand/                Authoritative visual resources             — shared, read-only to both
├── plugins/              Plugin and custom API source               — shared
├── data/                 Seed and fixture data                      — shared
├── knowledge/            Requirements, decisions, context
└── docs/                 Specs, architecture, security, releases
```

Rules that keep them separate:

- Portal metadata must not live under `src/`. That directory is the unpacked Dataverse solution root and is **packaged in full**, so anything placed there ships inside the managed solution. `app/` and `brand/` already sit outside it for exactly this reason.
- Portal metadata must not live under `app/`, and Code App source must not live under `powerpages/`. They have separate build, deploy and release paths — `pac pages upload` for the portal, `pa app push` for the Code App — and mixing them breaks both.
- This supersedes the layout in the Power Pages requirements pack section 12, which placed `src/powerpages/` and `src/resources/` inside the solution root.

What the two front ends **do** share, deliberately, is everything below the UI: the Dataverse schema in `src/`, the server-side commands in `plugins/`, and the brand tokens in `brand/`. Sharing those is what prevents divergent business logic; sharing UI directories would only cause deployment collisions.

Portal CSS consumes the existing `brand/` tokens — Outfit headings, Inter body, per AD-009 and OD-015. No second colour system is introduced in site CSS.

## 9. Build sequence

1. **Foundation.** Download the site baseline and commit it untouched. Create web roles, page hierarchy, navigation, shared layout and error components.
2. **Security.** Apply the section 4.2 schema deltas, resolve OD-022, create table permissions and page permissions, seed test contacts, run positive and negative access tests. No feature page is built before this gate passes.
3. **My Work and case detail.** Role-aware landing page, server-filtered worklists, case summary, route and stage indicator.
4. **Tax review.** Questionnaire rendering, draft save, mandatory validation, submit command, lock.
5. **AQS review.** Same pattern, with AQS sections and the four-value outcome scale.
6. **Remediation.** Adviser response, T&C attestation, SLA ageing, final outcome recorded without disturbing the initial one.
7. **Notifications and reporting.** Outbox events, flows with environment-safe recipients, drill-down to filtered lists.
8. **Hardening and ALM.** Accessibility, security, URL and Web API abuse tests, performance at realistic volume, managed promotion to TEST then PROD.

## 10. Testing

Every web role gets positive and negative tests. The negative set must prove a user cannot: view an unassigned case by editing the URL; read a restricted table through the Portals Web API; update a submitted review; alter another reviewer's response; see audit, import or export rows; or elevate access by editing client-side role values. Anonymous access to every business page must fail.

Accessibility is WCAG 2.2 AA (NFR-ACC-01): keyboard operation, visible focus, semantic landmarks, associated labels, validation summaries, contrast, and no status conveyed by colour alone.

## 11. Deliberate exclusions

- **Evidence upload.** Advisers reference existing Intelligent Office documents; nothing is uploaded or stored. PP-14 collapses to a reference field. This preserves the standing data principle that documents remain in Intelligent Office and are referenced, not stored.
- **Power Pages SPA / code site.** Excluded under section 2.
- **Custom access type.** Excluded while preview.
- **Automated SFTP transfer and Power BI.** Out of MVP under AD-034.
- **Portal write access to configuration, audit, import and export tables.** No permission is created.

## 12. Open decisions

| ID | Question | Blocks |
|---|---|---|
| OD-018 | Business calendar source for the 10-working-day remediation threshold | PP-13 |
| OD-019 | Are Adviser and Planner separate portal roles? | Web role matrix |
| OD-020 | Are T&C Manager and Supervisor separate roles? | Web role matrix, sign-off routing |
| OD-021 | Is Regional Manager portal access or notification-only? | Web role matrix, PP-15 |
| OD-022 | Case-access mechanic: N:N Contact relationship or per-persona lookups | Phase 2 gate |
| OD-023 | Production support owner | Go-live |

OD-011 (licensing for all personas) is confirmed in place for the portal personas as of 2026-08-29.
