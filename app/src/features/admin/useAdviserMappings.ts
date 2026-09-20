import { useEffect, useState } from 'react';
import { Al_advisermappingsService, ContactsService } from '../../generated';
import { logTechnical } from '../../services/errors';
import {
  toMappingRows,
  type AdviserMappingRow,
  type ContactOption,
} from './adviserMappingRows';

// Held in adviserMappingRows so the shaping can be tested; this module cannot be imported
// under the test runner because ../../generated pulls in the Power Apps client.
export type { AdviserMappingRow, ContactOption } from './adviserMappingRows';

export type AdviserMappingsState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; mappings: AdviserMappingRow[]; contacts: ContactOption[] };

/**
 * The adviser -> T&C Manager mapping, and the contacts that can be chosen as a manager
 * (Fixes 5, AD-162).
 *
 * <p>
 * Routing only. The mapping decides who is told a sign-off is waiting on a case; it grants
 * no access and restricts none, because every T&C Manager reads every case. Nothing on this
 * screen should ever be described to a user as controlling what anyone can see.
 * </p>
 * <p>
 * The manager list is contacts with a work email, because a mapping to somebody unreachable
 * sends nothing — `TcManagerRouting` reports that as ManagerNotReachable rather than
 * pretending it routed. Offering a contact with no email would be offering a choice that
 * silently does not work.
 * </p>
 */
export function useAdviserMappings(reloadKey = 0): AdviserMappingsState {
  const [state, setState] = useState<AdviserMappingsState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_advisermappingsService.getAll({ orderBy: ['al_adviseremail asc'] }),
      ContactsService.getAll({ orderBy: ['fullname asc'] }),
    ])
      .then(([mappingResult, contactResult]) => {
        if (cancelled) return;

        if (!mappingResult.success || !mappingResult.data) {
          logTechnical('adviser mappings load', mappingResult.error);
          setState({
            status: 'unavailable',
            reason: 'The adviser mapping could not be loaded right now.',
          });
          return;
        }

        if (!contactResult.success || !contactResult.data) {
          logTechnical('adviser mappings contacts load', contactResult.error);
          setState({
            status: 'unavailable',
            reason: 'The list of T&C Managers could not be loaded right now.',
          });
          return;
        }

        const contacts: ContactOption[] = contactResult.data
          .filter((c) => Number(c.statecode) === 0 && (c.emailaddress1 ?? '').trim() !== '')
          .map((c) => ({
            id: c.contactid,
            name: (c.fullname ?? '').trim(),
            email: (c.emailaddress1 ?? '').trim(),
          }))
          .filter((m) => m.name.length > 0);

        setState({
          status: 'ready',
          contacts,
          mappings: toMappingRows(mappingResult.data, contacts),
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('adviser mappings load', error);
        setState({
          status: 'unavailable',
          reason: 'The adviser mapping could not be loaded right now.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}

/**
 * Saves one mapping, creating or replacing the row for that adviser.
 *
 * <p>
 * `al_adviseremail` is the table's alternate key, so a second row for one adviser cannot
 * exist. That is enforced by Dataverse rather than by this function — which is why the
 * server-side resolver can take the first row it finds without checking for ambiguity.
 * </p>
 */
export async function saveAdviserMapping(
  existingId: string | null,
  adviserEmail: string,
  managerId: string,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  const email = adviserEmail.trim();
  if (email === '') {
    return { ok: false, reason: 'Enter the adviser’s work email.' };
  }
  if (managerId === '') {
    return { ok: false, reason: 'Choose a T&C Manager.' };
  }

  try {
    const result = existingId
      ? await Al_advisermappingsService.update(existingId, {
          al_adviseremail: email,
          'al_TcManagerId@odata.bind': `/contacts(${managerId})`,
        })
      : await Al_advisermappingsService.create({
          al_name: email,
          al_adviseremail: email,
          'al_TcManagerId@odata.bind': `/contacts(${managerId})`,
          // The generated model types statecode as required on create and keys it by the
          // option VALUE, not its label. Dataverse would default it, but the contract asks
          // for it, so it is stated rather than cast away.
          statecode: 0,
        });

    if (!result.success) {
      logTechnical('adviser mapping save', result.error);
      return { ok: false, reason: 'That mapping could not be saved.' };
    }

    return { ok: true };
  } catch (error) {
    logTechnical('adviser mapping save', error);
    return { ok: false, reason: 'That mapping could not be saved.' };
  }
}
