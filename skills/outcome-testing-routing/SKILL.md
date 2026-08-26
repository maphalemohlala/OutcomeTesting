---
name: outcome-testing-routing
description: Implement the approved Tax, AQS and remediation state machine. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Outcome Testing Routing

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, and `../../knowledge/phase-skill-map.md` before acting.

## Instructions
Model legal transitions explicitly. Required routes: Tax only, AQS only, Tax then AQS; never parallelise Tax and AQS unless requirements change. Invalid cases close as cancelled and resubmit as new. Tax records Pass/Fail and its own evidence. AQS records the four approved outcomes. Preserve initial and final outcomes and assignment history. Enforce transition guards, idempotency and audit events. Generate route tests including reassignment, incorrect routing, return, recheck, remediation rejection and closure.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
