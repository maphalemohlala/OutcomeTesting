import { describe, expect, it } from 'vitest';
import { formReadsComplete } from './reviewSections';

describe('the checklist reads', () => {
  const ok = { success: true };
  const failed = { success: false };

  it('make the review unavailable when the question versions or questions fail', () => {
    // responses, sections, fail reasons, versions, questions
    expect(formReadsComplete([ok, ok, ok, failed, ok])).toBe(false);
    expect(formReadsComplete([ok, ok, ok, ok, failed])).toBe(false);
  });

  it('draw the form once all of them came back', () => {
    expect(formReadsComplete([ok, ok, ok, ok, ok])).toBe(true);
  });
});
