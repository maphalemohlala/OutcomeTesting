import { useRemediation } from '../remediation/useRemediation';
import { RemedialText } from '../remediation/RemedialText';
import { groupIssues } from '../remediation/remediationIssues';
import { signoffCell, signoffNotes } from '../remediation/remediationForm';
import type { RemediationActionRow, SignoffRow } from '../remediation/remediationMapping';
import { Icon } from '../../components/icons/Icon';

/** "3 of 3 completed, 1 not performed": the card's one-line standing. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export function remedialSummary(actions: readonly RemediationActionRow[]): string {
  const done = actions.filter((action) => action.completedOn).length;
  const notPerformed = actions.filter((action) => action.actionPerformed === 'No').length;
  const base = `${done} of ${actions.length} completed`;
  return notPerformed > 0 ? `${base}, ${notPerformed} not performed` : base;
}

/** Each distinct value once, in the order met: one check or owner reads as itself, two as both. */
function distinct(values: (string | null)[]): string {
  const seen = values.filter((value, index): value is string => !!value && values.indexOf(value) === index);
  return seen.length > 0 ? seen.join(', ') : '—';
}

/**
 * The case's remedial actions on its details page (project owner, 2026-10-05: "Include the
 * remediation actions on the case details if there are any"). Read-only: answering and
 * signing off stay on the remediation page. Every live action is listed, settled ones
 * included - this is the case's record, not a worklist - and a deactivated one is not.
 *
 * The owner is the adviser the action is assigned to, never `ownerid`, which is the record's
 * system owner.
 *
 * What the adviser answered and noted, and what the supervisor decided and noted, are drawn
 * too (reported 2026-10-07: "the notes need to be shown on the remedial details and case
 * details as well") - the same cells the remediation page draws, from the same helpers.
 */
export function CaseRemedialActions({
  actions,
  signoffs = [],
  caseStatus = null,
  adviserName = null,
}: {
  actions: RemediationActionRow[];
  signoffs?: SignoffRow[];
  /** The case's status label, which decides whether a completed action awaits a supervisor. */
  caseStatus?: string | null;
  /** The case's adviser, who still answers for an action nobody has been given. */
  adviserName?: string | null;
}) {
  // A deactivated action is no longer owed, and the emailed document leaves it out too.
  const live = actions.filter((action) => action.active);
  if (live.length === 0) {
    return null;
  }

  return (
    <section className="case-card case-detail__remedial" aria-labelledby="panel-remedial">
      <div className="case-card__head">
        <h2 id="panel-remedial">Remedial actions</h2>
        <span className="case-card__aside">{remedialSummary(live)}</span>
      </div>
      <p className="remedial__meta">
        <span>
          Check <strong>{distinct(live.map((action) => action.triggeredBy))}</strong>
        </span>
        <span>
          Owner{' '}
          <strong>{distinct(live.map((action) => action.assignedTo ?? adviserName ?? 'Nobody assigned'))}</strong>
        </span>
        <span>
          Target date <strong>{distinct(live.map((action) => action.dueOn))}</strong>
        </span>
      </p>
      <ol className="remedial__list">
        {groupIssues(live).map(({ action, lines }) => {
          const notes = signoffNotes(action, signoffs);
          const decision = signoffCell(action, signoffs, caseStatus);
          return (
            <li key={action.id} className="remedial__row">
              <div className="remedial__points">
                {lines.map((line) => (
                  <div key={line.number} className="remedial__point">
                    <span className="remedial__number">{line.number}</span>
                    <span className="remedial__point-text">
                      <span className="remedial__label">Fail point</span>
                      <strong>{line.issue}</strong>
                    </span>
                  </div>
                ))}
                <p className="remedial__line">
                  <span className="remedial__label">Remedial action:</span>{' '}
                  <RemedialText text={action.remedialAction} />
                </p>
                {notes ? (
                  <p className="remedial__line">
                    <span className="remedial__label">Sign-off note:</span> {notes}
                  </p>
                ) : null}
              </div>
              <div className="remedial__response" data-performed={action.actionPerformed ?? 'none'}>
                <span className="remedial__label">Adviser response</span>
                {action.actionPerformed === 'Yes' ? (
                  <span className="remedial__answer">
                    <Icon name="check" />
                    Action performed
                  </span>
                ) : action.actionPerformed === 'No' ? (
                  <span className="remedial__answer">
                    <Icon name="warning" />
                    Not performed
                  </span>
                ) : (
                  <span className="remedial__answer remedial__answer--none">No response yet</span>
                )}
                {action.adviserNote ? <em className="remedial__note">{action.adviserNote}</em> : null}
              </div>
              <div className="remedial__state">
                <span className="remedial__status" data-status={action.status}>
                  {action.status}
                </span>
                {decision !== '—' ? <span className="remedial__decision">{decision}</span> : null}
              </div>
            </li>
          );
        })}
      </ol>
    </section>
  );
}

/** Loads the case's actions and draws them; draws nothing until they are read. */
export function CaseRemedialActionsPanel({ caseId, reloadKey }: { caseId: string; reloadKey: number }) {
  const state = useRemediation(caseId, reloadKey);
  return state.status === 'ready' ? (
    <CaseRemedialActions
      actions={state.actions}
      signoffs={state.signoffs}
      caseStatus={state.outcomeCase?.status ?? null}
      adviserName={state.outcomeCase?.adviserName ?? null}
    />
  ) : null;
}
