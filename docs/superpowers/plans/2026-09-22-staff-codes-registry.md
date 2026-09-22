# Staff Codes on the Registry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hold a staff code against each person in the `contact` registry and use it to fill the six Trail Light code columns, replacing the emails put in columns B and D on 2026-09-21.

**Architecture:** One role-agnostic text column on `contact`. `NotificationOutbox.MatchPerson` — already the single place that resolves a case's person to a contact, already email-first — returns the code alongside the address, so every caller inherits it. `GenerateExportPlugin` snapshots the codes onto the export record at generation time. The People page becomes the registry's admin home, moved under `/admin/people`.

**Tech Stack:** Dataverse solution XML, C# plug-ins (.NET Framework 4.6.2, xUnit + `FakeOrganizationService`), React + TypeScript Code App (Vite, Vitest).

**Spec:** `docs/superpowers/specs/2026-09-22-staff-codes-registry-design.md`

## Global Constraints

- **Column widths:** declare `MaxLength` explicitly; never rely on `Length`, which Dataverse re-emits and reads as bytes (AD-026). A column declared by `Length` alone is created at half its advertised size.
- **Trail Light is positional (AD-039):** 20 columns, fixed order, column 16 (index 15) an intentional blank separator. No task may add, remove or reorder a column.
- **Blank, never a fallback:** where a person has no staff code the export cell is empty. Never fall back to an email, a name, or the case's old hand-typed code.
- **Code App parameter declaration:** a Custom API request parameter the Code App's `dataSourcesInfo.ts` does not declare is **silently discarded and the command still reports success**. Every new parameter is declared by hand there in the same task that adds it.
- **Plugin tests:** run with `$env:DOTNET_ROLL_FORWARD='Major'` set.
- **App typecheck:** `tsc --noEmit` checks nothing in `app/`. Use `npm run build` (which runs `tsc -b`).
- **Naming:** logical name `al_staffcode`; the label shown to a person is **"Employee code"** (D9). Both refer to the same column.
- **Deploy target:** DEV (`Env_AQ_Dev`) only. Promotion to TEST is a separate decision each time.

---

### Task 1: The `al_staffcode` column on contact

**Files:**
- Modify: `src/Entities/Contact/Entity.xml`
- Modify: `plugins/OutcomeTesting.Plugins/ContactRegistry.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/ContactRegistryTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ContactRegistry.StaffCodeAttr` (`const string` = `"al_staffcode"`), used by Tasks 2, 6.

- [ ] **Step 1: Write the failing test**

Append to `plugins/OutcomeTesting.Plugins.Tests/ContactRegistryTests.cs` (create the file with this content if it does not exist):

```csharp
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    public class ContactRegistryStaffCodeTests
    {
        /// <summary>
        /// The registry's staff code column (2026-09-22). Named on ContactRegistry for the
        /// reason every other contact attribute is: the export, the command and the People
        /// page must not drift apart on the spelling.
        /// </summary>
        [Fact]
        public void The_staff_code_attribute_is_named_once()
        {
            Assert.Equal("al_staffcode", ContactRegistry.StaffCodeAttr);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter ContactRegistryStaffCodeTests
```

Expected: FAIL — `'ContactRegistry' does not contain a definition for 'StaffCodeAttr'`.

- [ ] **Step 3: Add the constant**

In `plugins/OutcomeTesting.Plugins/ContactRegistry.cs`, beside the other attribute constants:

```csharp
        /// <summary>
        /// The person's staff code (project owner, 2026-09-22), labelled "Employee code" on
        /// the People page.
        ///
        /// Role-agnostic on purpose: a contact does not know whether it is an adviser, a
        /// para-planner, a checker or a T&amp;C Manager, so one column serves all four. This
        /// is what replaces the per-case al_advisercode / al_paraplannercode that OD-050
        /// expected Tax/AQS to type and that nothing ever filled.
        ///
        /// Text, not a number. A code that is genuinely numeric is formatted as one on the
        /// way into the export by trailLight's `code` helper; one carrying letters or
        /// leading zeros survives intact.
        /// </summary>
        public const string StaffCodeAttr = "al_staffcode";
```

- [ ] **Step 4: Add the column to the solution**

In `src/Entities/Contact/Entity.xml`, inside `<attributes>`, alongside the existing `al_*` attributes. Note `MaxLength` is what sizes the physical column (AD-026); `Length` is output-only.

```xml
        <attribute PhysicalName="al_StaffCode">
          <Type>nvarchar</Type>
          <Name>al_staffcode</Name>
          <LogicalName>al_staffcode</LogicalName>
          <RequiredLevel>none</RequiredLevel>
          <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
          <ImeMode>auto</ImeMode>
          <ValidForUpdateApi>1</ValidForUpdateApi>
          <ValidForReadApi>1</ValidForReadApi>
          <ValidForCreateApi>1</ValidForCreateApi>
          <IsCustomField>1</IsCustomField>
          <IsAuditEnabled>1</IsAuditEnabled>
          <IsSecured>0</IsSecured>
          <IntroducedVersion>1.0.7.0</IntroducedVersion>
          <IsCustomizable>1</IsCustomizable>
          <IsRenameable>1</IsRenameable>
          <CanModifySearchSettings>1</CanModifySearchSettings>
          <CanModifyRequirementLevelSettings>1</CanModifyRequirementLevelSettings>
          <CanModifyAdditionalSettings>1</CanModifyAdditionalSettings>
          <SourceType>0</SourceType>
          <IsGlobalFilterEnabled>0</IsGlobalFilterEnabled>
          <IsSortableEnabled>0</IsSortableEnabled>
          <CanModifyGlobalFilterSettings>1</CanModifyGlobalFilterSettings>
          <CanModifyIsSortableSettings>1</CanModifyIsSortableSettings>
          <IsDataSourceSecret>0</IsDataSourceSecret>
          <AutoNumberFormat></AutoNumberFormat>
          <IsSearchable>1</IsSearchable>
          <IsFilterable>1</IsFilterable>
          <IsRetrievable>1</IsRetrievable>
          <IsLocalizable>0</IsLocalizable>
          <Format>text</Format>
          <MaxLength>50</MaxLength>
          <displaynames>
            <displayname description="Employee code" languagecode="1033" />
          </displaynames>
          <Descriptions>
            <Description description="The person's staff code, used to fill the Trail Light export's code columns (2026-09-22). Replaces the per-case adviser and paraplanner codes." languagecode="1033" />
          </Descriptions>
        </attribute>
```

- [ ] **Step 5: Run test to verify it passes**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter ContactRegistryStaffCodeTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Entities/Contact/Entity.xml plugins/OutcomeTesting.Plugins/ContactRegistry.cs plugins/OutcomeTesting.Plugins.Tests/ContactRegistryTests.cs
git commit -m "feat(registry): al_staffcode on contact, named once on ContactRegistry"
```

---

### Task 2: `MatchPerson` returns the staff code

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/NotificationOutbox.cs` (the `PersonMatch` class and `MatchPerson`)
- Test: `plugins/OutcomeTesting.Plugins.Tests/StaffCodeMatchTests.cs`

**Interfaces:**
- Consumes: `ContactRegistry.StaffCodeAttr` (Task 1).
- Produces: `NotificationOutbox.PersonMatch.StaffCode` (`string`, null unless `IsMatch`). Used by Tasks 3 and 4.

- [ ] **Step 1: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/StaffCodeMatchTests.cs`:

```csharp
using System;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// The staff code travels with the match (2026-09-22).
    ///
    /// MatchPerson is already the one place that decides which contact a case means, and it
    /// already refuses an ambiguous answer. Carrying the code back from the same resolution
    /// is what stops a second, weaker lookup growing beside it.
    /// </summary>
    public class StaffCodeMatchTests
    {
        private static void Contact(
            FakeOrganizationService service, string name, string email, string staffCode)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", name,
                "emailaddress1", email,
                "al_staffcode", staffCode,
                "statecode", 0);
        }

        [Fact]
        public void A_matched_person_carries_their_code()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", "4471");

            var match = NotificationOutbox.MatchPerson(
                service, "jane.adviser@example.com", null, "adviser");

            Assert.True(match.IsMatch);
            Assert.Equal("4471", match.StaffCode);
        }

        [Fact]
        public void A_matched_person_without_a_code_carries_none()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", null);

            var match = NotificationOutbox.MatchPerson(
                service, "jane.adviser@example.com", null, "adviser");

            Assert.True(match.IsMatch);
            Assert.True(string.IsNullOrEmpty(match.StaffCode));
        }

        [Fact]
        public void An_ambiguous_name_carries_no_code()
        {
            // Two contacts of one name resolve to nobody, so there is no code to report -
            // the whole reason this matching fails loudly rather than approximately.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com", "8820");
            Contact(service, "Sam Jones", "s.jones@example.com", "9910");

            var match = NotificationOutbox.MatchPerson(service, null, "Sam Jones", "para-planner");

            Assert.False(match.IsMatch);
            Assert.True(string.IsNullOrEmpty(match.StaffCode));
        }

        [Fact]
        public void An_address_beats_a_shared_name_and_still_brings_the_code()
        {
            // This is the case that earns the ParaplannerEmail import mapping its keep: two
            // people share the name, so a name lookup would refuse, but the address the
            // extract carried picks out one of them and their code comes with it.
            var service = new FakeOrganizationService();
            Contact(service, "Sam Jones", "sam.jones@example.com", "8820");
            Contact(service, "Sam Jones", "s.jones@example.com", "9910");

            var match = NotificationOutbox.MatchPerson(
                service, "s.jones@example.com", "Sam Jones", "para-planner");

            Assert.True(match.IsMatch);
            Assert.Equal("9910", match.StaffCode);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter StaffCodeMatchTests
```

Expected: FAIL — `'PersonMatch' does not contain a definition for 'StaffCode'`.

- [ ] **Step 3: Add `StaffCode` to `PersonMatch`**

In `plugins/OutcomeTesting.Plugins/NotificationOutbox.cs`, in the `PersonMatch` class, after the `Contact` property:

```csharp
            /// <summary>
            /// The matched contact's staff code, set only when <see cref="IsMatch"/>.
            ///
            /// Null on every failure kind by construction: an ambiguous name resolved to
            /// nobody, so there is no code to report. That is the point - a code guessed
            /// from the first of two Sam Joneses would attribute a fail to the wrong person
            /// on a file that leaves this system.
            /// </summary>
            public string StaffCode { get; set; }
```

- [ ] **Step 4: Read and return the column**

In `MatchPerson`, widen the `ColumnSet` and populate the new property. Two edits:

```csharp
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("emailaddress1", ContactRegistry.StaffCodeAttr),
                TopCount = 2,
                Criteria = new FilterExpression(),
            };
```

and the success return:

```csharp
            return new PersonMatch
            {
                Kind = PersonMatchKind.Matched,
                Email = found,
                Contact = matches[0].ToEntityReference(),
                StaffCode = matches[0].GetAttributeValue<string>(ContactRegistry.StaffCodeAttr),
            };
```

- [ ] **Step 5: Run test to verify it passes**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter StaffCodeMatchTests
```

Expected: PASS, 4 tests.

- [ ] **Step 6: Run the whole plugin suite for regressions**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests
```

Expected: PASS. The widened `ColumnSet` must not disturb `NotificationOutboxTests`.

- [ ] **Step 7: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/NotificationOutbox.cs plugins/OutcomeTesting.Plugins.Tests/StaffCodeMatchTests.cs
git commit -m "feat(registry): the staff code travels with a person match"
```

---

### Task 3: Adviser and paraplanner codes onto the export record

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/StaffCodeExportTests.cs`

**Interfaces:**
- Consumes: `NotificationOutbox.PersonMatch.StaffCode` (Task 2).
- Produces: `GenerateExportPlugin.AdviserCode(IOrganizationService, Entity)` and `GenerateExportPlugin.ParaplannerCode(IOrganizationService, Entity)`, both returning `string` (null when unresolved). Used by Task 4's neighbours only through the record build.

- [ ] **Step 1: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/StaffCodeExportTests.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Trail Light columns B and D carry CODES again (project owner, 2026-09-22), Trailight
    /// having said they could not accommodate the emails put there on 2026-09-21 (AD-183).
    ///
    /// The codes come from the registry, not from the case: al_advisercode and
    /// al_paraplannercode were never filled by anything, which is why the columns were empty
    /// and why emails went in. The case's own columns are no longer read.
    /// </summary>
    public class StaffCodeExportTests
    {
        private static Entity Case(string adviserEmail, string paraplannerEmail, string paraplanner)
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            if (adviserEmail != null) { row["al_adviseremail"] = adviserEmail; }
            if (paraplannerEmail != null) { row["al_paraplanneremail"] = paraplannerEmail; }
            if (paraplanner != null) { row["al_paraplanner"] = paraplanner; }
            return row;
        }

        private static void Contact(
            FakeOrganizationService service, string name, string email, string staffCode)
        {
            service.Seed("contact", Guid.NewGuid(),
                "fullname", name,
                "emailaddress1", email,
                "al_staffcode", staffCode,
                "statecode", 0);
        }

        [Fact]
        public void The_adviser_code_comes_from_the_registry()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", "4471");

            Assert.Equal(
                "4471",
                GenerateExportPlugin.AdviserCode(
                    service, Case("jane.adviser@example.com", null, null)));
        }

        [Fact]
        public void The_para_planner_code_comes_from_the_registry_by_address()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Sam Paraplanner", "sam.paraplanner@example.com", "8820");

            Assert.Equal(
                "8820",
                GenerateExportPlugin.ParaplannerCode(
                    service, Case(null, "sam.paraplanner@example.com", "Sam Paraplanner")));
        }

        [Fact]
        public void A_person_the_registry_does_not_hold_exports_no_code()
        {
            // Blank, never a fallback. AD-039 reads by position: column B is the adviser's
            // code on every row, or the file lies about the rows where it is something else.
            var service = new FakeOrganizationService();

            Assert.True(string.IsNullOrEmpty(
                GenerateExportPlugin.AdviserCode(
                    service, Case("nobody@example.com", null, null))));
        }

        [Fact]
        public void A_contact_with_no_code_exports_no_code()
        {
            var service = new FakeOrganizationService();
            Contact(service, "Jane Adviser", "jane.adviser@example.com", null);

            Assert.True(string.IsNullOrEmpty(
                GenerateExportPlugin.AdviserCode(
                    service, Case("jane.adviser@example.com", null, null))));
        }

        [Fact]
        public void The_case_own_code_column_is_not_read()
        {
            // D2: the registry is the only source. A case still carrying a hand-typed code
            // from before this change must not leak it into the file.
            var service = new FakeOrganizationService();
            var outcomeCase = Case("nobody@example.com", null, null);
            outcomeCase["al_advisercode"] = "STALE-99";

            Assert.True(string.IsNullOrEmpty(
                GenerateExportPlugin.AdviserCode(service, outcomeCase)));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter StaffCodeExportTests
```

Expected: FAIL — `'GenerateExportPlugin' does not contain a definition for 'AdviserCode'`.

- [ ] **Step 3: Add the two resolvers**

In `plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs`, beside the existing `ParaplannerEmail` helper:

```csharp
        /// <summary>
        /// The adviser's staff code for this case, or null where the registry does not hold
        /// one for them.
        ///
        /// Resolved from the registry rather than read off the case (project owner,
        /// 2026-09-22). al_advisercode was only ever filled by hand and almost never was -
        /// OD-050 expected Tax/AQS to type it - which is why column B was empty before the
        /// emails went in. A code belongs to a person, so it is held against the person.
        ///
        /// An unresolved adviser leaves the column EMPTY rather than falling back to the
        /// address, the name or the case's old value. AD-039 is positional: column B is the
        /// adviser's code on every row, or the file lies about the rows where it is not.
        /// </summary>
        public static string AdviserCode(IOrganizationService service, Entity outcomeCase)
        {
            if (outcomeCase == null) { return null; }

            var match = NotificationOutbox.MatchAdviser(
                service,
                outcomeCase.GetAttributeValue<string>("al_adviseremail"),
                outcomeCase.GetAttributeValue<string>("al_advisername"));

            return match != null && match.IsMatch ? match.StaffCode : null;
        }

        /// <summary>
        /// The para-planner's staff code for this case, or null where the registry does not
        /// hold one for them.
        ///
        /// Resolved by ADDRESS first (AD-186). This is what the ParaplannerEmail import
        /// mapping buys: two active contacts of one name resolve to nobody, so a name-only
        /// lookup would blank the code on exactly the rows where it is ambiguous - the
        /// problem this change exists to fix.
        /// </summary>
        public static string ParaplannerCode(IOrganizationService service, Entity outcomeCase)
        {
            if (outcomeCase == null) { return null; }

            var match = NotificationOutbox.MatchParaplanner(
                service,
                outcomeCase.GetAttributeValue<string>(ImportRules.ParaplannerEmailAttribute),
                outcomeCase.GetAttributeValue<string>("al_paraplanner"));

            return match != null && match.IsMatch ? match.StaffCode : null;
        }
```

- [ ] **Step 4: Point the record build at them**

In the `new Entity(RecordEntity)` initialiser, replace the two code assignments. Leave `al_adviseremail` and `al_paraplanneremail` in place — they remain useful snapshots even though the file no longer reads them.

```csharp
                    // Trail Light col B, a CODE again from 2026-09-22 (project owner:
                    // Trailight could not accommodate the emails put here on 2026-09-21).
                    // From the registry, not the case - see AdviserCode.
                    ["al_advisercode"] = AdviserCode(userService, outcomeCase),
```

```csharp
                    // Trail Light col D, a CODE again from 2026-09-22, resolved by address.
                    ["al_paraplannercode"] = ParaplannerCode(userService, outcomeCase),
```

- [ ] **Step 5: Run test to verify it passes**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter StaffCodeExportTests
```

Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs plugins/OutcomeTesting.Plugins.Tests/StaffCodeExportTests.cs
git commit -m "feat(export): adviser and paraplanner codes resolved from the registry"
```

---

### Task 4: The four fail-accountability code columns stop being blank

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs` (`NamedPerson`, `FlaggedText`, the record build)
- Test: `plugins/OutcomeTesting.Plugins.Tests/AccountabilityCodeTests.cs`

**Interfaces:**
- Consumes: `ContactRegistry.StaffCodeAttr` (Task 1).
- Produces: `GenerateExportPlugin.AccountablePerson` — a class with `string Name` and `string StaffCode`; `GenerateExportPlugin.NamedPerson(IOrganizationService, Entity, string)` returning it (null when no one is named); `FlaggedText(bool, Entity, string, AccountablePerson)`.

**Context for the implementer:** `FlaggedText` currently blanks any column whose attribute name ends in "code" whenever a specific person is named accountable, because "a contact carries no adviser or paraplanner code". Task 1 made that false. `NamedPerson` currently returns only `reference.Name` from a lookup on `al_outcome`; it must return the code too.

- [ ] **Step 1: Write the failing test**

Create `plugins/OutcomeTesting.Plugins.Tests/AccountabilityCodeTests.cs`:

```csharp
using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace OutcomeTesting.Plugins.Tests
{
    /// <summary>
    /// Trail Light columns L, N, R and T - the fail-accountability codes.
    ///
    /// These were blank whenever a specific person was named accountable, and deliberately
    /// so: a contact carried no code, and emitting the CASE's code beside someone else's
    /// name would have attributed the fail to a name and a code belonging to two different
    /// people. The registry makes that premise false, so the named person's own code goes in.
    /// </summary>
    public class AccountabilityCodeTests
    {
        private static Entity OutcomeCase()
        {
            var row = new Entity("al_outcomecase", Guid.NewGuid());
            row["al_advisername"] = "Jane Adviser";
            row["al_advisercode"] = "CASE-OLD";
            return row;
        }

        private static AccountablePersonFixture Named(
            FakeOrganizationService service, string name, string staffCode)
        {
            var id = Guid.NewGuid();
            service.Seed("contact", id,
                "fullname", name,
                "emailaddress1", "named@example.com",
                "al_staffcode", staffCode,
                "statecode", 0);
            return new AccountablePersonFixture { Id = id, Name = name };
        }

        private sealed class AccountablePersonFixture
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        private static Entity Outcome(AccountablePersonFixture person)
        {
            var row = new Entity("al_outcome", Guid.NewGuid());
            row[GenerateExportPlugin.FqAccountableContactAttr] =
                new EntityReference("contact", person.Id) { Name = person.Name };
            return row;
        }

        [Fact]
        public void A_named_person_brings_their_own_code()
        {
            var service = new FakeOrganizationService();
            var person = Named(service, "Robin Reviewer", "5150");

            var named = GenerateExportPlugin.NamedPerson(
                service, Outcome(person), GenerateExportPlugin.FqAccountableContactAttr);

            Assert.Equal("Robin Reviewer", named.Name);
            Assert.Equal("5150", named.StaffCode);
            Assert.Equal(
                "5150",
                GenerateExportPlugin.FlaggedText(true, OutcomeCase(), "al_advisercode", named));
        }

        [Fact]
        public void A_named_person_still_supplies_the_name_column()
        {
            var service = new FakeOrganizationService();
            var person = Named(service, "Robin Reviewer", "5150");

            var named = GenerateExportPlugin.NamedPerson(
                service, Outcome(person), GenerateExportPlugin.FqAccountableContactAttr);

            Assert.Equal(
                "Robin Reviewer",
                GenerateExportPlugin.FlaggedText(true, OutcomeCase(), "al_advisername", named));
        }

        [Fact]
        public void A_named_person_with_no_code_leaves_the_code_blank()
        {
            // NOT a fall back to the case's own code. That is the original defect: a name
            // and a code belonging to two different people reads as complete and is wrong.
            var service = new FakeOrganizationService();
            var person = Named(service, "Robin Reviewer", null);

            var named = GenerateExportPlugin.NamedPerson(
                service, Outcome(person), GenerateExportPlugin.FqAccountableContactAttr);

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(true, OutcomeCase(), "al_advisercode", named));
        }

        [Fact]
        public void Nobody_named_and_not_accountable_stays_empty()
        {
            var service = new FakeOrganizationService();

            Assert.Equal(
                string.Empty,
                GenerateExportPlugin.FlaggedText(false, OutcomeCase(), "al_advisercode", null));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AccountabilityCodeTests
```

Expected: FAIL — `NamedPerson` has the wrong arity and returns `string`.

- [ ] **Step 3: Introduce `AccountablePerson` and widen `NamedPerson`**

In `GenerateExportPlugin.cs`, replace the existing `NamedPerson` method with:

```csharp
        /// <summary>
        /// A contact named as accountable for a discipline: who they are, and their code.
        ///
        /// The code is why this is a pair rather than a name. Until 2026-09-22 a contact had
        /// no code at all, so the four accountability CODE columns were blanked whenever
        /// somebody was named - see FlaggedText. The registry now holds one.
        /// </summary>
        public sealed class AccountablePerson
        {
            /// <summary>The contact's display name, as the lookup reports it.</summary>
            public string Name { get; set; }

            /// <summary>Their staff code, or null where the registry holds none.</summary>
            public string StaffCode { get; set; }
        }

        /// <summary>
        /// The contact named as accountable for a discipline, or null where the export
        /// should use the people the case itself names.
        /// </summary>
        public static AccountablePerson NamedPerson(
            IOrganizationService service, Entity outcomeRow, string lookupAttribute)
        {
            if (outcomeRow == null)
            {
                return null;
            }

            var reference = outcomeRow.GetAttributeValue<EntityReference>(lookupAttribute);
            if (reference == null)
            {
                return null;
            }

            // Retrieved rather than taken from the lookup's Name alone: the lookup carries a
            // label, never the code. A contact that has been deleted since the judgement was
            // recorded still has its name on the row, so a failed read leaves the code null
            // rather than throwing - the name is the part that must survive.
            string staffCode = null;
            try
            {
                var contact = service.Retrieve(
                    ContactRegistry.Entity,
                    reference.Id,
                    new ColumnSet(ContactRegistry.StaffCodeAttr));
                staffCode = contact.GetAttributeValue<string>(ContactRegistry.StaffCodeAttr);
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                staffCode = null;
            }

            return new AccountablePerson { Name = reference.Name, StaffCode = staffCode };
        }
```

- [ ] **Step 4: Teach `FlaggedText` to use the code**

Replace both `FlaggedText` overloads with:

```csharp
        public static string FlaggedText(bool accountable, Entity outcomeCase, string caseAttribute)
        {
            return FlaggedText(accountable, outcomeCase, caseAttribute, null);
        }

        /// <summary>
        /// The text AD-039 wants in an accountability column: empty when the person that
        /// column names does not carry this fail, and otherwise their name or code.
        ///
        /// <paramref name="namedPerson"/> overrides the case's own value (project owner,
        /// 2026-09-19). Accountability can be given to any contact, not only the adviser or
        /// paraplanner the case names, and columns 11-20 are fixed as "fail adviser" and
        /// "fail paraplanner" pairs, so the chosen person is written into whichever slot the
        /// flags say they fill.
        ///
        /// A CODE column now carries THAT PERSON'S code (2026-09-22). It used to be blanked,
        /// because a contact carried no code and emitting the case's code beside someone
        /// else's name would attribute the fail to a name and a code belonging to two
        /// different people. The registry removed that constraint; the guard against mixing
        /// two people survives as the rule that the code must come from the same person as
        /// the name, and is blank when they have none.
        /// </summary>
        public static string FlaggedText(
            bool accountable, Entity outcomeCase, string caseAttribute, AccountablePerson namedPerson)
        {
            if (!accountable)
            {
                return string.Empty;
            }

            if (namedPerson != null && !string.IsNullOrWhiteSpace(namedPerson.Name))
            {
                return IsCodeColumn(caseAttribute)
                    ? (namedPerson.StaffCode ?? string.Empty).Trim()
                    : namedPerson.Name.Trim();
            }

            return outcomeCase.GetAttributeValue<string>(caseAttribute) ?? string.Empty;
        }
```

- [ ] **Step 5: Update the two call sites that build the named people**

In the record build, `NamedPerson` now takes the service:

```csharp
                var fqNamedPerson = NamedPerson(userService, outcomeRow, FqAccountableContactAttr);
                var aqNamedPerson = NamedPerson(userService, outcomeRow, AqAccountableContactAttr);
```

The eight `FlaggedText(...)` calls in the initialiser need no edit — they already pass `fqNamedPerson` / `aqNamedPerson`, whose type has changed.

**Important:** where the accountable person fills an *adviser* slot, the case attribute passed is still `"al_advisercode"`, and where they fill a *paraplanner* slot it is `"al_paraplannercode"`. Those strings now only select which slot the value lands in and whether `IsCodeColumn` is true; the value itself comes from `namedPerson`. Leave them as they are.

- [ ] **Step 6: Run test to verify it passes**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter AccountabilityCodeTests
```

Expected: PASS, 4 tests.

- [ ] **Step 7: Run the whole plugin suite**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests
```

Expected: PASS. `GenerateExportPluginTests` exercises `FlaggedText` and `NamedPerson` and will need its expectations updated where it asserted a blank code for a named person — that assertion is now wrong by direction, so change it to assert the person's code and note the date in the test's comment.

- [ ] **Step 8: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/GenerateExportPlugin.cs plugins/OutcomeTesting.Plugins.Tests/AccountabilityCodeTests.cs plugins/OutcomeTesting.Plugins.Tests/GenerateExportPluginTests.cs
git commit -m "feat(export): fail-accountability codes carry the named person's own code"
```

---

### Task 5: Trail Light columns B and D revert to codes

**Files:**
- Modify: `app/src/features/reports/trailLight.ts`
- Modify: `app/src/features/reports/trailLight.test.ts`

**Interfaces:**
- Consumes: nothing at runtime — reads `al_advisercode` / `al_paraplannercode` off the export record, which Tasks 3 and 4 populate.
- Produces: `TRAIL_LIGHT_HEADERS` (20 entries, indices 1 and 3 now `'Adviser Code'` / `'Paraplanner Code'`); `ExportRecord` = `Al_exportrecords` (no intersection).

- [ ] **Step 1: Rewrite the failing tests**

In `app/src/features/reports/trailLight.test.ts`, replace the three tests that assert emails in B and D. Delete the `paraplannerEmailIsGenerated` import and the test that asserts it.

```typescript
  it('carries codes in columns B and D', () => {
    // Project owner, 2026-09-22: Trailight could not accommodate the emails put here on
    // 2026-09-21 (AD-183), so the columns carry codes again. The codes now come from the
    // registry rather than from the case, which is what makes them non-empty.
    expect(TRAIL_LIGHT_HEADERS[1]).toBe('Adviser Code');
    expect(TRAIL_LIGHT_HEADERS[3]).toBe('Paraplanner Code');
  });

  it('is still twenty columns, the meaning of B and D having changed but not their place', () => {
    expect(TRAIL_LIGHT_HEADERS).toHaveLength(20);
    expect(TRAIL_LIGHT_HEADERS).not.toContain('Adviser Email');
    expect(TRAIL_LIGHT_HEADERS).not.toContain('Paraplanner Email');
  });

  it('writes the two codes into columns B and D, numeric ones as numbers', () => {
    const row = trailLightRow(
      record({ al_advisercode: '4471', al_paraplannercode: 'PP-01' }),
    );

    expect(row).toHaveLength(20);
    expect(row[1]).toBe(4471);
    expect(row[3]).toBe('PP-01');
  });

  it('leaves B or D blank rather than falling back to a name or an address', () => {
    // AD-039 reads by position: column B is the adviser's code on every row, or the file
    // lies about the rows where it is something else. A person the registry does not hold
    // a code for is a real outcome here.
    const row = trailLightRow(
      record({ al_advisername: 'Jane Adviser', al_paraplannername: 'Sam Paraplanner' }),
    );

    expect(row[1]).toBe('');
    expect(row[3]).toBe('');
  });
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd app && npx vitest run src/features/reports/trailLight.test.ts
```

Expected: FAIL — headers still read `'Adviser Email'`.

- [ ] **Step 3: Revert the headers**

In `app/src/features/reports/trailLight.ts`, change the first four entries of `TRAIL_LIGHT_HEADERS`:

```typescript
export const TRAIL_LIGHT_HEADERS = [
  'Adviser name',
  'Adviser Code',
  'Paraplanner Name',
  'Paraplanner Code',
```

Replace the "Columns B and D carry emails" paragraph in the block comment above it with:

```
 * **Columns B and D carry CODES (project owner, 2026-09-22).** Trailight could not
 * accommodate the emails put there on 2026-09-21 (AD-183). The positions are unchanged, so
 * nothing downstream shifts; what changes is what B and D mean, which is a change a
 * positional reader cannot detect for itself.
 *
 * **What makes this workable now, when it was not before.** The codes used to be
 * `al_outcomecase.al_advisercode` / `al_paraplannercode`, filled only by hand and almost
 * never filled (OD-050) — which is why the columns were empty and why emails went in. The
 * code is now held against the PERSON, on `contact.al_staffcode`, and resolved at
 * generation time.
 *
 * **Six columns, not two.** The four fail-accountability code columns (L, N, R, T) were
 * blanked whenever a specific person was named accountable, because a contact carried no
 * code. They now carry that person's own.
```

- [ ] **Step 4: Delete the intersection type and the guard**

Replace the `ExportRecord` type and remove `GeneratedHasParaplannerEmail` and `paraplannerEmailIsGenerated` entirely, along with their doc comments:

```typescript
/** The export record as the file builder needs it. */
export type ExportRecord = Al_exportrecords;
```

- [ ] **Step 5: Point the row builder at the codes**

In `trailLightRow`, replace the two email cells:

```typescript
    text(record.al_advisername),
    code(record.al_advisercode),
    text(record.al_paraplannername),
    // From `contact.al_staffcode`, resolved and snapshotted when the batch was generated
    // (GenerateExportPlugin.ParaplannerCode). Empty where the registry holds no code for
    // them — never the name or the address, which a positional reader could not tell apart
    // from a code.
    code(record.al_paraplannercode),
```

Update the `code()` helper's doc comment: it is used by **six** columns now, not four.

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd app && npx vitest run src/features/reports/trailLight.test.ts
```

Expected: PASS.

- [ ] **Step 7: Typecheck**

```bash
cd app && npm run build
```

Expected: no errors. `tsc --noEmit` checks nothing here — `tsc -b`, which `npm run build` runs, is what reports them.

- [ ] **Step 8: Commit**

```bash
git add app/src/features/reports/trailLight.ts app/src/features/reports/trailLight.test.ts
git commit -m "feat(export): Trail Light columns B and D carry codes again"
```

---

### Task 6: `al_UpdateUser` accepts a staff code

**Files:**
- Create: `src/customapis/al_UpdateUser/customapirequestparameters/StaffCode/customapirequestparameter.xml`
- Modify: `plugins/OutcomeTesting.Plugins/UpdateUserPlugin.cs`
- Modify: `app/.power/schemas/appschemas/dataSourcesInfo.ts`
- Modify: `app/src/services/commands/users.ts`
- Test: `app/src/services/commands/staffCodeParameter.test.ts`

**Interfaces:**
- Consumes: `ContactRegistry.StaffCodeAttr` (Task 1).
- Produces: `updateUser(input)` gains `staffCode?: string | null` on `UpdateUserInput`, marshalled as the `StaffCode` body parameter. Used by Task 7.

**Context for the implementer:** the Code App **silently discards** any command parameter `dataSourcesInfo.ts` does not declare, and the command still returns success. The generator that maintains that file lags new Custom API parameters by hours, so the declaration is written by hand and pinned by a test.

- [ ] **Step 1: Write the failing guard test**

Create `app/src/services/commands/staffCodeParameter.test.ts`:

```typescript
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

/**
 * The Code App discards a command parameter `dataSourcesInfo.ts` does not declare, in
 * silence, and the command still reports success. The generated file is regenerated from
 * Dataverse metadata, and that metadata lags a new Custom API parameter by hours, so this
 * declaration is written by hand.
 *
 * This test is the tripwire: if a regeneration ever drops StaffCode, the employee code
 * stops saving and nothing else would say so.
 */
describe('al_UpdateUser parameter declaration', () => {
  it('declares StaffCode, or the employee code silently never saves', () => {
    const path = fileURLToPath(
      new URL('../../../.power/schemas/appschemas/dataSourcesInfo.ts', import.meta.url),
    );
    const source = readFileSync(path, 'utf8');
    const command = source.slice(source.indexOf('"al_UpdateUser"'));
    const body = command.slice(0, command.indexOf('"responseInfo"'));

    expect(body).toContain('"name": "StaffCode"');
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd app && npx vitest run src/services/commands/staffCodeParameter.test.ts
```

Expected: FAIL — `StaffCode` is not declared.

- [ ] **Step 3: Declare the parameter to the Code App**

In `app/.power/schemas/appschemas/dataSourcesInfo.ts`, inside the `"al_UpdateUser"` block's `parameters` array, after `ExpectedRowVersion`:

```json
          {
            "name": "StaffCode",
            "in": "body",
            "required": false,
            "type": "string"
          },
```

- [ ] **Step 4: Declare the parameter to Dataverse**

Create `src/customapis/al_UpdateUser/customapirequestparameters/StaffCode/customapirequestparameter.xml`:

```xml
<customapirequestparameter uniquename="StaffCode">
  <description default="The person's staff code, shown as Employee code. Optional; an absent value leaves the stored code unchanged.">
    <label description="The person's staff code, shown as Employee code. Optional; an absent value leaves the stored code unchanged." languagecode="1033" />
  </description>
  <displayname default="Staff code">
    <label description="Staff code" languagecode="1033" />
  </displayname>
  <iscustomizable>1</iscustomizable>
  <isoptional>1</isoptional>
  <name>StaffCode</name>
  <type>10</type>
</customapirequestparameter>
```

- [ ] **Step 5: Write the staff code server-side**

In `plugins/OutcomeTesting.Plugins/UpdateUserPlugin.cs`, add the input constant beside the others:

```csharp
        private const string InStaffCode = "StaffCode";
```

Read it beside `expectedRowVersion`:

```csharp
            var staffCode = CommandHelpers.GetOptionalString(context, InStaffCode);
```

And write it onto the update, after `ContactRegistry.SetName(update, fullName);`:

```csharp
            // Absent leaves the stored code alone; an explicitly empty string clears it.
            // The distinction matters because the People page sends only what it edited,
            // and a save of somebody's NAME must not silently wipe their code.
            if (staffCode != null)
            {
                update[ContactRegistry.StaffCodeAttr] = staffCode.Trim().Length == 0
                    ? null
                    : staffCode.Trim();
            }
```

- [ ] **Step 6: Marshal it from the client**

In `app/src/services/commands/users.ts`, extend `UpdateUserInput` and `updateUser`:

```typescript
export interface UpdateUserInput {
  userId: string;
  fullName: string;
  /**
   * The person's staff code, shown as "Employee code". Omit to leave it unchanged; pass
   * an empty string to clear it.
   */
  staffCode?: string | null;
  expectedRowVersion?: string | null;
  idempotencyKey: string;
}
```

```typescript
export function updateUser(input: UpdateUserInput): Promise<CommandResult<UpdateUserOutput>> {
  const body: Record<string, unknown> = {
    UserId: input.userId,
    FullName: input.fullName,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.expectedRowVersion) body.ExpectedRowVersion = input.expectedRowVersion;
  if (input.staffCode !== undefined && input.staffCode !== null) {
    body.StaffCode = input.staffCode;
  }
  return executeCommand<UpdateUserOutput>('al_UpdateUser', body);
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
cd app && npx vitest run src/services/commands/staffCodeParameter.test.ts
```

Expected: PASS.

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/customapis/al_UpdateUser plugins/OutcomeTesting.Plugins/UpdateUserPlugin.cs app/.power/schemas/appschemas/dataSourcesInfo.ts app/src/services/commands/users.ts app/src/services/commands/staffCodeParameter.test.ts
git commit -m "feat(registry): al_UpdateUser accepts a staff code, declared by hand"
```

---

### Task 7: The People page shows and edits the employee code, and filters by role

**Files:**
- Modify: `app/src/hooks/useUserDirectory.ts`
- Modify: `app/src/features/people/PeoplePage.tsx`
- Modify: `app/src/features/people/PersonModals.tsx`
- Create: `app/src/features/people/peopleFilters.ts`
- Test: `app/src/features/people/peopleFilters.test.ts`

**Interfaces:**
- Consumes: `updateUser` with `staffCode` (Task 6).
- Produces: `DirectoryUser.staffCode` (`string | null`); `matchesRole(roles: readonly string[], filter: string): boolean` and `ROLE_FILTERS` from `peopleFilters.ts`.

- [ ] **Step 1: Write the failing test**

Create `app/src/features/people/peopleFilters.test.ts`:

```typescript
import { describe, expect, it } from 'vitest';
import { ROLE_FILTERS, matchesRole } from './peopleFilters';

describe('People role filter', () => {
  it('offers the six roles people actually hold', () => {
    // The three checker roles stay distinct (D6): they are what al_Role is seeded with and
    // what the Tax/AQS review routing already distinguishes.
    expect(ROLE_FILTERS).toEqual([
      'Adviser',
      'Paraplanner',
      'T&C Manager',
      'Tax Checker',
      'AQS Checker',
      'Senior Checker',
    ]);
  });

  it('matches anyone holding the role, not only those holding it alone', () => {
    // A person can hold several (D8) - a T&C Manager who also advises is one person, not a
    // reason to show them twice or to hide them from either filter.
    expect(matchesRole(['T&C Manager', 'Adviser'], 'Adviser')).toBe(true);
    expect(matchesRole(['T&C Manager', 'Adviser'], 'T&C Manager')).toBe(true);
  });

  it('lets everyone through when no role is chosen', () => {
    expect(matchesRole([], 'all')).toBe(true);
    expect(matchesRole(['Adviser'], 'all')).toBe(true);
  });

  it('excludes someone who holds no roles when a role is chosen', () => {
    expect(matchesRole([], 'Adviser')).toBe(false);
  });

  it('ignores casing and surrounding space, as the mappings are hand-entered', () => {
    expect(matchesRole(['  adviser '], 'Adviser')).toBe(true);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd app && npx vitest run src/features/people/peopleFilters.test.ts
```

Expected: FAIL — cannot resolve `./peopleFilters`.

- [ ] **Step 3: Write the filter module**

Create `app/src/features/people/peopleFilters.ts`:

```typescript
/**
 * The roles the People page filters by (project owner, 2026-09-22).
 *
 * These are the roles `al_Role` is seeded with that describe what a person DOES. The three
 * checker roles stay distinct rather than collapsing into one "Checker": that is how they
 * are seeded, and the Tax/AQS review routing already tells them apart, so grouping them
 * here would put a distinction the system relies on behind a label that hides it.
 *
 * Administrator, Outcome Testing Manager, Reviewer and Read Only User are deliberately
 * absent — they describe access to this application rather than a job on a case.
 */
export const ROLE_FILTERS = [
  'Adviser',
  'Paraplanner',
  'T&C Manager',
  'Tax Checker',
  'AQS Checker',
  'Senior Checker',
] as const;

/**
 * Whether a person's held roles satisfy the chosen filter.
 *
 * A person may hold several roles, so this asks whether they hold the chosen one at all
 * rather than whether it is their only one. Compared case-insensitively and trimmed,
 * because role mappings are keyed on a hand-entered email and carry a hand-entered label.
 */
export function matchesRole(roles: readonly string[], filter: string): boolean {
  if (filter === 'all') return true;
  const wanted = filter.trim().toLowerCase();
  return roles.some((role) => role.trim().toLowerCase() === wanted);
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd app && npx vitest run src/features/people/peopleFilters.test.ts
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Carry the staff code on the directory row**

In `app/src/hooks/useUserDirectory.ts`, add to `DirectoryUser`:

```typescript
  /** The person's staff code, shown as "Employee code". Null where none is held. */
  staffCode: string | null;
```

and in `toDirectoryUser`'s returned object:

```typescript
    staffCode: contact.al_staffcode?.trim() || null,
```

If `Contacts` (the generated model) does not yet declare `al_staffcode`, read it through a
local widening rather than editing the generated file, which a regeneration would discard:

```typescript
type ContactWithStaffCode = Contacts & { al_staffcode?: string };
```

and take the parameter as `ContactWithStaffCode`.

- [ ] **Step 6: Rework the table and filters**

In `app/src/features/people/PeoplePage.tsx`:

1. Import the filter module and add a role filter state beside `activeFilter`:

```typescript
import { ROLE_FILTERS, matchesRole } from './peopleFilters';
```

```typescript
  const [roleFilter, setRoleFilter] = useState<string>('all');
```

2. Apply it in `filtered`, and search the staff code rather than the case's code:

```typescript
  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return rows.filter((row) => {
      if (activeFilter === 'active' && !row.active) return false;
      if (activeFilter === 'inactive' && row.active) return false;
      if (!matchesRole(row.roles, roleFilter)) return false;
      const haystack = `${row.name} ${row.email} ${row.user?.staffCode ?? ''} ${row.roles.join(' ')}`;
      if (term && !haystack.toLowerCase().includes(term)) {
        return false;
      }
      return true;
    });
  }, [rows, search, activeFilter, roleFilter]);
```

3. Add the control inside the existing `FilterBar`:

```tsx
            <FilterField label="Role" htmlFor="people-role">
              <select
                id="people-role"
                value={roleFilter}
                onChange={(event) => setRoleFilter(event.target.value)}
              >
                <option value="all">All roles</option>
                {ROLE_FILTERS.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
            </FilterField>
```

4. Include `roleFilter` in `isFiltered` and in the Clear handler:

```typescript
  const isFiltered = search.trim() !== '' || activeFilter !== 'all' || roleFilter !== 'all';
```

5. Trim the table to the four columns asked for. Replace the `<thead>` row with:

```tsx
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Work email</th>
                  <th scope="col">Role</th>
                  <th scope="col">Employee code</th>
                  {canManage ? <th scope="col">Actions</th> : null}
                </tr>
```

and the body cells to match, rendering `row.roles.join(', ') || '—'` for Role and
`row.user?.staffCode ?? '—'` for Employee code. Keep the name cell's existing drill-down
`<Link>`, which still points at `/people/:role/:name`.

6. Leave `EXPORT_HEADERS` and the `ExportMenu` rows **unchanged**. The caseload figures
leave the table but stay in the export, which is what keeps the workload MI reachable after
the page moves under administration.

7. Update `PageIntro`'s `purpose` to say what the page is now:

```
purpose="The people known to the application, their roles and their employee codes. Sourced from Contacts and keyed on work email (AD-010). A role here grants access to this application; changes are enforced server-side and recorded in the audit trail (AD-041)."
```

- [ ] **Step 7: Let the edit modal set the employee code**

In `app/src/features/people/PersonModals.tsx`, add the field to `EditPersonModal`. Beside
the existing name state:

```tsx
  const [staffCode, setStaffCode] = useState(user.staffCode ?? '');
```

In the form, after the name field:

```tsx
        <label className="field" htmlFor="person-staff-code">
          <span className="field__label">Employee code</span>
          <input
            id="person-staff-code"
            type="text"
            value={staffCode}
            maxLength={50}
            onChange={(event) => setStaffCode(event.target.value)}
          />
          <span className="field__hint">
            Fills this person&rsquo;s code on the Trail Light export. Leave empty if they have none.
          </span>
        </label>
```

And in the submit handler, pass it alongside the name. It is always sent, so clearing the
box clears the stored code — the server reads an empty string as "clear" and an absent
parameter as "leave alone" (Task 6, Step 5):

```tsx
    const result = await updateUser({
      userId: user.id,
      fullName: name.trim(),
      staffCode: staffCode.trim(),
      expectedRowVersion: user.rowVersion,
      idempotencyKey: intent.keyFor(`edit:${user.id}`),
    });
```

- [ ] **Step 8: Run the app suite and typecheck**

```bash
cd app && npx vitest run
```

Expected: PASS. Any People page test asserting the old columns needs updating to the four.

```bash
cd app && npm run build
```

Expected: no errors.

- [ ] **Step 9: Commit**

```bash
git add app/src/hooks/useUserDirectory.ts app/src/features/people/
git commit -m "feat(people): employee code and role filter on the People page"
```

---

### Task 8: Administrators assign and withdraw roles from the People page

**Files:**
- Create: `app/src/features/people/RoleAssignment.tsx`
- Create: `app/src/features/people/roleAssignment.ts`
- Modify: `app/src/features/people/PeoplePage.tsx`
- Test: `app/src/features/people/roleAssignment.test.ts`

**Interfaces:**
- Consumes: `ROLE_FILTERS`, `matchesRole` (Task 7); `RoleMappingRow` from `../admin/useSecurityConfig`; `assignUserRole` and `setRoleAssignmentActive` from `../../services/commands/permissions`.
- Produces: `mappingFor(mappings, email, role)` returning `RoleMappingRow | null`; `<RoleAssignment>`, rendered in the People table's Role cell.

**Context for the implementer:** this is the "admins can do the mapping themselves" half
(spec D5). It writes `al_userrolemapping`, which `PermissionHelpers` reads as an
authorisation source — so **assigning a role here grants access to the application.** No new
Custom API: `assignUserRole` creates or reactivates a mapping and `setRoleAssignmentActive`
withdraws one. A withdrawn mapping keeps its row for the audit trail, which is why
withdrawing is a state change rather than a delete.

`assignUserRole` takes `roleCode` for a custom `al_Role` and `appRole` for a built-in web
role; the six roles on this page are custom, so `roleCode` is the one to send. The role
codes are the seeded ones: `ROLE-ADVISER`, `ROLE-PARAPLANNER`, `ROLE-T-C-MANAGER`,
`ROLE-TAX-CHECKER`, `ROLE-AQS-CHECKER`, `ROLE-SENIOR-CHECKER`.

- [ ] **Step 1: Write the failing test**

Create `app/src/features/people/roleAssignment.test.ts`:

```typescript
import { describe, expect, it } from 'vitest';
import { ROLE_CODES, mappingFor } from './roleAssignment';
import type { RoleMappingRow } from '../admin/useSecurityConfig';

function mapping(overrides: Partial<RoleMappingRow> = {}): RoleMappingRow {
  return {
    id: 'map-1',
    email: 'jane.adviser@example.com',
    role: 'Adviser',
    active: true,
    ...overrides,
  };
}

describe('Role assignment from the People page', () => {
  it('knows the seeded code for each role it offers', () => {
    // assignUserRole distinguishes a custom al_Role (RoleCode) from a built-in web role
    // (AppRole). All six of these are custom, so all six need their seeded code.
    expect(ROLE_CODES['Adviser']).toBe('ROLE-ADVISER');
    expect(ROLE_CODES['T&C Manager']).toBe('ROLE-T-C-MANAGER');
    expect(ROLE_CODES['Tax Checker']).toBe('ROLE-TAX-CHECKER');
    expect(ROLE_CODES['Senior Checker']).toBe('ROLE-SENIOR-CHECKER');
  });

  it('finds the active mapping for a person and role', () => {
    const found = mappingFor([mapping()], 'jane.adviser@example.com', 'Adviser');
    expect(found?.id).toBe('map-1');
  });

  it('ignores a withdrawn mapping, so re-assigning is offered again', () => {
    // The row is kept for the audit trail but no longer describes what someone holds.
    const found = mappingFor([mapping({ active: false })], 'jane.adviser@example.com', 'Adviser');
    expect(found).toBeNull();
  });

  it('matches the email case-insensitively', () => {
    // al_userrolemapping is keyed on a hand-entered email.
    const found = mappingFor([mapping()], 'Jane.Adviser@Example.com', 'Adviser');
    expect(found?.id).toBe('map-1');
  });

  it('does not confuse one role with another for the same person', () => {
    const found = mappingFor([mapping()], 'jane.adviser@example.com', 'Paraplanner');
    expect(found).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd app && npx vitest run src/features/people/roleAssignment.test.ts
```

Expected: FAIL — cannot resolve `./roleAssignment`.

- [ ] **Step 3: Write the lookup module**

Create `app/src/features/people/roleAssignment.ts`:

```typescript
import type { RoleMappingRow } from '../admin/useSecurityConfig';
import { ROLE_FILTERS } from './peopleFilters';

/**
 * The seeded `al_Role` code behind each role the People page offers.
 *
 * `assignUserRole` branches on RoleCode first and treats an empty AppRole as absent, so a
 * custom role must be sent by its code. These are the codes in `data/roles-seed`; a typo
 * here would create a mapping to a role nothing holds, which reads as success.
 */
export const ROLE_CODES: Record<(typeof ROLE_FILTERS)[number], string> = {
  Adviser: 'ROLE-ADVISER',
  Paraplanner: 'ROLE-PARAPLANNER',
  'T&C Manager': 'ROLE-T-C-MANAGER',
  'Tax Checker': 'ROLE-TAX-CHECKER',
  'AQS Checker': 'ROLE-AQS-CHECKER',
  'Senior Checker': 'ROLE-SENIOR-CHECKER',
};

/**
 * The active mapping granting one person one role, or null where they do not hold it.
 *
 * Withdrawn mappings are ignored: the row survives for the audit trail but no longer says
 * what somebody holds, and treating it as current would leave the page offering "withdraw"
 * for a role already withdrawn.
 */
export function mappingFor(
  mappings: readonly RoleMappingRow[],
  email: string,
  role: string,
): RoleMappingRow | null {
  const wantedEmail = email.trim().toLowerCase();
  const wantedRole = role.trim().toLowerCase();

  return (
    mappings.find(
      (row) =>
        row.active &&
        row.email.trim().toLowerCase() === wantedEmail &&
        row.role.trim().toLowerCase() === wantedRole,
    ) ?? null
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
cd app && npx vitest run src/features/people/roleAssignment.test.ts
```

Expected: PASS, 5 tests.

- [ ] **Step 5: Build the cell control**

Create `app/src/features/people/RoleAssignment.tsx`. It renders the roles a person holds and,
for an administrator, a control to grant or withdraw one.

```tsx
import { useState } from 'react';
import { ROLE_FILTERS } from './peopleFilters';
import { ROLE_CODES, mappingFor } from './roleAssignment';
import type { RoleMappingRow } from '../admin/useSecurityConfig';

interface Props {
  email: string;
  roles: string[];
  mappings: readonly RoleMappingRow[];
  canManage: boolean;
  busy: boolean;
  onGrant: (email: string, role: string) => void;
  onWithdraw: (mappingId: string, email: string, role: string) => void;
}

/**
 * One person's roles, and an administrator's controls to change them.
 *
 * Every role they hold is listed, not just one (D8): a T&C Manager who also advises is one
 * person holding two, and picking one to display would misreport them.
 *
 * The control says "grants access" in as many words. This writes al_userrolemapping, which
 * PermissionHelpers reads when it decides what a caller may do — an administrator tagging
 * somebody "Adviser" for reporting is also letting them in, and that should not be a
 * surprise discovered later.
 */
export function RoleAssignment({
  email,
  roles,
  mappings,
  canManage,
  busy,
  onGrant,
  onWithdraw,
}: Props) {
  const [choice, setChoice] = useState('');

  if (email === '') {
    // Someone named on a case who is not in the registry. There is no mapping to hold,
    // because al_userrolemapping is keyed on work email.
    return <span className="people__muted">Not in directory</span>;
  }

  return (
    <div className="people__roles">
      {roles.length === 0 ? (
        <span className="people__muted">No roles</span>
      ) : (
        <ul className="people__role-list">
          {roles.map((role) => {
            const held = mappingFor(mappings, email, role);
            return (
              <li key={role}>
                <span>{role}</span>
                {canManage && held ? (
                  <button
                    type="button"
                    className="people__role-withdraw"
                    disabled={busy}
                    onClick={() => onWithdraw(held.id, email, role)}
                  >
                    Withdraw
                  </button>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}

      {canManage ? (
        <div className="people__role-grant">
          <label className="visually-hidden" htmlFor={`grant-${email}`}>
            Grant a role to {email}
          </label>
          <select
            id={`grant-${email}`}
            value={choice}
            disabled={busy}
            onChange={(event) => setChoice(event.target.value)}
          >
            <option value="">Grant a role&hellip;</option>
            {ROLE_FILTERS.filter((role) => !mappingFor(mappings, email, role)).map((role) => (
              <option key={role} value={role}>
                {role}
              </option>
            ))}
          </select>
          <button
            type="button"
            disabled={busy || choice === '' || !(choice in ROLE_CODES)}
            onClick={() => {
              onGrant(email, choice);
              setChoice('');
            }}
          >
            Grant
          </button>
        </div>
      ) : null}
    </div>
  );
}
```

- [ ] **Step 6: Wire it into the page**

In `app/src/features/people/PeoplePage.tsx`:

1. Import the control and the commands:

```typescript
import { RoleAssignment } from './RoleAssignment';
import { ROLE_CODES } from './roleAssignment';
import { assignUserRole, setRoleAssignmentActive } from '../../services/commands/permissions';
```

2. Add a busy token so two clicks cannot race:

```typescript
  const [roleBusy, setRoleBusy] = useState<string | null>(null);
```

3. Add the two handlers beside `onToggleActive`:

```typescript
  async function onGrantRole(email: string, role: string) {
    setBanner(null);
    setRoleBusy(email);
    const token = `grant:${email}:${role}`;
    const result = await assignUserRole({
      userEmail: email,
      roleCode: ROLE_CODES[role as keyof typeof ROLE_CODES],
      idempotencyKey: intent.keyFor(token),
    });
    setRoleBusy(null);
    if (result.ok) {
      intent.release(token);
      setBanner({ tone: 'success', message: `${role} granted to ${email}.` });
      reload();
    } else {
      setBanner({ tone: 'error', message: messageForFailure(result) });
    }
  }

  async function onWithdrawRole(mappingId: string, email: string, role: string) {
    setBanner(null);
    setRoleBusy(email);
    const token = `withdraw:${mappingId}`;
    const result = await setRoleAssignmentActive({
      id: mappingId,
      active: false,
      idempotencyKey: intent.keyFor(token),
    });
    setRoleBusy(null);
    if (result.ok) {
      intent.release(token);
      setBanner({ tone: 'success', message: `${role} withdrawn from ${email}.` });
      reload();
    } else {
      setBanner({ tone: 'error', message: messageForFailure(result) });
    }
  }
```

4. Render it in the Role cell, replacing the plain `row.roles.join(', ')` from Task 7:

```tsx
                      <td>
                        <RoleAssignment
                          email={row.email}
                          roles={row.roles}
                          mappings={security.status === 'ready' ? security.mappings : []}
                          canManage={canManage}
                          busy={roleBusy === row.email}
                          onGrant={onGrantRole}
                          onWithdraw={onWithdrawRole}
                        />
                      </td>
```

- [ ] **Step 7: Run the suite and typecheck**

```bash
cd app && npx vitest run && npm run build
```

Expected: PASS, no type errors.

- [ ] **Step 8: Commit**

```bash
git add app/src/features/people/roleAssignment.ts app/src/features/people/roleAssignment.test.ts app/src/features/people/RoleAssignment.tsx app/src/features/people/PeoplePage.tsx
git commit -m "feat(people): administrators grant and withdraw roles from the registry"
```

---

### Task 9: People moves under administration

**Files:**
- Modify: `app/src/app/router.tsx:79-95`
- Modify: `app/src/types/permissions.ts`
- Modify: any navigation component linking to `/people` (search for the literal)
- Test: `app/src/types/permissions.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: the route `/admin/people`, gated by the existing `page.admin.users` resource key.

**Context for the implementer:** `page.admin.users` already exists in `RESOURCE_KEYS` and is already granted to Administrators in the bulk grant. `/admin/users` currently redirects to `/people`. This task inverts that.

- [ ] **Step 1: Write the failing test**

Append to `app/src/types/permissions.test.ts`:

```typescript
describe('People under administration (2026-09-22)', () => {
  it('routes /admin/people to the administration resource, not the cases one', () => {
    // Required, not cosmetic. The page's Role column writes al_userrolemapping, which is an
    // authorisation source, so leaving it on page.cases would let every case-worker grant
    // themselves and anyone else access.
    expect(resourceForPath('/admin/people')).toBe('page.admin.users');
  });

  it('keeps the per-person caseload drill-down available to case-workers', () => {
    expect(resourceForPath('/people/Adviser/Jane%20Adviser')).toBe('page.cases');
  });
});
```

Import `resourceForPath` alongside the file's existing imports if it is not already imported.

- [ ] **Step 2: Run test to verify it fails**

```bash
cd app && npx vitest run src/types/permissions.test.ts
```

Expected: FAIL — `/admin/people` falls through to the default.

- [ ] **Step 3: Map the path**

In `app/src/types/permissions.ts`, in the `resourceForPath` chain, **before** the
`/admin/...` entries that would not match and before any `/people` entry:

```typescript
  if (path.startsWith('/admin/people')) return 'page.admin.users';
```

Leave the existing `/people` mapping in place — it still serves the drill-down.

- [ ] **Step 4: Move the route**

In `app/src/app/router.tsx`, replace the `/people` route with an admin-gated one at the new
path, keep the drill-down where it is, and repoint the legacy redirects:

```tsx
        {/*
          The registry: who the application knows, what they hold and their employee code
          (project owner, 2026-09-22). Under administration because the Role column writes
          al_userrolemapping, which PermissionHelpers reads as an authorisation source - on
          page.cases, where this page used to live, every case-worker could grant roles.
        */}
        <Route
          path="/admin/people"
          element={
            <RequirePermission resource="page.admin.users">
              <PeoplePage />
            </RequirePermission>
          }
        />
        {/* The caseload drill-down stays a case view: it shows work, not access. */}
        <Route
          path="/people/:role/:name"
          element={
            <RequirePermission resource="page.cases">
              <PersonCasesPage />
            </RequirePermission>
          }
        />
        <Route path="/people" element={<Navigate to="/admin/people" replace />} />
```

and change the existing `/admin/users` redirect to point at the new path:

```tsx
        <Route path="/admin/users" element={<Navigate to="/admin/people" replace />} />
```

- [ ] **Step 5: Repoint the navigation**

```bash
cd app && grep -rn "'/people'\|\"/people\"" src --include=*.tsx --include=*.ts
```

Update every navigation link and `<Link to>` that pointed at the directory to
`/admin/people`, and move the entry into the administration group of the nav component.
Leave `/people/${role}/${name}` drill-down links alone.

- [ ] **Step 6: Run the tests and typecheck**

```bash
cd app && npx vitest run && npm run build
```

Expected: PASS, no type errors.

- [ ] **Step 7: Commit**

```bash
git add app/src/app/router.tsx app/src/types/permissions.ts app/src/types/permissions.test.ts app/src/components
git commit -m "feat(people): move the registry under administration"
```

---

### Task 10: Retire the case's own code fields from editing

**Files:**
- Modify: `app/src/features/cases/CaseEditPanel.tsx:108-111`
- Modify: `plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs:523-526`
- Modify: `app/src/features/reviews/checklistForm.ts:294-296`
- Test: `plugins/OutcomeTesting.Plugins.Tests/UpdateCaseDetailsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. This removes a write path.

- [ ] **Step 1: Write the failing test**

Append to `plugins/OutcomeTesting.Plugins.Tests/UpdateCaseDetailsTests.cs`:

```csharp
        /// <summary>
        /// The per-case codes are no longer editable (project owner, 2026-09-22). A code is
        /// a property of a PERSON and lives on contact.al_staffcode; leaving a second,
        /// hand-typed copy on the case would give two answers that could disagree, and the
        /// export reads only the registry.
        ///
        /// The columns stay in the database holding whatever was typed into them. Nothing
        /// reads them.
        /// </summary>
        [Fact]
        public void The_case_code_columns_are_not_editable()
        {
            Assert.False(UpdateCaseDetailsPlugin.Editables.ContainsKey("al_advisercode"));
            Assert.False(UpdateCaseDetailsPlugin.Editables.ContainsKey("al_paraplannercode"));
        }
```

- [ ] **Step 2: Run test to verify it fails**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests --filter UpdateCaseDetails
```

Expected: FAIL — both keys are present.

- [ ] **Step 3: Remove the two server-side entries**

In `plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs`, delete the
`al_advisercode` and `al_paraplannercode` entries from `Editables`, and note why above the
dictionary:

```csharp
            // al_advisercode and al_paraplannercode are deliberately absent from 2026-09-22.
            // The code is held against the person, on contact.al_staffcode, and the export
            // resolves it from there. A hand-typed copy on the case could only disagree.
```

- [ ] **Step 4: Remove the two client-side fields**

In `app/src/features/cases/CaseEditPanel.tsx`, delete the `al_advisercode` and
`al_paraplannercode` entries from the field list.

In `app/src/features/reviews/checklistForm.ts`, **delete** the two code rows from the case
header summary:

```typescript
    { label: 'Adviser code', value: text(record.al_advisercode) },
```
```typescript
    { label: 'Paraplanner code', value: text(record.al_paraplannercode) },
```

Deleted rather than repointed at the registry. This summary is built from one read of the
case; resolving two contacts to show a code a checker does not act on would add two reads
to every review page load for no decision it informs. The codes are visible on the People
page, which is where they are now maintained.

- [ ] **Step 5: Run the suites**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'; dotnet test plugins/OutcomeTesting.Plugins.Tests
```

```bash
cd app && npx vitest run && npm run build
```

Expected: PASS. `checklistForm.test.ts` asserts the two code rows and needs updating.

- [ ] **Step 6: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs plugins/OutcomeTesting.Plugins.Tests/UpdateCaseDetailsTests.cs app/src/features/cases/CaseEditPanel.tsx app/src/features/reviews/checklistForm.ts app/src/features/reviews/checklistForm.test.ts
git commit -m "refactor(cases): retire the per-case adviser and paraplanner codes"
```

---

### Task 11: Decision log, deploy to DEV, and verify

**Files:**
- Modify: `knowledge/decision-log.md`

- [ ] **Step 1: Record the decision**

Append to the decision table in `knowledge/decision-log.md`, taking the next free AD number:

```markdown
| AD-206 | **The staff code is held against the PERSON, and Trail Light columns B and D carry codes again.** `contact.al_staffcode` is new; `NotificationOutbox.MatchPerson` returns it with the match; `GenerateExportPlugin` snapshots it at generation time; `TRAIL_LIGHT_HEADERS[1]` and `[3]` are back to "Adviser Code" and "Paraplanner Code". **Six columns, not two** - the four fail-accountability code columns (L, N, R, T) were blanked whenever a specific person was named, on the stated grounds that "a contact carries no adviser or paraplanner code", and now carry that person's own. **This supersedes AD-183**, which put emails in B and D on 2026-09-21 and shipped to TEST at 1.0.6.0. | Project owner, 2026-09-22: Trailight "said it would be difficult to accommodate that change to email rather than code". **The emails were never the point** - they went in because the codes were EMPTY. `al_advisercode` and `al_paraplannercode` were filled only by hand, OD-050 expected Tax/AQS to type them, and nothing ever did. Reverting to codes is only viable because they now come from somewhere. **`ParaplannerEmail` stays mapped on the import** (AD-186): it is the join key that resolves the para-planner to exactly one contact, and without it the code would be blank on precisely the rows where two people share a name - the failure this change exists to remove. It also still addresses their letter, which AD-165 records is their only sight of the check. **The People page moved to `/admin/people`**, gated by `page.admin.users`: its Role column writes `al_userrolemapping`, which `PermissionHelpers` reads as an authorisation source, and on `page.cases` every case-worker could have granted roles. | 2026-09-22 |
```

Then amend the OD-050 row: adviser code and paraplanner code are struck from the list of
fields "manually completed by Tax/AQS", because they are no longer held on the case at all.
Leave the other nine, which the extract still does not carry.

- [ ] **Step 2: Build the plugin assembly in Release**

```powershell
dotnet build plugins/OutcomeTesting.Plugins -c Release
```

The `pushassembly` verb uploads `bin/Release` **without building it**, so this step is what
makes the upload carry today's code. Check the byte count changed.

- [ ] **Step 3: Upload the portal templates**

Two commits in this body of work changed `powerpages/.../OT-Case-Detail` and
`OT-Review-Detail` - they stop rendering the `data-ot-hdr="al_advisercode"` and
`data-ot-hdr="al_paraplannercode"` text boxes - and the deployment sequence omitted them
entirely, so the portal would have kept serving the old page indefinitely (2026-09-22
review).

```powershell
powershell -NoProfile -File .\powerpages\Deploy-Portal.ps1 -OrgUrl <DEV org url>
```

**Not a bare `pac powerpages upload`.** `powerpages/Deploy-Portal.ps1` is documented in its
own header as the only safe way to upload to this site, and it is what carries
`--modelVersion Enhanced` (line 308) - the flag every `pac pages` call against this site
needs, because on the enhanced model one table holds pages and templates and without it the
seeded GUIDs collide. Run bare, `pac pages upload` either aborts on a parent-scoped table
permission part-way through the component list, or completes and **deletes** every table
permission the manifest lists but the source folder no longer contains - which on
2026-09-06 left DEV holding 2 of 13 and took out the reviewer write path (OD-034). The
script moves `table-permissions/` aside, strips those manifest sections, uploads, then
restores the permissions by direct write and verifies by query.

**Order this with or before the solution import, never after.** The import is what removes
the two codes from `CaseHeaderRequestPlugin.CheckerEditable`. Import first and the live
page still offers a checker two editable boxes whose saves the server has just begun
refusing - a visible control that silently fails, which is worse than the control being
gone.

- **`powerpages/` here is hand-authored source, not a mirror of the live site.** An upload
  sends the whole tree and reports success either way, so anything changed on the live site
  since this folder was last touched is silently overwritten by a stale local copy. Check
  first rather than after: `-VerifyOnly` reports what is actually deployed without writing
  anything, and a `pac pages download` into a scratch folder diffed against this tree is
  what the 2026-09-19 and 2026-09-20 deployment notes did before every upload. If anything
  but the two templates differs, reconcile before uploading.
- **A successful-looking upload is not evidence.** On this site components ordered after a
  failure are silently skipped, so confirm the two templates by query or by opening the
  page, not by the absence of an error.

- [ ] **Step 4: Deploy the solution to DEV**

```powershell
pac solution import --path <packed solution> --environment Env_AQ_Dev --activate-plugins --force-overwrite
```

`Env_AQ_Dev` only — every environment write targets DEV until the project owner names
another. Two things about this command:

- **Pass `--activate-plugins`.** An import that carries step XML without it switches the
  steps off, which silently disabled six steps on 2026-09-02.
- **A silent CLI does not mean nothing happened.** The job runs server-side, so confirm by
  querying the `importjob` table rather than trusting the absence of output.

- [ ] **Step 5: Build and push the Code App**

```bash
cd app && npm run build
```

```powershell
npx pa app push
```

`pa app push` uploads `app/dist` **without building it**, so the build above is what makes
the push carry today's bundle — check the bundle hash changed. After the push, open the
`/app/` URL with the `sourcetime` the push prints; the short `/a/{appId}` URL keeps serving
the previous bundle and would show you yesterday's page while you verify.

- [ ] **Step 6: Verify against DEV**

1. Open `/admin/people` as an administrator. Confirm the four columns, the role filter and
   the search.
2. Set an employee code on a contact, save, reload, and confirm it persisted. **This is the
   step that catches the silent-drop trap** — if `StaffCode` were undeclared the save would
   report success and the value would vanish on reload.
3. Grant a role to a test contact, confirm it appears, then withdraw it and confirm it
   goes. Check `al_userrolemapping` directly: the withdrawn row must still be there, now
   inactive, because it is audit evidence and not a mistake to be erased.
4. Confirm a non-administrator is refused `/admin/people`.
5. Generate a Trail Light batch for a closed case whose adviser and para-planner both hold
   codes. Confirm columns B and D carry the codes and that the file still has 20 columns.
6. Generate one for a case with a named fail-accountable person and confirm their code
   appears in the matching code column, beside their name.
7. Generate one for a case with NO named fail-accountable person - the common shape, since
   accountability is otherwise derived from the case's own people. Columns L, N, R and T
   must carry the REGISTRY code for those people, or be blank where the registry holds
   none. They must never carry `al_advisercode`/`al_paraplannercode`, which the
   registration tool still seeds as `ADV-S01`, so a seeded case is the sharpest test.
8. Open the portal case-detail page as a checker and confirm the adviser code and
   paraplanner code boxes are gone. If they are still there, Step 3 did not take.

- [ ] **Step 7: Commit**

```bash
git add knowledge/decision-log.md
git commit -m "docs(decisions): staff codes on the registry, superseding AD-183"
```

---

## Sequencing note

Codes must be loaded onto contacts **before** the next Trail Light batch is generated.
Between deployment and that load, columns B and D will be blank where they currently carry
email addresses. This is operational, not a code change, and it is the one way this work
could land worse than what is in TEST today.
