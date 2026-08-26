# Development phase and skill map

## Mandatory operating sequence
For every feature: discover -> design -> plan -> implement -> verify -> review -> document. Do not skip a gate because code can be generated quickly.

| Phase | Primary skills | Exit gate |
|---|---|---|
| 0 Foundation and architecture | project-orchestrator, requirements-traceability, power-platform-alm | Environment, repository, solution boundaries, variables and connection strategy documented; blocking open decisions listed in `decision-log.md` |
| 1 Dataverse foundation | dataverse-solution-design, requirements-traceability | Schema, relationships, ownership, keys, choices and audit reviewed |
| 2 Security | power-platform-security | Role matrix includes positive and negative access tests |
| 3 Code App shell and case management | code-app-development, ascot-lloyd-design, command-concurrency, dataverse-solution-design | Local build, authentication, data access and core screens verified |
| 4 Routing and automation | outcome-testing-routing, command-concurrency, power-automate-engineering | Tax-only, AQS-only, Tax-to-AQS and invalid-case paths pass tests |
| 5 Remediation | outcome-testing-routing, power-automate-engineering, power-platform-security | Initial/final outcomes, returns, sign-off and locks verified |
| 6 MI, export and integrations | reporting-export-integration, power-automate-engineering | Reconciled export, failure handling and MI acceptance tests pass |
| 7 Power Pages, only if approved | power-pages-development, power-platform-security | Web roles, table permissions and end-to-end portal tests pass |
| 8 Test and release | test-quality-release, power-platform-alm | Managed deployment, smoke/UAT/security/performance checks and rollback runbook pass |

## Superpowers integration
Use Superpowers `brainstorming` before architecture or ambiguous design, `writing-plans` before implementation, `test-driven-development` for TypeScript/JavaScript/C#/plugins and testable business logic, `systematic-debugging` for failures, `requesting-code-review` before merge, and `verification-before-completion` before reporting completion. Use this project's domain skills after the relevant Superpowers method skill.

## Cross-phase inputs
`knowledge/requirements-index.md` supplies the requirement IDs every skill must cite. `knowledge/decision-log.md` records open decisions; work blocked by an open decision must name the OD ID instead of assuming a rule. `ascot-lloyd-design` is mandatory for any task that touches UI, in any phase, and treats `brand/` as the only source of visual truth.
