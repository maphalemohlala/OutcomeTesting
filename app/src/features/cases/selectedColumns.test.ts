import { describe, expect, it } from 'vitest';
import { remediationTotals } from '../dashboard/remediationTotals';
import { ACTION_COUNT_COLUMNS, remediationByCase, type CaseRemediationSource } from './caseRemediation';
import {
  CASE_SUMMARY_COLUMNS,
  OUTCOME_GRADE_COLUMNS,
  gradesByCase,
  toSummary,
} from './caseWorklistMapping';
import type { Al_outcomecases } from '../../generated/models/Al_outcomecasesModel';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';

/** A row that notes every property read from it. */
function spyRow<T>(seed: Record<string, unknown> = {}) {
  const read = new Set<string>();
  const row = new Proxy(seed, {
    get(target, key) {
      if (typeof key === 'string') read.add(key);
      return target[key as string];
    },
  }) as unknown as T;
  return { row, read };
}

/** Annotations ride on their column, and `<column>name` is a virtual the server fills in. */
function notSelected(read: Set<string>, columns: string[]): string[] {
  const selected = new Set(columns);
  return [...read].filter((key) => {
    const column = key.split('@')[0];
    if (selected.has(column)) return false;
    const base = column.replace(/name$/, '');
    return !(column !== base && (selected.has(base) || selected.has(`_${base}_value`)));
  });
}

/**
 * A column left out of a `select` reads as blank with no error, so each mapping is run against
 * a row that records what it touched, and everything touched must have been asked for.
 */
describe('the columns the list reads ask for', () => {
  it('cover everything a case summary reads', () => {
    const { row, read } = spyRow<Al_outcomecases>();

    toSummary(row);

    expect(notSelected(read, CASE_SUMMARY_COLUMNS)).toEqual([]);
  });

  it('cover everything the grades read', () => {
    const { row, read } = spyRow<Al_outcomes>({ _al_outcomecaseid_value: 'case-1' });

    gradesByCase([row]);

    expect(notSelected(read, OUTCOME_GRADE_COLUMNS)).toEqual([]);
  });

  it('cover everything the remediation counts read', () => {
    const { row, read } = spyRow<CaseRemediationSource>({ _al_outcomecaseid_value: 'case-1' });

    remediationByCase([row]);
    remediationTotals([row as never]);

    expect(notSelected(read, ACTION_COUNT_COLUMNS)).toEqual([]);
  });
});
