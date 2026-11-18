import { describe, expect, it } from 'vitest';
import { isRetiredRole } from './roleDetail';

/**
 * Restoring a rule or an assignment for a retired role is refused by the server, so the
 * Security configuration page must not offer it.
 */
describe('isRetiredRole', () => {
  const roles = [
    { code: 'Old Role', active: false },
    { code: 'Live Role', active: true },
  ];

  it('is true for a retired role, whatever the casing of the code', () => {
    expect(isRetiredRole('old role ', roles)).toBe(true);
  });

  it('is false for an active role and for a code no web role carries', () => {
    expect(isRetiredRole('Live Role', roles)).toBe(false);
    expect(isRetiredRole('Adviser', roles)).toBe(false);
  });
});
