import { useEffect, useState } from 'react';
import { Al_usersService } from '../generated';
import { logTechnical } from '../services/errors';

export interface DirectoryUser {
  id: string;
  name: string;
  email: string;
  active: boolean;
  createdOn: string | null;
  rowVersion: string | null;
}

export type UserDirectoryState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; users: DirectoryUser[] };

/**
 * Reads the application user registry (al_user, AD-041/AD-044) once, shared by the
 * Users admin page and the person pickers. Row-level visibility is enforced by
 * Dataverse security; this never returns sample rows.
 */
export function useUserDirectory(reloadKey = 0): UserDirectoryState {
  const [state, setState] = useState<UserDirectoryState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_usersService.getAll({ orderBy: ['al_name asc'], top: 5000 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          logTechnical('user directory load', result.error);
          setState({ status: 'unavailable', reason: 'The user list could not be loaded right now.' });
          return;
        }
        setState({
          status: 'ready',
          users: result.data.map((u) => ({
            id: u.al_userid,
            name: u.al_name,
            email: u.al_workemail,
            active: u.al_isactive !== false,
            createdOn: u.createdon ?? null,
            rowVersion: u.versionnumber != null ? String(u.versionnumber) : null,
          })),
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('user directory load', error);
        setState({ status: 'unavailable', reason: 'The user list could not be loaded right now.' });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
