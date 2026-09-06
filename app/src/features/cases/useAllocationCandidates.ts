import { useEffect, useState } from 'react';
import { isRecordId } from '../../services/odata';
import { ContactsService } from '../../generated';
import { toDirectoryUser } from '../../hooks/useUserDirectory';

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
 * The people a case may be allocated to: the active contacts (AD-041), keyed on work
 * email because that is the canonical cross-system identifier the command resolves both
 * the systemuser and the portal contact from (OD-003, AD-010).
 *
 * The registry is the list to pick from, not the authority on whether the allocation will
 * succeed. al_AssignCase re-resolves the email server-side and refuses if either identity
 * is missing, so a contact with no matching systemuser is offered here and refused there,
 * with a message that says which half is absent.
 */
export function useAllocationCandidates(reloadKey = 0): CandidatesState {
  const [state, setState] = useState<CandidatesState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    ContactsService.getAll({ orderBy: ['fullname asc'], top: 500 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          setState({ status: 'unavailable' });
          return;
        }

        const candidates = result.data
          .map(toDirectoryUser)
          .filter((user) => user !== null)
          .filter((user) => user.active)
          .map((user) => ({ id: user.id, name: user.name, workEmail: user.email }));

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
