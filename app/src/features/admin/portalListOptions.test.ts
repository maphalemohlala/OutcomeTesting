import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
import listOptionPermission from '../../../../powerpages/outcome-testing---outcometesting/table-permissions/List-Option---read.tablepermission.yml?raw';
import { MIGRATED_LISTS, MULTI_CHOICE_LISTS, SINGLE_CHOICE_LISTS } from './listOptions';

/**
 * The portal's four case-header dropdowns are drawn from al_listoption rows (AD-187).
 *
 * They used to be <option> tags written out by hand - nineteen of them across the four -
 * which is the reason an option could be added to a choice column in Dataverse and still not
 * be selectable by anybody. These pin that they are now data, and what they must not do.
 */

/** Every managed dropdown on the portal header: its lookup, its fetch, and its old values. */
const DROPDOWNS = [
  {
    label: 'Product / solution type',
    attr: 'al_producttypeid',
    fetch: 'product_types',
    list: 120910840,
    oldValues: [120910520, 120910521, 120910522, 120910523, 120910524],
    oldLabels: ['Accumulation Pension', 'No change reviews'],
  },
  {
    label: 'Sample source',
    attr: 'al_samplesourceid',
    fetch: 'sample_sources',
    list: 120910841,
    oldValues: [120910530, 120910531, 120910532, 120910533],
    oldLabels: ['High Risk', 'Thematic'],
  },
  {
    label: 'Case type',
    attr: 'al_casetypeid',
    fetch: 'case_types',
    list: 120910842,
    oldValues: [120910510, 120910511, 120910512, 120910513],
    oldLabels: ['New advice', 'Switch/Transfer'],
  },
  {
    label: 'Pre or post check',
    attr: 'al_preorpostcheckid',
    fetch: 'pre_post_checks',
    list: 120910843,
    oldValues: [120910540, 120910541],
    oldLabels: [] as string[],
  },
] as const;

/** One editable header cell's <select>, tags and Liquid intact. */
function headerSelect(attr: string): string {
  const start = reviewTemplate.indexOf(`data-ot-hdr="${attr}"`);
  expect(start, `the header select should be bound to ${attr}`).toBeGreaterThan(-1);
  const open = reviewTemplate.lastIndexOf('<select', start);
  const end = reviewTemplate.indexOf('</select>', start);
  return reviewTemplate.slice(open, end);
}

/** One list's fetchxml block. */
function fetchBlock(name: string): string {
  const start = reviewTemplate.indexOf(`{% fetchxml ${name} %}`);
  expect(start, `fetchxml ${name}`).toBeGreaterThan(-1);
  return reviewTemplate.slice(start, reviewTemplate.indexOf('{% endfetchxml %}', start));
}

describe('the portal draws every managed dropdown from the table', () => {
  it.each(DROPDOWNS)('$label writes no option value by hand', (dropdown) => {
    // THE regression this exists to catch. A hard-coded value is an option somebody can
    // choose that the management page does not know about, or an option the page offers
    // that the portal silently will not.
    const select = headerSelect(dropdown.attr);
    for (const value of dropdown.oldValues) {
      expect(select, `option value ${value}`).not.toContain(String(value));
    }
    for (const label of dropdown.oldLabels) {
      expect(select, label).not.toContain(label);
    }
  });

  it.each(DROPDOWNS)('$label loops the rows its fetch returned', (dropdown) => {
    const select = headerSelect(dropdown.attr);
    expect(select).toContain(`{% for o in ${dropdown.fetch}.results.entities %}`);
    expect(select).toContain('{{ o.al_name | escape }}');
  });

  it.each(DROPDOWNS)('$label asks for exactly its own list', (dropdown) => {
    // All four lists share one table, so a fetch without this filter would offer sample
    // sources in the case type dropdown - and they would save, because the option ids are
    // real. The server refuses that too; this stops it ever being offered.
    const fetch = fetchBlock(dropdown.fetch);
    expect(fetch).toContain('<entity name="al_listoption">');
    expect(fetch).toContain(
      `<condition attribute="al_list" operator="eq" value="${dropdown.list}" />`,
    );
  });

  it('gives each list its own fetch and its own lookup, with nothing shared by accident', () => {
    expect(new Set(DROPDOWNS.map((d) => d.fetch)).size).toBe(DROPDOWNS.length);
    expect(new Set(DROPDOWNS.map((d) => d.attr)).size).toBe(DROPDOWNS.length);
    expect(new Set(DROPDOWNS.map((d) => d.list)).size).toBe(DROPDOWNS.length);
  });

  it('covers every SINGLE-choice list, and no more', () => {
    // A list migrated in the app but left hardcoded here would be a dropdown that silently
    // disagreed with the management page about what exists.
    //
    // Single-choice only: Products is held through a many-to-many and is drawn as a
    // multi-select, so it is covered by its own tests below rather than by these.
    expect(DROPDOWNS.map((d) => d.attr).slice().sort()).toEqual(
      SINGLE_CHOICE_LISTS.map((l) => l.caseAttribute).slice().sort(),
    );
    expect(DROPDOWNS.map((d) => d.list).slice().sort()).toEqual(
      SINGLE_CHOICE_LISTS.map((l) => l.value).slice().sort(),
    );
  });

  it('accounts for every manageable list one way or the other', () => {
    // The guard that stops a list being added to the management page and then forgotten on
    // the portal: every migrated list is either a single-choice dropdown here or a
    // multi-choice one, and nothing falls between the two.
    expect(SINGLE_CHOICE_LISTS.length + MULTI_CHOICE_LISTS.length).toBe(MIGRATED_LISTS.length);
    expect(MULTI_CHOICE_LISTS.map((l) => l.key)).toEqual(['products']);
  });

  it.each(DROPDOWNS)('$label offers only what is in force, on the shared window', (dropdown) => {
    const fetch = fetchBlock(dropdown.fetch);
    // from <= day, and day < to. `gt` rather than `on-or-after`: an option retired today is
    // retired today, and the server refuses it either way.
    expect(fetch).toContain(
      '<condition attribute="al_effectivefrom" operator="on-or-before" value="{{ product_day }}" />',
    );
    expect(fetch).toContain(
      '<condition attribute="al_effectiveto" operator="gt" value="{{ product_day }}" />',
    );
    expect(fetch).toContain('<condition attribute="al_effectivefrom" operator="null" />');
    expect(fetch).toContain('<condition attribute="al_effectiveto" operator="null" />');
  });

  it('asks as of today, not as of the review’s submission day', () => {
    // as_of answers "which version was this answered against", a question about history.
    // This answers "what may be chosen now". Reusing as_of would have offered a checker the
    // options that existed when the review was submitted.
    expect(reviewTemplate).toContain("{% assign product_day = now | date: 'yyyy-MM-dd' %}");
  });

  it.each(DROPDOWNS)('$label keeps a retired option the case holds, marked', (dropdown) => {
    // Otherwise the cell renders as an em dash and a checker reads it as a field to fill in.
    // choicesIncludingHeld is the Code App's half of the same allowance.
    const select = headerSelect(dropdown.attr);
    expect(select).toContain('{% unless ');
    expect(select).toContain('(retired)');
  });

  it.each(DROPDOWNS)('$label only calls it retired when the list actually loaded', (dropdown) => {
    // Found by testing DEV in a browser: the options came back empty, and every held value
    // fell through the "not among the offered options" branch and rendered as
    // "Pre (retired)". It was not retired - the list could not be read.
    //
    // Two different things wear the same shape. "Not offered" means retired ONLY if there
    // was something to be offered; if the fetch returned nothing, all that is known is that
    // the catalogue is unavailable, and telling a checker their case holds a retired value
    // is a claim about their data made from a failure to read anyone's.
    const select = headerSelect(dropdown.attr);

    // The flag is set inside the loop, so it is true only when a row was drawn.
    const anyFlag = /\{% assign (\w+_any) = false %\}/.exec(select);
    expect(anyFlag, 'a flag recording whether any option was drawn').not.toBeNull();
    expect(select).toContain(`{% for o in ${dropdown.fetch}.results.entities %}{% assign ${anyFlag![1]} = true %}`);

    // ...and the suffix is behind it.
    expect(select).toContain(`{% if ${anyFlag![1]} %} (retired){% endif %}`);
    expect(select, 'the suffix must not be emitted unconditionally').not.toMatch(
      /escape \}\} \(retired\)/,
    );
  });
});

describe('what the portal may do with the options', () => {
  it('can read them, and cannot create, change or delete one', () => {
    // The management page is in the Code App. The portal renders the catalogue; it has no
    // business adding to it, and the table permission is what makes that true rather than a
    // convention about which page has a button.
    expect(listOptionPermission).toContain('adx_entitylogicalname: al_listoption');
    expect(listOptionPermission).toContain('adx_read: true');
    expect(listOptionPermission).toContain('adx_create: false');
    expect(listOptionPermission).toContain('adx_write: false');
    expect(listOptionPermission).toContain('adx_delete: false');
  });
});

describe('reading a case’s managed values on the portal', () => {
  it('prefers the chosen option and falls back to the choice column, on both pages', () => {
    // Until every case is backfilled, reading only the lookup would blank these on the older
    // ones - the same precedence the app and the export apply.
    const reviewPairs: readonly (readonly [string, string])[] = [
      ['h_producttypeid', 'h_producttype'],
      ['h_casetypeid', 'h_casetype'],
      ['h_samplesourceid', 'h_samplesource'],
      ['h_preorpostcheckid', 'h_prepost'],
    ];

    for (const [lookup, legacy] of reviewPairs) {
      expect(reviewTemplate, lookup).toContain(`{% if ${lookup}.name %}`);
      expect(reviewTemplate, legacy).toContain(`{{ ${legacy}.label | escape }}`);
    }

    for (const list of SINGLE_CHOICE_LISTS) {
      expect(caseTemplate, list.key).toContain(`{% if c.${list.caseAttribute}.name %}`);
      expect(caseTemplate, list.key).toContain(`{{ c.${list.legacyAttribute}.label`);
    }
  });

  it('asks for every lookup on both pages, or there would be nothing to read', () => {
    for (const list of SINGLE_CHOICE_LISTS) {
      expect(reviewTemplate, list.key).toContain(`<attribute name="${list.caseAttribute}" />`);
      expect(caseTemplate, list.key).toContain(`<attribute name="${list.caseAttribute}" />`);
    }
  });
});
