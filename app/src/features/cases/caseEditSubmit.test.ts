import { describe, expect, it } from 'vitest';
import {
  describeSubmit,
  validateSubmit,
  type AllocationOutcome,
  type CommandOutcome,
} from './caseEditSubmit';

const ok: CommandOutcome = { kind: 'ok' };
const skipped: CommandOutcome = { kind: 'skipped' };
const failed = (message: string): CommandOutcome => ({ kind: 'failed', message });

const alloc = (label: string, outcome: CommandOutcome): AllocationOutcome => ({ label, outcome });

describe('case edit submit sequencing (AD-040, BR-012)', () => {
  it('reports a plain field save', () => {
    const result = describeSubmit(ok, []);

    expect(result.notice).toBe('Case details updated.');
    expect(result.error).toBeNull();
    expect(result.close).toBe(true);
    expect(result.reload).toBe(true);
  });

  it('reports allocating one check', () => {
    const result = describeSubmit(skipped, [alloc('AQS', ok)]);

    expect(result.notice).toBe(
      'AQS check allocated — the checker will see it in their portal worklist.',
    );
    expect(result.close).toBe(true);
  });

  it('reports allocating both checks in one save', () => {
    const result = describeSubmit(skipped, [alloc('Tax', ok), alloc('AQS', ok)]);

    expect(result.notice).toBe(
      'Tax and AQS checks allocated — the checker will see it in their portal worklist.',
    );
  });

  it('reports fields and allocations together', () => {
    const result = describeSubmit(ok, [alloc('Tax', ok)]);

    expect(result.notice).toBe(
      'Case details updated, and Tax check allocated — the checker will see it in their portal worklist.',
    );
    expect(result.reload).toBe(true);
  });

  it('says nothing changed when the allocation was already recorded', () => {
    const result = describeSubmit(skipped, [alloc('AQS', { kind: 'ok', alreadyDone: true })]);

    expect(result.notice).toBe(
      'That allocation had already been recorded, so nothing was changed.',
    );
    expect(result.error).toBeNull();
  });

  it('does not claim a save when the field update was refused', () => {
    const result = describeSubmit(failed('This case changed since you loaded it.'), []);

    expect(result.error).toBe('This case changed since you loaded it.');
    expect(result.notice).toBeNull();
    expect(result.close).toBe(false);
    expect(result.reload).toBe(false);
  });

  it('names both halves when the fields saved and the allocation did not', () => {
    const result = describeSubmit(ok, [alloc('AQS', failed('That check is already submitted.'))]);

    expect(result.error).toBe(
      'Case details were saved, but one check could not be allocated — AQS: That check is already submitted. Nothing else has been changed.',
    );
    expect(result.notice).toBeNull();
    expect(result.close).toBe(false);
    // The fields did land, so the case underneath the modal is now stale.
    expect(result.reload).toBe(true);
  });

  it('names which check failed when the other one landed', () => {
    const result = describeSubmit(skipped, [
      alloc('Tax', ok),
      alloc('AQS', failed('No unsubmitted check of that type.')),
    ]);

    expect(result.error).toBe(
      'Tax check allocated, but one check could not be allocated — AQS: No unsubmitted check of that type. Nothing else has been changed.',
    );
    expect(result.reload).toBe(true);
  });

  it('reports an allocation-only failure as itself', () => {
    const result = describeSubmit(skipped, [alloc('AQS', failed('No unsubmitted check.'))]);

    expect(result.error).toBe('AQS: No unsubmitted check.');
    expect(result.reload).toBe(false);
    expect(result.close).toBe(false);
  });

  it('refuses to report success when nothing ran', () => {
    const result = describeSubmit(skipped, [alloc('AQS', skipped)]);

    expect(result.notice).toBeNull();
    expect(result.close).toBe(false);
  });
});

describe('case edit validation', () => {
  it('accepts a field change on its own', () => {
    expect(validateSubmit({ changedCount: 1, allocationCount: 0 })).toEqual([]);
  });

  it('accepts an allocation on its own', () => {
    expect(validateSubmit({ changedCount: 0, allocationCount: 1 })).toEqual([]);
  });

  it('asks for one or the other when the form is untouched', () => {
    expect(validateSubmit({ changedCount: 0, allocationCount: 0 })).toEqual([
      'Change at least one field, or choose a checker to allocate a check to.',
    ]);
  });
});
