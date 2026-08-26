---
applyTo: "src/Entities/**,src/Other/**,src/OptionSets/**,src/Roles/**,src/FieldSecurityProfiles/**,src/AppModules/**,**/*.cdsproj"
---
Use `skills/dataverse-solution-design/SKILL.md` and `skills/power-platform-alm/SKILL.md`. Preserve publisher prefix al, alternate keys, ownership, auditing and managed ALM.

The unpacked solution root is `src` (see `SolutionRootPath` in `AscotLloydOutcomeTesting.cdsproj`). Everything under `src/` is packaged into the solution, so never place Code App or tooling source there.
