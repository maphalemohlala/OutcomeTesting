# Vulnerable client managed list Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the case header's "Vulnerable client?" a sixth managed list on the Code App's Dropdown options page, read and written through a lookup everywhere the field appears, with every existing case keeping its value.

**Architecture:** Repeat the AD-188 shape once more. There's a new `al_list` value, 120910845, and a new lookup `al_outcomecase.al_vulnerableclientid` to `al_listoption`. The existing choice column `al_vulnerableclient` stays as the legacy fallback. The registration tool's generic verbs create the lookup and seed the rows from the choice column's own metadata. The plug-ins, the Code App and both portal pages gain the list next to Sample source, Case type and the others.

**Tech Stack:**
- Dataverse plug-ins: C# net462, xUnit, `FakeOrganizationService`.
- `OutcomeTesting.Registration`: net8, needs `DOTNET_ROLL_FORWARD=Major`.
- React/TypeScript Code App: vitest, `tsc -b`.
- Power Pages Liquid templates.

**Spec:** `docs/superpowers/specs/2026-09-30-vulnerable-client-managed-list-design.md`

## Global Constraints

- List value: `120910845`, label `Vulnerable client`. Lookup logical name `al_vulnerableclientid`, schema name `al_VulnerableClientId`, relationship `al_listoption_al_outcomecase_vulnerableclient`. Legacy column `al_vulnerableclient`.
- Starting options are exactly today's four choices, in today's order: Yes (120910550), No (120910551), Potentially vulnerable (120910552), N/A (120910553). No option is added by this work.
- The legacy column is kept and still read as a fallback everywhere; nothing is deleted.
- All environment writes go to DEV (`https://org0b075da8.crm11.dynamics.com`) only. TEST is the owner's call.
- Run plug-in tests with `DOTNET_ROLL_FORWARD=Major`. The app type check is `npx tsc -b`, because `tsc --noEmit` checks nothing here.
- Build Release before `pushassembly`, and verify by sha256 of `pluginassembly.content`.
- `npm run build` before `npx pa app push`, and check the bundle hash.
- Use `--modelVersion Enhanced` for any `pac powerpages` command. Prefer the registration tool's `pushwebtemplate`, because `pac powerpages upload` overwrites newer DEV state.

## Review Focus

1. **A case imported before this change, holding only the legacy value.** It must still show "Yes" / "No" / "Potentially vulnerable" / "N/A" on the review header, the case record, the Code App and the PDF before and after the backfill. The tests for this are in Task 1 (PDF fallback), Task 4 (checklistForm fallback) and Task 5 (portal fallback on both pages).
2. **An option from another list sent as Vulnerable client** must be refused ("is not an option on that list"). Tested in Task 1 through the `EveryList` theory row.
3. **A retired Vulnerable client option still held by a case** must still read on the review header, marked "(retired)" only when the list loaded. Tested in Task 5 through the `DROPDOWNS` theories.
4. **Clearing the field** (choosing "—") must clear the lookup rather than be refused. Tested in Task 1 (`Every_list_can_be_cleared`).
5. **The portal offering Vulnerable client options from another list** would save wrong-list ids. The `al_list` filter is pinned in Task 5 through the "asks for exactly its own list" theory.

---

### Task 1: Plug-ins know the Vulnerable client list

**Files:**
- Modify: `plugins/OutcomeTesting.Plugins/ListOptionRules.cs` (attribute constants near line 49; list values near line 86)
- Modify: `plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs` (`Editables`, after the `PreOrPostCheckAttribute` entry near line 557)
- Modify: `plugins/OutcomeTesting.Plugins/CaseHeaderRequestPlugin.cs` (`CheckerEditable`, after `ListOptionRules.PreOrPostCheckAttribute` near line 395)
- Modify: `plugins/OutcomeTesting.Plugins/CompletedCheck.cs` (ColumnSet near line 96; the Vulnerable client cell near line 246)
- Test: `plugins/OutcomeTesting.Plugins.Tests/ListOptionRulesTests.cs`
- Test: `plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs`

**Interfaces:**
- Produces:
  - `ListOptionRules.VulnerableClientAttribute = "al_vulnerableclientid"`
  - `ListOptionRules.VulnerableClientLegacyAttribute = "al_vulnerableclient"`
  - `ListOptionRules.VulnerableClient = 120910845`

- [ ] **Step 1: Write the failing tests**

In `ListOptionRulesTests.cs`, add a row to `EveryList()`. Then add a clearing theory after `Every_list_refuses_a_retired_option`, and extend the distinctness fact:

```csharp
                { ListOptionRules.PreOrPostCheckAttribute, ListOptionRules.PreOrPostCheck, ListOptionRules.PreOrPostCheckLegacyAttribute },
                { ListOptionRules.VulnerableClientAttribute, ListOptionRules.VulnerableClient, ListOptionRules.VulnerableClientLegacyAttribute },
```

```csharp
        [Theory]
        [MemberData(nameof(EveryList))]
        public void Every_list_can_be_cleared(string attribute, int list, string legacy)
        {
            // Choosing the dash sends an empty value; it must clear the lookup, not be refused.
            var service = new FakeOrganizationService();
            var update = new Entity("al_outcomecase", CaseId);
            var changes = new List<string>();

            UpdateCaseDetailsPlugin.ApplyFields(
                service,
                new Dictionary<string, string> { { attribute, string.Empty } },
                new Entity("al_outcomecase", CaseId),
                update,
                changes,
                new OptionLabels(service));

            Assert.True(update.Contains(attribute));
            Assert.Null(update[attribute]);
            Assert.True(list > 0);
            Assert.NotNull(legacy);
        }
```

In `The_four_lookups_are_distinct_from_each_other_and_from_their_choice_columns`, add `ListOptionRules.VulnerableClientAttribute,` as the last entry of `lookups`, and `ListOptionRules.VulnerableClientLegacyAttribute,` as the last entry of `legacies`.

In `CompletedCheckDocumentTests.cs`, add after `The_case_header_is_the_pages_table_and_io_reference_is_the_client_ref`:

```csharp
        [Fact]
        public void The_header_names_the_vulnerable_client_option_the_case_holds()
        {
            // A managed list from 2026-09-30: the option's own name, not the legacy choice.
            var service = Case();
            service.Row("al_outcomecase", CaseId)["al_vulnerableclientid"] =
                new EntityReference("al_listoption", Guid.NewGuid()) { Name = "Potentially vulnerable" };

            var header = Tables(CompletedCheck.Blocks(service, AqsReviewId))
                .First(t => t.Rows.Any(r => r.Cells.Any(c => c.Text == "Adviser name")));

            var row = Row(header, "Pre or post check");
            Assert.Equal("Vulnerable client?", row.Cells[2].Text);
            Assert.Equal("Potentially vulnerable", row.Cells[3].Text);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd plugins/OutcomeTesting.Plugins.Tests && DOTNET_ROLL_FORWARD=Major dotnet test --filter "FullyQualifiedName~ListOptionRulesTests|FullyQualifiedName~CompletedCheckDocumentTests"`
Expected: build error CS0117, `'ListOptionRules' does not contain a definition for 'VulnerableClientAttribute'`.

- [ ] **Step 3: Implement**

`ListOptionRules.cs`, after the `PreOrPostCheckLegacyAttribute` line:

```csharp
        /// <summary>From 2026-09-30 (the sixth list): "Vulnerable client?".</summary>
        public const string VulnerableClientAttribute = "al_vulnerableclientid";
        public const string VulnerableClientLegacyAttribute = "al_vulnerableclient";
```

and after `public const int Products = 120910844;`:

```csharp
        public const int VulnerableClient = 120910845;
```

`UpdateCaseDetailsPlugin.cs` `Editables`, after the `PreOrPostCheckAttribute` entry:

```csharp
                {
                    ListOptionRules.VulnerableClientAttribute,
                    new EditableField(EditableKind.ListOption, "Vulnerable client", ListOptionRules.VulnerableClient)
                },
```

Keep the existing `{ "al_vulnerableclient", new EditableField(EditableKind.Option, "Vulnerable client") }` entry: it's the way back for a case the backfill couldn't resolve, as the comment on `al_productsolutiontype` explains.

`CaseHeaderRequestPlugin.cs` `CheckerEditable`, after `ListOptionRules.PreOrPostCheckAttribute,`:

```csharp
                ListOptionRules.VulnerableClientAttribute,
```

`CompletedCheck.cs`: in the case ColumnSet, change `"al_preorpostcheck", "al_preorpostcheckid", "al_vulnerableclient",` to:

```csharp
                        "al_preorpostcheck", "al_preorpostcheckid", "al_vulnerableclient", "al_vulnerableclientid",
```

and change the cell `PdfCell.Label("Vulnerable client?"), PdfCell.Of(Choice("al_vulnerableclient")));` to:

```csharp
                PdfCell.Label("Vulnerable client?"), PdfCell.Of(Lookup("al_vulnerableclientid", "al_vulnerableclient")));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd plugins/OutcomeTesting.Plugins.Tests && DOTNET_ROLL_FORWARD=Major dotnet test`
Expected: all pass. The count is the previous 1744 plus 6 new theory cases (one `EveryList` row across the five theories, plus the new clearing theory's six rows) and one fact; read the exact total from the output.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Plugins/ListOptionRules.cs plugins/OutcomeTesting.Plugins/UpdateCaseDetailsPlugin.cs plugins/OutcomeTesting.Plugins/CaseHeaderRequestPlugin.cs plugins/OutcomeTesting.Plugins/CompletedCheck.cs plugins/OutcomeTesting.Plugins.Tests/ListOptionRulesTests.cs plugins/OutcomeTesting.Plugins.Tests/CompletedCheckDocumentTests.cs
git commit -m "feat(lists): the plug-ins accept Vulnerable client as a managed list"
```

---

### Task 2: The registration tool declares the list

**Files:**
- Modify: `plugins/OutcomeTesting.Registration/Program.cs`:
  - `ListOptionTable.Lists`, near line 11737;
  - the `al_list` option set in `CreateListOptionTable`, near line 8483;
  - the list constants, near line 11750.

**Interfaces:**
- Produces: `ListOptionTable.VulnerableClient = 120910845` and a `Def` that `createlistoptiontable` and `seedlistoptions` loop over.

- [ ] **Step 1: Add the constant after `public const int Products = 120910844;`**

```csharp
    public const int VulnerableClient = 120910845;
```

- [ ] **Step 2: Add the Def as the last entry of `ListOptionTable.Lists`, and update the summary's "four" to "five"**

```csharp
        new Def(VulnerableClient, "al_vulnerableclientid", "al_VulnerableClientId",
            "al_listoption_al_outcomecase_vulnerableclient", "Vulnerable client", "al_vulnerableclient"),
```

- [ ] **Step 3: Add the option to the `al_list` picklist a fresh environment is created with, after the Pre or post check option**

```csharp
                        new OptionMetadata(ListOptionTable.Text("Vulnerable client"), ListOptionTable.VulnerableClient),
```

- [ ] **Step 4: Build**

Run: `cd plugins/OutcomeTesting.Registration && DOTNET_ROLL_FORWARD=Major dotnet build -c Debug`
Expected: `Build succeeded.`, `0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add plugins/OutcomeTesting.Registration/Program.cs
git commit -m "feat(registration): declare the Vulnerable client list"
```

---

### Task 3: DEV schema, seed, solution membership and the generated model

These are environment steps, so there's no unit test. Each step reads back what it wrote. Run the tool as `DOTNET_ROLL_FORWARD=Major timeout 590 plugins/OutcomeTesting.Registration/bin/Debug/net8.0/OutcomeTesting.Registration.exe <verb> ...` from the repo root, one process at a time.

**Files:**
- Modify (generated): `app/src/generated/**`, `app/.power/schemas/**`, `app/power.config.json`

- [ ] **Step 1: Add the `al_list` value in DEV**

Run: `... addoptionvalue https://org0b075da8.crm11.dynamics.com al_listoption al_list 120910845 "Vulnerable client"`
Expected: the value is reported added, or already present.

- [ ] **Step 2: Create the lookup**

Run: `... createlistoptiontable https://org0b075da8.crm11.dynamics.com`
Expected: every existing part is reported "already present", and `al_vulnerableclientid` is reported created and added to OutcomeTesting.

- [ ] **Step 3: Seed and backfill**

Run a dry run first: `... seedlistoptions https://org0b075da8.crm11.dynamics.com`
Then run for real: `... seedlistoptions https://org0b075da8.crm11.dynamics.com --confirm https://org0b075da8.crm11.dynamics.com`
Expected:
- Four "Vulnerable client" rows: Yes, No, Potentially vulnerable, N/A, with `al_legacyvalue` 120910550 to 120910553.
- Every case holding `al_vulnerableclient` is pointed at its row.
- The other lists are reported unchanged.

- [ ] **Step 4: Prove solution membership**

Run: `... metadatamembership https://org0b075da8.crm11.dynamics.com`
Expected: "Every al_ table is carried". `al_outcomecase` carries its subcomponents, so the new lookup and relationship travel with it.

- [ ] **Step 5: Regenerate the Code App model**

Run from `app/`:
`npx pa app add data-source --connector dataverse --table al_outcomecase --non-interactive`
then
`npx pa app add data-source --connector dataverse --table al_listoption --non-interactive`

Expected:
- `src/generated/models/Al_outcomecasesModel.ts` now declares `_al_vulnerableclientid_value` and `al_vulnerableclientidname`.
- The `al_list` union in `Al_listoptionsModel.ts` includes `120910845`.

Then run `git diff app/src/generated/services/dataSourcesInfo.ts`. If the generator dropped any hand-declared Custom API parameter (it dropped `StaffCode` last time), restore that file with `git checkout -- app/src/generated/services/dataSourcesInfo.ts` and re-check that only the two tables' entries were meant to change.

- [ ] **Step 6: Type-check the untouched app against the new model**

Run: `cd app && npx tsc -b`
Expected: clean. The app doesn't reference the new fields yet.

- [ ] **Step 7: Commit the generated files**

```bash
git add app/src/generated app/.power/schemas app/power.config.json
git commit -m "chore(app): regenerate the model for the Vulnerable client lookup"
```

---

### Task 4: The Code App reads and writes the list

**Files:**
- Modify: `app/src/features/admin/listOptions.ts` (constants near line 42, the `ListValue` union, `MANAGED_LISTS`)
- Modify: `app/src/features/cases/CaseEditPanel.tsx` (the `al_vulnerableclient` choice field near line 105; the import near line 40)
- Modify: `app/src/features/cases/caseDetailMapping.ts` (the type near line 51; `toDetail` near line 180)
- Modify: `app/src/features/reviews/checklistForm.ts` (the "Vulnerable client?" row near line 395)
- Test: `app/src/features/admin/listOptions.test.ts`
- Test: `app/src/features/reviews/checklistForm.test.ts`
- Test: `app/src/features/cases/useCaseDetail.test.ts`

**Interfaces:**
- Consumes: the generated `_al_vulnerableclientid_value`, `al_vulnerableclientidname`, and the `al_list` union including `120910845` (Task 3).
- Produces: `LIST_VULNERABLE_CLIENT = 120910845` and a `MANAGED_LISTS` entry with key `vulnerable-client`, `caseAttribute: 'al_vulnerableclientid'` and `legacyAttribute: 'al_vulnerableclient'`. Task 5's portal test reads `SINGLE_CHOICE_LISTS`.

- [ ] **Step 1: Write the failing tests**

`listOptions.test.ts`:
- In `names the four the original request named, plus Products`, rename the test to `names the four the original request named, plus Products and Vulnerable client`, and append `'Vulnerable client',` to the expected labels.
- In `offers every list...`, append `LIST_VULNERABLE_CLIENT,` to the expected values, and add `LIST_VULNERABLE_CLIENT` to the file's import from `./listOptions`.
- In `holds Products through a relationship...`, append `'vulnerable-client',` to the `SINGLE_CHOICE_LISTS` keys.
- In `keeps naming the choice column each list replaces`, append `'al_vulnerableclient',`.
- In `builds the Web API field names for each list`, add:

```ts
    expect(lookupValueField(listByValue(LIST_VULNERABLE_CLIENT)!)).toBe('_al_vulnerableclientid_value');
```

`checklistForm.test.ts`: in `prefers the managed option over the choice column on every migrated list`, add `al_vulnerableclientidname: 'Chosen vulnerability',` to the record spread, and add:

```ts
    expect(byLabel.get('Vulnerable client?')).toBe('Chosen vulnerability');
```

Then add this test after it:

```ts
  it('still reads the legacy Vulnerable client choice on a case the backfill has not reached', () => {
    const byLabel = new Map(
      caseHeaderFields({
        ...record,
        al_vulnerableclient: 120910552,
      } as unknown as Al_outcomecases).map((f) => [f.label, f.value]),
    );
    expect(byLabel.get('Vulnerable client?')).toBe('Potentially vulnerable');
  });
```

`useCaseDetail.test.ts`: add `_al_vulnerableclientid_value: 'vc-1',` to the record the existing mapping test builds, and assert:

```ts
    expect(detail.al_vulnerableclientid).toBe('vc-1');
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd app && npx vitest run src/features/admin/listOptions.test.ts src/features/reviews/checklistForm.test.ts src/features/cases/useCaseDetail.test.ts`
Expected: FAIL. `LIST_VULNERABLE_CLIENT` is not exported, 'Vulnerable client' is missing from the labels, and `detail.al_vulnerableclientid` is undefined.

- [ ] **Step 3: Implement**

`listOptions.ts`, after `LIST_PRODUCTS`:

```ts
export const LIST_VULNERABLE_CLIENT = 120910845 as const;
```

Add it to the union:

```ts
  | typeof LIST_PRODUCTS
  | typeof LIST_VULNERABLE_CLIENT;
```

And append to `MANAGED_LISTS`:

```ts
  {
    value: LIST_VULNERABLE_CLIENT,
    key: 'vulnerable-client',
    caseRelationship: null,
    label: 'Vulnerable client',
    caseAttribute: 'al_vulnerableclientid',
    legacyAttribute: 'al_vulnerableclient',
  },
```

`CaseEditPanel.tsx`: replace

```ts
      { attr: 'al_vulnerableclient', label: 'Vulnerable client', kind: 'choice', options: Al_outcomecasesal_vulnerableclient },
```

with

```ts
      {
        attr: 'al_vulnerableclientid',
        label: 'Vulnerable client',
        kind: 'listoption',
        list: managedList('vulnerable-client'),
        help: 'Maintained under Admin → Dropdown options.',
      },
```

and remove `Al_outcomecasesal_vulnerableclient,` from the generated-model import if nothing else in the file uses it.

`caseDetailMapping.ts`: add to the type, after `al_preorpostcheckid: string;`:

```ts
  al_vulnerableclientid: string;
```

and in `toDetail`, after the `al_preorpostcheckid` line:

```ts
      al_vulnerableclientid: text(record._al_vulnerableclientid_value) ?? '',
```

`checklistForm.ts`: replace the "Vulnerable client?" row's value with

```ts
      value:
        lookupLabelOn(record as unknown as Record<string, unknown>, managedList('vulnerable-client')) ??
        choiceLabel(
          Al_outcomecasesal_vulnerableclient,
          record.al_vulnerableclient,
          record.al_vulnerableclientname,
        ),
```

- [ ] **Step 4: Run the tests and the type check**

Run: `cd app && npx vitest run && npx tsc -b`
Expected: every test file passes and tsc is clean. If a test elsewhere counted the lists (for example the portal test's "covers every SINGLE-choice list"), it now fails. That failure is Task 5's to fix, so go on to Task 5 before committing only if the only failures are in `portalListOptions.test.ts`. Commit Tasks 4 and 5 together in that case.

- [ ] **Step 5: Commit**

```bash
git add app/src/features/admin/listOptions.ts app/src/features/admin/listOptions.test.ts app/src/features/cases/CaseEditPanel.tsx app/src/features/cases/caseDetailMapping.ts app/src/features/cases/useCaseDetail.test.ts app/src/features/reviews/checklistForm.ts app/src/features/reviews/checklistForm.test.ts
git commit -m "feat(app): Vulnerable client is a managed list in the Code App"
```

---

### Task 5: Both portal pages draw the list

**Files:**
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`:
  - the case link-entity attributes, near line 161;
  - the held-value assigns, near line 393;
  - a new `vulnerable_clients` fetch, after the `sample_sources` fetch that ends near line 445;
  - the editable select, near line 678;
  - the read-only row, near line 695.
- Modify: `powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html`:
  - the case fetch attributes, near line 66;
  - the read-only Vulnerable client cell, near line 445.
- Test: `app/src/features/admin/portalListOptions.test.ts`

- [ ] **Step 1: Write the failing test**

In `portalListOptions.test.ts`, append to `DROPDOWNS`:

```ts
  {
    label: 'Vulnerable client',
    attr: 'al_vulnerableclientid',
    fetch: 'vulnerable_clients',
    list: 120910845,
    oldValues: [120910550, 120910551, 120910552, 120910553],
    oldLabels: ['Potentially vulnerable'],
  },
```

and add `['h_vulnerableclientid', 'h_vulnerable'],` to the `reviewPairs` in `prefers the chosen option and falls back to the choice column, on both pages`.

- [ ] **Step 2: Run it to verify it fails**

Run: `cd app && npx vitest run src/features/admin/portalListOptions.test.ts`
Expected: FAIL. "the header select should be bound to al_vulnerableclientid", and `fetchxml vulnerable_clients` is not found.

- [ ] **Step 3: Implement OT Review Detail**

After `<attribute name="al_vulnerableclient" />` in the case link-entity, add:

```html
        <attribute name="al_vulnerableclientid" />
```

After `{% assign held_preorpost = h_preorpostcheckid.id %}`, add:

```liquid
    {% assign h_vulnerableclientid = rv['case.al_vulnerableclientid'] %}
    {% assign held_vulnerable = h_vulnerableclientid.id %}
```

After the `sample_sources` fetch's `{% endfetchxml %}`, add a new fetch. It's identical to `sample_sources` except for its name and the `al_list` value:

```liquid
{% fetchxml vulnerable_clients %}
    <fetch>
      <entity name="al_listoption">
        <attribute name="al_listoptionid" />
        <attribute name="al_name" />
        <attribute name="al_sortorder" />
        <filter type="and">
          <condition attribute="statecode" operator="eq" value="0" />
          <condition attribute="al_list" operator="eq" value="120910845" />
          <filter type="or">
            <condition attribute="al_effectivefrom" operator="null" />
            <condition attribute="al_effectivefrom" operator="on-or-before" value="{{ product_day }}" />
          </filter>
          <filter type="or">
            <condition attribute="al_effectiveto" operator="null" />
            <condition attribute="al_effectiveto" operator="gt" value="{{ product_day }}" />
          </filter>
        </filter>
        <order attribute="al_sortorder" />
        <order attribute="al_name" />
      </entity>
    </fetch>
    {% endfetchxml %}
```

Replace the editable Vulnerable client cell (the `<select ... data-ot-hdr="al_vulnerableclient" ...>` with four hand-written options) with a select in the Sample source cell's exact shape, on one line:

```html
            <td class="lbl">Vulnerable client?</td><td class="val"><select class="ot-hdr-input" data-ot-hdr="al_vulnerableclientid" aria-label="Vulnerable client"><option value="">&mdash;</option>{% assign vulnerable_offered = false %}{% assign vulnerable_any = false %}{% for o in vulnerable_clients.results.entities %}{% assign vulnerable_any = true %}<option value="{{ o.al_listoptionid }}" {% if held_vulnerable and o.al_listoptionid == held_vulnerable %}selected{% assign vulnerable_offered = true %}{% endif %}>{{ o.al_name | escape }}</option>{% endfor %}{% unless vulnerable_offered %}{% if held_vulnerable %}<option value="{{ held_vulnerable }}" selected>{{ h_vulnerableclientid.name | escape }}{% if vulnerable_any %} (retired){% endif %}</option>{% endif %}{% endunless %}</select></td>
```

A case not yet backfilled (lookup empty, legacy value set) shows "—" in the editable select until it's edited or backfilled. The backfill in Task 3 covers every DEV case, so this is expected. The read-only row keeps the fallback.

Replace the read-only cell `<td class="lbl">Vulnerable client?</td><td class="val">{{ h_vulnerable.label | escape }}</td>` with:

```html
<td class="lbl">Vulnerable client?</td><td class="val">{% if h_vulnerableclientid.name %}{{ h_vulnerableclientid.name | escape }}{% else %}{{ h_vulnerable.label | escape }}{% endif %}</td>
```

Leave `{% assign h_vulnerable = rv['case.al_vulnerableclient'] %}` in place, because the fallback reads it.

- [ ] **Step 4: Implement OT Case Detail**

After `<attribute name="al_preorpostcheckid" />` in the case fetch, add `<attribute name="al_vulnerableclientid" />`. Then replace the Vulnerable client cell with:

```html
            <td class="lbl">Vulnerable client?</td><td class="val">{% if c.al_vulnerableclientid.name %}{{ c.al_vulnerableclientid.name | escape }}{% else %}{{ c.al_vulnerableclient.label | default: '—' | escape }}{% endif %}</td>
```

Run `grep -n "Vulnerable client" OT-Case-Detail.webtemplate.source.html` and apply the same replacement to every read-only Vulnerable client cell it lists.

- [ ] **Step 5: Run the portal tests and the whole app suite**

Run: `cd app && npx vitest run`
Expected: all pass. That includes `covers every SINGLE-choice list, and no more`, now with five dropdowns, and `asks for every lookup on both pages`.

- [ ] **Step 6: Commit**

```bash
git add powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html app/src/features/admin/portalListOptions.test.ts
git commit -m "feat(portal): the Vulnerable client dropdown is drawn from the managed list"
```

---

### Task 6: Deploy to DEV, prove it, record it

**Files:**
- Create: `docs/deployment/2026-09-30-vulnerable-client-managed-list.md`
- Modify: `knowledge/decision-log.md` (append AD-226)

- [ ] **Step 1: Push the plug-in assembly**

Run: `cd plugins && DOTNET_ROLL_FORWARD=Major dotnet build OutcomeTesting.Plugins -c Release`, then `sha256sum OutcomeTesting.Plugins/bin/Release/net462/OutcomeTesting.Plugins.dll`, then from the repo root `... pushassembly https://org0b075da8.crm11.dynamics.com`.
Verify: the sha256 of `pluginassemblies(7b51d0d1-f5a1-f111-b8dd-e4fade069307)` `content` (base64-decoded) equals the local DLL's.

- [ ] **Step 2: Push the two templates**

- `... pushwebtemplate https://org0b075da8.crm11.dynamics.com a1000000-0000-4000-8000-00000000001b powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html`
- OT Case Detail: find its id with `grep adx_webtemplateid powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/*.yml`, then push it the same way.

- [ ] **Step 3: Build and push the Code App**

Run: `cd app && npm run build`. Note the `index-*.js` name, then run `npx pa app push`.
Expected: "App pushed successfully". If it says the sign-in has expired, ask the owner to run `npx pa auth login` and retry.

- [ ] **Step 4: Prove it**

Server-side, with the registration tool's `webapi`/`webapimany`:
1. `al_listoptions?$filter=al_list eq 120910845&$select=al_name,al_legacyvalue,al_sortorder` returns the four rows.
2. Pick a DEV case that held `al_vulnerableclient`. Its `_al_vulnerableclientid_value` points at the matching row.
3. `al_UpdateCaseDetails` with `{"al_vulnerableclientid":"<a Vulnerable client row id>"}` succeeds.
4. The same call with a Sample source row id is refused with "is not an option on that list".

In the browser (Playwright, `app/e2e/.auth/portal.json`, read-only):
- On the review page of a case assigned to Service Account, the Vulnerable client select lists the four options, with the case's value selected.
- The case record shows the option's name.

- [ ] **Step 5: Solution membership**

Run: `... metadatamembership https://org0b075da8.crm11.dynamics.com`
Expected: every `al_` table is carried.

- [ ] **Step 6: Record**

Write `docs/deployment/2026-09-30-vulnerable-client-managed-list.md` with:
- what ran, with the commands and their read-backs;
- what was proved;
- the TEST runbook: export 1.0.17.0, import, then `seedlistoptions https://org37995f36.crm11.dynamics.com --confirm https://org37995f36.crm11.dynamics.com`, which is expected to be run by the owner, then diff the three masked portal components.

Append AD-226 to `knowledge/decision-log.md`: "Vulnerable client? is the sixth managed list (2026-09-30)", why, and what it costs.

- [ ] **Step 7: Commit**

```bash
git add docs/deployment/2026-09-30-vulnerable-client-managed-list.md knowledge/decision-log.md
git commit -m "docs: Vulnerable client managed list deployed to DEV; AD-226"
```
