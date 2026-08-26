---
name: requirements-traceability
description: Convert requirements into buildable, testable work. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Requirements Traceability

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, `../../knowledge/phase-skill-map.md`, `../../knowledge/requirements-index.md` and `../../knowledge/decision-log.md` before acting.

## Instructions
Maintain the requirement matrix in `knowledge/requirements-index.md` using the columns defined there: ID, source, actor, workflow step, priority, acceptance criteria, data entities, UI, automation, security, tests and release. Distinguish confirmed requirements, assumptions, open decisions and backlog. Record open decisions in `knowledge/decision-log.md` and never resolve one without a named owner, date and source. Reject invented business rules. Before implementation, ensure each story has Given/When/Then acceptance criteria. After implementation, link commits/tests/solution components to requirement IDs.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
