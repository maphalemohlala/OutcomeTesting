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

  /**
   * Every decision, not the latest (reported 2026-10-08: once the coach approved the reworked
   * remediation, what they had asked for when they sent it back was gone from the form). The
   * emailed document draws the same trail - RemediationDocument.SupervisorTrail.
   */
  it("draws every supervisor decision, each with its notes, in the form's sign-off row", () => {
    const row = page.slice(page.indexOf('Supervisor sign-off'));
    const cell = row.slice(row.indexOf('<td'), row.indexOf('</td>'));

    expect(cell).not.toContain('latest_supervisor');
    expect(cell).toContain('{{ ot_supervisor_trail }}');
    expect(page).toMatch(/\{% for s in \w+\.results\.entities %\}\s*\{% assign ot_tn = s\.al_notes \| default: '' \| strip %\}/);
    // Oldest first: each entry is put in front of the ones already drawn, since the read is newest first.
    expect(page).toContain('{{ ot_entry }}{% if ot_supervisor_trail != \'\' %}{{ ot_supervisor_trail }}{% endif %}');
  });
});
