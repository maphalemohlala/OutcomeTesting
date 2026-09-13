# Audit fixes, and the case page's remediation drawn as the form — DEV deployment, 2026-09-13

Target: `Env_AQ_Dev` (`https://org0b075da8.crm11.dynamics.com/`), as
`svc.automate.aq@ascotlloyd.co.uk`. **DEV only.** TEST and PROD are untouched, by project
owner direction of 2026-09-12.

Project owner, 2026-09-13: "audit, fix, commit, push, deploy", and — with two screenshots —
the case details page on the portal still draws its remediation as a stacked list rather
than as the paper form the remediation page draws.

---

## Scope of the audit

Everything since the cleanup audit closed on 2026-09-10 (`50b154d`): 69 commits, 516 files,
about 28,700 lines added. Reviewed by six scoped finders against the range, each finding then
verified by hand against the current source before anything was changed. Five further
finders — line-by-line TypeScript, line-by-line portal and schema, cross-file tracing,
removed-behaviour and altitude — were cut off by the session's rate limit before reporting
in the first pass; they were re-run in the second pass, recorded at the end of this note.

Baselines before any change: **769** plug-in tests, **464** app tests, `tsc -b` clean, eight
lint warnings.

## Defects fixed

| | Where | What was wrong | What it does now |
|---|---|---|---|
| 1 | `SignoffProgressPlugin` | The replay guard was keyed on the **action**. Once `SignoffGuardPlugin.AlreadySettled` let a rejected action be decided again (2026-09-12), the approval that followed a portal rejection found the rejection's Audit Event under the same key and returned before `MoveCase`, `RecordFinalOutcome`, the audit line and the adviser letter ran — the IO-300003 deadlock moved one plug-in along | Keyed on the **sign-off row** (`ReplayKeyFor`). A platform retry carries the same row id, so a replay is still caught; a second decision is a second row |
| 2 | `RetireQuestionPlugin`, `RetireSectionPlugin` | A second retire overwrote the first effective-to with today (or later), bringing the row back into force for the gap and changing what every review submitted in between owed. The column was fetched and never read | `AlreadyRetiredRefusal`: a row already out of force is refused and names its date; a future-dated retirement may still be revised |
| 3 | `MoveQuestionPlugin` | The old version was dated out **today** while the new one started on the caller's `EffectiveFrom`, so a move announced ahead of time left the question asked in neither section for the interval; and a retired question could be moved, un-retiring it up to today | Old version ends on the day the new one starts (a clean handover under `IsVersionEffective`); a retired question is refused (`RetiredRefusal`) |
| 4 | `UpdateSectionPlugin` | Handing a section to the other team was not guarded, although it takes the section's questions off that team's form and submit gate exactly as retiring it would. Moving the Tax check section to AQS would have made every Tax submit fail on "outcome not recorded" | `OwnerRoleChangeNeedsGuard`: any move to a role other than Both is checked against the protected codes, with its own consequence sentence |
| 5 | `CaseHeaderRequestPlugin` | The two option values from the portal payload were written unvalidated. A swapped or mistyped integer landed on the case, `DeriveRoute` silently did nothing with it, and the header drew blank under an audit line naming a change | `UnknownOptionRefusal` pins the four values the two columns carry; anything else is refused as VALIDATION |
| 6 | `verifytaxheader` (registration tool) | The harness restored three columns but the plug-in also runs `RequeueAfterRouteChange`, which from Assigned or Review In Progress returns the case to the queue and releases its assignment — neither restored nor reported | Refuses a case in either state; the restore cannot put an assignment back |
| 7 | `OT Case Detail` | Two remediation cells read `ownerid.name` for the supervisor, which is "# PowerPages Data Runtime PROD" on every portal sign-off | Reads `al_signedbyname`, as OT Remediation does since the signatory fix |

## Duplication and cost removed

- **`ChecklistQueries`** (new) — `EnsureQuestionCodeIsFree` was written three times,
  `CurrentVersion` three times with three different column sets, and the in-force checklist
  version twice. One home each. AddSection now checks every code in **one** `In` query rather
  than one per question; MoveQuestion reads the version once rather than twice.
- **`WebRoleRegistry.HasRole`** — the same loop over `RolesForContact` sat in four portal
  gates (claim, regrade, sign-off, Tax header edit).
- **`SectionRules.IsAssignableOwnerRole`** and its refusal — one predicate for AddSection and
  UpdateSection instead of two that could drift.
- **`UpdateSectionPlugin.DescribeChanges`** now writes the owner role's **label** through
  `OptionLabels`, so the history screen reads "'Tax team' -> 'Both'" rather than two numbers
  (FR-033). Tests without metadata still get the number.
- **`CaseHeaderTable`** (app) — one component behind the case page and the review page,
  which each built the same eighteen pairs; they had already diverged on how an empty value
  reads.
- **Dead code**: the `CaseField` type and its re-export, five `.case-detail__field*` rules,
  four `.library__edit*` rules, `.ot-remedial-settled`, and the `libraryStatus` shim (its
  tests moved onto `lib/effectiveWindow`, which had none of its own).
- **`QuestionLibraryPage`** — one render path for live and retired sections; the retired block
  no longer takes five no-op handlers.
- **`xlsx.ts`** — one compiled pattern per tag and attribute name, where a 60-column,
  1,000-row extract compiled a few hundred thousand `RegExp` objects on the UI thread.
- **`OT Review Detail`** — an added section's grid shape is derived once per section, not
  once per question row (K×Q iterations → Q).
- The eight `react-hooks/exhaustive-deps` warnings: the ready-state projections are memoised.

## The case page's remediation, as the form

`OT Case Detail` drew a six-column table and then a definition list. It now draws the block
OT Remediation draws, read-only: the eight-column table with Status and Age, a group heading
per check that says when a settled check was approved, the four answers as the last rows of
the same grid, the "Closed: …" line, the regraded-outcome grid with the Tax-only "not
applicable" wording, and the two sign-offs with the signatory. The fetches gained
`al_reviewinstanceid`, `al_clockstartedon` and `al_signedbyname`; the working-day age script
is carried into the template so the Age column resolves. Nothing about who may write what
changes; this page reports what the remediation page recorded.

Checked before the push: Liquid 354 opens against 354 closes, 62/62 `if`, 17/17 `for`, 5/5
`unless`, 7/7 `capture`, 19/19 `comment`, nesting walked, no tag delimiters inside any
comment, and `section`/`table`/`tbody`/`thead`/`tr`/`script`/`div` all balanced. The review
template: 453/453, nesting walked, all balanced.

## What ran

| | Step | Result |
|---|---|---|
| 1 | `dotnet test` (plugins) | **786 passed** (17 new) |
| 2 | `npx vitest run`, `npx tsc -b`, `npx eslint src` | **480 passed**, clean, **0 warnings** |
| 3 | `dotnet build -c Release` → `pushassembly` | **refused**: `0x8004418B PluginType [CorrectTaxOutcomePlugin] not found in PluginAssembly` — see below |
| 4 | `pushwebtemplate … OT Case Detail` | `a1000000-…-015`, **33525 → 47009 chars** |
| 5 | `pushwebtemplate … OT Review Detail` | `a1000000-…-01b`, **94297 → 94978 chars** |
| 6 | `npm run build` → `npx pa app push` | built clean, **pushed successfully** |
| 7 | `deleteplugintype … CorrectTaxOutcomePlugin --confirm` | custom API `al_CorrectTaxOutcome` (5 request parameters, 4 response properties) and its implementation step removed, type deleted, verified absent |
| 8 | `pushassembly` | **221,184 bytes**, matching the local Release build, modified 13:07:54Z |

**The orphaned plug-in type.** DEV carried `CorrectTaxOutcomePlugin` with a custom API
`al_CorrectTaxOutcome` bound to it. Neither exists in any commit — the class was built and
registered from a working tree during the OD-053 work and deleted once OD-053 settled that a
Tax outcome is corrected by no command, without ever being committed. One such orphan
refuses every assembly push for the whole solution. `deleteplugintype` is new in this change
and follows `deploy/Remove-OrphanedPluginTypes.ps1`'s order (parameters and properties, the
custom API which cascades its step, any standing step, the type), refuses a type whose `.cs`
still exists, and verifies by re-query.

**Not pushed:** `outcome-testing.css`. Its only change is the removal of a rule nothing
used, which is invisible; pushing it would mean bumping the stylesheet version in OT Layout
for no visible effect. Source and DEV differ by that one dead rule until the next stylesheet
change.

## Still open

- **Not seen in a browser.** The case page's remediation block is balanced and pushed, but
  no signed-in user has opened
  `/case-details/?id=af352f30-50af-f111-aaac-e4fade069307` since the push. That case is the
  one in the report, so it is the proof.
- **The checklist-version window** counts the effective-to day as in force
  (`ChecklistQueries.ChecklistVersionInForce`, `>= today`), where a question version and a
  section are out of force from the start of that day. Both former copies did this; it is now
  one place and documented, not changed.
- **`SignoffProgressPlugin`** re-reads the case status and the outcome that `MoveCase` has
  just read; `useQuestionLibrary` reloads all three tables after any single command. Both
  are real costs and neither was changed here — each is a design change with its own tests.
- **`graphify update .`** exits with "No code files found": no graph exists for this repo,
  so the instruction in the user-level CLAUDE.md that presumes one cannot be followed until
  a full `graphify .` has been run once.

---

## Second pass, same day: the five angles that were cut off

Project owner, 2026-09-13: complete the five review angles the rate limit stopped. Re-run
over `50b154d..HEAD` (now including the first pass's commit), each finding verified against
current source before anything was changed.

### Defects fixed

| | Where | What was wrong | What it does now |
|---|---|---|---|
| 8 | `SignoffProgressPlugin.AnyAwaitingSignoff` | Counted **any** sign-off row as the decision, "because the guard refuses a second one" — which stopped being true on 2026-09-12 when `AlreadySettled` let a rejected action be decided again. A reworked action still carrying its rejection row read as decided, so approving the check's other actions moved the case on (or regraded and closed it) with that action never approved | Only an **approved** sign-off decides. Recorded as **AD-125**, which amends AD-114(a) |
| 9 | `OT Remediation` decision panel | Offered the panel only for completed actions with **no** sign-off row, so the reworked action never came back to the supervisor — the IO-300003 deadlock on the page | Offered unless an **approved** sign-off exists |
| 10 | `CaseHeaderRequestPlugin` | Audited the portal header edit under `al_command` **120910752**, which is `ReturnCase`; the command path uses 120910778 `UpdateCaseDetails`. The history screen labelled every portal header edit a return, and `verifytaxheader` queried the same wrong value so its "new audit event" check passed on it | Takes the value from `UpdateCaseDetailsPlugin`; pinned by a test; the harness queries 120910778. The proof-run rows already written under ReturnCase are immutable and stay |
| 11 | `OT Review Detail` header selects | The "—" option encoded as 0, which the plug-in reads as "not sent", so a clear reported Saved and changed nothing | The dash is a disabled placeholder; clearing a recorded answer is a case correction through `al_UpdateCaseDetails` |
| 12 | `powerpages/…/sitesetting.yml` | `Webapi/contact/fields` in portal source still allowlisted three columns; DEV and `src/` carry four. A site uploaded from source would refuse every header-edit PATCH | Four columns, description updated |
| 13 | `OutcomeIndicator.tsx` | `TAX_SHAPE` keyed the Tax scale's third grade as "Insufficient evidence", but the label is "Pass with issues" since the AD-055 amendment — the one grade that sends a case to remediation drew with no mark | Keyed on the label the generated model carries |
| 14 | `CaseWorklistPage` | The "Not yet graded" filter tested only the BR-005 outcome, so a Tax-graded case was listed as ungraded beneath a cell reading "Tax: Pass" | A Tax grade counts as graded |
| 15 | `questionModalIntent.refusalFor` | Demanded wording on a move, where the control is disabled and the wording is carried forward, so a cleared draft could not be moved | Not asked for on a move |
| 16 | `al_RetireAndSucceedQuestion` | The `DisplayOrder` request parameter existed in the contract JSON, the plug-in and the app, but **not in DEV** and not in `src/` — a reorder from the Question library would have been refused by the Web API | `registerall` created it; `addcomponent` put it in the solution; its solution file is in `src/` |

### Altitude and coverage

- `SignoffGuardPlugin.DecisionRefusal` — the three final-outcome rules (a rejection carries no
  grade; the grade is on the BR-005 final scale; a grade needs notes) were decided inline in
  Execute and had no test. Now a static with five.
- `Remediation.CouldBeNonPass` is derived from `IsNonPassAnswer` over the remediable scales
  rather than restated by hand.
- `ImportParserTests` (new) — the CSV and date primitives the IO extract rewrite left
  implemented but untested: quoted commas, doubled quotes, day-first dates, month-first and
  out-of-range refusal, cell quoting, JSON escaping.
- `caseUpload.test.ts` regains the month-first, written-out and ISO-timestamp date cases.

### src/ copy-back (AD-013)

The DEV export differed from `src/` in exactly what the tracer said: the seven plug-in types
registered since 9f4e968 were missing from the assembly's type list, and the contact step
`{c3013097-…}` was missing from `Solution.xml`'s root components. Both files were copied back
from the export. Every other file under `customapis/` and `SdkMessageProcessingSteps/` was
identical apart from line endings.

### What ran

| | Step | Result |
|---|---|---|
| 9 | `dotnet test` (plugins) | **804 passed** (18 new) |
| 10 | `npx vitest run`, `npx tsc -b`, `npx eslint src` | **484 passed**, clean, clean |
| 11 | `pushwebtemplate … OT Remediation` | `a1000000-…-019`, **93176 → 93822 chars** |
| 12 | `pushwebtemplate … OT Review Detail` | `a1000000-…-01b`, **94978 → 95510 chars** |
| 13 | `dotnet build -c Release` → `registerall` | assembly updated (**221,696 bytes**), 31 commands upserted, `DisplayOrder` present on `al_RetireAndSucceedQuestion` (verified by query), **0 disabled steps** afterwards |
| 14 | `addcomponent … 10039 95f6c041-8fae-f111-aaac-e4fade069307` | `DisplayOrder` added to the solution, verified by query. `addcomponent` is new: `registerall` upserts a contract's parameters into the default solution, and the tool had no verb to add one component. The tool's own component-type table said 372 for a request parameter; this environment says 372 is a connector and a request parameter is **10039** (`solutioncomponentdefinition`), so the table is corrected too |
| 15 | `npm run build` → `npx pa app push` | built clean, **pushed successfully** |
| 16 | `pac solution export` → `unpack` → scoped copy-back | `Solution.xml`, the assembly type list |

### Left as they are, deliberately

- **Altitude**: `ProtectedCodeIn` stays on `RetireSectionPlugin` and the two window refusals
  (`AlreadyRetiredRefusal`, `RetiredRefusal`) stay on their plug-ins; `CaseHeaderTable` keeps
  its variant flag; the working-day script is inlined in both portal templates. Each is a
  fair point about placement and none changes behaviour; moving them is a refactor for a
  quieter day, not a fix.
- `ChecklistVersionInForce` still counts the effective-to day as in force, as before.
- Not seen in a browser: the case page's remediation block and the reworked-action sign-off
  panel. The case in the report is the proof for the first; a rejected-then-reworked action
  is the proof for the second.
