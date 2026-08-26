# Ascot Lloyd Copilot Skills Pack

Repository-ready Copilot and agent guidance for the Outcome Testing solution.

## Install
1. Extract the archive into the root of the Ascot Lloyd repository.
2. Preserve `.github/copilot-instructions.md`, `.github/instructions/`, `.github/prompts/`, `AGENTS.md`, `skills/` and `knowledge/`.
3. Commit the files to source control and review them through a pull request.
4. In Copilot Chat, ask the agent to read `AGENTS.md`, then run the reusable prompt for the current phase.
5. If using GitHub Copilot CLI with Superpowers, install Superpowers separately using its official repository instructions.

## Included
- 14 project-specific skills
- Phase-to-skill orchestration
- Repository-wide and path-specific Copilot instructions
- Reusable prompt files
- Project domain context, requirements index and decision log
- Official Microsoft and GitHub knowledge source catalogue

## Repository layout
- `src/` unpacked Dataverse solution root, packaged in full; flows unpack to `src/Workflows/`
- `app/` Power Apps Code App, deliberately outside the solution root
- `brand/` authoritative Ascot Lloyd visual resources; the only source of colours, fonts and marks
- `knowledge/` domain context, requirements index, decision log, knowledge sources
- `skills/` phase skills; `.github/` Copilot instructions and prompts

## Important
The skill pack contains guidance, not secrets or environment identifiers. Replace repository path examples only where your actual folder layout differs. Before using CLI syntax, verify the linked live Microsoft documentation.
