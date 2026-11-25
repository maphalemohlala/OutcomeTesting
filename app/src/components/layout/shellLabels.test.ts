import { describe, expect, it } from 'vitest';
import { initials, roleLabel } from './shellLabels';

describe('the signed-in footer', () => {
  it('names Administrator whenever it is held, whatever else is', () => {
    expect(roleLabel(['AL Portal - AQS Reviewer', 'Administrators'])).toBe('Administrator');
  });

  it('otherwise names the first role without its portal prefix', () => {
    expect(roleLabel(['AL Portal - Tax Reviewer'])).toBe('Tax Reviewer');
  });

  it('says so when no role is held', () => {
    expect(roleLabel([])).toBe('No role yet');
  });
});

describe('initials for the avatar and the product mark', () => {
  it('takes the first letter of the first two words', () => {
    expect(initials('Outcome Testing')).toBe('OT');
    expect(initials('svc automate aq')).toBe('SA');
  });

  it('takes the first two letters of a single word, so OTIS still reads OT', () => {
    expect(initials('OTIS')).toBe('OT');
  });

  it('splits an email-style name on its dot', () => {
    expect(initials('simunye.radingwana@example.com')).toBe('SR');
  });
});
