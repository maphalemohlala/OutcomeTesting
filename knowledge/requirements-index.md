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
| BR-002 | Validate required fields; return invalid/missing-information cases with a reason | Confirmed. **Server-side 2026-09-03** as `al_ImportCases` (AD-077); closing an exception is `al_ResolveImportException` (AD-078). Validation no longer lives only in the browser |
| BR-003 | Allocate to team queues and named individuals; support reassignment | Confirmed |
| BR-004 | Support Tax-only, AQS-only and Tax-then-AQS routes; Tax precedes AQS | Confirmed |
| BR-005 | Capture Pass, Pass with issues, Insufficient evidence, Potential harm | Confirmed |
| BR-006 | Every non-pass outcome requires remediation; guidance-only stays Pass with observations | Confirmed |
| BR-007 | Retain both initial and final outcomes | Confirmed |
| BR-008 | Adviser completes remediation; T&C manager verifies Insufficient evidence and Potential harm | Confirmed |
| BR-009 | Notify para-planners without granting operational access by default | Confirmed — **built 2026-09-04**. Delivered by the PP-15 `Review submitted` event, and **live since 2026-09-05**, when the drain was switched on and the path proved end to end (para-planners were not reached before that, because nothing sent). `SubmitReviewPlugin` resolves `al_outcomecase.al_paraplanner` to a **Contact** (never a system user), so the para-planner is emailed without a licence, a web role or any operational access to the case — the "without granting operational access" half is structural, not a setting. An ambiguous or unresolvable name leaves the row unrouted and the drain marks it `Failed`, rather than mailing a guessed address (AD-082). |
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
| PP-12 | Remediation workspace: adviser response and T&C Manager attestation, both in the portal | BR-006, BR-008, FR-020 to FR-023, AD-045 | **Both halves built 2026-09-02.** Adviser response, Intelligent Office reference and completion, inline on `/remediation`; then T&C approval or rejection with notes, where a rejection reopens the action and an approval advances the case. The two halves use different mechanics on purpose (AD-074): completion is an update behind a trigger column, attestation is a *create* on `al_signoff` carrying its own permission, so an adviser cannot approve their own remediation through the shared field allowlist. `SignoffGuardPlugin` validates and stamps, `SignoffProgressPlugin` applies the consequences |
| PP-13 | Remediation SLA: clock start and stop, outstanding age, 10-working-day threshold on a business calendar | BR-010 | **Built.** OD-018 resolved 2026-08-30; working-day ageing in `app/src/lib/workingDays.ts` and on the portal remediation list. The reset-and-preserve behaviour on a rejected sign-off was completed 2026-09-03 (AD-079): `al_clockstartedon` holds the current period's start, `createdon` keeps the original, the ten-working-day threshold measures the current period, and both periods are shown rather than merged |
| PP-14 | Evidence referenced from Intelligent Office; no portal upload or storage | AD-046 | Built 2026-09-02 — `al_RemediationAction.al_evidencereference`, captured on the adviser response form |
| PP-15 | Nine notification events emitted via an outbox; email only, delivered server-side | BR-009, FR-021, AD-035 | **Built, switched on and PROVED END TO END 2026-09-05 for the five enumerated events; four events remain unnamed.** The outbox, all five emitters and now the drain are built: an asynchronous post-operation Create step on `al_notification` sends through Dataverse server-side email from the account the step runs as — no Power Automate, no connector licence (AD-081). `Review submitted` routes to the case's para-planner, closing OD-030(ii) and carrying BR-009 (AD-082). A send that fails ends at `Failed` with `al_failurereason` and stays retryable through `al_DrainNotifications`. **Deployed to DEV 2026-09-04 (`52fe14c`), switched on 2026-09-05, and proved the same evening.** The 09-04 position — drain step unregistered, mailbox unapproved, BR-009 riding on the switch — is superseded on every count. The mailbox reads `isemailaddressapprovedbyo365admin = Yes` and `outgoingemailstatus = Success`; the drain step is registered, asynchronous and enabled, running as `svc.automate.aq@ascotlloyd.co.uk`; and an allocation raised in DEV travelled emitter → outbox row → asynchronous drain → server-side email → **delivered**, twice. Evidence: two `al_notification` rows at `Sent`, each with an Outgoing/`Sent` email activity and an Incoming/`Received` copy of the same message tracked back into the service mailbox. Reproduce with `provepp15`, read the standing evidence with `pp15evidence`, both in `plugins/OutcomeTesting.Registration`. Until this run the whole path had been verified only in pieces and `al_notification` held zero rows, so nothing had ever travelled it — "switched on" and "working" were genuinely different claims. **Operational prerequisite, unchanged:** server-side email needs an approved, tested mailbox per environment, and **registering the drain step is the per-environment gate** — until an environment's mailbox is confirmed the step is not registered, rows rest at `Pending`, and nothing claims an email was sent that was not. **Still short of the requirement as written:** PP-15 says nine events and five are built, because the other four are enumerated in no requirement, knowledge file or design document (OD-030 gap (a), answered 2026-09-03 by direction to build the five). Adding the rest stays additive. |
| PP-16 | User-friendly errors; technical detail and correlation ID logged separately, never surfaced | NFR-OBS-01 | Confirmed |
| PP-17 | Reporting drill-down from outcome cards to filtered lists; full MI and export remain back-office | BR-010, FR-032 | **Built 2026-09-04 (portal).** Five outcome cards on portal Home — the four BR-005 grades plus Not yet graded — each linking to `/cases?outcome=<slug>`, with a matching Outcome filter and Outcome column added to `ot-case-list`. The grade in force is final where recorded, otherwise initial (BR-007), expressed the same way in the card aggregates and the list filter; the `al_outcome` join is outer so ungraded cases are not dropped from the worklist. Grades render through the new `OT Outcome Label` template, never as option-set numbers. Full MI and the Trail Light export stay in the Code App, which had its own drill-down from `56dd370` — that was the Code App's, not the portal's, and did not meet PP-17. See AD-083. |

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

## Functional requirement build notes
Recorded only where the built state is not obvious from the range table above.

| ID | Built state |
|---|---|
| FR-002/FR-003 | **Both halves built 2026-09-03.** The return half — invalid rows recorded as Import Exceptions with the reason, plus a downloadable validation report — now runs server-side in `al_ImportCases` (AD-077), so a caller posting to the Web API cannot bypass BR-002. The resolve half is new: `/imports` offers "Close row" on an open exception, and `al_ResolveImportException` records `Resolved` or `Ignored` with a mandatory note in its own column, leaving the validation reason intact (AD-078) |
| FR-024 | **Recheck and regrade built 2026-09-03** in the Code App at `/cases/:caseId/recheck`. The T&C Manager sets the final outcome with a mandatory reason through `al_RegradeCase`; the initial outcome is preserved so both survive (BR-007, OD-007, AD-031). The screen is gated on `page.cases` for view and `command.regrade` for the action, and passes the outcome's row version as `ExpectedRowVersion` so a regrade against a stale read is refused rather than overwriting another correction. The OD-018 clock reset on a rejected sign-off was built separately the same day (AD-079) |

## Blocking dependencies
E1 is unblocked: allocation is settled as a shared queue with manual manager assignment (OD-002 resolved, AD-040). E3 is unblocked: V8 content, response types, mandatory rule, section ownership and the CRP rule are settled in `checklist-v8.md` and AD-019 to AD-022. E5 export mapping is settled (OD-004 resolved, AD-039); the Export Batch and Export Record tables **are built** — `src/Entities/al_ExportBatch` and `al_ExportRecord`, with `al_CreateExportBatch` and `al_GenerateExport` registered (corrected 2026-09-03; this line previously said they remained to build). The architecture gate OD-011 is deferred until the app is ready to promote. PP-15's blockers are closed (OD-030 resolved 2026-09-04): the drain is built and the `Review submitted` recipient is the case's para-planner (AD-081, AD-082). **On DEV it is now proved end to end (2026-09-05)** — mailbox approved and tested, drain step registered, and two allocations delivered through to a `Received` email. The per-environment prerequisite is unchanged and still gates TEST and PROD: each needs an approved, tested server-side mailbox before its drain step is registered. AD-076 self-claim was switched on in DEV on 2026-09-03 — plug-in registered, `Case Assignment - claim from the queue` permission created, `OutcomeTesting/Claim/Enabled` set to `true`.
