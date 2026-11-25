import { describe, expect, it } from 'vitest';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
import portalCssRaw from '../../../../powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css?raw';
import layoutTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-layout/OT-Layout.webtemplate.source.html?raw';

/**
 * The remediation form gains "Action performed" between Remedial action and Owner (project
 * owner, 2026-09-29). The checker now writes the remedial action; the adviser answers Yes or
 * No and may add a note. Both templates draw the same columns, or the case record and the
 * remediation page would describe one remediation two ways.
 */

const remediation = remediationTemplate.replace(/\r\n/g, '\n');
const caseDetail = caseTemplate.replace(/\r\n/g, '\n');

describe('OT Case Detail', () => {
  it('draws Action performed between Remedial action and Owner', () => {
    expect(caseDetail).toContain(
      '<th scope="col">Remedial action</th>\n                <th scope="col">Action performed</th>\n                <th scope="col">Owner</th>',
    );
  });

  it('spans the check heading across all nine columns', () => {
    expect(caseDetail).toContain('<th scope="colgroup" colspan="9">');
    expect(caseDetail).not.toContain('<th scope="colgroup" colspan="8">');
  });
});

/**
 * OT Remediation draws each action as a row in its check's card since the 2026-11-24
 * redesign, not as a table: the same three things in the same order, as labelled lines.
 */
describe('OT Remediation', () => {
  const open = '<li class="ot-rem-item{% if ot_quiet %} ot-rem-item--quiet{% endif %}">';
  const item = remediation.slice(remediation.indexOf(open), remediation.indexOf('</li>', remediation.indexOf(open)));

  it('collapses only a finished row with nothing to add: never one awaiting sign-off or carrying a note', () => {
    expect(remediation).toContain(open);
    expect(remediation).toContain('{% if latest == null %}{% unless ot_signoff_due %}{% assign ot_quiet = true %}{% endunless %}');
    expect(remediation).toContain("{% if ot_q_note == '' %}{% assign ot_quiet = true %}{% endif %}");
    expect(portalCssRaw).toContain('.ot-rem-item--quiet .ot-rem-item__meta { display: none; }');
  });

  it('draws Action performed between Remedial action and Owner', () => {
    const remedial = item.indexOf('{% assign ot_ra = a.al_remedialaction | strip %}');
    const performed = item.indexOf('>Action performed<');
    const owner = item.indexOf('<dt>Owner</dt>');
    expect(remedial).toBeGreaterThan(-1);
    expect(performed).toBeGreaterThan(remedial);
    expect(owner).toBeGreaterThan(performed);
  });

  it('heads every check with its own card, named once above its actions', () => {
    expect(remediation).toContain('<h3 class="ot-rem-check__title" id="ot-rem-check-{{ forloop.index }}">{{ ot_group | escape }}');
    expect(remediation).toContain('<ul class="ot-rem-list">{{ ot_check_rows }}</ul>');
  });
});

describe.each([
  ['OT Remediation', remediation],
  ['OT Case Detail', caseDetail],
])('%s', (_name, template) => {
  it('reads the two new columns', () => {
    expect(template).toContain('<attribute name="al_remedialaction" />');
    expect(template).toContain('<attribute name="al_actionperformed" />');
  });

  it('shows the checker\'s words where the row has them', () => {
    // Plain words escaped as before; formatted ones (starting with a tag) as cleaned markup.
    expect(template).toContain("{% assign ot_ra = a.al_remedialaction | strip %}");
    expect(template).toContain('{{ ot_ra | escape | newline_to_br }}');
  });
});

describe('the adviser\'s controls on OT Remediation', () => {
  it('offers Yes and No with the solution\'s own values', () => {
    expect(remediation).toContain('value="120910815" data-ot-performed-choice');
    expect(remediation).toContain('value="120910816" data-ot-performed-choice');
  });

  it('keeps the adviser\'s words as an optional note', () => {
    expect(remediation).toContain('Note (optional)');
    expect(remediation).toContain('data-ot-performed-note>{{ a.al_adviserresponse | escape }}</textarea>');
  });

  it('asks for an answer, not words, before signing off a new row', () => {
    expect(remediation).toContain('\'Answer "Action performed" for issue \' + pending[i].number + \' before signing off.\'');
  });

  it('sends the answer with the note', () => {
    expect(remediation).toContain('if (row.performed && row.choice !== null) { payload.al_actionperformed = row.choice; }');
  });

  it('collects both kinds of row in document order', () => {
    expect(remediation).toContain("var cells = document.querySelectorAll('[data-ot-response], [data-ot-performed]');");
  });
});

/**
 * The Yes / No pair reads as two inline options, not a bordered box inside a table cell
 * (T8, 2026-09-29) - the same treatment as the accountability options.
 */
describe('the Action performed pair is styled', () => {
  const css = portalCssRaw.replace(/\r\n/g, '\n');

  it('drops the fieldset border and padding', () => {
    expect(css).toMatch(/\.ot-performed \{[^}]*border: 0;[^}]*\}/);
    expect(css).toMatch(/\.ot-performed \{[^}]*padding: 0;[^}]*\}/);
  });

  it('sets the two labels inline with a small gap', () => {
    expect(css).toMatch(/\.ot-performed \.opt \{[^}]*display: inline-flex;[^}]*gap: var\(--space-1\);[^}]*\}/);
  });

  it('moves the stylesheet version with it', () => {
    const version = /outcome-testing\.css\?v=(\d+)/.exec(layoutTemplate);
    expect(version).not.toBeNull();
    expect(Number(version![1])).toBeGreaterThanOrEqual(24);
  });
});
