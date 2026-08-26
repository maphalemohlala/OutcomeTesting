---
name: power-platform-debugging
description: Diagnose PAC, Code Apps, Dataverse, flow and ALM failures. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Power Platform Debugging

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, and `../../knowledge/phase-skill-map.md` before acting.

## Instructions
Use Superpowers systematic-debugging first. Capture exact command, active auth/environment, versions, logs and minimal reproduction. Check identity/environment mismatch before changing artefacts. Separate build, authentication, metadata, connector, runtime and deployment failures. Change one variable at a time. Verify the fix using the original failing path and add a regression check. Never delete solution layers or production data as a troubleshooting shortcut.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
