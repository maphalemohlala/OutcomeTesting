import { useEffect, useState } from 'react';
import { Al_failreasonsService } from '../../generated';
import type { Al_failreasonsal_category } from '../../generated/models/Al_failreasonsModel';
import { logTechnical } from '../../services/errors';
import { describeSaveFailure } from './listOptionFailure';
import {
  nextFailReasonCode,
  toFailReasonRows,
  type FailReasonDraft,
  type FailReasonRow,
} from './failReasons';

export type FailReasonsState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; rows: FailReasonRow[] };

type Outcome = { ok: true } | { ok: false; reason: string };

const UNAVAILABLE = 'The File Quality fail points could not be loaded right now.';

/** Every fail point, retired ones included, read fresh on each reload. */
export function useFailReasons(reloadKey = 0): FailReasonsState {
  const [state, setState] = useState<FailReasonsState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    Al_failreasonsService.getAll({ top: 500 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          logTechnical('fail reasons load', result.error);
          setState({ status: 'unavailable', reason: UNAVAILABLE });
          return;
        }
        setState({ status: 'ready', rows: toFailReasonRows(result.data) });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('fail reasons load', error);
        setState({ status: 'unavailable', reason: UNAVAILABLE });
      });
    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}

async function write(what: string, run: () => Promise<{ success: boolean; error?: unknown }>): Promise<Outcome> {
  try {
    const result = await run();
    if (!result.success) {
      logTechnical(what, result.error);
      return { ok: false, reason: describeSaveFailure(result.error) };
    }
    return { ok: true };
  } catch (error) {
    logTechnical(what, error);
    return { ok: false, reason: describeSaveFailure(error) };
  }
}

/**
 * Adds a fail point, or saves a change to one. The code is set once on create and never
 * edited: it is the alternate key the seed data and the MI are keyed on.
 */
export function saveFailReason(draft: FailReasonDraft, rows: readonly FailReasonRow[]): Promise<Outcome> {
  const fields = {
    al_name: draft.name.trim(),
    al_category: draft.category as Al_failreasonsal_category,
    al_displayorder: Number(draft.order.trim()),
  };
  return write('fail reason save', () =>
    draft.id
      ? Al_failreasonsService.update(draft.id, fields)
      : Al_failreasonsService.create({
          ...fields,
          al_failreasoncode: nextFailReasonCode(rows, draft.category),
          statecode: 0,
        }),
  );
}

/**
 * Retires a fail point: it leaves the checklist of every review that has not ticked it, and
 * stays on any review that has. Deactivating rather than deleting, because deleting a reason
 * would also delete its ticks on reviews already submitted.
 */
export function retireFailReason(id: string): Promise<Outcome> {
  return write('fail reason retire', () => Al_failreasonsService.update(id, { statecode: 1, statuscode: 2 }));
}

export function reinstateFailReason(id: string): Promise<Outcome> {
  return write('fail reason reinstate', () => Al_failreasonsService.update(id, { statecode: 0, statuscode: 1 }));
}
