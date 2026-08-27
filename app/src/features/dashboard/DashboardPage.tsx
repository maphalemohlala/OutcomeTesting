import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { StageLabel } from '../../components/status/StageLabel';
import { useCaseDashboard } from './useCaseDashboard';
import './DashboardPage.css';

export function DashboardPage() {
  const state = useCaseDashboard();

  return (
    <>
      <PageIntro
        title="Dashboard"
        purpose="See the work that is waiting, what is ageing and what has failed validation."
        actions={
          <Link className="dashboard__link" to="/cases">
            Open case worklist
          </Link>
        }
      />

      {state.status === 'loading' ? <p role="status">Loading dashboard…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="dashboard__unavailable" aria-labelledby="dashboard-unavailable">
          <h2 id="dashboard-unavailable">The dashboard cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <section className="dashboard__stats" aria-label="Case totals">
            <div className="dashboard__stat">
              <span className="dashboard__stat-value">{state.data.totalOpen}</span>
              <span className="dashboard__stat-label">Open cases</span>
            </div>
            <div className="dashboard__stat" data-tone="blocked">
              <span className="dashboard__stat-value">{state.data.validationFailed}</span>
              <span className="dashboard__stat-label">Failed validation</span>
            </div>
            <div className="dashboard__stat" data-tone="waiting">
              <span className="dashboard__stat-value">{state.data.unrouted}</span>
              <span className="dashboard__stat-label">Awaiting a route</span>
            </div>
            <div className="dashboard__stat">
              <span className="dashboard__stat-value">{state.data.oldestOpenDays}</span>
              <span className="dashboard__stat-label">Oldest open (days)</span>
            </div>
          </section>

          <div className="dashboard__panels">
            <section className="dashboard__panel" aria-labelledby="dashboard-status">
              <h2 id="dashboard-status">Where open and closed cases sit</h2>
              {state.data.byStatus.length === 0 ? (
                <p className="dashboard__empty">No cases are visible to you yet.</p>
              ) : (
                <ul className="dashboard__status-list">
                  {state.data.byStatus.map((entry) => (
                    <li key={entry.status}>
                      <StageLabel status={entry.status} />
                      <span className="dashboard__count">{entry.count}</span>
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section className="dashboard__panel" aria-labelledby="dashboard-ageing">
              <h2 id="dashboard-ageing">How long open cases have been waiting</h2>
              <ul className="dashboard__ageing">
                {state.data.ageing.map((band) => (
                  <li key={band.label}>
                    <span className="dashboard__ageing-label">{band.label}</span>
                    <span className="dashboard__count">{band.count}</span>
                  </li>
                ))}
              </ul>
            </section>
          </div>
        </>
      ) : null}
    </>
  );
}
