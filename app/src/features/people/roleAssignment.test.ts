import { describe, expect, it } from 'vitest';
import { ROLE_CODES, canWithdraw, mappingFor } from './roleAssignment';
import { ROLE_FILTERS } from './peopleFilters';
import type { RoleMappingRow } from '../admin/useSecurityConfig';

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
  it('knows the seeded code for each role it offers', () => {
    // assignUserRole distinguishes a custom al_Role (RoleCode) from a built-in web role
    // (AppRole). All six of these are custom, so all six need their seeded code.
    expect(ROLE_CODES['Adviser']).toBe('ROLE-ADVISER');
    expect(ROLE_CODES['Paraplanner']).toBe('ROLE-PARAPLANNER');
    expect(ROLE_CODES['T&C Manager']).toBe('ROLE-T-C-MANAGER');
    expect(ROLE_CODES['Tax Checker']).toBe('ROLE-TAX-CHECKER');
    expect(ROLE_CODES['AQS Checker']).toBe('ROLE-AQS-CHECKER');
    expect(ROLE_CODES['Senior Checker']).toBe('ROLE-SENIOR-CHECKER');
  });

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
 * The Grant dropdown offers only ROLE_FILTERS, but the Role column lists every active
 * al_userrolemapping row a person holds - so Administrator, Outcome Testing Manager,
 * Reviewer and Read Only User each got a one-click Withdraw with no confirmation and no way
 * back from this page (2026-09-22 review). The permission boundary was intact throughout;
 * the affordance was the defect. These pin the rule that Withdraw and Grant offer the same
 * set, so the two cannot drift apart again.
 */
describe('canWithdraw', () => {
  it('offers withdrawal for every role this page can grant back', () => {
    for (const role of ROLE_FILTERS) {
      expect(canWithdraw(role)).toBe(true);
    }
  });

  it('refuses the application access roles the Grant dropdown never offers', () => {
    for (const role of ['Administrator', 'Outcome Testing Manager', 'Reviewer', 'Read Only User']) {
      expect(canWithdraw(role)).toBe(false);
    }
  });

  it('agrees with the grant dropdown exactly, by construction', () => {
    // Not a second list to keep in step: a role is withdrawable here precisely when it is
    // grantable here, which is what stops the two drifting.
    expect(ROLE_FILTERS.filter((role) => !canWithdraw(role))).toEqual([]);
  });

  it('matches the hand-entered label case-insensitively and trimmed', () => {
    expect(canWithdraw('  adviser ')).toBe(true);
    expect(canWithdraw('ADMINISTRATOR')).toBe(false);
  });
});
