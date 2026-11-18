import { describe, expect, it } from 'vitest';
import { UNREACHABLE } from '../../services/commands/failures';
import { FAIL_POINT_SAVE_FALLBACK, describeFailPointSaveFailure } from './failReasons';

describe('a fail point that could not be saved', () => {
  it('is called a fail point, not a dropdown option', () => {
    expect(describeFailPointSaveFailure(new Error('boom'), true)).toBe(FAIL_POINT_SAVE_FALLBACK);
    expect(FAIL_POINT_SAVE_FALLBACK).toMatch(/fail point/);
    expect(FAIL_POINT_SAVE_FALLBACK).not.toMatch(/option/i);
  });

  it('still says when the connection is down', () => {
    expect(describeFailPointSaveFailure(new Error('Failed to fetch'), false)).toBe(UNREACHABLE);
  });
});
