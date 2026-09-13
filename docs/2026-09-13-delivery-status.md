# Delivery status — 2026-09-13

Written at commit `da4ad69` on `feat/checklist-administration`. Supersedes
`docs/2026-09-06-delivery-status.md` as the current status; that one still records how the
contact registry and web-role model were settled. The register of what is left remains
`docs/2026-09-04-outstanding-work.md`, read together with the "Still open" and "Left as they
are" sections of the deployment notes since.

Every environment claim below was verified by query against `Env_AQ_Dev` after the fact.
Where something did **not** land, it says so.

**The day in one line: the Tax team got its header edit and the case page got the paper
form, and the audit that followed found that the sign-off loop closed on 2026-09-12 had
been left open in three other places.**

---

## 1. What went in today, in order

| Commit | What | Deployment note |
|---|---|---|
| `ff78ba3` | Sign-offs name the supervisor, not the site's application user | `2026-09-13-signoff-signatory.md` |
| `88008a4` | The case header drawn as the Checker Checklist draws it, in both front ends | `2026-09-13-case-header-format.md` |
| `1868776` | The Tax check outcome reworded to Pass with issues (AD-055 amended) | `2026-09-13-tax-outcome-wording.md` |
| `e9ba5eb` | The IO extract columns created; the import half released | `2026-09-13-io-extract-schema.md` |
| `972ddf6`, `5d21fae` | A Tax checker sets the two Tax team header fields from the portal, proved end to end | `2026-09-13-tax-team-header-edit.md` |
| `6592884` | OD-053: a Tax outcome is corrected by no command | decision log |
| `e915a64` | The audit's first pass: seven defects, the duplication and cost findings, and the case page's remediation drawn as the form | `2026-09-13-audit-fixes-and-case-remediation-form.md` |
| `da4ad69` | The audit's second pass: the five angles the rate limit had cut off | same note, second section |

## 2. The sign-off loop, three times

The finding worth reading first. On 2026-09-12 `SignoffGuardPlugin.AlreadySettled` started
letting a rejected remediation action be decided again once the adviser had reworked it —
the fix for IO-300003, where every rejection had been terminal. Three other readers still
assumed a rejection was final, and each reopened the same deadlock one step along:

| Reader | Symptom | Fixed in |
|---|---|---|
| `SignoffProgressPlugin`'s replay guard, keyed on the action | The approval after a rejection found the rejection's Audit Event and returned before moving the case | `e915a64` |
| `SignoffProgressPlugin.AnyAwaitingSignoff`, counting any sign-off row as a decision | Approving the check's other actions moved the case on — or regraded and closed it — with the reworked action never approved | `da4ad69` |
| `OT Remediation`'s decision panel, offered only for actions with no sign-off row | The reworked action never came back to the supervisor at all | `da4ad69` |

All three now read only an **approved** sign-off as a decision that ends anything. Recorded
as **AD-125**, which amends AD-114(a). Not yet seen in a browser: a rejected-then-reworked
action reappearing in the supervisor's panel is the proof.

## 3. Other defects the audit closed

Plug-ins: retiring a question or section twice resurrected it for the gap; a future-dated
move left a question asked in neither section; a section could change team past the
protected-question guard; the portal header edit wrote any integer the page sent and
audited it as a return (`ReturnCase`, not `UpdateCaseDetails`); the header-edit proof
harness could re-queue a live case. Portal: the case page named the supervisor as the
application user; the header selects offered a dash that saved nothing; the portal-source
allowlist for contact columns was one column behind DEV. App: the Tax indicator keyed its
third grade on the old wording; "Not yet graded" ignored a Tax grade; a move demanded
wording it never uses. Registration: `al_RetireAndSucceedQuestion` had no `DisplayOrder`
parameter in DEV, so a reorder from the Question library would have been refused.

## 4. DEV, after

| | State |
|---|---|
| Plug-in assembly | 221,696 bytes, 46 types; the orphaned `CorrectTaxOutcomePlugin` and its custom API removed |
| Steps | 0 disabled after `registerall` |
| Web templates | OT Case Detail, OT Review Detail, OT Remediation pushed today; the case page draws remediation as the form |
| Custom APIs | 31 registered from contracts; `DisplayOrder` on `al_RetireAndSucceedQuestion` created and in the solution |
| Code App | pushed after each pass |
| `src/` | manifest and assembly type list copied back from the export (AD-013); one hand-written parameter file matching the DEV row |

| Suite | |
|---|---|
| `dotnet test` (plugins) | **804** |
| `npx vitest run` (app) | **484** |
| `tsc -b`, `eslint` | clean |

## 5. Tooling that exists because of today

- `deleteplugintype <org> <TypeName> --confirm <org>` — clears a plug-in type registered in
  the environment that the assembly no longer carries, with its custom API and step. One
  orphan refuses every `pushassembly`.
- `addcomponent <org> <type> <id> [solution]` — one named component into the solution.
  `registerall` upserts a contract's parameters into the default solution, and nothing put
  them in `OutcomeTesting`. The tool's component-type table now carries this environment's
  numbers (10038–10040 for custom API parts), read from `solutioncomponentdefinition`.
- `verifytaxheader` refuses a case at Assigned or Review In Progress, which it cannot restore.

## 6. Still open

- **Browser proof** of the case page's remediation block and of the reworked-action sign-off
  panel (§2).
- **Altitude**: `ProtectedCodeIn` on `RetireSectionPlugin`, the two window refusals on their
  plug-ins, `CaseHeaderTable`'s variant flag, the working-day script inlined in two
  templates. Placement, not behaviour; listed in the audit note.
- **`ChecklistVersionInForce`** counts the effective-to day as in force where a question
  version or section does not. One place now, documented, not changed.
- **`origin/main`** has not been advanced past `88e6182`; the branch carries both audit
  commits. Merging is the project owner's call.
- Everything in `docs/2026-09-04-outstanding-work.md` not closed by a later note.
