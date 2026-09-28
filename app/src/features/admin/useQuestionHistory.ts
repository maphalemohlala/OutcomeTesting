import { useEffect, useState } from 'react';
import { Al_auditeventsService, Al_questionversionsService } from '../../generated';
import { logTechnical } from '../../services/errors';
import { isRecordId } from '../../services/odata';
import { questionHistory, type HistoryEntry } from './questionHistory';

/** A question's or a section's history, as the history modal shows it. */
export type HistoryState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; entries: HistoryEntry[] };

const UNAVAILABLE =
  'The history for this question could not be loaded. You may not have access to the audit trail.';

/**
 * Reads one question's versions and the audit events written against it or any of them, and
 * hands both to questionHistory, which holds the rules and is tested there.
 *
 * Read access to the trail is Dataverse privilege on al_auditevent, as for a case's history:
 * a role without it gets the unavailable state rather than a history with no names in it.
 */
export function useQuestionHistory(questionId: string): HistoryState {
  const [state, setState] = useState<HistoryState>({ status: 'loading' });

  useEffect(() => {
    // Interpolated into two filters, one inside quotes, so it must parse as a record id
    // first (services/odata.ts). It comes from the library's own rows, but the rule is kept.
    if (!isRecordId(questionId)) return;
    let cancelled = false;

    Al_questionversionsService.getAll({ filter: `_al_questionid_value eq ${questionId}`, top: 500 })
      .then(async (versions) => {
        if (!versions.success) throw versions.error;
        const ids = [questionId, ...versions.data.map((v) => v.al_questionversionid)];
        const events = await Al_auditeventsService.getAll({
          filter: ids.map((id) => `al_targetid eq '${id}'`).join(' or '),
          top: 500,
        });
        if (!events.success) throw events.error;
        return questionHistory(questionId, versions.data, events.data);
      })
      .then((entries) => {
        if (!cancelled) setState({ status: 'ready', entries });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('question history load', error);
        setState({ status: 'unavailable', reason: UNAVAILABLE });
      });

    return () => {
      cancelled = true;
    };
  }, [questionId]);

  return isRecordId(questionId) ? state : { status: 'unavailable', reason: UNAVAILABLE };
}
