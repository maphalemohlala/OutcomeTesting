import { describe, expect, it } from 'vitest';
import { summarise, type WorkloadReview } from './workload';

const NOW = new Date('2026-09-23T12:00:00Z');

function review(checker: string | null, status: string, extra: Partial<WorkloadReview> = {}): WorkloadReview {
  return { checker, status, startedOn: null, submittedOn: null, openedOn: '2026-09-21', ...extra };
}

describe('summarise (AR-01: individual and overall workloads)', () => {
  it('counts each checker\'s allocated, in-progress and recently completed checks', () => {
    const load = summarise([
      review('Ada', 'Assigned'),
      review('Ada', 'Review In Progress'),
      review('Ada', 'Submitted', { submittedOn: '2026-09-10' }),
      review('Bo', 'Submitted', { submittedOn: '2026-07-01' }),
    ], 0, NOW);

    const ada = load.checkers.find((c) => c.checker === 'Ada')!;
    expect(ada).toMatchObject({ allocated: 1, inProgress: 1, completedLast30Days: 1 });
    const bo = load.checkers.find((c) => c.checker === 'Bo')!;
    expect(bo.completedLast30Days).toBe(0);
  });

  it('ages the oldest open check in working days', () => {
    // Monday 21 to Wednesday 23 September is three working days, the first day counting.
    const load = summarise([review('Ada', 'Assigned', { openedOn: '2026-09-21' })], 0, NOW);
    expect(load.checkers[0].oldestOpenWorkingDays).toBe(3);
  });

  it('totals the team, including what is waiting to be allocated', () => {
    const load = summarise([
      review('Ada', 'Assigned'),
      review('Bo', 'Review In Progress'),
      review('Bo', 'Submitted', { submittedOn: '2026-09-02' }),
      review('Bo', 'Submitted', { submittedOn: '2026-08-29' }),
    ], 4, NOW);
    expect(load.totals).toEqual({ awaitingAllocation: 4, allocated: 1, inProgress: 1, completedThisMonth: 1 });
  });

  it('files a check with no holder under "Unassigned" rather than dropping it', () => {
    expect(summarise([review(null, 'Assigned')], 0, NOW).checkers[0].checker).toBe('Unassigned');
  });

  it('lists the busiest checker first', () => {
    const load = summarise([review('Bo', 'Assigned'), review('Ada', 'Assigned'), review('Ada', 'Assigned')], 0, NOW);
    expect(load.checkers.map((c) => c.checker)).toEqual(['Ada', 'Bo']);
  });
});
