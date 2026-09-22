import { describe, expect, it } from 'vitest';
import { canWithdraw, mappingFor } from './roleAssignment';
import type { RoleMappingRow } from '../admin/useSecurityConfig';

/**
 * The roles the page can grant: live `mspp_webrole` names, as `useRoles` reads them.
 *
 * Not the `al_Role` codes this file used to pin. Those were refused by the server with "The
 * role code does not match an active role" - `AssignUserRolePlugin` resolves RoleCode
 * against the web role registry ONLY, the `al_role` fallback having been dropped under
 * OD-037 so that table can be deleted.
 */
const GRANTABLE = [
  'AL Portal - Adviser Remediation',
  'AL Portal - Planner',
  'AL Portal - T&C Supervisor',
  'AL Portal - Tax Reviewer',
  'AL Portal - AQS Reviewer',
] as const;

function mapping(overrides: Partial<RoleMappingRow> = {}): RoleMappingRow {
  return {
    id: 'map-1',
    email: 'jane.adviser@example.com',
    role: 'Adviser',
    active: true,
    ...overrides,
  };
}

describe('Role assignment from the People page', () => {
  it('finds the active mapping for a person and role', () => {
    const found = mappingFor([mapping()], 'jane.adviser@example.com', 'Adviser');
    expect(found?.id).toBe('map-1');
  });

  it('ignores a withdrawn mapping, so re-assigning is offered again', () => {
    // The row is kept for the audit trail but no longer describes what someone holds.
    const found = mappingFor([mapping({ active: false })], 'jane.adviser@example.com', 'Adviser');
    expect(found).toBeNull();
  });

  it('matches the email case-insensitively', () => {
    // al_userrolemapping is keyed on a hand-entered email.
    const found = mappingFor([mapping()], 'Jane.Adviser@Example.com', 'Adviser');
    expect(found?.id).toBe('map-1');
  });

  it('does not confuse one role with another for the same person', () => {
    const found = mappingFor([mapping()], 'jane.adviser@example.com', 'Paraplanner');
    expect(found).toBeNull();
  });
});

/**
 * Which roles the People page may withdraw.
 *
 * The Role column lists every active al_userrolemapping row a person holds, which can
 * include a role this page cannot grant back - and each of those used to get a one-click
 * Withdraw with no confirmation and no way back from this page (2026-09-22 review). The
 * permission boundary was intact throughout; the affordance was the defect. These pin the
 * rule that Withdraw and Grant offer the same set, so the two cannot drift apart again.
 */
describe('canWithdraw', () => {
  it('offers withdrawal for every role this page can grant back', () => {
    for (const role of GRANTABLE) {
      expect(canWithdraw(role, GRANTABLE)).toBe(true);
    }
  });

  it('refuses a role the Grant dropdown cannot offer back', () => {
    // Administrators is a real web role that useRoles returns, but one the page may be
    // showing without offering: whatever is not in `grantable` gets no Withdraw.
    for (const role of ['Administrators', 'AL Portal - Portal Administrator', 'Reviewer']) {
      expect(canWithdraw(role, GRANTABLE)).toBe(false);
    }
  });

  it('agrees with the grant dropdown exactly, by construction', () => {
    // Not a second list to keep in step: a role is withdrawable here precisely when it is
    // grantable here, which is what stops the two drifting.
    expect(GRANTABLE.filter((role) => !canWithdraw(role, GRANTABLE))).toEqual([]);
  });

  it('offers nothing while the role list is still loading', () => {
    // useRoles reports 'loading' first, so grantable is empty on the first render. A
    // Withdraw offered then could not be granted back.
    expect(canWithdraw('AL Portal - Planner', [])).toBe(false);
  });

  it('matches the hand-entered label case-insensitively and trimmed', () => {
    expect(canWithdraw('  al portal - planner ', GRANTABLE)).toBe(true);
    expect(canWithdraw('ADMINISTRATORS', GRANTABLE)).toBe(false);
  });
});
