import { useEffect, useState } from 'react';
import { Al_pagepermissionsService, Al_userrolemappingsService } from '../../generated';
import { APP_ROLE_BY_VALUE, ACCESS_LEVEL_BY_VALUE } from '../../types/permissions';
import { choiceLabel } from '../../lib/choiceLabel';

/**
 * What to show for an option value no map recognises.
 *
 * The previous fallback was String(value), which put a bare option number in the role and
 * level columns — and rendered the literal text "undefined" when the column was empty,
 * because String(undefined) is a string. Neither tells an administrator anything.
 *
 * The number is still shown, but labelled as unrecognised rather than presented as a role.
 * This screen is explicitly administrative, so the raw value is the useful detail here: it
 * is what someone needs in order to find the row that is wrong.
 */
function unrecognised(value: number | undefined): string {
  return value == null ? 'Not set' : `Unrecognised (${value})`;
}

/**
 * A custom role is identified by its stable al_role code (AD-044) and takes precedence;
 * built-in roles come from the al_approle choice.
 */
function roleLabel(
  roleCode: string | undefined,
  formatted: string | undefined,
  value: number | undefined,
): string {
  return (
    roleCode?.trim() || choiceLabel(APP_ROLE_BY_VALUE, value, formatted) || unrecognised(value)
  );
}

export interface RoleMappingRow {
  id: string;
  email: string;
  role: string;
  /** False once the assignment has been withdrawn; the row is kept for the audit trail. */
  active: boolean;
}

export interface PagePermissionRow {
  id: string;
  role: string;
  resource: string;
  level: string;
  active: boolean;
}

export type SecurityConfigState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; mappings: RoleMappingRow[]; permissions: PagePermissionRow[] };

export function useSecurityConfig(reloadKey: number): SecurityConfigState {
  const [state, setState] = useState<SecurityConfigState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    // Withdrawn rows are kept and shown as inactive, so an administrator can restore them.
    Promise.all([
      Al_userrolemappingsService.getAll({ top: 5000 }),
      Al_pagepermissionsService.getAll({ top: 5000 }),
    ])
      .then(([mapResult, permResult]) => {
        if (cancelled) return;
        if (!mapResult.success || !permResult.success) {
          setState({
            status: 'unavailable',
            reason: 'Security configuration could not be loaded from Dataverse.',
          });
          return;
        }

        const mappings: RoleMappingRow[] = mapResult.data
          .map((m) => ({
            id: m.al_userrolemappingid,
            email: m.al_useremail ?? '',
            role: roleLabel(m.al_rolecode, m.al_approlename, m.al_approle),
            active: Number(m.statecode) === 0,
          }))
          .sort((a, b) => a.email.localeCompare(b.email));

        const permissions: PagePermissionRow[] = permResult.data
          .map((p) => ({
            id: p.al_pagepermissionid,
            role: roleLabel(p.al_rolecode, p.al_approlename, p.al_approle),
            resource: p.al_resourcekey ?? '',
            level:
              choiceLabel(ACCESS_LEVEL_BY_VALUE, p.al_accesslevel, p.al_accesslevelname) ??
              unrecognised(p.al_accesslevel),
            active: Number(p.statecode) === 0,
          }))
          .sort((a, b) => a.resource.localeCompare(b.resource) || a.role.localeCompare(b.role));

        setState({ status: 'ready', mappings, permissions });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'Security configuration could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
