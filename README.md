# Outcome Testing

Source for the Ascot Lloyd Outcome Testing solution (OTIS in production) on Microsoft Power Platform.

## Repository layout

| Folder | Contents | Deploys with |
|---|---|---|
| `release/` | The installable managed solution. Latest is 1.0.32.0: `OutcomeTesting_1_0_32_0_managed.zip` for environments named Outcome Testing. Production (OTIS) has only `OTIS_1_0_30_0_managed.zip` (1.0.30.0); an OTIS 1.0.32.0 package is made from the 1.0.32.0 zip with the registration tool's `brandpackage` verb. `OutcomeTesting_1_0_30_0_managed.zip` is the previous release | `scripts/Import-Solution.ps1 -Managed` |
| `src/` | Unpacked Dataverse solution `OutcomeTesting`: tables, security roles, plug-in steps, the Code App package and portal components. Refreshed from DEV at 1.0.33.0 but not yet committed - see below | - |
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

## `src/` is refreshed but not committed

The working tree's `src/` was refreshed from DEV with `scripts/round-trip-src.ps1`: `src/Other/Solution.xml` reads 1.0.33.0, one version ahead of the latest release (1.0.32.0). The committed `src/` is still the much older export (`Solution.xml` 1.0.6.0). The refresh left 73 untracked files under `src/` that the owner must commit with it, among them both environment-variable definitions (`src/environmentvariabledefinitions/al_NotificationSenderAddress` and `al_ProductName`); until then a clone does not carry them.

Install from `release/`, never from a package packed out of `src/`. `src/PluginAssemblies/` holds whatever DLL the last export carried, and an import of it would replace the live assembly; `plugins/deploy/Pack-Schema-Solution.ps1` and a build of `AscotLloydOutcomeTesting.cdsproj` both strip it before packing. To bring `src/` up to date, export the `OutcomeTesting` solution from DEV as unmanaged and mirror it into `src/` with `scripts/round-trip-src.ps1`.

## Configuration

Environment-specific values are Dataverse environment variables, never literals in code: `al_NotificationSenderAddress` (sender mailbox), `al_PortalBaseUrl` (portal link in letters) and `al_ProductName` (OTIS in production). The solution carries the first and last; `al_PortalBaseUrl` is left out on purpose, so create it in each environment. Set all three values per environment after import.
