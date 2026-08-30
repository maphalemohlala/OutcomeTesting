import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { useReports } from './useReports';
import './ReportsPage.css';

export function ReportsPage() {
  const state = useReports();

  return (
    <>
      <PageIntro
        title="Management reporting"
        purpose="Outcome volumes, remediation ageing and sign-off accountability, read from live case data (BR-010)."
        actions={
          <Link className="reports__link" to="/exports">
            Go to Trail Light exports
          </Link>
        }
      />

      {state.status === 'loading' ? <p role="status">Loading management reporting…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="reports__unavailable" aria-labelledby="reports-unavailable">
          <h2 id="reports-unavailable">Management reporting cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <section className="reports__stats" aria-label="Reporting totals">
            <div className="reports__stat">
              <span className="reports__stat-value">{state.data.outcomeTotal}</span>
              <span className="reports__stat-label">Recorded outcomes</span>
            </div>
            <div className="reports__stat">
              <span className="reports__stat-value">{state.data.finalisedCount}</span>
              <span className="reports__stat-label">Finalised outcomes</span>
            </div>
            <div className="reports__stat" data-tone="waiting">
              <span className="reports__stat-value">{state.data.openRemediation}</span>
              <span className="reports__stat-label">Open remediation</span>
            </div>
            <div className="reports__stat" data-tone="blocked">
              <span className="reports__stat-value">{state.data.overdueRemediation}</span>
              <span className="reports__stat-label">Overdue remediation</span>
            </div>
          </section>

          <div className="reports__panels">
            <section className="reports__panel" aria-labelledby="reports-outcomes">
              <h2 id="reports-outcomes">Outcome volumes</h2>
              <p className="reports__note">
                Counted on the final outcome, or the initial outcome where none is set yet (BR-007).
              </p>
              {state.data.outcomeTotal === 0 ? (
                <p className="reports__empty">No outcomes are visible to you yet.</p>
              ) : (
                <ul className="reports__list">
                  {state.data.outcomeVolumes.map((entry) => (
                    <li key={entry.outcome}>
                      <span className="reports__list-label">{entry.outcome}</span>
                      <span className="reports__count">{entry.count}</span>
                    </li>
                  ))}
                  <li className="reports__list-summary">
                    <span className="reports__list-label">Regraded (BR-007)</span>
                    <span className="reports__count">{state.data.regradedCount}</span>
                  </li>
                </ul>
              )}
            </section>

            <section className="reports__panel" aria-labelledby="reports-ageing">
              <h2 id="reports-ageing">Remediation ageing</h2>
              <p className="reports__note">
                Actions not yet completed, by working days since they were raised (BR-010). Bank
                holidays are not excluded, so a span over one reads a day older than it is.
              </p>
              <ul className="reports__list">
                {state.data.remediationAgeing.map((band) => (
                  <li key={band.label}>
                    <span className="reports__list-label">{band.label}</span>
                    <span className="reports__count">{band.count}</span>
                  </li>
                ))}
              </ul>
            </section>

            <section className="reports__panel" aria-labelledby="reports-accountability">
              <h2 id="reports-accountability">Sign-off accountability</h2>
              <p className="reports__note">T&amp;C Manager validation decisions (BR-008, FR-023).</p>
              <ul className="reports__list">
                <li>
                  <span className="reports__list-label">Approved</span>
                  <span className="reports__count">{state.data.signoffApproved}</span>
                </li>
                <li>
                  <span className="reports__list-label">Rejected and returned</span>
                  <span className="reports__count">{state.data.signoffRejected}</span>
                </li>
              </ul>
            </section>
          </div>
        </>
      ) : null}
    </>
  );
}
