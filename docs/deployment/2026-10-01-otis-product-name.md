# OTIS in PROD, Outcome Testing in DEV and TEST (1.0.18.0)

Date: 2026-10-01. Spec: `docs/superpowers/specs/2026-10-01-otis-product-name-design.md`.

## What was asked

The owner wants the app, the portal and the solution called **OTIS** in PROD only. DEV and TEST
keep "Outcome Testing". In PROD the change covers titles, document headings, role names and team
names. The solution changes its display name only, and the portal gets a new address. The owner
chose one per-environment setting over editing PROD by hand.

## What changed

| Part | Change |
|---|---|
| Plug-ins | `ProductName` reads `al_ProductName`, with a fallback of "Outcome Testing". Every name that carries the product is derived from it: the manager web role, the App User, App Admin and Team Manager roles, the Tax and AQS teams, the queue account, and the checklist and remediation headings. `EnvironmentVariable` is the shared reader; `PortalSite` now uses it too. The privilege-denied message no longer names the product. |
| Registration tool | `grantapprole`, `checkassignable`, `grantsecurity`, `grantteamsecurity`, `ensureaccessprincipals`, `fixpermissions` and the web-role seed read the target environment's name. New verb `brandpackage <in.zip> <out.zip> <product>`. |
| Code App | `app/product/` holds the derived names, a store and a loader. The loader reads `al_ProductName` through two new data sources. The shell header, page title, document headings, error page, allocation scope and fallback permission rules follow it. |
| Portal | Eight templates read `settings['OT/ProductName'] \| default: 'Outcome Testing'`: Header, OT Access Denied, OT Case Detail, OT Case List, OT Home, OT My Work, OT Remediation and OT Review Detail. Header, OT Case List and OT My Work also derive `ot_manager_role`. |
| Solution | New environment variable `al_ProductName`, with the default "Outcome Testing". |

**Packaged labels.** Seven labels are fixed in the package: the solution display name, the
three security roles, the Code App display name, the manager web role and the site name. The
PROD package rewrites them with `brandpackage`. Run against the real 1.0.17.0 and 1.0.18.0
exports, it changed exactly those 7 labels in 4 files and left the other 610 and 611 files
byte-identical.

**The `StaffCode` declaration.** Adding the two data sources regenerated
`dataSourcesInfo.ts`, which dropped the hand-declared `StaffCode` parameter of `al_UpdateUser`
again. It was restored, and its guard tests pass.

## Tests

- **Plug-ins:** 1765/1765, including 11 new `ProductNameTests`: today's literals under the
  default, the OTIS forms, allocation, reconciler lookups, and the fallbacks.
- **Code App:** 1242/1242, and `tsc -b` is clean. New tests:
  - `productName.test.ts` (8);
  - `productNameDerived.test.ts` (5). It fails if a template, app string or plug-in string
    writes the name out. It was proved by planting a literal in OT Home.
- **Updated tests:** `portalPageAccess.test.ts` now asserts the derived manager check.
- **`Check-PortalTemplates.ps1`:** passes for every edited template. It reports one failure in
  OT AQS Notes: without `-OrgUrl` it cannot type Q-GR-03 and Q-GR-04. That predates this
  change.

## DEV

| Step | Result |
|---|---|
| `al_ProductName` | Created with the default "Outcome Testing"; `addcomponent` 380, in OutcomeTesting |
| Assembly | Release built and pushed. sha256 `965e689a…`, read back from DEV |
| Eight templates | Pushed. DEV's copies were identical to the repo before the push |
| Code App | `index-fDUUEYvN.js`, pushed; the player serves it |
| Browser, as Service Account | Code App header and title "Outcome Testing", full 11-item menu. Portal case list: 16 rows and the oversight toggle. Review page headed "Outcome Testing - Checker Checklist" |

## TEST - 1.0.18.0

Exported managed to `artifacts/2026-10-01-otis/` and imported by the agent: operation
`0fe25db8…` succeeded at 08:41Z. The full comparison was then run again:

| Check | Result |
|---|---|
| Solution | 1.0.18.0 managed; components identical by type and name |
| Assembly | `965e689a…`, identical to DEV; 65 steps, all enabled |
| APIs, schema, roles, site settings | identical |
| `al_ProductName` | present, default "Outcome Testing", no value |
| Templates against the repo | 49 of 51. OT Review Detail and OT My Work keep TEST-only copies from earlier direct pushes, so the import cannot reach them. Their literal role check means the same thing on TEST |

**Owner, TEST:** the agent's re-push was refused ("Production Deploy"). Push both templates so
future imports reach them:

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; $t='plugins\OutcomeTesting.Registration\bin\Debug\net8.0\OutcomeTesting.Registration.exe'; $w='powerpages\outcome-testing---outcometesting\web-templates'
& $t pushwebtemplate https://org37995f36.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b "$w\ot-review-detail\OT-Review-Detail.webtemplate.source.html"
& $t pushwebtemplate https://org37995f36.crm11.dynamics.com a1000000-0000-4000-8000-000000000013 "$w\ot-my-work\OT-My-Work.webtemplate.source.html"
```

## PROD (`https://org3461d426.crm11.dynamics.com/`)

**Surveyed read-only on 2026-10-01.** Nothing of this project is there yet: no `al_` publisher,
tables, portal site or Code App. The Power Pages platform solutions are present and newer than
TEST's. `svc.automate.aq` is System Administrator.

**The wrong package was imported first (09:30-09:47Z).** PROD received
`artifacts/OutcomeTesting_1_0_8_0_managed.zip`, an export from 2026-09-22 that sits at the top of
`artifacts/`, rather than the OTIS package. Read back from PROD:

| Item | PROD now |
|---|---|
| Solution | "Outcome Testing" 1.0.8.0, managed |
| Assembly | sha256 `caf43fbb…` (TEST and DEV: `965e689a…`); 31 steps, all enabled; 31 APIs (TEST 32) |
| Names | roles "Outcome Testing App User" and "App Admin" (no Team Manager); Code App "Ascot Lloyd Outcome Testing"; site "Outcome Testing - outcometesting"; web role "AL Portal - Outcome Testing Manager" |
| `al_ProductName` | absent, so step 2 below failed |
| Teams and account | `ensureaccessprincipals` ran at 09:49Z. With no variable it fell back to the default and created "Outcome Testing - Tax Team" (`104c6a63…`), "Outcome Testing - AQS Team" (`85046a69…`), their two default queues, and the account "Outcome Testing - AQS Team" (`89046a69…`). The teams have no members and no roles |

Importing the OTIS 1.0.18.0 package over it upgrades in place and renames every packaged label.
The teams and the account are data, so the upgrade does not touch them. They have to be deleted
or renamed by hand. The agent was refused the delete.

The registration verbs that create or look up named things now refuse a target with no
`al_ProductName` definition, instead of quietly using the default (`TargetProduct`).

**Recovery, read back by the agent.**
1. The owner removed 1.0.8.0 (10:31-11:12Z). Two OTIS imports started meanwhile were turned
   away by the uninstall lock and changed nothing.
2. OTIS 1.0.18.0 was installed fresh, 11:26-11:45Z.
3. The old teams and account were deleted, and `ensureaccessprincipals` ran at 11:58Z.

| Check | PROD |
|---|---|
| Solution | "OTIS" 1.0.18.0, managed; component counts by type equal TEST's |
| Assembly | `965e689a…`, identical to DEV and TEST; 65 of 65 steps enabled, the same step names as TEST; 32 APIs, the same as TEST |
| `al_ProductName` | value "OTIS" |
| Names | roles OTIS App User, App Admin and Team Manager; Code App "OTIS"; site "OTIS"; web role "AL Portal - OTIS Manager" |
| Teams and account | "OTIS - Tax Team" and "OTIS - AQS Team" in the root business unit, with no roles, as in TEST; their default queues; account "OTIS - AQS Team". No "Outcome Testing" principals remain |

Steps 1 to 3 below are done. Steps 4 to 7 wait on the owner's decisions.

The branded package `artifacts/2026-10-01-otis/OTIS_1_0_18_0_managed.zip` is ready. The agent's
import was refused, and so was writing its request file, so every PROD write below is the
owner's. After each one, the agent can check the result read-only.

1. **Import the package.** Power Apps → PROD → Solutions → Import → that zip. Alternatively,
   re-authenticate `pac` and run
   `.\scripts\Import-Solution.ps1 -Environment https://org3461d426.crm11.dynamics.com/ -ZipFile artifacts\2026-10-01-otis\OTIS_1_0_18_0_managed.zip -Managed`.
2. **Name the product.** Run `webapimany` with
   `artifacts\2026-10-01-otis\prod-product-name.json`. It sets `al_ProductName` to OTIS, by
   schema name.
3. **Create the teams and the queue account.** Run `ensureaccessprincipals <PROD> --confirm <PROD>`.
   It prints the product name it read; this must say OTIS. It then creates the OTIS Tax and
   AQS teams and the queue account.
4. **Portal: decisions needed.** These must happen in Power Pages and Entra:
   - reactivate the imported site at the new address;
   - give it its own Entra sign-in (app registration and the `Authentication/*` settings);
   - add the site setting `OT/ProductName` = OTIS, kept out of the solution.
   - add `al_PortalBaseUrl`, created per environment and never shipped.
5. **Configuration seed: decision needed.** The seed covers routes, the checklist, fail
   reasons, list options with legacy values, notification templates, `al_role`, and page
   permissions with the manager role written as `AL Portal - OTIS Manager`. Before it can be
   built, the owner must choose which environment's checklist PROD starts from: DEV's or TEST's.
6. **People: decision needed.** Role mappings, adviser mappings, and `OTIS App User` plus
   Basic User for each person.
7. **Email.** Approve and enable the sending mailbox.
