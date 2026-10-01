# The product is called OTIS in PROD - design

Date: 2026-10-01. Owner direction, the same day:

- PROD calls the app, the portal and the solution **OTIS**.
- DEV and TEST keep "Outcome Testing".
- In PROD the change covers the app and portal titles, the document titles, and the role and
  team names.
- The solution's **display** name changes; its unique name stays `OutcomeTesting`.
- PROD's portal gets its own address.
- Owner's choice of approach: one per-environment setting (option 2), not hand edits in PROD.

## The problem

"Outcome Testing" is a label in some places and a **key** in others:

| Kind | Where | Examples |
|---|---|---|
| Label | plug-in documents and messages | `ChecklistDocument.Title`, `CompletedCheck.FormFooter`, `RemediationDocument.FormFooter`, the AssignCase refusal |
| Label | Code App | shell product name, `index.html` title, document footers, error text |
| Label | portal templates | OT Home card, access-denied text, doc footers, the Checker Checklist heading |
| **Key** | plug-in | `AllocationScope` (manager web role), `AssignCasePlugin` (security role names), `CaseAccessReconciler` (team and account names), `PermissionHelpers` (app-role label) |
| **Key** | Code App | `APP_ROLES`, the default permission rules, `allocationScope` |
| **Key** | portal | `user.roles contains 'AL Portal - Outcome Testing Manager'` in Header, OT Case List, OT My Work |
| **Key** | data | `al_rolecode` on page permissions and role mappings; team and account names |
| **Key** | registration tool | `EnsureRole`, team setup, `WebRoleSeed` |
| Packaged label | solution | solution display name, Code App display name, security role names, the manager web role, the site name, the `al_approle` label "Outcome Testing Manager" |

Renaming a key in one place only breaks access. Renaming a packaged component directly in PROD
leaves an unmanaged layer that hides every later import of it. That is what broke TEST's Review
page twice on 2026-10-01.

## The design

**One product name per environment. Every key and label is derived from it, never written
out.**

1. **The source of the name.**
   - **Plug-in, registration tool and Code App:** a new environment variable,
     `al_ProductName`. Its definition travels in the solution with the default
     "Outcome Testing". PROD sets its value to "OTIS".
   - **Portal:** a site setting, `OT/ProductName`. It is created only in an environment that
     changes the name, and kept out of the solution. Templates read
     `settings['OT/ProductName'] | default: 'Outcome Testing'`, so DEV and TEST need nothing.
2. **The derived names.** One function per codebase, from the product name P:

   | Name | Derived as |
   |---|---|
   | Manager web role | `AL Portal - P Manager` |
   | Security roles | `P App User`, `P App Admin`, `P Team Manager` |
   | Teams and the AQS queue account | `P - Tax Team`, `P - AQS Team` |
   | App-role label | `P Manager` |
   | Checklist title and footer | `P - Checker Checklist`; `P Checker Checklist \| V5 Draft` |
   | Remediation and case record footers | `P — Remediation and escalation \| V8`; `P — Case record \| V8` |

   The other web roles (Tax Reviewer, Planner, Tax and AQS Team Manager, and so on) do not carry
   the product name, and do not change.
3. **Packaged labels: a tested rewrite of the PROD package.**
   - These labels are fixed inside the package, so no setting can reach them: the solution
     display name, the Code App display name, the three security role names, the manager web
     role name, the site name and the `al_approle` label.
   - A registration-tool verb, `brandpackage <in.zip> <out.zip> <product>`, rewrites exactly
     those labels in a managed export. It refuses a package where any expected label is
     missing.
   - Every PROD release runs it. The package keeps every id, so upgrades stay in-place
     upgrades.
4. **Data.** PROD is seeded with the derived names: page-permission and role-mapping role
   codes, teams, and the account. The registration verbs read `al_ProductName` from the target
   environment.
5. **The portal address.** It is chosen when PROD's site is provisioned. `al_PortalBaseUrl` is
   set to it, and nothing else moves.

## Decided during the build

- **The legacy `al_approle` label "Outcome Testing Manager" is not renamed.**
  - The code describes the picklist as "retained only to label rows that already carry
    al_approle", and new access is written against web role names.
  - PROD starts with no such rows, so the label is never shown there.
  - Renaming it would mean passing the product name through six static parsers in
    `PermissionHelpers`, and through `domain.ts` and `permissions.ts` in the app, for no
    visible change.
- **`PermissionHelpers` and the Code App's `permissions.ts` keep the default literal in one
  place each.** `brandRules` renames the manager role in the built-in fallback rules. Stored
  rules are data, and PROD's are seeded with the OTIS name.
- **The privilege-denied message no longer names the product.** It is a constant with no
  access to the environment.

## Not changing

- The unique names `OutcomeTesting` (solution) and `OutcomeTesting.Plugins` (assembly).
- The `al_` prefix.
- Table and column names.
- Folder names, and DEV and TEST behaviour. With no value set, every derived name equals
  today's name.

## Proof

- **Unit tests:** every derived name for P = "Outcome Testing" equals today's literal, and for
  P = "OTIS" equals the OTIS form.
- **Plug-in:** the variable is read with a value, with only its default, and with neither.
- **App:** the shell and the permission defaults follow the name.
- **Portal:** the static template check fails if a template writes the manager role or a title
  with the product name spelled out.
- **`brandpackage`:** run against the real 1.0.17.0 export. Every label changes, and the ids
  and the byte-identical assembly do not.
- **DEV and TEST after deployment:** behaviour unchanged. The parity comparison shows no
  difference.
- **PROD after install:** the derived names exist and match. A browser check of the portal and
  the Code App under the OTIS name.
