import { useEffect, useState } from 'react';
import {
  Al_outcomesService,
  Al_remediationactionsService,
  Al_signoffsService,
} from '../../generated';
import {
  Al_remediationactionsal_actionstatus,
  type Al_remediationactions,
} from '../../generated/models/Al_remediationactionsModel';
import {
  Al_outcomesal_finaloutcome,
  Al_outcomesal_initialoutcome,
  type Al_outcomes,
} from '../../generated/models/Al_outcomesModel';
import {
  Al_signoffsal_signoffdecision,
  type Al_signoffs,
} from '../../generated/models/Al_signoffsModel';

export interface RemediationActionRow {
  id: string;
  reference: string;
  description: string;
  status: string;
  dueOn: string | null;
  completedOn: string | null;
  triggeredBy: string | null;
  owner: string | null;
  rowVersion: string | null;
}

export interface OutcomeRow {
  id: string;
  reference: string;
  initialOutcome: string;
  finalOutcome: string | null;
  regradeReason: string | null;
  finalisedOn: string | null;
  regradedOn: string | null;
  reviewInstance: string | null;
}

export interface SignoffRow {
  id: string;
  reference: string;
  decision: string;
  notes: string | null;
  signedOffOn: string | null;
  remediationAction: string | null;
  signedOffBy: string | null;
}

export type RemediationState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | {
      status: 'ready';
      actions: RemediationActionRow[];
      outcomes: OutcomeRow[];
      signoffs: SignoffRow[];
    };

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

function text(value: string | undefined): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function toAction(record: Al_remediationactions): RemediationActionRow {
  return {
    id: record.al_remediationactionid,
    reference: text(record.al_name) ?? record.al_remediationactioncode,
    description: text(record.al_description) ?? '—',
    status:
      record.al_actionstatusname ??
      Al_remediationactionsal_actionstatus[record.al_actionstatus] ??
      '—',
    dueOn: date(record.al_duedate),
    completedOn: date(record.al_completedon),
    triggeredBy: text(record.al_reviewinstanceidname),
    owner: text(record.owneridname),
    rowVersion: record.versionnumber != null ? String(record.versionnumber) : null,
  };
}

function toOutcome(record: Al_outcomes): OutcomeRow {
  return {
    id: record.al_outcomeid,
    reference: text(record.al_name) ?? record.al_outcomecode,
    initialOutcome:
      record.al_initialoutcomename ??
      Al_outcomesal_initialoutcome[record.al_initialoutcome] ??
      '—',
    finalOutcome:
      record.al_finaloutcomename ??
      (record.al_finaloutcome !== undefined
        ? Al_outcomesal_finaloutcome[record.al_finaloutcome]
        : null),
    regradeReason: text(record.al_regradereason),
    finalisedOn: date(record.al_finalisedon),
    regradedOn: date(record.al_regradedon),
    reviewInstance: text(record.al_reviewinstanceidname),
  };
}

function toSignoff(record: Al_signoffs): SignoffRow {
  return {
    id: record.al_signoffid,
    reference: text(record.al_name) ?? record.al_signoffcode,
    decision:
      record.al_signoffdecisionname ??
      Al_signoffsal_signoffdecision[record.al_signoffdecision] ??
      '—',
    notes: text(record.al_notes),
    signedOffOn: date(record.al_signedoffon),
    remediationAction: text(record.al_remediationactionidname),
    signedOffBy: text(record.owneridname),
  };
}

/**
 * Reads the remediation actions (BR-006), preserved initial/final outcomes (BR-007)
 * and T&C Manager sign-offs (BR-008, FR-023) raised for one case. Read-only: the
 * complete, validate, regrade and sign-off write paths are permissioned server-side
 * commands (AD-003, AD-031). Row visibility is enforced by Dataverse security (BR-012).
 */
export function useRemediation(caseId: string | undefined, reloadKey = 0): RemediationState {
  const [state, setState] = useState<RemediationState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;

    const filter = `_al_outcomecaseid_value eq ${caseId}`;

    Promise.all([
      Al_remediationactionsService.getAll({ filter, orderBy: ['createdon asc'], top: 200 }),
      Al_outcomesService.getAll({ filter, orderBy: ['createdon asc'], top: 200 }),
      Al_signoffsService.getAll({ filter, orderBy: ['createdon asc'], top: 200 }),
    ])
      .then(([actionResult, outcomeResult, signoffResult]) => {
        if (cancelled) return;
        if (!actionResult.success || !outcomeResult.success || !signoffResult.success) {
          setState({
            status: 'unavailable',
            reason: 'The remediation record for this case could not be loaded from Dataverse.',
          });
          return;
        }
        setState({
          status: 'ready',
          actions: actionResult.data.map(toAction),
          outcomes: outcomeResult.data.map(toOutcome),
          signoffs: signoffResult.data.map(toSignoff),
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'The remediation record for this case could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId, reloadKey]);

  return state;
}
