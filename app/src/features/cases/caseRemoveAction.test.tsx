import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

import { CASE_STATUSES, type CaseStatus } from '../../types/domain';

vi.mock('../../services/commands/updateCaseDetails', () => ({ updateCaseDetails: vi.fn() }));

const { CaseRemoveAction } = await import('./CaseRemoveAction');
const { canRemoveCase } = await import('./caseRemoval');

const REMOVABLE: CaseStatus[] = [
  'Imported',
  'Validation Failed',
  'Ready for Allocation',
  'Queued',
  'Assigned',
  'Review In Progress',
];

describe('removing a case', () => {
  it('is open only until the check is graded', () => {
    expect(CASE_STATUSES.filter(canRemoveCase)).toEqual(REMOVABLE);
  });

  it('offers the action on a case that can still be removed', () => {
    const html = renderToStaticMarkup(
      <CaseRemoveAction detail={{ id: 'c1', status: 'Queued' }} onRemoved={() => {}} />,
    );
    expect(html).toContain('Remove case');
  });

  it('offers nothing on a graded or already removed case', () => {
    for (const status of ['Submitted', 'Closed', 'No Check Required'] as CaseStatus[]) {
      const html = renderToStaticMarkup(
        <CaseRemoveAction detail={{ id: 'c1', status }} onRemoved={() => {}} />,
      );
      expect(html).toBe('');
    }
  });

  it('says why a Tier 1 high-risk case cannot be removed', () => {
    const html = renderToStaticMarkup(
      <CaseRemoveAction
        detail={{ id: 'c1', status: 'Queued', checklist: { items: ['Tax Check', 'High Risk Item 1'] } }}
        onRemoved={() => {}}
      />,
    );
    expect(html).not.toContain('Remove case');
    expect(html).toContain('Tier 1');
  });

  it('still offers removal when only High Risk Item 2 is ticked', () => {
    const html = renderToStaticMarkup(
      <CaseRemoveAction
        detail={{ id: 'c1', status: 'Queued', checklist: { items: ['High Risk Item 2'] } }}
        onRemoved={() => {}}
      />,
    );
    expect(html).toContain('Remove case');
  });
});
