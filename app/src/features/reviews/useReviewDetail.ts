import { useEffect, useState } from 'react';
import { isVersionEffective } from './versionEffective';
import { buildSectionFilter, referenceDay } from './sectionFilter';
import { answerOf } from './reviewAnswer';
import { date, text } from '../../lib/format';
import { buildSections, type ReviewSection } from './reviewSections';
import {
  caseHeaderFields,
  failPoints,
  type FailPoint,
  type HeaderField,
  type TickedAnswer,
} from './checklistForm';
import type { ReviewType } from '../../types/domain';
import { isRecordId } from '../../services/odata';
import {
  Al_reviewinstancesService,
  Al_responsesService,
  Al_questionversionsService,
  Al_questionsService,
  Al_sectionsService,
  Al_failreasonsService,
  Al_outcomecasesService,
  Al_al_failreason_al_responsesetService,
} from '../../generated';
import {
  Al_reviewinstancesal_reviewstatus,
  Al_reviewinstancesal_reviewtype,
  type Al_reviewinstances,
} from '../../generated/models/Al_reviewinstancesModel';
import type { Al_responses } from '../../generated/models/Al_responsesModel';
import {
  Al_questionversionsal_responsetype,
  type Al_questionversions,
} from '../../generated/models/Al_questionversionsModel';
import {
  Al_failreasonsal_category,
  type Al_failreasons,
} from '../../generated/models/Al_failreasonsModel';
import { choiceLabel } from '../../lib/choiceLabel';

export interface ReviewResponse extends TickedAnswer {
  answer: string | null;
  answeredOn: string | null;
}

export interface ReviewHeader {
  id: string;
  reference: string;
  type: ReviewType | string;
  status: string;
  sequence: number;
  checklistVersion: string | null;
  checklistVersionId: string | null;
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
  /** The document's case header block; null when the case could not be read. */
  caseHeader: HeaderField[] | null;
  /**
   * The team's sections with every question in force, in the sections' display order -
   * which is the order of the Checker Checklist document - each with its answer or none.
   */
  sections: ReviewSection<ReviewResponse>[];
  /** The standalone File Quality fail points block: the team's reasons, ticked where recorded. */
  failPoints: FailPoint[];
}

export type ReviewDetailState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; detail: ReviewDetail };

/** The al_section owner role per review discipline (AD-020). */
const OWNER_ROLE: Record<string, number> = { Tax: 120910100, AQS: 120910101 };

/**
 * Fail reasons are a many-to-many between al_response and al_failreason through
 * al_al_failreason_al_response, so they cannot be read off the response row. The generated
 * client has no $expand - IGetAllOptions is select/filter/orderBy/top/skip/count - so the
 * intersect is queried directly and joined here, which is what AnswerWriter does
 * server-side for the same reason.
 *
 * Chunked because the filter is an OR per response id and a review with many questions
 * would otherwise build a URL long enough to be refused.
 */
const LINK_CHUNK = 20;

async function linkedFailReasons(responseIds: string[]): Promise<Set<string>> {
  const linked = new Set<string>();
  if (responseIds.length === 0) return linked;

  const chunks: string[][] = [];
  for (let i = 0; i < responseIds.length; i += LINK_CHUNK) {
    chunks.push(responseIds.slice(i, i + LINK_CHUNK));
  }

  const linkResults = await Promise.all(
    chunks.map((chunk) =>
      Al_al_failreason_al_responsesetService.getAll({
        filter: chunk.map((id) => `al_responseid eq ${id}`).join(' or '),
        top: 1000,
      }),
    ),
  );

  for (const result of linkResults) {
    if (!result.success) continue;
    for (const link of result.data) {
      linked.add(link.al_failreasonid);
    }
  }

  return linked;
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
    versionId: record._al_questionversionid_value ?? null,
    question:
      text(version?.al_questiontext) ?? text(record.al_questionversionidname) ?? record.al_name,
    responseTypeValue: version?.al_responsetype ?? null,
    responseType: version
      ? version.al_responsetypename ??
        Al_questionversionsal_responsetype[version.al_responsetype] ??
        '—'
      : '—',
    answerChoice: record.al_answerchoice ?? null,
    answerChoices: record.al_answerchoices ?? [],
    answer: answerOf(record),
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
    checklistVersionId: record._al_checklistversionid_value ?? null,
    caseId: record._al_outcomecaseid_value ?? null,
    caseName: text(record.al_outcomecaseidname),
    owner: text(record.owneridname),
    startedOn: date(record.al_startedon),
    submittedOn: date(record.al_submittedon),
    isSubmitted: record.al_reviewstatus === 120910212,
    typeMismatch: type !== expected,
  };
}

function toFailReason(record: Al_failreasons) {
  return {
    id: record.al_failreasonid,
    name: record.al_name,
    category: choiceLabel(Al_failreasonsal_category, record.al_category, record.al_categoryname),
    categoryValue: record.al_category ?? null,
    order: record.al_displayorder ?? 0,
  };
}

/**
 * Reads one review instance and lays it out as the Checker Checklist form (BR-004).
 * Read-only: grading is a permissioned write path deferred under OD-007, so this view
 * records nothing. Row visibility is enforced by Dataverse security (BR-012); a review
 * the user may not see returns as unavailable rather than showing partial data.
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
      Al_questionsService.getAll({ top: 500 }),
      Al_failreasonsService.getAll({ top: 500 }),
    ])
      .then(async ([review, responses, versions, questions, reasons]) => {
        if (cancelled) return;
        if (!review.success || !review.data) {
          setState({
            status: 'unavailable',
            reason:
              'This review could not be loaded. It may not exist, or you may not have access to it.',
          });
          return;
        }

        const header = toHeader(review.data, expectedType);

        // The form is the team's: the sections this discipline owns (AD-020) on the
        // checklist version issued to the review. Read after the review because both keys
        // come off it. The case is the document's header block; either read failing leaves
        // its block absent rather than failing the page.
        // Owned by this discipline or by Both, and in force on the review's reference day
        // (AD-123). The reference day is computed once here and reused for the answers
        // below, so the sections and the answers can never be read as of different days.
        const ownerRole = OWNER_ROLE[header.type] ?? OWNER_ROLE[expectedType];
        const referenceDayValue = referenceDay(review.data.al_submittedon);
        const sectionFilter = buildSectionFilter(
          ownerRole,
          header.checklistVersionId ?? null,
          referenceDayValue,
        );

        const responseIds = responses.success
          ? responses.data.map((response) => response.al_responseid)
          : [];

        const [sections, outcomeCase, linked] = await Promise.all([
          Al_sectionsService.getAll({ filter: sectionFilter, top: 100 }),
          header.caseId
            ? Al_outcomecasesService.get(header.caseId).catch(() => null)
            : Promise.resolve(null),
          linkedFailReasons(responseIds).catch(() => new Set<string>()),
        ]);

        if (cancelled) return;

        // Answers are read against the versions in force on the review's reference day: its
        // submission day once submitted, otherwise today (AD-015, BR-013). A retired version
        // keeps the answers written against it, but showing them beside the current
        // version's is how one review came to list Tax check reason three times. An answer
        // whose version is unknown (the versions read failed) is kept rather than hidden.
        const asOf = new Date(referenceDayValue);
        const versionById = new Map<string, Al_questionversions>();
        const effective: Al_questionversions[] = [];
        if (versions.success) {
          for (const version of versions.data) {
            versionById.set(version.al_questionversionid, version);
            if (isVersionEffective(version, asOf)) effective.push(version);
          }
        }

        const rows = responses.success
          ? responses.data
              .filter((response) => {
                const version = response._al_questionversionid_value
                  ? versionById.get(response._al_questionversionid_value)
                  : undefined;
                return !version || isVersionEffective(version, asOf);
              })
              .map((response) => toResponse(response, versionById))
          : [];

        setState({
          status: 'ready',
          detail: {
            header,
            caseHeader:
              outcomeCase?.success && outcomeCase.data ? caseHeaderFields(outcomeCase.data) : null,
            sections: buildSections(
              sections.success
                ? sections.data.map((section) => ({
                    id: section.al_sectionid,
                    code: text(section.al_sectioncode),
                    name: section.al_name,
                    order: section.al_displayorder ?? 0,
                    helpText: text(section.al_helptext),
                  }))
                : [],
              questions.success
                ? questions.data.map((question) => ({
                    id: question.al_questionid,
                    sectionId: question._al_sectionid_value ?? null,
                    order: question.al_displayorder ?? 0,
                    code: question.al_questioncode ?? null,
                  }))
                : [],
              effective.map((version) => ({
                id: version.al_questionversionid,
                questionId: version._al_questionid_value ?? null,
                order: version.al_displayorder ?? 0,
                text: text(version.al_questiontext) ?? version.al_name,
                responseTypeValue: version.al_responsetype ?? null,
                responseType:
                  version.al_responsetypename ??
                  Al_questionversionsal_responsetype[version.al_responsetype] ??
                  '—',
                mandatory: version.al_ismandatory,
              })),
              rows,
            ),
            failPoints: failPoints(
              reasons.success ? reasons.data.map(toFailReason) : [],
              linked,
            ),
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
