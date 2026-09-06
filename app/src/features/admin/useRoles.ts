import { useEffect, useState } from 'react';
import { Mspp_webrolesService } from '../../generated';
import { isSystemWebRole } from '../../types/permissions';
import { logTechnical } from '../../services/errors';

export interface RoleRow {
  id: string;
  name: string;
  /**
   * The stable code assignments and permission rules match on. A web role has no separate
   * business key, so its name is the code — which is exactly what al_rolecode carries.
   */
  code: string;
  description: string | null;
  active: boolean;
  rowVersion: string | null;
}

export type RolesState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; roles: RoleRow[] };

/**
 * The application's roles, read from the Power Pages web roles (AD-041, AD-044).
 *
 * This is the live list, so a role added on the portal appears in the app with no code
 * change, and one added here is a real web role rather than a second registry beside it.
 * `APP_ROLES` in types/permissions.ts is only the vocabulary the default matrix is written
 * against; this is what an administrator actually picks from.
 *
 * The two Power Pages system roles are filtered out. They exist to make the portal work
 * rather than to describe a job, and offering them would invite granting business access to
 * "everyone who is signed in" — a decision no requirement makes.
 *
 * No `top`: mspp_webrole is a surface over powerpagecomponent and returns nothing when a
 * row limit is applied.
 */
export function useRoles(reloadKey = 0): RolesState {
  const [state, setState] = useState<RolesState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Mspp_webrolesService.getAll({ orderBy: ['mspp_name asc'] })
      .then((result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          logTechnical('roles load', result.error);
          setState({ status: 'unavailable', reason: 'The role list could not be loaded right now.' });
          return;
        }
        setState({
          status: 'ready',
          roles: result.data
            .map((r) => ({
              id: r.mspp_webroleid,
              name: (r.mspp_name ?? '').trim(),
              code: (r.mspp_name ?? '').trim(),
              description: r.mspp_description ?? null,
              active: Number(r.statecode) === 0,
              rowVersion: null,
            }))
            .filter((role) => role.name.length > 0 && !isSystemWebRole(role.name)),
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
