# Checklist Administration — Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Six server-side commands that add, retire and move checklist questions and sections, so the checklist can be administered without the `pac` CLI.

**Architecture:** Six Dataverse Custom APIs, each a JSON contract plus a plug-in built to the `al_RetireAndSucceedQuestion` pattern: check the caller, replay on an idempotency key, write an immutable Audit Event. One shared guard class holds the eight question codes that compiled C# depends on. No UI — that is plan three.

**Tech Stack:** C# plug-ins (Dataverse SDK, xUnit), the `OutcomeTesting.Registration` CLI, TypeScript client wrappers, Power Apps `pa` CLI for data-source registration.

**Spec:** `docs/superpowers/specs/2026-09-11-checklist-administration-design.md`

**Depends on:** `docs/superpowers/plans/2026-09-11-checklist-administration-foundation.md`, complete as of 2026-09-12. Every reader already honours section dates, the optional flag and the Both owner role. This plan writes the states that plan taught the system to read.

## Global Constraints

- **Deploy to `Env_AQ_Dev` only** (`https://org0b075da8.crm11.dynamics.com/`), and nothing else, unless the project owner names another environment for that specific promotion (direction, 2026-09-12). Confirm with `pac auth list` before every environment write and stop if the active row is not DEV.
- Security is enforced server-side, never in UI code (AGENTS.md, AD-041).
- Audit Events are immutable; nothing edits or deletes one (BR-012, NFR-AUD-01).
- Nothing is ever hard-deleted. Content leaves service by being dated out (AD-037, AD-122).
- All six commands require **Edit on `question.retire`**. No new resource key is minted — a new key would have no stored rule in TEST or PROD and would lock every administrator out (AD-041 as amended 2026-09-09).
- Audit command values: **120910793–798**. Highest currently in use is 120910792.
- `al_ownerrole`: Tax team `120910100`, AQS checker `120910101`, Both `120910105`.
- Test-visible helpers are `public`, not `internal` — the assembly is signed and deliberately carries no `InternalsVisibleTo` (see `PluginBase`).
- Plug-in tests run with `DOTNET_ROLL_FORWARD=Major`. App checks are `npx tsc -b` (not `--noEmit`) and `npm test`.
- `build -c Release` **before** `pushassembly`; it uploads `bin/Release` without building.
- This CLI does **not** gate writes uniformly. `adddatecolumn`, `addboolcolumn`, `addmemocolumn` and `addchoicecolumn` take `--confirm <orgUrl>`; `addoptionvalue`, `addmetadatatosolution`, `pushassembly` and `pushwebtemplate` do **not** and will swallow the flag as a positional argument. Copy each signature from its dispatch in `Program.cs` rather than assuming.

**Branch:** continue on `feat/checklist-administration`, which carries the foundation.

---

### Task 1: `ChecklistGuards` — the eight codes compiled C# depends on

Retiring or moving one of these does not degrade the checklist, it stops the system producing outcomes. The guard is a lookup from code to the sentence explaining what breaks, so a refusal tells the administrator *why* rather than just "no".

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/ChecklistGuards.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/ChecklistGuardsTests.cs`

**Interfaces:**
- Produces: `ChecklistGuards.ProtectedReason(string questionCode) -> string` (null when unprotected), `ChecklistGuards.ProtectedCodes -> IEnumerable<string>`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122: eight question codes are read by code, not just by reviewers. Retiring or
    /// moving one is refused, and the refusal names what would break.
    /// </summary>
    public class ChecklistGuardsTests
    {
        [Theory]
        [InlineData("Q-GR-01")]
        [InlineData("Q-TAX-02")]
        [InlineData("Q-FQ-01")]
        [InlineData("Q-FQTAX-01")]
        [InlineData("Q-FQ-02")]
        [InlineData("Q-FQTAX-02")]
        [InlineData("Q-FQ-03")]
        [InlineData("Q-FQTAX-03")]
        public void Each_load_bearing_code_is_protected_and_explains_itself(string code)
        {
            var reason = ChecklistGuards.ProtectedReason(code);

            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.EndsWith(".", reason);
        }

        [Fact]
        public void An_ordinary_question_is_not_protected()
        {
            Assert.Null(ChecklistGuards.ProtectedReason("Q-E1-01"));
            Assert.Null(ChecklistGuards.ProtectedReason("Q-AML-01"));
        }

        [Fact]
        public void The_code_is_matched_without_regard_to_case()
        {
            Assert.NotNull(ChecklistGuards.ProtectedReason("q-gr-01"));
        }

        [Fact]
        public void A_missing_or_empty_code_is_not_protected()
        {
            Assert.Null(ChecklistGuards.ProtectedReason(null));
            Assert.Null(ChecklistGuards.ProtectedReason(""));
        }

        [Fact]
        public void Exactly_eight_codes_are_protected()
        {
            // Pinned deliberately. A ninth appearing here without a matching constant in the
            // plug-in that reads it is how this guard drifts out of truth.
            Assert.Equal(8, ChecklistGuards.ProtectedCodes.Count());
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter ChecklistGuardsTests
```

Expected: FAIL to compile — `The name 'ChecklistGuards' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

```csharp
using System;
using System.Collections.Generic;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Question codes that compiled C# reads by name (AD-122). Taking one out of the form
    /// does not degrade the checklist, it stops the system producing outcomes, so
    /// RetireQuestion, MoveQuestion, RetireSection and the optional flag all refuse them.
    ///
    /// Rewording a protected question is safe and is not guarded: the code lives on
    /// al_question and no version edit touches it.
    ///
    /// Each entry names what breaks, because a refusal that only says "no" leaves the
    /// administrator with nowhere to go.
    /// </summary>
    public static class ChecklistGuards
    {
        private static readonly Dictionary<string, string> Protected =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Q-GR-01", "Q-GR-01 carries the advice quality grade, which OutcomeRules maps to the case outcome. Without it no case can be graded." },
                { "Q-TAX-02", "Q-TAX-02 is the Tax check outcome, which decides whether a Tax case goes to remediation." },
                { "Q-FQ-01", "Q-FQ-01 is the AQS file quality outcome and Trail Light column 10; the export refuses a row without it." },
                { "Q-FQTAX-01", "Q-FQTAX-01 is the Tax file quality outcome." },
                { "Q-FQ-02", "Q-FQ-02 is the AQS checker observation, carried into the remediation description." },
                { "Q-FQTAX-02", "Q-FQTAX-02 is the Tax checker observation, carried into the remediation description." },
                { "Q-FQ-03", "Q-FQ-03 is the AQS \"Remedial action required?\" answer, which raises remediation under BR-006." },
                { "Q-FQTAX-03", "Q-FQTAX-03 is the Tax \"Remedial action required?\" answer, which raises remediation under BR-006." },
            };

        /// <summary>Every protected code, for tests and for an administration screen.</summary>
        public static IEnumerable<string> ProtectedCodes
        {
            get { return Protected.Keys; }
        }

        /// <summary>
        /// What breaks if this question leaves the form, or null when nothing does.
        /// </summary>
        public static string ProtectedReason(string questionCode)
        {
            if (string.IsNullOrWhiteSpace(questionCode))
            {
                return null;
            }

            string reason;
            return Protected.TryGetValue(questionCode.Trim(), out reason) ? reason : null;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter ChecklistGuardsTests
```

Expected: PASS, 12 tests.

- [ ] **Step 5: Run the whole suite and commit**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
git add plugins/OutcomeTesting.Plugins/ChecklistGuards.cs plugins/OutcomeTesting.Plugins.Tests/ChecklistGuardsTests.cs
git commit -m "feat(checklist): guard the eight question codes compiled C# reads by name

Each entry names what breaks, so a refusal tells the administrator why rather
than just no. Nothing calls it yet.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

Expected: 706 + 12 = 718 tests.

---

### Task 2: `al_AddQuestion`

The exemplar. Read `RetireAndSucceedQuestionPlugin.cs` in full before starting — Tasks 3 to 7 repeat its shape, and this task is where that shape is established.

**Files:**
- Create: `plugins/customapi/al_AddQuestion.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/AddQuestionPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/AddQuestionPluginTests.cs`

**Interfaces:**
- Consumes: `CommandHelpers.ParseRequiredGuid/GetRequiredString/GetOptionalString/FindAuditByKey/WriteAuditEvent/PreconditionPrefix`, `PermissionHelpers.EnsureAppPermission(systemService, context, resourceKey, requiredLevel)`, `PermissionHelpers.AccessEdit`, `SectionRules.IsSectionEffective`
- Produces: Custom API `al_AddQuestion`, audit command **120910793**, and `AddQuestionPlugin.ParseEffectiveFrom(string, DateTime) -> DateTime` for its test

- [ ] **Step 1: Write the contract**

Create `plugins/customapi/al_AddQuestion.customapi.json`:

```json
{
  "$comment": "Contract for the AddQuestion server-side command (AD-003, AD-122). Custom API parameter Type codes: 0=Boolean, 10=String.",
  "customApi": {
    "uniquename": "al_AddQuestion",
    "name": "al_AddQuestion",
    "displayname": "Add Question",
    "description": "Creates a question and its first version in a section of the checklist version in force. Enforces the caller holds Edit on question.retire and writes an immutable Audit Event.",
    "bindingtype": 0,
    "boundentitylogicalname": null,
    "isfunction": false,
    "isprivate": false,
    "allowedcustomprocessingsteptype": 0,
    "executeprivilegename": null,
    "pluginType": "OutcomeTesting.Plugins.AddQuestionPlugin"
  },
  "requestParameters": [
    { "uniquename": "SectionId", "name": "SectionId", "displayname": "Section id", "description": "Id of the al_section to add the question to.", "type": 10, "isoptional": false },
    { "uniquename": "QuestionCode", "name": "QuestionCode", "displayname": "Question code", "description": "Unique code for the question, for example Q-E1-06.", "type": 10, "isoptional": false },
    { "uniquename": "Name", "name": "Name", "displayname": "Name", "description": "Short name for the question row.", "type": 10, "isoptional": false },
    { "uniquename": "Wording", "name": "Wording", "displayname": "Wording", "description": "Question text as the reviewer reads it.", "type": 10, "isoptional": false },
    { "uniquename": "ResponseType", "name": "ResponseType", "displayname": "Response type", "description": "Numeric al_responsetype option value.", "type": 10, "isoptional": false },
    { "uniquename": "Mandatory", "name": "Mandatory", "displayname": "Mandatory", "description": "'true' or 'false'. Absent is treated as true.", "type": 10, "isoptional": true },
    { "uniquename": "DisplayOrder", "name": "DisplayOrder", "displayname": "Display order", "description": "Optional integer order within the section. Absent appends after the highest in force.", "type": 10, "isoptional": true },
    { "uniquename": "EffectiveFrom", "name": "EffectiveFrom", "displayname": "Effective from", "description": "Optional yyyy-MM-dd. Absent is today. May not be in the past.", "type": 10, "isoptional": true },
    { "uniquename": "IdempotencyKey", "name": "IdempotencyKey", "displayname": "Idempotency key", "description": "Stable key for the intent; a replay returns the same question.", "type": 10, "isoptional": false }
  ],
  "responseProperties": [
    { "uniquename": "QuestionId", "name": "QuestionId", "displayname": "Question id", "description": "Id of the al_question created.", "type": 10 },
    { "uniquename": "VersionId", "name": "VersionId", "displayname": "Version id", "description": "Id of the al_questionversion created.", "type": 10 },
    { "uniquename": "AuditEventId", "name": "AuditEventId", "displayname": "Audit event id", "description": "Id of the Audit Event written.", "type": 10 },
    { "uniquename": "Conflict", "name": "Conflict", "displayname": "Conflict", "description": "True when an optimistic-concurrency conflict was detected.", "type": 0 }
  ]
}
```

- [ ] **Step 2: Write the failing tests**

The date rule is the part with a decision in it, so it is extracted and tested directly; the Dataverse writes are covered by the DEV exercise in Task 8.

Create `plugins/OutcomeTesting.Plugins.Tests/AddQuestionPluginTests.cs`:

```csharp
using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122: a question may be dated forward so in-flight reviews finish against the set
    /// they started with, but never backward — a question cannot retrospectively have been
    /// owed by a review that has already been answered.
    /// </summary>
    public class AddQuestionPluginTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 12);

        [Fact]
        public void An_absent_date_is_today()
        {
            Assert.Equal(Today, AddQuestionPlugin.ParseEffectiveFrom(null, Today));
            Assert.Equal(Today, AddQuestionPlugin.ParseEffectiveFrom("   ", Today));
        }

        [Fact]
        public void Today_is_accepted()
        {
            Assert.Equal(Today, AddQuestionPlugin.ParseEffectiveFrom("2026-09-12", Today));
        }

        [Fact]
        public void A_future_date_is_accepted()
        {
            Assert.Equal(
                new DateTime(2026, 10, 1),
                AddQuestionPlugin.ParseEffectiveFrom("2026-10-01", Today));
        }

        [Fact]
        public void A_past_date_is_refused()
        {
            var error = Assert.Throws<Microsoft.Xrm.Sdk.InvalidPluginExecutionException>(
                () => AddQuestionPlugin.ParseEffectiveFrom("2026-09-11", Today));

            Assert.Contains("in the past", error.Message);
        }

        [Fact]
        public void An_unparseable_date_is_refused_rather_than_defaulted()
        {
            // Defaulting here would silently put the question in force today when the
            // administrator asked for something else.
            Assert.Throws<Microsoft.Xrm.Sdk.InvalidPluginExecutionException>(
                () => AddQuestionPlugin.ParseEffectiveFrom("next Tuesday", Today));
        }

        [Fact]
        public void The_date_is_read_as_a_day_not_a_moment()
        {
            // Date-only columns compared against a timestamp are what AD-091 was raised to
            // stop; a time component is discarded rather than carried.
            Assert.Equal(
                new DateTime(2026, 10, 1),
                AddQuestionPlugin.ParseEffectiveFrom("2026-10-01T14:30:00", Today));
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter AddQuestionPluginTests
```

Expected: FAIL to compile — `The name 'AddQuestionPlugin' does not exist`.

- [ ] **Step 4: Write the plug-in**

Create `plugins/OutcomeTesting.Plugins/AddQuestionPlugin.cs`:

```csharp
using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AddQuestion (AD-003, AD-122). Creates an al_question and its
    /// first al_questionversion in one transaction, in the section named.
    ///
    /// The question is added to the checklist version already in force rather than by
    /// reissuing the checklist: effective dating is what protects history, because every
    /// read path is date-scoped and a submitted review reads as of its submission day
    /// (AD-091). A mandatory question in force today is owed by every unsubmitted review of
    /// that discipline at its next submit, which is what EffectiveFrom is for.
    /// </summary>
    public class AddQuestionPlugin : PluginBase
    {
        private const string InSectionId = "SectionId";
        private const string InQuestionCode = "QuestionCode";
        private const string InName = "Name";
        private const string InWording = "Wording";
        private const string InResponseType = "ResponseType";
        private const string InMandatory = "Mandatory";
        private const string InDisplayOrder = "DisplayOrder";
        private const string InEffectiveFrom = "EffectiveFrom";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutQuestionId = "QuestionId";
        private const string OutVersionId = "VersionId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const int CommandAddQuestion = 120910793;

        public AddQuestionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AddQuestionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var sectionId = CommandHelpers.ParseRequiredGuid(context, InSectionId);
            var questionCode = CommandHelpers.GetRequiredString(context, InQuestionCode).Trim();
            var name = CommandHelpers.GetRequiredString(context, InName).Trim();
            var wording = CommandHelpers.GetRequiredString(context, InWording).Trim();
            var responseTypeArg = CommandHelpers.GetRequiredString(context, InResponseType);
            var mandatoryArg = CommandHelpers.GetOptionalString(context, InMandatory);
            var displayOrderArg = CommandHelpers.GetOptionalString(context, InDisplayOrder);
            var effectiveFromArg = CommandHelpers.GetOptionalString(context, InEffectiveFrom);
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandAddQuestion);
            if (existingAudit != null)
            {
                SetResponse(
                    context,
                    existingAudit.GetAttributeValue<string>("al_details"),
                    existingAudit.GetAttributeValue<string>("al_targetid"),
                    existingAudit.Id);
                return;
            }

            int responseType;
            if (!int.TryParse(responseTypeArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out responseType))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Response type must be a numeric option value.");
            }

            var effectiveFrom = ParseEffectiveFrom(effectiveFromArg, DateTime.UtcNow.Date);
            var mandatory = string.IsNullOrWhiteSpace(mandatoryArg) || ReadBool(mandatoryArg);

            // The section must exist and still be in force: adding a question to a section
            // that has been dated out creates content no reviewer will ever see.
            var section = userService.Retrieve(
                "al_section", sectionId, new ColumnSet("al_effectivefrom", "al_effectiveto", "al_name"));
            if (!SectionRules.IsSectionEffective(
                section.GetAttributeValue<DateTime?>("al_effectivefrom"),
                section.GetAttributeValue<DateTime?>("al_effectiveto"),
                DateTime.UtcNow))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "That section is no longer part of the checklist, so a question cannot be added to it.");
            }

            EnsureCodeIsFree(userService, questionCode);

            var displayOrder = ResolveDisplayOrder(userService, sectionId, displayOrderArg);

            var question = new Entity(QuestionEntity)
            {
                ["al_name"] = name,
                ["al_questioncode"] = questionCode,
                ["al_displayorder"] = displayOrder,
                ["al_sectionid"] = new EntityReference("al_section", sectionId),
            };
            var questionId = userService.Create(question);

            var code = "QV-" + questionId.ToString("N") + "-v1";
            var version = new Entity(VersionEntity)
            {
                ["al_name"] = "Question version v1",
                ["al_questionversioncode"] = code,
                ["al_questiontext"] = wording,
                ["al_versionnumber"] = 1,
                ["al_effectivefrom"] = effectiveFrom,
                ["al_responsetype"] = new OptionSetValue(responseType),
                ["al_ismandatory"] = mandatory,
                ["al_displayorder"] = displayOrder,
                ["al_questionid"] = new EntityReference(QuestionEntity, questionId),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };
            var versionId = userService.Create(version);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandAddQuestion, "AddQuestion " + questionCode, QuestionEntity, questionId,
                "Added to section " + section.GetAttributeValue<string>("al_name"),
                versionId.ToString("D"), idempotencyKey, context);

            SetResponse(context, versionId.ToString("D"), questionId.ToString("D"), auditId);
        }

        /// <summary>
        /// The day this question starts being asked. Absent is today; a future day lets an
        /// administrator leave in-flight reviews on the set they started with; the past is
        /// refused, because a question cannot retrospectively have been owed by a review
        /// that has already been answered.
        ///
        /// Public and static so it can be tested directly — the assembly is signed and
        /// carries no InternalsVisibleTo.
        /// </summary>
        public static DateTime ParseEffectiveFrom(string value, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return today;
            }

            DateTime parsed;
            if (!DateTime.TryParse(
                value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective from must be a date in the form yyyy-MM-dd.");
            }

            // Read as a day, not a moment: these columns are date-only, and comparing one
            // against a timestamp is what AD-091 was raised to stop.
            var day = parsed.Date;
            if (day < today.Date)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective from cannot be in the past, because a question cannot have been owed by a review already answered.");
            }

            return day;
        }

        private static bool ReadBool(string value)
        {
            bool parsed;
            if (!bool.TryParse(value, out parsed))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "Mandatory must be 'true' or 'false'.");
            }

            return parsed;
        }

        /// <summary>
        /// al_questioncode carries the al_questioncodekey alternate key, so a duplicate
        /// collides at the platform. Caught here so the caller reads a sentence about the
        /// code rather than a key-violation stack.
        /// </summary>
        private static void EnsureCodeIsFree(IOrganizationService service, string questionCode)
        {
            var query = new QueryExpression(QuestionEntity)
            {
                ColumnSet = new ColumnSet("al_questionid"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questioncode", ConditionOperator.Equal, questionCode);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Question code '" + questionCode + "' is already in use.");
            }
        }

        /// <summary>Explicit order where given, otherwise after the highest in the section.</summary>
        private static int ResolveDisplayOrder(
            IOrganizationService service, Guid sectionId, string displayOrderArg)
        {
            int explicitOrder;
            if (!string.IsNullOrWhiteSpace(displayOrderArg)
                && int.TryParse(displayOrderArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out explicitOrder))
            {
                return explicitOrder;
            }

            var query = new QueryExpression(QuestionEntity)
            {
                ColumnSet = new ColumnSet("al_displayorder"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_sectionid", ConditionOperator.Equal, sectionId);
            query.AddOrder("al_displayorder", OrderType.Descending);

            var highest = service.RetrieveMultiple(query).Entities;
            return highest.Count == 0 ? 1 : highest[0].GetAttributeValue<int>("al_displayorder") + 1;
        }

        private static void SetResponse(
            IPluginExecutionContext context, string versionId, string questionId, Guid auditId)
        {
            context.OutputParameters[OutQuestionId] = questionId;
            context.OutputParameters[OutVersionId] = versionId;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = false;
        }
    }
}
```

- [ ] **Step 5: Run the tests, then the suite**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter AddQuestionPluginTests
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
```

Expected: 6 pass, then 724 total.

- [ ] **Step 6: Commit**

```bash
git add plugins/customapi/al_AddQuestion.customapi.json plugins/OutcomeTesting.Plugins/AddQuestionPlugin.cs plugins/OutcomeTesting.Plugins.Tests/AddQuestionPluginTests.cs
git commit -m "feat(checklist): al_AddQuestion

Creates the question and its first version in one transaction. EffectiveFrom may
be today or later and never the past - a question cannot retrospectively have
been owed by a review already answered - and an unparseable date is refused
rather than quietly defaulted to today.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: `al_RetireQuestion`

Takes a question out of service by dating out its current version and creating no successor. Refuses the eight protected codes.

**Files:**
- Create: `plugins/customapi/al_RetireQuestion.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/RetireQuestionPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/RetireQuestionPluginTests.cs`

**Interfaces:**
- Consumes: `ChecklistGuards.ProtectedReason`, `CommandHelpers.*`, `PermissionHelpers.*`
- Produces: Custom API `al_RetireQuestion`, audit command **120910794**, `RetireQuestionPlugin.ParseEffectiveTo(string, DateTime) -> DateTime`

- [ ] **Step 1: Write the contract**

Create `plugins/customapi/al_RetireQuestion.customapi.json`, copying the `al_AddQuestion` file's shape and changing `uniquename`, `name`, `displayname`, `description` and `pluginType` to `al_RetireQuestion` / `Retire Question` / `OutcomeTesting.Plugins.RetireQuestionPlugin`, with these parameters:

| Request parameter | Type | Optional | Description |
|---|---|---|---|
| `QuestionId` | 10 | no | Id of the al_question to retire. |
| `EffectiveTo` | 10 | yes | Optional yyyy-MM-dd. Absent is today. May not be in the past. |
| `Reason` | 10 | **no** | Why the question is no longer asked. Recorded on the Audit Event. |
| `IdempotencyKey` | 10 | no | Stable key for the intent; a replay returns the same result. |

| Response property | Type | Description |
|---|---|---|
| `QuestionId` | 10 | Id of the al_question retired. |
| `RetiredVersionId` | 10 | Id of the al_questionversion dated out. |
| `AuditEventId` | 10 | Id of the Audit Event written. |
| `Conflict` | 0 | True when an optimistic-concurrency conflict was detected. |

- [ ] **Step 2: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/RetireQuestionPluginTests.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122. Retiring may be dated forward but never backward: a question that was in
    /// force when a review answered it must stay so, or the review's own history changes.
    /// </summary>
    public class RetireQuestionPluginTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 12);

        [Fact]
        public void An_absent_date_retires_today()
        {
            Assert.Equal(Today, RetireQuestionPlugin.ParseEffectiveTo(null, Today));
        }

        [Fact]
        public void A_future_date_is_accepted()
        {
            Assert.Equal(
                new DateTime(2026, 10, 1),
                RetireQuestionPlugin.ParseEffectiveTo("2026-10-01", Today));
        }

        [Fact]
        public void A_past_date_is_refused()
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => RetireQuestionPlugin.ParseEffectiveTo("2026-09-01", Today));

            Assert.Contains("in the past", error.Message);
        }

        [Fact]
        public void An_unparseable_date_is_refused_rather_than_defaulted()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => RetireQuestionPlugin.ParseEffectiveTo("soon", Today));
        }
    }
}
```

- [ ] **Step 3: Run to verify failure**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter RetireQuestionPluginTests
```

Expected: FAIL to compile — `The name 'RetireQuestionPlugin' does not exist`.

- [ ] **Step 4: Write the plug-in**

Create `plugins/OutcomeTesting.Plugins/RetireQuestionPlugin.cs`:

```csharp
using System;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command RetireQuestion (AD-003, AD-122). Stamps al_effectiveto on the
    /// question's current version and creates no successor, which is the whole difference
    /// from RetireAndSucceedQuestion.
    ///
    /// The version stays Active: statecode is never consulted (AD-091), so every answer
    /// already recorded against it keeps resolving. The question simply stops being asked.
    /// </summary>
    public class RetireQuestionPlugin : PluginBase
    {
        private const string InQuestionId = "QuestionId";
        private const string InEffectiveTo = "EffectiveTo";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string OutQuestionId = "QuestionId";
        private const string OutRetiredVersionId = "RetiredVersionId";
        private const string OutAuditEventId = "AuditEventId";
        private const string OutConflict = "Conflict";

        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const int CommandRetireQuestion = 120910794;

        public RetireQuestionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(RetireQuestionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var questionId = CommandHelpers.ParseRequiredGuid(context, InQuestionId);
            var effectiveToArg = CommandHelpers.GetOptionalString(context, InEffectiveTo);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandRetireQuestion);
            if (existingAudit != null)
            {
                SetResponse(
                    context, questionId.ToString("D"),
                    existingAudit.GetAttributeValue<string>("al_targetid"), existingAudit.Id);
                return;
            }

            var question = userService.Retrieve(
                QuestionEntity, questionId, new ColumnSet("al_questioncode", "al_name"));
            var questionCode = question.GetAttributeValue<string>("al_questioncode");

            // Some questions are read by code, not only by reviewers. Retiring one does not
            // degrade the checklist, it stops the system producing outcomes (AD-122).
            var protectedReason = ChecklistGuards.ProtectedReason(questionCode);
            if (protectedReason != null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + protectedReason +
                    " It cannot be retired without a code change.");
            }

            var effectiveTo = ParseEffectiveTo(effectiveToArg, DateTime.UtcNow.Date);
            var current = CurrentVersion(userService, questionId);
            if (current == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "That question has no version to retire.");
            }

            userService.Update(new Entity(VersionEntity, current.Id) { ["al_effectiveto"] = effectiveTo });

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandRetireQuestion, "RetireQuestion " + questionCode,
                VersionEntity, current.Id, reason,
                effectiveTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                idempotencyKey, context);

            SetResponse(context, questionId.ToString("D"), current.Id.ToString("D"), auditId);
        }

        /// <summary>
        /// The day the question stops being asked. Absent is today; later is allowed so a
        /// change can be announced ahead of time; earlier is refused, because a question
        /// that was in force when a review answered it must stay so.
        /// </summary>
        public static DateTime ParseEffectiveTo(string value, DateTime today)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return today;
            }

            DateTime parsed;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective to must be a date in the form yyyy-MM-dd.");
            }

            var day = parsed.Date;
            if (day < today.Date)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Effective to cannot be in the past, because a question that was in force when a review answered it must stay so.");
            }

            return day;
        }

        private static Entity CurrentVersion(IOrganizationService service, Guid questionId)
        {
            var query = new QueryExpression(VersionEntity)
            {
                ColumnSet = new ColumnSet("al_versionnumber", "al_effectiveto"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questionid", ConditionOperator.Equal, questionId);
            query.AddOrder("al_versionnumber", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0] : null;
        }

        private static void SetResponse(
            IPluginExecutionContext context, string questionId, string versionId, Guid auditId)
        {
            context.OutputParameters[OutQuestionId] = questionId;
            context.OutputParameters[OutRetiredVersionId] = versionId;
            context.OutputParameters[OutAuditEventId] = auditId.ToString("D");
            context.OutputParameters[OutConflict] = false;
        }
    }
}
```

- [ ] **Step 5: Run the tests, then the suite, then commit**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter RetireQuestionPluginTests
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
git add plugins/customapi/al_RetireQuestion.customapi.json plugins/OutcomeTesting.Plugins/RetireQuestionPlugin.cs plugins/OutcomeTesting.Plugins.Tests/RetireQuestionPluginTests.cs
git commit -m "feat(checklist): al_RetireQuestion"
```

Expected: 4 pass, then 728 total.

---

### Task 4: `al_MoveQuestion`

A move is a retire plus an add, in one transaction. An in-place change to `al_question.al_sectionid` would re-file every historic answer under a section it was never answered in — for every submitted review at once.

**Files:**
- Create: `plugins/customapi/al_MoveQuestion.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/MoveQuestionPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/MoveQuestionPluginTests.cs`

**Interfaces:**
- Consumes: `ChecklistGuards.ProtectedReason`, `SectionRules.IsSectionEffective`, `AddQuestionPlugin.ParseEffectiveFrom`, `CommandHelpers.*`, `PermissionHelpers.*`
- Produces: Custom API `al_MoveQuestion`, audit command **120910795**, `MoveQuestionPlugin.RefusalFor(string questionCode, Guid fromSection, Guid toSection) -> string`

- [ ] **Step 1: Write the contract**

Create `plugins/customapi/al_MoveQuestion.customapi.json` to the same shape, `pluginType` `OutcomeTesting.Plugins.MoveQuestionPlugin`:

| Request parameter | Type | Optional | Description |
|---|---|---|---|
| `QuestionId` | 10 | no | Id of the al_question to move. |
| `TargetSectionId` | 10 | no | Id of the al_section to move it to. |
| `NewQuestionCode` | 10 | no | Code for the question in its new section; codes are unique. |
| `EffectiveFrom` | 10 | yes | Optional yyyy-MM-dd for the new question. Absent is today. |
| `Reason` | 10 | **no** | Why the question is moving. Recorded on the Audit Event. |
| `IdempotencyKey` | 10 | no | Stable key for the intent. |

| Response property | Type | Description |
|---|---|---|
| `RetiredVersionId` | 10 | Id of the al_questionversion dated out in the old section. |
| `NewQuestionId` | 10 | Id of the al_question created in the target section. |
| `NewVersionId` | 10 | Id of its first al_questionversion. |
| `AuditEventId` | 10 | Id of the Audit Event written. |
| `Conflict` | 0 | True when an optimistic-concurrency conflict was detected. |

- [ ] **Step 2: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/MoveQuestionPluginTests.cs`:

```csharp
using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122. A move retires the question where it was and creates it where it is going,
    /// so history stays attached to the section it was answered in. Protected codes are
    /// refused, because a move retires the original just as surely as a retire does.
    /// </summary>
    public class MoveQuestionPluginTests
    {
        private static readonly Guid Tax = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private static readonly Guid Aqs = Guid.Parse("22222222-2222-4222-8222-222222222222");

        [Fact]
        public void An_ordinary_question_may_move()
        {
            Assert.Null(MoveQuestionPlugin.RefusalFor("Q-E1-01", Tax, Aqs));
        }

        [Fact]
        public void A_protected_question_may_not_move()
        {
            var refusal = MoveQuestionPlugin.RefusalFor("Q-GR-01", Tax, Aqs);

            Assert.NotNull(refusal);
            Assert.Contains("Q-GR-01", refusal);
        }

        [Fact]
        public void A_move_to_the_section_it_is_already_in_is_refused()
        {
            // Otherwise the question is retired and recreated for no change, and the old
            // code is burned - codes are unique and never freed.
            var refusal = MoveQuestionPlugin.RefusalFor("Q-E1-01", Tax, Tax);

            Assert.NotNull(refusal);
            Assert.Contains("already in", refusal);
        }
    }
}
```

- [ ] **Step 3: Run to verify failure**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter MoveQuestionPluginTests
```

Expected: FAIL to compile — `The name 'MoveQuestionPlugin' does not exist`.

- [ ] **Step 4: Write the plug-in**

Create `plugins/OutcomeTesting.Plugins/MoveQuestionPlugin.cs`. It is `RetireQuestionPlugin` followed by `AddQuestionPlugin`, in one plug-in execution and therefore one transaction, so a move cannot half-complete and leave a question retired in one section and absent from the other.

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command MoveQuestion (AD-003, AD-122). Retires the question in its old
    /// section and creates it in the target, carrying the same wording, response type and
    /// mandatory flag.
    ///
    /// Not a lookup update. al_sectionid lives on al_question and is not versioned, so
    /// changing it in place re-files every answer ever recorded - for every submitted
    /// review at once - under a section they were never answered in.
    ///
    /// A move between sections owned by different roles changes which discipline owes the
    /// question. That is the point of the action and is not guarded; the caller states it.
    /// </summary>
    public class MoveQuestionPlugin : PluginBase
    {
        private const string InQuestionId = "QuestionId";
        private const string InTargetSectionId = "TargetSectionId";
        private const string InNewQuestionCode = "NewQuestionCode";
        private const string InEffectiveFrom = "EffectiveFrom";
        private const string InReason = "Reason";
        private const string InIdempotencyKey = "IdempotencyKey";

        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const int CommandMoveQuestion = 120910795;

        public MoveQuestionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(MoveQuestionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var questionId = CommandHelpers.ParseRequiredGuid(context, InQuestionId);
            var targetSectionId = CommandHelpers.ParseRequiredGuid(context, InTargetSectionId);
            var newCode = CommandHelpers.GetRequiredString(context, InNewQuestionCode).Trim();
            var effectiveFromArg = CommandHelpers.GetOptionalString(context, InEffectiveFrom);
            var reason = CommandHelpers.GetRequiredString(context, InReason).Trim();
            var idempotencyKey = CommandHelpers.GetRequiredString(context, InIdempotencyKey);

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandMoveQuestion);
            if (existingAudit != null)
            {
                context.OutputParameters["NewQuestionId"] = existingAudit.GetAttributeValue<string>("al_targetid");
                context.OutputParameters["NewVersionId"] = existingAudit.GetAttributeValue<string>("al_details");
                context.OutputParameters["RetiredVersionId"] = string.Empty;
                context.OutputParameters["AuditEventId"] = existingAudit.Id.ToString("D");
                context.OutputParameters["Conflict"] = false;
                return;
            }

            var question = userService.Retrieve(
                QuestionEntity, questionId,
                new ColumnSet("al_questioncode", "al_name", "al_sectionid", "al_displayorder"));
            var questionCode = question.GetAttributeValue<string>("al_questioncode");
            var fromSection = question.GetAttributeValue<EntityReference>("al_sectionid");

            var refusal = RefusalFor(
                questionCode, fromSection == null ? Guid.Empty : fromSection.Id, targetSectionId);
            if (refusal != null)
            {
                throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
            }

            var target = userService.Retrieve(
                "al_section", targetSectionId, new ColumnSet("al_effectivefrom", "al_effectiveto", "al_name"));
            if (!SectionRules.IsSectionEffective(
                target.GetAttributeValue<DateTime?>("al_effectivefrom"),
                target.GetAttributeValue<DateTime?>("al_effectiveto"),
                DateTime.UtcNow))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "The target section is no longer part of the checklist.");
            }

            var today = DateTime.UtcNow.Date;
            var effectiveFrom = AddQuestionPlugin.ParseEffectiveFrom(effectiveFromArg, today);

            // Retire where it was. The version stays Active so its answers keep resolving.
            var current = CurrentVersion(userService, questionId);
            if (current == null)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix + "That question has no version to move.");
            }

            userService.Update(new Entity(VersionEntity, current.Id) { ["al_effectiveto"] = today });

            // Create it where it is going, carrying the frozen answer shape forward.
            var newQuestion = new Entity(QuestionEntity)
            {
                ["al_name"] = question.GetAttributeValue<string>("al_name"),
                ["al_questioncode"] = newCode,
                ["al_displayorder"] = question.GetAttributeValue<int>("al_displayorder"),
                ["al_sectionid"] = new EntityReference("al_section", targetSectionId),
            };
            var newQuestionId = userService.Create(newQuestion);

            var source = userService.Retrieve(
                VersionEntity, current.Id,
                new ColumnSet("al_questiontext", "al_responsetype", "al_ismandatory", "al_displayorder"));

            var newVersion = new Entity(VersionEntity)
            {
                ["al_name"] = "Question version v1",
                ["al_questionversioncode"] = "QV-" + newQuestionId.ToString("N") + "-v1",
                ["al_questiontext"] = source.GetAttributeValue<string>("al_questiontext"),
                ["al_versionnumber"] = 1,
                ["al_effectivefrom"] = effectiveFrom,
                ["al_responsetype"] = source.GetAttributeValue<OptionSetValue>("al_responsetype"),
                ["al_ismandatory"] = source.GetAttributeValue<bool>("al_ismandatory"),
                ["al_displayorder"] = source.GetAttributeValue<int>("al_displayorder"),
                ["al_questionid"] = new EntityReference(QuestionEntity, newQuestionId),
                ["statecode"] = new OptionSetValue(0),
                ["statuscode"] = new OptionSetValue(1),
            };
            var newVersionId = userService.Create(newVersion);

            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandMoveQuestion,
                "MoveQuestion " + questionCode + " -> " + newCode,
                QuestionEntity, newQuestionId,
                reason + " (moved to " + target.GetAttributeValue<string>("al_name") + ")",
                newVersionId.ToString("D"), idempotencyKey, context);

            context.OutputParameters["RetiredVersionId"] = current.Id.ToString("D");
            context.OutputParameters["NewQuestionId"] = newQuestionId.ToString("D");
            context.OutputParameters["NewVersionId"] = newVersionId.ToString("D");
            context.OutputParameters["AuditEventId"] = auditId.ToString("D");
            context.OutputParameters["Conflict"] = false;
        }

        /// <summary>
        /// Why this question cannot move, or null when it can. A protected code is refused
        /// because a move retires the original just as surely as a retire does; a move to
        /// the section it is already in is refused because it would burn the old code -
        /// codes are unique and never freed - for no change.
        /// </summary>
        public static string RefusalFor(string questionCode, Guid fromSectionId, Guid toSectionId)
        {
            var protectedReason = ChecklistGuards.ProtectedReason(questionCode);
            if (protectedReason != null)
            {
                return protectedReason + " It cannot be moved without a code change.";
            }

            if (fromSectionId != Guid.Empty && fromSectionId == toSectionId)
            {
                return "That question is already in the section you are moving it to.";
            }

            return null;
        }

        private static Entity CurrentVersion(IOrganizationService service, Guid questionId)
        {
            var query = new QueryExpression(VersionEntity)
            {
                ColumnSet = new ColumnSet("al_versionnumber"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_questionid", ConditionOperator.Equal, questionId);
            query.AddOrder("al_versionnumber", OrderType.Descending);

            var found = service.RetrieveMultiple(query).Entities;
            return found.Count > 0 ? found[0] : null;
        }
    }
}
```

- [ ] **Step 5: Run the tests, then the suite, then commit**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter MoveQuestionPluginTests
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
git add plugins/customapi/al_MoveQuestion.customapi.json plugins/OutcomeTesting.Plugins/MoveQuestionPlugin.cs plugins/OutcomeTesting.Plugins.Tests/MoveQuestionPluginTests.cs
git commit -m "feat(checklist): al_MoveQuestion, as retire-plus-add in one transaction"
```

Expected: 3 pass, then 731 total.

---

### Task 5: `al_AddSection`, with its questions

A section arrives complete rather than as an empty shell. `Questions` is a JSON array in a string parameter — Custom API parameters are scalars, and this codebase already passes numbers as strings — and the whole thing commits in one transaction, so a section is never created with half its questions.

**Files:**
- Create: `plugins/customapi/al_AddSection.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/AddSectionPlugin.cs`
- Create: `plugins/OutcomeTesting.Plugins/SectionQuestionSpec.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/SectionQuestionSpecTests.cs`

**Interfaces:**
- Consumes: `CommandHelpers.*`, `PermissionHelpers.*`, `AddQuestionPlugin.ParseEffectiveFrom`
- Produces: Custom API `al_AddSection`, audit command **120910796**, `SectionQuestionSpec.ParseMany(string json) -> IList<SectionQuestionSpec>` with fields `Code`, `Name`, `Wording`, `ResponseType`, `Mandatory`, `DisplayOrder`

**Contract** — `pluginType` `OutcomeTesting.Plugins.AddSectionPlugin`:

| Request parameter | Type | Optional | Description |
|---|---|---|---|
| `SectionCode` | 10 | no | Unique code, for example `S-CD2`. |
| `Name` | 10 | no | Section name as the reviewer reads it. |
| `HelpText` | 10 | yes | Optional guidance shown under the heading. |
| `OwnerRole` | 10 | no | `120910100` Tax, `120910101` AQS, or `120910105` Both. |
| `DisplayOrder` | 10 | yes | Optional integer. Absent appends after the highest in the version. |
| `IsOptional` | 10 | yes | `'true'` or `'false'`. Absent is false. |
| `EffectiveFrom` | 10 | yes | Optional yyyy-MM-dd. Absent is today. May not be in the past. |
| `Questions` | 10 | yes | JSON array of `{code, name, wording, responseType, mandatory, displayOrder}`. |
| `IdempotencyKey` | 10 | no | Stable key for the intent. |

| Response property | Type | Description |
|---|---|---|
| `SectionId` | 10 | Id of the al_section created. |
| `QuestionIds` | 10 | Comma-separated ids of the questions created, in order. |
| `AuditEventId` | 10 | Id of the Audit Event written. |
| `Conflict` | 0 | True when an optimistic-concurrency conflict was detected. |

- [ ] **Step 1: Write the failing tests for the parser**

The parser is where the decisions are, so it is a class of its own and tested directly. Create `plugins/OutcomeTesting.Plugins.Tests/SectionQuestionSpecTests.cs`:

```csharp
using System.Linq;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-122: a section may be created with its questions in one call. The array is
    /// validated in full before anything is written, so a bad element cannot leave a
    /// section half-populated.
    /// </summary>
    public class SectionQuestionSpecTests
    {
        [Fact]
        public void An_absent_or_empty_array_yields_no_questions()
        {
            Assert.Empty(SectionQuestionSpec.ParseMany(null));
            Assert.Empty(SectionQuestionSpec.ParseMany("   "));
            Assert.Empty(SectionQuestionSpec.ParseMany("[]"));
        }

        [Fact]
        public void A_well_formed_element_is_read_in_full()
        {
            var specs = SectionQuestionSpec.ParseMany(
                "[{\"code\":\"Q-CD2-01\",\"name\":\"Fair value\",\"wording\":\"Is fair value evidenced?\",\"responseType\":120910009,\"mandatory\":true,\"displayOrder\":2}]");

            var only = Assert.Single(specs);
            Assert.Equal("Q-CD2-01", only.Code);
            Assert.Equal("Fair value", only.Name);
            Assert.Equal("Is fair value evidenced?", only.Wording);
            Assert.Equal(120910009, only.ResponseType);
            Assert.True(only.Mandatory);
            Assert.Equal(2, only.DisplayOrder);
        }

        [Fact]
        public void Order_defaults_to_the_position_in_the_array()
        {
            var specs = SectionQuestionSpec.ParseMany(
                "[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}," +
                "{\"code\":\"Q-B\",\"name\":\"B\",\"wording\":\"B?\",\"responseType\":120910000}]");

            Assert.Equal(new[] { 1, 2 }, specs.Select(s => s.DisplayOrder).ToArray());
        }

        [Fact]
        public void Mandatory_defaults_to_true()
        {
            // AD-019: every displayed answerable question is mandatory unless its section
            // is optional. Defaulting to false here would quietly invert that.
            var specs = SectionQuestionSpec.ParseMany(
                "[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}]");

            Assert.True(specs[0].Mandatory);
        }

        [Theory]
        [InlineData("[{\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}]", "code")]
        [InlineData("[{\"code\":\"Q-A\",\"wording\":\"A?\",\"responseType\":120910000}]", "name")]
        [InlineData("[{\"code\":\"Q-A\",\"name\":\"A\",\"responseType\":120910000}]", "wording")]
        [InlineData("[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\"}]", "responseType")]
        public void A_missing_required_field_is_refused_by_name(string json, string field)
        {
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany(json));

            Assert.Contains(field, error.Message);
        }

        [Fact]
        public void A_duplicate_code_within_the_array_is_refused()
        {
            // The alternate key would catch this at the second Create, after the section
            // and the first question were already written.
            var error = Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany(
                    "[{\"code\":\"Q-A\",\"name\":\"A\",\"wording\":\"A?\",\"responseType\":120910000}," +
                    "{\"code\":\"Q-A\",\"name\":\"B\",\"wording\":\"B?\",\"responseType\":120910000}]"));

            Assert.Contains("Q-A", error.Message);
        }

        [Fact]
        public void Malformed_json_is_refused_rather_than_ignored()
        {
            Assert.Throws<InvalidPluginExecutionException>(
                () => SectionQuestionSpec.ParseMany("{not json"));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter SectionQuestionSpecTests
```

Expected: FAIL to compile — `The name 'SectionQuestionSpec' does not exist`.

- [ ] **Step 3: Write the parser**

The plug-in project targets net462 and the assembly is signed, so do **not** add a JSON package. Check first whether `System.Runtime.Serialization.Json.DataContractJsonSerializer` is already referenced anywhere in the assembly:

```bash
grep -rn "DataContractJsonSerializer\|Newtonsoft\|System.Text.Json" plugins/OutcomeTesting.Plugins/*.cs plugins/OutcomeTesting.Plugins/*.csproj
```

If nothing comes back, use `DataContractJsonSerializer` from the framework — it needs only a `System.Runtime.Serialization` reference, which is in the box for net462 and adds no dependency to a signed plug-in assembly.

Create `plugins/OutcomeTesting.Plugins/SectionQuestionSpec.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.Xrm.Sdk;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// One question as AddSection receives it (AD-122). The whole array is parsed and
    /// validated before anything is written, so a bad element refuses the call rather than
    /// leaving a section with half its questions.
    ///
    /// DataContractJsonSerializer rather than a package: the plug-in assembly is signed and
    /// targets net462, and adding a dependency to it is a deployment problem, not a coding
    /// convenience.
    /// </summary>
    [DataContract]
    public class SectionQuestionSpec
    {
        [DataMember(Name = "code")]
        public string Code { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "wording")]
        public string Wording { get; set; }

        [DataMember(Name = "responseType")]
        public int? ResponseType { get; set; }

        [DataMember(Name = "mandatory")]
        public bool? MandatoryRaw { get; set; }

        [DataMember(Name = "displayOrder")]
        public int? DisplayOrderRaw { get; set; }

        /// <summary>AD-019: mandatory unless the element says otherwise.</summary>
        public bool Mandatory
        {
            get { return !MandatoryRaw.HasValue || MandatoryRaw.Value; }
        }

        /// <summary>Position in the array where the element does not say.</summary>
        public int DisplayOrder { get; set; }

        public static IList<SectionQuestionSpec> ParseMany(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<SectionQuestionSpec>();
            }

            SectionQuestionSpec[] parsed;
            try
            {
                var serialiser = new DataContractJsonSerializer(typeof(SectionQuestionSpec[]));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    parsed = (SectionQuestionSpec[])serialiser.ReadObject(stream);
                }
            }
            catch (Exception error)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "The questions could not be read as JSON: " + error.Message);
            }

            var specs = new List<SectionQuestionSpec>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < (parsed == null ? 0 : parsed.Length); index++)
            {
                var spec = parsed[index];
                var position = (index + 1).ToString(CultureInfo.InvariantCulture);

                Require(spec.Code, "code", position);
                Require(spec.Name, "name", position);
                Require(spec.Wording, "wording", position);
                if (!spec.ResponseType.HasValue)
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "Question " + position + " has no responseType.");
                }

                spec.Code = spec.Code.Trim();
                if (!seen.Add(spec.Code))
                {
                    throw new InvalidPluginExecutionException(
                        CommandHelpers.PreconditionPrefix +
                        "Question code '" + spec.Code + "' appears twice in the same section.");
                }

                spec.DisplayOrder = spec.DisplayOrderRaw.HasValue ? spec.DisplayOrderRaw.Value : index + 1;
                specs.Add(spec);
            }

            return specs;
        }

        private static void Require(string value, string field, string position)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Question " + position + " has no " + field + ".");
            }
        }
    }
}
```

- [ ] **Step 4: Run the parser tests**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter SectionQuestionSpecTests
```

Expected: PASS, 10 tests. If `DataContractJsonSerializer` rejects `int?` members, change `ResponseType` to `int` with a sentinel of `0` and test for `0` rather than null — do not add a JSON package to a signed assembly.

- [ ] **Step 5: Write the plug-in**

Create `plugins/OutcomeTesting.Plugins/AddSectionPlugin.cs`. It resolves the checklist version in force the same way `ClaimCasePlugin.ResolveChecklistVersion` does, so the administrator never picks a version; creates the section; then creates each question and its v1.

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Server-side command AddSection (AD-003, AD-123). Creates a section under the
    /// checklist version in force, with its questions, in one transaction.
    ///
    /// A new section renders in both front ends without a code change: formBlocks falls
    /// through to a generic block and the portal template defaults its title and layout
    /// before its dispatch chain. It will not be folded into one of the document's designed
    /// blocks - that mapping is hand-written (AD-098) and stays so.
    /// </summary>
    public class AddSectionPlugin : PluginBase
    {
        private const string SectionEntity = "al_section";
        private const string QuestionEntity = "al_question";
        private const string VersionEntity = "al_questionversion";
        private const int CommandAddSection = 120910796;

        public AddSectionPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AddSectionPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var userService = localPluginContext.InitiatingUserService;
            var systemService = localPluginContext.PluginUserService;

            var sectionCode = CommandHelpers.GetRequiredString(context, "SectionCode").Trim();
            var name = CommandHelpers.GetRequiredString(context, "Name").Trim();
            var helpText = CommandHelpers.GetOptionalString(context, "HelpText");
            var ownerRoleArg = CommandHelpers.GetRequiredString(context, "OwnerRole");
            var displayOrderArg = CommandHelpers.GetOptionalString(context, "DisplayOrder");
            var isOptionalArg = CommandHelpers.GetOptionalString(context, "IsOptional");
            var effectiveFromArg = CommandHelpers.GetOptionalString(context, "EffectiveFrom");
            var questionsArg = CommandHelpers.GetOptionalString(context, "Questions");
            var idempotencyKey = CommandHelpers.GetRequiredString(context, "IdempotencyKey");

            PermissionHelpers.EnsureAppPermission(
                systemService, context, "question.retire", PermissionHelpers.AccessEdit);

            var existingAudit = CommandHelpers.FindAuditByKey(systemService, idempotencyKey, CommandAddSection);
            if (existingAudit != null)
            {
                context.OutputParameters["SectionId"] = existingAudit.GetAttributeValue<string>("al_targetid");
                context.OutputParameters["QuestionIds"] = existingAudit.GetAttributeValue<string>("al_details");
                context.OutputParameters["AuditEventId"] = existingAudit.Id.ToString("D");
                context.OutputParameters["Conflict"] = false;
                return;
            }

            int ownerRole;
            if (!int.TryParse(ownerRoleArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out ownerRole)
                || !IsAssignableOwnerRole(ownerRole))
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Owner role must be Tax (120910100), AQS (120910101) or Both (120910105).");
            }

            // Parsed before anything is written, so a bad element refuses the call rather
            // than leaving a section with half its questions.
            var questions = SectionQuestionSpec.ParseMany(questionsArg);

            var today = DateTime.UtcNow.Date;
            var effectiveFrom = AddQuestionPlugin.ParseEffectiveFrom(effectiveFromArg, today);
            var isOptional = !string.IsNullOrWhiteSpace(isOptionalArg)
                && string.Equals(isOptionalArg, "true", StringComparison.OrdinalIgnoreCase);

            EnsureSectionCodeIsFree(userService, sectionCode);

            var checklistVersionId = ResolveChecklistVersion(userService);
            var displayOrder = ResolveDisplayOrder(userService, checklistVersionId, displayOrderArg);

            var section = new Entity(SectionEntity)
            {
                ["al_name"] = name,
                ["al_sectioncode"] = sectionCode,
                ["al_ownerrole"] = new OptionSetValue(ownerRole),
                ["al_displayorder"] = displayOrder,
                ["al_isconditional"] = false,
                ["al_isoptional"] = isOptional,
                ["al_effectivefrom"] = effectiveFrom,
                ["al_checklistversionid"] = new EntityReference("al_checklistversion", checklistVersionId),
            };
            if (!string.IsNullOrWhiteSpace(helpText))
            {
                section["al_helptext"] = helpText;
            }

            var sectionId = userService.Create(section);

            var createdIds = new List<string>();
            foreach (var spec in questions)
            {
                var questionId = userService.Create(new Entity(QuestionEntity)
                {
                    ["al_name"] = spec.Name.Trim(),
                    ["al_questioncode"] = spec.Code,
                    ["al_displayorder"] = spec.DisplayOrder,
                    ["al_sectionid"] = new EntityReference(SectionEntity, sectionId),
                });

                userService.Create(new Entity(VersionEntity)
                {
                    ["al_name"] = "Question version v1",
                    ["al_questionversioncode"] = "QV-" + questionId.ToString("N") + "-v1",
                    ["al_questiontext"] = spec.Wording.Trim(),
                    ["al_versionnumber"] = 1,
                    ["al_effectivefrom"] = effectiveFrom,
                    ["al_responsetype"] = new OptionSetValue(spec.ResponseType.Value),
                    ["al_ismandatory"] = spec.Mandatory,
                    ["al_displayorder"] = spec.DisplayOrder,
                    ["al_questionid"] = new EntityReference(QuestionEntity, questionId),
                    ["statecode"] = new OptionSetValue(0),
                    ["statuscode"] = new OptionSetValue(1),
                });

                createdIds.Add(questionId.ToString("D"));
            }

            var questionIds = string.Join(",", createdIds.ToArray());
            var auditId = CommandHelpers.WriteAuditEvent(
                systemService, CommandAddSection, "AddSection " + sectionCode,
                SectionEntity, sectionId,
                "Owner role " + ownerRole + ", " + createdIds.Count + " question(s)",
                questionIds, idempotencyKey, context);

            context.OutputParameters["SectionId"] = sectionId.ToString("D");
            context.OutputParameters["QuestionIds"] = questionIds;
            context.OutputParameters["AuditEventId"] = auditId.ToString("D");
            context.OutputParameters["Conflict"] = false;
        }

        /// <summary>
        /// Tax, AQS or Both. Adviser, T&amp;C Manager and Manager / Admin are valid owner
        /// roles that no review is ever opened as, so a section owned by one is owed by
        /// nobody - refused rather than created as something invisible.
        /// </summary>
        private static bool IsAssignableOwnerRole(int ownerRole)
        {
            return ownerRole == ResponseRules.OwnerRoleTaxTeam
                || ownerRole == ResponseRules.OwnerRoleAqsChecker
                || ownerRole == SectionRules.OwnerRoleBoth;
        }

        private static void EnsureSectionCodeIsFree(IOrganizationService service, string sectionCode)
        {
            var query = new QueryExpression(SectionEntity)
            {
                ColumnSet = new ColumnSet("al_sectionid"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_sectioncode", ConditionOperator.Equal, sectionCode);

            if (service.RetrieveMultiple(query).Entities.Count > 0)
            {
                throw new InvalidPluginExecutionException(
                    CommandHelpers.PreconditionPrefix +
                    "Section code '" + sectionCode + "' is already in use.");
            }
        }

        /// <summary>
        /// The checklist version in force today, resolved as ClaimCasePlugin resolves it:
        /// the window is applied in memory rather than in the query, because a date-range
        /// condition would put the answer at the mercy of how the platform compares a
        /// date-only column to a UTC timestamp.
        /// </summary>
        private static Guid ResolveChecklistVersion(IOrganizationService service)
        {
            var query = new QueryExpression("al_checklistversion")
            {
                ColumnSet = new ColumnSet("al_effectivefrom", "al_effectiveto"),
                Criteria = new FilterExpression
                {
                    Conditions = { new ConditionExpression("statecode", ConditionOperator.Equal, 0) },
                },
                Orders = { new OrderExpression("al_effectivefrom", OrderType.Descending) },
            };

            var today = DateTime.UtcNow.Date;
            foreach (var version in service.RetrieveMultiple(query).Entities)
            {
                var from = version.GetAttributeValue<DateTime?>("al_effectivefrom");
                var to = version.GetAttributeValue<DateTime?>("al_effectiveto");

                if ((!from.HasValue || from.Value.Date <= today) && (!to.HasValue || to.Value.Date >= today))
                {
                    return version.Id;
                }
            }

            throw new InvalidPluginExecutionException(
                CommandHelpers.PreconditionPrefix +
                "No checklist version is in force, so a section cannot be added (BR-013).");
        }

        private static int ResolveDisplayOrder(
            IOrganizationService service, Guid checklistVersionId, string displayOrderArg)
        {
            int explicitOrder;
            if (!string.IsNullOrWhiteSpace(displayOrderArg)
                && int.TryParse(displayOrderArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out explicitOrder))
            {
                return explicitOrder;
            }

            var query = new QueryExpression(SectionEntity)
            {
                ColumnSet = new ColumnSet("al_displayorder"),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition("al_checklistversionid", ConditionOperator.Equal, checklistVersionId);
            query.AddOrder("al_displayorder", OrderType.Descending);

            var highest = service.RetrieveMultiple(query).Entities;
            return highest.Count == 0 ? 1 : highest[0].GetAttributeValue<int>("al_displayorder") + 1;
        }
    }
}
```

- [ ] **Step 6: Run the suite and commit**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
git add plugins/customapi/al_AddSection.customapi.json plugins/OutcomeTesting.Plugins/AddSectionPlugin.cs plugins/OutcomeTesting.Plugins/SectionQuestionSpec.cs plugins/OutcomeTesting.Plugins.Tests/SectionQuestionSpecTests.cs
git commit -m "feat(checklist): al_AddSection, with its questions in one transaction"
```

Expected: 741 total.

---

### Task 6: `al_RetireSection`

**Files:**
- Create: `plugins/customapi/al_RetireSection.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/RetireSectionPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/RetireSectionPluginTests.cs`

**Interfaces:**
- Consumes: `ChecklistGuards.ProtectedReason`, `RetireQuestionPlugin.ParseEffectiveTo`, `CommandHelpers.*`, `PermissionHelpers.*`
- Produces: Custom API `al_RetireSection`, audit command **120910797**, `RetireSectionPlugin.ProtectedCodeIn(IEnumerable<string> questionCodes) -> string`

**Contract** — `pluginType` `OutcomeTesting.Plugins.RetireSectionPlugin`: `SectionId` (10, required), `EffectiveTo` (10, optional), `Reason` (10, **required**), `IdempotencyKey` (10, required). Responses: `SectionId`, `AuditEventId` (both 10), `Conflict` (0).

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-123. Retiring a section takes its questions out of the form just as surely as
    /// retiring them individually would, so a section holding a load-bearing code is
    /// refused for the same reason.
    /// </summary>
    public class RetireSectionPluginTests
    {
        [Fact]
        public void A_section_of_ordinary_questions_may_be_retired()
        {
            Assert.Null(RetireSectionPlugin.ProtectedCodeIn(new[] { "Q-E1-01", "Q-E1-02" }));
        }

        [Fact]
        public void A_section_holding_a_protected_question_is_refused_and_names_it()
        {
            var refusal = RetireSectionPlugin.ProtectedCodeIn(new[] { "Q-E1-01", "Q-GR-01" });

            Assert.NotNull(refusal);
            Assert.Contains("Q-GR-01", refusal);
        }

        [Fact]
        public void An_empty_section_may_be_retired()
        {
            Assert.Null(RetireSectionPlugin.ProtectedCodeIn(new string[0]));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure, then write the plug-in**

The body mirrors `RetireQuestionPlugin`: parse, `EnsureAppPermission` on `question.retire` Edit, `FindAuditByKey` replay, then —

```csharp
// Retiring the section is what removes its questions from the form and the gate.
// Their versions are deliberately left alone: the answers they hold keep resolving
// (AD-091), and dating them out too would be a second, redundant edit to history.
var codes = QuestionCodesIn(userService, sectionId);
var refusal = ProtectedCodeIn(codes);
if (refusal != null)
{
    throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
}

var effectiveTo = RetireQuestionPlugin.ParseEffectiveTo(effectiveToArg, DateTime.UtcNow.Date);
userService.Update(new Entity("al_section", sectionId) { ["al_effectiveto"] = effectiveTo });
```

with:

```csharp
/// <summary>
/// The refusal for the first load-bearing code this section holds, or null when it
/// holds none.
/// </summary>
public static string ProtectedCodeIn(IEnumerable<string> questionCodes)
{
    foreach (var code in questionCodes)
    {
        var reason = ChecklistGuards.ProtectedReason(code);
        if (reason != null)
        {
            return reason + " Retiring the section that holds it would take it out of the form, so the section cannot be retired.";
        }
    }

    return null;
}

private static IEnumerable<string> QuestionCodesIn(IOrganizationService service, Guid sectionId)
{
    var query = new QueryExpression("al_question")
    {
        ColumnSet = new ColumnSet("al_questioncode"),
        Criteria = new FilterExpression(),
    };
    query.Criteria.AddCondition("al_sectionid", ConditionOperator.Equal, sectionId);

    var codes = new List<string>();
    foreach (var row in service.RetrieveMultiple(query).Entities)
    {
        codes.Add(row.GetAttributeValue<string>("al_questioncode"));
    }

    return codes;
}
```

- [ ] **Step 3: Run the suite and commit**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
git add plugins/customapi/al_RetireSection.customapi.json plugins/OutcomeTesting.Plugins/RetireSectionPlugin.cs plugins/OutcomeTesting.Plugins.Tests/RetireSectionPluginTests.cs
git commit -m "feat(checklist): al_RetireSection"
```

Expected: 744 total.

---

### Task 7: `al_UpdateSection`

The only in-place edit in this plan. Sections are not versioned, so the audited before/after is what preserves the trail — the `al_UpdateCaseDetails` precedent (AD-043).

**Files:**
- Create: `plugins/customapi/al_UpdateSection.customapi.json`
- Create: `plugins/OutcomeTesting.Plugins/UpdateSectionPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/UpdateSectionPluginTests.cs`

**Interfaces:**
- Produces: Custom API `al_UpdateSection`, audit command **120910798**, `UpdateSectionPlugin.DescribeChanges(Entity before, Entity after) -> string`

**Contract** — `pluginType` `OutcomeTesting.Plugins.UpdateSectionPlugin`: `SectionId` (10, required), `Name`, `HelpText`, `OwnerRole`, `DisplayOrder`, `IsOptional` (all 10, optional), `Reason` (10, **required**), `IdempotencyKey` (10, required). Responses: `SectionId`, `AuditEventId` (10), `Conflict` (0).

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-123, AD-043. Sections are not versioned, so the Audit Event's before/after is the
    /// only record of what a section used to be. It has to be accurate.
    /// </summary>
    public class UpdateSectionPluginTests
    {
        private static Entity Section(string name, bool optional, int ownerRole)
        {
            return new Entity("al_section")
            {
                ["al_name"] = name,
                ["al_isoptional"] = optional,
                ["al_ownerrole"] = new OptionSetValue(ownerRole),
            };
        }

        [Fact]
        public void An_unchanged_section_describes_no_change()
        {
            var before = Section("Consumer Duty", false, 120910101);
            var after = Section("Consumer Duty", false, 120910101);

            Assert.Equal(string.Empty, UpdateSectionPlugin.DescribeChanges(before, after));
        }

        [Fact]
        public void A_renamed_section_records_both_names()
        {
            var description = UpdateSectionPlugin.DescribeChanges(
                Section("Consumer Duty", false, 120910101),
                Section("Consumer Duty overlay", false, 120910101));

            Assert.Contains("Consumer Duty", description);
            Assert.Contains("Consumer Duty overlay", description);
        }

        [Fact]
        public void A_team_change_is_recorded_because_it_reaches_submitted_reviews()
        {
            var description = UpdateSectionPlugin.DescribeChanges(
                Section("Tax check", false, 120910100),
                Section("Tax check", false, 120910105));

            Assert.Contains("120910100", description);
            Assert.Contains("120910105", description);
        }

        [Fact]
        public void Becoming_optional_is_recorded()
        {
            var description = UpdateSectionPlugin.DescribeChanges(
                Section("CRP", false, 120910101),
                Section("CRP", true, 120910101));

            Assert.Contains("optional", description.ToLowerInvariant());
        }
    }
}
```

- [ ] **Step 2: Write the plug-in**

Same preamble as the others. It reads the section, applies only the parameters that were supplied, refuses making a section optional where it holds a protected code, and writes the before/after through `DescribeChanges`:

```csharp
// Owner role is editable by project owner direction of 2026-09-11, and the change is
// retroactive in a way nothing else here is: owner role is not versioned and carries no
// dates, so a section switched from Tax to AQS stops rendering in submitted Tax reviews
// and starts rendering in submitted AQS ones, which never answered it. The answers are
// not lost - they hang off the review instance - but which discipline appears to have
// been asked does change. The Audit Event is what keeps that honest.
```

and

```csharp
// An optional section owes no answers, so making one optional excuses a load-bearing
// question exactly as retiring it would.
if (becomingOptional)
{
    var refusal = RetireSectionPlugin.ProtectedCodeIn(QuestionCodesIn(userService, sectionId));
    if (refusal != null)
    {
        throw new InvalidPluginExecutionException(CommandHelpers.PreconditionPrefix + refusal);
    }
}
```

`DescribeChanges` compares `al_name`, `al_helptext`, `al_ownerrole`, `al_displayorder` and `al_isoptional`, emitting one `field: before -> after` clause per changed field and an empty string when nothing changed.

- [ ] **Step 3: Run the suite and commit**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
git add plugins/customapi/al_UpdateSection.customapi.json plugins/OutcomeTesting.Plugins/UpdateSectionPlugin.cs plugins/OutcomeTesting.Plugins.Tests/UpdateSectionPluginTests.cs
git commit -m "feat(checklist): al_UpdateSection, with an audited before and after"
```

Expected: 748 total.

---

### Task 8: `DisplayOrder` on `al_RetireAndSucceedQuestion`

Reordering is a new version, never an in-place update, because display order is frozen on the version by AD-015. The existing command already carries `ResponseType` and `Mandatory` forward when absent; this adds a third in the same shape.

**Files:**
- Modify: `plugins/customapi/al_RetireAndSucceedQuestion.customapi.json`
- Modify: `plugins/OutcomeTesting.Plugins/RetireAndSucceedQuestionPlugin.cs`
- Modify: `app/src/services/commands/questions.ts`

- [ ] **Step 1: Add the request parameter to the contract**

```json
{ "uniquename": "DisplayOrder", "name": "DisplayOrder", "displayname": "Display order", "description": "Optional. Integer order for the new version. Absent carries the current value forward.", "type": 10, "isoptional": true }
```

- [ ] **Step 2: Read and apply it in the plug-in**

Beside the existing `responseTypeOverride` and `mandatoryOverride` reads, add `var displayOrderOverride = CommandHelpers.GetOptionalString(context, "DisplayOrder");`, and replace the block that copies `al_displayorder` forward:

```csharp
int displayOrderValue;
if (!string.IsNullOrWhiteSpace(displayOrderOverride)
    && int.TryParse(displayOrderOverride, out displayOrderValue))
{
    successor["al_displayorder"] = displayOrderValue;
}
else if (current.Contains("al_displayorder"))
{
    successor["al_displayorder"] = current.GetAttributeValue<int>("al_displayorder");
}
```

- [ ] **Step 3: Add it to the client wrapper**

In `questions.ts`, add `displayOrder?: number` to `RetireAndSucceedQuestionInput` and, beside the existing optional marshalling, `if (input.displayOrder !== undefined) body.DisplayOrder = String(input.displayOrder);`

- [ ] **Step 4: Verify and commit**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
cd ../app && npx tsc -b && npm test
git add plugins/customapi/al_RetireAndSucceedQuestion.customapi.json plugins/OutcomeTesting.Plugins/RetireAndSucceedQuestionPlugin.cs app/src/services/commands/questions.ts
git commit -m "feat(checklist): carry DisplayOrder through RetireAndSucceedQuestion"
```

---

### Task 9: Register, deploy to DEV, and prove each command

**DEV only.** Confirm `pac auth list` shows `Env_AQ_Dev` before anything here.

- [ ] **Step 1: Mint the six audit command values**

`addcommandvalue` takes `<orgUrl> <value> <label>` and no `--confirm` — check its dispatch in `Program.cs` before running, and read each value back from metadata rather than trusting the insert (the OD-032 lesson).

```bash
cd plugins/OutcomeTesting.Registration
ORG=https://org0b075da8.crm11.dynamics.com/
for V in "120910793 AddQuestion" "120910794 RetireQuestion" "120910795 MoveQuestion" \
         "120910796 AddSection" "120910797 RetireSection" "120910798 UpdateSection"; do
  DOTNET_ROLL_FORWARD=Major dotnet run -- addcommandvalue $ORG $V
done
```

Then read back, expecting 30 values:

```bash
DOTNET_ROLL_FORWARD=Major dotnet run -- fetch $ORG "<fetch top='1'><entity name='al_auditevent'><attribute name='al_command' /></entity></fetch>"
```

- [ ] **Step 2: Build Release and push the assembly**

```bash
cd ../OutcomeTesting.Plugins && DOTNET_ROLL_FORWARD=Major dotnet build -c Release
ls -l bin/Release/net462/OutcomeTesting.Plugins.dll
cd ../OutcomeTesting.Registration && DOTNET_ROLL_FORWARD=Major dotnet run -- pushassembly $ORG
```

The byte count in the push output must match the `ls`. `pushassembly` uploads `bin/Release` without building.

- [ ] **Step 3: Register the six Custom APIs**

```bash
DOTNET_ROLL_FORWARD=Major dotnet run -- registerall $ORG
```

`registerall` has been refused before on field lengths that appear in neither the contract schema nor the error — see `docs/deployment/2026-09-07-role-conflict-rule-deployment.md`. If it refuses, read that note before changing anything.

- [ ] **Step 4: Add each Custom API to the solution**

`registerall` prints these rather than running them. Run what it prints; the form is:

```bash
pac solution add-solution-component --solutionUniqueName OutcomeTesting --component CustomAPI --componentId <id>
```

- [ ] **Step 5: Exercise each command once against DEV**

Nothing in Tasks 2 to 7 proves a Dataverse write, only the rules. Create a scratch section, add a question to it, move it, retire it, then retire the section — and read each result back with `fetch`. Record the ids in the deployment note. Do not leave scratch content in force: retire what you create.

- [ ] **Step 6: Round-trip the solution and commit**

Export, unpack, and copy back `customapis/` and `Other/` per `docs/deployment/2026-09-06-ad013-round-trip.md`, then confirm `pac solution pack --folder src` succeeds with only the `CanvasApps` warning.

---

### Task 10: Client wrappers and data-source registration

Without the data source the Power Apps client cannot resolve the API and the command fails before it reaches Dataverse. `operations.test.ts` enforces this, so it fails first and tells you the command to run.

**Files:**
- Create: `app/src/services/commands/sections.ts`
- Modify: `app/src/services/commands/questions.ts`, `app/src/services/commands/operations.ts`

- [ ] **Step 1: Add the six operations, and watch the test fail**

Add to `COMMAND_OPERATIONS` in alphabetical position: `al_AddQuestion`, `al_AddSection`, `al_MoveQuestion`, `al_RetireQuestion`, `al_RetireSection`, `al_UpdateSection`.

```bash
cd app && npm test -- operations
```

Expected: FAIL, six times, each naming the command to run — that is the guard working.

- [ ] **Step 2: Register each API with the code app**

```bash
cd app
for N in al_AddQuestion al_AddSection al_MoveQuestion al_RetireQuestion al_RetireSection al_UpdateSection; do
  npx pa app add dataverse-api --api-name $N
done
```

- [ ] **Step 3: Re-run the test**

```bash
cd app && npm test -- operations
```

Expected: PASS. If a data source is still missing, the API was not registered in DEV — go back to Task 9 step 3 rather than editing `dataSourcesInfo` by hand.

- [ ] **Step 4: Write the typed wrappers**

Follow `retireAndSucceedQuestion` in `questions.ts` exactly: an input interface, an output interface matching the contract's response properties, and a function that marshals optional values as strings. Add `addQuestion`, `retireQuestion` and `moveQuestion` to `questions.ts`; create `sections.ts` with `addSection`, `retireSection` and `updateSection`. `addSection` takes `questions?: SectionQuestionInput[]` and marshals it with `JSON.stringify`.

- [ ] **Step 5: Verify and commit**

```bash
cd app && npx tsc -b && npm test
git add app/src/services/commands/
git commit -m "feat(app): typed wrappers for the six checklist administration commands"
```

---

## Done when

- An administrator holding Edit on `question.retire` can add, retire and move a question, and add, retire and update a section, through six Custom APIs — each audited, each replay-safe, each refusing the eight load-bearing codes where it should.
- Every command has been exercised once against DEV and read back.
- The plug-in suite, `npx tsc -b` and the app suite all pass.
- No UI exists yet. That is plan three.

## Deliberately not in this plan

- **The Question library UI** — the modal, the add and retire controls, the team selector, the retired grouping. Plan three.
- **Un-retire.** Anything dated out stays out; putting it back is an add.
- **Folding a new section into a designed document block, and subsections.** AD-098's mapping stays hand-written; a new section renders through the generic fallback.
- **Any promotion beyond DEV.**
