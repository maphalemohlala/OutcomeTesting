import { useEffect, useState } from 'react';
import { ContactsService } from '../generated';
import type { Contacts } from '../generated/models/ContactsModel';
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
 * The display name for a contact.
 *
 * `fullname` is a calculated column, so it is present on a read but absent from anything
 * the app writes. Falling back through the parts it is calculated from means a contact
 * created in the same session still shows a name, and a contact with neither still
 * resolves to something a person can recognise rather than an empty cell.
 */
export function contactName(contact: Contacts): string {
  const full = contact.fullname?.trim();
  if (full) return full;

  const parts = [contact.firstname?.trim(), contact.lastname?.trim()].filter(Boolean);
  if (parts.length > 0) return parts.join(' ');

  return contact.emailaddress1?.trim() ?? '';
}

/** One contact as a directory row, or null when it cannot serve as an application user. */
export function toDirectoryUser(contact: Contacts): DirectoryUser | null {
  const email = contact.emailaddress1?.trim();
  // AD-010 keys the registry on work email, and al_AssignCase resolves both the
  // systemuser and the contact from it. A contact with no email cannot be allocated to,
  // so listing it would offer a choice every command downstream would refuse.
  if (!email) return null;

  return {
    id: contact.contactid,
    name: contactName(contact) || email,
    email,
    active: Number(contact.statecode) === 0,
    createdOn: contact.createdon ?? null,
    rowVersion: contact.versionnumber != null ? String(contact.versionnumber) : null,
  };
}

/**
 * Reads the application user registry once, shared by the People directory and the
 * person pickers. Row-level visibility is enforced by Dataverse security; this never
 * returns sample rows.
 *
 * The registry is `contact`. It was `al_user`, a second list of people that nothing
 * joined to the first: authorisation reads al_userrolemapping keyed on work email and
 * tolerates a missing registry row, so al_user was a directory rather than a gate, and
 * the environment's only role mapping was for an email that had no al_user row at all.
 * Contact is what Power Pages resolves permissions through (AD-047) and what
 * al_AssignCase already demands alongside the systemuser (OD-003), so keying the app to
 * it leaves one set of people instead of two that could disagree.
 */
export function useUserDirectory(reloadKey = 0): UserDirectoryState {
  const [state, setState] = useState<UserDirectoryState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    ContactsService.getAll({ orderBy: ['fullname asc'], top: 5000 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          logTechnical('user directory load', result.error);
          setState({ status: 'unavailable', reason: 'The user list could not be loaded right now.' });
          return;
        }
        setState({
          status: 'ready',
          users: result.data
            .map(toDirectoryUser)
            .filter((user): user is DirectoryUser => user !== null),
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
