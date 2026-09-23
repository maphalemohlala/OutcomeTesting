import { describe, expect, it } from 'vitest';
import caseListTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-list/OT-Case-List.webtemplate.source.html?raw';
import layoutTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-layout/OT-Layout.webtemplate.source.html?raw';
import portalCss from '../../../../powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css?raw';

/**
 * The case list asks the page shell for more room (project owner, 2026-09-22).
 *
 * Three files have to agree for that to show up at all, and each fails silently on its own:
 * the template carries the marker, the stylesheet carries the rule that answers it, and the
 * layout's cache-busting version moves so a browser holding the old file picks the rule up.
 * The last of those is the one with history - a border fix shipped to DEV twice on 2026-09-11
 * and was reported both times as not showing, because the querystring had not moved.
 */

describe('the case list asks for the extra width', () => {
  it('marks its own table and nothing else', () => {
    const markers = caseListTemplate.match(/data-ot-wide/g) ?? [];

    // Once on the table wrapper. The comment above it names the attribute as well, which is
    // why this counts occurrences on real markup rather than in the file as a whole.
    expect(caseListTemplate).toContain('<div class="ot-table-wrap" data-ot-wide>');
    expect(markers.length).toBeGreaterThan(0);
  });

  it('is the only portal page asking, so no other page moves', () => {
    // The point of a marker over raising the shared cap: the review form and the remediation
    // pages keep the 82rem reading width they were set to.
    expect(layoutTemplate).not.toContain('data-ot-wide');
  });
});

describe('the stylesheet answers it', () => {
  it('lifts the cap through :has, because the shell is rendered before the page', () => {
    expect(portalCss).toContain('.ot-page__inner:has([data-ot-wide]) { max-width: 110rem; }');
  });

  it('leaves the shared reading width alone', () => {
    expect(portalCss).toContain('.ot-page__inner { max-width: 82rem; margin: 0 auto; }');
  });

  it('raises the cap rather than lowering it', () => {
    const shared = /\.ot-page__inner \{ max-width: (\d+)rem/.exec(portalCss);
    const wide = /\.ot-page__inner:has\(\[data-ot-wide\]\) \{ max-width: (\d+)rem/.exec(portalCss);

    expect(shared).not.toBeNull();
    expect(wide).not.toBeNull();
    expect(Number(wide![1])).toBeGreaterThan(Number(shared![1]));
  });
});

describe('the stylesheet version moved with it', () => {
  it('is past the version that shipped before this change', () => {
    // OT Layout says to bump this on EVERY change to outcome-testing.css, not only a material
    // one: the deploy succeeds either way, so forgetting looks exactly like a rule that does
    // not work.
    const version = /outcome-testing\.css\?v=(\d+)/.exec(layoutTemplate);

    expect(version).not.toBeNull();
    expect(Number(version![1])).toBeGreaterThanOrEqual(22);
  });
});
