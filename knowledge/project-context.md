# Ascot Lloyd Outcome Testing domain context

## Business objective
Build a Dataverse-centred outcome testing and file-checking platform that replaces fragmented Word, Excel and email handling with controlled case intake, allocation, Tax and AQS review, remediation, audit, MI and export processes.

## Confirmed core workflow
1. Import or create a case from Intelligent Office/new-business data.
2. Validate required data. Cases with invalid or missing intake data are returned with a reason and, where work must restart, closed as Returned/Cancelled and resubmitted as a new case linked through Previous Case/Replacement Case.
3. Allocate to a team queue and, where needed, an individual.
4. Route as Tax only, AQS only, or Tax then AQS. Tax and AQS are sequential when both are required.
5. Tax records its own Tax grade and evidence, then routes to AQS or returns/closes as appropriate. A completed Tax check with a non-pass result enters remediation; only wrong-route, no-check-required or invalid cases are returned or cancelled.
6. AQS records Pass, Pass with issues, Insufficient evidence, or Potential harm.
7. Non-pass outcomes create remediation. Preserve initial and final outcomes.
8. Advisers complete remediation; T&C managers validate Insufficient evidence and Potential harm. Rejected remediation returns with notes.
9. Final sign-off locks submitted sections and closes the case.
10. Produce MI datasets and Trail Light-compatible exports. Automated SFTP is a later enhancement unless explicitly approved.

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

## Canonical lifecycle
`Imported -> Validation Failed | Ready for Allocation -> Queued -> Assigned -> Review In Progress -> Submitted -> Awaiting Remediation | Closed -> Remediation In Progress -> Awaiting Sign-off -> Awaiting Recheck/Regrade -> Closed`

## Repository layout
- `src/` is the unpacked Dataverse solution root and is packaged in full. Flows unpack to `src/Workflows/`.
- `app/` holds the Power Apps Code App. It must stay outside `src/`.
- `knowledge/` holds domain context, the requirements index and the decision log.
- `skills/` holds the phase skills; `.github/` holds Copilot instructions and prompts.

## Quality constraints
Security, auditability, accessibility, server-side query performance, idempotent imports/exports, error handling, traceability and managed-solution ALM are mandatory. Never claim production readiness without passing tests and deployment gates.
