# "Vulnerable client?" becomes a managed dropdown list

Date: 2026-09-30
Status: Design agreed in conversation 2026-09-30; written spec awaiting review
Source: project owner, 2026-09-30: "Add Vulnerable client to the dropdown options so we can
manage it like the other options"
Builds on: AD-187 (options as rows), AD-188 (the other four lists migrated)

## Problem

The case header's **Vulnerable client?** is a fixed choice column (`al_vulnerableclient`: Yes,
No, Potentially vulnerable, N/A). Changing its options means a developer adds choice metadata,
rebuilds the Code App and re-pushes the portal template, and TEST and PROD run the managed
solution, where that cannot be done at all (AD-187's three reasons). The other five header lists
(Product / solution type, Sample source, Case type, Pre or post check, Products) are already rows
an administrator manages on **Dropdown options** with no deployment.

## Outcome

**Vulnerable client?** is a sixth list on Dropdown options. An administrator can add, rename,
reorder, retire and reinstate its options there, and the choice made on a case reads and saves
through the list everywhere the field appears. Its starting options are today's four, in today's
order. Every existing case keeps the value it holds.

## Design: the AD-188 shape, applied once more

| Part | Change |
|---|---|
| `al_listoption.al_list` | New value **120910845 "Vulnerable client"**, the next in the block (Products is 120910844) |
| `al_outcomecase` | New lookup **`al_vulnerableclientid`** to `al_listoption`, Restrict cascade, relationship `al_listoption_al_outcomecase_vulnerableclient`. `al_vulnerableclient` stays as the legacy column, as the other four did |
| Registration tool | One `Def` added to `ListOptionTable.Lists`. The existing idempotent verbs then do the work: `createlistoptiontable` adds the lookup; `seedlistoptions` creates the four rows from the choice column's own metadata (never a list typed out a second time) and points every case holding a value at its row |
| Plug-ins | `ListOptionRules`: the list constant and attribute. `UpdateCaseDetailsPlugin.Editables`: `al_vulnerableclientid` as a ListOption field, so an option from another list is refused ("... is not an option on that list"). `CaseHeaderRequestPlugin.CheckerEditable`: the lookup joins the checker's header fields. `CompletedCheck`: the PDF reads the lookup, falling back to the legacy value |
| Code App | `listOptions.ts`: `LIST_VULNERABLE_CLIENT` in `MANAGED_LISTS`, so it appears on Dropdown options and in `SINGLE_CHOICE_LISTS`. `CaseEditPanel`, `caseDetailMapping` and `checklistForm` read and write the lookup like Sample source. Generated model refreshed for the new column |
| Portal | `OT Review Detail` header: the Vulnerable client select is drawn from the list's rows, as the other four are; the existing test that every portal dropdown equals `MIGRATED_LISTS` forces this. `OT Case Detail`: reads the lookup, falling back to the legacy value |

Everything else about managed lists already applies unchanged: effective-dated retiring, a
retired option still reading correctly on a case that holds it, and delete refused while any
case uses the option.

## Deployment

**DEV:**
1. Add the `al_list` value.
2. Run `createlistoptiontable`, then `seedlistoptions`.
3. Add the new column and relationship to the solution, then run the membership check.
4. Push the assembly, the two templates and the Code App.

**Proved in DEV:**
- The list appears on Dropdown options with four options.
- A case's existing value reads the same in the Code App, both portal pages and the PDF.
- A new option added on the page can be chosen on a case, in the Code App and on the portal header.
- An option from another list is refused.

**TEST, when the owner says:**
1. Export the solution as 1.0.17.0 and import it.
2. Run `seedlistoptions` against TEST. It creates the four rows there and backfills TEST's cases, as was done for the first lists in 1.0.6.0. Its writes are expected to be refused to the agent, so they are handed to the owner.
3. Check the three portal components that direct pushes have masked before.

## Testing

- **Unit (vitest):**
  - the list is declared and offered;
  - the portal dropdown set still equals `MIGRATED_LISTS`;
  - `caseDetailMapping` and `checklistForm` read the lookup, with the legacy fallback.
- **Plug-ins:**
  - the header and command paths accept a Vulnerable client option and refuse one from another list;
  - the PDF shows the option's name.
- **Live:** the DEV steps above.

## Out of scope

Removing the legacy `al_vulnerableclient` column; adding any new option (the owner adds options
on the page); the reports and export, which do not read this field today.
