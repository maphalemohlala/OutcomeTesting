import { describe, expect, it } from 'vitest';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';

/**
 * The adviser's earlier answers, kept when the T&C Manager sends work back (2026-10-08).
 * SignoffProgressPlugin copies the rejected answer into al_responsehistory before the action
 * reopens; both pages draw it under the current answer, wherever that answer is drawn,
 * including under the box the adviser is reworking it in.
 */

const HISTORY =
  "{% assign ot_hist = a.al_responsehistory | default: '' | strip %}{% if ot_hist != '' %}<div class=\"ot-remedial-form__note ot-response-history\"><strong>Earlier responses</strong><br />{{ ot_hist | escape | newline_to_br }}</div>{% endif %}";

const pages = [
  ['OT Remediation', remediationTemplate.replace(/\r\n/g, '\n'), 8],
  ['OT Case Detail', caseTemplate.replace(/\r\n/g, '\n'), 4],
] as const;

describe.each(pages)('%s', (_name, page, cells) => {
  it('reads the history with the actions', () => {
    expect(page).toContain('<attribute name="al_adviserresponse" />\n');
    expect(page).toMatch(/<attribute name="al_adviserresponse" \/>\s*<attribute name="al_responsehistory" \/>/);
  });

  it('draws it under every cell that shows the answer', () => {
    expect(page.split(HISTORY).length - 1).toBe(cells);
  });
});
