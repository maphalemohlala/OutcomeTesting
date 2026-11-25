import type { ReactNode } from 'react';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { Icon } from '../../components/icons/Icon';
import type { Outcome } from '../../types/domain';
import type { CaseOutcomeState } from './useCaseOutcome';
import './CaseOutcomeSummary.css';
import { Skeleton } from '../../components/feedback/Skeleton';

const MARKED: readonly string[] = ['Pass', 'Pass with issues', 'Insufficient evidence', 'Potential harm'];

/** A grade on the advice scale gets its mark; anything else (a Tax grade) is shown as words. */
function Grade({ value }: { value: string }) {
  return MARKED.includes(value) ? <OutcomeIndicator outcome={value as Outcome} /> : <>{value}</>;
}

/**
 * A glance summary of the case's recorded grades (BR-007). The full remediation,
 * regrade and sign-off detail lives on the remediation screen; this only surfaces
 * the preserved initial and final outcomes so the case's standing is visible here.
 */
export function CaseOutcomeSummary({
  state,
  action,
}: {
  /** The page's read of the case's outcomes, shared with the accountability editor. */
  state: CaseOutcomeState;
  /** Drawn beside the newest grade: the amend button, which amends that one. */
  action?: ReactNode;
}) {
  const outcomes = state.status === 'ready' ? state.outcomes : [];
  // One check: its name sits in the card's header, as a label for the whole card.
  const single = outcomes.length === 1 ? outcomes[0] : null;

  return (
    <section className="case-outcome" aria-labelledby="panel-outcome">
      <div className="case-outcome__head">
        <h2 id="panel-outcome">Outcome</h2>
        {single ? (
          <span className="case-outcome__check">{single.reviewInstance ?? single.reference}</span>
        ) : null}
      </div>

      {state.status === 'loading' ? <Skeleton label="Loading outcome…" /> : null}

      {state.status === 'unavailable' ? (
        <p className="case-outcome__note">The outcome for this case could not be loaded.</p>
      ) : null}

      {state.status === 'ready' && outcomes.length === 0 ? (
        <>
          <p className="case-outcome__note">No outcome has been recorded for this case yet.</p>
          {action}
        </>
      ) : null}

      {outcomes.length > 0 ? (
        <ul className="case-outcome__list">
          {outcomes.map((outcome, index) => (
            <li key={outcome.id} className="case-outcome__item">
              {single ? null : (
                <span className="case-outcome__check">{outcome.reviewInstance ?? outcome.reference}</span>
              )}
              <div className="case-outcome__row">
                <div className="case-outcome__grades">
                  <div className="case-outcome__box">
                    <span className="case-outcome__label">Initial</span>
                    <span className="case-outcome__grade">
                      <Grade value={outcome.initialOutcome} />
                    </span>
                  </div>
                  <Icon name="arrowRight" className="icon case-outcome__arrow" />
                  <div className="case-outcome__box" data-empty={outcome.finalOutcome ? undefined : 'true'}>
                    <span className="case-outcome__label">Final</span>
                    <span className="case-outcome__grade">
                      {outcome.finalOutcome ? <Grade value={outcome.finalOutcome} /> : 'Not finalised'}
                    </span>
                  </div>
                </div>
                {index === outcomes.length - 1 && action ? (
                  <div className="case-outcome__action">{action}</div>
                ) : null}
              </div>
              {outcome.regraded || outcome.finalisedOn ? (
                <span className="case-outcome__meta">
                  {outcome.regraded ? <span className="case-outcome__flag">Regraded</span> : null}
                  {outcome.finalisedOn ? `Finalised ${outcome.finalisedOn}` : null}
                </span>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
