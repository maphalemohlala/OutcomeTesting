import { describe, expect, it } from 'vitest';
import {
  nextDisplayOrder,
  nextFailReasonCode,
  toFailReasonRows,
  validateFailReasonDraft,
} from './failReasons';

const REC = 120910402;
const TAX = 120910403;

const rows = toFailReasonRows([
  { al_failreasonid: 'b', al_name: 'Record Keeping - B', al_failreasoncode: 'FR-REC-11', al_category: REC, al_displayorder: 2, statecode: 0 },
  { al_failreasonid: 'a', al_name: 'Record Keeping - A', al_failreasoncode: 'FR-REC-02', al_category: REC, al_displayorder: 1, statecode: 0 },
  { al_failreasonid: 'c', al_name: 'Old wording', al_failreasoncode: 'FR-REC-12', al_category: REC, al_displayorder: 3, statecode: 1 },
]);

describe('fail points in the question library', () => {
  it('orders by display order and flags inactive rows as retired', () => {
    expect(rows.map((r) => [r.id, r.retired, r.categoryLabel])).toEqual([
      ['a', false, 'Record Keeping'],
      ['b', false, 'Record Keeping'],
      ['c', true, 'Record Keeping'],
    ]);
  });

  it('takes the next code in the category, counting retired rows', () => {
    expect(nextFailReasonCode(rows, REC)).toBe('FR-REC-13');
    expect(nextFailReasonCode(rows, TAX)).toBe('FR-TAX-01');
  });

  it('puts a new point at the foot of the list', () => {
    expect(nextDisplayOrder(rows)).toBe(4);
  });

  it('refuses blank wording, a live duplicate and a non-numeric order', () => {
    expect(validateFailReasonDraft({ name: ' ', category: REC, order: '1' }, rows)).toMatch(/wording/);
    expect(validateFailReasonDraft({ name: 'record keeping - a', category: REC, order: '1' }, rows)).toMatch(/already/);
    expect(validateFailReasonDraft({ name: 'New', category: REC, order: 'x' }, rows)).toMatch(/whole number/);
  });

  it('lets a retired wording be added again, and a row keep its own wording', () => {
    expect(validateFailReasonDraft({ name: 'Old wording', category: REC, order: '5' }, rows)).toBeNull();
    expect(validateFailReasonDraft({ id: 'a', name: 'Record Keeping - A', category: REC, order: '1' }, rows)).toBeNull();
  });
});
