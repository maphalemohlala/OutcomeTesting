import { useEffect, useState } from 'react';
import { Al_advisermappingsService, ContactsService } from '../../generated';
import type { Al_advisermappingsal_adviserstatus } from '../../generated/models/Al_advisermappingsModel';
import { logTechnical } from '../../services/errors';
import {
  offeredManagers,
  toKnownContacts,
  toMappingRows,
  type AdviserMappingRow,
  type ContactOption,
  type KnownContact,
} from './adviserMappingRows';
import { CONTACT_NAME_COLUMNS } from '../../lib/contactName';
import { describeRemoveFailure, describeSaveFailure } from './adviserMappingFailure';
import { readAll } from '../../services/readAll';

// Held in adviserMappingRows so the shaping can be tested; this module cannot be imported
// under the test runner because ../../generated pulls in the Power Apps client.
export type { AdviserMappingRow, ContactOption, KnownContact } from './adviserMappingRows';

export type AdviserMappingsState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; mappings: AdviserMappingRow[]; contacts: ContactOption[]; known: KnownContact[] };

/**
 * The adviser -> T&C Manager mapping, and the contacts that can be chosen as a manager
 * (Fixes 5, AD-162).
 *
 * <p>
 * The mapping decides who is told a sign-off is waiting on a case, and it is also the gate
 * on acting: in the portal only a mapped T&C Manager can sign the case off or record its
 * final outcome (SupervisorMapping.EnsureManagesCase). An adviser may have several, all
 * equal (owner, 2026-11-24): one row each, every one told, any one may sign.
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
      readAll((o) => Al_advisermappingsService.getAll(o), { orderBy: ['al_adviseremail asc', 'al_advisermappingid asc'] }),
      readAll((o) => ContactsService.getAll(o), {
        orderBy: ['fullname asc', 'contactid asc'],
        // All the contact mapping below reads.
        select: ['contactid', ...CONTACT_NAME_COLUMNS, 'statecode'],
      }),
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

        const known: KnownContact[] = toKnownContacts(contactResult.data);
        const contacts: ContactOption[] = offeredManagers(known);

        setState({
          status: 'ready',
          contacts,
          known,
          // Offered contacts first, then everyone, so an inactive manager on record is still named.
          mappings: toMappingRows(mappingResult.data, [...contacts, ...known]),
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
 * Saves one mapping: a new adviser-and-manager row, or a changed manager on an existing row.
 *
 * <p>
 * An adviser may have several rows, one per manager. Adviser email + manager is the table's
 * alternate key, so the same pair cannot be saved twice; Dataverse enforces it.
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
      return { ok: false, reason: describeSaveFailure(result.error) };
    }

    return { ok: true };
  } catch (error) {
    logTechnical('adviser mapping save', error);
    return { ok: false, reason: describeSaveFailure(error) };
  }
}

/**
 * Sets the adviser's status, which OTIS follows over IO; null hands the decision back to IO.
 * One row is enough: the server copies it to the adviser's other rows and open cases, and
 * refuses anyone without the adviser.status capability.
 */
export async function saveAdviserStatus(
  rowId: string,
  status: number | null,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    const result = await Al_advisermappingsService.update(rowId, {
      // null, not undefined: undefined is dropped from the request and clears nothing.
      al_adviserstatus: status as unknown as Al_advisermappingsal_adviserstatus,
    });
    if (!result.success) {
      logTechnical('adviser status save', result.error);
      return { ok: false, reason: describeSaveFailure(result.error) };
    }
    return { ok: true };
  } catch (error) {
    logTechnical('adviser status save', error);
    return { ok: false, reason: describeSaveFailure(error) };
  }
}

/**
 * Removes one adviser-and-manager row. That manager stops being told about the adviser's cases
 * and AdviserMappingPlugin takes the cases off their list; the adviser's other managers, if
 * any, carry on. With none left, nobody can sign the adviser's cases off.
 *
 * Deleted rather than deactivated. Adviser + manager is an alternate key, and an alternate
 * key holds against inactive rows too, so a deactivated row would make that manager
 * impossible to give the adviser again, with no row on the page to explain why.
 *
 * No history is lost by deleting: the mapping carries none, and the audit trail for a
 * sign-off records who was told at the time it happened (F18).
 */
export async function removeAdviserMapping(
  id: string,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    await Al_advisermappingsService.delete(id);
    return { ok: true };
  } catch (error) {
    logTechnical('adviser mapping remove', error);
    return { ok: false, reason: describeRemoveFailure(error) };
  }
}
