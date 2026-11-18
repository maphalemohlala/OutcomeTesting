import { describe, expect, it } from 'vitest';
import { letterWordingMissing } from './notificationTemplates';

describe('a new letter with no wording', () => {
  it('counts an emptied editor as no body, so Save stays off', () => {
    expect(letterWordingMissing('Subject', '<p><br></p>')).toBe(true);
    expect(letterWordingMissing('Subject', '<p>&nbsp;</p>')).toBe(true);
    expect(letterWordingMissing('Subject', '')).toBe(true);
  });

  it('needs a subject too', () => {
    expect(letterWordingMissing('  ', '<p>Hello</p>')).toBe(true);
  });

  it('is complete once both carry text', () => {
    expect(letterWordingMissing('Subject', '<p>Hello</p>')).toBe(false);
  });
});
