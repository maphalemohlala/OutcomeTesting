import { describe, expect, it } from 'vitest';
import { isSignedInPerson } from './peopleDirectory';

describe('isSignedInPerson', () => {
  it('matches the signed-in email whatever its case or spacing', () => {
    expect(isSignedInPerson(' Jane.Doe@Example.com', 'jane.doe@example.com ')).toBe(true);
  });

  it('is false for someone else, and while the signed-in email is unknown', () => {
    expect(isSignedInPerson('john@example.com', 'jane@example.com')).toBe(false);
    expect(isSignedInPerson('jane@example.com', undefined)).toBe(false);
    expect(isSignedInPerson('', '')).toBe(false);
  });
});
