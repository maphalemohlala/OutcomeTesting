import { useEffect, useState } from 'react';
import { Al_pagepermissionsService, Al_userrolemappingsService } from '../../generated';
import { APP_ROLE_BY_VALUE, ACCESS_LEVEL_BY_VALUE } from '../../types/permissions';

export interface RoleMappingRow {
  id: string;
  email: string;
  role: string;
}

export interface PagePermissionRow {
  id: string;
  role: string;
  resource: string;
  level: string;
}

export type SecurityConfigState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; mappings: RoleMappingRow[]; permissions: PagePermissionRow[] };

export function useSecurityConfig(reloadKey: number): SecurityConfigState {
  const [state, setState] = useState<SecurityConfigState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_userrolemappingsService.getAll({ filter: 'statecode eq 0', top: 5000 }),
      Al_pagepermissionsService.getAll({ filter: 'statecode eq 0', top: 5000 }),
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
