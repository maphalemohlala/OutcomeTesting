import { describe, expect, it } from 'vitest';
import { ROLE_CODES, mappingFor } from './roleAssignment';
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
