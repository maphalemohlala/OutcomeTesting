# Requirements index

Authoritative wording lives in the build package documents `01-Solution-Vision` to `20-Open-Decisions`. This file is the stable ID register used by `skills/requirements-traceability/SKILL.md` and by the "Requirement IDs affected" output of every skill. If a requirement is not listed here, it is not approved.

## ID schemes
| Prefix | Meaning | Source document |
|---|---|---|
| BR-nnn | Business requirement | 02-Business-Requirements |
| FR-nnn | Functional requirement | 03-Functional-Requirements |
| NFR-xxx-nn | Non-functional requirement | 04-Non-Functional-Requirements |
| PP-nn | Power Pages portal requirement | Power Pages requirements pack (`OPP-41186` signed scope + build guide) |
| OD-nnn | Open decision | 20-Open-Decisions, tracked in `decision-log.md` |
| E-n | Delivery epic | 14-Development-Backlog |

## Business requirements
| ID | Summary | Status |
|---|---|---|
| BR-001 | Import case data from Intelligent Office Excel extracts | Confirmed |
| BR-002 | Validate required fields; return invalid/missing-information cases with a reason | Confirmed |
| BR-003 | Allocate to team queues and named individuals; support reassignment | Confirmed |
| BR-004 | Support Tax-only, AQS-only and Tax-then-AQS routes; Tax precedes AQS | Confirmed |
| BR-005 | Capture Pass, Pass with issues, Insufficient evidence, Potential harm | Confirmed |
| BR-006 | Every non-pass outcome requires remediation; guidance-only stays Pass with observations | Confirmed |
| BR-007 | Retain both initial and final outcomes | Confirmed |
| BR-008 | Adviser completes remediation; T&C manager verifies Insufficient evidence and Potential harm | Confirmed |
| BR-009 | Notify para-planners without granting operational access by default | Confirmed |
| BR-010 | Capture structured MI including remediation ageing and accountability | Confirmed |
| BR-011 | Produce Trail Light-compatible Excel output; automated transfer conditional | Export confirmed, transfer conditional |
| BR-012 | Role-based security, edit protection and audit history | Confirmed |
| BR-013 | Administrators maintain questions without corrupting historic data | Confirmed |

## Functional requirements
| Range | Area | IDs |
|---|---|---|
| Intake and allocation | Upload, validation, exceptions, worklist, assignment, reassignment | FR-001 to FR-006 |
| Reviews and outcomes | Versioned review instances, section ownership, response types, fail reasons, Tax-before-AQS, Tax routing, Tax grade, submission locks | FR-010 to FR-017 |
| Remediation and closure | Action generation, notification, attestation, T&C sign-off, final outcome, closure rules | FR-020 to FR-025 |
| Administration and export | Question versioning, retire-and-succeed, Trail Light batches, audit history | FR-030 to FR-033 |

## Non-functional requirements
NFR-SEC-01, NFR-SEC-02, NFR-AUD-01, NFR-PERF-01, NFR-PERF-02, NFR-REL-01, NFR-REL-02, NFR-ALM-01, NFR-OPS-01, NFR-ACC-01 (WCAG 2.2 AA), NFR-DATA-01, NFR-OBS-01.

## Power Pages portal requirements
Approved 2026-08-29 by the signed scope `OPP-41186` item 13. The portal serves Tax checkers, AQS checkers, advisers and T&C Managers; the Code App serves managers and administrators (AD-044). Design: `docs/superpowers/specs/2026-08-29-power-pages-portal-design.md`.

| ID | Summary | Traces to | Status |
|---|---|---|---|
| PP-01 | Entra ID authentication; anonymous access disabled; Contact provisioning; non-disclosing access-denied page | BR-012, NFR-SEC-01, NFR-SEC-02 | Confirmed |
| PP-02 | Role-aware navigation enforced by page and table permissions, never UI hiding | BR-012, NFR-SEC-01 | Confirmed |
| PP-03 | My Work dashboard: assigned, pending Tax, pending AQS, drafts, returned, open and overdue remediation | BR-003, FR-004 | Confirmed |
| PP-04 | Case search and worklist with server-side filtering and pagination; no unbounded client retrieval | FR-004 to FR-006, NFR-PERF-01 | Confirmed |
| PP-05 | Case detail in business sections; no administrative, integration, GUID or audit-payload fields exposed | FR-010 range | Confirmed |
| PP-06 | Route display and transitions for Tax-only, AQS-only and Tax-then-AQS; return with reason | BR-004, FR-014, FR-015, FR-002, FR-003 | Confirmed |
| PP-07 | Tax review: versioned questionnaire, draft save, mandatory enforcement, atomic submit, Tax grade held separately | FR-016, FR-010 to FR-013 | Confirmed |
| PP-08 | AQS review: versioned questionnaire, conditional reasons, no cross-discipline overwrite | BR-005, FR-010 to FR-013 | Confirmed |
| PP-09 | Questions administered outside the portal; published versions immutable; portal reads only the assigned version | BR-013, FR-030, FR-031 | Confirmed |
| PP-10 | Four AQS outcomes; initial and final outcomes stored separately; Tax Pass/Fail separate from AQS scale | BR-005, BR-007 | Confirmed |
| PP-11 | Submission lock across forms, Web API and manipulated URLs; reopen and correction require a reason and full before/after audit | BR-012, FR-017, NFR-AUD-01, AD-031 | Confirmed |
| PP-12 | Remediation workspace: adviser response and T&C Manager attestation, both in the portal | BR-006, BR-008, FR-020 to FR-023, AD-045 | Adviser half built 2026-09-02 — response, Intelligent Office reference and completion, inline on `/remediation`. T&C attestation not built |
| PP-13 | Remediation SLA: clock start and stop, outstanding age, 10-working-day threshold on a business calendar | BR-010 | Confirmed. OD-018 resolved 2026-08-30; working-day ageing implemented in the Code App (`app/src/lib/workingDays.ts`) and on the portal remediation list. The reset-and-preserve behaviour on a rejected sign-off is not built — it needs each period stored on the action |
| PP-14 | Evidence referenced from Intelligent Office; no portal upload or storage | AD-046 | Built 2026-09-02 — `al_RemediationAction.al_evidencereference`, captured on the adviser response form |
| PP-15 | Nine notification events emitted to Power Automate via an outbox; email only | BR-009, FR-021, AD-035 | Confirmed |
| PP-16 | User-friendly errors; technical detail and correlation ID logged separately, never surfaced | NFR-OBS-01 | Confirmed |
| PP-17 | Reporting drill-down from outcome cards to filtered lists; full MI and export remain back-office | BR-010, FR-032 | Confirmed |

## Traceability matrix columns
Every implemented story records: requirement ID, source document, actor, workflow step, priority, Given/When/Then acceptance criteria, data entities, screens/routes, commands, flows, security roles affected, tests, and release version.

## Epic to phase mapping
| Epic | Scope | Phase (see `phase-skill-map.md`) |
|---|---|---|
| E1 | Foundation and data model | 0, 1 |
| E2 | Intake and work management | 3, 4 |
| E3 | Reviews and dynamic checklist | 4 |
| E4 | Outcomes and remediation | 5 |
| E5 | MI, export, administration, hardening | 6, 8 |

## Blocking dependencies
E1 is unblocked: allocation is settled as a shared queue with manual manager assignment (OD-002 resolved, AD-040). E3 is unblocked: V8 content, response types, mandatory rule, section ownership and the CRP rule are settled in `checklist-v8.md` and AD-019 to AD-022. E5 export mapping is settled (OD-004 resolved, AD-039); the Export Batch and Export Record tables remain to build. The architecture gate OD-011 is deferred until the app is ready to promote.
