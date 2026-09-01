import { useEffect, useState } from 'react';
import {
  Al_auditeventsService,
  Al_outcomesService,
  Al_remediationactionsService,
  Al_reviewinstancesService,
  Al_signoffsService,
} from '../../generated';
import { Al_auditeventsal_command } from '../../generated/models/Al_auditeventsModel';
import { logTechnical } from '../../services/errors';
import { choiceLabel } from './choiceLabel';

export interface HistoryEntry {
  id: string;
  occurredOn: string | null;
  command: string;
  actor: string | null;
  target: string;
  reason: string | null;
  details: string | null;
}

export type CaseHistoryState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; entries: HistoryEntry[] };

/** Friendly names for the tables a case's audit events can target. */
const TARGET_LABELS: Record<string, string> = {
  al_outcomecase: 'Case',
  al_reviewinstance: 'Check',
  al_remediationaction: 'Remediation action',
  al_outcome: 'Outcome',
  al_signoff: 'Sign-off',
};

async function relatedIds(caseId: string): Promise<string[]> {
  const filter = `_al_outcomecaseid_value eq ${caseId}`;
  const [reviews, actions, outcomes, signoffs] = await Promise.all([
    Al_reviewinstancesService.getAll({ filter, top: 200 }),
    Al_remediationactionsService.getAll({ filter, top: 200 }),
    Al_outcomesService.getAll({ filter, top: 200 }),
    Al_signoffsService.getAll({ filter, top: 200 }),
  ]);

  const ids = [caseId];
  if (reviews.success) ids.push(...reviews.data.map((r) => r.al_reviewinstanceid));
  if (actions.success) ids.push(...actions.data.map((a) => a.al_remediationactionid));
  if (outcomes.success) ids.push(...outcomes.data.map((o) => o.al_outcomeid));
  if (signoffs.success) ids.push(...signoffs.data.map((s) => s.al_signoffid));
  return ids;
}

function formatDate(iso: string | undefined): string | null {
  if (!iso) return null;
  const time = new Date(iso).getTime();
  return Number.isNaN(time) ? null : new Date(time).toLocaleString('en-GB');
}

/**
 * Reads the immutable command trail for one case (FR-033, BR-012, NFR-AUD-01). Audit events
 * are written by the server-side commands against whichever record they changed, so the case's
 * own events are joined with those of its checks, remediation actions, outcomes and sign-offs.
 *
 * Read access is governed by Dataverse privilege on al_auditevent: a role without it gets the
 * unavailable state rather than a misleadingly empty trail.
 */
export function useCaseHistory(caseId: string | undefined, reloadKey = 0): CaseHistoryState {
  const [state, setState] = useState<CaseHistoryState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;

    relatedIds(caseId)
      .then((ids) => {
        const filter = ids.map((id) => `al_targetid eq '${id}'`).join(' or ');
        return Al_auditeventsService.getAll({
          filter,
          orderBy: ['al_occurredon desc'],
          top: 500,
        });
      })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          logTechnical('case history load', result.error);
          setState({
            status: 'unavailable',
            reason:
              'The history for this case could not be loaded. You may not have access to the audit trail.',
          });
          return;
        }
        setState({
          status: 'ready',
          entries: result.data.map((event) => ({
            id: event.al_auditeventid,
            occurredOn: formatDate(event.al_occurredon ?? event.createdon),
            command:
              choiceLabel(Al_auditeventsal_command, event.al_command, event.al_commandname) ??
              'Unknown command',
            actor: event.al_actorname?.trim() || event.createdbyname?.trim() || null,
            target: TARGET_LABELS[event.al_targettable ?? ''] ?? event.al_targettable ?? '—',
            reason: event.al_reason?.trim() || null,
            details: event.al_details?.trim() || null,
          })),
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('case history load', error);
        setState({
          status: 'unavailable',
          reason: 'The history for this case could not be loaded right now.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId, reloadKey]);

  if (!caseId) {
    return { status: 'unavailable', reason: 'No case was requested.' };
  }

  return state;
}
