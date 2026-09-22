import type { RoleMappingRow } from '../admin/useSecurityConfig';
import { ROLE_FILTERS } from './peopleFilters';

/**
 * The seeded `al_Role` code behind each role the People page offers.
 *
 * `assignUserRole` branches on RoleCode first and treats an empty AppRole as absent, so a
 * custom role must be sent by its code. These are the codes in `data/roles-seed`; a typo
 * here would create a mapping to a role nothing holds, which reads as success.
 */
export const ROLE_CODES: Record<(typeof ROLE_FILTERS)[number], string> = {
  Adviser: 'ROLE-ADVISER',
  Paraplanner: 'ROLE-PARAPLANNER',
  'T&C Manager': 'ROLE-T-C-MANAGER',
  'Tax Checker': 'ROLE-TAX-CHECKER',
  'AQS Checker': 'ROLE-AQS-CHECKER',
  'Senior Checker': 'ROLE-SENIOR-CHECKER',
};

/**
 * The active mapping granting one person one role, or null where they do not hold it.
 *
 * Withdrawn mappings are ignored: the row survives for the audit trail but no longer says
 * what somebody holds, and treating it as current would leave the page offering "withdraw"
 * for a role already withdrawn.
 */
export function mappingFor(
  mappings: readonly RoleMappingRow[],
  email: string,
  role: string,
): RoleMappingRow | null {
  const wantedEmail = email.trim().toLowerCase();
  const wantedRole = role.trim().toLowerCase();

  return (
    mappings.find(
      (row) =>
        row.active &&
        row.email.trim().toLowerCase() === wantedEmail &&
        row.role.trim().toLowerCase() === wantedRole,
    ) ?? null
  );
}
