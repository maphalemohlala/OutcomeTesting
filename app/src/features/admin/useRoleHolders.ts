import { useEffect, useState } from 'react';
import { executeCommand } from '../../services/commands/commandClient';
import { logTechnical } from '../../services/errors';
import type { RoleHolderRecord } from './roleDetail';

export type RoleHoldersState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; holders: RoleHolderRecord[] };

/**
 * Everyone holding one role, from both sources (AD-089).
 *
 * A Custom API rather than a table read, because the contact-to-web-role intersect has no
 * many-to-many on the role and the generated client has no $expand — the browser cannot
 * perform the second half of this read at all.
 */
export function useRoleHolders(roleCode: string, reloadKey = 0): RoleHoldersState {
  const [state, setState] = useState<RoleHoldersState>({ status: 'loading' });

  useEffect(() => {
    if (!roleCode) return;
    let cancelled = false;
    // No synchronous setState here to reset to 'loading' on every reloadKey bump: the
    // sibling hooks (useSecurityConfig, useRoles, useUserDirectory) follow the same
    // convention, so a reconcile refresh keeps the current holders on screen rather than
    // flashing back to loading — and it sidesteps react-hooks/set-state-in-effect, which
    // flags a setState called synchronously in the effect body rather than from a callback.

    executeCommand<{ Holders: string }>('al_GetRoleHolders', { RoleCode: roleCode })
      .then((result) => {
        if (cancelled) return;
        if (!result.ok) {
          logTechnical('role holders load', result.message);
          setState({ status: 'unavailable', reason: 'The people holding this role could not be loaded right now.' });
          return;
        }

        try {
          setState({ status: 'ready', holders: JSON.parse(result.data.Holders) as RoleHolderRecord[] });
        } catch (error) {
          logTechnical('role holders parse', error);
          setState({ status: 'unavailable', reason: 'The people holding this role could not be read.' });
        }
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('role holders load', error);
        setState({ status: 'unavailable', reason: 'The people holding this role could not be loaded right now.' });
      });

    return () => {
      cancelled = true;
    };
  }, [roleCode, reloadKey]);

  return state;
}
