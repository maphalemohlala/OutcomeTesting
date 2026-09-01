import { useEffect, useState } from 'react';
import { Al_rolesService } from '../../generated';
import { logTechnical } from '../../services/errors';

export interface RoleRow {
  id: string;
  name: string;
  code: string;
  description: string | null;
  active: boolean;
  rowVersion: string | null;
}

export type RolesState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; roles: RoleRow[] };

/** Reads the extensible role registry (al_role, AD-044). Never returns sample rows. */
export function useRoles(reloadKey = 0): RolesState {
  const [state, setState] = useState<RolesState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_rolesService.getAll({ orderBy: ['al_name asc'], top: 500 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          logTechnical('roles load', result.error);
          setState({ status: 'unavailable', reason: 'The role list could not be loaded right now.' });
          return;
        }
        setState({
          status: 'ready',
          roles: result.data.map((r) => ({
            id: r.al_roleid,
            name: r.al_name,
            code: r.al_rolecode,
            description: r.al_description ?? null,
            active: r.al_isactive !== false,
            rowVersion: r.versionnumber != null ? String(r.versionnumber) : null,
          })),
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('roles load', error);
        setState({ status: 'unavailable', reason: 'The role list could not be loaded right now.' });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
