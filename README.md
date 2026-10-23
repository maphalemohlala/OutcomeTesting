# Outcome Testing

Source for the Ascot Lloyd Outcome Testing solution (OTIS in production) on Microsoft Power Platform.

## Repository layout

| Folder | Contents | Deploys with |
|---|---|---|
| `src/` | Unpacked Dataverse solution `OutcomeTesting`: tables, security roles, plug-in steps, the Code App package and portal components | `scripts/Import-Solution.ps1` |
| `app/` | Power Apps Code App (React, TypeScript, Vite) | `npm run build`, then `pa app push` |
| `powerpages/` | Power Pages portal metadata and its check scripts (see `powerpages/README.md`) | `powerpages/Deploy-Portal.ps1` |
| `plugins/OutcomeTesting.Plugins` | Dataverse plug-ins and Custom API handlers (.NET Framework 4.6.2) | the registration tool |
| `plugins/OutcomeTesting.Plugins.Tests` | Unit tests for the plug-ins | `dotnet test` |
| `plugins/OutcomeTesting.Registration` | Command-line tool that registers plug-in types, steps and Custom APIs | - |
| `plugins/OutcomeTesting.DataImport` | Headless Configuration Migration import for the `data/` packages | - |
| `plugins/customapi`, `plugins/deploy` | Custom API contracts and registration scripts | - |
| `data/` | Configuration packages: checklist, sections, questions, fail reasons, review routes and roles. `io-task-extract-sample.csv` is an anonymised fixture for the unit tests | `OutcomeTesting.DataImport` |
| `knowledge/checklist-v8.md` | The Checker Checklist V8 content catalogue, read by `powerpages/Check-PortalTemplates.ps1` | - |
| `docs/` | Solution guide, technical documentation, role user guides and the data model | - |

## Building

- Code App: `cd app`, `npm ci`, `npm run build`. Unit tests: `npm test`.
- Plug-ins: `dotnet build plugins/OutcomeTesting.Plugins -c Release`. Unit tests: `dotnet test plugins/OutcomeTesting.Plugins.Tests`.
- The plug-in assembly is strong-name signed. The signing key `plugins/OutcomeTesting.Plugins/OutcomeTesting.Plugins.snk` is not in this repository and is supplied separately; the build fails without it.

## Configuration

Environment-specific values are Dataverse environment variables, never literals in code: `al_NotificationSenderAddress` (sender mailbox), `al_PortalBaseUrl` (portal link in letters) and `al_ProductName` (OTIS in production). They are not shipped in the solution, so create and set them in each environment.
