import { describe, expect, it } from 'vitest';
import { caseKeyDates } from './caseKeyDates';

describe('the case key dates', () => {
  it('orders check dates and the due date as they happened, and says how late a submission was', () => {
    expect(
      caseKeyDates([{ type: 'AQS', startedOn: '23 Sep 2026', submittedOn: '29 Sep 2026' }], '26 Sep 2026'),
    ).toEqual([
      { date: '23 Sep 2026', label: 'AQS check started' },
      { date: '26 Sep 2026', label: 'Case due' },
      { date: '29 Sep 2026', label: 'AQS check submitted, 3 days after the due date' },
    ]);
  });

  it('says nothing about lateness for a submission on time, and orders across months', () => {
    expect(
      caseKeyDates([{ type: 'Tax', startedOn: '30 Sep 2026', submittedOn: '01 Oct 2026' }], '02 Oct 2026').map(
        (d) => d.label,
      ),
    ).toEqual(['Tax check started', 'Tax check submitted', 'Case due']);
  });

  it('leaves out what is missing or unreadable', () => {
    expect(caseKeyDates([{ type: 'AQS', startedOn: null, submittedOn: 'soon' }], null)).toEqual([]);
  });

  it('counts one day late in the singular', () => {
    expect(caseKeyDates([{ type: 'AQS', startedOn: null, submittedOn: '27 Sep 2026' }], '26 Sep 2026')[1].label).toBe(
      'AQS check submitted, 1 day after the due date',
    );
  });
});
