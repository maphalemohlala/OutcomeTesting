import { describe, expect, it } from 'vitest';
import { recordDisabled } from './failAccountability';

describe('the Record accountability button', () => {
  it('stays off after a save until the re-read lands, so a second click cannot write twice', () => {
    // Just recorded: the draft still differs from the row the page holds, but the write is done.
    expect(recordDisabled({ changed: true, busy: false, awaitingReread: true })).toBe(true);
  });

  it('is on for an unsaved change, and off while saving or with nothing changed', () => {
    expect(recordDisabled({ changed: true, busy: false, awaitingReread: false })).toBe(false);
    expect(recordDisabled({ changed: true, busy: true, awaitingReread: false })).toBe(true);
    expect(recordDisabled({ changed: false, busy: false, awaitingReread: false })).toBe(true);
  });
});
