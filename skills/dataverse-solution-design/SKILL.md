---
name: dataverse-solution-design
description: Design and validate the Dataverse model. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Dataverse Solution Design

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, and `../../knowledge/phase-skill-map.md` before acting.

## Instructions
For every table define display/schema name using publisher prefix `al`, purpose, ownership, primary name, alternate keys, columns, choices, relationships, cascading behaviour, auditing, duplicate detection, retention and security. Prefer configuration-driven question versions and immutable submitted responses. Preserve initial and final outcomes. Validate referential integrity and prevent circular ownership. Output schema changes in a form suitable for solution source control and PAC model generation. Do not generate destructive migration steps without an explicit backup and rollback plan.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
