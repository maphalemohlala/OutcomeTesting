import { describe, expect, it } from 'vitest';
import { hasEnded, notStarted } from '../../lib/effectiveWindow';
import { hasText } from './notificationTemplates';
import { versionsAsOf } from './questionSchedule';

const today = new Date('2026-11-06T12:00:00Z');
const version = (number: number, from: string) => ({
  _al_questionid_value: 'q',
  al_versionnumber: number,
  al_effectivefrom: from,
});

describe('the version a question shows', () => {
  it('stays on the wording reviewers answer until a forward-dated edit starts', () => {
    const shown = versionsAsOf([version(1, '2026-01-01'), version(2, '2026-12-01')], today).get('q');

    expect(shown?.version.al_versionnumber).toBe(1);
    expect(shown?.scheduled).toBe('Changes on 01 Dec 2026');
  });

  it('moves to the new wording on the day it starts', () => {
    const shown = versionsAsOf([version(1, '2026-01-01'), version(2, '2026-11-06')], today).get('q');

    expect(shown?.version.al_versionnumber).toBe(2);
    expect(shown?.scheduled).toBeNull();
  });

  it('says when a question added ahead of time will start', () => {
    const shown = versionsAsOf([version(1, '2026-12-01')], today).get('q');

    expect(shown?.version.al_versionnumber).toBe(1);
    expect(shown?.scheduled).toBe('Starts on 01 Dec 2026');
  });
});

describe('retired and not started are different things', () => {
  it('reads a future start as not started and not ended', () => {
    expect(notStarted('2026-12-01', today)).toBe(true);
    expect(hasEnded(null, today)).toBe(false);
  });

  it('reads an end date of today as ended, and a later one as not yet', () => {
    expect(hasEnded('2026-11-06', today)).toBe(true);
    expect(hasEnded('2026-11-07', today)).toBe(false);
  });
});

describe('an editor left empty', () => {
  it('is empty even with the markup a cleared editor leaves', () => {
    expect(hasText('<p><br></p>')).toBe(false);
    expect(hasText('<br>')).toBe(false);
    expect(hasText('&nbsp;')).toBe(false);
  });

  it('is not empty once it holds a word', () => {
    expect(hasText('<p>Dear adviser</p>')).toBe(true);
  });
});
