import type { RoleMappingRow } from '../admin/useSecurityConfig';

/**
 * Whether this page may withdraw a role it finds somebody holding.
 *
 * Only a role it can also GRANT, which is why `grantable` is passed in rather than held as
 * a constant here: it is the live `mspp_webrole` list `useRoles` reads, so Grant and
 * Withdraw offer the same set by construction and cannot drift apart.
 *
 * `al_userrolemapping` can carry a role this page does not offer - one withdrawn on the
 * portal since, or a system role - and each of those used to get a one-click Withdraw with
 * no confirmation and no way back from here (2026-09-22 review). The permission boundary was
 * never the problem; a control that only goes one way is.
 *
 * The roles themselves are still DISPLAYED. Hiding them would misreport what somebody
 * holds, which is worse than offering no button.
 */
export function canWithdraw(role: string, grantable: readonly string[]): boolean {
  const wanted = role.trim().toLowerCase();
  return grantable.some((offered) => offered.trim().toLowerCase() === wanted);
}

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
