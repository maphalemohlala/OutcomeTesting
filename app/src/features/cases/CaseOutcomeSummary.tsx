import { useCaseOutcome } from './useCaseOutcome';
import './CaseOutcomeSummary.css';

/**
 * A glance summary of the case's recorded grades (BR-007). The full remediation,
 * regrade and sign-off detail lives on the remediation screen; this only surfaces
 * the preserved initial and final outcomes so the case's standing is visible here.
 */
export function CaseOutcomeSummary({ caseId }: { caseId: string }) {
  const state = useCaseOutcome(caseId);

  return (
    <section className="case-outcome" aria-labelledby="panel-outcome">
      <h2 id="panel-outcome">Outcome</h2>

      {state.status === 'loading' ? <p role="status">Loading outcome…</p> : null}

      {state.status === 'unavailable' ? (
        <p className="case-outcome__note">The outcome for this case could not be loaded.</p>
      ) : null}

      {state.status === 'ready' ? (
        state.outcomes.length === 0 ? (
          <p className="case-outcome__note">No outcome has been recorded for this case yet.</p>
        ) : (
          <ul className="case-outcome__list">
            {state.outcomes.map((outcome) => (
              <li key={outcome.id} className="case-outcome__item">
                <div className="case-outcome__review">
                  <span className="case-outcome__label">Check</span>
                  <span>{outcome.reviewInstance ?? outcome.reference}</span>
                </div>
                <div className="case-outcome__grades">
                  <div>
                    <span className="case-outcome__label">Initial</span>
                    <span className="case-outcome__grade">{outcome.initialOutcome}</span>
                  </div>
                  <span className="case-outcome__arrow" aria-hidden="true">
                    →
                  </span>
                  <div>
                    <span className="case-outcome__label">Final</span>
                    <span className="case-outcome__grade" data-empty={outcome.finalOutcome ? undefined : 'true'}>
                      {outcome.finalOutcome ?? 'Not finalised'}
                    </span>
                  </div>
                  {outcome.regraded ? <span className="case-outcome__flag">Regraded</span> : null}
                </div>
                {outcome.finalisedOn ? (
                  <span className="case-outcome__meta">Finalised {outcome.finalisedOn}</span>
                ) : null}
              </li>
            ))}
          </ul>
        )
      ) : null}
    </section>
  );
}
