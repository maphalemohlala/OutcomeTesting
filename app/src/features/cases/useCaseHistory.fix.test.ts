import { describe, expect, it, vi } from 'vitest';

const CASE_ID = '11111111-1111-1111-1111-111111111111';

const auditGetAll = vi.fn();
const empty = () => Promise.resolve({ success: true, data: [] });

vi.mock('../../generated', () => ({
  Al_auditeventsService: { getAll: (o: unknown) => auditGetAll(o) },
  Al_outcomesService: { getAll: empty },
  Al_remediationactionsService: { getAll: empty },
  Al_reviewinstancesService: { getAll: empty },
  Al_signoffsService: { getAll: empty },
}));

const { readCaseHistory, toHistoryEntry } = await import('./useCaseHistory');

describe('the case history', () => {
  it('reads every page of the audit trail, not just the first 500', async () => {
    const page1 = Array.from({ length: 5000 }, (_, i) => ({ al_auditeventid: `a${i}` }));
    const page2 = [{ al_auditeventid: 'last' }];
    auditGetAll.mockReset();
    auditGetAll
      .mockResolvedValueOnce({ success: true, data: page1, skipToken: 'next' })
      .mockResolvedValueOnce({ success: true, data: page2 });

    const result = await readCaseHistory(CASE_ID);

    expect(result.success).toBe(true);
    expect(result.data).toHaveLength(5001);
    expect(auditGetAll).toHaveBeenCalledTimes(2);
    expect(auditGetAll.mock.calls[0][0]).not.toHaveProperty('top');
    expect(auditGetAll.mock.calls[1][0]).toMatchObject({ skipToken: 'next' });
  });

  it('shows when on the UK clock, whatever the browser zone', () => {
    vi.stubEnv('TZ', 'Asia/Tokyo');
    try {
      const entry = toHistoryEntry({
        al_auditeventid: 'e1',
        al_occurredon: '2026-11-16T22:30:00Z',
      } as never);
      expect(entry.occurredOn).toBe('16 Nov 2026, 22:30');
    } finally {
      vi.unstubAllEnvs();
    }
  });

  it('leaves an unreadable time empty', () => {
    expect(toHistoryEntry({ al_auditeventid: 'e1', al_occurredon: 'nonsense' } as never).occurredOn).toBeNull();
  });
});
