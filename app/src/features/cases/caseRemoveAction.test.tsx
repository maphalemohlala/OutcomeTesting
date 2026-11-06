import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

import { CASE_STATUSES, type CaseStatus } from '../../types/domain';

vi.mock('../../services/commands/updateCaseDetails', () => ({ updateCaseDetails: vi.fn() }));

const { CaseRemoveAction, canRemoveCase } = await import('./CaseRemoveAction');

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
});
