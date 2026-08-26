---
name: reporting-export-integration
description: Design MI, export batches and external integrations. Use for the Ascot Lloyd Outcome Testing solution when the task matches this domain.
---
# Reporting Export Integration

## Required context
Read `../../knowledge/project-context.md`, `../../knowledge/knowledge-sources.md`, and `../../knowledge/phase-skill-map.md` before acting.

## Instructions
Separate Export Batch from Export Record: batch controls an execution; records track item-level payload/status/error. Define source-to-target mapping, data types, required fields, transformations, file naming, reconciliation, duplicate prevention, retries and partial-failure handling. Create Trail Light-compatible Excel/CSV output only from an approved mapping. Keep manual SFTP as the baseline unless automation is approved. For MI, use server-side queries and documented metric definitions; never calculate regulated metrics from ambiguous fields.

## Required output
- Decisions and assumptions
- Files/components changed
- Tests or verification evidence
- Risks and rollback notes
- Requirement IDs affected
