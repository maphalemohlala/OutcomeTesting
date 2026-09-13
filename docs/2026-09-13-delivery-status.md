# Delivery status — 2026-09-13

Written at commit `c81730c` on `feat/checklist-administration`. Supersedes
`docs/2026-09-06-delivery-status.md` as the current status; that one still records how the
contact registry and web-role model were settled. The register of what is left remains
`docs/2026-09-04-outstanding-work.md`, read together with the "Still open" and "Left as they
are" sections of the deployment notes since.

Every environment claim below was verified by query against `Env_AQ_Dev` after the fact.
Where something did **not** land, it says so.

**The day in one line: the Tax team got its header edit and the case page got the paper
form, the audit that followed found that the sign-off loop closed on 2026-09-12 had been
left open in three other places, and an evening of testing on case 254398988 turned up
three more defects in the remediation page - one of which had been quietly discarding
regrades.**

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
| `c81730c` | Three reports on case 254398988, and the sweep for more like them (§7) | `2026-09-13-regrade-replay-key.md`, `-regrade-before-recheck.md`, `-remediation-confirms-in-place.md`, `docs/audits/2026-09-13-three-bug-classes-swept.md` |

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
| Plug-in assembly | **222,720 bytes** after the evening's two plug-in changes (221,696 at the audit's second pass) |
| Steps | 0 disabled after `registerall` |
| Web templates | OT Case Detail, OT Review Detail, OT Remediation pushed today; the case page draws remediation as the form. OT Remediation pushed three more times in the evening (93822 → 108070 chars) and OT Review Detail once (→ 99982) |
| Custom APIs | 31 registered from contracts; `DisplayOrder` on `al_RetireAndSucceedQuestion` created and in the solution |
| Code App | pushed after each pass |
| `src/` | manifest and assembly type list copied back from the export (AD-013); one hand-written parameter file matching the DEV row |

| Suite | |
|---|---|
| `dotnet test` (plugins) | **816** (804 at the audit's second pass; 12 added in the evening) |
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
- **The claim navigation** (§7): claiming a case sends the browser straight to the review
  page, whose editability depends on the assignment the plug-in has just written. Confirmed
  by reading, not reproduced, and the fix needs a decision first.
- **Case 254398988 carries a final outcome of Pass** recorded before AD-127's guard existed.
  Nothing is stuck - it now sits at Awaiting Recheck, and recording the final outcome
  overwrites it and closes the case.
- **Browser proof** of the three evening fixes. Each was verified against Dataverse and, for
  the page helper, against a fake DOM; none has been watched in a browser.
- ~~`origin/main` behind the branch~~ — **closed.** `main` was at `757d646` when this was
  written, and the project owner called the merge the same evening: `feat/checklist-administration`
  fast-forwarded onto it, so `main` now stands at `d6148e3` and carries both audit passes and
  the evening's work. The two branches are identical. Promotion to TEST or PROD is a separate
  decision and has not been made.
- Everything in `docs/2026-09-04-outstanding-work.md` not closed by a later note.

---

## 7. The evening: three reports on one case, and the sweep

Testing a remediation end to end on case **254398988** produced three reports in a row. Each
was a different defect, and in every one Dataverse already held what the tester thought had
been lost.

| Report | What was actually true | Defect | Recorded |
|---|---|---|---|
| "It shows saved but it does not save" | The regrade had saved in full | The **second** regrade was discarded: the derived replay key was the outcome id alone, so it matched the first Audit Event and returned its result while writing nothing | **AD-126** |
| "The buttons show even before it is saved; the form is still editable" | Nothing had ever been recorded on the remediation - no save had been submitted | The page offered **Record the final outcome** on a case at Awaiting Remediation with six unanswered actions, and took a Pass twice. No lifecycle guard anywhere | **AD-127** |
| "Supervisor sign-off does not show the updates" | Six actions Completed, six approvals, the case moved to Awaiting Recheck | The page reloaded into a render cache the write does not invalidate, and re-drew the old state under a success message | **AD-128** |

The third root explains the second report's lost typing: the regrade panel's reload discarded
the form the adviser had filled in but not yet saved.

### The sweep

Asked for after the third report. All three classes, across the codebase:

- **Reloads after a write** — three portal pages write. `OT Review Detail`'s Tax header edit
  had the same defect, reloading *immediately*, and is fixed. `OT Review List`'s claim
  navigates rather than reloads, and its target page decides editability from the row the
  claim just wrote: reported, not fixed, because the remedy needs a decision.
- **Derived replay keys** — six. One was the report; one (`portal-complete-…`) had already
  been fixed for this exact defect, which is what makes it a class; three are sound for
  reasons now written down; one is a timestamp that can never replay, harmless because the
  plug-in exits early when nothing changed. One latent risk, unreachable while a closed case
  cannot reopen.
- **Lifecycle guards** — every front-end-callable command has one. The regrade was the only
  gap, and each control is drawn under the condition its command enforces.

The audit also closed a risk in its own session's fix: AD-127's guard sits in the shared
`Regrade`, which the sign-off calls when a supervisor grades as they approve. That path was
safe only because `MoveCase` runs first, and nothing tested the two together. A test now
composes them.

Full findings: `docs/audits/2026-09-13-three-bug-classes-swept.md`.
