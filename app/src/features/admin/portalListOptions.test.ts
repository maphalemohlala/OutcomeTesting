import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
import listOptionPermission from '../../../../powerpages/outcome-testing---outcometesting/table-permissions/List-Option---read.tablepermission.yml?raw';
import { LIST_PRODUCT_SOLUTION_TYPE, MIGRATED_LISTS } from './listOptions';

/**
 * The portal's Product / solution type dropdown is drawn from al_listoption rows (AD-187).
 *
 * It used to be five <option> tags written out by hand, which is the reason a new option
 * could be added to the choice column in Dataverse and still not be selectable by anybody.
 * These pin that it is now data, and the three things it must not do.
 */

const PRODUCT = MIGRATED_LISTS[0];

/** The Product / solution type cell of the editable header, tags and Liquid intact. */
function headerSelect(): string {
  const start = reviewTemplate.indexOf('data-ot-hdr="al_producttypeid"');
  expect(start, 'the header select should be bound to the lookup').toBeGreaterThan(-1);
  const open = reviewTemplate.lastIndexOf('<select', start);
  const end = reviewTemplate.indexOf('</select>', start);
  return reviewTemplate.slice(open, end);
}

describe('the portal draws the options from the table', () => {
  it('no longer writes any of the five option values out by hand', () => {
    // THE regression this exists to catch. A hard-coded value here is an option somebody can
    // choose that the management page does not know about, or an option the page offers that
    // the portal silently will not.
    const select = headerSelect();
    for (const value of [120910520, 120910521, 120910522, 120910523, 120910524]) {
      expect(select, `option value ${value}`).not.toContain(String(value));
    }
    expect(select).not.toContain('Accumulation Pension');
    expect(select).not.toContain('No change reviews');
  });

  it('loops the rows the fetch returned', () => {
    expect(headerSelect()).toContain('{% for po in product_types.results.entities %}');
    expect(headerSelect()).toContain('{{ po.al_name | escape }}');
  });

  it('asks for exactly the one list, by the value the app and the plug-ins use', () => {
    // All four lists share one table, so a fetch without this filter would offer sample
    // sources in the product type dropdown.
    const fetch = reviewTemplate.slice(
      reviewTemplate.indexOf('{% fetchxml product_types %}'),
      reviewTemplate.indexOf('{% endfetchxml %}', reviewTemplate.indexOf('{% fetchxml product_types %}')),
    );
    expect(fetch).toContain('<entity name="al_listoption">');
    expect(fetch).toContain(
      `<condition attribute="al_list" operator="eq" value="${LIST_PRODUCT_SOLUTION_TYPE}" />`,
    );
    expect(PRODUCT.value).toBe(LIST_PRODUCT_SOLUTION_TYPE);
  });

  it('offers only the options in force, on the same half-open window as everything else', () => {
    const fetch = reviewTemplate.slice(
      reviewTemplate.indexOf('{% fetchxml product_types %}'),
      reviewTemplate.indexOf('{% endfetchxml %}', reviewTemplate.indexOf('{% fetchxml product_types %}')),
    );
    // from <= day, and day < to. `gt` rather than `on-or-after`: an option retired today is
    // retired today, and the server refuses it either way.
    expect(fetch).toContain('<condition attribute="al_effectivefrom" operator="on-or-before" value="{{ product_day }}" />');
    expect(fetch).toContain('<condition attribute="al_effectiveto" operator="gt" value="{{ product_day }}" />');
    expect(fetch).toContain('<condition attribute="al_effectivefrom" operator="null" />');
    expect(fetch).toContain('<condition attribute="al_effectiveto" operator="null" />');
  });

  it('asks as of today, not as of the review’s submission day', () => {
    // as_of answers "which version was this answered against", a question about history.
    // This answers "what may be chosen now". Reusing as_of would have offered a checker the
    // options that existed when the review was submitted.
    expect(reviewTemplate).toContain("{% assign product_day = now | date: 'yyyy-MM-dd' %}");
  });

  it('keeps a retired option the case actually holds, marked as retired', () => {
    // Otherwise the cell renders as "—" and a checker reads it as a field to fill in. The
    // Code App's panel makes the same allowance; choicesIncludingHeld is its half.
    const select = headerSelect();
    expect(select).toContain('{% unless held_is_offered %}');
    expect(select).toContain('(retired)');
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

describe('reading a case’s product type on the portal', () => {
  it('prefers the chosen option and falls back to the choice column, on both pages', () => {
    // Until every case is backfilled, reading only the lookup would blank this on the older
    // ones - which is the same precedence the app and the export apply.
    expect(reviewTemplate).toContain('{% if h_producttypeid.name %}');
    expect(reviewTemplate).toContain('{{ h_producttype.label | escape }}');
    expect(caseTemplate).toContain('{% if c.al_producttypeid.name %}');
    expect(caseTemplate).toContain("{{ c.al_productsolutiontype.label | default: '—' | escape }}");
  });

  it('asks for the lookup on both pages, or there would be nothing to read', () => {
    expect(reviewTemplate).toContain('<attribute name="al_producttypeid" />');
    expect(caseTemplate).toContain('<attribute name="al_producttypeid" />');
  });
});
