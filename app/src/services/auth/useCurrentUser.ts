import { useEffect, useState } from 'react';
import { getContext } from '@microsoft/power-apps/app';

export interface CurrentUser {
  fullName?: string;
  userPrincipalName?: string;
  objectId?: string;
}

type UserState =
  | { status: 'loading' }
  | { status: 'ready'; user: CurrentUser }
  | { status: 'error'; message: string };

/**
 * Identity comes from the Power Apps host. It is display context only —
 * authorisation is enforced by Dataverse roles, never by this value.
 */
export function useCurrentUser(): UserState {
  const [state, setState] = useState<UserState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    getContext()
      .then((context) => {
        if (cancelled) return;
        setState({ status: 'ready', user: context.user });
      })
      .catch(() => {
        if (cancelled) return;
        setState({ status: 'error', message: 'Signed-in user could not be identified.' });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
