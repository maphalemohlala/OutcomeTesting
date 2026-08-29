# Portal Answering Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the assigned Tax or AQS checker answer their review questionnaire in the Power Pages portal, with every rule enforced server-side.

**Architecture:** The page writes `al_response` directly over the Portals Web API (actions are unsupported there, create/update/associate are not). A synchronous pre-operation plug-in enforces the submission lock, the typed-column match, the permitted option subset and section ownership; a post-operation plug-in advances the review status. All decision logic lives in a pure static `ResponseRules` class with no Dataverse dependency, so it is unit-testable without a Dataverse fake — mirroring the pure-guard pattern already used in `app/src/types/domain.ts`. Authorization is Contact-scoped table permissions, never plug-in identity.

**Tech Stack:** Dataverse plug-ins (C#, net462, Microsoft.CrmSdk.CoreAssemblies 9.0.2), xUnit, Power Pages Liquid web templates, vanilla ES5 browser JS, PowerShell deployment via the Dataverse Web API.

**Spec:** `docs/superpowers/specs/2026-08-29-portal-answering-design.md`

## Global Constraints

- Never invent a business rule. Cite the requirement ID from `knowledge/requirements-index.md`, and name the blocking OD ID from `knowledge/decision-log.md` rather than assuming a resolution.
- Security is enforced in Dataverse/Power Pages permissions, never only in UI code (NFR-SEC-01).
- Do not hardcode secrets, tenant/environment IDs, URLs, connection IDs, email addresses or group IDs.
- Portal metadata stays under `powerpages/`, never under `src/` or `app/` (AD-048).
- `brand/` is the only authoritative source of colours and fonts. Portal CSS consumes existing tokens; no second colour system (AD-049).
- Plug-in assembly targets `net462` and is signed with `OutcomeTesting.Plugins.snk`.
- Client scripts are ES5 (no arrow functions, `const`/`let`, or template literals) — they run inside the Power Pages bundle alongside jQuery.
- Accessibility is WCAG 2.2 AA (NFR-ACC-01).
- Option set values used across this plan, verbatim from the schema:
  - Response type: Text `120910000`, Multiline text `120910001`, Date `120910002`, Single select `120910003`, Multi select `120910004`, Pass/Fail `120910005`, Pass/Fail/Insufficient `120910006`, Yes/No `120910007`, Yes/No/NA `120910008`, Yes/No/Insufficient `120910009`, Grade `120910010`
  - Answer choice: Pass `120910300`, Fail `120910301`, Insufficient evidence `120910302`, Pass with issues `120910303`, Potential harm `120910304`, Yes `120910305`, No `120910306`, N/A `120910307`, root causes `120910320`–`120910328`
  - Answer choices (multi): `120910340`–`120910344`
  - Review status: Assigned `120910210`, Review In Progress `120910211`, Submitted `120910212`
  - Review type: Tax `120910200`, AQS `120910201`
  - Section owner role: Tax team `120910100`, AQS checker `120910101`

---

## File Structure

| File | Responsibility |
|---|---|
| `plugins/OutcomeTesting.Plugins/ResponseRules.cs` | Pure decision logic. No Dataverse types. The only file the unit tests touch. |
| `plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs` | Pre-operation wiring: retrieve context rows, call `ResponseRules`, throw prefixed failures, stamp `al_responsecode`/`al_name`. |
| `plugins/OutcomeTesting.Plugins/ResponseProgressPlugin.cs` | Post-operation: advance review status Assigned → Review In Progress. |
| `plugins/OutcomeTesting.Plugins.Tests/` | xUnit project. First plug-in tests in the repository. |
| `plugins/deploy/Register-ResponseGuard.ps1` | Registers the two plug-in types and their SDK message processing steps. |
| `powerpages/.../web-templates/ot-review-detail/` | Renders typed controls; hosts the autosave script. |
| `powerpages/.../web-files/outcome-testing.css` | `.ot-answer*` classes. |
| `powerpages/.../table-permissions/` | Contact-scoped review + child response permissions. |
| `data/v8-seed/data.xml` | Q-TAX-02 response type correction. |

---

### Task 1: `ResponseRules` and the plug-in test project

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/ResponseRules.cs`
- Create: `plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj`
- Test: `plugins/OutcomeTesting.Plugins.Tests/ResponseRulesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `OutcomeTesting.Plugins.ResponseRules` with
  - `AnswerColumn ColumnFor(int responseType)` where `AnswerColumn` is `enum { Text, Date, Choice, Choices }`
  - `IReadOnlyList<int> PermittedChoices(int responseType)`
  - `int OwnerRoleFor(int reviewType)`
  - `string BuildResponseCode(Guid reviewId, Guid questionVersionId)`
  - `bool IsNonPass(int choice)`
  - `string ValidateAnswer(int responseType, bool hasText, bool hasDate, int? choice, IReadOnlyCollection<int> choices)` returning `null` when valid, otherwise a message with no prefix.

- [ ] **Step 1: Create the test project**

Create `plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <IsPackable>false</IsPackable>
    <SignAssembly>false</SignAssembly>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" Version="1.0.3" PrivateAssets="All" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\OutcomeTesting.Plugins\OutcomeTesting.Plugins.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/ResponseRulesTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class ResponseRulesTests
    {
        [Theory]
        [InlineData(120910000, ResponseRules.AnswerColumn.Text)]
        [InlineData(120910001, ResponseRules.AnswerColumn.Text)]
        [InlineData(120910002, ResponseRules.AnswerColumn.Date)]
        [InlineData(120910003, ResponseRules.AnswerColumn.Choice)]
        [InlineData(120910004, ResponseRules.AnswerColumn.Choices)]
        [InlineData(120910005, ResponseRules.AnswerColumn.Choice)]
        [InlineData(120910010, ResponseRules.AnswerColumn.Choice)]
        public void ColumnFor_maps_each_response_type_to_its_typed_column(int type, ResponseRules.AnswerColumn expected)
        {
            Assert.Equal(expected, ResponseRules.ColumnFor(type));
        }

        [Fact]
        public void ColumnFor_rejects_an_unknown_response_type()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ResponseRules.ColumnFor(999));
        }

        [Fact]
        public void PermittedChoices_for_grade_is_the_four_BR005_outcomes()
        {
            Assert.Equal(
                new[] { 120910300, 120910303, 120910302, 120910304 },
                ResponseRules.PermittedChoices(120910010));
        }

        [Fact]
        public void PermittedChoices_for_yes_no_na_excludes_insufficient_evidence()
        {
            var permitted = ResponseRules.PermittedChoices(120910008);
            Assert.Contains(120910307, permitted);
            Assert.DoesNotContain(120910302, permitted);
        }

        [Fact]
        public void PermittedChoices_for_single_select_is_the_root_cause_block()
        {
            var permitted = ResponseRules.PermittedChoices(120910003);
            Assert.Equal(9, permitted.Count);
            Assert.Contains(120910320, permitted);
            Assert.Contains(120910328, permitted);
        }

        [Fact]
        public void ValidateAnswer_accepts_a_permitted_choice()
        {
            Assert.Null(ResponseRules.ValidateAnswer(120910005, false, false, 120910301, new int[0]));
        }

        [Fact]
        public void ValidateAnswer_rejects_a_choice_outside_the_scale()
        {
            // Potential harm belongs to Grade, not Pass / Fail.
            var failure = ResponseRules.ValidateAnswer(120910005, false, false, 120910304, new int[0]);
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_allows_a_note_alongside_a_choice()
        {
            // al_answertext doubles as evidence held with a structured answer.
            // useReviewDetail.ts noteOf() already reads the record this way.
            Assert.Null(ResponseRules.ValidateAnswer(120910005, true, false, 120910301, new int[0]));
        }

        [Fact]
        public void ValidateAnswer_rejects_an_answer_in_the_wrong_column()
        {
            var failure = ResponseRules.ValidateAnswer(120910000, false, true, null, new int[0]);
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_rejects_a_choice_on_a_text_question()
        {
            var failure = ResponseRules.ValidateAnswer(120910000, true, false, 120910300, new int[0]);
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_accepts_an_empty_answer_so_a_draft_can_be_cleared()
        {
            Assert.Null(ResponseRules.ValidateAnswer(120910005, false, false, null, new int[0]));
        }

        [Fact]
        public void ValidateAnswer_rejects_a_multi_select_value_outside_the_tax_reason_list()
        {
            var failure = ResponseRules.ValidateAnswer(120910004, false, false, null, new[] { 120910340, 120910300 });
            Assert.NotNull(failure);
        }

        [Fact]
        public void ValidateAnswer_accepts_every_tax_reason_at_once()
        {
            var all = new[] { 120910340, 120910341, 120910342, 120910343, 120910344 };
            Assert.Null(ResponseRules.ValidateAnswer(120910004, false, false, null, all));
        }

        [Theory]
        [InlineData(120910200, 120910100)]
        [InlineData(120910201, 120910101)]
        public void OwnerRoleFor_maps_review_type_to_the_section_owner_role(int reviewType, int expected)
        {
            Assert.Equal(expected, ResponseRules.OwnerRoleFor(reviewType));
        }

        [Theory]
        [InlineData(120910301, true)]
        [InlineData(120910302, true)]
        [InlineData(120910304, true)]
        [InlineData(120910300, false)]
        [InlineData(120910303, false)]
        public void IsNonPass_covers_fail_insufficient_and_potential_harm(int choice, bool expected)
        {
            Assert.Equal(expected, ResponseRules.IsNonPass(choice));
        }

        [Fact]
        public void BuildResponseCode_is_stable_and_fits_the_code_column()
        {
            var review = new Guid("11111111-1111-4111-8111-111111111111");
            var question = new Guid("22222222-2222-4222-8222-222222222222");
            var code = ResponseRules.BuildResponseCode(review, question);

            Assert.Equal(code, ResponseRules.BuildResponseCode(review, question));
            Assert.True(code.Length <= 100);
            Assert.Contains("|", code);
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: FAIL to compile — `ResponseRules` does not exist.

- [ ] **Step 4: Write the implementation**

Create `plugins/OutcomeTesting.Plugins/ResponseRules.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Pure decision logic for a checklist answer (AD-023, AD-053). Deliberately free of
    /// Dataverse types so it can be unit-tested without a fake organisation service; the
    /// plug-ins are thin wiring over this. Mirrors the pure-guard pattern in
    /// app/src/types/domain.ts, where the client-side mirror of a command rule is a
    /// function over primitives.
    /// </summary>
    public static class ResponseRules
    {
        public enum AnswerColumn { Text, Date, Choice, Choices }

        // al_questionversion.al_responsetype
        public const int TypeText = 120910000;
        public const int TypeMultilineText = 120910001;
        public const int TypeDate = 120910002;
        public const int TypeSingleSelect = 120910003;
        public const int TypeMultiSelect = 120910004;
        public const int TypePassFail = 120910005;
        public const int TypePassFailInsufficient = 120910006;
        public const int TypeYesNo = 120910007;
        public const int TypeYesNoNa = 120910008;
        public const int TypeYesNoInsufficient = 120910009;
        public const int TypeGrade = 120910010;

        // al_response.al_answerchoice
        public const int ChoicePass = 120910300;
        public const int ChoiceFail = 120910301;
        public const int ChoiceInsufficient = 120910302;
        public const int ChoicePassWithIssues = 120910303;
        public const int ChoicePotentialHarm = 120910304;
        public const int ChoiceYes = 120910305;
        public const int ChoiceNo = 120910306;
        public const int ChoiceNa = 120910307;

        // al_reviewinstance.al_reviewstatus
        public const int StatusAssigned = 120910210;
        public const int StatusInProgress = 120910211;
        public const int StatusSubmitted = 120910212;

        // al_reviewinstance.al_reviewtype and al_section.al_ownerrole
        public const int ReviewTypeTax = 120910200;
        public const int ReviewTypeAqs = 120910201;
        public const int OwnerRoleTaxTeam = 120910100;
        public const int OwnerRoleAqsChecker = 120910101;

        private static readonly int[] RootCauses =
        {
            120910320, 120910321, 120910322, 120910323, 120910324,
            120910325, 120910326, 120910327, 120910328,
        };

        private static readonly int[] TaxReasons =
        {
            120910340, 120910341, 120910342, 120910343, 120910344,
        };

        /// <summary>The typed column an answer of this response type must occupy (AD-023).</summary>
        public static AnswerColumn ColumnFor(int responseType)
        {
            switch (responseType)
            {
                case TypeText:
                case TypeMultilineText:
                    return AnswerColumn.Text;
                case TypeDate:
                    return AnswerColumn.Date;
                case TypeMultiSelect:
                    return AnswerColumn.Choices;
                case TypeSingleSelect:
                case TypePassFail:
                case TypePassFailInsufficient:
                case TypeYesNo:
                case TypeYesNoNa:
                case TypeYesNoInsufficient:
                case TypeGrade:
                    return AnswerColumn.Choice;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(responseType), responseType, "Unknown response type.");
            }
        }

        /// <summary>
        /// The subset of the al_response_answerchoice union set this response type may use.
        /// The constraint lives on the question version rather than the schema (AD-023), so
        /// it is enforced here and nowhere else.
        /// </summary>
        public static IReadOnlyList<int> PermittedChoices(int responseType)
        {
            switch (responseType)
            {
                case TypePassFail:
                    return new[] { ChoicePass, ChoiceFail };
                case TypePassFailInsufficient:
                    return new[] { ChoicePass, ChoiceFail, ChoiceInsufficient };
                case TypeYesNo:
                    return new[] { ChoiceYes, ChoiceNo };
                case TypeYesNoNa:
                    return new[] { ChoiceYes, ChoiceNo, ChoiceNa };
                case TypeYesNoInsufficient:
                    return new[] { ChoiceYes, ChoiceNo, ChoiceInsufficient };
                case TypeGrade:
                    // BR-005 order: Pass, Pass with issues, Insufficient evidence, Potential harm.
                    return new[] { ChoicePass, ChoicePassWithIssues, ChoiceInsufficient, ChoicePotentialHarm };
                case TypeSingleSelect:
                    // Primary root cause. Q-TAX-02 was retyped to PassFailInsufficient
                    // under AD-055, so this scale is unambiguous.
                    return RootCauses;
                case TypeMultiSelect:
                    return TaxReasons;
                default:
                    return new int[0];
            }
        }

        /// <summary>AD-020 section ownership, keyed by the review's discipline.</summary>
        public static int OwnerRoleFor(int reviewType)
        {
            switch (reviewType)
            {
                case ReviewTypeTax:
                    return OwnerRoleTaxTeam;
                case ReviewTypeAqs:
                    return OwnerRoleAqsChecker;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(reviewType), reviewType, "Unknown review type.");
            }
        }

        /// <summary>BR-006: these are the outcomes that require remediation, and so a fail reason.</summary>
        public static bool IsNonPass(int choice)
        {
            return choice == ChoiceFail || choice == ChoiceInsufficient || choice == ChoicePotentialHarm;
        }

        /// <summary>
        /// The al_ResponseCodeKey value for one answer. Stable, so a replayed create collides
        /// on the alternate key instead of producing two rival answers to the same question.
        /// Two GUIDs and a separator is 73 characters, inside the AD-026 MaxLength of 100.
        /// </summary>
        public static string BuildResponseCode(Guid reviewId, Guid questionVersionId)
        {
            return reviewId.ToString("D") + "|" + questionVersionId.ToString("D");
        }

        /// <summary>
        /// Validates the shape of an answer. Returns null when valid, otherwise a message
        /// with no failure prefix — the caller adds PRECONDITION:.
        ///
        /// An entirely empty answer is valid: clearing a field is a legitimate draft edit,
        /// and SubmitReview is what refuses to submit an unanswered mandatory question.
        ///
        /// Text alongside a choice or choices is valid and deliberate: al_answertext doubles
        /// as the evidence note held with a structured answer, which is how
        /// app/src/features/reviews/useReviewDetail.ts noteOf() already reads the record.
        /// </summary>
        public static string ValidateAnswer(
            int responseType,
            bool hasText,
            bool hasDate,
            int? choice,
            IReadOnlyCollection<int> choices)
        {
            var selected = choices ?? (IReadOnlyCollection<int>)new int[0];
            var column = ColumnFor(responseType);

            switch (column)
            {
                case AnswerColumn.Text:
                    if (hasDate || choice.HasValue || selected.Count > 0)
                    {
                        return "This question takes a written answer only.";
                    }
                    return null;

                case AnswerColumn.Date:
                    if (hasText || choice.HasValue || selected.Count > 0)
                    {
                        return "This question takes a date only.";
                    }
                    return null;

                case AnswerColumn.Choice:
                    if (hasDate || selected.Count > 0)
                    {
                        return "This question takes a single selection.";
                    }
                    if (choice.HasValue && !PermittedChoices(responseType).Contains(choice.Value))
                    {
                        return "That option is not available for this question.";
                    }
                    return null;

                case AnswerColumn.Choices:
                    if (hasDate || choice.HasValue)
                    {
                        return "This question takes one or more selections from its own list.";
                    }
                    var permitted = PermittedChoices(responseType);
                    if (selected.Any(value => !permitted.Contains(value)))
                    {
                        return "One or more of those options is not available for this question.";
                    }
                    return null;

                default:
                    return "This question cannot be answered.";
            }
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: PASS, 27 tests (the `Theory` cases count individually: 7 for `ColumnFor`, 2 for `OwnerRoleFor`, 5 for `IsNonPass`, plus 13 `Fact`s).

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/ResponseRules.cs plugins/OutcomeTesting.Plugins.Tests
git commit -m "feat(plugins): pure answer rules with the first plug-in test project"
```

---

### Task 2: `ResponseGuardPlugin` and `ResponseProgressPlugin`

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs`
- Create: `plugins/OutcomeTesting.Plugins/ResponseProgressPlugin.cs`
- Modify: `knowledge/decision-log.md` — append AD-053

**Interfaces:**
- Consumes: `ResponseRules` from Task 1.
- Produces: plug-in type names `OutcomeTesting.Plugins.ResponseGuardPlugin` and `OutcomeTesting.Plugins.ResponseProgressPlugin`, referenced by Task 3's registration script.

- [ ] **Step 1: Write the guard plug-in**

Create `plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Synchronous pre-operation guard on al_response Create and Update, and on
    /// Associate/Disassociate of al_failreason_response (AD-053).
    ///
    /// This is the PP-11 submission lock and the AD-023 answer-shape rule enforced below
    /// the Portals Web API, so neither a hand-edited URL nor a direct PATCH can bypass
    /// them (NFR-SEC-01).
    ///
    /// It deliberately performs no authorization. Power Pages Web API writes reach
    /// Dataverse under the site's application user, so InitiatingUserId is not the
    /// checker; treating it as one would be a security hole rather than a check. The
    /// caller gate is the Contact-scoped table permission on al_reviewinstance with
    /// al_response as its child permission (AD-047).
    /// </summary>
    public class ResponseGuardPlugin : PluginBase
    {
        private const string PreconditionPrefix = "PRECONDITION: ";
        private const string ConflictPrefix = "CONFLICT: ";

        private const string ResponseEntity = "al_response";
        private const string FailReasonRelationship = "al_failreason_response";

        public ResponseGuardPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ResponseGuardPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            switch (context.MessageName)
            {
                case "Create":
                case "Update":
                    GuardWrite(service, context);
                    return;
                case "Associate":
                case "Disassociate":
                    GuardRelationship(service, context);
                    return;
                default:
                    return;
            }
        }

        private static void GuardWrite(IOrganizationService service, IPluginExecutionContext context)
        {
            if (!(context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity target))
            {
                return;
            }

            if (target.LogicalName != ResponseEntity)
            {
                return;
            }

            // On Update the Target carries only changed columns, so the review and question
            // version come from the pre-image rather than the Target.
            var pre = context.PreEntityImages.Values.FirstOrDefault();

            var reviewRef = target.GetAttributeValue<EntityReference>("al_reviewinstanceid")
                ?? pre?.GetAttributeValue<EntityReference>("al_reviewinstanceid");
            var questionVersionRef = target.GetAttributeValue<EntityReference>("al_questionversionid")
                ?? pre?.GetAttributeValue<EntityReference>("al_questionversionid");

            if (reviewRef == null || questionVersionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "An answer must belong to a review and a question.");
            }

            var review = service.Retrieve(
                "al_reviewinstance",
                reviewRef.Id,
                new ColumnSet("al_reviewstatus", "al_reviewtype", "al_checklistversionid"));

            EnsureNotSubmitted(review);

            var questionVersion = service.Retrieve(
                "al_questionversion",
                questionVersionRef.Id,
                new ColumnSet("al_responsetype", "al_questionid"));

            var responseType = questionVersion.GetAttributeValue<OptionSetValue>("al_responsetype");
            if (responseType == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question has no response type, so it cannot be answered.");
            }

            EnsureSectionBelongsToReview(service, questionVersion, review, reviewRef.Id);
            EnsureAnswerShape(target, pre, responseType.Value);

            // Stamped server-side, never accepted from the client: al_ResponseCodeKey is what
            // makes a replayed create collide instead of writing a rival answer.
            if (context.MessageName == "Create")
            {
                target["al_responsecode"] = ResponseRules.BuildResponseCode(reviewRef.Id, questionVersionRef.Id);
                if (!target.Contains("al_name"))
                {
                    target["al_name"] = questionVersionRef.Name ?? "Answer";
                }
            }
        }

        private static void EnsureNotSubmitted(Entity review)
        {
            var status = review.GetAttributeValue<OptionSetValue>("al_reviewstatus");
            if (status != null && status.Value == ResponseRules.StatusSubmitted)
            {
                throw new InvalidPluginExecutionException(
                    ConflictPrefix + "This review has been submitted and can no longer be changed.");
            }
        }

        /// <summary>
        /// AD-020 section ownership, and the AD-023 rule that a review answers only the
        /// checklist version issued to it. Both are structural: a Tax review cannot hold an
        /// AQS section's answer even if a request is crafted by hand (PP-08).
        /// </summary>
        private static void EnsureSectionBelongsToReview(
            IOrganizationService service,
            Entity questionVersion,
            Entity review,
            Guid reviewId)
        {
            var questionRef = questionVersion.GetAttributeValue<EntityReference>("al_questionid");
            if (questionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question is not attached to a section.");
            }

            var question = service.Retrieve("al_question", questionRef.Id, new ColumnSet("al_sectionid"));
            var sectionRef = question.GetAttributeValue<EntityReference>("al_sectionid");
            if (sectionRef == null)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question is not attached to a section.");
            }

            var section = service.Retrieve(
                "al_section",
                sectionRef.Id,
                new ColumnSet("al_ownerrole", "al_checklistversionid"));

            var reviewType = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
            var ownerRole = section.GetAttributeValue<OptionSetValue>("al_ownerrole");
            if (reviewType == null || ownerRole == null
                || ownerRole.Value != ResponseRules.OwnerRoleFor(reviewType.Value))
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question belongs to another discipline's section.");
            }

            var issued = review.GetAttributeValue<EntityReference>("al_checklistversionid");
            var sectionVersion = section.GetAttributeValue<EntityReference>("al_checklistversionid");
            if (issued == null || sectionVersion == null || issued.Id != sectionVersion.Id)
            {
                throw new InvalidPluginExecutionException(
                    PreconditionPrefix + "This question is not part of the checklist version issued to this review.");
            }
        }

        private static void EnsureAnswerShape(Entity target, Entity pre, int responseType)
        {
            var hasText = HasText(target, pre, "al_answertext");
            var hasDate = Resolve<DateTime?>(target, pre, "al_answerdate").HasValue;

            var choiceValue = Resolve<OptionSetValue>(target, pre, "al_answerchoice");
            int? choice = choiceValue == null ? (int?)null : choiceValue.Value;

            var choicesValue = Resolve<OptionSetValueCollection>(target, pre, "al_answerchoices");
            var choices = choicesValue == null
                ? new int[0]
                : choicesValue.Select(value => value.Value).ToArray();

            var failure = ResponseRules.ValidateAnswer(responseType, hasText, hasDate, choice, choices);
            if (failure != null)
            {
                throw new InvalidPluginExecutionException(PreconditionPrefix + failure);
            }
        }

        /// <summary>
        /// The effective value after this write: the Target when it carries the column,
        /// otherwise the pre-image. A Target that explicitly sets a column to null clears it,
        /// which is why Contains is checked before falling back.
        /// </summary>
        private static T Resolve<T>(Entity target, Entity pre, string attribute)
        {
            if (target.Contains(attribute))
            {
                return target.GetAttributeValue<T>(attribute);
            }
            return pre == null ? default(T) : pre.GetAttributeValue<T>(attribute);
        }

        private static bool HasText(Entity target, Entity pre, string attribute)
        {
            return !string.IsNullOrWhiteSpace(Resolve<string>(target, pre, attribute));
        }

        /// <summary>
        /// A fail reason may only be attached to or removed from an answer on a review that
        /// is still open (FR-013, PP-11).
        /// </summary>
        private static void GuardRelationship(IOrganizationService service, IPluginExecutionContext context)
        {
            if (!(context.InputParameters.Contains("Relationship")
                && context.InputParameters["Relationship"] is Relationship relationship))
            {
                return;
            }

            if (!string.Equals(relationship.SchemaName, FailReasonRelationship, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var responseIds = CollectResponseIds(context);
            foreach (var responseId in responseIds)
            {
                var response = service.Retrieve(
                    ResponseEntity, responseId, new ColumnSet("al_reviewinstanceid"));
                var reviewRef = response.GetAttributeValue<EntityReference>("al_reviewinstanceid");
                if (reviewRef == null)
                {
                    continue;
                }

                var review = service.Retrieve(
                    "al_reviewinstance", reviewRef.Id, new ColumnSet("al_reviewstatus"));
                EnsureNotSubmitted(review);
            }
        }

        /// <summary>
        /// Either end of the N:N may be the Target, so both are inspected rather than
        /// assuming the caller associated from the response side.
        /// </summary>
        private static IEnumerable<Guid> CollectResponseIds(IPluginExecutionContext context)
        {
            var ids = new List<Guid>();

            if (context.InputParameters.Contains("Target")
                && context.InputParameters["Target"] is EntityReference target
                && target.LogicalName == ResponseEntity)
            {
                ids.Add(target.Id);
            }

            if (context.InputParameters.Contains("RelatedEntities")
                && context.InputParameters["RelatedEntities"] is EntityReferenceCollection related)
            {
                ids.AddRange(related.Where(r => r.LogicalName == ResponseEntity).Select(r => r.Id));
            }

            return ids.Distinct();
        }
    }
}
```

- [ ] **Step 2: Write the progress plug-in**

Create `plugins/OutcomeTesting.Plugins/ResponseProgressPlugin.cs`:

```csharp
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Post-operation on al_response Create and Update. The first saved answer moves the
    /// review from Assigned to Review In Progress, which is the FR-010 lifecycle step the
    /// checker never performs explicitly (AD-053).
    ///
    /// This runs server-side because the portal holds no write permission on
    /// al_reviewinstance and must not be given one: a checker who could write the review
    /// row directly could also write al_submittedon.
    /// </summary>
    public class ResponseProgressPlugin : PluginBase
    {
        public ResponseProgressPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(ResponseProgressPlugin))
        {
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var service = localPluginContext.PluginUserService;

            if (!(context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity target))
            {
                return;
            }

            if (target.LogicalName != "al_response")
            {
                return;
            }

            // On Update the Target carries only changed columns, so the review link comes
            // from the pre-image registered by Register-ResponseGuard.ps1.
            var pre = context.PreEntityImages.Values.FirstOrDefault();
            var reviewRef = target.GetAttributeValue<EntityReference>("al_reviewinstanceid")
                ?? pre?.GetAttributeValue<EntityReference>("al_reviewinstanceid");

            if (reviewRef == null)
            {
                return;
            }

            var review = service.Retrieve(
                "al_reviewinstance",
                reviewRef.Id,
                new ColumnSet("al_reviewstatus", "al_startedon"));

            var status = review.GetAttributeValue<OptionSetValue>("al_reviewstatus");
            if (status == null || status.Value != ResponseRules.StatusAssigned)
            {
                return;
            }

            var update = new Entity("al_reviewinstance", reviewRef.Id)
            {
                ["al_reviewstatus"] = new OptionSetValue(ResponseRules.StatusInProgress),
            };

            // Started is stamped once, on the transition, so a later edit never moves it.
            if (!review.Contains("al_startedon"))
            {
                update["al_startedon"] = DateTime.UtcNow;
            }

            service.Update(update);
        }
    }
}
```

- [ ] **Step 3: Build and run the tests**

Run: `dotnet build plugins/OutcomeTesting.Plugins && dotnet test plugins/OutcomeTesting.Plugins.Tests`
Expected: build succeeds, 20 tests pass.

- [ ] **Step 4: Record AD-053**

Append to the decision table in `knowledge/decision-log.md`:

```markdown
| AD-053 | Portal answers are written directly to `al_response` over the Portals Web API rather than through a command and cloud flow. A synchronous pre-operation `ResponseGuardPlugin` enforces the PP-11 submission lock, the AD-023 typed-column match, the permitted option subset and AD-020 section ownership, and stamps `al_responsecode` so `al_ResponseCodeKey` makes a replayed create collide. A post-operation `ResponseProgressPlugin` advances Assigned to Review In Progress. Authorization is Contact-scoped table permissions (AD-047), never plug-in identity. | The Portals Web API supports create, update and associate but explicitly not actions, so answering needs no flow while submission does; a flow per answer would also spend a Power Automate run on every debounced edit. Putting the rules in a plug-in rather than the page keeps them unbypassable by a hand-edited URL or a direct PATCH (NFR-SEC-01). Authorization is excluded from the plug-in deliberately: Power Pages Web API writes arrive under the site's application user, so `InitiatingUserId` is not the checker and a check against it would look like security while enforcing nothing. Decision logic lives in a dependency-free `ResponseRules` class so it is unit-testable without a Dataverse fake, which is what let this land with the repository's first plug-in tests. | 2026-08-29 |
```

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs plugins/OutcomeTesting.Plugins/ResponseProgressPlugin.cs knowledge/decision-log.md
git commit -m "feat(plugins): guard and progress plug-ins for portal answers (AD-053)"
```

---

### Task 3: Registration script

**Files:**
- Create: `plugins/deploy/Register-ResponseGuard.ps1`

**Interfaces:**
- Consumes: plug-in type names from Task 2.
- Produces: nothing consumed by later tasks. This is a deployment step, not a code dependency.

Note: unlike `Register-SubmitReview.ps1`, these are **not** custom APIs. They are SDK message processing steps, so there is no `customapi.json` contract and no binding assertion; the script creates `plugintype` rows and `sdkmessageprocessingstep` rows instead.

- [ ] **Step 1: Write the script**

Create `plugins/deploy/Register-ResponseGuard.ps1`:

```powershell
<#
.SYNOPSIS
    Registers the ResponseGuard and ResponseProgress plug-in steps (AD-053).

.DESCRIPTION
    These are SDK message processing steps, not Custom APIs, so nothing here touches
    customapis. Steps:
      1. pac plugin push  - refreshes the assembly.
      2. plugintype rows  - created explicitly; pac plugin push does not create them
                            for classes the environment has not seen (AD-052).
      3. sdkmessageprocessingstep rows:
           ResponseGuardPlugin    Create/Update  al_response  stage 20 (pre-op)  sync
           ResponseGuardPlugin    Associate/Disassociate      stage 20           sync
           ResponseProgressPlugin Create/Update  al_response  stage 40 (post-op) sync
      4. Pre-images on the Update steps, so the guard sees columns the Target omits.

    Idempotent: existing components are looked up and reused rather than duplicated.

.PARAMETER OrgUrl
    The Dataverse environment URL to deploy to. No environment is assumed or defaulted.

.PARAMETER AccessToken
    A bearer token for the Dataverse Web API of OrgUrl.

.PARAMETER PluginAssemblyId
    Id of the existing OutcomeTestingPlugins assembly registered in the environment.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OrgUrl,
    [Parameter(Mandatory = $true)][string]$AccessToken,
    [string]$SolutionUniqueName = 'OutcomeTesting',
    [string]$PluginAssemblyId = '7b51d0d1-f5a1-f111-b8dd-e4fade069307',
    [string]$PacPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pluginProject = Join-Path $root 'OutcomeTesting.Plugins'

$api = "$($OrgUrl.TrimEnd('/'))/api/data/v9.2"
$headers = @{
    Authorization              = "Bearer $AccessToken"
    'OData-MaxVersion'         = '4.0'
    'OData-Version'            = '4.0'
    Accept                     = 'application/json'
    'Content-Type'             = 'application/json; charset=utf-8'
    'MSCRM.SolutionUniqueName' = $SolutionUniqueName
}

function Invoke-Dv {
    param([string]$Method, [string]$Path, [object]$Body)
    $json = if ($Body) { $Body | ConvertTo-Json -Depth 6 } else { $null }
    return Invoke-RestMethod -Method $Method -Uri "$api/$Path" -Headers $headers -Body $json
}

function Get-OrCreate {
    param([string]$Set, [string]$IdField, [string]$Filter, [object]$Body)
    $existing = Invoke-Dv -Method Get -Path "$Set`?`$filter=$Filter&`$select=$IdField"
    if ($existing.value.Count -gt 0) {
        Write-Host "  exists: $Filter"
        return $existing.value[0].$IdField
    }
    $resp = Invoke-RestMethod -Method Post -Uri "$api/$Set" -Headers ($headers + @{ Prefer = 'return=representation' }) -Body ($Body | ConvertTo-Json -Depth 6)
    Write-Host "  created: $Filter"
    return $resp.$IdField
}

Write-Host '0. Resolving PAC CLI...'
if (-not $PacPath) {
    $onPath = Get-Command pac -ErrorAction SilentlyContinue
    if ($onPath) {
        $PacPath = $onPath.Source
    }
    else {
        $found = Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA 'Microsoft\PowerAppsCLI') -Filter 'pac.exe' -Recurse -ErrorAction SilentlyContinue |
                 Sort-Object FullName -Descending | Select-Object -First 1
        if ($found) { $PacPath = $found.FullName }
    }
}
if (-not $PacPath -or -not (Test-Path -LiteralPath $PacPath)) {
    throw "PAC CLI not found. Pass -PacPath with the full path to pac.exe, or add it to PATH."
}
Write-Host "  using: $PacPath"

Write-Host '1. Pushing plug-in assembly...'
$dll = Join-Path $pluginProject 'bin\Debug\net462\OutcomeTesting.Plugins.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    throw "Plug-in assembly not found: $dll. Run 'dotnet build' in $pluginProject first."
}
& $PacPath plugin push --pluginId $PluginAssemblyId --pluginFile $dll --type Assembly
if ($LASTEXITCODE -ne 0) {
    throw "pac plugin push failed with exit code $LASTEXITCODE. Fix the push, then re-run; the Web API steps below are idempotent."
}

function Resolve-PluginType {
    param([string]$TypeName)
    $pt = Invoke-Dv -Method Get -Path "plugintypes`?`$filter=typename eq '$TypeName'&`$select=plugintypeid"
    if ($pt.value.Count -gt 0) {
        Write-Host "  exists: $TypeName"
        return $pt.value[0].plugintypeid
    }
    $shortName = $TypeName -replace '^.*\.', ''
    $body = [ordered]@{
        typename                      = $TypeName
        name                          = $TypeName
        friendlyname                  = $shortName
        'pluginassemblyid@odata.bind' = "/pluginassemblies($PluginAssemblyId)"
    }
    $created = Invoke-RestMethod -Method Post -Uri "$api/plugintypes" -Headers ($headers + @{ Prefer = 'return=representation' }) -Body ($body | ConvertTo-Json -Depth 4)
    Write-Host "  created: $TypeName"
    return $created.plugintypeid
}

Write-Host '2. Resolving plug-in types...'
$guardTypeId = Resolve-PluginType -TypeName 'OutcomeTesting.Plugins.ResponseGuardPlugin'
$progressTypeId = Resolve-PluginType -TypeName 'OutcomeTesting.Plugins.ResponseProgressPlugin'

function Get-MessageId {
    param([string]$Name)
    $m = Invoke-Dv -Method Get -Path "sdkmessages`?`$filter=name eq '$Name'&`$select=sdkmessageid"
    if ($m.value.Count -eq 0) { throw "SDK message '$Name' not found." }
    return $m.value[0].sdkmessageid
}

function Get-MessageFilterId {
    param([string]$MessageId, [string]$Entity)
    $f = Invoke-Dv -Method Get -Path "sdkmessagefilters`?`$filter=_sdkmessageid_value eq $MessageId and primaryobjecttypecode eq '$Entity'&`$select=sdkmessagefilterid"
    if ($f.value.Count -eq 0) { throw "No message filter for '$Entity' on that message." }
    return $f.value[0].sdkmessagefilterid
}

# stage 20 = pre-operation, 40 = post-operation; mode 0 = synchronous.
function New-Step {
    param(
        [string]$Name, [string]$PluginTypeId, [string]$Message,
        [string]$Entity, [int]$Stage, [string]$FilteringAttributes
    )
    $messageId = Get-MessageId -Name $Message
    $body = [ordered]@{
        name                            = $Name
        stage                           = $Stage
        mode                            = 0
        rank                            = 1
        supporteddeployment             = 0
        invocationsource                = 0
        'sdkmessageid@odata.bind'       = "/sdkmessages($messageId)"
        'plugintypeid@odata.bind'       = "/plugintypes($PluginTypeId)"
    }
    if ($Entity) {
        $filterId = Get-MessageFilterId -MessageId $messageId -Entity $Entity
        $body['sdkmessagefilterid@odata.bind'] = "/sdkmessagefilters($filterId)"
    }
    if ($FilteringAttributes) {
        $body['filteringattributes'] = $FilteringAttributes
    }
    return Get-OrCreate -Set 'sdkmessageprocessingsteps' -IdField 'sdkmessageprocessingstepid' -Filter "name eq '$Name'" -Body $body
}

Write-Host '3. Registering steps...'
$answerColumns = 'al_answertext,al_answerchoice,al_answerchoices,al_answerdate'

$guardCreate = New-Step -Name 'ResponseGuard: Create of al_response' -PluginTypeId $guardTypeId -Message 'Create' -Entity 'al_response' -Stage 20
$guardUpdate = New-Step -Name 'ResponseGuard: Update of al_response' -PluginTypeId $guardTypeId -Message 'Update' -Entity 'al_response' -Stage 20 -FilteringAttributes $answerColumns

# Associate and Disassociate carry no per-entity message filter, so these register
# unfiltered and the plug-in checks the relationship name itself.
New-Step -Name 'ResponseGuard: Associate fail reason' -PluginTypeId $guardTypeId -Message 'Associate' -Stage 20 | Out-Null
New-Step -Name 'ResponseGuard: Disassociate fail reason' -PluginTypeId $guardTypeId -Message 'Disassociate' -Stage 20 | Out-Null

$progressCreate = New-Step -Name 'ResponseProgress: Create of al_response' -PluginTypeId $progressTypeId -Message 'Create' -Entity 'al_response' -Stage 40
$progressUpdate = New-Step -Name 'ResponseProgress: Update of al_response' -PluginTypeId $progressTypeId -Message 'Update' -Entity 'al_response' -Stage 40 -FilteringAttributes $answerColumns

Write-Host '4. Registering pre-images on the Update steps...'
# imagetype 0 = pre-image. The guard needs the review and question links on Update,
# which the Target omits because only changed columns travel in it.
foreach ($step in @(
    @{ Id = $guardUpdate;    Name = 'ResponseGuard Update pre-image' },
    @{ Id = $progressUpdate; Name = 'ResponseProgress Update pre-image' }
)) {
    $body = [ordered]@{
        name                                        = $step.Name
        entityalias                                 = 'PreImage'
        imagetype                                   = 0
        attributes                                  = "al_reviewinstanceid,al_questionversionid,$answerColumns"
        'sdkmessageprocessingstepid@odata.bind'     = "/sdkmessageprocessingsteps($($step.Id))"
    }
    Get-OrCreate -Set 'sdkmessageprocessingstepimages' -IdField 'sdkmessageprocessingstepimageid' -Filter "name eq '$($step.Name)'" -Body $body | Out-Null
}

Write-Host "Done. ResponseGuard and ResponseProgress are registered in '$SolutionUniqueName'." -ForegroundColor Green
```

- [ ] **Step 2: Verify the script parses**

Run: `powershell -NoProfile -Command "[void][System.Management.Automation.Language.Parser]::ParseFile('plugins/deploy/Register-ResponseGuard.ps1', [ref]$null, [ref]$null); 'parsed'"`
Expected: prints `parsed` with no parse errors. (Executing it needs a live environment and a token; that is a deployment step, not part of this task's verification.)

- [ ] **Step 3: Commit**

```bash
git add plugins/deploy/Register-ResponseGuard.ps1
git commit -m "feat(deploy): register the response guard and progress steps"
```

---

### Task 4: Correct Q-TAX-02's response type

**Files:**
- Modify: `data/v8-seed/data.xml` — the Q-TAX-02-V1 record
- Modify: `knowledge/checklist-v8.md` — the S-TAX table and the "Not answered by the document" list
- Modify: `knowledge/decision-log.md` — append AD-055

**Interfaces:**
- Consumes: nothing.
- Produces: `Single select` (`120910003`) is now used only by Q-GR-02, which is what makes `ResponseRules.PermittedChoices(120910003)` unambiguous. Task 1's implementation already assumes this.

- [ ] **Step 1: Change the seed record**

In `data/v8-seed/data.xml`, inside the `al_questionversion` entity block, find the record whose `al_questionversioncode` is `Q-TAX-02-V1` and change:

```xml
<field name="al_responsetype" value="120910003" />
```

to:

```xml
<field name="al_responsetype" value="120910006" />
```

- [ ] **Step 2: Verify exactly one Single select question remains**

Run:

```bash
python -c "
import re
x=open('data/v8-seed/data.xml',encoding='utf-8').read()
blk=re.search(r'<entity name=\"al_questionversion\".*?</entity>', x, re.S).group(0)
hits=[re.search(r'al_questionversioncode\" value=\"([^\"]*)\"',r.group(1)).group(1)
      for r in re.finditer(r'<record id=\"[^\"]*\">(.*?)</record>', blk, re.S)
      if re.search(r'al_responsetype\" value=\"120910003\"', r.group(1))]
print(hits)
"
```

Expected: `['Q-GR-02-V1']`

- [ ] **Step 3: Update the checklist catalogue**

In `knowledge/checklist-v8.md`, in the S-TAX table, change the Q-TAX-02 row's response type from `SingleSelect` to `PassFailInsufficient`, and append to the note below that table:

```markdown
Q-TAX-02 is recorded as `PassFailInsufficient`, not `SingleSelect`. Its options in the source document are PASS, INSUFFICIENT EVIDENCE, FAIL, which is exactly that scale; "SingleSelect" in the transcription described cardinality rather than a vocabulary. See AD-055.
```

Then add to the "Not answered by the document" list:

```markdown
4. Per-question option lists for `SingleSelect`. The schema has none, so a `SingleSelect` question's options are fixed by its response type. Resolved for V8 by AD-055; a future question needing its own list would need a schema change.
```

- [ ] **Step 4: Record AD-055**

Append to the decision table in `knowledge/decision-log.md`:

```markdown
| AD-055 | `al_QuestionVersion` carries no per-question option list, so a `Single select` question's options are fixed by its response type. Q-TAX-02 (Tax check outcome) is retyped from `Single select` to `Pass / Fail / Insufficient evidence` in `data/v8-seed`, leaving `Single select` used only by Q-GR-02 (Primary root cause) and therefore unambiguous. | Two seeded questions used `Single select` with different vocabularies, and nothing in the schema distinguished them, so a portal control could not know which options to render. The resolution is read from the questions rather than invented: Q-TAX-02's own options in `knowledge/checklist-v8.md` are PASS, INSUFFICIENT EVIDENCE, FAIL, which is precisely the existing `Pass / Fail / Insufficient evidence` scale; the "SingleSelect" label described cardinality, not a vocabulary. Retyping avoids adding an option-list column that AD-022 deliberately excluded. This is a correction to unpublished DEV seed data re-imported under AD-027, not an amendment to a version any review has been issued, so BR-013 and PP-09 immutability are not engaged. A future question needing its own list would need the schema change AD-022 declined. | 2026-08-29 |
```

- [ ] **Step 5: Re-import the seed**

Run: `pac data import --data data\v8-seed`
Expected: succeeds. Under AD-027 a re-run updates existing rows rather than duplicating them; the record count stays at 117.

- [ ] **Step 6: Commit**

```bash
git add data/v8-seed/data.xml knowledge/checklist-v8.md knowledge/decision-log.md
git commit -m "fix(seed): retype Q-TAX-02 to Pass/Fail/Insufficient evidence (AD-055)"
```

---

### Task 5: Read-all / write-own table permissions and Web API site settings

**Files:**
- Create: `.../table-permissions/Review-Instance-All-Read.tablepermission.yml` (Global read)
- Create: `.../table-permissions/Review-Instance-Assigned.tablepermission.yml` (Contact, anchors write)
- Create: `.../table-permissions/Response-All-Read.tablepermission.yml` (Global read)
- Create: `.../table-permissions/Response-Of-Assigned-Review.tablepermission.yml` (Parent; **the edit boundary**)
- Create: `.../table-permissions/Fail-Reason-Read.tablepermission.yml` (Global read)
- Rename: `PROVISIONAL-DEV-ONLY---OutcomeCase` to `Outcome-Case-All-Read.tablepermission.yml`, keeping its id so upload updates the same row
- Modify: `powerpages/outcome-testing---outcometesting/sitesetting.yml`
- Delete: `powerpages/outcome-testing---outcometesting/table-permissions/PROVISIONAL-DEV-ONLY---ReviewInstance.tablepermission.yml`
- Delete: `powerpages/outcome-testing---outcometesting/table-permissions/PROVISIONAL-DEV-ONLY---Response.tablepermission.yml`
- Delete: `powerpages/outcome-testing---outcometesting/table-permissions/PROVISIONAL-DEV-ONLY---FailReason.tablepermission.yml`

**Interfaces:**
- Consumes: `al_reviewinstance.al_assignedcontactid` and the `contact_al_reviewinstance` relationship, both built under AD-050.
- Produces: read on all cases, reviews and responses, and write on `al_response` for the assigned checker only (AD-056). Tasks 6–8 depend on the write half; without it every save returns 403.

Reviewers **read everything and change only what is assigned to them** (product owner direction 2026-08-29). Power Pages unions table permissions, so this is a Global read alongside a Contact-anchored write chain, and the edit boundary is the single `Response-Of-Assigned-Review` row. This supersedes the AD-047 no-Global-access clause for reviewer roles and retires the "cannot view an unassigned case by editing the URL" negative test for them; every write-side negative test is unchanged.

Scope values: Global `756150000`, Contact `756150001`, Account `756150002`, Parent `756150003`, Self `756150004`, Custom `756150005`. Web role ids are the two already used by the provisional permissions: Tax Reviewer `c53b2908-1fc1-4470-89cd-6f5b95c17ffe`, AQS Reviewer `e24b50c5-1443-4725-84c9-70355724547f`.

Column names verified against the live environment, not assumed. Every existing permission in the repository is Global scope, so none of them exercises the Contact or Parent keys. `pac modelbuilder build --entitynamesfilter mspp_entitypermission` against Env_AQ_Dev gives the authoritative set: `mspp_contactrelationship`, `mspp_parententitypermission`, `mspp_parentrelationship`. The site metadata table is `mspp_entitypermission` — `adx_entitypermission` does not exist in this environment — while `pac pages` YAML keeps the legacy `adx_` prefix, and every key in the downloaded files maps `mspp_*` to `adx_*` one for one.

- [ ] **Step 1: Create the Contact-scoped review permission**

Create `powerpages/outcome-testing---outcometesting/table-permissions/Review-Instance-Assigned.tablepermission.yml`:

```yaml
adx_append: false
adx_appendto: false
adx_create: false
adx_delete: false
adx_entitylogicalname: al_reviewinstance
adx_entityname: Review Instance - assigned to me
adx_entitypermission_webrole:
- c53b2908-1fc1-4470-89cd-6f5b95c17ffe
- e24b50c5-1443-4725-84c9-70355724547f
adx_entitypermissionid: a1000000-0000-4000-8000-000000000071
adx_read: true
adx_scope: 756150001
adx_contactrelationship: contact_al_reviewinstance
adx_write: false
```

- [ ] **Step 2: Create the child response permission**

Create `powerpages/outcome-testing---outcometesting/table-permissions/Response-Of-Assigned-Review.tablepermission.yml`:

```yaml
adx_append: true
adx_appendto: true
adx_create: true
adx_delete: false
adx_entitylogicalname: al_response
adx_entityname: Response - on a review assigned to me
adx_entitypermission_webrole:
- c53b2908-1fc1-4470-89cd-6f5b95c17ffe
- e24b50c5-1443-4725-84c9-70355724547f
adx_entitypermissionid: a1000000-0000-4000-8000-000000000072
adx_parententitypermission: a1000000-0000-4000-8000-000000000071
adx_parentrelationship: al_reviewinstance_response
adx_read: true
adx_scope: 756150003
adx_write: true
```

Delete is deliberately false. An answer is cleared by writing empty values, which keeps the row and its audit history; deleting it would lose both (BR-012).

- [ ] **Step 3: Create the fail reason read permission**

Create `powerpages/outcome-testing---outcometesting/table-permissions/Fail-Reason-Read.tablepermission.yml`:

```yaml
adx_append: false
adx_appendto: true
adx_create: false
adx_delete: false
adx_entitylogicalname: al_failreason
adx_entityname: Fail Reason - read
adx_entitypermission_webrole:
- c53b2908-1fc1-4470-89cd-6f5b95c17ffe
- e24b50c5-1443-4725-84c9-70355724547f
adx_entitypermissionid: a1000000-0000-4000-8000-000000000073
adx_read: true
adx_scope: 756150000
adx_write: false
```

Global read is correct here and only here: fail reasons are reference data with no case, client or reviewer content, which is the same treatment AD-047 gives `al_ChecklistVersion`, `al_Section` and `al_QuestionVersion`.

- [ ] **Step 4: Remove the three provisional permissions they replace**

```bash
git rm powerpages/outcome-testing---outcometesting/table-permissions/PROVISIONAL-DEV-ONLY---ReviewInstance.tablepermission.yml \
       powerpages/outcome-testing---outcometesting/table-permissions/PROVISIONAL-DEV-ONLY---Response.tablepermission.yml \
       powerpages/outcome-testing---outcometesting/table-permissions/PROVISIONAL-DEV-ONLY---FailReason.tablepermission.yml
```

Leaving them in place would defeat the change: Power Pages unions permissions, so a surviving Global-scope read on `al_reviewinstance` would keep every review visible to both roles.

- [ ] **Step 5: Add the Web API site settings**

Append to `powerpages/outcome-testing---outcometesting/sitesetting.yml`, following the shape of the existing `OutcomeTesting/Flow/SubmitReview` entry:

```yaml
- adx_name: Webapi/al_response/enabled
  adx_sitesettingid: a1000000-0000-4000-8000-0000000000a1
  adx_value: 'true'
- adx_name: Webapi/al_response/fields
  adx_sitesettingid: a1000000-0000-4000-8000-0000000000a2
  adx_value: al_answertext,al_answerchoice,al_answerchoices,al_answerdate
```

The field list is the allowlist the Web API enforces. It names only the four answer columns, so `al_responsecode`, `al_name` and both lookups cannot be written from the browser even by a crafted request — which is what lets the guard plug-in stamp the response code as the sole authority.

- [ ] **Step 6: Upload and verify the negative case**

Run: `pac pages upload --path powerpages/outcome-testing---outcometesting`

Then, signed in as a Tax Reviewer contact, against a review assigned to a **different** contact:

| Request | Expected |
|---|---|
| `GET /_api/al_reviewinstances(<other-review-id>)` | **Returns the record.** Reading all reviews is intended under AD-056. |
| `GET /_api/al_responses?$filter=_al_reviewinstanceid_value eq <other-review-id>` | **Returns the answers.** Also intended. |
| `PATCH /_api/al_responses(<other-reviewers-response-id>)` | **403.** This is the edit boundary and the only test that still gates release. |
| `PATCH` a response on a review assigned to me | Succeeds. |

If the third row succeeds, the Contact-anchored write chain is not working: check that `Response-Of-Assigned-Review` still carries `adx_scope: 756150003` with its parent id, and that nothing grants Global write on `al_response`.

- [ ] **Step 7: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/table-permissions powerpages/outcome-testing---outcometesting/sitesetting.yml
git commit -m "feat(powerpages): Contact-scoped review and response permissions (AD-047)"
```

---

### Task 6: Render typed answer controls

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`
- Modify: `powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css`

**Interfaces:**
- Consumes: the table permissions from Task 5.
- Produces: each answerable question renders inside a container carrying
  `data-ot-answer`, `data-question-version="<guid>"`, `data-response-id="<guid or empty>"`,
  `data-response-type="<int>"` and `data-column="text|date|choice|choices"`.
  Task 7's script binds to exactly these attributes.

- [ ] **Step 1: Filter sections by owner role**

In the template, the existing `questions` FetchXML links to `al_section` with a filter on `al_checklistversionid`. Add the owner-role condition to that same `<filter>`, so the query returns only this discipline's sections:

```liquid
{% if rv.al_reviewtype.value == 120910200 %}
  {% assign owner_role = 120910100 %}
{% else %}
  {% assign owner_role = 120910101 %}
{% endif %}
```

and inside the `al_section` link-entity filter:

```xml
<condition attribute="al_ownerrole" operator="eq" value="{{ owner_role }}" />
```

- [ ] **Step 2: Replace the read-only answer cell with controls**

The current markup renders each question as a `<tr>` with a static answer cell. Replace the table with a definition-style list so each question can carry a fieldset, a note and a status line. Substitute the whole `<tbody>` loop body with:

```liquid
{% assign qv_id = qv.al_questionversionid %}
{% assign rt = qv.al_responsetype.value %}

{% assign existing = null %}
{% for a in arows %}
  {% if a.al_questionversionid.id == qv_id %}{% assign existing = a %}{% endif %}
{% endfor %}

{% if rt == 120910000 or rt == 120910001 %}{% assign column = 'text' %}
{% elsif rt == 120910002 %}{% assign column = 'date' %}
{% elsif rt == 120910004 %}{% assign column = 'choices' %}
{% else %}{% assign column = 'choice' %}
{% endif %}

<div class="ot-answer"
     data-ot-answer
     data-question-version="{{ qv_id }}"
     data-response-id="{{ existing.al_responseid }}"
     data-response-type="{{ rt }}"
     data-column="{{ column }}">

  <fieldset class="ot-answer__field">
    <legend class="ot-answer__legend">
      {{ qv.al_questiontext | escape }}
      {% if qv.al_ismandatory %}<span class="ot-required">Required</span>{% endif %}
    </legend>

    {% if column == 'text' %}
      {% if rt == 120910001 %}
        <textarea class="ot-answer__input" rows="4"
                  data-ot-input
                  aria-label="{{ qv.al_questiontext | escape }}">{{ existing.al_answertext | escape }}</textarea>
      {% else %}
        <input type="text" class="ot-answer__input"
               data-ot-input
               value="{{ existing.al_answertext | escape }}"
               aria-label="{{ qv.al_questiontext | escape }}" />
      {% endif %}

    {% elsif column == 'date' %}
      <input type="date" class="ot-answer__input"
             data-ot-input
             value="{{ existing.al_answerdate | date: 'yyyy-MM-dd' }}"
             aria-label="{{ qv.al_questiontext | escape }}" />

    {% elsif column == 'choices' %}
      {% assign options = '120910340,120910341,120910342,120910343,120910344' | split: ',' %}
      {% assign labels = 'LSA/LSDBA/TTFAC,Trust,IHT,Tax calculation,Other' | split: ',' %}
      {% for opt in options %}
        <label class="ot-answer__option">
          <input type="checkbox" data-ot-input value="{{ opt }}"
                 {% if existing.al_answerchoices contains labels[forloop.index0] %}checked{% endif %} />
          <span>{{ labels[forloop.index0] }}</span>
        </label>
      {% endfor %}

    {% else %}
      {% include 'OT Answer Options' response_type: rt selected: existing.al_answerchoice.value question: qv_id %}
    {% endif %}

    {% comment %}
      al_answertext doubles as the evidence note held with a structured answer, which is
      how the Code App already reads the record (useReviewDetail.ts noteOf). The note is
      revealed by the client only for a non-pass selection; it is optional under AD-019.
    {% endcomment %}
    {% if column == 'choice' or column == 'choices' %}
      <div class="ot-answer__note" data-ot-note hidden>
        <label class="ot-answer__note-label" for="note-{{ qv_id }}">
          Evidence or observation (optional)
        </label>
        <textarea id="note-{{ qv_id }}" class="ot-answer__input" rows="3"
                  data-ot-note-input>{{ existing.al_answertext | escape }}</textarea>
      </div>
    {% endif %}
  </fieldset>

  <p class="ot-answer__status" data-ot-status aria-live="polite"></p>
</div>
```

- [ ] **Step 3: Create the shared option renderer**

Create `powerpages/outcome-testing---outcometesting/web-templates/ot-answer-options/OT-Answer-Options.webtemplate.source.html`:

```liquid
{% comment %}
  Radio group for one single-choice question. Include with:
  (% include 'OT Answer Options' response_type: 120910005 selected: 120910301 question: '<guid>' %)

  The permitted subset per response type is the AD-023 rule; it is mirrored here for
  rendering and enforced server-side by ResponseRules.PermittedChoices, which is the
  authority. A value added here without a matching change there is refused on save.
{% endcomment %}
{% case response_type %}
  {% when 120910005 %}
    {% assign values = '120910300,120910301' | split: ',' %}
    {% assign labels = 'Pass,Fail' | split: ',' %}
  {% when 120910006 %}
    {% assign values = '120910300,120910301,120910302' | split: ',' %}
    {% assign labels = 'Pass,Fail,Insufficient evidence' | split: ',' %}
  {% when 120910007 %}
    {% assign values = '120910305,120910306' | split: ',' %}
    {% assign labels = 'Yes,No' | split: ',' %}
  {% when 120910008 %}
    {% assign values = '120910305,120910306,120910307' | split: ',' %}
    {% assign labels = 'Yes,No,N/A' | split: ',' %}
  {% when 120910009 %}
    {% assign values = '120910305,120910306,120910302' | split: ',' %}
    {% assign labels = 'Yes,No,Insufficient evidence' | split: ',' %}
  {% when 120910010 %}
    {% assign values = '120910300,120910303,120910302,120910304' | split: ',' %}
    {% assign labels = 'Pass,Pass with issues,Insufficient evidence,Potential harm' | split: ',' %}
  {% else %}
    {% assign values = '120910320,120910321,120910322,120910323,120910324,120910325,120910326,120910327,120910328' | split: ',' %}
    {% assign labels = 'FactFind quality,Risk / capacity mismatch,Research / rationale,Charges / value,Client communication,Process / documentation,AML / CRA,Retirement Proposition,Adviser judgement' | split: ',' %}
{% endcase %}

<div class="ot-answer__options">
  {% for value in values %}
    <label class="ot-answer__option">
      <input type="radio"
             name="q-{{ question }}"
             value="{{ value }}"
             data-ot-input
             {% if selected and selected == value %}checked{% endif %} />
      <span>{{ labels[forloop.index0] }}</span>
    </label>
  {% endfor %}
</div>
```

Create its manifest `powerpages/outcome-testing---outcometesting/web-templates/ot-answer-options/OT-Answer-Options.webtemplate.yml`:

```yaml
adx_name: OT Answer Options
adx_webtemplateid: a1000000-0000-4000-8000-000000000021
```

The `adx_name` is the string the `{% include %}` in Step 2 resolves; the id continues the `a1000000-…` block the site's other templates use.

- [ ] **Step 4: Add the CSS**

Append to `powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css`:

```css
/* Answer controls (PP-07, PP-08). Focus is always visible and status is never
   conveyed by colour alone, both NFR-ACC-01 requirements. */
.ot-answer { border-bottom: 1px solid var(--ot-rule, #e5e5e5); padding: 1rem 0; }
.ot-answer:last-child { border-bottom: 0; }
.ot-answer__field { border: 0; margin: 0; padding: 0; }
.ot-answer__legend { font-weight: 500; margin-bottom: 0.5rem; padding: 0; }
.ot-answer__options { display: flex; flex-wrap: wrap; gap: 0.75rem; }
.ot-answer__option { display: inline-flex; align-items: center; gap: 0.4rem; cursor: pointer; }
.ot-answer__input { width: 100%; max-width: 46rem; padding: 0.5rem; font: inherit; }
.ot-answer__input:focus-visible,
.ot-answer__option input:focus-visible { outline: 3px solid var(--portalThemeColor1, #005a9e); outline-offset: 2px; }
.ot-answer__note { margin-top: 0.75rem; }
.ot-answer__note-label { display: block; margin-bottom: 0.25rem; font-size: 0.9rem; }
.ot-answer__status { min-height: 1.25rem; margin: 0.35rem 0 0; font-size: 0.85rem; }
.ot-answer__status--saving { color: var(--ot-muted, #595959); }
.ot-answer__status--saved { color: var(--ot-muted, #595959); }
.ot-answer__status--error { color: var(--ot-error, #a4262c); font-weight: 500; }
```

- [ ] **Step 5: Upload and check rendering**

Run: `pac pages upload --path powerpages/outcome-testing---outcometesting`

Open `/review?id=<a Tax review id>` and confirm: only S-TAX sections render; Q-TAX-01 shows five checkboxes; Q-TAX-02 shows three radios reading Pass, Fail, Insufficient evidence; Q-TAX-03 shows a textarea. Then open an AQS review and confirm S-TAX does not appear.

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting
git commit -m "feat(powerpages): render typed answer controls on the review page"
```

---

### Task 7: Autosave

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`

**Interfaces:**
- Consumes: the `data-ot-*` attributes from Task 6 and the Web API settings from Task 5.
- Produces: a saved `al_response` row per answered question, which `al_SubmitReview` already reads.

- [ ] **Step 1: Add the autosave script**

Append inside the `{% else %}` branch that renders when the review is not submitted, after the existing submit script:

```html
<script>
  /*
   * Answer autosave (PP-07, PP-08, AD-053).
   *
   * Path: page -> Portals Web API -> al_response -> ResponseGuardPlugin.
   *
   * Unlike submission, this needs no cloud flow: the Web API supports create and
   * update, and only actions are unsupported. Every rule that matters - the
   * submission lock, the typed-column match, the option subset and section
   * ownership - is enforced in the plug-in, so this script is convenience, never
   * access control (NFR-SEC-01).
   */
  (function () {
    'use strict';

    var DEBOUNCE_MS = 800;
    var NON_PASS = [120910301, 120910302, 120910304];

    function api(method, path, body, onDone, onFail) {
      var xhr = new XMLHttpRequest();
      xhr.open(method, '/_api/' + path, true);
      xhr.setRequestHeader('Content-Type', 'application/json');
      xhr.setRequestHeader('Accept', 'application/json');
      xhr.setRequestHeader('OData-MaxVersion', '4.0');
      xhr.setRequestHeader('OData-Version', '4.0');

      // Power Pages requires the request verification token on every write.
      var token = document.querySelector('[name="__RequestVerificationToken"]');
      if (token) { xhr.setRequestHeader('__RequestVerificationToken', token.value); }

      xhr.onload = function () {
        if (xhr.status >= 200 && xhr.status < 300) {
          onDone(xhr);
        } else {
          onFail(xhr.responseText || xhr.statusText);
        }
      };
      xhr.onerror = function () { onFail(''); };
      xhr.send(body ? JSON.stringify(body) : null);
    }

    /*
     * The plug-in prefixes its failures so the client can branch without parsing
     * prose. Messages say what happened, what is still safe and what to do next, and
     * never expose a table name, query, id or stack trace (PP-16).
     */
    function friendlyError(raw) {
      var text = String(raw || '');
      if (text.indexOf('CONFLICT:') !== -1) {
        return 'This review has been submitted and can no longer be changed. Reload the page.';
      }
      if (text.indexOf('UNAUTHORIZED:') !== -1) {
        return 'You do not have permission to change this answer.';
      }
      if (text.indexOf('PRECONDITION:') !== -1) {
        var idx = text.indexOf('PRECONDITION:');
        return text.substring(idx + 'PRECONDITION:'.length).trim();
      }
      return 'Not saved - retry. Your other answers are unaffected.';
    }

    function idFromHeader(xhr) {
      // Create returns the new row's URI in OData-EntityId, e.g.
      // https://host/_api/al_responses(<guid>)
      var header = xhr.getResponseHeader('OData-EntityId') || '';
      var match = header.match(/\(([0-9a-f-]{36})\)/i);
      return match ? match[1] : null;
    }

    function two(n) { return (n < 10 ? '0' : '') + n; }

    function bind(root) {
      var questionVersion = root.getAttribute('data-question-version');
      var column = root.getAttribute('data-column');
      var status = root.querySelector('[data-ot-status]');
      var note = root.querySelector('[data-ot-note]');
      var noteInput = root.querySelector('[data-ot-note-input]');
      var timer = null;

      function show(message, kind) {
        if (!status) { return; }
        status.textContent = message;
        status.className = 'ot-answer__status ot-answer__status--' + kind;
      }

      function currentPayload() {
        var body = {};
        var inputs = root.querySelectorAll('[data-ot-input]');
        var i;

        if (column === 'text') {
          body.al_answertext = inputs[0].value || null;
        } else if (column === 'date') {
          body.al_answerdate = inputs[0].value || null;
        } else if (column === 'choice') {
          body.al_answerchoice = null;
          for (i = 0; i < inputs.length; i++) {
            if (inputs[i].checked) { body.al_answerchoice = parseInt(inputs[i].value, 10); }
          }
          body.al_answertext = noteInput && noteInput.value ? noteInput.value : null;
        } else {
          var picked = [];
          for (i = 0; i < inputs.length; i++) {
            if (inputs[i].checked) { picked.push(inputs[i].value); }
          }
          // A multi-select choice column is sent as a comma-separated value list.
          body.al_answerchoices = picked.length ? picked.join(',') : null;
          body.al_answertext = noteInput && noteInput.value ? noteInput.value : null;
        }

        return body;
      }

      function revealNote() {
        if (!note) { return; }
        var chosen = null;
        var inputs = root.querySelectorAll('[data-ot-input]');
        for (var i = 0; i < inputs.length; i++) {
          if (inputs[i].checked) { chosen = parseInt(inputs[i].value, 10); }
        }
        note.hidden = NON_PASS.indexOf(chosen) === -1;
      }

      function save() {
        var responseId = root.getAttribute('data-response-id');
        var body = currentPayload();
        show('Saving...', 'saving');

        function done() {
          var now = new Date();
          show('Saved ' + two(now.getHours()) + ':' + two(now.getMinutes()), 'saved');
        }

        function fail(raw) { show(friendlyError(raw), 'error'); }

        if (responseId) {
          api('PATCH', 'al_responses(' + responseId + ')', body, done, fail);
          return;
        }

        // The two lookups are set only on create; the Web API field allowlist does not
        // include them, so they travel as bind properties rather than columns.
        body['al_reviewinstanceid@odata.bind'] = '/al_reviewinstances(' + REVIEW_ID + ')';
        body['al_questionversionid@odata.bind'] = '/al_questionversions(' + questionVersion + ')';

        api('POST', 'al_responses', body, function (xhr) {
          var created = idFromHeader(xhr);
          if (created) { root.setAttribute('data-response-id', created); }
          done();
        }, fail);
      }

      function queue() {
        revealNote();
        if (timer) { window.clearTimeout(timer); }
        timer = window.setTimeout(save, DEBOUNCE_MS);
      }

      var inputs = root.querySelectorAll('[data-ot-input]');
      for (var i = 0; i < inputs.length; i++) {
        inputs[i].addEventListener('change', queue);
        if (inputs[i].tagName === 'TEXTAREA' || inputs[i].type === 'text') {
          inputs[i].addEventListener('input', queue);
        }
      }
      if (noteInput) { noteInput.addEventListener('input', queue); }

      revealNote();
    }

    var REVIEW_ID = document.querySelector('[data-ot-questionnaire]')
      ? document.querySelector('[data-ot-questionnaire]').getAttribute('data-review-id')
      : null;

    if (!REVIEW_ID) { return; }

    var answers = document.querySelectorAll('[data-ot-answer]');
    for (var i = 0; i < answers.length; i++) { bind(answers[i]); }
  })();
</script>
```

- [ ] **Step 2: Add the questionnaire wrapper the script reads the review id from**

Wrap the rendered sections in the template with:

```liquid
<div data-ot-questionnaire data-review-id="{{ rv.al_reviewinstanceid }}">
```

closing it after the final `{% endfor %}` of the section loop.

- [ ] **Step 3: Upload and test the happy path**

Run: `pac pages upload --path powerpages/outcome-testing---outcometesting`

As the assigned checker on a Tax review: select Pass on Q-TAX-02, wait a second, confirm the status line reads `Saved HH:MM`. Reload; the selection persists. In the Code App's review detail for the same review, the answer appears — confirming both front ends read one record shape.

- [ ] **Step 4: Test the guard rejections**

| Action | Expected |
|---|---|
| Answer a question, then submit the review, then edit the answer | Status shows the submitted-and-locked message; Dataverse unchanged |
| `PATCH /_api/al_responses(<id>)` with `al_answerchoice: 120910304` on a Pass/Fail question | Rejected; the status maps the PRECONDITION text |
| Answer any question on a review in status Assigned | Review moves to Review In Progress and `al_startedon` is stamped |
| Answer two questions quickly | Two rows, distinct `al_responsecode` values, no duplicates |

- [ ] **Step 5: Keyboard and screen reader pass**

Tab through one full section: every control reachable, focus visible, each radio group announced with its question as the legend, and the save status announced without stealing focus.

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting
git commit -m "feat(powerpages): autosave answers over the portal Web API"
```

---

### Task 8: Fail reasons

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`
- Modify: `knowledge/decision-log.md` — append AD-054

**Interfaces:**
- Consumes: the `data-ot-answer` container and `NON_PASS` reveal logic from Task 7, and the `al_failreason` permission from Task 5.
- Produces: `al_failreason_response` associations, which BR-010 fail-reason MI later reads.

- [ ] **Step 1: Fetch the fail reasons and current associations**

Add before the section loop:

```liquid
{% fetchxml failreasons %}
<fetch>
  <entity name="al_failreason">
    <attribute name="al_failreasonid" />
    <attribute name="al_name" />
    <attribute name="al_category" />
    <attribute name="al_displayorder" />
    <order attribute="al_displayorder" />
  </entity>
</fetch>
{% endfetchxml %}
```

- [ ] **Step 2: Render the picker inside each choice answer**

Inside the `{% if column == 'choice' or column == 'choices' %}` block from Task 6, before the note textarea:

```liquid
<div class="ot-answer__reasons" data-ot-reasons hidden>
  <p class="ot-answer__note-label">Fail reasons (FR-013 — select all that apply)</p>
  {% for fr in failreasons.results.entities %}
    <label class="ot-answer__option">
      <input type="checkbox" data-ot-reason value="{{ fr.al_failreasonid }}" />
      <span>{{ fr.al_category.label | escape }} — {{ fr.al_name | escape }}</span>
    </label>
  {% endfor %}
</div>
```

- [ ] **Step 3: Associate and disassociate on change**

Add to the `bind` function in the Task 7 script, after `revealNote`:

```javascript
      var reasons = root.querySelector('[data-ot-reasons]');

      function revealReasons(chosen) {
        if (!reasons) { return; }
        reasons.hidden = NON_PASS.indexOf(chosen) === -1;
      }

      /*
       * FR-013 permits several reasons on one answer, so this is an N:N association
       * rather than a column. A reason can only be attached once the answer row
       * exists, which is why it waits on data-response-id.
       */
      function toggleReason(box) {
        var responseId = root.getAttribute('data-response-id');
        if (!responseId) {
          show('Choose an answer first, then add its reasons.', 'error');
          box.checked = !box.checked;
          return;
        }

        if (box.checked) {
          api('POST',
            'al_responses(' + responseId + ')/al_failreason_response/$ref',
            { '@odata.id': '/_api/al_failreasons(' + box.value + ')' },
            function () { show('Saved', 'saved'); },
            function (raw) { box.checked = false; show(friendlyError(raw), 'error'); });
        } else {
          api('DELETE',
            'al_responses(' + responseId + ')/al_failreason_response/$ref?$id=/_api/al_failreasons(' + box.value + ')',
            null,
            function () { show('Saved', 'saved'); },
            function (raw) { box.checked = true; show(friendlyError(raw), 'error'); });
        }
      }

      var reasonBoxes = root.querySelectorAll('[data-ot-reason]');
      for (var r = 0; r < reasonBoxes.length; r++) {
        reasonBoxes[r].addEventListener('change', (function (box) {
          return function () { toggleReason(box); };
        })(reasonBoxes[r]));
      }
```

and call `revealReasons(chosen)` from inside `revealNote`, where `chosen` is already computed.

- [ ] **Step 4: Record AD-054**

Append to the decision table in `knowledge/decision-log.md`:

```markdown
| AD-054 | The fail-reason picker appears on any non-pass answer — Fail, Insufficient evidence or Potential harm — not only on the file quality outcome question, and is filtered by the section's category. | The V8 document files the 20 seed reasons under "File Quality fail points", but their categories span AML, Breach, Record Keeping and Tax check, which is wider than any one question; read literally, the AML, Breach and Tax-check rows would be unreachable seed data. Attaching on any non-pass follows BR-006, under which every non-pass outcome requires remediation and therefore needs a recorded reason for the adviser to work from, and FR-013, which permits several reasons on one answer. Confirmed by the product owner 2026-08-29. Recorded rather than assumed because the source document does not state the trigger. | 2026-08-29 |
```

- [ ] **Step 5: Test**

| Action | Expected |
|---|---|
| Select Pass on Q-FQ-01 | No reasons picker, no note |
| Select Fail on Q-FQ-01 | Reasons picker and note both appear |
| Tick two reasons | Both associate; reload shows both still ticked |
| Untick one | Disassociates; the other remains |
| Tick a reason before answering | Refused with "Choose an answer first", checkbox reverts |
| Submit the review, then tick a reason | Rejected by the guard with the locked message |

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting knowledge/decision-log.md
git commit -m "feat(powerpages): attach fail reasons to non-pass answers (AD-054, FR-013)"
```

---

## Traceability

| Requirement | Task |
|---|---|
| PP-07 Tax questionnaire, draft save, mandatory enforcement | 6, 7 (mandatory gate already in `al_SubmitReview`) |
| PP-08 AQS questionnaire, conditional reasons, no cross-discipline overwrite | 6 (section filter), 8 (reasons), 2 (server-side ownership check) |
| PP-09 portal reads only the assigned version | 2, 6 |
| PP-11 submission lock across forms, Web API and manipulated URLs | 2, 7 |
| PP-16 user-friendly errors | 7 |
| FR-012 response types | 1, 6 |
| FR-013 several fail reasons on one answer | 8 |
| FR-010 lifecycle transition | 2 |
| BR-012 audit and edit protection | 2, 5 |
| BR-013 historic question versions unaffected | 4 |
| NFR-SEC-01 security not in UI code | 2, 5 |
| NFR-ACC-01 WCAG 2.2 AA | 6, 7 |
| AD-047 Contact-scoped portal access | 5 |

## Deliberately not in this plan

- **S-CRP conditional rendering.** AD-021's derivation from case product/solution type needs the mapping list, which OD-016 still gates. CRP renders for every AQS review.
- **The SubmitReview cloud flow.** Never created — `OutcomeTesting/Flow/SubmitReview` is empty and `src/Workflows/` holds no flow. Answering is fully testable without it; reaching the locked state is not. Build it from `docs/submit-review-flow.md` before the Task 7 lock test and the Task 8 locked-reason test can pass.
- **`al_Notification`.** PP-15's nine events have no table. Separate module.
- **Case-access permissions.** OD-022. Task 5 scopes reviews and responses only.
