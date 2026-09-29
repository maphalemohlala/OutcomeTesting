import { describe, expect, it } from 'vitest';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';

/**
 * The remediation form gains "Action performed" between Remedial action and Owner (project
 * owner, 2026-09-29). The checker now writes the remedial action; the adviser answers Yes or
 * No and may add a note. Both templates draw the same columns, or the case record and the
 * remediation page would describe one remediation two ways.
 */

const remediation = remediationTemplate.replace(/\r\n/g, '\n');
const caseDetail = caseTemplate.replace(/\r\n/g, '\n');

describe.each([
  ['OT Remediation', remediation],
  ['OT Case Detail', caseDetail],
])('%s', (_name, template) => {
  it('draws Action performed between Remedial action and Owner', () => {
    expect(template).toContain(
      '<th scope="col">Remedial action</th>\n                <th scope="col">Action performed</th>\n                <th scope="col">Owner</th>',
    );
  });

  it('spans the check heading across all nine columns', () => {
    expect(template).toContain('<th scope="colgroup" colspan="9">');
    expect(template).not.toContain('<th scope="colgroup" colspan="8">');
  });

  it('reads the two new columns', () => {
    expect(template).toContain('<attribute name="al_remedialaction" />');
    expect(template).toContain('<attribute name="al_actionperformed" />');
  });

  it('shows the checker\'s words where the row has them', () => {
    expect(template).toContain('{{ a.al_remedialaction | escape | newline_to_br }}');
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
