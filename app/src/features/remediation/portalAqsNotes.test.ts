import { describe, expect, it } from 'vitest';
import aqsNotes from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-aqs-notes/OT-AQS-Notes.webtemplate.source.html?raw';
import taxNotes from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-tax-notes/OT-Tax-Notes.webtemplate.source.html?raw';
import aqsNotesMeta from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-aqs-notes/OT-AQS-Notes.webtemplate.yml?raw';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import caseDetail from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';

/**
 * The AQS checker's Case Notes and Even Better If, shown to the adviser (project owner,
 * 2026-09-30). Read as text, not executed: these pin the markup the panel depends on.
 */

/** The template with its Liquid comments removed, so a comment cannot satisfy a test. */
const live = aqsNotes.replace(/\{% comment %\}[\s\S]*?\{% endcomment %\}/g, '');

describe('the AQS check notes panel', () => {
  it('is a web template of its own, named as the pages include it', () => {
    expect(aqsNotesMeta).toMatch(/^adx_name: OT AQS Notes\r?$/m);
    expect(aqsNotesMeta).toMatch(/^adx_webtemplateid: [0-9a-f-]{36}\r?$/m);
  });

  it('reads the two AQS note questions and nothing else', () => {
    expect(live).toContain('<value>Q-GR-03</value>');
    expect(live).toContain('<value>Q-GR-04</value>');
    expect(live.match(/<value>/g)).toHaveLength(2);
  });

  it('reads only submitted AQS checks on this case', () => {
    // A draft is work in progress; a half-written note must not reach the adviser as a finding.
    expect(live).toContain('<condition attribute="al_reviewtype" operator="eq" value="120910201" />');
    expect(live).toContain('<condition attribute="al_submittedon" operator="not-null" />');
    expect(live).toContain(
      '<condition attribute="al_outcomecaseid" operator="eq" value="{{ case_id | xml_escape }}" />',
    );
  });

  it('draws nothing when both boxes are empty', () => {
    // An empty panel headed "AQS check notes" reads as a fault.
    const gate = live.indexOf('{% if ot_aqs_note_count > 0 %}');
    expect(gate).toBeGreaterThan(-1);
    expect(live.indexOf('<section')).toBeGreaterThan(gate);
    expect(live).toContain("{% if ot_note_text != '' or ot_note_rich != '' %}");
  });

  it('labels each note, and escapes plain text while keeping its line breaks', () => {
    expect(live).toContain('AQS check notes');
    expect(live).toContain('Case Notes');
    expect(live).toContain('Even Better If');
    expect(live).toContain('{{ ot_note_text | escape | newline_to_br }}');
  });

  it('dates each note when the case carries more than one submitted AQS check', () => {
    expect(live).toContain('AQS check submitted');
    expect(live).toContain("{% if ot_aqs_multi %}");
  });

  it('offers nothing to edit', () => {
    expect(live).not.toMatch(/<(input|textarea|select|button)\b/);
    expect(live).not.toContain('contenteditable');
  });
});

describe('where the AQS notes appear', () => {
  it('sits on the Remediation page directly after the Tax notes, above the actions', () => {
    const tax = remediation.indexOf("{% include 'OT Tax Notes' case_id: c.al_outcomecaseid %}");
    const aqs = remediation.indexOf("{% include 'OT AQS Notes' case_id: c.al_outcomecaseid %}");
    // The heading id also appears in the page's no-case branch, higher up; the one that
    // matters is the actions card that follows the notes.
    const actions = remediation.indexOf('id="ot-remediation-heading"', tax);
    expect(tax).toBeGreaterThan(-1);
    expect(aqs).toBeGreaterThan(tax);
    expect(actions).toBeGreaterThan(aqs);
  });

  it('sits on the case record above Remediation and escalation', () => {
    const aqs = caseDetail.indexOf("{% include 'OT AQS Notes' case_id: c.al_outcomecaseid %}");
    const remediationSection = caseDetail.indexOf('<section aria-labelledby="ot-case-remediation">');
    expect(aqs).toBeGreaterThan(-1);
    expect(remediationSection).toBeGreaterThan(aqs);
  });
});

describe('a note whose other column is empty', () => {
  // Found on DEV, 2026-09-30: a plain-text note has no rich text, the column comes back null,
  // and on this site null != '' is TRUE. Both panels then took the rich-text branch and drew
  // an empty box where the note should be - the Tax panel had done so for every Tax "Case
  // notes" since it was built. Reading each column through default: '' makes a missing
  // value the empty string the comparisons expect.
  it.each([
    ['OT AQS Notes', aqsNotes],
    ['OT Tax Notes', taxNotes],
  ])('%s treats a missing column as empty before testing it', (_name, template) => {
    const live = template.replace(/\{% comment %\}[\s\S]*?\{% endcomment %\}/g, '');
    const reads = live.match(/\{% assign ot_note_(text|rich) = [^%]*%\}/g) ?? [];
    expect(reads.length).toBeGreaterThanOrEqual(4);
    for (const read of reads) {
      expect(read.endsWith("| default: '' | strip %}"), read).toBe(true);
    }
  });
});
