import { useEffect, useState } from 'react';
import type { CaseStatus, ReviewRoute } from '../../types/domain';
import { REVIEW_ROUTES } from '../../types/domain';
import { Al_outcomecasesService } from '../../generated';
import { logTechnical } from '../../services/errors';
import {
  Al_outcomecasesal_casestatus,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';

export interface CaseField {
  label: string;
  value: string | null;
}

/**
 * Raw current values for the editable case attributes, keyed by al_outcomecase logical
 * name. Choice fields carry the numeric option value (or null), dates carry yyyy-MM-dd
 * (or ''), text carries the string. Used to prefill and diff the edit form.
 */
export interface CaseEditValues {
  al_clientname: string;
  al_advisername: string;
  al_advisercode: string;
  al_adviserstatus: number | null;
  al_paraplanner: string;
  al_paraplannercode: string;
  al_products: string;
  al_casetype: number | null;
  al_advicedate: string;
  al_productsolutiontype: number | null;
  al_samplesource: number | null;
  al_checkername: string;
  al_checkdate: string;
  al_preorpostcheck: number | null;
  al_vulnerableclient: number | null;
  al_taxcheckrequired: number | null;
  al_taxteamdisposition: number | null;
  al_casestatus: number;
  al_priority: number | null;
  al_duedate: string;
}

export interface CaseDetail {
  id: string;
  title: string;
  caseReference: string;
  status: CaseStatus;
  statusValue: number;
  route: ReviewRoute | null;
  owner: string | null;
  ageInDays: number;
  priority: string | null;
  dueDate: string | null;
  rowVersion: string | null;
  previousCase: string | null;
  client: CaseField[];
  adviser: CaseField[];
  adviceAndProduct: CaseField[];
  checkAndTax: CaseField[];
  edit: CaseEditValues;
}

export type CaseDetailState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; detail: CaseDetail };

function toRoute(name: string | undefined): ReviewRoute | null {
  return REVIEW_ROUTES.find((route) => route === name) ?? null;
}

function ageInDays(createdOn: string | undefined): number {
  if (!createdOn) return 0;
  const created = new Date(createdOn).getTime();
  if (Number.isNaN(created)) return 0;
  return Math.max(0, Math.floor((Date.now() - created) / 86_400_000));
}

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

/** ISO/date string to yyyy-MM-dd for a date input, or '' when absent/invalid. */
function ymd(value: string | undefined): string {
  if (!value) return '';
  const time = new Date(value).getTime();
  if (Number.isNaN(time)) return '';
  return new Date(time).toISOString().slice(0, 10);
}

function opt(value: number | undefined): number | null {
  return value == null ? null : value;
}

function toDetail(record: Al_outcomecases): CaseDetail {
  // al_priority/al_duedate are read defensively so the app builds before the data
  // source is regenerated to include the new columns; the numeric priority is mapped
  // to a label locally in case the formatted name is not returned.
  const extra = record as Al_outcomecases & {
    al_priority?: number;
    al_priorityname?: string;
    al_duedate?: string;
  };
  const PRIORITY_LABELS: Record<number, string> = {
    120910780: 'Low',
    120910781: 'Normal',
    120910782: 'High',
    120910783: 'Urgent',
  };
  const priorityLabel =
    text(extra.al_priorityname) ??
    (extra.al_priority != null ? (PRIORITY_LABELS[extra.al_priority] ?? null) : null);
  return {
    id: record.al_outcomecaseid,
    title: text(record.al_name) ?? record.al_casereference,
    caseReference: record.al_casereference,
    status: Al_outcomecasesal_casestatus[record.al_casestatus] as CaseStatus,
    statusValue: record.al_casestatus,
    route: toRoute(record.al_reviewrouteidname),
    owner: text(record.owneridname),
    ageInDays: ageInDays(record.createdon),
    priority: priorityLabel,
    dueDate: date(extra.al_duedate),
    rowVersion: record.versionnumber != null ? String(record.versionnumber) : null,
    previousCase: text(record.al_previouscaseidname),
    client: [
      { label: 'Client', value: text(record.al_clientname) },
      { label: 'Vulnerable client', value: text(record.al_vulnerableclientname) },
    ],
    adviser: [
      { label: 'Adviser', value: text(record.al_advisername) },
      { label: 'Adviser code', value: text(record.al_advisercode) },
      { label: 'Adviser status', value: text(record.al_adviserstatusname) },
      { label: 'Paraplanner', value: text(record.al_paraplanner) },
      { label: 'Paraplanner code', value: text(record.al_paraplannercode) },
    ],
    adviceAndProduct: [
      { label: 'Case type', value: text(record.al_casetypename) },
      { label: 'Product/solution type', value: text(record.al_productsolutiontypename) },
      { label: 'Products', value: text(record.al_products) },
      { label: 'Advice date', value: date(record.al_advicedate) },
      { label: 'Sample source', value: text(record.al_samplesourcename) },
      { label: 'Check point', value: text(record.al_preorpostcheckname) },
    ],
    checkAndTax: [
      { label: 'Checker', value: text(record.al_checkername) },
      { label: 'Check date', value: date(record.al_checkdate) },
      { label: 'Tax check required', value: text(record.al_taxcheckrequiredname) },
      { label: 'Tax team disposition', value: text(record.al_taxteamdispositionname) },
    ],
    edit: {
      al_clientname: text(record.al_clientname) ?? '',
      al_advisername: text(record.al_advisername) ?? '',
      al_advisercode: text(record.al_advisercode) ?? '',
      al_adviserstatus: opt(record.al_adviserstatus),
      al_paraplanner: text(record.al_paraplanner) ?? '',
      al_paraplannercode: text(record.al_paraplannercode) ?? '',
      al_products: text(record.al_products) ?? '',
      al_casetype: opt(record.al_casetype),
      al_advicedate: ymd(record.al_advicedate),
      al_productsolutiontype: opt(record.al_productsolutiontype),
      al_samplesource: opt(record.al_samplesource),
      al_checkername: text(record.al_checkername) ?? '',
      al_checkdate: ymd(record.al_checkdate),
      al_preorpostcheck: opt(record.al_preorpostcheck),
      al_vulnerableclient: opt(record.al_vulnerableclient),
      al_taxcheckrequired: opt(record.al_taxcheckrequired),
      al_taxteamdisposition: opt(record.al_taxteamdisposition),
      al_casestatus: record.al_casestatus,
      al_priority: opt(extra.al_priority),
      al_duedate: ymd(extra.al_duedate),
    },
  };
}

/**
 * Reads a single Outcome Case by id from Dataverse. Row-level visibility is enforced
 * by Dataverse security (BR-012): a case the signed-in user may not see returns as
 * unavailable rather than showing partial data.
 */
export function useCaseDetail(caseId: string | undefined, reloadKey = 0): CaseDetailState {
  const [state, setState] = useState<CaseDetailState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  // Reset to loading when the route's case changes, without a setState-in-effect.
  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;

    Al_outcomecasesService.get(caseId)
      .then((result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          if (!result.success) {
            logTechnical('case detail load', result.error);
          }
          setState({
            status: 'unavailable',
            reason:
              'The selected case could not be found. It may have been removed, or you may not have access to it.',
          });
          return;
        }
        setState({ status: 'ready', detail: toDetail(result.data) });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('case detail load', error);
        setState({
          status: 'unavailable',
          reason: 'This case could not be loaded right now. Please try again later.',
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
