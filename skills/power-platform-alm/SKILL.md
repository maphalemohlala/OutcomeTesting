---
name: power-platform-alm
description: Control solutions, environments and deployments. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Power Platform Alm

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, and `../../knowledge/phase-skill-map.md` before acting.

## Instructions
DEV is the only unmanaged authoring environment. TEST and PROD receive managed artefacts. Define solution boundaries without unnecessary fragmentation, publisher prefix `al`, semantic versioning, connection references, environment variables, deployment settings and service principal use. Use `pac solution unpack/pack` where applicable, commit unpacked source, validate dependencies and use managed upgrades rather than uncontrolled layering. Require backups, deployment logs, smoke tests and rollback steps for releases.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
