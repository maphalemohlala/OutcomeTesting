import type { CaseStatus, ReviewRoute } from '../../types/domain';
import { REVIEW_ROUTES } from '../../types/domain';
import {
  Al_outcomecasesal_adviserstatus,
  Al_outcomecasesal_casestatus,
  Al_outcomecasesal_casetype,
  Al_outcomecasesal_preorpostcheck,
  Al_outcomecasesal_priority,
  Al_outcomecasesal_productsolutiontype,
  Al_outcomecasesal_samplesource,
  Al_outcomecasesal_taxcheckrequired,
  Al_outcomecasesal_taxteamdisposition,
  Al_outcomecasesal_vulnerableclient,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';
import { choiceLabel as choice } from './choiceLabel';
import { lookupLabel } from './lookupLabel';

/**
 * Pure record-to-view mapping, kept free of the generated services so it stays unit
 * testable (the Power Apps data SDK cannot be imported under vitest).
 */

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

function toRoute(name: string | null): ReviewRoute | null {
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

export function toDetail(record: Al_outcomecases): CaseDetail {
  const extra = record as Al_outcomecases & {
    al_priority?: number;
    al_priorityname?: string;
    al_duedate?: string;
  };
  return {
    id: record.al_outcomecaseid,
    title: text(record.al_name) ?? record.al_casereference,
    caseReference: record.al_casereference,
    status: Al_outcomecasesal_casestatus[record.al_casestatus] as CaseStatus,
    statusValue: record.al_casestatus,
    route: toRoute(lookupLabel(record, 'al_reviewrouteid', record.al_reviewrouteidname)),
    owner: lookupLabel(record, 'ownerid', record.owneridname),
    ageInDays: ageInDays(record.createdon),
    priority: choice(Al_outcomecasesal_priority, extra.al_priority, extra.al_priorityname),
    dueDate: date(extra.al_duedate),
    rowVersion: record.versionnumber != null ? String(record.versionnumber) : null,
    previousCase: lookupLabel(record, 'al_previouscaseid', record.al_previouscaseidname),
    client: [
      { label: 'Client', value: text(record.al_clientname) },
      {
        label: 'Vulnerable client',
        value: choice(
          Al_outcomecasesal_vulnerableclient,
          record.al_vulnerableclient,
          record.al_vulnerableclientname,
        ),
      },
    ],
    adviser: [
      { label: 'Adviser', value: text(record.al_advisername) },
      { label: 'Adviser code', value: text(record.al_advisercode) },
      {
        label: 'Adviser status',
        value: choice(
          Al_outcomecasesal_adviserstatus,
          record.al_adviserstatus,
          record.al_adviserstatusname,
        ),
      },
      { label: 'Paraplanner', value: text(record.al_paraplanner) },
      { label: 'Paraplanner code', value: text(record.al_paraplannercode) },
    ],
    adviceAndProduct: [
      {
        label: 'Case type',
        value: choice(Al_outcomecasesal_casetype, record.al_casetype, record.al_casetypename),
      },
      {
        label: 'Product/solution type',
        value: choice(
          Al_outcomecasesal_productsolutiontype,
          record.al_productsolutiontype,
          record.al_productsolutiontypename,
        ),
      },
      { label: 'Products', value: text(record.al_products) },
      { label: 'Advice date', value: date(record.al_advicedate) },
      {
        label: 'Sample source',
        value: choice(
          Al_outcomecasesal_samplesource,
          record.al_samplesource,
          record.al_samplesourcename,
        ),
      },
      {
        label: 'Check point',
        value: choice(
          Al_outcomecasesal_preorpostcheck,
          record.al_preorpostcheck,
          record.al_preorpostcheckname,
        ),
      },
    ],
    checkAndTax: [
      { label: 'Checker', value: text(record.al_checkername) },
      { label: 'Check date', value: date(record.al_checkdate) },
      {
        label: 'Tax check required',
        value: choice(
          Al_outcomecasesal_taxcheckrequired,
          record.al_taxcheckrequired,
          record.al_taxcheckrequiredname,
        ),
      },
      {
        label: 'Tax team disposition',
        value: choice(
          Al_outcomecasesal_taxteamdisposition,
          record.al_taxteamdisposition,
          record.al_taxteamdispositionname,
        ),
      },
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
