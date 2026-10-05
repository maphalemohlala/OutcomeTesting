import { useRemediation } from '../remediation/useRemediation';
import { groupIssues } from '../remediation/remediationIssues';
import type { RemediationActionRow } from '../remediation/remediationMapping';

/**
 * The case's remedial actions on its details page (project owner, 2026-10-05: "Include the
 * remediation actions on the case details if there are any"). Read-only: answering and
 * signing off stay on the remediation page. Every live action is listed, settled ones
 * included - this is the case's record, not a worklist - and a deactivated one is not.
 *
 * The owner is the adviser the action is assigned to, never `ownerid`, which is the record's
 * system owner.
 */
export function CaseRemedialActions({ actions }: { actions: RemediationActionRow[] }) {
  // A deactivated action is no longer owed, and the emailed document leaves it out too.
  const live = actions.filter((action) => action.active);
  if (live.length === 0) {
    return null;
  }

  return (
    <section className="case-detail__remedial" aria-labelledby="panel-remedial">
      <h2 id="panel-remedial">Remedial actions</h2>
      <table className="case-detail__remedial-table">
        <thead>
          <tr>
            <th scope="col">No.</th>
            <th scope="col">Check</th>
            <th scope="col">Fail point</th>
            <th scope="col">Remedial action</th>
            <th scope="col">Owner</th>
            <th scope="col">Target date</th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {groupIssues(live).flatMap(({ action, lines }) =>
            lines.map((line, index) => (
              <tr key={`${action.id}-${line.number}`}>
                <td>{line.number}</td>
                {index === 0 ? (
                  <>
                    <td rowSpan={lines.length}>{action.triggeredBy ?? '—'}</td>
                    <td>{line.issue}</td>
                    <td rowSpan={lines.length}>{action.remedialAction ?? '—'}</td>
                    <td rowSpan={lines.length}>{action.assignedTo ?? 'Nobody assigned'}</td>
                    <td rowSpan={lines.length}>{action.dueOn ?? '—'}</td>
                    <td rowSpan={lines.length}>{action.status}</td>
                  </>
                ) : (
                  <td>{line.issue}</td>
                )}
              </tr>
            )),
          )}
        </tbody>
      </table>
    </section>
  );
}

/** Loads the case's actions and draws them; draws nothing until they are read. */
export function CaseRemedialActionsPanel({ caseId, reloadKey }: { caseId: string; reloadKey: number }) {
  const state = useRemediation(caseId, reloadKey);
  return state.status === 'ready' ? <CaseRemedialActions actions={state.actions} /> : null;
}
