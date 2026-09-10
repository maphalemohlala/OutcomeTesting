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
import { date, text } from '../../lib/format';

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
  remedialAction: string | null;
  evidenceReference: string | null;
  clientContactRequired: string | null;
  recheckRequired: string | null;
  changesAdvice: string | null;
  assignedTo: string | null;
  /**
   * Raw, unformatted. The BR-010 clock counts working days off createdOn and
   * clockStartedOn, and "the most recently completed action" has to compare completions -
   * neither of which a display date like "22 Sep 2026" can answer.
   */
  createdOn: string | null;
  clockStartedOn: string | null;
  completedOnRaw: string | null;
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
  /**
   * The action this sign-off is against.
   *
   * The id, not the name: every action a review raises shares one al_name ("Remediation
   * IO-SEED-TAX-01"), so a sign-off matched by name would attach to all of them alike.
   */
  remediationActionId: string | null;
  signedOffBy: string | null;
}



/** Labels for the three remedial-form answers (AD-095); values from the 1209107xx band. */
export const CLIENT_CONTACT_REQUIRED: Record<number, string> = {
  120910793: 'Yes',
  120910794: 'No',
  120910795: 'Potentially',
};
export const RECHECK_REQUIRED: Record<number, string> = { 120910796: 'Yes', 120910797: 'No' };
export const CHANGES_ADVICE: Record<number, string> = { 120910798: 'Yes', 120910799: 'No' };

/** The SDK returns an empty choice as null, and the generated model does not know these columns yet. */
function choice(record: Al_remediationactions, attr: string, labels: Record<number, string>): string | null {
  const value = (record as unknown as Record<string, unknown>)[attr];
  return typeof value === 'number' ? (labels[value] ?? String(value)) : null;
}

export function toAction(record: Al_remediationactions): RemediationActionRow {
  const extra = record as unknown as Record<string, unknown>;
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
    assignedTo: text(extra.al_assignedcontactidname as string | undefined),
    remedialAction: text(extra.al_adviserresponse as string | undefined),
    evidenceReference: text(extra.al_evidencereference as string | undefined),
    clientContactRequired: choice(record, 'al_clientcontactrequired', CLIENT_CONTACT_REQUIRED),
    recheckRequired: choice(record, 'al_recheckrequired', RECHECK_REQUIRED),
    changesAdvice: choice(record, 'al_changesadvice', CHANGES_ADVICE),
    createdOn: (extra.createdon as string | undefined) ?? null,
    clockStartedOn: (extra.al_clockstartedon as string | undefined) ?? null,
    completedOnRaw: record.al_completedon ?? null,
    rowVersion: record.versionnumber != null ? String(record.versionnumber) : null,
  };
}

export function toOutcome(record: Al_outcomes): OutcomeRow {
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

export function toSignoff(record: Al_signoffs): SignoffRow {
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
    remediationActionId:
      ((record as unknown as Record<string, unknown>)._al_remediationactionid_value as
        | string
        | undefined) ?? null,
    signedOffBy: text(record.owneridname),
  };
}
