import type { ReviewType } from '../../types/domain';
import {
  Al_reviewinstancesal_reviewstatus,
  Al_reviewinstancesal_reviewtype,
  type Al_reviewinstances,
} from '../../generated/models/Al_reviewinstancesModel';
import { date } from '../../lib/format';
import { lookupLabel } from './lookupLabel';

/**
 * One Tax or AQS check on a case, as the Checks table and the reallocation control read it.
 *
 * Split out of `useCaseReviews` so the mapping is testable: that module imports
 * `../../generated`, which cannot be loaded outside the hosted runtime — the same reason
 * `adviserMappingRows.ts` exists.
 */
export interface CaseReview {
  id: string;
  reference: string;
  type: ReviewType | string;
  status: string;
  sequence: number;
  startedOn: string | null;
  submittedOn: string | null;
  /** Who holds the check. `al_AssignCase` makes the checker the review's record owner. */
  owner: string | null;
}

/**
 * The owner's name, however this read supplied it.
 *
 * F26, found in DEV on 2026-09-20. This read `owneridname` alone, which the Web API does
 * not return — it expresses a lookup's label as an annotation on `_ownerid_value` — so the
 * Checks table showed "Unassigned" against every allocated check. On case 900000006 it read
 * `Tax check | Tax | Assigned | Unassigned`: the Status column and the Owner column
 * contradicting each other in one row, with the case header two lines above naming the
 * checker correctly from a different field.
 *
 * `lookupLabel` is the helper `caseDetailMapping` and `caseWorklistMapping` already use for
 * exactly this. This mapper was the one that did not, which is also what F16 was on the
 * adviser mapping table — the third time the same read has been written by hand.
 */
export function toReview(record: Al_reviewinstances): CaseReview {
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
    owner: lookupLabel(record, 'ownerid', record.owneridname),
  };
}
