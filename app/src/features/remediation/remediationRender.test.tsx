import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import type { RemediationActionRow } from './remediationMapping';

// The page reaches Dataverse through these; the table itself takes its rows as props, so
// they are stubbed rather than the SDK being loaded to import the module.
vi.mock('./useRemediation', () => ({ useRemediation: () => null }));
vi.mock('../../services/commands/completeRemediation', () => ({ completeRemediation: vi.fn() }));
vi.mock('../../services/errors', () => ({ messageForFailure: () => '' }));
vi.mock('../../hooks/useIntentKey', () => ({ useIntentKeys: () => ({ keyFor: () => '' }) }));

const { ActionsTable, RemediationDetails, RemediationFormBlock } = await import(
  './RemediationPage',
);

/**
 * What the Code App's remediation page actually draws, as markup.
 *
 * `remediationIssues.test.ts` checks the model the table is built from; this renders the
 * table itself and reads the rows back out, so an action whose issues the model splits but
 * the component never draws as separate rows is caught here rather than on someone's screen.
 */

const DESCRIPTION = [
  'Issues found on the check:',
  '- AML - No CRA completed or missing data fields',
  '- AML - CRA information not recorded on FactFind',
  '- Breach - Any other process breach has been identified',
  '- Record Keeping - Concession required but not on file',
  '',
  'The checker recorded: no CRA on file',
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
    createdOn: '2026-09-08T09:00:00Z',
    clockStartedOn: null,
    completedOnRaw: null,
  };
}

function draw(actions: RemediationActionRow[]): string {
  return renderToStaticMarkup(
    <ActionsTable
      actions={actions}
      signoffs={[]}
      adviserName={null}
    />,
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
      'AML - No CRA completed or missing data fields',
    ]);
    expect(body[1].slice(0, 2)).toEqual([
      '2',
      'AML - CRA information not recorded on FactFind',
    ]);
    expect(body[2].slice(0, 2)).toEqual([
      '3',
      'Breach - Any other process breach has been identified',
    ]);
    expect(body[3].slice(0, 2)).toEqual([
      '4',
      'Record Keeping - Concession required but not on file',
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
    // Six columns follow the issue: remedial action, owner, target date, status, age and
    // sign-off. The four that carried the adviser's answers are gone - they are the form
    // block under the table now - and so is the complete button: every action is completed
    // and signed off on the portal, so this table reports and never writes (2026-09-10).
    // React’s server renderer writes the attribute as rowSpan; HTML parses it either way.
    expect([...markup.matchAll(/rowspan="4"/gi)]).toHaveLength(6);

    const body = rows(markup).slice(1);
    expect(body[0]).toHaveLength(8);
    expect(body[1]).toHaveLength(2);
  });

  it('draws no context row under the items at all', () => {
    // The checker's observation is not shown (project owner, 2026-09-10); the four item
    // rows and the header are the whole table.
    const body = rows(draw([action('a1', DESCRIPTION)])).slice(1);

    expect(body).toHaveLength(4);
    expect(draw([action('a1', DESCRIPTION)])).not.toContain('The checker recorded');
  });

  it('never repeats the standing sentence under the rows', () => {
    // It said the same thing under every row of every case, and both halves of it are on
    // the page in their own right now (project owner, 2026-09-10).
    const markup = draw([action('a1', DESCRIPTION)]);

    expect(markup).not.toContain('Raised automatically when the review was submitted');
    expect(markup).not.toContain('Review the file and record what you have put right');
  });

  it('numbers straight through a second action', () => {
    const second = [
      'Issues found on the check:',
      '- Record Keeping - No client agreement or client acceptance in place',
    ].join('\n');
    const body = rows(draw([action('a1', DESCRIPTION), action('a2', second)])).slice(1);
    const last = body[body.length - 1];

    expect(last.slice(0, 2)).toEqual([
      '5',
      'Record Keeping - No client agreement or client acceptance in place',
    ]);
  });

  it('still draws an action whose description carries no item list', () => {
    // What dropoutcomeactions leaves behind when it strips a case's only action: the
    // checker's words and nothing else.
    const stripped = [
      'The checker recorded: failed',
      '',
      'Raised automatically when the review was submitted (Tax check: Fail). Review the file.',
    ].join('\n');
    const body = rows(draw([action('a1', stripped)])).slice(1);

    expect(body).toHaveLength(1);
    expect(body[0].slice(0, 2)).toEqual(['1', '—']);
  });

  it('falls back to a dash when a description says nothing at all', () => {
    const body = rows(draw([action('a1', 'Raised automatically when the review was submitted.')])).slice(1);

    expect(body).toHaveLength(1);
    expect(body[0][1]).toBe('—');
  });
});

function details(outcome: string | null): string {
  return renderToStaticMarkup(
    <MemoryRouter>
      <RemediationDetails
        outcomeCase={{
          reference: 'IO-SEED-TAX-01',
          status: 'Awaiting Remediation',
          clientName: 'Seed Client TAX01',
          adviserName: 'Seed Adviser 01',
        }}
        outcome={outcome}
        caseId={'case-1'}
      />
    </MemoryRouter>,
  );
}

describe('the remediation details', () => {
  it('shows the outcome above the table, not as one of its numbered rows', () => {
    const markup = details('Tax check: Insufficient evidence');

    expect(markup).toContain('<dt>Outcome</dt>');
    expect(markup).toContain('Tax check: Insufficient evidence');
    expect(rows(draw([action('a', DESCRIPTION)])).flat()).not.toContain(
      'Tax check: Insufficient evidence',
    );
  });

  it('names the client, the adviser and a way back to the case, as the portal does', () => {
    const markup = details('Tax check: Insufficient evidence');

    expect(markup).toContain('<dt>Client</dt>');
    expect(markup).toContain('Seed Client TAX01');
    expect(markup).toContain('<dt>Adviser</dt>');
    expect(markup).toContain('Seed Adviser 01');
    expect(markup).toContain('Open the full case record');
  });

  it("draws the four fields in the portal's order", () => {
    // Client, Adviser, Outcome, Case - across the card, not stacked down it. The order is
    // the portal's and is what makes the two screens read as the same one.
    const markup = details('Tax check: Insufficient evidence');
    const labels = [...markup.matchAll(/<dt>([^<]+)<\/dt>/g)].map((m) => m[1]);

    expect(labels).toEqual(['Client', 'Adviser', 'Outcome', 'Case']);
  });

  it('draws nothing at all when there is neither a case nor an outcome', () => {
    expect(
      renderToStaticMarkup(
        <MemoryRouter>
          <RemediationDetails outcomeCase={null} outcome={null} caseId={undefined} />
        </MemoryRouter>,
      ),
    ).toBe('');
  });
});

describe('the remediation form block', () => {
  it('draws the eight fields of the form’s last block, not four columns per row', () => {
    const markup = renderToStaticMarkup(
      <RemediationFormBlock actions={[action('a1', DESCRIPTION)]} outcomes={[]} signoffs={[]} />,
    );

    for (const label of [
      'Client contact required?',
      'Recheck required?',
      'Do the remedial actions change the advice?',
      'All remedial actions checked and approved?',
      'Regraded outcome',
      'Date',
      'Supervisor sign-off',
      'Adviser sign-off',
    ]) {
      expect(markup).toContain(label);
    }

    // And none of them is a column any more.
    const header = rows(draw([action('a1', DESCRIPTION)]))[0];
    expect(header).not.toContain('Client contact');
    expect(header).not.toContain('Recheck');
    expect(header).not.toContain('Changes advice');
  });
});
