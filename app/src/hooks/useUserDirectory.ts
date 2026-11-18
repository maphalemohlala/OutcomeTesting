import { useEffect, useState } from 'react';
import { ContactsService } from '../generated';
import type { Contacts } from '../generated/models/ContactsModel';
import { logTechnical } from '../services/errors';
import { readAll } from '../services/readAll';
import { contactName } from '../lib/contactName';

export interface DirectoryUser {
  id: string;
  name: string;
  email: string;
  active: boolean;
  createdOn: string | null;
  rowVersion: string | null;
  /** The person's staff code, shown as "Employee code". Null where none is held. */
  staffCode: string | null;
}

/**
 * The generated `Contacts` model does not yet declare `al_staffcode` — the generator is
 * built from Dataverse metadata that lags a new column by hours, and this column was added
 * in the same body of work as this hook. Widened locally rather than editing the generated
 * file, which a regeneration would discard.
 */
type ContactWithStaffCode = Contacts & { al_staffcode?: string };

export type UserDirectoryState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; users: DirectoryUser[] };

/** One contact as a directory row, or null when it cannot serve as an application user. */
function toDirectoryUser(contact: ContactWithStaffCode): DirectoryUser | null {
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
    staffCode: contact.al_staffcode?.trim() || null,
  };
}

/** Exactly what contactName and toDirectoryUser read; contact has hundreds of columns. */
const DIRECTORY_COLUMNS = [
  'contactid', 'fullname', 'firstname', 'lastname', 'emailaddress1', 'statecode', 'createdon',
  'versionnumber', 'al_staffcode',
];

const UNAVAILABLE: UserDirectoryState = {
  status: 'unavailable',
  reason: 'The user list could not be loaded right now.',
};

/** How long one read serves every caller. Past it, somebody else's change to a person shows. */
const DIRECTORY_MAX_AGE_MS = 5 * 60 * 1000;

/** The read in flight or done for one reload key, shared by every caller on every screen. */
let shared: { key: number; startedAt: number; read: Promise<UserDirectoryState> } | null = null;

/**
 * One read per reload key, however many hooks ask, for five minutes. A changed key reads
 * again (the People page bumps it after an edit), as does a caller arriving after the limit;
 * a failed read is forgotten, so the next caller retries. Never rejects.
 */
export function loadUserDirectory(reloadKey = 0): Promise<UserDirectoryState> {
  if (shared?.key === reloadKey && Date.now() - shared.startedAt < DIRECTORY_MAX_AGE_MS) {
    return shared.read;
  }

  const read: Promise<UserDirectoryState> = readAll((o) => ContactsService.getAll(o), {
    orderBy: ['fullname asc', 'contactid asc'],
    select: DIRECTORY_COLUMNS,
  })
    .then((result): UserDirectoryState => {
      if (!result.success) {
        logTechnical('user directory load', result.error);
        return UNAVAILABLE;
      }
      return {
        status: 'ready',
        users: result.data
          .map(toDirectoryUser)
          .filter((user): user is DirectoryUser => user !== null),
      };
    })
    .catch((error): UserDirectoryState => {
      logTechnical('user directory load', error);
      return UNAVAILABLE;
    })
    .then((state) => {
      if (state.status !== 'ready' && shared?.read === read) shared = null;
      return state;
    });

  shared = { key: reloadKey, startedAt: Date.now(), read };
  return read;
}

/** Test seam: forget what was read. */
export function resetUserDirectoryForTests(): void {
  shared = null;
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

    void loadUserDirectory(reloadKey).then((next) => {
      if (!cancelled) setState(next);
    });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
