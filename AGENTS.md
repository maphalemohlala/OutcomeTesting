# Ascot Lloyd Outcome Testing Copilot instructions

This repository builds an enterprise Outcome Testing solution on Power Platform using Dataverse, Power Apps Code Apps, Power Automate, optional Power Pages and managed ALM across DEV, TEST and PROD.

## Mandatory method
1. Read `knowledge/project-context.md`, `knowledge/phase-skill-map.md`, `knowledge/requirements-index.md` and `knowledge/decision-log.md`.
2. Identify the current phase and use the matching `skills/*/SKILL.md` instructions.
3. Use Superpowers brainstorming for unclear design, writing-plans before coding, TDD for testable code/business logic, systematic-debugging for failures, requesting-code-review before merge, and verification-before-completion before a completion claim.
4. Never invent a business rule. Cite the requirement ID from `knowledge/requirements-index.md`, and name the blocking OD ID from `knowledge/decision-log.md` rather than assuming a resolution.
5. Make the smallest safe change. Preserve traceability and rollback.
6. DEV is the only authoring environment. TEST and PROD use managed deployments.
7. Do not hardcode secrets, tenant/environment IDs, URLs, connection IDs, email addresses or group IDs.
8. Do not expose credentials, personal information or production data in prompts, fixtures, logs or commits.
9. Prefer the current `pa` CLI for new Code Apps. Verify live Microsoft Learn syntax before commands. Use PAC CLI for platform/solution operations supported by its current documentation.
10. A task is not complete until relevant tests pass and evidence is reported.
11. Any task touching UI must activate `skills/ascot-lloyd-design/SKILL.md` first. `brand/` is the only authoritative source of colours, fonts, logos and imagery. Never guess or substitute brand values.

## Architecture invariants
- Dataverse is the system of record.
- Tax and AQS run sequentially when both are required.
- Preserve initial/final outcomes, question versions, submitted responses and assignment/status history.
- Submitted review sections are immutable to unauthorised downstream roles.
- Integrations are idempotent, logged and recoverable.
- Security is enforced in Dataverse/Power Pages permissions, never only in UI code.

## Skill discovery
Before work, inspect `skills/` and load every directly applicable SKILL.md. The `project-orchestrator` skill selects phase skills for multi-domain tasks.
