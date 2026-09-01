import { useEffect, useState } from 'react';
import { Al_pagepermissionsService, Al_userrolemappingsService } from '../../generated';
import { APP_ROLE_BY_VALUE, ACCESS_LEVEL_BY_VALUE } from '../../types/permissions';

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
            role:
              m.al_rolecode?.trim() ||
              m.al_approlename ||
              APP_ROLE_BY_VALUE[Number(m.al_approle)] ||
              String(m.al_approle),
            active: Number(m.statecode) === 0,
          }))
          .sort((a, b) => a.email.localeCompare(b.email));

        const permissions: PagePermissionRow[] = permResult.data
          .map((p) => ({
            id: p.al_pagepermissionid,
            role:
              p.al_rolecode?.trim() ||
              p.al_approlename ||
              APP_ROLE_BY_VALUE[Number(p.al_approle)] ||
              String(p.al_approle),
            resource: p.al_resourcekey ?? '',
            level: p.al_accesslevelname ?? ACCESS_LEVEL_BY_VALUE[Number(p.al_accesslevel)] ?? String(p.al_accesslevel),
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
