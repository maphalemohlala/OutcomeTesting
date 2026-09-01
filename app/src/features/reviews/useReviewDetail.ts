import { useEffect, useState } from 'react';
import type { ReviewType } from '../../types/domain';
import { isRecordId } from '../../services/odata';
import {
  Al_reviewinstancesService,
  Al_responsesService,
  Al_questionversionsService,
} from '../../generated';
import {
  Al_reviewinstancesal_reviewstatus,
  Al_reviewinstancesal_reviewtype,
  type Al_reviewinstances,
} from '../../generated/models/Al_reviewinstancesModel';
import {
  Al_responsesal_answerchoice,
  Al_responsesal_answerchoices,
  type Al_responses,
} from '../../generated/models/Al_responsesModel';
import {
  Al_questionversionsal_responsetype,
  type Al_questionversions,
} from '../../generated/models/Al_questionversionsModel';

export interface ReviewResponse {
  id: string;
  order: number;
  question: string;
  responseType: string;
  answer: string | null;
  note: string | null;
  answeredOn: string | null;
}

export interface ReviewHeader {
  id: string;
  reference: string;
  type: ReviewType | string;
  status: string;
  sequence: number;
  checklistVersion: string | null;
  caseId: string | null;
  caseName: string | null;
  owner: string | null;
  startedOn: string | null;
  submittedOn: string | null;
  isSubmitted: boolean;
  typeMismatch: boolean;
}

export interface ReviewDetail {
  header: ReviewHeader;
  responses: ReviewResponse[];
}

export type ReviewDetailState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; detail: ReviewDetail };

function text(value: string | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

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

/** The reviewer's answer, whichever response field the question type populated. */
function answerOf(record: Al_responses): string | null {
  if (record.al_answerchoice !== undefined) {
    return (
      record.al_answerchoicename ?? Al_responsesal_answerchoice[record.al_answerchoice] ?? null
    );
  }
  if (record.al_answerchoices?.length) {
    return record.al_answerchoices
      .map((value) => Al_responsesal_answerchoices[value] ?? String(value))
      .join(', ');
  }
  if (text(record.al_answertext)) return text(record.al_answertext);
  return date(record.al_answerdate);
}

/** A free-text note kept alongside a structured answer (e.g. evidence for a Fail). */
function noteOf(record: Al_responses): string | null {
  const structured = record.al_answerchoice !== undefined || !!record.al_answerchoices?.length;
  return structured ? text(record.al_answertext) : null;
}

function toResponse(
  record: Al_responses,
  versions: Map<string, Al_questionversions>,
): ReviewResponse {
  const version = record._al_questionversionid_value
    ? versions.get(record._al_questionversionid_value)
    : undefined;
  return {
    id: record.al_responseid,
    order: version?.al_displayorder ?? 0,
    question:
      text(version?.al_questiontext) ?? text(record.al_questionversionidname) ?? record.al_name,
    responseType: version
      ? version.al_responsetypename ??
        Al_questionversionsal_responsetype[version.al_responsetype] ??
        '—'
      : '—',
    answer: answerOf(record),
    note: noteOf(record),
    answeredOn: date(record.al_answerdate) ?? date(record.modifiedon),
  };
}

function toHeader(record: Al_reviewinstances, expected: ReviewType): ReviewHeader {
  const type = Al_reviewinstancesal_reviewtype[record.al_reviewtype];
  const status =
    record.al_reviewstatusname ??
    Al_reviewinstancesal_reviewstatus[record.al_reviewstatus] ??
    '—';
  return {
    id: record.al_reviewinstanceid,
    reference: text(record.al_name) ?? record.al_reviewinstancecode,
    type: record.al_reviewtypename ?? type ?? '—',
    status,
    sequence: record.al_sequence ?? 0,
    checklistVersion: text(record.al_checklistversionidname),
    caseId: record._al_outcomecaseid_value ?? null,
    caseName: text(record.al_outcomecaseidname),
    owner: text(record.owneridname),
    startedOn: date(record.al_startedon),
    submittedOn: date(record.al_submittedon),
    isSubmitted: record.al_reviewstatus === 120910212,
    typeMismatch: type !== expected,
  };
}

/**
 * Reads one review instance and the answers captured against it (BR-004). Read-only:
 * grading is a permissioned write path deferred under OD-007, so this view records
 * nothing. Row visibility is enforced by Dataverse security (BR-012); a review the
 * user may not see returns as unavailable rather than showing partial data.
 */
export function useReviewDetail(
  reviewId: string | undefined,
  expectedType: ReviewType,
): ReviewDetailState {
  const [state, setState] = useState<ReviewDetailState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(reviewId);

  // Reset to loading when the route's review changes, without a setState-in-effect.
  if (reviewId !== loadedFor) {
    setLoadedFor(reviewId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    // The id comes from the route, so it is untrusted until it parses as a record id;
    // an id that is not one never reaches a filter. The unavailable state for that case
    // is derived below rather than set here.
    if (!isRecordId(reviewId)) return;
    let cancelled = false;

    Promise.all([
      Al_reviewinstancesService.get(reviewId),
      Al_responsesService.getAll({
        filter: `_al_reviewinstanceid_value eq ${reviewId}`,
        top: 200,
      }),
      Al_questionversionsService.getAll({ top: 500 }),
    ])
      .then(([review, responses, versions]) => {
        if (cancelled) return;
        if (!review.success || !review.data) {
          setState({
            status: 'unavailable',
            reason:
              'This review could not be loaded. It may not exist, or you may not have access to it.',
          });
          return;
        }

        const versionById = new Map<string, Al_questionversions>();
        if (versions.success) {
          for (const version of versions.data) {
            versionById.set(version.al_questionversionid, version);
          }
        }

        const rows = responses.success
          ? responses.data
              .map((response) => toResponse(response, versionById))
              .sort((a, b) => a.order - b.order)
          : [];

        setState({
          status: 'ready',
          detail: {
            header: toHeader(review.data, expectedType),
            responses: rows,
          },
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'This review could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reviewId, expectedType]);

  if (!reviewId) {
    return { status: 'unavailable', reason: 'No review was requested.' };
  }

  if (!isRecordId(reviewId)) {
    return {
      status: 'unavailable',
      reason: 'This review could not be loaded. It may not exist, or you may not have access to it.',
    };
  }

  return state;
}
