# Remediation documents, para-planner letter and an open check form - implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:**
- Advisers and para-planners receive the check PDFs, and the PDFs include each check's
  remedial actions.
- The portal review page and the Code App case page show the remedial actions.
- The check form no longer greys out, pre-ticks or refuses any answer.

**Architecture:**
- **Emails:** every adviser letter is queued through
  `NotificationOutbox.QueueWithCompletedCheck`, which attaches `CompletedCheckPdf.Documents`.
- **Para-planner letter:** a fixed letter with its own outbox occurrence, queued beside the
  adviser's remediation letter.
- **Remedial actions in the check PDF:** a new `CheckRemedialActions` block builder appends
  them to `CompletedCheck.Blocks`.
- **Fresh attachments:** `Remediation.Raise` refreshes the attachments on pending remediation
  rows once every action exists.
- **Portal:** gains a read-only remedial section on submitted reviews, and loses every
  automatic lock.
- **Server:** loses the matching refusals and rewrites.
- **Code App:** a gated panel on the case page.

**Tech Stack:**
- C# Dataverse plug-ins, net462, tested with xUnit and `FakeOrganizationService`.
- Power Pages Liquid and inline JavaScript, tested with vitest reading the template as text.
- React Code App, tested with vitest and `renderToStaticMarkup`.

**Spec:** `docs/superpowers/specs/2026-10-05-remediation-documents-and-open-form-design.md`

## Global Constraints

- Deploy to **DEV only** (`https://org0b075da8.crm11.dynamics.com/`). Never TEST or PROD in this plan.
- Plug-in tests: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`.
- App type check: `npx tsc -b` in `app/`. `tsc --noEmit` checks nothing there.
- App tests: `npx vitest run <file>` in `app/`.
- User-facing text never cites decision codes (AD-, OD-, BR-, FR-, PP-). Describe the rule in plain words.
- People are identified by email, never by name. Para-planner address: `NotificationOutbox.ParaplannerEmail`.
- Building a document never costs the letter. Attachment code never throws into the
  transaction that queued the letter.
- The para-planner letter is **not editable**. It is not in `NotificationTemplates` and is
  queued with `templateCode: null`.
- Portal Liquid on this site rejects two-argument `truncate`, multi-character `split` and
  `entities[0]`. It treats `null != ''` as true, so compare through `default: ''`.
- Push the portal one template at a time with the registration tool's `pushwebtemplate`.
  Never use `pac powerpages upload`, because `powerpages/` is hand-authored, not a mirror.
- `pushassembly` uploads `bin/Release` without building it, and `npx pa app push` uploads
  `app/dist` without building it. Build first, then check the byte count or bundle hash.
- Commit only the files the task names. The working tree holds unrelated uncommitted changes
  (the email-identity deployment doc, `AppPermissionGateTests.cs`, the App Admin role XML,
  `.docx` files and `test-results/`). Never stage them.
- End every commit message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- After code changes, run `graphify update .` from the repo root.

## Review Focus

1. **A deferred Tax fail raised at the AQS submit.** Its actions are linked to the AQS review,
   so they appear in the AQS check's PDF and portal section, not in the Tax check's. Task 3
   pins that the Tax PDF shows no section when it raised none.
2. **A case with no para-planner email.** The para-planner row is still queued, with no
   recipient. The drain later marks it Failed with a reason. No exception, and the adviser's
   letter is unaffected. Pinned in Task 5.
3. **A replayed submit or reassignment.** The para-planner row must not be queued twice. It has
   its own code, distinct from the adviser's. Pinned in Task 5.
4. **A row already Sent when the actions are raised again.** The refresh must never rewrite a
   sent row's attachments. Pinned in Task 5.
5. **Contradicting answers.** For example, grade Pass while a point reads Fail. They must save
   and submit with no error, and remediation is still raised only by
   `OutcomeRules.RequiresRemediation`. Pinned by the source contract in Task 1.

---

### Task 1: The server stops refusing or rewriting answers

**Files:**
- Create: `plugins/OutcomeTesting.Plugins.Tests/OpenCheckFormTests.cs`
- Modify: `plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs` (calls at lines 125-127; methods `EnsureOutcomeAgreesWithForm` ~177 and `EnsureRemedialAgreesWithOutcome` ~277)
- Modify: `plugins/OutcomeTesting.Plugins/ResponseProgressPlugin.cs` (calls at lines 73-74; methods `ClearOutcomesContradictedByAnswer` ~226, `ClearFileQuality` ~282, `ReconcileRemedialAction` ~333, `Move` ~376)
- Modify: `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs:902-921` and `:950-957`
- Modify: `plugins/OutcomeTesting.Plugins/ChecklistGating.cs`, `plugins/OutcomeTesting.Plugins/ChecklistQueries.cs`
- Modify: `plugins/OutcomeTesting.Plugins.Tests/ChecklistGatingTests.cs` (and any test of a deleted member)
- Modify: `knowledge/decision-log.md` (append AD-231)

**Interfaces:**
- Consumes: nothing.
- Produces: no plug-in references `GradeRefusal`, `FileQualityRefusal`, `RemedialActionRefusal`, `RemedialActionDefault`, `GradeCleared`, `FailPointsLocked` or `ReadGatingFacts`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The check form decides nothing for the checker (project owner, 2026-10-05: "Remove any
    /// parts of the form that are automatically greyed out or selected by default", and
    /// "allow any answer"). No test drives the three plug-ins end to end over a full
    /// checklist, so this reads their source, the way AssignCaseIsScopedTests does.
    /// </summary>
    public class OpenCheckFormTests
    {
        private static readonly string[] Rules =
        {
            "GradeRefusal(",
            "FileQualityRefusal(",
            "RemedialActionRefusal(",
            "RemedialActionDefault(",
            "GradeCleared(",
            "FailPointsLocked(",
            "ReadGatingFacts(",
            "ClearOutcomesContradictedByAnswer(",
            "ReconcileRemedialAction(",
        };

        [Theory]
        [InlineData("ResponseGuardPlugin.cs")]
        [InlineData("ResponseProgressPlugin.cs")]
        [InlineData("SubmitReviewPlugin.cs")]
        public void No_plugin_refuses_or_rewrites_an_answer_for_contradicting_the_form(string file)
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), file));

            foreach (var rule in Rules)
            {
                Assert.False(source.Contains(rule), file + " still calls " + rule);
            }
        }

        [Fact]
        public void Remediation_is_still_decided_by_the_grade_or_the_flag()
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), "SubmitReviewPlugin.cs"));

            Assert.Contains("OutcomeRules.RequiresRemediation(outcomeValue, remedialFlagged)", source);
            Assert.Contains("RemedialActions.EnsureWritten(", source);
        }

        [Fact]
        public void The_root_cause_is_still_cleared_on_a_pass()
        {
            var source = File.ReadAllText(Path.Combine(PluginsPath(), "ResponseProgressPlugin.cs"));

            Assert.Contains("ClearRootCauseOnPass(service, target, pre, reviewRef.Id);", source);
        }

        private static string PluginsPath()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "OutcomeTesting.Plugins")))
            {
                directory = directory.Parent;
            }

            return Path.Combine(directory.FullName, "OutcomeTesting.Plugins");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~OpenCheckFormTests`
Expected: FAIL. The three theory cases report a rule still being called, and the two facts pass.

- [ ] **Step 3: Remove the call sites**

**`ResponseGuardPlugin.cs`:**
- Delete the two calls at lines 125-128 (`EnsureOutcomeAgreesWithForm(...)` and `EnsureRemedialAgreesWithOutcome(...)`).
- Delete both methods, with their doc comments (from the `/// <summary>` above line ~160 to the end of `EnsureRemedialAgreesWithOutcome`, ~line 328).
- Leave `EnsureNotSubmitted`, `EnsureSectionBelongsToReview` and `EnsureAnswerShape` as they are.

**`ResponseProgressPlugin.cs`:**
- Delete the two calls at lines 73-74.
- Delete `ClearOutcomesContradictedByAnswer`, `ClearFileQuality`, `ReconcileRemedialAction` and `Move`, with their doc comments.
- Keep `ClearRootCauseOnPass`.
- Fix the class summary (lines ~16-22). Drop the sentence about an Insufficient evidence answer clearing a grade, and add:
  `/// Since 2026-10-05 it no longer clears or fills an outcome the rest of the form contradicts: the checker's answer stands.`

**`SubmitReviewPlugin.cs`:**
- Delete the block from the comment `// What the rest of the form says, read once for both refusals below` (line ~902) through the `fileQualityRefusal` throw (line ~921).
- Delete the comment and block at lines ~950-957 that compute and throw `gradeRefusal`.
- Keep `TryGradeFromAnswer`, `RequiresRemediation`, `EnsureWritten` and everything after them.

- [ ] **Step 4: Delete what is now unused**

Run: `rg -n "GradeRefusal|GradeCleared|FileQualityRefusal|FailPointsLocked|RemedialActionDefault|RemedialActionRefusal|ReadGatingFacts|GatingFacts|IsAmlCraClean|IsNoOrFail|IsInsufficient" plugins --glob "*.cs"`

For each member of `ChecklistGating` and `ChecklistQueries` that now has no caller outside its own file and its own tests, delete the member and the tests that pin it. Expect these to go: `GradeRefusal`, `GradeCleared`, `FileQualityRefusal`, `FailPointsLocked`, `RemedialActionDefault`, `RemedialActionRefusal`, `ChecklistQueries.ReadGatingFacts` and its `GatingFacts` type.

Keep the code constants (`RemedialActionQuestionCode`, `TaxRemedialActionQuestionCode`, `TaxCheckOutcomeQuestionCode`, `AmlCraSectionCode`) and `IsOutcomeQuestion`, `IsNoOrFail`, `IsInsufficient` and `IsAmlCraClean` while anything still references them. Re-run the `rg` after each deletion.

Replace the class summary of `ChecklistGating` with:

```csharp
    /// <summary>
    /// The checklist's outcome questions, by code. Until 2026-10-05 this also held the rules
    /// that took options off the grade and the file quality outcome and decided "Remedial
    /// action required?" from the outcome; the project owner withdrew them ("allow any
    /// answer"), so the checker's choice stands and remediation follows the grade or the flag
    /// alone (OutcomeRules.RequiresRemediation).
    /// </summary>
```

- [ ] **Step 5: Build and run the whole plug-in suite**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: PASS. Any failure names a test of a deleted member. Delete that test only if it pinned the withdrawn rule. A test of anything else failing means you removed too much, so restore it.

- [ ] **Step 6: Record the decision**

Append to the table in `knowledge/decision-log.md`, after the AD-230 row:

```markdown
| AD-231 | **The check form decides nothing for the checker.** Nothing on OT Review Detail is greyed out or ticked automatically: Pass and Pass with issues stay on the grade, Pass stays on the file quality outcome, "Remedial action required?" is the checker's to answer, and the fail-point boxes stay open. The server matches: `ResponseGuardPlugin` no longer refuses a grade, file quality outcome or remedial answer the rest of the form contradicts, `ResponseProgressPlugin` no longer clears or fills them, and `SubmitReviewPlugin` no longer refuses them at submit. Remediation is still raised by `OutcomeRules.RequiresRemediation` - a grade other than Pass, or "Remedial action required?" Yes - and the remedial action text is still required for every fail point when it is owed. A Pass with "Remedial action required? No" closes with no remediation even where a point reads Fail. The root cause is still hidden and cleared on a Pass. | Supersedes the gating of 2026-09-19 (item 10), 2026-09-22 and 2026-09-23. Project owner, 2026-10-05: "Remove any parts of the form that are automatically greyed out or selected by default"; leave them open for users to select; allow any answer. | 2026-10-05 |
```

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins.Tests/OpenCheckFormTests.cs plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs plugins/OutcomeTesting.Plugins/ResponseProgressPlugin.cs plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs plugins/OutcomeTesting.Plugins/ChecklistGating.cs plugins/OutcomeTesting.Plugins/ChecklistQueries.cs plugins/OutcomeTesting.Plugins.Tests/ChecklistGatingTests.cs knowledge/decision-log.md
git commit -m "feat(checklist): the server no longer refuses or rewrites contradicting answers (AD-231)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
(Add any other test file Step 5 changed.)

---

### Task 2: The portal form greys out and pre-ticks nothing

**Files:**
- Create: `app/src/features/reviews/portalOpenForm.test.ts`
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`
  - Liquid pre-pass ~1090-1125 (`form_insufficient`, `form_nofail`, `form_fq_outcome`).
  - `locked_values` block ~1745-1777.
  - Script ~2730-3110: `findings`, `lockOptions`, `syncFailPoints`, `syncGradeOptions`, `impliedRemedial` and its sync.
- Modify: `app/src/features/reviews/portalGating.test.ts`

**Interfaces:**
- Consumes: nothing (Task 1 is the server half, but the two are independent).
- Produces: a template with no `locked_values`, `lockOptions`, `syncGradeOptions`, `syncFailPoints` or `impliedRemedial`.

- [ ] **Step 1: Write the failing test**

`app/src/features/reviews/portalOpenForm.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * The check form decides nothing for the checker (project owner, 2026-10-05: "Remove any parts
 * of the form that are automatically greyed out or selected by default" - leave them open,
 * allow any answer). Read as text: the template is Liquid and script nothing compiles.
 */
describe('the check form decides nothing for the checker', () => {
  it('renders no option locked by the server', () => {
    expect(reviewTemplate).not.toContain('locked_values');
    expect(reviewTemplate).not.toContain('form_insufficient');
    expect(reviewTemplate).not.toContain('form_nofail');
    expect(reviewTemplate).not.toContain('form_fq_outcome');
  });

  it('locks, unticks and pre-ticks nothing from the script', () => {
    for (const name of ['lockOptions', 'syncGradeOptions', 'syncFailPoints', 'impliedRemedial']) {
      expect(reviewTemplate, name).not.toContain(name);
    }
  });

  it('no longer tells the checker an answer was cleared', () => {
    expect(reviewTemplate).not.toContain('Grade cleared');
    expect(reviewTemplate).not.toContain('File quality outcome cleared');
    expect(reviewTemplate).not.toContain('so there are no fail points to record');
  });

  it('still hides the root cause on a Pass, which is hiding and not greying out', () => {
    expect(reviewTemplate).toContain('var syncRootCause = function () {');
  });

  it('still disables every control once the review is submitted', () => {
    expect(reviewTemplate).toContain('{% unless editable %} disabled{% endunless %}');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (in `app/`): `npx vitest run src/features/reviews/portalOpenForm.test.ts`
Expected: FAIL on the first three tests.

- [ ] **Step 3: Remove the Liquid locks**

In `OT-Review-Detail.webtemplate.source.html`:
- Delete the pre-pass that assigns `form_insufficient`, `form_nofail` and `form_fq_outcome`. It starts at `{% assign form_insufficient = false %}` (~line 1096), runs through its `{% endfor %}`, and includes the comment above it that explains it.
  - Before deleting, search the file for every variable the pre-pass assigns (e.g. `scan_code`, `scan_value`, `fq_outcome_codes`). Delete only those used nowhere else.
- Delete the `{% assign locked_values = '' %}` block and all its `if`/`elsif` branches (~1745-1777), with its comment.
- Remove ` locked: locked_values` from the `{% include 'OT Answer Options' ... %}` call on ~1778. Leave `OT Answer Options` itself unchanged: `locked` is an optional parameter.

- [ ] **Step 4: Remove the script locks**

Read lines ~2700-3120 first. Delete:
- `var findings = function ...` and every helper only it uses, for example `isOutcomeQuestion` and the AML/CRA counters. Check each with a search before deleting.
- `var lockOptions = function ...`.
- `var syncFailPoints = function ...`, plus the `failPointsBlock` and `failPointsNote` lookups if nothing else uses them.
- `var syncGradeOptions = function ...`.
- `var impliedRemedial = function ...`, the function that applies it to `remedialRow` (the one calling `lockOptions(remedialRow, ...)`), and its `userDriven` handling.
- Every `addEventListener` and load-time call that invokes the deleted functions.
- The call `window.otRemedial.redraw()` that sat inside `syncGradeOptions`. The remedial card already redraws on `change` (see `document.addEventListener('change', ...)` in the card script ~3944).

Keep `syncRootCause`, `gradeChosen`, `gradeRow`, `rootCause` and their listeners.

Then search the file for `gradeStatus`, `say(`, `GRADE_PASS`, `INSUFFICIENT`, `NO`, `YES` and `FAIL`. Delete any declaration left with no use.

- [ ] **Step 5: Retire the portal gating tests**

In `app/src/features/reviews/portalGating.test.ts`:
- Delete every `describe` block from `'the outcome questions are excluded from the scan'` through the `'the remedial action lock'` block. These pin the withdrawn rules.
- Keep `'the rows carry what the rules are keyed on'` and `'the case header stays editable after the Tax check is submitted'`.
- Replace the file's doc comment with:

```ts
/**
 * The answer rows still carry their question and section codes, and the case header stays
 * editable after the Tax check. The gating these tests used to pin was withdrawn on
 * 2026-10-05; portalOpenForm.test.ts pins that it is gone.
 */
```

If a kept test fails because its rows' codes were only written for the gating, keep the codes in the template: the remedial card's mirror and other scripts read `data-ot-code`.

- [ ] **Step 6: Run the portal tests**

Run (in `app/`): `npx vitest run src/features/reviews`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html app/src/features/reviews/portalOpenForm.test.ts app/src/features/reviews/portalGating.test.ts
git commit -m "feat(portal): the check form greys out and pre-ticks nothing (AD-231)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: The check PDF ends with that check's remedial actions

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/CheckRemedialActions.cs`
- Modify: `plugins/OutcomeTesting.Plugins/RemediationDocument.cs:395, :513, :518` (`Issues`, `Text`, `Day` from `private` to `internal`)
- Modify: `plugins/OutcomeTesting.Plugins/CompletedCheck.cs:120-121` and its class summary (lines ~18-24)
- Test: `plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs`

**Interfaces:**
- Consumes: `RemediationDocument.Issues(string)`, `RemediationDocument.Text(string)`, `RemediationDocument.Day(DateTime?)` (made internal here).
- Produces: `public static class CheckRemedialActions { public const string Heading = "Remedial actions"; public static List<PdfBlock> Blocks(IOrganizationService service, Guid reviewId); }`. `CompletedCheck.Blocks` appends it last.

- [ ] **Step 1: Write the failing tests**

Add to `CompletedCheckDocumentTests.cs`, under a new `// ================================================================== remedial actions` divider before `// ================================================================== the files`:

```csharp
        [Fact]
        public void A_check_that_raised_remediation_ends_with_its_remedial_actions()
        {
            var service = Case();
            var action = Action(service, completed: false);
            action[RemedialActions.ActionAttr] = "Re-verify ID and file the evidence.";

            var blocks = CompletedCheck.Blocks(service, AqsReviewId);

            Assert.Equal(CheckRemedialActions.Heading, blocks.Last(b => b.Kind == PdfBlockKind.Subheading).Text);

            var table = Tables(blocks).Last();
            Assert.Equal(
                new[] { "No.", "Fail point", "Remedial action", "Owner", "Target date", "Status" },
                table.Rows[0].Cells.Select(c => c.Text).ToArray());

            Assert.Equal(3, table.Rows.Count);
            Assert.Equal("1", table.Rows[1].Cells[0].Text);
            Assert.Equal("ID verification completed and retained for all relevant clients/parties.: No", table.Rows[1].Cells[1].Text);
            Assert.Equal("Re-verify ID and file the evidence.", table.Rows[1].Cells[2].Text);
            Assert.Equal("Adam Strumidlo", table.Rows[1].Cells[3].Text);
            Assert.Equal("05 Oct 2026", table.Rows[1].Cells[4].Text);

            Assert.Equal("2", table.Rows[2].Cells[0].Text);
            Assert.Equal("Client objectives clearly evidenced and specific: Fail", table.Rows[2].Cells[1].Text);
            Assert.Equal(string.Empty, table.Rows[2].Cells[2].Text);
        }

        [Fact]
        public void A_check_that_raised_nothing_has_no_remedial_section()
        {
            var service = Case();
            Action(service, completed: false);

            // The action belongs to the AQS check, so the Tax check's document carries none.
            var blocks = CompletedCheck.Blocks(service, TaxReviewId);

            Assert.DoesNotContain(blocks, b => b.Kind == PdfBlockKind.Subheading && b.Text == CheckRemedialActions.Heading);
        }

        [Fact]
        public void A_row_raised_before_the_checkers_words_shows_the_advisers_own()
        {
            var service = Case();
            Action(service, completed: true);

            var table = Tables(CompletedCheck.Blocks(service, AqsReviewId)).Last();

            Assert.Equal("ID re-verified and retained on file.", table.Rows[1].Cells[2].Text);
        }
```

Check `PdfCell.Blank()` text: if `Blank()` renders `Text` as something other than `string.Empty`, adjust the `Rows[2].Cells[2]` assertion to that value after reading `PdfWriter.cs:199`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CompletedCheckDocumentTests`
Expected: FAIL to compile. `CheckRemedialActions` does not exist.

- [ ] **Step 3: Write the implementation**

In `RemediationDocument.cs`, change `private static List<string> Issues(`, `private static string Text(` and `private static string Day(` to `internal static`.

Create `plugins/OutcomeTesting.Plugins/CheckRemedialActions.cs`:

```csharp
using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The remedial actions one check raised, as the last section of that check's document
    /// (project owner, 2026-10-05: add the remediation actions to the PDF downloaded from the
    /// portal and to the one that goes out to advisers and para-planners).
    ///
    /// <para>
    /// Only this check's actions: those whose <c>al_reviewinstanceid</c> is the review. A Tax
    /// fail deferred to the AQS submit is raised against the AQS review, so it is drawn under
    /// the AQS check. The case's whole Remediation and escalation form stays its own document
    /// (<see cref="RemediationDocument"/>).
    /// </para>
    /// <para>Never throws, for the reason every attachment gives: the letter matters more.</para>
    /// </summary>
    public static class CheckRemedialActions
    {
        /// <summary>The section's heading, on the page and in the document.</summary>
        public const string Heading = "Remedial actions";

        /// <summary>The section, or an empty list when the check raised nothing or cannot be read.</summary>
        public static List<PdfBlock> Blocks(IOrganizationService service, Guid reviewId)
        {
            var blocks = new List<PdfBlock>();

            List<Entity> actions;
            try
            {
                actions = Actions(service, reviewId);
            }
            catch (Exception)
            {
                return blocks;
            }

            if (actions.Count == 0)
            {
                return blocks;
            }

            var labels = new OptionLabels(service);
            var table = new PdfTable(0.05, 0.3, 0.3, 0.13, 0.11, 0.11);
            table.AddHeader(
                PdfCell.Head("No."), PdfCell.Head("Fail point"), PdfCell.Head("Remedial action"),
                PdfCell.Head("Owner"), PdfCell.Head("Target date"), PdfCell.Head("Status"));

            var number = 0;
            foreach (var action in actions)
            {
                var checkerAction = action.GetAttributeValue<string>(RemedialActions.ActionAttr);
                var adviserText = action.GetAttributeValue<string>("al_adviserresponse");
                var owner = action.GetAttributeValue<EntityReference>("al_assignedcontactid");
                var status = action.GetAttributeValue<OptionSetValue>("al_actionstatus");

                // The checker's words where the row has them (2026-09-29); a row raised before
                // that carries the adviser's own remedial action, as RemediationDocument shows.
                var details = new[]
                {
                    RemediationDocument.Text(string.IsNullOrWhiteSpace(checkerAction) ? adviserText : checkerAction),
                    owner != null && !string.IsNullOrWhiteSpace(owner.Name) ? owner.Name : "Nobody assigned",
                    RemediationDocument.Day(action.GetAttributeValue<DateTime?>("al_duedate")),
                    status == null ? "—" : labels.Label("al_remediationaction", "al_actionstatus", status.Value),
                };

                var issues = RemediationDocument.Issues(action.GetAttributeValue<string>("al_description"));
                if (issues.Count == 0)
                {
                    issues.Add("—");
                }

                for (var i = 0; i < issues.Count; i++)
                {
                    number++;
                    var cells = new List<PdfCell>
                    {
                        PdfCell.Of(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        PdfCell.Of(issues[i]),
                    };

                    // The action's own columns sit on its first issue; the rows under it
                    // belong to the same action, as on the remediation form.
                    foreach (var detail in details)
                    {
                        cells.Add(i == 0 ? PdfCell.Of(detail) : PdfCell.Blank());
                    }

                    table.Add(cells.ToArray());
                }
            }

            blocks.Add(PdfBlock.Subheading(Heading));
            blocks.Add(PdfBlock.Table(table));
            return blocks;
        }

        /// <summary>The review's live actions, in the order they were raised.</summary>
        private static List<Entity> Actions(IOrganizationService service, Guid reviewId)
        {
            var query = new QueryExpression("al_remediationaction")
            {
                ColumnSet = new ColumnSet(
                    "al_description", "al_actionstatus", "al_duedate", "al_adviserresponse",
                    "al_assignedcontactid", "createdon", "al_remediationactioncode",
                    RemedialActions.ActionAttr),
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_reviewinstanceid", ConditionOperator.Equal, reviewId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            query.AddOrder("createdon", OrderType.Ascending);
            query.AddOrder("al_remediationactioncode", OrderType.Ascending);

            return new List<Entity>(CommandHelpers.RetrieveAll(service, query));
        }
    }
}
```

In `CompletedCheck.Blocks`, replace

```csharp
            Draw(blocks, items, reasons, isTax);
            return blocks;
```

with

```csharp
            Draw(blocks, items, reasons, isTax);

            // What the check raised, last, as the review page now prints it (2026-10-05).
            blocks.AddRange(CheckRemedialActions.Blocks(service, reviewId));
            return blocks;
```

In the class summary of `CompletedCheck` (~lines 18-24), replace `One check per file; <see cref="RemediationDocument"/> is the remedial actions' own.` with `One check per file, ending with the remedial actions that check raised (2026-10-05, <see cref="CheckRemedialActions"/>); <see cref="RemediationDocument"/> is the case's whole remediation form.`

- [ ] **Step 4: Run tests to verify they pass**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CompletedCheckDocumentTests`
Expected: PASS. Then run the whole suite. Any `PdfDumpForManualCheck`/sample test still passes.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/CheckRemedialActions.cs plugins/OutcomeTesting.Plugins/RemediationDocument.cs plugins/OutcomeTesting.Plugins/CompletedCheck.cs plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs
git commit -m "feat(pdf): each check's document ends with the remedial actions it raised

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Every adviser letter carries the checks

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/NotificationOutbox.cs:257-282` (`QueueWithCompletedCheck` gains `occurrence`)
- Modify: `plugins/OutcomeTesting.Plugins/NotificationEmitterPlugin.cs:232-241` (remediation letter), `:296-305` (case passed)
- Modify: `plugins/OutcomeTesting.Plugins/SignoffProgressPlugin.cs:448-457` (sign-off letters)
- Test: `plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs`

**Interfaces:**
- Consumes: Task 3's PDF (not required to compile).
- Produces: `NotificationOutbox.QueueWithCompletedCheck(IOrganizationService service, Guid correlationId, int eventValue, string targetTable, Guid targetId, string recipientEmail, string subject, string body, string templateCode, EntityReference caseRef, string occurrence = null)`.

- [ ] **Step 1: Write the failing tests**

Add to `CompletedCheckDocumentTests.cs`, under a new `// ================================================================== the letters` divider at the end of the test methods (before the helpers):

```csharp
        private const string AdviserEmail = "adam.strumidlo@example.com";
        private const string Checks = "Tax check 300000006.pdf|AQS check 300000006.pdf";

        /// <summary>The case's adviser, reachable by email (AD-228).</summary>
        private static FakeOrganizationService WithAdviser()
        {
            var service = Case();
            service.Row("al_outcomecase", CaseId)["al_adviseremail"] = AdviserEmail;
            service.Seed("contact", Guid.NewGuid(),
                "fullname", "Adam Strumidlo",
                "emailaddress1", AdviserEmail,
                "statecode", new OptionSetValue(0));
            return service;
        }

        private static Entity LetterTo(FakeOrganizationService service, string email)
        {
            var created = service.Creates.Single(c =>
                c.LogicalName == "al_notification" && c.GetAttributeValue<string>("al_recipientemail") == email);
            return service.Row("al_notification", created.Id);
        }

        [Fact]
        public void The_case_passed_letter_carries_the_checks()
        {
            var service = WithAdviser();

            NotificationEmitterPlugin.QueueCasePassed(service, Guid.NewGuid(), Ref());

            Assert.Equal(Checks, LetterTo(service, AdviserEmail).GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr));
        }

        [Fact]
        public void The_advisers_remediation_letter_carries_the_checks()
        {
            var service = WithAdviser();
            Action(service, completed: false);

            Remediation.AssignOpenActions(service, Ref(), Guid.NewGuid());

            Assert.Equal(Checks, LetterTo(service, AdviserEmail).GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr));
        }

        [Fact]
        public void Every_adviser_letter_is_queued_with_the_checks()
        {
            // The sign-off letters are queued deep inside SignoffProgressPlugin, which no test
            // drives end to end; this pins that each adviser letter goes the one way.
            var plugins = Path.Combine(PluginsPath(), "OutcomeTesting.Plugins");
            var emitter = File.ReadAllText(Path.Combine(plugins, "NotificationEmitterPlugin.cs"));
            var signoff = File.ReadAllText(Path.Combine(plugins, "SignoffProgressPlugin.cs"));

            Assert.Equal(3, Count(emitter, "NotificationOutbox.QueueWithCompletedCheck("));
            Assert.Equal(1, Count(emitter, "NotificationOutbox.Queue("));
            Assert.Contains("NotificationOutbox.QueueWithCompletedCheck(", signoff.Substring(signoff.IndexOf("private static void QueueSignoffNotification(", StringComparison.Ordinal)));
        }

        private static int Count(string text, string probe)
        {
            var count = 0;
            for (var at = text.IndexOf(probe, StringComparison.Ordinal); at >= 0; at = text.IndexOf(probe, at + probe.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        private static string PluginsPath()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "OutcomeTesting.Plugins")))
            {
                directory = directory.Parent;
            }

            return directory.FullName;
        }
```

Add `using System.IO;` to the file's usings. The `3` counts the remediation letter, the case-passed letter, and the para-planner letter that Task 5 adds. The one remaining `Queue(` is the allocation letter, which goes to a checker. Until Task 5 lands, this test expects 3 and finds 2. That is intended: it fails until Task 5.

- [ ] **Step 2: Run tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CompletedCheckDocumentTests`
Expected: FAIL. The attachment name is null on both letters, and the count test fails.

If `AssignOpenActions` queues nothing because the fake does not match the contact, read `NotificationOutbox.MatchAdviser` and seed what it reads (for example `statuscode`), as `AdviserNotificationTests.Case()` does.

- [ ] **Step 3: Write the implementation**

In `NotificationOutbox.cs`, change `QueueWithCompletedCheck`:

```csharp
        public static Guid QueueWithCompletedCheck(
            IOrganizationService service,
            Guid correlationId,
            int eventValue,
            string targetTable,
            Guid targetId,
            string recipientEmail,
            string subject,
            string body,
            string templateCode,
            EntityReference caseRef,
            string occurrence = null)
        {
            var id = Queue(
                service, correlationId, eventValue, targetTable, targetId,
                recipientEmail, subject, body, templateCode, occurrence);
```

Leave the rest of the method unchanged. Add to its summary: `/// Every letter to an adviser goes this way (2026-10-05: "Include a pdf of the checks whenever sending an email to advisers").`

In `NotificationEmitterPlugin.QueueRemediationAssigned`, replace the final `NotificationOutbox.Queue(...)` call with:

```csharp
            // Every adviser letter carries the checks (project owner, 2026-10-05).
            NotificationOutbox.QueueWithCompletedCheck(
                service,
                correlationId,
                NotificationOutbox.EventRemediationAssigned,
                targetTable,
                targetId,
                email,
                letter.Subject,
                letter.Body,
                code,
                caseRef);
```

In `NotificationEmitterPlugin.QueueCasePassed`, replace the final `NotificationOutbox.Queue(...)` call with:

```csharp
            NotificationOutbox.QueueWithCompletedCheck(
                service,
                correlationId,
                NotificationOutbox.EventCasePassed,
                "al_outcomecase",
                caseRef.Id,
                email,
                passed.Subject,
                passed.Body,
                NotificationTemplates.CasePassed,
                caseRef);
```

In `SignoffProgressPlugin.QueueSignoffNotification`, replace the final `NotificationOutbox.Queue(service, context, ...)` call with:

```csharp
            // Every adviser letter carries the checks (project owner, 2026-10-05).
            NotificationOutbox.QueueWithCompletedCheck(
                service,
                context.CorrelationId,
                approved ? NotificationOutbox.EventSignoffApproved : NotificationOutbox.EventSignoffRejected,
                SignoffEntity,
                signoff.Id,
                email,
                letter.Subject,
                letter.Body,
                code,
                caseRef);
```

- [ ] **Step 4: Run tests**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: everything passes except `Every_adviser_letter_is_queued_with_the_checks` (2 of 3 until Task 5). No other test regresses. `AdviserNotificationTests` still passes: its case has no submitted review, so the letter simply carries no attachment.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/NotificationOutbox.cs plugins/OutcomeTesting.Plugins/NotificationEmitterPlugin.cs plugins/OutcomeTesting.Plugins/SignoffProgressPlugin.cs plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs
git commit -m "feat(notifications): every adviser letter carries the check PDFs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: The para-planner's copy, and attachments that list every action

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/ParaplannerRemediationLetter.cs`
- Modify: `plugins/OutcomeTesting.Plugins/NotificationEmitterPlugin.cs` (end of `QueueRemediationAssigned`)
- Modify: `plugins/OutcomeTesting.Plugins/NotificationOutbox.cs` (new `RefreshDocuments`)
- Modify: `plugins/OutcomeTesting.Plugins/Remediation.cs:797-878` (`Raise`)
- Modify: `knowledge/decision-log.md` (append AD-232)
- Test: `plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs`

**Interfaces:**
- Consumes: `QueueWithCompletedCheck(..., EntityReference caseRef, string occurrence = null)` from Task 4.
- Produces:
  - `public static class ParaplannerRemediationLetter { public const string Occurrence = "PARAPLANNER"; public static string Subject(string reference); public static string Body(string reference); }`
  - `public static void NotificationOutbox.RefreshDocuments(IOrganizationService service, EntityReference caseRef, Guid reviewId)`.

- [ ] **Step 1: Write the failing tests**

Add to `CompletedCheckDocumentTests.cs`, in the letters section:

```csharp
        private const string ParaplannerEmail = "pat.planner@example.com";

        [Fact]
        public void The_para_planner_gets_their_own_copy_with_the_checks()
        {
            var service = WithAdviser();
            service.Row("al_outcomecase", CaseId)["al_paraplanneremail"] = ParaplannerEmail;
            Action(service, completed: false);

            Remediation.AssignOpenActions(service, Ref(), Guid.NewGuid());

            var copy = LetterTo(service, ParaplannerEmail);
            Assert.Equal("Checks and remedial points: 300000006", copy.GetAttributeValue<string>("al_subject"));
            Assert.Equal("The checks and remedial points for case 300000006 are attached.", copy.GetAttributeValue<string>("al_body"));
            Assert.Equal(Checks, copy.GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr));

            // Its own outbox row: a replay collides with it, and it never collides with the adviser's.
            Assert.Equal(
                NotificationOutbox.CodeFor(NotificationOutbox.EventRemediationAssigned, AqsReviewId, ParaplannerRemediationLetter.Occurrence),
                copy.GetAttributeValue<string>("al_notificationcode"));
            Assert.NotEqual(
                LetterTo(service, AdviserEmail).GetAttributeValue<string>("al_notificationcode"),
                copy.GetAttributeValue<string>("al_notificationcode"));
        }

        [Fact]
        public void A_case_with_no_para_planner_email_still_queues_the_copy_unaddressed()
        {
            // The drain parks it as Failed with a reason; the adviser's letter is unaffected.
            var service = WithAdviser();
            Action(service, completed: false);

            Remediation.AssignOpenActions(service, Ref(), Guid.NewGuid());

            var copy = service.Creates.Single(c =>
                c.LogicalName == "al_notification"
                && c.GetAttributeValue<string>("al_notificationcode")
                    == NotificationOutbox.CodeFor(NotificationOutbox.EventRemediationAssigned, AqsReviewId, ParaplannerRemediationLetter.Occurrence));
            Assert.Null(copy.GetAttributeValue<string>("al_recipientemail"));
            Assert.NotNull(LetterTo(service, AdviserEmail));
        }

        [Fact]
        public void The_para_planner_letter_is_not_an_editable_template()
        {
            Assert.DoesNotContain(NotificationTemplates.All, t => t.Subject.StartsWith("Checks and remedial points", StringComparison.Ordinal));
        }

        [Fact]
        public void Raising_the_actions_refreshes_a_pending_letters_documents_and_leaves_a_sent_one_alone()
        {
            var service = Case();
            var pending = service.Seed("al_notification", Guid.NewGuid(),
                "al_event", new OptionSetValue(NotificationOutbox.EventRemediationAssigned),
                "al_status", new OptionSetValue(NotificationOutbox.StatusPending),
                "al_targetid", AqsReviewId.ToString("D"),
                NotificationOutbox.AttachmentNameAttr, "stale.pdf",
                NotificationOutbox.AttachmentBodyAttr, "AA==");
            var sent = service.Seed("al_notification", Guid.NewGuid(),
                "al_event", new OptionSetValue(NotificationOutbox.EventRemediationAssigned),
                "al_status", new OptionSetValue(NotificationOutbox.StatusSent),
                "al_targetid", AqsReviewId.ToString("D"),
                NotificationOutbox.AttachmentNameAttr, "sent.pdf",
                NotificationOutbox.AttachmentBodyAttr, "AA==");

            Remediation.Raise(
                service, Ref(), "300000006", AqsReviewId, 1, "Insufficient evidence", null,
                new[] { "First point: No", "Second point: Fail" }, null,
                new DateTime(2026, 9, 25, 10, 0, 0, DateTimeKind.Utc),
                new[] { "Fix the first.", "Fix the second." });

            Assert.Equal(Checks, service.Row("al_notification", pending.Id).GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr));
            Assert.Equal("sent.pdf", service.Row("al_notification", sent.Id).GetAttributeValue<string>(NotificationOutbox.AttachmentNameAttr));
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter FullyQualifiedName~CompletedCheckDocumentTests`
Expected: FAIL to compile. `ParaplannerRemediationLetter` does not exist.

- [ ] **Step 3: Write the letter**

Create `plugins/OutcomeTesting.Plugins/ParaplannerRemediationLetter.cs`:

```csharp
namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// The para-planner's copy when a remediation is raised (project owner, 2026-10-05: "When a
    /// remediation is raised include paraplanner in email with pdf of checks and remedial
    /// actions", as a separate email).
    ///
    /// <para>
    /// <b>Not editable.</b> "It should just be a copy of the checks and remedial points": the
    /// letter is its attachments, so the wording is fixed here and kept out of
    /// <see cref="NotificationTemplates"/>, where the template editor would offer it. It is
    /// queued with no template code, so no stored row can reword or redirect it. No portal
    /// button: the para-planner has no access to the case.
    /// </para>
    /// </summary>
    public static class ParaplannerRemediationLetter
    {
        /// <summary>The outbox occurrence that makes it a row of its own beside the adviser's.</summary>
        public const string Occurrence = "PARAPLANNER";

        public static string Subject(string reference)
        {
            return "Checks and remedial points: " + NotificationTemplates.ReferenceOr(reference);
        }

        public static string Body(string reference)
        {
            return string.IsNullOrWhiteSpace(reference)
                ? "The checks and remedial points for this case are attached."
                : "The checks and remedial points for case " + reference.Trim() + " are attached.";
        }
    }
}
```

At the end of `NotificationEmitterPlugin.QueueRemediationAssigned`, after the adviser's `QueueWithCompletedCheck` call from Task 4, add:

```csharp
            // The para-planner's own copy (project owner, 2026-10-05): the same documents, a
            // fixed line of wording, its own outbox row so it neither collides with the
            // adviser's nor goes twice.
            var paraplannerReference = caseRow == null ? null : caseRow.GetAttributeValue<string>("al_casereference");
            NotificationOutbox.QueueWithCompletedCheck(
                service,
                correlationId,
                NotificationOutbox.EventRemediationAssigned,
                targetTable,
                targetId,
                NotificationOutbox.ParaplannerEmail(service, caseRef),
                ParaplannerRemediationLetter.Subject(paraplannerReference),
                ParaplannerRemediationLetter.Body(paraplannerReference),
                null,
                caseRef,
                ParaplannerRemediationLetter.Occurrence);
```

`Queue` with a null template code is safe: `NotificationTemplateRows.SettingsFor` returns empty settings for it. Its `QueueCustom` finds the administrator's extra letters already queued by the adviser's call (same event, target and template code), so it adds none.

- [ ] **Step 4: Write the refresh**

In `NotificationOutbox.cs`, after `AttachCompletedCheck`, add:

```csharp
        /// <summary>
        /// Rebuilds the documents on every Pending remediation letter for this review that
        /// already carries some (2026-10-05).
        ///
        /// <para>
        /// The letters are queued by NotificationEmitterPlugin on the create of the FIRST
        /// action a submit raises, so the documents drawn then list one action. Called by
        /// <see cref="Remediation.Raise"/> once every action exists. Pending only: a sent
        /// letter's documents are what the recipient has, and are never rewritten.
        /// </para>
        /// <para>Never throws: the actions are raised whether or not a document can be redrawn.</para>
        /// </summary>
        public static void RefreshDocuments(IOrganizationService service, EntityReference caseRef, Guid reviewId)
        {
            if (caseRef == null || reviewId == Guid.Empty)
            {
                return;
            }

            try
            {
                var query = new QueryExpression(NotificationEntity)
                {
                    ColumnSet = new ColumnSet(AttachmentNameAttr),
                    Criteria = new FilterExpression(),
                };
                query.Criteria.AddCondition("al_targetid", ConditionOperator.Equal, reviewId.ToString("D"));
                query.Criteria.AddCondition("al_event", ConditionOperator.Equal, EventRemediationAssigned);
                query.Criteria.AddCondition("al_status", ConditionOperator.Equal, StatusPending);

                foreach (var row in service.RetrieveMultiple(query).Entities)
                {
                    if (!string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(AttachmentNameAttr)))
                    {
                        AttachCompletedCheck(service, row.Id, caseRef);
                    }
                }
            }
            catch (Exception)
            {
                // See the summary.
            }
        }
```

In `Remediation.cs`, rename the existing `public static IList<Guid> Raise(...)` to `private static IList<Guid> RaiseActions(...)` with the same parameters and body. Above it, add a public `Raise` that keeps the existing doc comment:

```csharp
        public static IList<Guid> Raise(
            IOrganizationService service,
            EntityReference caseRef,
            string caseReference,
            Guid reviewId,
            int sequence,
            string reason,
            string observation,
            IList<string> items,
            EntityReference adviserContact,
            DateTime raisedOn,
            IList<string> remedialActions = null,
            string overallRemedialAction = null)
        {
            var raised = RaiseActions(
                service, caseRef, caseReference, reviewId, sequence, reason, observation,
                items, adviserContact, raisedOn, remedialActions, overallRemedialAction);

            // The letters were queued on the first create and drew one action; every action
            // exists now (2026-10-05).
            NotificationOutbox.RefreshDocuments(service, caseRef, reviewId);
            return raised;
        }
```

Move the existing doc comment onto the public `Raise`. Give `RaiseActions` a one-line summary: `/// The actions themselves; see <see cref="Raise"/>.`

- [ ] **Step 5: Run tests**

Run: `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: PASS, including `Every_adviser_letter_is_queued_with_the_checks` (now 3).

If `The_para_planner_gets_their_own_copy_with_the_checks` fails on attachment names, check that `ParaplannerEmail` reads `CasePeople.ParaplannerEmailAttr`. If that is not `al_paraplanneremail`, seed the attribute it names.

- [ ] **Step 6: Record the decision**

Append to `knowledge/decision-log.md`, after AD-231:

```markdown
| AD-232 | **Advisers and para-planners get the checks, with each check's remedial actions.** Every letter to an adviser - remediation raised, case passed, sign-off approved, sign-off rejected - is queued through `NotificationOutbox.QueueWithCompletedCheck` and carries one PDF per submitted check, plus the remediation form once an action is completed. Each check's PDF ends with the remedial actions that check raised (`CheckRemedialActions`), and the submitted review page shows the same section, so its Save as PDF matches. When a remediation is raised the para-planner gets a separate, fixed letter (`ParaplannerRemediationLetter`, occurrence `PARAPLANNER`, no template code, not in the template editor) with the same documents. `Remediation.Raise` refreshes the documents on Pending remediation letters once every action exists. The Code App case page lists the case's remedial actions to those who can open its remediation. | Project owner, 2026-10-05: include the para-planner, in a separate email, with a PDF of the checks and remedial actions; include a PDF of the checks whenever emailing advisers; show the remediation actions on the case details and in the PDFs downloaded from the portal and sent out. "The paraplanner letter is not editable. It should just be a copy of the checks and remedial points." | 2026-10-05 |
```

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/ParaplannerRemediationLetter.cs plugins/OutcomeTesting.Plugins/NotificationEmitterPlugin.cs plugins/OutcomeTesting.Plugins/NotificationOutbox.cs plugins/OutcomeTesting.Plugins/Remediation.cs plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs knowledge/decision-log.md
git commit -m "feat(notifications): the para-planner's copy of the checks and remedial points; documents list every action (AD-232)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: The submitted review page shows its remedial actions

**Files:**
- Create: `app/src/features/reviews/portalReviewRemedialRead.test.ts`
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html` (insert after the `{% endif %}` that closes the editable remedial card, ~line 3963, before `{% if editable %}` of the submit card)

**Interfaces:**
- Consumes: Liquid variable `review_id` (already assigned; used by the `selectedreasons` fetch).
- Produces: a `<section data-ot-remedial-read>` headed "Remedial actions", rendered only `{% unless editable %}` and only when the review has actions.

- [ ] **Step 1: Write the failing test**

```ts
import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * A submitted check shows the remedial actions it raised, so the page's Save as PDF carries
 * them as the emailed document does (project owner, 2026-10-05).
 */
const start = reviewTemplate.indexOf('{% fetchxml review_actions %}');
const section = reviewTemplate.slice(start, reviewTemplate.indexOf('</section>', start));

describe('the submitted review page lists its remedial actions', () => {
  it('reads this review\'s live actions', () => {
    expect(start).toBeGreaterThan(-1);
    expect(section).toContain('<condition attribute="al_reviewinstanceid" operator="eq" value="{{ review_id | xml_escape }}" />');
    expect(section).toContain('<condition attribute="statecode" operator="eq" value="0" />');
  });

  it('is drawn only on a review that can no longer be edited', () => {
    const opener = reviewTemplate.lastIndexOf('{% unless editable %}', start);
    expect(opener).toBeGreaterThan(-1);
    expect(reviewTemplate.slice(opener, start)).not.toContain('{% endunless %}');
  });

  it('heads the columns as the emailed document does', () => {
    for (const heading of ['No.', 'Fail point', 'Remedial action', 'Owner', 'Target date', 'Status']) {
      expect(section).toContain(`<th scope="col">${heading}</th>`);
    }
    expect(section).toContain('>Remedial actions</h2>');
  });

  it('splits the fail points with the filters this site accepts', () => {
    expect(section).toContain("| remove_first: '- '");
    expect(section).not.toMatch(/split: '[^']{2,}'/);
    expect(section).not.toMatch(/truncate: \d+, /);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (in `app/`): `npx vitest run src/features/reviews/portalReviewRemedialRead.test.ts`
Expected: FAIL. `start` is -1.

- [ ] **Step 3: Write the section**

Insert after the `{% endif %}` that closes the editable "Fail points and remedial actions" card:

```liquid
    {% unless editable %}
      {% comment %}
        The remedial actions this check raised, read-only (project owner, 2026-10-05: add the
        remediation actions to the PDF downloaded from the portal). The editable card above is
        not rendered once the review is submitted, so without this the page and its Save as
        PDF lost them. CheckRemedialActions draws the same table into the emailed document.

        The fail points are split out of al_description as OT Case Detail splits them:
        newline_to_br then a one-character split, and a line is a fail point when putting
        "- " back in front of it with its first "- " removed reproduces it.
      {% endcomment %}
      {% fetchxml review_actions %}
      <fetch>
        <entity name="al_remediationaction">
          <attribute name="al_remediationactionid" />
          <attribute name="al_description" />
          <attribute name="al_remedialaction" />
          <attribute name="al_adviserresponse" />
          <attribute name="al_assignedcontactid" />
          <attribute name="al_duedate" />
          <attribute name="al_actionstatus" />
          <attribute name="createdon" />
          <filter type="and">
            <condition attribute="al_reviewinstanceid" operator="eq" value="{{ review_id | xml_escape }}" />
            <condition attribute="statecode" operator="eq" value="0" />
          </filter>
          <order attribute="createdon" descending="false" />
          <order attribute="al_remediationactioncode" descending="false" />
        </entity>
      </fetch>
      {% endfetchxml %}
      {% assign review_actions_list = review_actions.results.entities %}
      {% if review_actions_list.size > 0 %}
      <section class="ot-card ot-remedial" data-ot-remedial-read aria-labelledby="ot-remedial-read-heading">
        <h2 class="ot-card__title" id="ot-remedial-read-heading">Remedial actions</h2>
        <div class="ot-table-wrap">
          <table class="ot-table ot-remedial__table">
            <thead>
              <tr>
                <th scope="col">No.</th>
                <th scope="col">Fail point</th>
                <th scope="col">Remedial action</th>
                <th scope="col">Owner</th>
                <th scope="col">Target date</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              {% assign rr_no = 0 %}
              {% for a in review_actions_list %}
                {% assign rr_text = a.al_remedialaction | default: a.al_adviserresponse | default: '—' %}
                {% assign rr_owner = a.al_assignedcontactid.name | default: 'Nobody assigned' %}
                {% assign rr_due = '—' %}{% if a.al_duedate %}{% assign rr_due = a.al_duedate | date: 'dd MMM yyyy' %}{% endif %}
                {% assign rr_status = a.al_actionstatus.label | default: '—' %}
                {% assign rr_flat = a.al_description | default: '' | newline_to_br | replace: '<br />', '|' | replace: '<br>', '|' %}
                {% assign rr_lines = rr_flat | split: '|' %}
                {% assign rr_count = 0 %}
                {% for l in rr_lines %}{% assign line = l | strip %}{% assign rest = line | remove_first: '- ' %}{% assign rebuilt = '- ' | append: rest %}{% if rebuilt == line %}{% assign rr_count = rr_count | plus: 1 %}{% endif %}{% endfor %}
                {% if rr_count == 0 %}
                  {% assign rr_no = rr_no | plus: 1 %}
                  <tr>
                    <td>{{ rr_no }}</td>
                    <td>—</td>
                    <td>{{ rr_text | escape | newline_to_br }}</td>
                    <td>{{ rr_owner | escape }}</td>
                    <td>{{ rr_due }}</td>
                    <td>{{ rr_status | escape }}</td>
                  </tr>
                {% else %}
                  {% assign rr_shown = 0 %}
                  {% for l in rr_lines %}
                    {% assign line = l | strip %}{% assign rest = line | remove_first: '- ' %}{% assign rebuilt = '- ' | append: rest %}
                    {% if rebuilt == line %}
                      {% assign rr_shown = rr_shown | plus: 1 %}
                      {% assign rr_no = rr_no | plus: 1 %}
                      <tr>
                        <td>{{ rr_no }}</td>
                        <td>{{ rest | escape }}</td>
                        {% if rr_shown == 1 %}
                          <td rowspan="{{ rr_count }}">{{ rr_text | escape | newline_to_br }}</td>
                          <td rowspan="{{ rr_count }}">{{ rr_owner | escape }}</td>
                          <td rowspan="{{ rr_count }}">{{ rr_due }}</td>
                          <td rowspan="{{ rr_count }}">{{ rr_status | escape }}</td>
                        {% endif %}
                      </tr>
                    {% endif %}
                  {% endfor %}
                {% endif %}
              {% endfor %}
            </tbody>
          </table>
        </div>
      </section>
      {% endif %}
    {% endunless %}
```

Before inserting, confirm `review_id` is assigned above this point (search `{% assign review_id`). If the page uses a different name in this region, use `rv.al_reviewinstanceid` and update the test's expected condition string to match.

- [ ] **Step 4: Run tests**

Run (in `app/`): `npx vitest run src/features/reviews`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html app/src/features/reviews/portalReviewRemedialRead.test.ts
git commit -m "feat(portal): a submitted check shows its remedial actions, so its PDF does too

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: The Code App case page lists the remedial actions

**Files:**
- Create: `app/src/features/cases/CaseRemedialActions.tsx`
- Create: `app/src/features/cases/caseRemedialActions.test.tsx`
- Modify: `app/src/features/cases/CaseDetailPage.tsx:203-209`
- Modify: `app/src/features/cases/CaseDetailPage.css` (append table styles)

**Interfaces:**
- Consumes: `useRemediation(caseId, reloadKey)` from `../remediation/useRemediation`. Ready state is `{ status: 'ready', actions: RemediationActionRow[], ... }`. Also `groupIssues(actions)` from `../remediation/remediationIssues`, which returns `{ action, lines: { number, issue }[] }[]`.
- Produces: `export function CaseRemedialActions({ actions }: { actions: RemediationActionRow[] })` and `export function CaseRemedialActionsPanel({ caseId, reloadKey }: { caseId: string; reloadKey: number })`.

- [ ] **Step 1: Write the failing test**

`app/src/features/cases/caseRemedialActions.test.tsx`:

```tsx
import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

import type { RemediationActionRow } from '../remediation/remediationMapping';

vi.mock('../remediation/useRemediation', () => ({ useRemediation: () => ({ status: 'loading' }) }));

const { CaseRemedialActions } = await import('./CaseRemedialActions');

function action(overrides: Partial<RemediationActionRow>): RemediationActionRow {
  return {
    id: 'a1',
    reference: 'Remediation 300000006',
    description: 'Issues found on the check:\n- ID verification: No\n- Client objectives: Fail\n',
    status: 'Open',
    dueOn: '05 Oct 2026',
    completedOn: null,
    triggeredBy: 'AQS check',
    owner: 'Service Account',
    rowVersion: null,
    remedialAction: 'Re-verify ID and file the evidence.',
    actionPerformed: null,
    adviserNote: null,
    evidenceReference: null,
    clientContactRequired: null,
    recheckRequired: null,
    changesAdvice: null,
    assignedTo: 'Adam Strumidlo',
    createdOn: '2026-09-25T10:00:00Z',
    clockStartedOn: null,
    completedOnRaw: null,
    ...overrides,
  };
}

describe('the case page lists the remedial actions', () => {
  it('draws nothing when the case has none', () => {
    expect(renderToStaticMarkup(<CaseRemedialActions actions={[]} />)).toBe('');
  });

  it('draws one numbered row per fail point with the action, adviser, date and status', () => {
    const html = renderToStaticMarkup(<CaseRemedialActions actions={[action({})]} />);

    expect(html).toContain('<h2 id="panel-remedial">Remedial actions</h2>');
    expect(html).toContain('ID verification: No');
    expect(html).toContain('Client objectives: Fail');
    expect(html).toContain('Re-verify ID and file the evidence.');
    expect(html).toContain('Adam Strumidlo');
    expect(html).toContain('05 Oct 2026');
    expect(html).toContain('AQS check');
    expect(html).toContain('<td>1</td>');
    expect(html).toContain('<td>2</td>');
    // The record owner is a system account, never the person who owes the action.
    expect(html).not.toContain('Service Account');
  });

  it('says nobody is assigned rather than leaving the owner blank', () => {
    const html = renderToStaticMarkup(<CaseRemedialActions actions={[action({ assignedTo: null })]} />);
    expect(html).toContain('Nobody assigned');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run (in `app/`): `npx vitest run src/features/cases/caseRemedialActions.test.tsx`
Expected: FAIL. The module `./CaseRemedialActions` does not exist.

- [ ] **Step 3: Write the component**

`app/src/features/cases/CaseRemedialActions.tsx`:

```tsx
import { useRemediation } from '../remediation/useRemediation';
import { groupIssues } from '../remediation/remediationIssues';
import type { RemediationActionRow } from '../remediation/remediationMapping';

/**
 * The case's remedial actions on its details page (project owner, 2026-10-05: "Include the
 * remediation actions on the case details if there are any"). Read-only: answering and
 * signing off stay on the remediation page. Every action is listed, settled ones included -
 * this is the case's record, not a worklist.
 *
 * The owner is the adviser the action is assigned to, never `ownerid`, which is the record's
 * system owner.
 */
export function CaseRemedialActions({ actions }: { actions: RemediationActionRow[] }) {
  if (actions.length === 0) {
    return null;
  }

  return (
    <section className="case-detail__remedial" aria-labelledby="panel-remedial">
      <h2 id="panel-remedial">Remedial actions</h2>
      <table className="case-detail__remedial-table">
        <thead>
          <tr>
            <th scope="col">No.</th>
            <th scope="col">Check</th>
            <th scope="col">Fail point</th>
            <th scope="col">Remedial action</th>
            <th scope="col">Owner</th>
            <th scope="col">Target date</th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {groupIssues(actions).flatMap(({ action, lines }) =>
            lines.map((line, index) => (
              <tr key={`${action.id}-${line.number}`}>
                <td>{line.number}</td>
                {index === 0 ? (
                  <>
                    <td rowSpan={lines.length}>{action.triggeredBy ?? '—'}</td>
                    <td>{line.issue}</td>
                    <td rowSpan={lines.length}>{action.remedialAction ?? '—'}</td>
                    <td rowSpan={lines.length}>{action.assignedTo ?? 'Nobody assigned'}</td>
                    <td rowSpan={lines.length}>{action.dueOn ?? '—'}</td>
                    <td rowSpan={lines.length}>{action.status}</td>
                  </>
                ) : (
                  <td>{line.issue}</td>
                )}
              </tr>
            )),
          )}
        </tbody>
      </table>
    </section>
  );
}

/** Loads the case's actions and draws them; draws nothing until they are read. */
export function CaseRemedialActionsPanel({ caseId, reloadKey }: { caseId: string; reloadKey: number }) {
  const state = useRemediation(caseId, reloadKey);
  return state.status === 'ready' ? <CaseRemedialActions actions={state.actions} /> : null;
}
```

If `groupIssues` gives `line.issue` and `line.number` names other than those in its `IssueLine` interface (`number`, `issue`), use the interface's names.

In `CaseDetailPage.tsx`, add the import `import { CaseRemedialActionsPanel } from './CaseRemedialActions';` and replace the gated link block (lines ~203-209):

```tsx
                    <PermissionGate resource="page.remediation">
                      {/*
                        The case's remedial actions, to the same people the remediation link
                        has always been shown to (2026-10-05).
                      */}
                      <CaseRemedialActionsPanel caseId={state.detail.id} reloadKey={reloadKey} />
                      <section className="case-detail__related" aria-label="Related records">
                        <Link to={`/cases/${state.detail.id}/remediation`}>
                          Remediation and sign-off →
                        </Link>
                      </section>
                    </PermissionGate>
```

Append to `CaseDetailPage.css`, matching the existing `.case-detail__checks-table` rules:

```css
.case-detail__remedial-table {
  width: 100%;
  border-collapse: collapse;
}

.case-detail__remedial-table th,
.case-detail__remedial-table td {
  text-align: left;
  vertical-align: top;
  padding: 0.4rem 0.6rem;
  border-bottom: 1px solid var(--color-border, #d0d4da);
}
```

Read `.case-detail__checks-table` in the same file first. If it uses different variables or spacing, copy its values instead.

- [ ] **Step 4: Run tests and type check**

Run (in `app/`): `npx vitest run src/features/cases src/features/remediation; npx tsc -b`
Expected: PASS, and no type errors.

- [ ] **Step 5: Commit**

```bash
git add app/src/features/cases/CaseRemedialActions.tsx app/src/features/cases/caseRemedialActions.test.tsx app/src/features/cases/CaseDetailPage.tsx app/src/features/cases/CaseDetailPage.css
git commit -m "feat(app): the case page lists its remedial actions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Deploy to DEV and prove it

**Files:**
- Create: `docs/deployment/2026-10-05-remediation-documents-and-open-form.md`

**Interfaces:**
- Consumes: Tasks 1-7 committed.
- Produces: DEV running the new assembly, template and Code App, with the proof recorded.

- [ ] **Step 1: Full local verification**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests
cd app; npx vitest run; npx tsc -b; cd ..
```
Expected: all pass. Record the test counts.

- [ ] **Step 2: Push the assembly**

```powershell
dotnet build plugins/OutcomeTesting.Plugins -c Release
(Get-Item plugins/OutcomeTesting.Plugins/bin/Release/net462/OutcomeTesting.Plugins.dll).Length
dotnet build plugins/OutcomeTesting.Registration -c Release
$t='C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Registration\bin\Release\net8.0\OutcomeTesting.Registration.exe'
& $t pushassembly https://org0b075da8.crm11.dynamics.com/ C:\Users\rsimu\OutcomeTesting\plugins\OutcomeTesting.Plugins\bin\Release\net462\OutcomeTesting.Plugins.dll
```

Run one registration-tool process at a time. If it hangs at "Connecting…", another instance is alive: kill it and retry. Confirm the byte count the tool reports equals the local file. No new plug-in type or step is needed: every change is inside existing steps.

- [ ] **Step 3: Push the review template**

```powershell
& $t pushwebtemplate https://org0b075da8.crm11.dynamics.com/ a1000000-0000-4000-8000-00000000001b "C:\Users\rsimu\OutcomeTesting\powerpages\outcome-testing---outcometesting\web-templates\ot-review-detail\OT-Review-Detail.webtemplate.source.html"
```

- [ ] **Step 4: Build and push the Code App**

```powershell
cd app; npm run build; npx pa app push; cd ..
```

Record the bundle name in `app/dist/assets`. Use the `/app/` URL with `sourcetime` that the push prints, not the short `/a/{appId}` URL, which serves the old bundle.

- [ ] **Step 5: Prove it on DEV**

DEV cases are seeded Queued with no reviews. Assign an AQS review with `al_AssignCase`, as in the DEV e2e runbook.
1. Open the review in the DEV portal. Confirm no option is greyed out and nothing is pre-ticked.
2. Answer a test point Fail, grade Pass with issues, and answer "Remedial action required?" No. Confirm each saves with no error.
3. Write the remedial actions and submit. Confirm the submit succeeds.
4. Read the outbox for the review with one `webapi` GET:
   `al_notifications?$select=al_recipientemail,al_subject,al_attachmentname,al_status,al_notificationcode&$filter=al_targetid eq '<reviewid>'`.
   Expect two rows:
   - the adviser's `REMEDIATION-*` letter;
   - the para-planner's `Checks and remedial points: <ref>`.

   Both should have `al_attachmentname` listing the check PDFs.
5. Decode one `al_attachmentbody` part to a file in the scratchpad. Open it and confirm it ends with the "Remedial actions" table listing every fail point.
6. Reload the submitted review page. Confirm the "Remedial actions" section is shown, and Save as PDF includes it. If it is missing, the page may be serving a cached fetch: retry the same URL with the id upper-cased.
7. Open the case in the Code App at the push URL. Confirm the "Remedial actions" panel.

DEV delivers no email: the mailbox has server-side sync disabled, so rows rest at Pending Send. The proof is the outbox rows and their attachments.

- [ ] **Step 6: Write the deployment record**

Create `docs/deployment/2026-10-05-remediation-documents-and-open-form.md` with:
- a table of artefacts pushed (assembly bytes and sha256, template, Code App bundle and push time);
- the proof from Step 5 (review id, the two notification codes, attachment names, what the PDF showed);
- "TEST and PROD: not deployed; promotion is the owner's call."

- [ ] **Step 7: Commit and refresh the graph**

```bash
git add docs/deployment/2026-10-05-remediation-documents-and-open-form.md
git commit -m "docs(deploy): remediation documents, para-planner letter and open check form live in DEV

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
graphify update .
```
