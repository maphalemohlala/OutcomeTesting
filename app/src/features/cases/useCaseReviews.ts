import { useEffect, useState } from 'react';
import type { ReviewType } from '../../types/domain';
import { isRecordId } from '../../services/odata';
import { Al_reviewinstancesService } from '../../generated';
import {
  Al_reviewinstancesal_reviewstatus,
  Al_reviewinstancesal_reviewtype,
  type Al_reviewinstances,
} from '../../generated/models/Al_reviewinstancesModel';

export interface CaseReview {
  id: string;
  reference: string;
  type: ReviewType | string;
  status: string;
  sequence: number;
  startedOn: string | null;
  submittedOn: string | null;
  owner: string | null;
}

export type CaseReviewsState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; reviews: CaseReview[] };

function date(value: string | undefined): string | null {
  if (!value) return null;
  const time = new Date(value).getTime();
  if (Number.isNaN(time)) return null;
  return new Date(time).toLocaleDateString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

function toReview(record: Al_reviewinstances): CaseReview {
  return {
    id: record.al_reviewinstanceid,
    reference: record.al_name?.trim() || record.al_reviewinstancecode,
    type: record.al_reviewtypename ?? Al_reviewinstancesal_reviewtype[record.al_reviewtype] ?? '—',
    status:
      record.al_reviewstatusname ??
      Al_reviewinstancesal_reviewstatus[record.al_reviewstatus] ??
      '—',
    sequence: record.al_sequence ?? 0,
    startedOn: date(record.al_startedon),
    submittedOn: date(record.al_submittedon),
    owner: record.owneridname?.trim() || null,
  };
}

/**
 * Lists the Tax and AQS checks raised for one case (BR-004), ordered by sequence so
 * Tax precedes AQS. Row visibility is enforced by Dataverse security (BR-012).
 */
export function useCaseReviews(caseId: string | undefined): CaseReviewsState {
  const [state, setState] = useState<CaseReviewsState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    // The id comes from the route, so it is untrusted until it parses as a record id;
    // an id that is not one never reaches a filter. The unavailable state for that case
    // is derived below rather than set here.
    if (!isRecordId(caseId)) return;
    let cancelled = false;

    Al_reviewinstancesService.getAll({
      filter: `_al_outcomecaseid_value eq ${caseId}`,
      orderBy: ['al_sequence asc'],
      top: 50,
    })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          setState({ status: 'unavailable' });
          return;
        }
        setState({ status: 'ready', reviews: result.data.map(toReview) });
      })
      .catch(() => {
        if (cancelled) return;
        setState({ status: 'unavailable' });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId]);

  if (caseId && !isRecordId(caseId)) {
    return { status: 'unavailable' };
  }

  return state;
}
