# Ascot Lloyd Outcome Testing domain context

## Business objective
Build a Dataverse-centred outcome testing and file-checking platform that replaces fragmented Word, Excel and email handling with controlled case intake, allocation, Tax and AQS review, remediation, audit, MI and export processes.

## Confirmed core workflow
1. Import or create a case from Intelligent Office/new-business data.
2. Validate required data. Cases with invalid or missing intake data are returned with a reason and, where work must restart, closed as Returned/Cancelled and resubmitted as a new case linked through Previous Case/Replacement Case.
3. Allocate to a shared team queue; a manager then manually assigns the case to a named team member. Tax and AQS use the same model, with no skills routing or auto-allocation (OD-002, AD-040).
4. Route as Tax only, AQS only, or Tax then AQS. Tax and AQS are sequential when both are required.
5. Tax records its own Tax grade and evidence, then routes to AQS or returns/closes as appropriate. A completed Tax check with a non-pass result enters remediation; only wrong-route, no-check-required or invalid cases are returned or cancelled.
6. AQS records Pass, Pass with issues, Insufficient evidence, or Potential harm.
7. Non-pass outcomes create remediation. Preserve initial and final outcomes.
8. Advisers complete remediation; T&C managers validate Insufficient evidence and Potential harm. Rejected remediation returns with notes.
9. Final sign-off locks submitted sections and closes the case. Reopening, overriding or regrading a submitted or closed outcome is a privileged correction owned by the T&C Manager (Outcome Testing Manager/Administrator escalation) that requires a mandatory reason on an immutable Audit Event and preserves the initial outcome (OD-007, AD-031).
10. Produce MI datasets and Trail Light-compatible exports. Export is a manual, on-demand production of a Trail Light-compatible file; automated SFTP and a Power BI build are out of MVP scope (AD-034). Notifications are delivered by email only (AD-035).

## Actors and access
Administrators, Outcome Testing Managers, Checkers/AQS Reviewers, Tax Reviewers, Advisers/Remediation Users, T&C Managers/Supervisors, Regional Leads and Report Users. Users are managed through Entra groups and Power Platform/Dataverse teams. Multi-role membership is allowed.

## Data principles
- Dataverse is the system of record.
- Use relational tables, stable alternate keys and explicit status/state models.
- Preserve historical question versions and responses.
- Use team ownership where queues and operational resilience require it.
- Enable Dataverse auditing on sensitive and lifecycle tables.
- Do not store secrets or environment-specific identifiers in source.
- Treat documents as external artefacts referenced from Dataverse unless the approved design says otherwise.

## Core entities
The approved data model defines the following tables. Names here are authoritative for schema work; do not introduce synonyms.

| Group | Tables |
|---|---|
| Case | Outcome Case, Case Assignment, Review Route |
| Intake | Import Batch, Import Exception |
| Review | Review Instance, Response |
| Checklist configuration | Checklist, Checklist Version, Section, Question, Question Version, Fail Reason |
| Outcome and remediation | Outcome, Remediation Action, Sign-off, Recheck |
| Operations | Notification, Audit Event, User Role Mapping |
| Export | Export Batch, Export Record |

A single Review Instance carries `reviewType` (Tax or AQS) rather than separate Tax and AQS tables. Documents remain in Intelligent Office and are referenced, not stored.

Checklist configuration is a strict single-parent hierarchy, Checklist -> Checklist Version -> Section -> Question -> Question Version, with delete restricted at every level (AD-016, AD-017). Fail Reason sits outside that chain and attaches to Response many-to-many, because FR-013 allows several reasons on one answer (AD-025).

A Review Instance carries the checklist version issued to it and stores its answers as Responses, one per question version. Each Response holds its answer in the typed column matching the question version's response type (AD-023). Review Instance and Response are user-owned so they can be allocated; configuration tables are organisation-owned (AD-024). The Outcome Case link on Review Instance is not built yet, because Outcome Case does not exist.

Checker Checklist V8 is seeded from `data/v8-seed`: 1 checklist, 1 version, 11 sections, 42 questions, 42 question versions and 20 fail reasons (AD-027).

The Outcome and Sign-off tables now exist in DEV (AD-032): `al_Outcome` preserves the initial and final outcome (BR-007) and carries the AD-031 regrade reason, and `al_Signoff` records the T&C Manager's Approved/Rejected validation (FR-023, BR-008). The separate Recheck table stays in the model but is deferred; the final outcome is carried on `al_Outcome`.

The Intake tables now exist in DEV (AD-033): `al_ImportBatch` tracks one uploaded Intelligent Office extract with its row/imported/exception counts and a Received/Validating/Completed status, and `al_ImportException` holds each row that failed validation (row number, case reference, reason, Open/Resolved/Ignored status), related to its batch with Restrict delete (FR-001 to FR-003, BR-001, BR-002). The `/imports` screen reads both read-only; the upload and resolve write actions remain a later command/flow path.

## Canonical lifecycle
`Imported -> Validation Failed | Ready for Allocation -> Queued -> Assigned -> Review In Progress -> Submitted -> Awaiting Remediation | Closed -> Remediation In Progress -> Awaiting Sign-off -> Awaiting Recheck/Regrade -> Closed`

`No Check Required` is a terminal bypass state for cases that must not be graded (AD-036). Tax wrong-route is a manager-controlled reassignment between Tax and AQS with a mandatory reason, retaining prior assignment history, not a status.

This sequence is enforced, not just documented (AD-057). `CaseLifecycle` in the plug-in assembly gates every `al_casestatus` write in `al_UpdateCaseDetails`, and `CASE_STATUS_TRANSITIONS` in the Code App offers the manager only the statuses the command would accept. `Closed` and `No Check Required` are terminal: reopening or regrading a closed outcome is the privileged T&C Manager correction (OD-007, AD-031), not a details edit.

## Front ends
Two front ends run over one Dataverse dataset, split by persona rather than by feature (AD-044, supersedes AD-002).

| Audience | Front end | Surface |
|---|---|---|
| Managers and administrators | Power Apps Code App (`app/`) | Oversight, allocation and reassignment, imports, question administration, security administration, audit investigation, MI and export |
| Tax checkers, AQS checkers, advisers, T&C Managers | Power Pages portal (`powerpages/`) | Where checks and remediation are performed |

Code App screens for cases, dashboard and reviews are management oversight views, not the checker's working surface. Business logic lives in shared server-side commands (AD-003) and is never duplicated per front end. Portal identity is the Entra-synced `Contact`, joined to the application identity on work email; `systemuser` remains the record owner (AD-047). The whole remediation loop — adviser response and T&C attestation — runs in the portal (AD-045).

## Repository layout
- `src/` is the unpacked Dataverse solution root and is packaged in full. Flows unpack to `src/Workflows/`.
- `app/` holds the Power Apps Code App. It must stay outside `src/`.
- `powerpages/` holds the Power Pages site metadata. It must stay outside `src/` and outside `app/`; the two front ends never share a directory (AD-048).
- `brand/` holds the authoritative Ascot Lloyd visual resources. It must stay outside `src/` so brand assets are not packaged into the solution.
- `knowledge/` holds domain context, the requirements index and the decision log.
- `skills/` holds the phase skills; `.github/` holds Copilot instructions and prompts.

## Quality constraints
Security, auditability, accessibility, server-side query performance, idempotent imports/exports, error handling, traceability and managed-solution ALM are mandatory. Never claim production readiness without passing tests and deployment gates.
