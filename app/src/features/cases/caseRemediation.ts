import { Al_remediationactionsal_actionstatus } from '../../generated/models/Al_remediationactionsModel';

/**
 * Where a case's remediation stands, for the worklist and the reports that drill into it.
 *
 * Per CASE, not per action and not per review. The worklist lists cases, so a filter that
 * counts anything else lists a different number from the tile that links to it: TEST on
 * 2026-09-28 held 84 open actions on 11 cases, and the 84 was read as cases.
 *
 * A case is complete only when every action on it is Completed - "approved as a whole", the
 * rule SignoffProgressPlugin applies before a case may leave Awaiting Sign-off.
 */

/** Only what the rule reads, so a test needs no generated model. */
export interface CaseRemediationSource {
  al_remediationactionid: string;
  al_actionstatus: number;
  al_actionstatusname?: string;
  _al_outcomecaseid_value?: string;
}

export interface CaseRemediation {
  state: 'open' | 'complete';
  open: number;
  total: number;
}

export function isActionCompleted(action: CaseRemediationSource): boolean {
  // The formatted name when Dataverse returned one, else the option-set map. The name alone
  // would read every completed action as open whenever the formatted value is absent.
  const status =
    action.al_actionstatusname ??
    Al_remediationactionsal_actionstatus[
      action.al_actionstatus as keyof typeof Al_remediationactionsal_actionstatus
    ];
  return status === 'Completed';
}

export function remediationByCase(actions: CaseRemediationSource[]): Map<string, CaseRemediation> {
  const byCase = new Map<string, CaseRemediation>();

  for (const action of actions) {
    const caseId = action._al_outcomecaseid_value;
    if (!caseId) continue;
    const entry = byCase.get(caseId) ?? { state: 'complete', open: 0, total: 0 };
    entry.total += 1;
    if (!isActionCompleted(action)) {
      entry.open += 1;
      entry.state = 'open';
    }
    byCase.set(caseId, entry);
  }

  return byCase;
}
