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

describe('the portal draws Products as a tick list', () => {
  /** The Products checkbox group, tags and Liquid intact. */
  function productSet(): string {
    const start = reviewTemplate.indexOf('data-ot-hdr-set="al_productids"');
    expect(start, 'the products group should be bound to the set field').toBeGreaterThan(-1);
    const open = reviewTemplate.lastIndexOf('<div', start);
    return reviewTemplate.slice(open, reviewTemplate.indexOf('</div>', start));
  }

  it('is a set, not one of the single-choice dropdowns', () => {
    // A case covers several products - the column it replaces is labelled "Product(s)" and
    // holds "Pension; ISA" - so a single <select> would record less than the free text did.
    expect(MULTI_CHOICE_LISTS.map((l) => l.key)).toEqual(['products']);
    expect(DROPDOWNS.map((d) => d.attr)).not.toContain('al_productids');
    expect(productSet()).toContain('type="checkbox"');
  });

  it('no longer renders the free-text box it replaces', () => {
    expect(reviewTemplate).not.toContain('data-ot-hdr="al_products"');
  });

  it('asks for its own list, and for what the case already carries', () => {
    const offered = reviewTemplate.slice(
      reviewTemplate.indexOf('{% fetchxml products %}'),
      reviewTemplate.indexOf('{% endfetchxml %}', reviewTemplate.indexOf('{% fetchxml products %}')),
    );
    expect(offered).toContain('<condition attribute="al_list" operator="eq" value="120910844" />');

    // The held set is a many-to-many, so it cannot be read off the case row.
    const held = reviewTemplate.slice(
      reviewTemplate.indexOf('{% fetchxml case_products %}'),
      reviewTemplate.indexOf('{% endfetchxml %}', reviewTemplate.indexOf('{% fetchxml case_products %}')),
    );
    expect(held).toContain('<link-entity name="al_listoption_al_outcomecase_products"');
    expect(held).toContain('intersect="true"');
  });

  it('tests membership on a delimited id, not a bare substring', () => {
    // The pattern OT Answer Options uses for `locked`. No guid is a substring of another
    // today, and a test that depends on that staying true is one that breaks quietly.
    expect(reviewTemplate).toContain("{% assign okey = '|' | append: o.al_listoptionid | append: '|' %}");
    expect(productSet()).toContain('{% if held_product_ids contains okey %}');
  });

  it('keeps a product the case carries that is no longer offered', () => {
    // Saving any other header field sends the whole set, so an option silently dropped from
    // the list would be silently dropped from the case. The server leaves an
    // already-attached option alone for the same reason.
    const set = productSet();
    expect(set).toContain('{% unless offered_product_ids contains hkey %}');
    expect(set).toContain('checked /> {{ h.al_name | escape }}');
  });

  it('only calls one retired when the offered list actually loaded', () => {
    // Same lesson as the single-choice dropdowns, applied before it could bite: an empty
    // fetch means the catalogue is unavailable, not that everything on the case is retired.
    expect(productSet()).toContain('{% if products_any %} (retired){% endif %}');
  });
});

describe('the portal sends the product set', () => {
  it('reads the group by its boxes, sorted', () => {
    // Sorted so ticking A then B and B then A give the same string; otherwise the change
    // check fires on a set nobody changed and the command rewrites associations that match.
    expect(reviewTemplate).toContain('function setValueOf(container)');
    expect(reviewTemplate).toContain('chosen.sort();');
  });

  it('sends the WHOLE set rather than what moved', () => {
    expect(reviewTemplate).toContain("fields[group.el.getAttribute('data-ot-hdr-set')] = now;");
  });

  it('does not send a set nobody touched', () => {
    expect(reviewTemplate).toContain('if (now === group.initial) { continue; }');
  });

  it('re-baselines the set after a save', () => {
    // Without this the next save re-sends a set that is already recorded. To what was SENT,
    // not to what the boxes hold on reply: a box ticked mid-flight was never sent (900000006,
    // portalHeaderAutosave.test.ts).
    expect(reviewTemplate).toContain('headerSets[m].initial = sentSets[m];');
  });
});

describe('reading the products a case carries', () => {
  it('lists their names on both pages, falling back to the free text', () => {
    for (const template of [reviewTemplate, caseTemplate]) {
      expect(template).toContain('{% for h in case_products.results.entities %}');
      expect(template).toContain("{% assign product_names = product_names | append: '; ' | append: h.al_name %}");
      expect(template).toContain('{% if product_names != "" %}');
    }
    expect(reviewTemplate).toContain("{{ rv['case.al_products'] | escape }}");
    expect(caseTemplate).toContain("{{ c.al_products | default: '—' | escape }}");
  });
});

/**
 * Products became a catalogue of 55 on 2026-09-23 (project owner). Fifty-five stacked
 * checkboxes in a header table cell drag one row taller than the rest of the card put
 * together, so the list is folded behind a summary and given a search box.
 *
 * These pin the three things about that fold which are load-bearing rather than cosmetic.
 */
describe('the Products tick list folds away when the catalogue is long', () => {
  it('asks for more rows than a default page would give it', () => {
    /*
     * The failure this prevents is the quiet one. A fetch that does not say how many rows
     * it wants takes the platform's default page, and a catalogue cut at its page size
     * looks exactly like a catalogue that is complete - the product a checker cannot find
     * is the one they stop looking for.
     */
    const offered = reviewTemplate.slice(
      reviewTemplate.indexOf('{% fetchxml products %}'),
      reviewTemplate.indexOf('{% endfetchxml %}', reviewTemplate.indexOf('{% fetchxml products %}')),
    );

    expect(offered).toContain('<fetch count="200">');
  });

  it('still hangs the whole thing off data-ot-hdr-set, so the collector is unchanged', () => {
    // The fold is presentation. The collector finds [data-ot-hdr-set] and reads the boxes
    // inside it, so nesting them deeper must not move that attribute off the wrapper.
    expect(reviewTemplate).toContain('data-ot-hdr-set="al_productids" data-ot-picker');
    expect(reviewTemplate).toContain("fields[group.el.getAttribute('data-ot-hdr-set')] = now;");
  });

  it('renders the panel OPEN and hides the controls, so a page without script still works', () => {
    /*
     * Progressive enhancement, and the direction matters. The toggle and the search box
     * are rendered `hidden` and unhidden by script; the panel itself is NOT hidden in the
     * markup. A page whose script never runs is therefore the long tick list it always
     * was - tall, but complete and answerable. Hiding the panel in the markup instead
     * would mean a script failure took the Products field away entirely.
     */
    expect(reviewTemplate).toContain('data-ot-picker-toggle aria-expanded="true" hidden');
    expect(reviewTemplate).toContain('data-ot-picker-search placeholder="Search products"');

    const panel = reviewTemplate.indexOf('class="ot-picker__panel" data-ot-picker-panel');
    expect(panel, 'the panel is rendered').toBeGreaterThan(-1);
    const tag = reviewTemplate.slice(panel, reviewTemplate.indexOf('>', panel));
    expect(tag, 'the panel must NOT be hidden in the markup').not.toContain('hidden');

    expect(reviewTemplate).toContain('toggle.hidden = false;');
    expect(reviewTemplate).toContain('open(false);');
  });

  it('never filters away a product that is ticked', () => {
    // The same rule filterTickOptions holds for the Code App panel, and for the same
    // reason: searching is for finding the next product, not for deciding what is chosen.
    const filter = reviewTemplate.slice(
      reviewTemplate.indexOf('function filter()'),
      reviewTemplate.indexOf('toggle.hidden = false;'),
    );

    expect(filter).toContain("var keep = term === '' || text.indexOf(term) !== -1 || (box && box.checked);");
  });
});
