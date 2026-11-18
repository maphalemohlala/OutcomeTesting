import { describe, expect, it } from 'vitest';
import { can, resolvePermissions, type PermissionRule } from './permissions';

/**
 * Role names are compared without regard to case or surrounding space, as the server gate
 * compares them (PermissionHelpers.MaxLevel). The client compared them exactly, so a rule
 * stored as "administrators" granted the server's Manage while the app hid the screen.
 */
describe('role names in the permission rules', () => {
  const rules: PermissionRule[] = [{ role: 'administrators ', resource: 'permission.manage', level: 'Manage' }];

  it('match a held role that differs only in case or spacing', () => {
    expect(can(resolvePermissions(['Administrators'], rules), 'permission.manage', 'Manage')).toBe(true);
    expect(can(resolvePermissions([' ADMINISTRATORS'], rules), 'permission.manage', 'Manage')).toBe(true);
  });

  it('still do not match a different role', () => {
    expect(can(resolvePermissions(['Administrator'], rules), 'permission.manage', 'View')).toBe(false);
  });
});
