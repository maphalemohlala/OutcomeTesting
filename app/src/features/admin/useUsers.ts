import { useEffect, useState } from 'react';
import { Al_usersService } from '../../generated';
import { logTechnical } from '../../services/errors';

export interface UserRow {
  id: string;
  name: string;
  email: string;
  active: boolean;
}

export type UsersState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; users: UserRow[] };

/** Reads the application user registry (al_user, AD-041, AD-044). Never returns sample rows. */
export function useUsers(reloadKey = 0): UsersState {
  const [state, setState] = useState<UsersState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_usersService.getAll({ orderBy: ['al_name asc'], top: 5000 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          logTechnical('users load', result.error);
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
          })),
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('users load', error);
        setState({ status: 'unavailable', reason: 'The user list could not be loaded right now.' });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
