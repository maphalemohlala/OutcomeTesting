import { describe, expect, it } from 'vitest';
import { versionsAsOf } from './questionSchedule';

const today = new Date('2026-11-16T12:00:00Z');
const version = (number: number, from: string, to: string | null = null) => ({
  _al_questionid_value: 'q',
  al_versionnumber: number,
  al_effectivefrom: from,
  al_effectiveto: to,
});

describe('a retirement still to come', () => {
  it('is its own note, so it does not count as a scheduled change', () => {
    const shown = versionsAsOf([version(1, '2026-01-01', '2026-12-01')], today).get('q');

    expect(shown?.version.al_versionnumber).toBe(1);
    expect(shown?.retires).toBe('Retires on 01 Dec 2026');
    expect(shown?.scheduled).toBeNull();
  });

  it('is shown beside a scheduled change that carries it', () => {
    const shown = versionsAsOf(
      [version(1, '2026-01-01', '2026-11-20'), version(2, '2026-11-20', '2026-12-01')],
      today,
    ).get('q');

    expect(shown?.scheduled).toBe('Changes on 20 Nov 2026');
    expect(shown?.retires).toBe('Retires on 01 Dec 2026');
  });

  it('is not shown once the question has retired', () => {
    const shown = versionsAsOf([version(1, '2026-01-01', '2026-11-16')], today).get('q');

    expect(shown?.retires).toBeNull();
    expect(shown?.scheduled).toBeNull();
  });

  it('leaves a question with no end date unflagged', () => {
    const shown = versionsAsOf([version(1, '2026-01-01')], today).get('q');

    expect(shown?.retires).toBeNull();
    expect(shown?.scheduled).toBeNull();
  });
});
