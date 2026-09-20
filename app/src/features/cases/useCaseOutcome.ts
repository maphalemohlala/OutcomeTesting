import { useEffect, useState } from 'react';
import { isRecordId } from '../../services/odata';
import { Al_outcomesService } from '../../generated';
import {
  Al_outcomesal_finaloutcome,
  Al_outcomesal_initialoutcome,
  type Al_outcomes,
} from '../../generated/models/Al_outcomesModel';
import { date } from '../../lib/format';
import { lookupLabel } from './lookupLabel';
import { recordedFlags, type AccountabilityFlags } from './failAccountability';

export interface CaseOutcomeRow {
  id: string;
  reference: string;
  reviewInstance: string | null;
  initialOutcome: string;
  finalOutcome: string | null;
  regraded: boolean;
  finalisedOn: string | null;
  /**
   * Who has been recorded as carrying a fail, exactly as the row holds it (item 8,
   * 2026-09-19). Four falses means nobody has said yet, and the export derives the pair -
   * effectiveAccountability is what turns that into what the extract will actually carry.
   */
  accountability: AccountabilityFlags;
  /**
   * The contact named as carrying each fail, where someone other than the case's own
   * adviser or paraplanner was chosen (item 8, 2026-09-19). Null means the extract uses
   * the person the case itself names.
   */
  fqAccountable: { id: string; name: string } | null;
  aqAccountable: { id: string; name: string } | null;
  /** Needed by al_SetFailAccountability, which targets the outcome. */
  rowVersion: string | null;
}

export type CaseOutcomeState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; outcomes: CaseOutcomeRow[] };


/** A lookup as the panel needs it: the id to send back, and a name to show. */
function namedContact(id?: string, name?: string | null): { id: string; name: string } | null {
  if (!id) return null;
  return { id, name: name?.trim() || 'Someone not in the directory' };
}

function toOutcome(record: Al_outcomes): CaseOutcomeRow {
  return {
    id: record.al_outcomeid,
    reference: record.al_name?.trim() || record.al_outcomecode,
    reviewInstance: lookupLabel(record, 'al_reviewinstanceid', record.al_reviewinstanceidname),
    initialOutcome:
      record.al_initialoutcomename ??
      Al_outcomesal_initialoutcome[record.al_initialoutcome] ??
      '—',
    finalOutcome:
      record.al_finaloutcomename ??
      (record.al_finaloutcome !== undefined
        ? Al_outcomesal_finaloutcome[record.al_finaloutcome]
        : null),
    regraded: Boolean(record.al_regradedon),
    finalisedOn: date(record.al_finalisedon),
    accountability: recordedFlags(record),
    fqAccountable: namedContact(
      record._al_fqaccountablecontactid_value,
      lookupLabel(record, 'al_fqaccountablecontactid', record.al_fqaccountablecontactidname),
    ),
    aqAccountable: namedContact(
      record._al_aqaccountablecontactid_value,
      lookupLabel(record, 'al_aqaccountablecontactid', record.al_aqaccountablecontactidname),
    ),
    rowVersion: record.versionnumber != null ? String(record.versionnumber) : null,
  };
}

/**
 * The preserved initial and final outcomes recorded against one case (BR-007), one row
 * per graded review. Read-only; the regrade and sign-off write paths are server-side
 * commands (AD-003). Row visibility is enforced by Dataverse security (BR-012).
 */
export function useCaseOutcome(caseId: string | undefined, reloadKey = 0): CaseOutcomeState {
  const [state, setState] = useState<CaseOutcomeState>({ status: 'loading' });
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
    void reloadKey; // named in the dependency list below; recording accountability re-reads.

    Al_outcomesService.getAll({
      filter: `_al_outcomecaseid_value eq ${caseId}`,
      top: 50,
    })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          setState({ status: 'unavailable' });
          return;
        }
        setState({ status: 'ready', outcomes: result.data.map(toOutcome) });
      })
      .catch(() => {
        if (cancelled) return;
        setState({ status: 'unavailable' });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId, reloadKey]);

  if (caseId && !isRecordId(caseId)) {
    return { status: 'unavailable' };
  }

  return state;
}
