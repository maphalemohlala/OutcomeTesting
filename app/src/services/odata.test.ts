import { describe, expect, it } from 'vitest';
import { isRecordId, odataEscape } from './odata';

describe('isRecordId', () => {
  it('accepts a canonical record id', () => {
    expect(isRecordId('3f2504e0-4f89-11d3-9a0c-0305e82c3301')).toBe(true);
  });

  it('accepts an id in either case, and tolerates surrounding whitespace', () => {
    expect(isRecordId('3F2504E0-4F89-11D3-9A0C-0305E82C3301')).toBe(true);
    expect(isRecordId('  3f2504e0-4f89-11d3-9a0c-0305e82c3301  ')).toBe(true);
  });

  it('rejects absent values', () => {
    expect(isRecordId(undefined)).toBe(false);
    expect(isRecordId(null)).toBe(false);
    expect(isRecordId('')).toBe(false);
  });

  it('rejects malformed ids', () => {
    expect(isRecordId('not-a-guid')).toBe(false);
    expect(isRecordId('3f2504e0-4f89-11d3-9a0c')).toBe(false); // truncated
    expect(isRecordId('3f2504e0-4f89-11d3-9a0c-0305e82c3301x')).toBe(false); // trailing char
    expect(isRecordId('{3f2504e0-4f89-11d3-9a0c-0305e82c3301}')).toBe(false); // braced form
    expect(isRecordId('3g2504e0-4f89-11d3-9a0c-0305e82c3301')).toBe(false); // non-hex
  });

  // The point of the guard: these are the shapes that would break out of a filter clause.
  it('rejects filter-breakout attempts', () => {
    expect(isRecordId('3f2504e0-4f89-11d3-9a0c-0305e82c3301 or al_name ne null')).toBe(false);
    expect(isRecordId("' or '1' eq '1")).toBe(false);
    expect(isRecordId('00000000-0000-0000-0000-000000000000 or true')).toBe(false);
  });
});

describe('odataEscape', () => {
  it('leaves an ordinary value untouched', () => {
    expect(odataEscape('BATCH-2026-001')).toBe('BATCH-2026-001');
  });

  it("doubles an apostrophe so a name like O'Brien does not end the literal", () => {
    expect(odataEscape("O'Brien")).toBe("O''Brien");
  });

  it('doubles every apostrophe, not just the first', () => {
    expect(odataEscape("' or '1' eq '1")).toBe("'' or ''1'' eq ''1");
  });
});
