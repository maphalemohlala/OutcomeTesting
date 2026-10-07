import { describe, expect, it } from 'vitest';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';

/**
 * The supervisor's sign-off notes on the portal's remediation page and case record (reported
 * 2026-10-07: "the notes need to be shown on the remedial details and case details as well").
 * They went out in the adviser's email and were drawn nowhere - neither page even read them.
 *
 * Read through `default: ''` before comparing: this site's Liquid treats null != '' as true.
 */

const pages = [
  ['OT Remediation', remediationTemplate.replace(/\r\n/g, '\n')],
  ['OT Case Detail', caseTemplate.replace(/\r\n/g, '\n')],
] as const;

describe.each(pages)('%s', (_name, page) => {
  it('reads the notes with every sign-off it fetches', () => {
    const blocks = [...page.matchAll(/<entity name="al_signoff">([\s\S]*?)<\/entity>/g)].map((m) => m[1]);
    expect(blocks.length).toBeGreaterThan(0);
    for (const block of blocks) {
      if (block.includes('al_signoffdecision')) {
        expect(block).toContain('<attribute name="al_notes" />');
      }
    }
  });

  it("draws the latest decision's notes beside it on each action", () => {
    expect(page).toContain("{% assign ot_sn = latest.al_notes | default: '' | strip %}");
    expect(page).not.toMatch(
      /\{\{ latest\.al_signoffdecision\.label \| escape \}\} \{\{ latest\.al_signedoffon \| date: 'dd MMM yyyy' \}\}\s*\n\s*\{% elsif/,
    );
  });

  it("draws the supervisor's latest notes in the form's sign-off row", () => {
    expect(page).toContain("{% assign ot_supervisor_notes = latest_supervisor.al_notes | default: '' | strip %}");
  });
});
