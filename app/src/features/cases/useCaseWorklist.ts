import { useEffect, useState } from 'react';
import {
  Al_listoption_al_outcomecase_productssetService,
  Al_listoptionsService,
  Al_outcomecasesService,
  Al_outcomesService,
  Al_remediationactionsService,
  Al_reviewinstancesService,
  ContactsService,
} from '../../generated';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';
import type { Al_remediationactions } from '../../generated/models/Al_remediationactionsModel';
import { logTechnical } from '../../services/errors';
import { commandsSent } from '../../services/commands/commandClient';
import {
  CASE_SUMMARY_COLUMNS,
  OUTCOME_GRADE_COLUMNS,
  REVIEW_SUBMISSION_COLUMNS,
  gradesByCase,
  productNamesByCase,
  staffCodesByEmail,
  submissionsByCase,
  toSummary,
} from './caseWorklistMapping';
import { ACTION_COUNT_COLUMNS, remediationByCase, type CaseRemediation } from './caseRemediation';
import type { CaseOutcomeGrades, CaseSummary } from './caseWorklistMapping';
import { readAll } from '../../services/readAll';
import { LIST_OPTION_COLUMNS } from '../admin/listOptions';

/**
 * The outcome and remediation rows this read fetched, for the reports page, which counts the
 * same rows and should not read them a second time. Null where that read failed.
 */
interface ReportRows {
  outcomes: Al_outcomes[] | null;
  actions: Al_remediationactions[] | null;
}

export type WorklistState =
  | ({ status: 'unavailable'; reason: string } & ReportRows)
  | { status: 'loading' }
  | ({
      status: 'ready';
      cases: CaseSummary[];
      /**
       * False when remediation actions could not be read. The remediation filter and column
       * are then hidden rather than reading every case as having raised none.
       */
      remediationKnown: boolean;
    } & ReportRows);

const UNAVAILABLE = 'The case list could not be loaded right now. Please try again later.';

/**
 * A read that threw, as the read that answered no. The three reads below are each judged on
 * their own, so one of them throwing must not take the other two's rows with it - the
 * reports page shows its figures from outcomes and actions whether or not the cases came.
 */
const refused = (error: unknown) => ({ success: false as const, data: [] as never[], error });

/** One read of the list and everything joined to it. Never rejects. */
function readWorklist(): Promise<WorklistState> {
  return Promise.all([
    readAll((o) => Al_outcomecasesService.getAll(o), { orderBy: ['createdon asc', 'al_outcomecaseid asc'], select: CASE_SUMMARY_COLUMNS }).catch(refused),
    readAll((o) => Al_outcomesService.getAll(o), { orderBy: ['al_outcomeid asc'], select: OUTCOME_GRADE_COLUMNS }).catch(refused),
    readAll((o) => Al_remediationactionsService.getAll(o), { orderBy: ['al_remediationactionid asc'], select: ACTION_COUNT_COLUMNS }).catch(refused),
    // Products degrade to the old column: a failed read costs a report column, not the list.
    readAll((o) => Al_listoption_al_outcomecase_productssetService.getAll(o), {
      orderBy: ['al_listoption_al_outcomecase_productsid asc'],
    }).catch(() => null),
    readAll((o) => Al_listoptionsService.getAll(o), { orderBy: ['al_listoptionid asc'], select: LIST_OPTION_COLUMNS }).catch(() => null),
    // Both degrade the same way: blank codes and timings, never a lost list.
    readAll((o) => ContactsService.getAll(o), {
      orderBy: ['contactid asc'],
      select: ['contactid', 'emailaddress1', 'al_staffcode', 'statecode'],
    }).catch(() => null),
    readAll((o) => Al_reviewinstancesService.getAll(o), {
      orderBy: ['al_reviewinstanceid asc'],
      select: REVIEW_SUBMISSION_COLUMNS,
    }).catch(() => null),
  ])
    .then(([cases, outcomes, actions, productLinks, catalogue, contacts, reviews]): WorklistState => {
      const reportRows: ReportRows = {
        outcomes: outcomes.success ? outcomes.data : null,
        actions: actions.success ? actions.data : null,
      };
      if (!cases.success) {
        logTechnical('worklist load', cases.error);
        return { status: 'unavailable', reason: UNAVAILABLE, ...reportRows };
      }
      // A caller without read on al_outcome still gets their cases; the grade column
      // then reads as not yet graded rather than the whole worklist going unavailable.
      if (!outcomes.success) logTechnical('worklist outcome join', outcomes.error);
      const grades: Map<string, CaseOutcomeGrades> = outcomes.success
        ? gradesByCase(outcomes.data)
        : new Map();
      // The same degradation for remediation: the cases still list, without the column.
      if (!actions.success) logTechnical('worklist remediation join', actions.error);
      const remediation: Map<string, CaseRemediation> = actions.success
        ? remediationByCase(actions.data)
        : new Map();

      const products =
        productLinks?.success && catalogue?.success
          ? productNamesByCase(productLinks.data, catalogue.data, new Date())
          : new Map<string, string>();

      const codes = contacts?.success
        ? staffCodesByEmail(contacts.data as Parameters<typeof staffCodesByEmail>[0])
        : new Map<string, string>();
      const codeOf = (email: string | undefined) => codes.get(email?.trim().toLowerCase() ?? '') ?? null;
      const submissions = reviews?.success ? submissionsByCase(reviews.data) : null;

      return {
        status: 'ready',
        cases: cases.data.map((record) => ({
          ...toSummary(record, grades.get(record.al_outcomecaseid)),
          products: products.get(record.al_outcomecaseid) || (record.al_products ?? null),
          adviserCode: codeOf(record.al_adviseremail),
          paraplannerCode: codeOf(record.al_paraplanneremail),
          taxSubmittedOn: submissions ? (submissions.get(record.al_outcomecaseid)?.tax ?? null) : undefined,
          aqsSubmittedOn: submissions ? (submissions.get(record.al_outcomecaseid)?.aqs ?? null) : undefined,
          remediation: actions.success ? (remediation.get(record.al_outcomecaseid) ?? null) : undefined,
        })),
        remediationKnown: actions.success,
        ...reportRows,
      };
    })
    .catch((error): WorklistState => {
      logTechnical('worklist load', error);
      return { status: 'unavailable', reason: UNAVAILABLE, outcomes: null, actions: null };
    });
}

/** The last list read, and how many commands this app had sent when that read began. */
let cached: { state: WorklistState; commands: number } | null = null;

/**
 * What a screen shows the moment it opens: the last list read, or loading.
 *
 * The last list is shown only while no command has been sent since it was read. A save, an
 * allocation or an import may have changed a case, and a list from before it would show the
 * change as not having happened - so after any command the screen loads as it always did.
 */
export function cachedCaseWorklist(): WorklistState {
  return cached && cached.commands === commandsSent() ? cached.state : { status: 'loading' };
}

/**
 * Reads the list afresh, always, and keeps a successful answer for the next screen. A failed
 * read is what the screen then shows, and the last list is forgotten: rows nobody could
 * confirm would otherwise go on being counted and exported with nothing to say how old they were.
 */
export async function loadCaseWorklist(): Promise<WorklistState> {
  const commands = commandsSent();
  const state = await readWorklist();
  cached = state.status === 'ready' ? { state, commands } : null;
  return state;
}

/** Test seam, and what a change of signed-in user would call: forget the last list. */
export function resetCaseWorklistForTests(): void {
  cached = null;
}

/**
 * Reads the Outcome Case worklist from Dataverse, joined to the BR-007 outcome so a row
 * can show and be filtered by the grade it currently stands on. Row-level visibility is
 * enforced by Dataverse security (BR-012), so this returns only the cases the signed-in
 * user may see. Never returns sample rows.
 *
 * Four screens read this list. Each opens on the last one read (cachedCaseWorklist) and
 * reads again behind it, so moving between them does not wait on seven whole-table reads.
 * The last list shows only until that read lands or fails.
 */
export function useCaseWorklist(): WorklistState {
  const [state, setState] = useState<WorklistState>(cachedCaseWorklist);

  useEffect(() => {
    let cancelled = false;

    void loadCaseWorklist().then((read) => {
      if (!cancelled) setState(read);
    });

    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
