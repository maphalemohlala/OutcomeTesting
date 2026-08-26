---
name: code-app-development
description: Build the Power Apps Code App in VS Code. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Code App Development

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, and `../../knowledge/phase-skill-map.md` before acting.

## Instructions
Use TypeScript and the repository-selected SPA framework. Inspect `power.config.json`, package scripts and generated services before editing. Use the current `pa` CLI for new Code Apps and verify live Microsoft Learn syntax; treat older `pac code` commands as legacy where Microsoft documentation indicates replacement. Keep generated connector models separate from domain services. Implement accessible, responsive components, server-side filtering/paging, explicit loading/empty/error states, centralised error handling and telemetry without sensitive data. Build and test locally, then publish only to the active DEV environment. Never hardcode environment IDs, URLs or secrets.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
