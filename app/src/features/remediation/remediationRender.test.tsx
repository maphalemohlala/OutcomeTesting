import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

import type { RemediationActionRow } from './remediationMapping';

// The page reaches Dataverse through these; the table itself takes its rows as props, so
// they are stubbed rather than the SDK being loaded to import the module.
vi.mock('./useRemediation', () => ({ useRemediation: () => null }));
vi.mock('../../services/commands/completeRemediation', () => ({ completeRemediation: vi.fn() }));
vi.mock('../../services/errors', () => ({ messageForFailure: () => '' }));
vi.mock('../../hooks/useIntentKey', () => ({ useIntentKeys: () => ({ keyFor: () => '' }) }));

const { ActionsTable } = await import('./RemediationPage');

/**
 * What the Code App's remediation page actually draws, as markup.
 *
 * `remediationIssues.test.ts` checks the model the table is built from; this renders the
 * table itself and reads the rows back out, so an action whose issues the model splits but
 * the component never draws as separate rows is caught here rather than on someone's screen.
 */

const DESCRIPTION = [
  'Issues found on the check:',
  '- Fail point: AML - No CRA completed or missing data fields',
  '- Fail point: AML - CRA information not recorded on FactFind',
  '- Fail point: Breach - Any other process breach has been identified',
  '- Fail point: Record Keeping - Concession required but not on file',
  '',
  'Raised automatically when the review was submitted (Tax check: Pass). Review the file and record what you have put right.',
].join('\n');

function action(id: string, description: string): RemediationActionRow {
  return {
    id,
    reference: id,
    description,
    status: 'Open',
    dueOn: '22 Sep 2026',
    completedOn: null,
    triggeredBy: null,
    owner: null,
    rowVersion: null,
    remedialAction: null,
    evidenceReference: null,
    clientContactRequired: null,
    recheckRequired: null,
    changesAdvice: null,
    assignedTo: 'Seed Adviser 01',
  };
}

function draw(actions: RemediationActionRow[]): string {
  return renderToStaticMarkup(
    <ActionsTable actions={actions} busyId={null} onComplete={() => {}} />,
  );
}

/** The `<tr>` blocks, each as its cells' text. */
function rows(markup: string): string[][] {
  return [...markup.matchAll(/<tr[^>]*>(.*?)<\/tr>/gs)].map((row) =>
    [...row[1].matchAll(/<t[dh][^>]*>(.*?)<\/t[dh]>/gs)].map((cell) =>
      cell[1]
        .replace(/<[^>]+>/g, ' ')
        .replace(/&#x27;|&quot;|&amp;/g, "'")
        .replace(/\s+/g, ' ')
        .trim(),
    ),
  );
}

describe('the remediation actions table', () => {
  it('draws one numbered row per fail item, not one paragraph per action', () => {
    const body = rows(draw([action('a1', DESCRIPTION)])).slice(1);

    expect(body[0].slice(0, 2)).toEqual([
      '1',
      'Fail point: AML - No CRA completed or missing data fields',
    ]);
    expect(body[1].slice(0, 2)).toEqual([
      '2',
      'Fail point: AML - CRA information not recorded on FactFind',
    ]);
    expect(body[2].slice(0, 2)).toEqual([
      '3',
      'Fail point: Breach - Any other process breach has been identified',
    ]);
    expect(body[3].slice(0, 2)).toEqual([
      '4',
      'Fail point: Record Keeping - Concession required but not on file',
    ]);
  });

  it('never puts the whole list in one cell', () => {
    for (const row of rows(draw([action('a1', DESCRIPTION)]))) {
      for (const cell of row) {
        expect(cell).not.toContain('Issues found on the check:');
      }
    }
  });

  it('spans the action’s own columns across its items rather than repeating them', () => {
    const markup = draw([action('a1', DESCRIPTION)]);
    // Nine columns follow the issue: remedial action, owner, target date, status, the three
    // form answers, adviser sign-off, and the complete button.
    // React’s server renderer writes the attribute as rowSpan; HTML parses it either way.
    expect([...markup.matchAll(/rowspan="4"/gi)]).toHaveLength(9);

    const body = rows(markup).slice(1);
    expect(body[0]).toHaveLength(11);
    expect(body[1]).toHaveLength(2);
  });

  it('shows the context behind the items once, under them', () => {
    const body = rows(draw([action('a1', DESCRIPTION)])).slice(1);
    const note = body[4];

    expect(note).toHaveLength(1);
    expect(note[0]).toContain('Raised automatically when the review was submitted');
    expect(note[0]).not.toContain('Fail point');
  });

  it('numbers straight through a second action', () => {
    const second = ['Issues found on the check:', '- Tax check outcome: Fail'].join('\n');
    const body = rows(draw([action('a1', DESCRIPTION), action('a2', second)])).slice(1);
    const last = body[body.length - 1];

    expect(last.slice(0, 2)).toEqual(['5', 'Tax check outcome: Fail']);
  });

  it('still draws an action whose description carries no item list', () => {
    const plain = 'Raised automatically when the review was submitted. Review the file.';
    const body = rows(draw([action('a1', plain)])).slice(1);

    expect(body).toHaveLength(1);
    expect(body[0].slice(0, 2)).toEqual(['1', plain]);
  });
});
