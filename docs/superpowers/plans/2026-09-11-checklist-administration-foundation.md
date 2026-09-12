# Checklist Administration — Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach every reader of the checklist that a section can be dated out, marked optional, or owned by both disciplines — before any command exists that can write those states.

**Architecture:** Three new columns on `al_section` plus one new `al_ownerrole` value, a pure-rules seam (`SectionRules`) that both plug-ins share, and five readers taught to honour it. No Custom API, no UI. Every state this plan teaches the system to read is set by hand for now; the commands that write it are plan two.

**Tech Stack:** C# plug-ins (Dataverse SDK, xUnit), React + TypeScript Code App, Power Pages Liquid/FetchXML, the `OutcomeTesting.Registration` CLI for metadata.

**Spec:** `docs/superpowers/specs/2026-09-11-checklist-administration-design.md`

## Global Constraints

- Security is enforced server-side, never in UI code. The app shows and hides; the plug-in decides (AGENTS.md, AD-041).
- `statecode` is never consulted to decide whether checklist content applies. Effective dates decide (AD-091).
- Audit Events are immutable. Nothing in this plan edits or deletes one (BR-012, NFR-AUD-01).
- New option values: `al_ownerrole` **Both = 120910105**. Do not reuse an existing value.
- Owner role values in force: Tax team `120910100`, AQS checker `120910101`.
- Review types: Tax `120910200`, AQS `120910201`.
- Plug-in tests run with `DOTNET_ROLL_FORWARD=Major`.
- `build -c Release` **before** `pushassembly` — it uploads `bin/Release` without building.
- Metadata writes read back from Dataverse rather than trusting the write (the OD-032 lesson).
- Do not hand-edit `app/src/generated/**`. It is regenerated from Dataverse.
- **Deploy to `Env_AQ_Dev` only** (`https://org0b075da8.crm11.dynamics.com/`), and to nothing else, unless the project owner names another environment for that specific promotion (direction, 2026-09-12). Confirm with `pac auth list` before every environment write and stop if the active row is not DEV. This plan never promotes; TEST took its first managed install on 2026-09-11, so promotion is a live path and each one is the owner's call.

**Branch:** branch from `fix/portal-signin-identity-username`, **not** from `main`.

`main` is 95 commits stale and carries nothing this branch does not. Every line reference in
this plan was read from the current branch, and the gap is not cosmetic: `SubmitReviewPlugin`
differs by 533 lines, `ResponseGuardPlugin` by 22, and `RemediationResponseGuardTests.cs` —
the fixture Task 5 copies its style from — does not exist on `main` at all.

```bash
git checkout -b feat/checklist-administration
```

Whether the portal-signin work merges before this does is a separate question; this plan only
needs the code it was written against.

---

### Task 1: `SectionRules` — the shared rule, free of Dataverse types

`ResponseRules` is deliberately free of Dataverse types so its rules can be tested without a fake organisation service. `SectionRules` follows it exactly. Both plug-ins and both front ends express the same two rules, so they are written once here and the plug-ins call them.

**Files:**
- Create: `plugins/OutcomeTesting.Plugins/SectionRules.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/SectionRulesTests.cs`

**Interfaces:**
- Consumes: `ResponseRules.IsVersionEffective`, `ResponseRules.OwnerRoleFor`, `ResponseRules.OwnerRoleTaxTeam`, `ResponseRules.OwnerRoleAqsChecker`
- Produces: `SectionRules.OwnerRoleBoth` (const int), `SectionRules.IsSectionEffective(DateTime?, DateTime?, DateTime) -> bool`, `SectionRules.OwnerRoleServes(int sectionOwnerRole, int reviewType) -> bool`

- [ ] **Step 1: Write the failing tests**

Create `plugins/OutcomeTesting.Plugins.Tests/SectionRulesTests.cs`:

```csharp
using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Section-level rules (AD-123). A section can now be dated out and can be owned by
    /// both disciplines; these are the two rules every reader applies, held in one place
    /// so the plug-ins and the two front ends cannot drift apart.
    /// </summary>
    public class SectionRulesTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        [Fact]
        public void A_section_with_no_dates_is_always_in_force()
        {
            Assert.True(SectionRules.IsSectionEffective(null, null, Today));
        }

        [Fact]
        public void A_section_dated_from_tomorrow_is_not_yet_in_force()
        {
            Assert.False(SectionRules.IsSectionEffective(Today.AddDays(1), null, Today));
        }

        [Fact]
        public void A_section_dated_out_today_is_no_longer_in_force()
        {
            Assert.False(SectionRules.IsSectionEffective(null, Today, Today));
        }

        [Fact]
        public void A_section_dated_out_tomorrow_is_still_in_force_today()
        {
            Assert.True(SectionRules.IsSectionEffective(null, Today.AddDays(1), Today));
        }

        [Fact]
        public void A_tax_section_serves_a_tax_review_only()
        {
            Assert.True(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleTaxTeam, ResponseRules.ReviewTypeTax));
            Assert.False(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleTaxTeam, ResponseRules.ReviewTypeAqs));
        }

        [Fact]
        public void An_aqs_section_serves_an_aqs_review_only()
        {
            Assert.True(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleAqsChecker, ResponseRules.ReviewTypeAqs));
            Assert.False(SectionRules.OwnerRoleServes(
                ResponseRules.OwnerRoleAqsChecker, ResponseRules.ReviewTypeTax));
        }

        [Fact]
        public void A_both_section_serves_either_discipline()
        {
            Assert.True(SectionRules.OwnerRoleServes(
                SectionRules.OwnerRoleBoth, ResponseRules.ReviewTypeTax));
            Assert.True(SectionRules.OwnerRoleServes(
                SectionRules.OwnerRoleBoth, ResponseRules.ReviewTypeAqs));
        }

        [Fact]
        public void A_section_owned_by_a_non_review_role_serves_neither_discipline()
        {
            // Adviser, T&C Manager and Manager / Admin are valid owner roles that no
            // review instance is ever opened as. Such a section must not be demanded.
            Assert.False(SectionRules.OwnerRoleServes(120910102, ResponseRules.ReviewTypeTax));
            Assert.False(SectionRules.OwnerRoleServes(120910102, ResponseRules.ReviewTypeAqs));
        }

        [Fact]
        public void An_unrecognised_review_type_is_served_by_nothing()
        {
            Assert.False(SectionRules.OwnerRoleServes(SectionRules.OwnerRoleBoth, 999));
            Assert.False(SectionRules.OwnerRoleServes(ResponseRules.OwnerRoleTaxTeam, 999));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter SectionRulesTests
```

Expected: FAIL to compile — `The name 'SectionRules' does not exist in the current context`.

- [ ] **Step 3: Write the implementation**

Create `plugins/OutcomeTesting.Plugins/SectionRules.cs`:

```csharp
using System;

namespace OutcomeTesting.Plugins
{
    /// <summary>
    /// Section-level rules (AD-123), deliberately free of Dataverse types so they can be
    /// tested without a fake organisation service — the same choice ResponseRules makes.
    ///
    /// Two rules, and both were previously absent rather than wrong: al_section carried no
    /// effective dates at all, and owner role was matched by strict equality in five
    /// places, which is why a section could be neither retired nor shared between the two
    /// disciplines.
    /// </summary>
    public static class SectionRules
    {
        /// <summary>
        /// A section owed by the Tax review and the AQS review alike. Each answers its own
        /// copy, because responses hang off the review instance and not off the section.
        /// AD-020 already described the File Quality fail points this way.
        /// </summary>
        public const int OwnerRoleBoth = 120910105;

        /// <summary>
        /// Whether a section is in force on a given day. Identical in meaning to a question
        /// version's window and delegated to it outright, so the two can never diverge:
        /// in force from the start of effective-from, out of force from the start of
        /// effective-to.
        /// </summary>
        public static bool IsSectionEffective(DateTime? effectiveFrom, DateTime? effectiveTo, DateTime asOf)
        {
            return ResponseRules.IsVersionEffective(effectiveFrom, effectiveTo, asOf);
        }

        /// <summary>
        /// Whether a section belongs to the discipline running this review. True for the
        /// discipline's own owner role and for Both; false for everything else, including
        /// the owner roles no review is ever opened as (Adviser, T&amp;C Manager,
        /// Manager / Admin) and any review type that is not Tax or AQS.
        /// </summary>
        public static bool OwnerRoleServes(int sectionOwnerRole, int reviewType)
        {
            int disciplineRole;
            if (!OutcomeRules.TryOwnerRoleForReviewType(reviewType, out disciplineRole))
            {
                return false;
            }

            return sectionOwnerRole == disciplineRole || sectionOwnerRole == OwnerRoleBoth;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter SectionRulesTests
```

Expected: PASS, 9 tests.

- [ ] **Step 5: Run the whole suite, to prove nothing else moved**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
```

Expected: PASS, 691 tests. The suite stood at 682 before this plan. (The "301 tests" figure in the decision log is stale — it dates from 2026-09-05.)

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/SectionRules.cs plugins/OutcomeTesting.Plugins.Tests/SectionRulesTests.cs
git commit -m "feat(checklist): add SectionRules, the effective-date and Both owner-role rules

Both rules were absent rather than wrong: al_section carried no dates, and
owner role was matched by equality in five places. Held in one Dataverse-free
class so the readers taught next cannot drift apart. No reader calls it yet.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: `adddatecolumn` and `addboolcolumn` verbs

The registration CLI can add memo and choice columns but not date or boolean ones, and this plan needs two dates and a flag. Both verbs are modelled on `AddMemoColumn`, including its read-back — on this project a successful-looking write is not evidence.

**Files:**
- Modify: `plugins/OutcomeTesting.Registration/Program.cs` — dispatch beside `addmemocolumn` (around line 237), bodies beside `AddMemoColumn` (around line 2686)

**Interfaces:**
- Consumes: `Connect`, `SolutionUniqueName`, `ConfirmedFor`, `NotificationTable.Text` — all existing in `Program.cs`
- Produces: two CLI verbs
  - `adddatecolumn <orgUrl> <entityLogicalName> <SchemaName> <displayName> [<description>] --confirm <orgUrl>`
  - `addboolcolumn <orgUrl> <entityLogicalName> <SchemaName> <displayName> <defaultValue true|false> [<description>] --confirm <orgUrl>`

- [ ] **Step 1: Add the dispatch**

In `Program.cs`, immediately after the `addchoicecolumn` block (around line 245):

```csharp
if (args.Length >= 2 && args[0].Equals("adddatecolumn", StringComparison.OrdinalIgnoreCase))
{
    return AddDateColumn(args);
}

if (args.Length >= 2 && args[0].Equals("addboolcolumn", StringComparison.OrdinalIgnoreCase))
{
    return AddBoolColumn(args);
}
```

- [ ] **Step 2: Add the two bodies**

In `Program.cs`, immediately after the `AddMemoColumn` method:

```csharp
// A date-only column. DateTimeBehavior is DateOnly because every effective date in this
// model is compared a day at a time (AD-091); a UserLocal behaviour would put the answer
// at the mercy of the reader's time zone.
int AddDateColumn(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 5 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This writes metadata to a live environment. Re-run as: adddatecolumn <orgUrl> " +
            "<entityLogicalName> <SchemaName> <displayName> [<description>] --confirm <orgUrl>");
        return 1;
    }

    var entity = a[2].Trim();
    var schemaName = a[3].Trim();
    var displayName = a[4];
    var description = a.Length > 5 && !a[5].StartsWith("--", StringComparison.Ordinal) ? a[5] : string.Empty;
    var logicalName = schemaName.ToLowerInvariant();

    using var svc = Connect(orgUrl);

    var existing = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    if (existing.EntityMetadata.Attributes.Any(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"'{entity}' already has a column '{logicalName}'. Nothing was changed.");
        return 1;
    }

    svc.Execute(new CreateAttributeRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        EntityName = entity,
        Attribute = new DateTimeAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = logicalName,
            Format = DateTimeFormat.DateOnly,
            DateTimeBehavior = DateTimeBehavior.DateOnly,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            DisplayName = NotificationTable.Text(displayName),
            Description = NotificationTable.Text(description),
        },
    });

    // Read back, because on this project a successful-looking write is not evidence.
    var after = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    var created = after.EntityMetadata.Attributes.FirstOrDefault(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));

    if (created == null)
    {
        Console.Error.WriteLine($"'{logicalName}' was not found on '{entity}' after the create.");
        return 1;
    }

    Console.WriteLine($"Created {entity}.{logicalName} as a DateOnly column.");
    return 0;
}

// A two-option boolean column. The default matters: every existing row takes it, so a
// flag that must not change behaviour for content already published defaults to false.
int AddBoolColumn(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 6 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This writes metadata to a live environment. Re-run as: addboolcolumn <orgUrl> " +
            "<entityLogicalName> <SchemaName> <displayName> <true|false> [<description>] --confirm <orgUrl>");
        return 1;
    }

    var entity = a[2].Trim();
    var schemaName = a[3].Trim();
    var displayName = a[4];

    bool defaultValue;
    if (!bool.TryParse(a[5], out defaultValue))
    {
        Console.Error.WriteLine("Default value must be 'true' or 'false'.");
        return 1;
    }

    var description = a.Length > 6 && !a[6].StartsWith("--", StringComparison.Ordinal) ? a[6] : string.Empty;
    var logicalName = schemaName.ToLowerInvariant();

    using var svc = Connect(orgUrl);

    var existing = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    if (existing.EntityMetadata.Attributes.Any(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)))
    {
        Console.Error.WriteLine($"'{entity}' already has a column '{logicalName}'. Nothing was changed.");
        return 1;
    }

    svc.Execute(new CreateAttributeRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        EntityName = entity,
        Attribute = new BooleanAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = logicalName,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            DisplayName = NotificationTable.Text(displayName),
            Description = NotificationTable.Text(description),
            DefaultValue = defaultValue,
            OptionSet = new BooleanOptionSetMetadata(
                new OptionMetadata(NotificationTable.Text("Yes"), 1),
                new OptionMetadata(NotificationTable.Text("No"), 0)),
        },
    });

    var after = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    var created = after.EntityMetadata.Attributes.FirstOrDefault(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));

    if (created == null)
    {
        Console.Error.WriteLine($"'{logicalName}' was not found on '{entity}' after the create.");
        return 1;
    }

    Console.WriteLine($"Created {entity}.{logicalName} as a boolean column defaulting to {defaultValue}.");
    return 0;
}
```

- [ ] **Step 3: Build the tool**

```bash
cd plugins/OutcomeTesting.Registration && DOTNET_ROLL_FORWARD=Major dotnet build
```

Expected: build succeeds. If `DateTimeBehavior` or `BooleanOptionSetMetadata` is unresolved, add `using Microsoft.Xrm.Sdk.Metadata;` — `AddMemoColumn` already relies on that namespace, so it is almost certainly present.

- [ ] **Step 4: Verify the usage text, without touching an environment**

```bash
cd plugins/OutcomeTesting.Registration && DOTNET_ROLL_FORWARD=Major dotnet run -- adddatecolumn https://example.crm11.dynamics.com
```

Expected: exit 1 and the usage line. This proves dispatch and the confirm guard without writing metadata.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Registration/Program.cs
git commit -m "feat(tooling): adddatecolumn and addboolcolumn verbs

The CLI could add memo and choice columns only. Both new verbs follow
AddMemoColumn, read-back included. Dates are DateOnly behaviour, because every
effective date in this model is compared a day at a time.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Create the columns and the Both value in DEV

Metadata only. No reader consults any of it yet, so this task cannot change behaviour — which is exactly why it is its own task and why the whole suite is re-run at the end of it.

**Files:**
- Modify: `src/Entities/al_Section/` — replaced from the DEV export, never hand-edited (AD-013)

**Interfaces:**
- Consumes: `adddatecolumn` and `addboolcolumn` from Task 2; existing `addoptionvalue <orgUrl> <entity> <attribute> <value> <label> [<description>]` and `addmetadatatosolution <orgUrl> <entity> [<attribute>]`
- Produces: `al_section.al_effectivefrom`, `al_section.al_effectiveto`, `al_section.al_isoptional`, and `al_ownerrole` value `120910105`

**This is the first task that writes to a live environment.** Everything before it was inert.

**Environment:** `Env_AQ_Dev`, `https://org0b075da8.crm11.dynamics.com/`. `pac` is already authenticated as `svc.automate.aq@ascotlloyd.co.uk` — confirm with `pac auth list` before starting, and stop if the active row is any other environment.

Every argument below is taken from the verb's own dispatch in `Program.cs`. Note that `addoptionvalue` and `addmetadatatosolution` take **no** `--confirm` flag: each would swallow it as the next positional argument, setting the option's description to the literal `"--confirm"` or asking for an attribute of that name.

- [ ] **Step 1: Create the three columns**

```bash
cd plugins/OutcomeTesting.Registration
ORG=https://org0b075da8.crm11.dynamics.com/
DOTNET_ROLL_FORWARD=Major dotnet run -- adddatecolumn $ORG al_section al_EffectiveFrom "Effective from" "The day this section starts being asked. Empty means it always has been." --confirm $ORG
DOTNET_ROLL_FORWARD=Major dotnet run -- adddatecolumn $ORG al_section al_EffectiveTo "Effective to" "The day this section stops being asked. Empty means it still is." --confirm $ORG
DOTNET_ROLL_FORWARD=Major dotnet run -- addboolcolumn $ORG al_section al_IsOptional "Optional" false "When Yes, this section's questions are not owed at submit (AD-123)." --confirm $ORG
```

Expected: three `Created al_section.<name>` lines, the first two reporting `behaviour DateOnly`. A behaviour of `UserLocal` is a failure even though the column exists — delete it and investigate rather than continuing. Each verb refuses a second run, which makes this safe to repeat.

- [ ] **Step 2: Mint the Both owner role**

```bash
DOTNET_ROLL_FORWARD=Major dotnet run -- addoptionvalue $ORG al_section al_ownerrole 120910105 "Both" "A section owed by the Tax review and the AQS review alike (AD-123)."
```

Exit codes are meaningful here: `0` added or already present with this label, `1` the label is already in use by another value, `2` the value exists carrying a *different* label. Only `0` may continue.

- [ ] **Step 3: Read the metadata back, rather than trusting four successful-looking writes**

```bash
DOTNET_ROLL_FORWARD=Major dotnet run -- fetch $ORG "<fetch><entity name='al_section'><attribute name='al_sectioncode' /><attribute name='al_ownerrole' /><attribute name='al_effectivefrom' /><attribute name='al_effectiveto' /><attribute name='al_isoptional' /></entity></fetch>"
```

Expected: 12 sections, with empty dates. The query succeeding at all is the proof — it errors on an unknown attribute, so a column that was not created stops this step rather than passing quietly.

`al_isoptional` comes back **null**, not false: Dataverse applies a boolean default to new rows only and does not backfill. That is safe, because every reader treats absent as required, but it means `al_isoptional eq false` matches nothing. Do not "fix" it with a backfill — the null path is the one the tests pin.

- [ ] **Step 4: Carry the option value into the solution**

The three columns were created with `SolutionUniqueName` and are already in the solution. `al_ownerrole` is an existing column, so its new value is not:

```bash
DOTNET_ROLL_FORWARD=Major dotnet run -- addmetadatatosolution $ORG al_section al_ownerrole
```

- [ ] **Step 5: Export from DEV and copy back, the AD-013 round trip**

The procedure is `docs/deployment/2026-09-06-ad013-round-trip.md`: `src/` is not hand-edited, it is replaced by what Dataverse emits. Read-only against DEV.

```bash
SCRATCH=$(mktemp -d)
pac solution export --name OutcomeTesting --path $SCRATCH --managed false --overwrite
pac solution unpack --zipfile $SCRATCH/OutcomeTesting.zip --folder $SCRATCH/unpacked --packagetype Unmanaged
```

Then copy back **only** `Entities/al_Section/`:

```bash
cp -r $SCRATCH/unpacked/Entities/al_Section/. src/Entities/al_Section/
git diff --stat src/Entities/al_Section/
```

A full AD-013 round trip copies back `Entities`, `customapis`, `PluginAssemblies`, `Roles`, `SdkMessageProcessingSteps` and `Other/` — never `CanvasApps` (AD-012). That is deliberately **not** done here: a whole-solution reconciliation touches hundreds of files through exporter reordering alone, and burying a three-column schema change inside it makes both unreviewable. If the export reveals drift elsewhere, record it and raise it as its own round trip with its own deployment note, exactly as 2026-09-06 was.

- [ ] **Step 6: Verify `src/` still packs**

```bash
pac solution pack --folder src --zipfile $SCRATCH/repack.zip --packagetype Unmanaged
grep -c "al_effectivefrom\|al_effectiveto\|al_isoptional\|120910105" src/Entities/al_Section/Entity.xml
```

Expected: the pack succeeds warning only about `CanvasApps`, which AD-012 excludes on purpose — that warning is the expected output, not a problem. The grep count is at least 4. A pack failure means the copied-back XML is inconsistent with the rest of `src/`; do not commit it.

- [ ] **Step 7: Prove nothing changed behaviourally**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
cd ../app && npx tsc -b && npm test
```

Expected: both PASS, the plug-in suite at 691. Note `tsc -b`, not `tsc --noEmit` — the latter checks nothing in this project.

- [ ] **Step 8: Commit**

```bash
git add src/Entities/al_Section/Entity.xml
git commit -m "feat(schema): al_section gains effective dates, an optional flag and a Both owner role

Metadata only; nothing reads any of it yet. Dates are nullable so the nine
seeded sections need no backfill - a null effective-from already means in force.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 4: `SubmitReviewPlugin` honours section dates, optional and Both

The mandatory gate currently demands every mandatory question in a section matching the discipline's owner role exactly, with no regard to whether the section is still in force or is optional. This is the task that decides what a reviewer is allowed to submit.

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs:315-345` (the mandatory query and its in-memory effectiveness filter)
- Test: `plugins/OutcomeTesting.Plugins.Tests/SubmitReviewSectionRulesTests.cs` (create)

**Interfaces:**
- Consumes: `SectionRules.IsSectionEffective`, `SectionRules.OwnerRoleServes`, `SectionRules.OwnerRoleBoth`
- Produces: no new public surface; behaviour change only

- [ ] **Step 1: Read the existing code before changing it**

```bash
sed -n '294,350p' plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs
```

Note two things: the section link filters `al_ownerrole` with `ConditionOperator.Equal`, and the effectiveness of each question version is applied **in memory** after the query, not in it. The section rules follow the same shape — fetched as columns, judged in memory — because a date-only column compared inside a query is at the mercy of how the platform coerces it, which is the reason AD-091 gives for doing it this way.

- [ ] **Step 2: Write the failing tests**

This suite tests extracted statics directly with plain `Entity` objects — `SubmitReviewPlugin.HasAnswer` and `RemediationResponseGuardPlugin.Refusal` are the precedents — rather than arranging a whole plug-in execution. Follow that: the new rule goes in a static named `IsOwed`, and the test builds the aliased row the query returns.

Create `plugins/OutcomeTesting.Plugins.Tests/SubmitReviewSectionRulesTests.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// AD-123: a question is owed only when its section is in force and is not optional.
    /// The section columns arrive through a link-entity alias, so they are boxed in
    /// AliasedValue and a null column arrives as no entry at all — which is the shape
    /// this fixture reproduces.
    /// </summary>
    public class SubmitReviewSectionRulesTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        /// <summary>One row as the mandatory query returns it: the version, plus its
        /// section's columns under the "s" alias.</summary>
        private static Entity Row(
            bool optional = false,
            DateTime? sectionFrom = null,
            DateTime? sectionTo = null,
            DateTime? versionFrom = null,
            DateTime? versionTo = null)
        {
            var row = new Entity("al_questionversion", Guid.NewGuid());
            row["s.al_isoptional"] = new AliasedValue("al_section", "al_isoptional", optional);

            if (sectionFrom.HasValue)
            {
                row["s.al_effectivefrom"] =
                    new AliasedValue("al_section", "al_effectivefrom", sectionFrom.Value);
            }

            if (sectionTo.HasValue)
            {
                row["s.al_effectiveto"] =
                    new AliasedValue("al_section", "al_effectiveto", sectionTo.Value);
            }

            if (versionFrom.HasValue) row["al_effectivefrom"] = versionFrom.Value;
            if (versionTo.HasValue) row["al_effectiveto"] = versionTo.Value;

            return row;
        }

        [Fact]
        public void A_question_in_a_live_required_section_is_owed()
        {
            // The regression guard. Without it every test below passes by accident if the
            // gate simply stops demanding anything at all.
            Assert.True(SubmitReviewPlugin.IsOwed(Row(), Today));
        }

        [Fact]
        public void A_question_in_an_optional_section_is_not_owed()
        {
            Assert.False(SubmitReviewPlugin.IsOwed(Row(optional: true), Today));
        }

        [Fact]
        public void A_question_in_a_retired_section_is_not_owed()
        {
            Assert.False(SubmitReviewPlugin.IsOwed(Row(sectionTo: Today), Today));
        }

        [Fact]
        public void A_question_in_a_section_retired_tomorrow_is_still_owed_today()
        {
            Assert.True(SubmitReviewPlugin.IsOwed(Row(sectionTo: Today.AddDays(1)), Today));
        }

        [Fact]
        public void A_question_in_a_section_not_yet_in_force_is_not_owed()
        {
            Assert.False(SubmitReviewPlugin.IsOwed(Row(sectionFrom: Today.AddDays(1)), Today));
        }

        [Fact]
        public void A_retired_question_version_in_a_live_section_is_not_owed()
        {
            // The version window still applies; the section rule is an additional gate,
            // not a replacement for it.
            Assert.False(SubmitReviewPlugin.IsOwed(Row(versionTo: Today), Today));
        }

        [Fact]
        public void A_missing_optional_flag_is_read_as_required()
        {
            // A section row written before al_isoptional existed returns no aliased value.
            // Absent must mean required, or every such section silently stops being owed.
            var row = new Entity("al_questionversion", Guid.NewGuid());
            Assert.True(SubmitReviewPlugin.IsOwed(row, Today));
        }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter SubmitReviewSectionRulesTests
```

Expected: FAIL to compile — `'SubmitReviewPlugin' does not contain a definition for 'IsOwed'`.

- [ ] **Step 4: Write `IsOwed` and its helper**

In `SubmitReviewPlugin.cs`, beside the other public statics such as `HasAnswer`:

```csharp
/// <summary>
/// Whether one row of the mandatory query is actually owed an answer (AD-123). Three
/// gates, in the order they are cheapest to fail: the section is not optional, the
/// section is in force, and the question version is in force.
///
/// Public and static so it can be tested with a plain Entity. The assembly is signed and
/// deliberately carries no InternalsVisibleTo (see PluginBase), so public is what makes a
/// helper reachable from the test project - HasAnswer is public for the same reason.
/// </summary>
public static bool IsOwed(Entity versionWithSection, DateTime asOf)
{
    var optional = versionWithSection.GetAttributeValue<AliasedValue>("s.al_isoptional");
    if (optional != null && optional.Value is bool isOptional && isOptional)
    {
        return false;
    }

    if (!SectionRules.IsSectionEffective(
        AliasedDate(versionWithSection, "s.al_effectivefrom"),
        AliasedDate(versionWithSection, "s.al_effectiveto"),
        asOf))
    {
        return false;
    }

    return ResponseRules.IsVersionEffective(
        versionWithSection.GetAttributeValue<DateTime?>("al_effectivefrom"),
        versionWithSection.GetAttributeValue<DateTime?>("al_effectiveto"),
        asOf);
}

/// <summary>
/// A date read through a link-entity alias. Aliased values arrive boxed, and a column
/// holding null arrives as no aliased value at all rather than as an aliased null.
/// </summary>
private static DateTime? AliasedDate(Entity row, string alias)
{
    var aliased = row.GetAttributeValue<AliasedValue>(alias);
    return aliased != null && aliased.Value is DateTime value ? value : (DateTime?)null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter SubmitReviewSectionRulesTests
```

Expected: PASS, 7 tests.

- [ ] **Step 6: Change the query**

In `SubmitReviewPlugin.cs`, in the mandatory query, replace the owner-role equality condition with membership and fetch the three new section columns:

```csharp
var sectionLink = questionLink.AddLink("al_section", "al_sectionid", "al_sectionid");
sectionLink.EntityAlias = "s";
sectionLink.Columns = new ColumnSet(
    "al_effectivefrom", "al_effectiveto", "al_isoptional", "al_ownerrole");
sectionLink.LinkCriteria.AddCondition(
    "al_checklistversionid", ConditionOperator.Equal, checklistVersion.Id);

// Owner role is membership, not equality: a Both section (AD-123) is owed by the Tax
// review and the AQS review alike, each answering its own copy. Equality here is what
// made a shared section impossible to express.
sectionLink.LinkCriteria.AddCondition(
    "al_ownerrole", ConditionOperator.In, ownerRole, SectionRules.OwnerRoleBoth);
```

- [ ] **Step 7: Replace the in-memory filter with the tested static**

The loop that already judges each question version now calls `IsOwed`, which judges the section and the version together:

```csharp
var now = DateTime.UtcNow;
var required = new List<Entity>();
foreach (var version in service.RetrieveMultiple(mandatoryQuery).Entities)
{
    // Judged here rather than in the query for the reason AD-091 gives: a date-only
    // column compared inside a query is at the mercy of how the platform coerces it.
    if (IsOwed(version, now))
    {
        required.Add(version);
    }
}
```

Delete the inline `ResponseRules.IsVersionEffective` call this replaces — `IsOwed` now makes it, and leaving both would apply the version window twice.

- [ ] **Step 8: Run the whole suite**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
```

Expected: PASS. Any failure in the existing `SubmitReviewAnswerTests` or `SubmitReviewCallerTests` is a real regression — the gate is load-bearing for every route.

- [ ] **Step 9: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/SubmitReviewPlugin.cs plugins/OutcomeTesting.Plugins.Tests/SubmitReviewSectionRulesTests.cs
git commit -m "feat(submit): the mandatory gate honours section dates, optional and Both

A question is owed only when its section is in force, is not optional, and
serves this review's discipline. Owner role becomes membership rather than
equality, which is what lets one section be owed by both teams.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 5: `ResponseGuardPlugin` accepts a Both section and refuses a retired one

This is the plug-in the spec singles out: change only the renderers and a Both section appears correctly and then refuses every answer typed into it.

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs:172-193`
- Test: `plugins/OutcomeTesting.Plugins.Tests/ResponseGuardSectionRulesTests.cs` (create)

**Interfaces:**
- Consumes: `SectionRules.OwnerRoleServes`, `SectionRules.IsSectionEffective`
- Produces: no new public surface

- [ ] **Step 1: Write the failing tests**

`RemediationResponseGuardPlugin.Refusal` is the pattern to copy: a static returning `null` when the write is allowed and the refusal message when it is not, tested with plain `Entity` objects.

Create `plugins/OutcomeTesting.Plugins.Tests/ResponseGuardSectionRulesTests.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using OutcomeTesting.Plugins;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Answering against the AD-123 section rules. The discipline check becomes
    /// membership, so a Both section is answerable by either team; and a retired section
    /// takes no new answer while the answers it already holds keep resolving (AD-091).
    /// </summary>
    public class ResponseGuardSectionRulesTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 11);

        private static Entity Section(int ownerRole, DateTime? from = null, DateTime? to = null)
        {
            var section = new Entity("al_section", Guid.NewGuid())
            {
                ["al_ownerrole"] = new OptionSetValue(ownerRole),
            };
            if (from.HasValue) section["al_effectivefrom"] = from.Value;
            if (to.HasValue) section["al_effectiveto"] = to.Value;
            return section;
        }

        [Fact]
        public void A_tax_review_may_answer_a_tax_section()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam), ResponseRules.ReviewTypeTax, Today));
        }

        [Fact]
        public void A_tax_review_may_answer_a_both_section()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(SectionRules.OwnerRoleBoth), ResponseRules.ReviewTypeTax, Today));
        }

        [Fact]
        public void An_aqs_review_may_answer_a_both_section()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(SectionRules.OwnerRoleBoth), ResponseRules.ReviewTypeAqs, Today));
        }

        [Fact]
        public void A_tax_review_may_not_answer_an_aqs_section()
        {
            var refusal = ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleAqsChecker), ResponseRules.ReviewTypeTax, Today);

            Assert.NotNull(refusal);
            Assert.Contains("another discipline", refusal);
        }

        [Fact]
        public void A_retired_section_refuses_a_new_answer()
        {
            var refusal = ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam, to: Today),
                ResponseRules.ReviewTypeTax,
                Today);

            Assert.NotNull(refusal);
            Assert.Contains("no longer part of the checklist", refusal);
        }

        [Fact]
        public void A_section_not_yet_in_force_refuses_an_answer()
        {
            Assert.NotNull(ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam, from: Today.AddDays(1)),
                ResponseRules.ReviewTypeTax,
                Today));
        }

        [Fact]
        public void A_section_retired_tomorrow_still_accepts_an_answer_today()
        {
            Assert.Null(ResponseGuardPlugin.SectionRefusal(
                Section(ResponseRules.OwnerRoleTaxTeam, to: Today.AddDays(1)),
                ResponseRules.ReviewTypeTax,
                Today));
        }

        [Fact]
        public void A_section_with_no_owner_role_is_refused_rather_than_assumed()
        {
            var section = new Entity("al_section", Guid.NewGuid());

            Assert.NotNull(ResponseGuardPlugin.SectionRefusal(
                section, ResponseRules.ReviewTypeTax, Today));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter ResponseGuardSectionRulesTests
```

Expected: FAIL to compile — `'ResponseGuardPlugin' does not contain a definition for 'SectionRefusal'`.

- [ ] **Step 3: Extract the rule as a static**

In `ResponseGuardPlugin.cs`, add beside the other statics:

```csharp
/// <summary>
/// Why this section cannot be answered by this review, or null when it can (AD-123).
/// Public and static so it can be tested with a plain Entity, exactly as
/// RemediationResponseGuardPlugin.Refusal is. The assembly is signed and carries no
/// InternalsVisibleTo, so internal would not be reachable from the tests.
///
/// Owner role is membership rather than equality: a Both section is answered by the Tax
/// review and the AQS review alike, each writing its own responses.
/// </summary>
public static string SectionRefusal(Entity section, int reviewType, DateTime asOf)
{
    var ownerRole = section.GetAttributeValue<OptionSetValue>("al_ownerrole");
    if (ownerRole == null || !SectionRules.OwnerRoleServes(ownerRole.Value, reviewType))
    {
        return "This question belongs to another discipline's section.";
    }

    // A retired section takes no new answers, while the answers it already holds keep
    // resolving (AD-091).
    if (!SectionRules.IsSectionEffective(
        section.GetAttributeValue<DateTime?>("al_effectivefrom"),
        section.GetAttributeValue<DateTime?>("al_effectiveto"),
        asOf))
    {
        return "This section is no longer part of the checklist, so it cannot be answered.";
    }

    return null;
}
```

- [ ] **Step 4: Call it from the guard**

Replace the existing discipline check (around line 177) and widen the retrieve above it:

```csharp
var section = service.Retrieve(
    "al_section",
    sectionRef.Id,
    new ColumnSet("al_ownerrole", "al_checklistversionid", "al_effectivefrom", "al_effectiveto"));

var reviewType = review.GetAttributeValue<OptionSetValue>("al_reviewtype");
if (reviewType == null)
{
    throw new InvalidPluginExecutionException(
        PreconditionPrefix + "This review has no recognised discipline.");
}

var sectionRefusal = SectionRefusal(section, reviewType.Value, DateTime.UtcNow);
if (sectionRefusal != null)
{
    throw new InvalidPluginExecutionException(PreconditionPrefix + sectionRefusal);
}
```

Leave the checklist-version check that follows exactly as it is. Confirm `using System;` is present at the top of the file; add it if not.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests --filter ResponseGuardSectionRulesTests
```

Expected: PASS, 8 tests.

- [ ] **Step 6: Run the whole suite**

```bash
cd plugins && DOTNET_ROLL_FORWARD=Major dotnet test OutcomeTesting.Plugins.Tests
```

Expected: PASS.

- [ ] **Step 7: Build Release and push the assembly**

```bash
cd plugins/OutcomeTesting.Plugins && DOTNET_ROLL_FORWARD=Major dotnet build -c Release
ls -l bin/Release/net462/OutcomeTesting.Plugins.dll
cd ../OutcomeTesting.Registration && DOTNET_ROLL_FORWARD=Major dotnet run -- pushassembly https://org0b075da8.crm11.dynamics.com/
```

`build -c Release` first is not optional: `pushassembly` uploads `bin/Release` without building, so skipping it ships the previous build. Check the byte count in the push output against the `ls` above — they must match exactly.

`pushassembly` takes `<orgUrl> [<dllPath>]` and **no** `--confirm`; a `--confirm` lands in the path slot and fails with "Plug-in assembly not found". Omitted, the path defaults to the Release build above.

- [ ] **Step 8: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/ResponseGuardPlugin.cs plugins/OutcomeTesting.Plugins.Tests/ResponseGuardSectionRulesTests.cs
git commit -m "feat(answering): accept a Both section from either discipline, refuse a retired one

The discipline check becomes membership. Without this a Both section would
render correctly in both front ends and then refuse every answer typed into it.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 6: The portal renders Both and hides retired sections

Two FetchXML blocks in one web template, both filtering owner role by equality and neither aware of section dates.

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html` — the `questions` fetch (section link filter, around line 320) and the `sections` fetch (around line 355)

**Interfaces:**
- Consumes: `as_of` and `owner_role`, both already assigned in the template
- Produces: no new variables

- [ ] **Step 1: Read the surrounding code and its warnings**

```bash
sed -n '280,300p;348,362p' powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html
```

`as_of` is already computed — the review's submitted day where submitted, otherwise today. Reuse it; do not compute a second one. Heed the file's own warning about Liquid tag delimiters inside comments.

- [ ] **Step 2: Widen the section filter in the `questions` fetch**

Replace the section link's filter block (around line 320):

```xml
<filter type="and">
  <condition attribute="al_checklistversionid" operator="eq" value="{{ cv_id }}" />
  <condition attribute="al_ownerrole" operator="in">
    <value>{{ owner_role }}</value>
    <value>120910105</value>
  </condition>
  <filter type="or">
    <condition attribute="al_effectivefrom" operator="null" />
    <condition attribute="al_effectivefrom" operator="on-or-before" value="{{ as_of }}" />
  </filter>
  <filter type="or">
    <condition attribute="al_effectiveto" operator="null" />
    <condition attribute="al_effectiveto" operator="gt" value="{{ as_of }}" />
  </filter>
</filter>
```

The two date filters are copied deliberately from the question-version filter thirty lines above, so a section window and a version window read identically in the same file. `120910105` is written as a literal because FetchXML has no constants; it is `SectionRules.OwnerRoleBoth`.

- [ ] **Step 3: Widen the `sections` fetch the same way**

Replace its filter block (around line 355) with the same five conditions, keeping its existing `| xml_escape` on `cv_id`. Add `<attribute name="al_isoptional" />` to that entity so step 4 can read it.

- [ ] **Step 4: Mark an optional section in the rendered form**

Where the section heading is emitted, after `blk_title` is resolved, add the marker. Find the `h2` the block comment describes as "an h2, the intro line where there is one, then one table", and append:

```liquid
{% if sec.al_isoptional %}<span class="ot-optional">Optional — this section may be left blank</span>{% endif %}
```

Use the section variable already in scope at that point; do not introduce a new loop.

- [ ] **Step 5: Push the template and check it renders**

```bash
cd plugins/OutcomeTesting.Registration
DOTNET_ROLL_FORWARD=Major dotnet run -- pushwebtemplate https://org0b075da8.crm11.dynamics.com/ a1000000-0000-4000-8000-00000000001b "../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html"
```

`pushwebtemplate` takes `<orgUrl> <powerpagecomponentid> <path>` — a GUID, not a name, and no `--confirm`. OT Review Detail is `a1000000-0000-4000-8000-00000000001b`, as six prior deployment notes record. The verb prints the character count before and after; a clearing of the site cache is the owner's step, so a stale page after a push is not necessarily a failed push.

Then open a Tax review and an AQS review in the portal and confirm: every section that rendered before still renders, and nothing is duplicated. A blank page means a Liquid syntax error — check the template's own warning about comments.

- [ ] **Step 6: Prove the new behaviour by hand**

**The CLI has no generic row-update verb**, so a section's dates and owner role cannot be set from here. Do not invent one for a test. Three checks are available without writing data, and together they cover the risk:

1. **The FetchXML is valid.** Run the `sections` filter through `fetch`, substituting a real `cv_id` and `owner_role`. A nested `<condition operator="in">` inside a link-entity filter is the part most likely to be rejected; if it parses, it parses.
2. **It is a no-op today.** Run the old equality-only filter and the new one side by side and compare the row counts. They must be identical, because no section is Both and none carries dates. A difference here means the change altered what reviewers see before any data said it should.
3. **The date operators discriminate.** Section dates are all null, but *question version* dates are real, and the operators are the same. Query one question's versions at two `as_of` dates — `Q-TAX-02` has three versions — and confirm a different version comes back each time. On 2026-09-12 this returned v1 as of 2026-08-27 and v3 as of 2026-09-12.

Counts alone can mislead: the whole-table in-force count is 46 at three different dates, which looks like a filter doing nothing and is in fact arithmetic — the not-yet-started successors excluded at an early date exactly offset the then-live versions included. Compare identities, not totals.

**What this does not prove**, and what remains the owner's step: a section actually dated out disappearing from a live review, and a Both section rendering in both disciplines with answers saving in each. That needs a section row edited in the maker portal, plus a portal cache clear. Flag it rather than claiming it.

- [ ] **Step 7: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html
git commit -m "feat(portal): render Both sections, hide retired ones, mark optional ones

Both fetches filtered owner role by equality and neither knew about section
dates. The date filters are copied from the question-version filter in the same
file so the two windows read identically.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 7: The Code App renders Both and hides retired sections

The same two rules, expressed as an OData filter.

**Files:**
- Create: `app/src/features/reviews/sectionFilter.ts`
- Modify: `app/src/features/reviews/useReviewDetail.ts:232-244`
- Test: `app/src/features/reviews/sectionFilter.test.ts`

**Put the function in its own module, not in `useReviewDetail.ts`.** That hook imports the
generated Power Apps services, and importing it from a test fails outright with
`Cannot find module '@microsoft/power-apps/dist/data/multiSelectPicklistUtils'`. It is why no
sibling test imports the hook and why `versionEffective.ts` and `reviewAnswer.ts` exist as
separate files — follow that pattern rather than fighting it.

**Interfaces:**
- Consumes: `header.type`, `header.checklistVersionId`, `header.submittedOn` — all already on the header
- Produces: `buildSectionFilter(ownerRole: number, checklistVersionId: string | null, asOf: string): string`, `referenceDay(submittedOn: string | null | undefined): string` and `OWNER_ROLE_BOTH`, all from `sectionFilter.ts`

- [ ] **Step 1: Write the failing test**

Create `app/src/features/reviews/sectionFilter.test.ts`:

```typescript
import { describe, expect, it } from 'vitest';
import { buildSectionFilter, OWNER_ROLE_BOTH } from './sectionFilter';

describe('buildSectionFilter', () => {
  it('accepts the discipline’s own role or Both', () => {
    const filter = buildSectionFilter(120910100, null, '2026-09-11');
    expect(filter).toContain('al_ownerrole eq 120910100');
    expect(filter).toContain('al_ownerrole eq 120910105');
  });

  it('excludes a section dated out on or before the reference day', () => {
    const filter = buildSectionFilter(120910101, null, '2026-09-11');
    expect(filter).toContain('al_effectiveto eq null or al_effectiveto gt 2026-09-11');
  });

  it('excludes a section not yet in force', () => {
    const filter = buildSectionFilter(120910101, null, '2026-09-11');
    expect(filter).toContain('al_effectivefrom eq null or al_effectivefrom le 2026-09-11');
  });

  it('scopes to the checklist version when the review carries one', () => {
    const filter = buildSectionFilter(120910101, 'abc-123', '2026-09-11');
    expect(filter).toContain('_al_checklistversionid_value eq abc-123');
  });

  it('omits the version clause when the review carries none', () => {
    expect(buildSectionFilter(120910101, null, '2026-09-11')).not.toContain('checklistversionid');
  });
});
```

Check the test runner first — if this project uses Jest rather than Vitest, change the import to match its siblings in `app/src/features/reviews/`.

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd app && npm test -- sectionFilter
```

Expected: FAIL — `buildSectionFilter` is not exported.

- [ ] **Step 3: Extract and widen the filter**

Create `sectionFilter.ts` with the two functions and the constant, then import them into `useReviewDetail.ts`:

```typescript
/** The Both owner role (AD-123). Mirrors SectionRules.OwnerRoleBoth in the plug-in assembly. */
export const OWNER_ROLE_BOTH = 120910105;

/**
 * The sections this review must render: owned by this discipline or by Both, on the
 * checklist version issued to the review, and in force on the reference day. The
 * reference day is the review's submitted day once submitted, otherwise today — the same
 * as-of rule the question versions already use (AD-091), applied one level up, so a
 * submitted review keeps rendering the section it was answered against.
 */
export function buildSectionFilter(
  ownerRole: number,
  checklistVersionId: string | null,
  asOf: string,
): string {
  return [
    `(al_ownerrole eq ${ownerRole} or al_ownerrole eq ${OWNER_ROLE_BOTH})`,
    `(al_effectivefrom eq null or al_effectivefrom le ${asOf})`,
    `(al_effectiveto eq null or al_effectiveto gt ${asOf})`,
    checklistVersionId ? `_al_checklistversionid_value eq ${checklistVersionId}` : null,
  ]
    .filter(Boolean)
    .join(' and ');
}
```

Then replace the inline `sectionFilter` construction with a call to it:

```typescript
const ownerRole = OWNER_ROLE[header.type] ?? OWNER_ROLE[expectedType];
const asOf = (header.submittedOn ?? new Date().toISOString()).slice(0, 10);
const sectionFilter = buildSectionFilter(ownerRole, header.checklistVersionId ?? null, asOf);
```

If the header does not carry `submittedOn`, add it to `toHeader` from the review's `al_submittedon`; check the header type before assuming either way.

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd app && npm test -- sectionFilter
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Typecheck and run the whole app suite**

```bash
cd app && npx tsc -b && npm test
```

`tsc -b`, not `tsc --noEmit` — the latter checks nothing in this project. Expect both to pass. `checklistDocument.test.ts` and `checklistRender.test.tsx` both filter sections by owner role in their own fixtures; if either fails, it is asserting equality and needs the same widening.

- [ ] **Step 6: Commit**

```bash
git add app/src/features/reviews/useReviewDetail.ts app/src/features/reviews/sectionFilter.test.ts
git commit -m "feat(app): render Both sections and hide retired ones

The filter is extracted so it can be tested directly, and reads as-of the
review's submitted day, so a submitted review keeps rendering the sections it
was answered against.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 8: Record the decisions

The decision log is how this project explains itself. Two entries, written once the behaviour they describe exists.

**Files:**
- Modify: `knowledge/decision-log.md` — append to the AD table, after AD-121
- Modify: `knowledge/project-context.md` — the checklist configuration paragraph

- [ ] **Step 1: Append AD-122 and AD-123**

Follow the existing row format exactly: `| AD-nnn | decision | why | date |`. AD-122 records that checklist content is administered in the version in force rather than by reissuing the checklist, amending AD-016 and PP-09, and citing effective dating as the protection AD-016 was reaching for. AD-123 records section effective dates, the optional flag and the Both owner role, amending AD-019 (not every displayed question is mandatory any more — an optional section's are not) and AD-020 (owner role gains Both). State in AD-123 that owner role is matched by membership in five places and name them, because that list is the thing a future reader will need.

- [ ] **Step 2: Update the checklist configuration paragraph**

In `project-context.md`, the paragraph describing the Checklist → Version → Section → Question → Question Version hierarchy now understates the model. Add that sections carry effective dates and an optional flag, and that a section may be owned by Tax, AQS or both.

- [ ] **Step 3: Commit**

```bash
git add knowledge/decision-log.md knowledge/project-context.md
git commit -m "docs: record AD-122 and AD-123, the checklist administration decisions

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Done when

- A section dated out by hand disappears from both front ends and from the submit gate, and still renders in a review submitted while it was in force.
- A section marked optional by hand is rendered, marked as optional, and owed by nobody at submit.
- A section set to Both by hand renders in a Tax review and an AQS review, accepts answers in each, and is demanded of each.
- The plug-in suite passes, `npx tsc -b` is clean, and the app suite passes.
- Nothing writes any of these states yet. That is plan two: the six commands and the Question library UI.

## Deliberately not in this plan

Spec section 8 lists `useQuestionLibrary` among the readers that must learn the new rules. It is held back to plan two on purpose: the library is the administration surface, its in-force grouping is meaningless until something can retire a section, and rewriting it twice would waste the work. The five readers here are the ones a **reviewer** depends on, and they are the ones that must not be left half-taught.
