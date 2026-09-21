import { useEffect, useState } from 'react';
import { Al_listoptionsService } from '../../generated';
import { logTechnical } from '../../services/errors';
import { today } from './effectiveDay';
import {
  toOptionRows,
  type ListOptionRow,
  type ManagedList,
  type OptionDraft,
  type RawListOption,
} from './listOptions';
import { describeDeleteFailure, describeSaveFailure } from './listOptionFailure';

// The shaping lives in listOptions so it can be tested; this module cannot be imported under
// the test runner because ../../generated pulls in the Power Apps client.
export type { ListOptionRow, ManagedList, OptionDraft } from './listOptions';

export type ListOptionsState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; options: ListOptionRow[] };

export type AllListOptionsState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; rows: RawListOption[] };

const UNAVAILABLE = 'The options for this list could not be loaded right now.';

/**
 * Every managed list's options, unshaped.
 *
 * One read for all four lists rather than one per list: they share a table, the whole
 * catalogue is a handful of rows, and a screen showing four dropdowns should not make four
 * round trips to fill them. Shaping per list is `toOptionRows`, which is pure and tested.
 *
 * Read fresh on every reload rather than cached: an administrator who has just added an
 * option and is looking at the table to see whether it worked is the whole audience for the
 * management screen, and a stale read there reads as a failure.
 */
export function useAllListOptions(reloadKey = 0): AllListOptionsState {
  const [state, setState] = useState<AllListOptionsState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_listoptionsService.getAll({ orderBy: ['al_sortorder asc', 'al_name asc'] })
      .then((result) => {
        if (cancelled) return;

        if (!result.success || !result.data) {
          logTechnical('list options load', result.error);
          setState({ status: 'unavailable', reason: UNAVAILABLE });
          return;
        }

        setState({ status: 'ready', rows: result.data });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('list options load', error);
        setState({ status: 'unavailable', reason: UNAVAILABLE });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}

/** The options of ONE list, retired ones included and flagged. */
export function useListOptions(list: ManagedList, reloadKey = 0): ListOptionsState {
  const all = useAllListOptions(reloadKey);
  if (all.status !== 'ready') return all;
  return { status: 'ready', options: toOptionRows(all.rows, list, new Date()) };
}

/** Blank rather than a number, so clearing the sort order clears it instead of writing 0. */
function sortOrderOf(draft: OptionDraft): number | undefined {
  const order = draft.sortOrder.trim();
  return order === '' ? undefined : Number(order);
}

/**
 * Adds an option, or saves an edit to one.
 *
 * `al_legacyvalue` is never written here. It records which choice value an option was
 * migrated from and is set once by the migration; letting this page change it would let an
 * administrator silently repoint every case that still holds the old integer.
 */
export async function saveListOption(
  list: ManagedList,
  draft: OptionDraft,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  const label = draft.label.trim();

  try {
    const result = draft.id
      ? await Al_listoptionsService.update(draft.id, {
          al_name: label,
          al_sortorder: sortOrderOf(draft),
          al_effectivefrom: draft.effectiveFrom ?? undefined,
          al_effectiveto: draft.effectiveTo ?? undefined,
        })
      : await Al_listoptionsService.create({
          al_name: label,
          al_list: list.value,
          al_sortorder: sortOrderOf(draft),
          al_effectivefrom: draft.effectiveFrom ?? undefined,
          al_effectiveto: draft.effectiveTo ?? undefined,
          // The generated model types statecode as required on create and keys it by the
          // option VALUE, not its label. Dataverse would default it, but the contract asks
          // for it, so it is stated rather than cast away.
          statecode: 0,
        });

    if (!result.success) {
      logTechnical('list option save', result.error);
      return { ok: false, reason: describeSaveFailure(result.error) };
    }

    return { ok: true };
  } catch (error) {
    logTechnical('list option save', error);
    return { ok: false, reason: describeSaveFailure(error) };
  }
}

/**
 * Retires an option: it stops being offered, and every case already holding it is untouched.
 *
 * This is what "remove" means on this page, and it is the action offered first. Effective
 * dating rather than deactivating the row, so it reads the same way the checklist does and
 * so a case that holds the option still resolves its name.
 */
export async function retireListOption(
  id: string,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    const result = await Al_listoptionsService.update(id, { al_effectiveto: today() });
    if (!result.success) {
      logTechnical('list option retire', result.error);
      return { ok: false, reason: describeSaveFailure(result.error) };
    }
    return { ok: true };
  } catch (error) {
    logTechnical('list option retire', error);
    return { ok: false, reason: describeSaveFailure(error) };
  }
}

/** Puts a retired option back into the dropdown, because retiring is meant to be reversible. */
export async function reinstateListOption(
  id: string,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    const result = await Al_listoptionsService.update(id, { al_effectiveto: undefined });
    if (!result.success) {
      logTechnical('list option reinstate', result.error);
      return { ok: false, reason: describeSaveFailure(result.error) };
    }
    return { ok: true };
  } catch (error) {
    logTechnical('list option reinstate', error);
    return { ok: false, reason: describeSaveFailure(error) };
  }
}

/**
 * Deletes an option outright.
 *
 * Offered, rather than hidden behind retire, because an option added by mistake and never
 * used should be removable without leaving a tombstone an administrator has to explain. It
 * is safe to offer precisely because this page is not what decides it: the lookup carries
 * CascadeType.Restrict, so Dataverse refuses the delete the moment any case holds the
 * option, and `describeDeleteFailure` turns that refusal into the suggestion to retire.
 */
export async function deleteListOption(
  id: string,
): Promise<{ ok: true } | { ok: false; reason: string }> {
  try {
    await Al_listoptionsService.delete(id);
    return { ok: true };
  } catch (error) {
    logTechnical('list option delete', error);
    return { ok: false, reason: describeDeleteFailure(error) };
  }
}
