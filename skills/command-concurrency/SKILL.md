---
name: command-concurrency
description: Design privileged lifecycle commands, correlation IDs and optimistic concurrency. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Command and Concurrency

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, `../../knowledge/phase-skill-map.md`, `../../knowledge/requirements-index.md` and `../../knowledge/decision-log.md` before acting.

## Instructions
Separate queries from commands. Reads may use generated Dataverse services behind repositories; every state transition must go through a named server-side command implemented as a Dataverse custom API or a solution-aware flow, never as an unrestricted client update.

Approved commands: `ImportCases`, `AssignCase`, `ReturnCase`, `StartReview`, `SubmitReview`, `IssueOutcome`, `CompleteRemediation`, `SignOffRemediation`, `RegradeCase`, `CreateExportBatch`. Do not add a command without a requirement ID.

For each command define: caller roles, preconditions and transition guard, input contract, expected row version, correlation ID, idempotency key, side effects, emitted Audit Event, notification trigger, failure codes and rollback behaviour.

Concurrency rules: the client sends the row version it read; the command rejects a stale version with a distinct conflict failure code; the client surfaces a reload-and-retry path rather than silently overwriting. Retried commands with the same idempotency key must not duplicate records or actions.

Immutability rules: submitted review responses and initial outcomes are never updated in place. Reopen, override and regrade require an elevated role and a mandatory reason, and remain blocked until OD-007 is resolved.

Produce unit tests for guards and idempotency, and integration tests for the conflict path, the unauthorised-caller path and the replay path.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
