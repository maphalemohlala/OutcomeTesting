import { useEffect, useState } from 'react';
import { isRecordId } from '../../services/odata';
import { Al_usersService } from '../../generated';
import type { Al_users } from '../../generated/models/Al_usersModel';

export interface AllocationCandidate {
  id: string;
  name: string;
  workEmail: string;
}

export type CandidatesState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; candidates: AllocationCandidate[] };

/**
 * The people a case may be allocated to: the active application user registry (AD-041),
 * keyed on work email because that is the canonical cross-system identifier the command
 * resolves both the systemuser and the portal contact from (OD-003, AD-010).
 *
 * The registry is the list to pick from, not the authority on whether the allocation will
 * succeed. al_AssignCase re-resolves the email server-side and refuses if either identity
 * is missing, so a registry row for someone with no portal contact is offered here and
 * refused there, with a message that says which half is absent.
 */
export function useAllocationCandidates(reloadKey = 0): CandidatesState {
  const [state, setState] = useState<CandidatesState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_usersService.getAll({ orderBy: ['al_name asc'], top: 500 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          setState({ status: 'unavailable' });
          return;
        }

        const candidates = result.data
          .filter((user: Al_users) => user.al_isactive !== false)
          .filter((user: Al_users) => Boolean(user.al_workemail?.trim()))
          .map((user: Al_users) => ({
            id: user.al_userid,
            name: user.al_name?.trim() || user.al_workemail,
            workEmail: user.al_workemail.trim(),
          }));

        setState({ status: 'ready', candidates });
      })
      .catch(() => {
        if (!cancelled) setState({ status: 'unavailable' });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}

export { isRecordId };
