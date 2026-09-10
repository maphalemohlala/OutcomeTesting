# Remedial Form Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The remediation form the adviser fills on a remediation action, and the per-case remediation view, match the agreed paper form: a numbered table of No. | Issue / fail reason | Remedial action | Owner | Target date | Sign-off, then Client contact required? (Yes / No / Potentially), Recheck required? (Yes / No), Do the remedial actions change the advice? (Yes / No), All remedial actions checked and approved?, Regraded outcome, Date, Supervisor sign-off, Adviser sign-off.

**Architecture:** Three new choice columns on `al_remediationaction` carry the adviser's three answers; the adviser's existing free-text response is the "Remedial action". Owner, target date, adviser sign-off (completion) and supervisor sign-off (the `al_signoff` decision) already exist. "All remedial actions checked and approved?" is derived from the latest sign-off per action; "Regraded outcome" and its date come from `al_outcome`. The response guard locks the three new columns with the existing two once the action is Completed. Two portal templates render the form (the cross-case worklist keeps the write panels; the case page renders the per-case form read-only) and the app's remediation tab shows the same columns.

**Tech Stack:** Dataverse solution XML under `src/`; C# 7.3 / net462 plug-ins with xUnit; net8 registration tool; Power Pages Liquid templates with vanilla JS; React + TypeScript Code App with vitest.

**Spec:** The screenshot of the form supplied by the project owner on 2026-09-09 and their direction "this will be filled in by the adviser at the remediation action". Decision to be recorded as AD-095.

## Global Constraints

- Option values for the three new columns, from the free tail of the 1209107xx band (770-792 are used): `al_clientcontactrequired` Yes `120910793`, No `120910794`, Potentially `120910795`; `al_recheckrequired` Yes `120910796`, No `120910797`; `al_changesadvice` Yes `120910798`, No `120910799`. Schema names `al_ClientContactRequired`, `al_RecheckRequired`, `al_ChangesAdvice`; display names "Client contact required?", "Recheck required?", "Remedial actions change the advice?".
- Sign-off decision values: Approved `120910720`, Rejected `120910721`. Action status values: Open `120910600`, In progress `120910601`, Completed `120910602`.
- Plug-in code is C# 7.3 on net462: no `is not`, no target-typed `new()`, no switch expressions, no nullable reference annotations.
- The three new columns are written only by the adviser through the portal Web API PATCH on `al_remediationaction`; the allowlist site setting `Webapi/al_remediationaction/fields` (id `a1000000-0000-4000-8000-0000000000a6`) must list them. Status and completion stay off the list.
- Template component ids: OT Remediation `a1000000-0000-4000-8000-000000000019`, OT Case Detail `a1000000-0000-4000-8000-000000000015`.
- Tests: plug-ins `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj` (537 passing before this plan); app `cd app && npx vitest run` (214 passing) and `npx eslint .` (0 errors, 9 pre-existing warnings) and `npm run build`.
- The registration tool needs `DOTNET_ROLL_FORWARD=Major`; one process at a time, 600000 ms timeout; write verbs take `--confirm <orgUrl>` as their last two arguments.
- Do not edit files under `app/src/generated/` or `app/.power/`: they are regenerated from Dataverse. Type the new columns locally.
- Commit only the files each task names. The tree carries unrelated uncommitted work; `git add` explicit paths only. Commit messages end with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.

---

### Task 1: Tool verbs `addchoicecolumn` and `setstepfilter`

**Files:**
- Modify: `plugins/OutcomeTesting.Registration/Program.cs` (dispatch chain near the `addmemocolumn` branch at line ~237; functions next to `AddMemoColumn` at line ~1729)

**Interfaces:**
- Produces: verb `addchoicecolumn <orgUrl> <entityLogicalName> <SchemaName> <displayName> <value:label;value:label...> [<description>] --confirm <orgUrl>` creating a local-option-set picklist column in solution `SolutionUniqueName`, reading it back; verb `setstepfilter <orgUrl> "<step name>" <comma-separated attributes> --confirm <orgUrl>` updating `sdkmessageprocessingstep.filteringattributes` for the step with that exact name.
- Consumes: `Connect`, `ConfirmedFor(a, orgUrl)`, `SolutionUniqueName`, `NotificationTable.Text(string)` (all existing in Program.cs).

- [ ] **Step 1: Add the two dispatch branches**

Directly after the `addmemocolumn` branch (`if (args.Length >= 2 && args[0].Equals("addmemocolumn", ...)) { return AddMemoColumn(args); }`), add:

```csharp
if (args.Length >= 2 && args[0].Equals("addchoicecolumn", StringComparison.OrdinalIgnoreCase))
{
    return AddChoiceColumn(args);
}

if (args.Length >= 2 && args[0].Equals("setstepfilter", StringComparison.OrdinalIgnoreCase))
{
    return SetStepFilter(args);
}
```

- [ ] **Step 2: Add `AddChoiceColumn` directly after `AddMemoColumn`**

```csharp
// A picklist column with its own option set (AD-095). Options are given as
// "value:label;value:label"; values are the project's own 1209107xx band, so a typo here
// collides with nothing by accident but is still read back before being trusted.
int AddChoiceColumn(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 6 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This writes metadata to a live environment. Re-run as: addchoicecolumn <orgUrl> " +
            "<entityLogicalName> <SchemaName> <displayName> <value:label;value:label...> [<description>] --confirm <orgUrl>");
        return 1;
    }

    var entity = a[2].Trim();
    var schemaName = a[3].Trim();
    var displayName = a[4];
    var optionsArg = a[5];
    var description = a.Length > 6 && !a[6].StartsWith("--", StringComparison.Ordinal) ? a[6] : string.Empty;
    var logicalName = schemaName.ToLowerInvariant();

    var options = new List<OptionMetadata>();
    foreach (var pair in optionsArg.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = pair.Split(new[] { ':' }, 2);
        int value;
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out value) || string.IsNullOrWhiteSpace(parts[1]))
        {
            Console.Error.WriteLine($"Option '{pair}' is not value:label.");
            return 1;
        }

        options.Add(new OptionMetadata(NotificationTable.Text(parts[1].Trim()), value));
    }

    if (options.Count == 0)
    {
        Console.Error.WriteLine("At least one option is required.");
        return 1;
    }

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

    var optionSet = new OptionSetMetadata
    {
        IsGlobal = false,
        OptionSetType = OptionSetType.Picklist,
        DisplayName = NotificationTable.Text(displayName),
    };
    foreach (var option in options)
    {
        optionSet.Options.Add(option);
    }

    svc.Execute(new CreateAttributeRequest
    {
        SolutionUniqueName = SolutionUniqueName,
        EntityName = entity,
        Attribute = new PicklistAttributeMetadata
        {
            SchemaName = schemaName,
            LogicalName = logicalName,
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            DisplayName = NotificationTable.Text(displayName),
            Description = NotificationTable.Text(description),
            OptionSet = optionSet,
        },
    });

    var after = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
    {
        LogicalName = entity,
        EntityFilters = EntityFilters.Attributes,
    });

    var created = after.EntityMetadata.Attributes.FirstOrDefault(x =>
        string.Equals(x.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase)) as PicklistAttributeMetadata;

    if (created == null)
    {
        Console.Error.WriteLine($"'{logicalName}' was not found on '{entity}' after the create returned. Investigate before relying on it.");
        return 1;
    }

    var readBack = created.OptionSet.Options
        .Select(o => $"{o.Value}={o.Label?.UserLocalizedLabel?.Label}")
        .ToList();
    Console.WriteLine($"{entity}.{logicalName} created with options {string.Join(", ", readBack)}.");
    return 0;
}

// Updates the filtering attributes of an existing step by its exact name (AD-095): a guard
// registered on two columns does not fire for a write that carries only a third, so a new
// guarded column is a change to the step, not only to the plug-in.
int SetStepFilter(string[] a)
{
    var orgUrl = a[1];
    if (a.Length < 4 || !ConfirmedFor(a, orgUrl))
    {
        Console.Error.WriteLine(
            "This changes a registered step. Re-run as: setstepfilter <orgUrl> \"<step name>\" <attr,attr,...> --confirm <orgUrl>");
        return 1;
    }

    var stepName = a[2];
    var attributes = a[3].Trim();

    using var svc = Connect(orgUrl);

    var query = new QueryExpression("sdkmessageprocessingstep")
    {
        ColumnSet = new ColumnSet("name", "filteringattributes"),
    };
    query.Criteria.AddCondition("name", ConditionOperator.Equal, stepName);
    var found = svc.RetrieveMultiple(query).Entities;
    if (found.Count != 1)
    {
        Console.Error.WriteLine($"Expected one step named '{stepName}', found {found.Count}.");
        return 1;
    }

    var before = found[0].GetAttributeValue<string>("filteringattributes");
    svc.Update(new Entity("sdkmessageprocessingstep", found[0].Id)
    {
        ["filteringattributes"] = attributes,
    });

    var after = svc.Retrieve("sdkmessageprocessingstep", found[0].Id, new ColumnSet("filteringattributes"))
        .GetAttributeValue<string>("filteringattributes");
    Console.WriteLine($"'{stepName}': filteringattributes '{before}' -> '{after}'.");
    return string.Equals(after, attributes, StringComparison.Ordinal) ? 0 : 2;
}
```

If `OptionMetadata`, `OptionSetMetadata`, `OptionSetType` or `PicklistAttributeMetadata` are unresolved, add `using Microsoft.Xrm.Sdk.Metadata;` at the top of Program.cs (it is almost certainly present, since `MemoAttributeMetadata` is used).

- [ ] **Step 3: Build**

Run: `DOTNET_ROLL_FORWARD=Major dotnet build plugins/OutcomeTesting.Registration/OutcomeTesting.Registration.csproj`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Dry-run the guards**

Run: `DOTNET_ROLL_FORWARD=Major dotnet plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll addchoicecolumn https://org0b075da8.crm11.dynamics.com al_remediationaction al_ClientContactRequired "Client contact required?" "120910793:Yes;120910794:No;120910795:Potentially"`
Expected: the usage line (no `--confirm`), exit code 1, nothing written. Same for `setstepfilter https://org0b075da8.crm11.dynamics.com "x" "y"`.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Registration/Program.cs
git commit -m "feat(registration): addchoicecolumn and setstepfilter verbs (AD-095)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Solution XML and the Web API allowlist

**Files:**
- Modify: `src/Entities/al_RemediationAction/Entity.xml` (three `<attribute>` blocks appended inside `<attributes>`, after the `al_ActionStatus` block)
- Modify: `powerpages/outcome-testing---outcometesting/sitesetting.yml` (the `Webapi/al_remediationaction/fields` entry, lines ~120-123)

**Interfaces:**
- Produces: the columns and their option sets as the solution knows them, so the next solution import carries them; the allowlist value `al_adviserresponse,al_evidencereference,al_completerequested,al_clientcontactrequired,al_recheckrequired,al_changesadvice`.

- [ ] **Step 1: Add the three attributes to Entity.xml**

Insert after the closing `</attribute>` of the `al_ActionStatus` block (the block that starts `<attribute PhysicalName="al_ActionStatus">`). Each block mirrors that one with `RequiredLevel` `none`, no `AppDefaultValue`, `DisplayMask` `ValidForAdvancedFind|ValidForForm|ValidForGrid`:

```xml
        <attribute PhysicalName="al_ClientContactRequired">
          <Type>picklist</Type>
          <Name>al_clientcontactrequired</Name>
          <LogicalName>al_clientcontactrequired</LogicalName>
          <RequiredLevel>none</RequiredLevel>
          <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
          <ImeMode>auto</ImeMode>
          <ValidForUpdateApi>1</ValidForUpdateApi>
          <ValidForReadApi>1</ValidForReadApi>
          <ValidForCreateApi>1</ValidForCreateApi>
          <IsCustomField>1</IsCustomField>
          <IsAuditEnabled>1</IsAuditEnabled>
          <IsSecured>0</IsSecured>
          <IntroducedVersion>1.0.0.0</IntroducedVersion>
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
          <optionset Name="al_remediationaction_clientcontactrequired">
            <OptionSetType>picklist</OptionSetType>
            <IntroducedVersion>1.0.0.0</IntroducedVersion>
            <IsCustomizable>1</IsCustomizable>
            <displaynames>
              <displayname description="Client contact required?" languagecode="1033" />
            </displaynames>
            <Descriptions>
              <Description description="Adviser's answer on the remediation form (AD-095)." languagecode="1033" />
            </Descriptions>
            <options>
              <option value="120910793" IsHidden="0">
                <labels>
                  <label description="Yes" languagecode="1033" />
                </labels>
              </option>
              <option value="120910794" IsHidden="0">
                <labels>
                  <label description="No" languagecode="1033" />
                </labels>
              </option>
              <option value="120910795" IsHidden="0">
                <labels>
                  <label description="Potentially" languagecode="1033" />
                </labels>
              </option>
            </options>
          </optionset>
          <displaynames>
            <displayname description="Client contact required?" languagecode="1033" />
          </displaynames>
          <Descriptions>
            <Description description="Whether the client must be contacted as part of remediation: Yes, No or Potentially. Answered by the adviser on the remediation form (AD-095); locked with the response once the action is Completed." languagecode="1033" />
          </Descriptions>
        </attribute>
        <attribute PhysicalName="al_RecheckRequired">
          <Type>picklist</Type>
          <Name>al_recheckrequired</Name>
          <LogicalName>al_recheckrequired</LogicalName>
          <RequiredLevel>none</RequiredLevel>
          <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
          <ImeMode>auto</ImeMode>
          <ValidForUpdateApi>1</ValidForUpdateApi>
          <ValidForReadApi>1</ValidForReadApi>
          <ValidForCreateApi>1</ValidForCreateApi>
          <IsCustomField>1</IsCustomField>
          <IsAuditEnabled>1</IsAuditEnabled>
          <IsSecured>0</IsSecured>
          <IntroducedVersion>1.0.0.0</IntroducedVersion>
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
          <optionset Name="al_remediationaction_recheckrequired">
            <OptionSetType>picklist</OptionSetType>
            <IntroducedVersion>1.0.0.0</IntroducedVersion>
            <IsCustomizable>1</IsCustomizable>
            <displaynames>
              <displayname description="Recheck required?" languagecode="1033" />
            </displaynames>
            <Descriptions>
              <Description description="Adviser's answer on the remediation form (AD-095)." languagecode="1033" />
            </Descriptions>
            <options>
              <option value="120910796" IsHidden="0">
                <labels>
                  <label description="Yes" languagecode="1033" />
                </labels>
              </option>
              <option value="120910797" IsHidden="0">
                <labels>
                  <label description="No" languagecode="1033" />
                </labels>
              </option>
            </options>
          </optionset>
          <displaynames>
            <displayname description="Recheck required?" languagecode="1033" />
          </displaynames>
          <Descriptions>
            <Description description="Whether the adviser believes the case needs a recheck after remediation. Answered on the remediation form (AD-095); the recheck itself is the T&amp;C Manager's decision at sign-off." languagecode="1033" />
          </Descriptions>
        </attribute>
        <attribute PhysicalName="al_ChangesAdvice">
          <Type>picklist</Type>
          <Name>al_changesadvice</Name>
          <LogicalName>al_changesadvice</LogicalName>
          <RequiredLevel>none</RequiredLevel>
          <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
          <ImeMode>auto</ImeMode>
          <ValidForUpdateApi>1</ValidForUpdateApi>
          <ValidForReadApi>1</ValidForReadApi>
          <ValidForCreateApi>1</ValidForCreateApi>
          <IsCustomField>1</IsCustomField>
          <IsAuditEnabled>1</IsAuditEnabled>
          <IsSecured>0</IsSecured>
          <IntroducedVersion>1.0.0.0</IntroducedVersion>
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
          <optionset Name="al_remediationaction_changesadvice">
            <OptionSetType>picklist</OptionSetType>
            <IntroducedVersion>1.0.0.0</IntroducedVersion>
            <IsCustomizable>1</IsCustomizable>
            <displaynames>
              <displayname description="Remedial actions change the advice?" languagecode="1033" />
            </displaynames>
            <Descriptions>
              <Description description="Adviser's answer on the remediation form (AD-095)." languagecode="1033" />
            </Descriptions>
            <options>
              <option value="120910798" IsHidden="0">
                <labels>
                  <label description="Yes" languagecode="1033" />
                </labels>
              </option>
              <option value="120910799" IsHidden="0">
                <labels>
                  <label description="No" languagecode="1033" />
                </labels>
              </option>
            </options>
          </optionset>
          <displaynames>
            <displayname description="Remedial actions change the advice?" languagecode="1033" />
          </displaynames>
          <Descriptions>
            <Description description="Whether the remedial actions change the advice given. Answered by the adviser on the remediation form (AD-095); locked with the response once the action is Completed." languagecode="1033" />
          </Descriptions>
        </attribute>
```

- [ ] **Step 2: Extend the allowlist**

In `sitesetting.yml`, on the entry whose `adx_name` is `Webapi/al_remediationaction/fields`, change `adx_value` to:

```yaml
  adx_value: al_adviserresponse,al_evidencereference,al_completerequested,al_clientcontactrequired,al_recheckrequired,al_changesadvice
```

and change its `adx_description` to:

```yaml
- adx_description: "Allowlist of columns the browser may write on al_remediationaction. The adviser's own words, the Intelligent Office reference (PP-14), the completion trigger, and the three remediation-form answers (AD-095): client contact required, recheck required, changes the advice. Status and completed-on stay off the list so the plug-in remains the sole authority over the transition."
```

- [ ] **Step 3: Check the XML is well-formed**

Run: `python3 -c "import xml.dom.minidom,sys; xml.dom.minidom.parse('src/Entities/al_RemediationAction/Entity.xml'); print('ok')"`
Expected: `ok`. Then `grep -c "al_remediationaction_clientcontactrequired\|al_remediationaction_recheckrequired\|al_remediationaction_changesadvice" src/Entities/al_RemediationAction/Entity.xml` → `3`.

- [ ] **Step 4: Commit**

```bash
git add src/Entities/al_RemediationAction/Entity.xml powerpages/outcome-testing---outcometesting/sitesetting.yml
git commit -m "feat(schema): three remediation-form answers on al_remediationaction, and their Web API allowlist (AD-095)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: The response guard locks the three answers too

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/RemediationResponseGuardPlugin.cs` (`ResponseColumns`, `Refusal`, `Normalise`)
- Test: `plugins/OutcomeTesting.Plugins.Tests/RemediationResponseGuardTests.cs`

**Interfaces:**
- Produces: `RemediationResponseGuardPlugin.ResponseColumns` = `{ "al_adviserresponse", "al_evidencereference", "al_clientcontactrequired", "al_recheckrequired", "al_changesadvice" }`; `Refusal(Entity current, Entity update)` compares picklist columns by option value and text columns by normalised text.

- [ ] **Step 1: Write the failing tests**

Add inside the test class, after the existing facts:

```csharp
        private static Entity ActionWithAnswers(int status)
        {
            var action = Action(status);
            action["al_clientcontactrequired"] = new OptionSetValue(120910794);
            action["al_recheckrequired"] = new OptionSetValue(120910797);
            action["al_changesadvice"] = new OptionSetValue(120910799);
            return action;
        }

        [Fact]
        public void The_form_answers_are_editable_until_the_action_is_submitted()
        {
            var update = new Entity("al_remediationaction", ActionId);
            update["al_clientcontactrequired"] = new OptionSetValue(120910795);

            Assert.Null(RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusInProgress), update));
        }

        [Fact]
        public void A_submitted_form_answer_cannot_be_changed()
        {
            // AD-095: the three answers travel with the response and lock with it, otherwise
            // the T&C Manager attests to a form the adviser can still rewrite underneath.
            var update = new Entity("al_remediationaction", ActionId);
            update["al_recheckrequired"] = new OptionSetValue(120910796);

            var refusal = RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusCompleted), update);

            Assert.NotNull(refusal);
            Assert.StartsWith("CONFLICT:", refusal);
        }

        [Fact]
        public void Re_sending_the_same_answer_after_submission_is_not_a_change()
        {
            var update = new Entity("al_remediationaction", ActionId);
            update["al_changesadvice"] = new OptionSetValue(120910799);

            Assert.Null(RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusCompleted), update));
        }

        [Fact]
        public void Clearing_a_submitted_answer_is_a_change()
        {
            var update = new Entity("al_remediationaction", ActionId);
            update["al_clientcontactrequired"] = null;

            Assert.NotNull(RemediationResponseGuardPlugin.Refusal(ActionWithAnswers(Remediation.StatusCompleted), update));
        }
```

- [ ] **Step 2: Run to see them fail**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~RemediationResponseGuardTests"`
Expected: `A_submitted_form_answer_cannot_be_changed` and `Clearing_a_submitted_answer_is_a_change` fail (the guard ignores columns it does not list; the first returns null, the second returns null). The other two pass already; they pin the behaviour once the columns are guarded.

- [ ] **Step 3: Guard the columns and compare by type**

Replace the `ResponseColumns` declaration with:

```csharp
        /// <summary>
        /// The columns that carry the adviser's submission: the remedial action text, the
        /// Intelligent Office reference, and the three remediation-form answers (AD-095).
        /// The registered step's filtering attributes list the same five.
        /// </summary>
        public static readonly string[] ResponseColumns =
        {
            "al_adviserresponse",
            "al_evidencereference",
            "al_clientcontactrequired",
            "al_recheckrequired",
            "al_changesadvice",
        };
```

In `Refusal`, replace the two lines that read `incoming` and `stored` with:

```csharp
                var incoming = Describe(update.Contains(column) ? update[column] : null);
                var stored = Describe(current.Contains(column) ? current[column] : null);
```

and add next to `Normalise`:

```csharp
        /// <summary>
        /// One comparable form for a text or choice column: the option value for a choice,
        /// the normalised text otherwise, and the empty string for nothing.
        /// </summary>
        private static string Describe(object value)
        {
            var option = value as OptionSetValue;
            if (option != null)
            {
                return option.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return Normalise(value as string);
        }
```

Leave `Normalise(string)` as it is (it must return `string.Empty` for null; check and keep).

- [ ] **Step 4: Run the guard tests, then the whole suite**

Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj --filter "FullyQualifiedName~RemediationResponseGuardTests"` → all pass.
Run: `DOTNET_ROLL_FORWARD=Major dotnet test plugins/OutcomeTesting.Plugins.Tests/OutcomeTesting.Plugins.Tests.csproj` → 0 failed; note the count (expected 541).

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/RemediationResponseGuardPlugin.cs plugins/OutcomeTesting.Plugins.Tests/RemediationResponseGuardTests.cs
git commit -m "feat(remediation): the response guard locks the three form answers with the response (AD-095)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: The portal worklist becomes the form

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html`

**Interfaces:**
- Consumes: the three columns from Task 2; the allowlist from Task 2 (deployed in Task 7).
- Produces: the table No. | Case | Issue / fail reason | Remedial action | Owner | Target date | Status | Age | Sign-off; the adviser panel with the form; the sign-off panel showing the form.

- [ ] **Step 1: Fetch the new columns and the sign-offs**

In the `actions` FetchXML add, after `<attribute name="al_assignedcontactid" />`:

```xml
    <attribute name="al_clientcontactrequired" />
    <attribute name="al_recheckrequired" />
    <attribute name="al_changesadvice" />
```

Directly after `{% assign rows = actions.results.entities %}` add a second fetch for the page's sign-offs and flatten them, mirroring the `selected_reason_pairs` trick in OT Review Detail:

```liquid
{% comment %}
  The Sign-off column (AD-095): the latest T&C decision per action, or the adviser's
  completion where none has been made yet. Fetched for the page's actions only and
  flattened to a delimited key list, so the per-row test is a string match rather than a
  nested loop over every sign-off for every action.
{% endcomment %}
{% if rows.size > 0 %}
  {% fetchxml signoffs %}
  <fetch>
    <entity name="al_signoff">
      <attribute name="al_remediationactionid" />
      <attribute name="al_signoffdecision" />
      <attribute name="al_signedoffon" />
      <filter type="and">
        <condition attribute="al_remediationactionid" operator="in">
          {% for a in rows %}<value>{{ a.al_remediationactionid }}</value>{% endfor %}
        </condition>
      </filter>
      <order attribute="al_signedoffon" descending="true" />
    </entity>
  </fetch>
  {% endfetchxml %}
{% endif %}
```

- [ ] **Step 2: Replace the table header and row cells**

Replace the `<thead>` with:

```html
        <thead>
          <tr>
            <th scope="col">No.</th>
            <th scope="col">Case</th>
            <th scope="col">Issue / fail reason</th>
            <th scope="col">Remedial action</th>
            <th scope="col">Owner</th>
            <th scope="col">Target date</th>
            <th scope="col">Status</th>
            <th scope="col">Age</th>
            <th scope="col">Sign-off</th>
          </tr>
        </thead>
```

Replace the row's cells (from the first `<td>` holding the case link through the `Completed` cell) so the row reads, keeping the existing case-link and age markup verbatim where shown:

```liquid
            <tr>
              <td>{{ forloop.index | plus: page_offset }}</td>
              <td>
                {% assign cid = a['case.al_outcomecaseid'] %}
                {% if cid %}
                  <a href="/case-details?id={{ cid }}">
                    {{ a['case.al_casereference'] | default: 'Untitled case' | escape }}
                  </a>
                {% else %}
                  {{ a['case.al_casereference'] | default: '—' | escape }}
                {% endif %}
              </td>
              <td>{{ a.al_description | escape }}</td>
              <td>{{ a.al_adviserresponse | default: '—' | escape | newline_to_br }}</td>
              <td>{{ a.al_assignedcontactid.name | default: a['case.al_advisername'] | default: 'Unassigned' | escape }}</td>
              <td>{{ a.al_duedate | date: 'dd MMM yyyy' | default: '—' }}</td>
              <td>{% include 'OT Status Badge' status: a.al_actionstatus.label %}</td>
              <td>
                {% assign clock_start = a.al_clockstartedon | default: a.createdon %}
                <span class="ot-age"
                      data-ot-age
                      data-opened="{{ clock_start | date: 'yyyy-MM-dd' }}"
                      data-created="{{ a.createdon | date: 'yyyy-MM-dd' }}"
                      data-completed="{{ a.al_completedon | date: 'yyyy-MM-dd' }}">—</span>
              </td>
              <td>
                {% assign latest = null %}
                {% if signoffs %}{% for s in signoffs.results.entities %}{% if latest == null and s.al_remediationactionid.id == a.al_remediationactionid %}{% assign latest = s %}{% endif %}{% endfor %}{% endif %}
                {% if latest %}
                  {{ latest.al_signoffdecision.label | escape }} {{ latest.al_signedoffon | date: 'dd MMM yyyy' }}
                {% elsif a.al_completedon %}
                  Adviser completed {{ a.al_completedon | date: 'dd MMM yyyy' }}; awaiting supervisor
                {% else %}
                  —
                {% endif %}
              </td>
            </tr>
```

The No. cell numbers across pages: add `{% assign page_offset = page_number | minus: 1 | times: page_size %}` directly after the existing `{% if page_number < 1 %}{% assign page_number = 1 %}{% endif %}` line near the top of the template.

Both response-row `<td colspan="9">` cells keep `colspan="9"` (the column count is unchanged at nine).

- [ ] **Step 3: The adviser's panel becomes the form**

Replace the adviser panel's two fields (the `What did you do?` textarea block and the `Intelligent Office reference` block) with:

```liquid
                    <div class="ot-response__field">
                      <label for="ot-response-{{ a.al_remediationactionid }}">Remedial action</label>
                      <textarea id="ot-response-{{ a.al_remediationactionid }}"
                                rows="4"
                                data-ot-response-text>{{ a.al_adviserresponse | escape }}</textarea>
                      <p class="ot-response__hint">What you did, or will do, to put this right.</p>
                    </div>

                    <div class="ot-response__grid">
                      <div class="ot-response__field">
                        <span class="ot-response__label">Owner</span>
                        <p>{{ a.al_assignedcontactid.name | default: a['case.al_advisername'] | escape }}</p>
                      </div>
                      <div class="ot-response__field">
                        <span class="ot-response__label">Target date</span>
                        <p>{{ a.al_duedate | date: 'dd MMM yyyy' | default: '—' }}</p>
                      </div>
                    </div>

                    <fieldset class="ot-response__field">
                      <legend>Client contact required?</legend>
                      <label><input type="radio" name="ot-cc-{{ a.al_remediationactionid }}" value="120910793" data-ot-q="clientcontact"{% if a.al_clientcontactrequired.value == 120910793 %} checked{% endif %} /> Yes</label>
                      <label><input type="radio" name="ot-cc-{{ a.al_remediationactionid }}" value="120910794" data-ot-q="clientcontact"{% if a.al_clientcontactrequired.value == 120910794 %} checked{% endif %} /> No</label>
                      <label><input type="radio" name="ot-cc-{{ a.al_remediationactionid }}" value="120910795" data-ot-q="clientcontact"{% if a.al_clientcontactrequired.value == 120910795 %} checked{% endif %} /> Potentially</label>
                    </fieldset>

                    <fieldset class="ot-response__field">
                      <legend>Recheck required?</legend>
                      <label><input type="radio" name="ot-rc-{{ a.al_remediationactionid }}" value="120910796" data-ot-q="recheck"{% if a.al_recheckrequired.value == 120910796 %} checked{% endif %} /> Yes</label>
                      <label><input type="radio" name="ot-rc-{{ a.al_remediationactionid }}" value="120910797" data-ot-q="recheck"{% if a.al_recheckrequired.value == 120910797 %} checked{% endif %} /> No</label>
                    </fieldset>

                    <fieldset class="ot-response__field">
                      <legend>Do the remedial actions change the advice?</legend>
                      <label><input type="radio" name="ot-ca-{{ a.al_remediationactionid }}" value="120910798" data-ot-q="changesadvice"{% if a.al_changesadvice.value == 120910798 %} checked{% endif %} /> Yes</label>
                      <label><input type="radio" name="ot-ca-{{ a.al_remediationactionid }}" value="120910799" data-ot-q="changesadvice"{% if a.al_changesadvice.value == 120910799 %} checked{% endif %} /> No</label>
                    </fieldset>

                    <div class="ot-response__field">
                      <label for="ot-evidence-{{ a.al_remediationactionid }}">
                        Intelligent Office reference
                      </label>
                      <input type="text"
                             id="ot-evidence-{{ a.al_remediationactionid }}"
                             value="{{ a.al_evidencereference | escape }}"
                             data-ot-response-evidence />
                      <p class="ot-response__hint">
                        The remedial task you completed in Intelligent Office. Nothing is uploaded
                        here and no file is stored by this site.
                      </p>
                    </div>
```

Change the "Save and mark complete" button text to `Save and sign off as adviser`, and the summary text `Record your response` to `Complete the remediation form`.

- [ ] **Step 4: The sign-off panel shows the form**

In the T&C panel, replace the `{% if a.al_adviserresponse %} ... {% endif %}` block with:

```liquid
                    <div class="ot-response__field">
                      <span class="ot-response__label">Remedial action</span>
                      <p>{{ a.al_adviserresponse | default: '—' | escape | newline_to_br }}</p>
                      <p class="ot-response__hint">
                        Client contact required: {{ a.al_clientcontactrequired.label | default: '—' }} ·
                        Recheck required: {{ a.al_recheckrequired.label | default: '—' }} ·
                        Changes the advice: {{ a.al_changesadvice.label | default: '—' }}
                        {% if a.al_evidencereference %} · Intelligent Office reference: {{ a.al_evidencereference | escape }}{% endif %}
                      </p>
                    </div>
```

Change the `Decision` label to `All remedial actions checked and approved?` and leave the two options as Approved / Rejected. Change the button text to `Record supervisor sign-off`.

- [ ] **Step 5: The response script sends the three answers**

In the adviser script's `wire(panel)`, after `var evidence = ...`, add:

```javascript
      function answer(name) {
        var picked = panel.querySelector('[data-ot-q="' + name + '"]:checked');
        return picked ? Number(picked.value) : null;
      }
```

and replace the payload with:

```javascript
        var payload = {
          al_adviserresponse: response.value,
          al_evidencereference: evidence.value,
          al_clientcontactrequired: answer('clientcontact'),
          al_recheckrequired: answer('recheck'),
          al_changesadvice: answer('changesadvice')
        };
```

Add the client-side check before the round trip, next to the existing "Record what you did" check:

```javascript
        if (markComplete && (answer('clientcontact') === null || answer('recheck') === null || answer('changesadvice') === null)) {
          show('Answer all three questions on the form before signing off.', 'error');
          return;
        }
```

The success message for `markComplete` becomes `'Signed off as adviser. Reloading...'`.

- [ ] **Step 6: A small style for the grid and fieldsets**

If `outcome-testing.css` (find it with `grep -rl "ot-response__field" powerpages/`) has no rule for `.ot-response__grid`, add to that file:

```css
.ot-response__grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0 1.5rem; }
.ot-response__field fieldset, fieldset.ot-response__field { border: 0; padding: 0; margin: 0 0 1rem; }
fieldset.ot-response__field legend { font-weight: 600; margin-bottom: .25rem; }
fieldset.ot-response__field label { margin-right: 1.25rem; }
```

and name that file in the commit. If the css file is served as a web file that needs its own push, note that in the report; the deploy task handles it.

- [ ] **Step 7: Check the Liquid**

Run: `python3 -c "
import re,sys
s=open('powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html',encoding='utf-8').read()
for tag in ['if','for','fetchxml','comment']:
    o=len(re.findall(r'{%%-?\s*%s\b'%tag,s)); c=len(re.findall(r'{%%-?\s*end%s\b'%tag,s)); print(tag,o,c); assert o==c, tag
print('balanced')"`
Expected: `balanced`.

- [ ] **Step 8: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html
git commit -m "feat(portal): the remediation worklist renders the remedial form and the adviser fills it (AD-095)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

(add the css file to the `git add` if Step 6 changed one).

---

### Task 5: The case page renders the per-case form

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html` (the `actions` fetch at ~line 73 and the Remediation section at ~line 211)

**Interfaces:**
- Consumes: the three columns; `al_signoff` and `al_outcome` rows of the case.
- Produces: the read-only per-case form matching the screenshot.

- [ ] **Step 1: Extend the fetches**

In the `actions` FetchXML add after `<attribute name="al_completedon" />`:

```xml
        <attribute name="al_adviserresponse" />
        <attribute name="al_evidencereference" />
        <attribute name="al_assignedcontactid" />
        <attribute name="al_clientcontactrequired" />
        <attribute name="al_recheckrequired" />
        <attribute name="al_changesadvice" />
        <attribute name="createdon" />
```

Directly after that fetch's `{% endfetchxml %}` add:

```liquid
    {% fetchxml case_signoffs %}
    <fetch>
      <entity name="al_signoff">
        <attribute name="al_remediationactionid" />
        <attribute name="al_signoffdecision" />
        <attribute name="al_signedoffon" />
        <attribute name="ownerid" />
        <filter type="and">
          <condition attribute="al_outcomecaseid" operator="eq" value="{{ case_id | xml_escape }}" />
        </filter>
        <order attribute="al_signedoffon" descending="true" />
      </entity>
    </fetch>
    {% endfetchxml %}

    {% fetchxml case_outcomes %}
    <fetch top="1">
      <entity name="al_outcome">
        <attribute name="al_initialoutcome" />
        <attribute name="al_finaloutcome" />
        <attribute name="al_finalisedon" />
        <attribute name="al_regradedon" />
        <filter type="and">
          <condition attribute="al_outcomecaseid" operator="eq" value="{{ case_id | xml_escape }}" />
        </filter>
        <order attribute="createdon" descending="true" />
      </entity>
    </fetch>
    {% endfetchxml %}
```

- [ ] **Step 2: Replace the Remediation section body**

Replace everything between `<h2 class="ot-card__title" id="ot-case-remediation">Remediation</h2>` and the section's closing `</section>` with:

```liquid
      {% assign case_actions = actions.results.entities %}
      {% if case_actions.size > 0 %}
        {% assign all_approved = true %}
        {% assign any_rejected = false %}
        {% assign latest_supervisor = null %}
        {% assign latest_adviser = null %}
        {% assign form_source = null %}
        <div class="ot-table-wrap">
          <table class="ot-table ot-remedial-form">
            <caption>Remediation and escalation</caption>
            <thead>
              <tr>
                <th scope="col">No.</th>
                <th scope="col">Issue / fail reason</th>
                <th scope="col">Remedial action</th>
                <th scope="col">Owner</th>
                <th scope="col">Target date</th>
                <th scope="col">Sign-off</th>
              </tr>
            </thead>
            <tbody>
              {% for a in case_actions %}
                {% assign latest = null %}
                {% for s in case_signoffs.results.entities %}{% if latest == null and s.al_remediationactionid.id == a.al_remediationactionid %}{% assign latest = s %}{% endif %}{% endfor %}
                {% if latest %}
                  {% if latest.al_signoffdecision.value == 120910721 %}{% assign any_rejected = true %}{% assign all_approved = false %}{% endif %}
                  {% if latest_supervisor == null %}{% assign latest_supervisor = latest %}{% endif %}
                {% else %}
                  {% assign all_approved = false %}
                {% endif %}
                {% if a.al_completedon and latest_adviser == null %}{% assign latest_adviser = a %}{% endif %}
                {% if form_source == null and a.al_clientcontactrequired %}{% assign form_source = a %}{% endif %}
                <tr>
                  <td>{{ forloop.index }}</td>
                  <td>{{ a.al_description | escape }}</td>
                  <td>{{ a.al_adviserresponse | default: '—' | escape | newline_to_br }}</td>
                  <td>{{ a.al_assignedcontactid.name | default: 'Unassigned' | escape }}</td>
                  <td>{{ a.al_duedate | date: 'dd MMM yyyy' | default: '—' }}</td>
                  <td>
                    {% if latest %}
                      {{ latest.al_signoffdecision.label | escape }} {{ latest.al_signedoffon | date: 'dd MMM yyyy' }}
                    {% elsif a.al_completedon %}
                      Adviser completed {{ a.al_completedon | date: 'dd MMM yyyy' }}
                    {% else %}
                      {% include 'OT Status Badge' status: a.al_actionstatus.label %}
                    {% endif %}
                  </td>
                </tr>
              {% endfor %}
            </tbody>
          </table>
        </div>

        {% if form_source == null %}{% assign form_source = case_actions[0] %}{% endif %}
        {% assign outcome = case_outcomes.results.entities[0] %}
        <dl class="ot-remedial-form__block">
          <div><dt>Client contact required?</dt><dd>{{ form_source.al_clientcontactrequired.label | default: '—' }}</dd></div>
          <div><dt>Recheck required?</dt><dd>{{ form_source.al_recheckrequired.label | default: '—' }}</dd></div>
          <div><dt>Do the remedial actions change the advice?</dt><dd>{{ form_source.al_changesadvice.label | default: '—' }}</dd></div>
          <div><dt>All remedial actions checked and approved?</dt><dd>{% if all_approved %}Yes{% elsif any_rejected %}No{% else %}—{% endif %}</dd></div>
          <div><dt>Regraded outcome</dt><dd>{% if outcome and outcome.al_finaloutcome %}{{ outcome.al_finaloutcome.label | escape }}{% else %}—{% endif %}</dd></div>
          <div><dt>Date</dt><dd>{% if outcome and outcome.al_regradedon %}{{ outcome.al_regradedon | date: 'dd MMM yyyy' }}{% elsif outcome and outcome.al_finalisedon %}{{ outcome.al_finalisedon | date: 'dd MMM yyyy' }}{% else %}—{% endif %}</dd></div>
          <div><dt>Supervisor sign-off</dt><dd>{% if latest_supervisor %}{{ latest_supervisor.al_signoffdecision.label | escape }}, {{ latest_supervisor.ownerid.name | escape }}, {{ latest_supervisor.al_signedoffon | date: 'dd MMM yyyy' }}{% else %}—{% endif %}</dd></div>
          <div><dt>Adviser sign-off</dt><dd>{% if latest_adviser %}{{ latest_adviser.al_assignedcontactid.name | escape }}, {{ latest_adviser.al_completedon | date: 'dd MMM yyyy' }}{% if latest_adviser.al_evidencereference %} (IO {{ latest_adviser.al_evidencereference | escape }}){% endif %}{% else %}—{% endif %}</dd></div>
        </dl>
        <p class="ot-response__hint">
          The three answers are the adviser's, recorded on the remediation action{% if case_actions.size > 1 %} (the first action carrying an answer){% endif %}.
          "All remedial actions checked and approved" reads Yes when every action's latest supervisor decision is Approved.
        </p>
      {% else %}
        {% include 'OT Empty State' title: 'No remediation' message: 'A pass outcome requires no remediation. Actions appear here for any non-pass outcome.' %}
      {% endif %}
```

- [ ] **Step 3: Style the block**

In the same css file Task 4 used (find with `grep -rl "ot-response__field" powerpages/`), add:

```css
.ot-remedial-form__block { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: .5rem 1.5rem; margin: 1rem 0 0; }
.ot-remedial-form__block div { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 0 .75rem; padding: .35rem 0; border-bottom: 1px solid var(--ot-border, #d9dee5); }
.ot-remedial-form__block dt { font-weight: 600; }
.ot-remedial-form__block dd { margin: 0; }
```

- [ ] **Step 4: Check the Liquid**

Run the same balance check as Task 4 Step 7 against `OT-Case-Detail.webtemplate.source.html`. Expected: `balanced`.

- [ ] **Step 5: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html <the css file>
git commit -m "feat(portal): the case page renders the per-case remedial form (AD-095)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: The app's remediation tab shows the form's columns

**Files:**
- Create: `app/src/features/remediation/remediationMapping.ts`
- Create: `app/src/features/remediation/remediationMapping.test.ts`
- Modify: `app/src/features/remediation/useRemediation.ts` (move `toAction`, `toOutcome`, `toSignoff`, `date`, `text` into the mapping module and import them; extend `RemediationActionRow`)
- Modify: `app/src/features/remediation/RemediationPage.tsx` (the actions table)

**Interfaces:**
- Produces: `RemediationActionRow` gains `remedialAction: string | null`, `evidenceReference: string | null`, `clientContactRequired: string | null`, `recheckRequired: string | null`, `changesAdvice: string | null`, `assignedTo: string | null`. Exported `toAction(record)`, `toOutcome(record)`, `toSignoff(record)`, `date`, `text`, and the three label maps `CLIENT_CONTACT_REQUIRED`, `RECHECK_REQUIRED`, `CHANGES_ADVICE`.

- [ ] **Step 1: Write the failing test**

```typescript
import { describe, expect, it } from 'vitest';
import type { Al_remediationactions } from '../../generated/models/Al_remediationactionsModel';
import { toAction } from './remediationMapping';

/**
 * The remedial form's per-action columns (AD-095). The three answers are not in the
 * generated model, which is regenerated from Dataverse; they are read off the record by
 * name and labelled here, and the SDK returns an unanswered one as null.
 */
function action(fields: Record<string, unknown>): Al_remediationactions {
  return {
    al_remediationactionid: 'a1a1a1a1-0000-4000-8000-000000000001',
    al_remediationactioncode: 'RA-1',
    al_name: 'RA-1',
    al_description: 'Suitability report missing',
    al_actionstatus: 120910601,
    al_actionstatusname: 'In progress',
    ...fields,
  } as unknown as Al_remediationactions;
}

describe('toAction', () => {
  it('reads the remedial action text and the three answers by label', () => {
    const row = toAction(
      action({
        al_adviserresponse: 'Reissued the report',
        al_evidencereference: 'IO-77',
        al_clientcontactrequired: 120910795,
        al_recheckrequired: 120910796,
        al_changesadvice: 120910799,
        al_assignedcontactidname: 'Sims Rad',
      }),
    );
    expect(row.remedialAction).toBe('Reissued the report');
    expect(row.evidenceReference).toBe('IO-77');
    expect(row.clientContactRequired).toBe('Potentially');
    expect(row.recheckRequired).toBe('Yes');
    expect(row.changesAdvice).toBe('No');
    expect(row.assignedTo).toBe('Sims Rad');
  });

  it('treats null answers as unanswered', () => {
    const row = toAction(
      action({
        al_adviserresponse: null,
        al_clientcontactrequired: null,
        al_recheckrequired: null,
        al_changesadvice: null,
      }),
    );
    expect(row.remedialAction).toBeNull();
    expect(row.clientContactRequired).toBeNull();
    expect(row.recheckRequired).toBeNull();
    expect(row.changesAdvice).toBeNull();
  });

  it('keeps the existing columns', () => {
    const row = toAction(action({ al_duedate: '2026-09-20T00:00:00Z' }));
    expect(row.description).toBe('Suitability report missing');
    expect(row.status).toBe('In progress');
    expect(row.dueOn).toBe('20 Sept 2026');
  });
});
```

- [ ] **Step 2: Run to see it fail**

Run: `cd app && npx vitest run src/features/remediation/remediationMapping.test.ts`
Expected: fails to resolve `./remediationMapping`.

- [ ] **Step 3: Create the mapping module**

Move `date`, `text`, `toAction`, `toOutcome`, `toSignoff` and the `RemediationActionRow`, `OutcomeRow`, `SignoffRow` interfaces out of `useRemediation.ts` into `remediationMapping.ts` (export them all), keeping their bodies, and extend `toAction`:

```typescript
/** Labels for the three remedial-form answers (AD-095); values from the 1209107xx band. */
export const CLIENT_CONTACT_REQUIRED: Record<number, string> = {
  120910793: 'Yes',
  120910794: 'No',
  120910795: 'Potentially',
};
export const RECHECK_REQUIRED: Record<number, string> = { 120910796: 'Yes', 120910797: 'No' };
export const CHANGES_ADVICE: Record<number, string> = { 120910798: 'Yes', 120910799: 'No' };

/** The SDK returns an empty choice as null, and the generated model does not know these columns yet. */
function choice(record: Al_remediationactions, attr: string, labels: Record<number, string>): string | null {
  const value = (record as unknown as Record<string, unknown>)[attr];
  return typeof value === 'number' ? (labels[value] ?? String(value)) : null;
}

export function toAction(record: Al_remediationactions): RemediationActionRow {
  const extra = record as unknown as Record<string, unknown>;
  return {
    id: record.al_remediationactionid,
    reference: text(record.al_name) ?? record.al_remediationactioncode,
    description: text(record.al_description) ?? '—',
    status:
      record.al_actionstatusname ??
      Al_remediationactionsal_actionstatus[record.al_actionstatus] ??
      '—',
    dueOn: date(record.al_duedate),
    completedOn: date(record.al_completedon),
    triggeredBy: text(record.al_reviewinstanceidname),
    owner: text(record.owneridname),
    assignedTo: text(extra.al_assignedcontactidname as string | undefined),
    remedialAction: text(extra.al_adviserresponse as string | undefined),
    evidenceReference: text(extra.al_evidencereference as string | undefined),
    clientContactRequired: choice(record, 'al_clientcontactrequired', CLIENT_CONTACT_REQUIRED),
    recheckRequired: choice(record, 'al_recheckrequired', RECHECK_REQUIRED),
    changesAdvice: choice(record, 'al_changesadvice', CHANGES_ADVICE),
    rowVersion: record.versionnumber != null ? String(record.versionnumber) : null,
  };
}
```

`text` must accept `string | null | undefined` (the SDK returns null); `date` likewise. The `RemediationActionRow` interface gains the six fields listed under Interfaces. `useRemediation.ts` then imports what it needs from `./remediationMapping` and re-exports the row types (`export type { RemediationActionRow, OutcomeRow, SignoffRow } from './remediationMapping';`) so `RemediationPage.tsx` keeps compiling.

- [ ] **Step 4: Render the form's columns**

In `RemediationPage.tsx`, replace the actions table header and cells with:

```tsx
      <thead>
        <tr>
          <th scope="col">No.</th>
          <th scope="col">Issue / fail reason</th>
          <th scope="col">Remedial action</th>
          <th scope="col">Owner</th>
          <th scope="col">Target date</th>
          <th scope="col">Status</th>
          <th scope="col">Client contact</th>
          <th scope="col">Recheck</th>
          <th scope="col">Changes advice</th>
          <th scope="col">Adviser sign-off</th>
          <th scope="col">
            <span className="remediation__sr-only">Complete</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {actions.map((action, index) => (
          <tr key={action.id}>
            <th scope="row">{index + 1}</th>
            <td>{action.description}</td>
            <td>
              {action.remedialAction ?? '—'}
              {action.evidenceReference ? (
                <span className="remediation__note"> IO {action.evidenceReference}</span>
              ) : null}
            </td>
            <td>{action.assignedTo ?? action.owner ?? 'Unassigned'}</td>
            <td>{action.dueOn ?? '—'}</td>
            <td>{action.status}</td>
            <td>{action.clientContactRequired ?? '—'}</td>
            <td>{action.recheckRequired ?? '—'}</td>
            <td>{action.changesAdvice ?? '—'}</td>
            <td>{action.completedOn ?? '—'}</td>
            <td>
              {/* the existing complete button cell, unchanged */}
            </td>
          </tr>
        ))}
      </tbody>
```

Keep the existing complete-button cell's JSX exactly as it is inside the last `<td>`. If a test or export elsewhere references `triggeredBy` or `owner` on the row, they still exist; nothing else changes.

- [ ] **Step 5: Run tests, lint, build**

Run: `cd app && npx vitest run` → 0 failed (expected 217). `npx eslint .` → 0 errors. `npm run build` → built.

- [ ] **Step 6: Commit**

```bash
git add app/src/features/remediation/remediationMapping.ts app/src/features/remediation/remediationMapping.test.ts app/src/features/remediation/useRemediation.ts app/src/features/remediation/RemediationPage.tsx
git commit -m "feat(app): the remediation tab shows the remedial form's columns (AD-095)

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Deploy to DEV and record

**Files:**
- Create: `docs/deployment/2026-09-09-remedial-form.md`
- Modify: `knowledge/decision-log.md` (append AD-095 after AD-094; leave uncommitted, per the standing ruling that the file carries unrelated uncommitted edits)

**Interfaces:**
- Consumes: every verb from Task 1; `pushassembly`, `pushwebtemplate`, `setsitesetting`, `fetch`; `npx pa app push` from `app/`.

- [ ] **Step 1: Create the three columns in DEV**

Three commands, one at a time (each with `DOTNET_ROLL_FORWARD=Major`, 600000 ms timeout, from the repo root, `T=plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.dll`, `U=https://org0b075da8.crm11.dynamics.com`):

```
dotnet $T addchoicecolumn $U al_remediationaction al_ClientContactRequired "Client contact required?" "120910793:Yes;120910794:No;120910795:Potentially" "Whether the client must be contacted as part of remediation. Adviser's answer on the remediation form (AD-095)." --confirm $U
dotnet $T addchoicecolumn $U al_remediationaction al_RecheckRequired "Recheck required?" "120910796:Yes;120910797:No" "Whether the adviser believes the case needs a recheck after remediation (AD-095)." --confirm $U
dotnet $T addchoicecolumn $U al_remediationaction al_ChangesAdvice "Remedial actions change the advice?" "120910798:Yes;120910799:No" "Whether the remedial actions change the advice given (AD-095)." --confirm $U
```

Expected each: `al_remediationaction.<name> created with options ...` listing the values and labels.

- [ ] **Step 2: Push the assembly and widen the guard step**

```
dotnet build plugins/OutcomeTesting.Plugins/OutcomeTesting.Plugins.csproj -c Release
dotnet $T pushassembly $U
dotnet $T setstepfilter $U "RemediationResponseGuardPlugin: Update of al_remediationaction" al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice --confirm $U
```

Expected: `pushed OutcomeTesting.Plugins ... bytes ... modified <timestamp>`; then `'RemediationResponseGuardPlugin: Update of al_remediationaction': filteringattributes 'al_adviserresponse,al_evidencereference' -> 'al_adviserresponse,al_evidencereference,al_clientcontactrequired,al_recheckrequired,al_changesadvice'.` If the step is not found under that name, list steps with `fetch $U '<fetch><entity name="sdkmessageprocessingstep"><attribute name="name"/><attribute name="filteringattributes"/><filter><condition attribute="name" operator="like" value="%RemediationResponseGuard%"/></filter></entity></fetch>'` and use the exact name it prints.

- [ ] **Step 3: Allowlist and templates**

```
dotnet $T setsitesetting $U Webapi/al_remediationaction/fields al_adviserresponse,al_evidencereference,al_completerequested,al_clientcontactrequired,al_recheckrequired,al_changesadvice --confirm $U
dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000019 "powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html"
dotnet $T pushwebtemplate $U a1000000-0000-4000-8000-000000000015 "powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html"
```

Expected: the setting reads back the new value; each template push prints `pushed web template '...' (...): <old> -> <new> chars, modified <timestamp>`. If Tasks 4 or 5 changed a css web file, report that it needs pushing and how the earlier notes did it (search `docs/deployment` for `outcome-testing.css`); do not invent a push.

- [ ] **Step 4: Push the app**

Run from `app/`: `npx pa app push` → `App pushed successfully`.

- [ ] **Step 5: Record the decision and the deploy**

Append after the AD-094 row in `knowledge/decision-log.md` (do not commit this file):

```markdown
| AD-095 | **The remediation form is the adviser's, filled on the remediation action.** Three new choice columns on `al_remediationaction` — `al_clientcontactrequired` (Yes 120910793 / No 120910794 / Potentially 120910795), `al_recheckrequired` (Yes 120910796 / No 120910797), `al_changesadvice` (Yes 120910798 / No 120910799) — carry the form's questions; the adviser's free-text response is the "Remedial action"; owner is the assigned contact, target date the due date, adviser sign-off the completion, supervisor sign-off the `al_signoff` decision, "All remedial actions checked and approved" the derived state of every action's latest decision, and "Regraded outcome" the `al_outcome` final grade. `RemediationResponseGuardPlugin` locks the three with the response once the action is Completed, and its step filters on all five. The worklist renders the form with the write panels; the case page renders it per case, read-only; the app's remediation tab shows the same columns. | Project owner's form and direction 2026-09-09 ("filled in by the adviser at the remediation action"). Columns live on the action rather than the case because that is where the adviser writes and where the Contact-scoped write permission already reaches (AD-069); a case-level block would need a new write path. Values from the free tail of the 1209107xx band (770-792 were used). "All checked and approved" is derived rather than stored so it cannot disagree with the sign-offs it summarises. | 2026-09-09 |
```

Create `docs/deployment/2026-09-09-remedial-form.md` with: the report (form supplied, adviser fills it), what changed (the table above), tests (plug-ins count from Task 3, app count from Task 6), a "What ran" table with every command from Steps 1-4 and its printed result, a "Retest" section (as an adviser: open My Work, expand an action assigned to you, fill the form, save and sign off; as a supervisor: sign off; open the case page and read the form), and a sign-off table. No placeholders.

- [ ] **Step 6: Commit the deploy note**

```bash
git add docs/deployment/2026-09-09-remedial-form.md
git commit -m "docs(deployment): AD-095 remedial form — columns, guard, templates and app live in DEV

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```
