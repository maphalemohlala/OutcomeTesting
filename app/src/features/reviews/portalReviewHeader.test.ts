import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * The review page's header blocks (2026-11-24 redesign): Home > Tax reviews (or AQS reviews) >
 * the case reference, the title "Tax review <reference>", and top right Back to your queue,
 * the full case record and, once submitted, Save as PDF.
 *
 * The blocks render before the content block, so each reads the review itself. Each must
 * apply the content block's GUID shape check before an id reaches a fetchxml (F48: a value
 * that is not a GUID throws a Liquid error into the page), and must read through a loop,
 * never entities[0], which throws on an empty result here.
 */
const block = (name: string) => {
  const start = reviewTemplate.indexOf(`{% block ${name} %}`);
  return start < 0 ? '' : reviewTemplate.slice(start, reviewTemplate.indexOf('{% endblock %}', start));
};

describe('the review page header blocks', () => {
  for (const name of ['breadcrumb', 'title', 'actions']) {
    it(`${name}: reads the review only behind the shape check, by a loop`, () => {
      const b = block(name);
      expect(b).not.toBe('');
      expect(b).toContain("{% if ot_bk_len == 32 and ot_bk_rest == '' %}{% assign ot_bk_by = 'id' %}");
      expect(b).toContain("{% elsif user and ot_bk_clen == 32 and ot_bk_crest == '' %}");
      expect(b).toContain("{% if ot_bk_by != '' %}{% fetchxml ot_hd_rv %}");
      expect(b).toContain('{% for r in ot_hd_rv.results.entities %}');
      expect(b).not.toContain('entities[0]');
      // A type reaches the fetch only when it is one of the two review types.
      expect(b).toContain("{% if ot_bk_type == '' or ot_bk_type == '120910200' or ot_bk_type == '120910201' %}");
    });
  }

  it('all render before the content block', () => {
    const content = reviewTemplate.indexOf('{% block content %}');
    for (const name of ['breadcrumb', 'title', 'actions']) {
      expect(reviewTemplate.indexOf(`{% block ${name} %}`)).toBeLessThan(content);
    }
  });

  it('offers Save as PDF only on a submitted review, wired up by the content block', () => {
    const actions = block('actions');
    expect(actions).toMatch(/\{% if ot_hd_sub %\}<button type="button" class="ot-btn ot-print-action" data-ot-print>/);
    expect(reviewTemplate).toMatch(/\{% if rv\.al_submittedon %\}\s*<script>[\s\S]*?document\.querySelector\('\[data-ot-print\]'\)/);
  });
});

describe('the review summary card', () => {
  it('keeps the two hooks the submit script rewrites without a reload', () => {
    const start = reviewTemplate.indexOf('<section class="ot-card ot-rem-summary ot-review-summary"');
    expect(start).toBeGreaterThan(-1);
    const card = reviewTemplate.slice(start, reviewTemplate.indexOf('</section>', start));
    expect(card).toContain('<span data-ot-review-status>');
    expect(card).toContain('<dd data-ot-submitted-on>');
    expect(reviewTemplate).toContain("document.querySelector('[data-ot-review-status]')");
    expect(reviewTemplate).toContain("document.querySelector('[data-ot-submitted-on]')");
  });
});
