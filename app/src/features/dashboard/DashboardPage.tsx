import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import type { Outcome } from '../../types/domain';
import { REMEDIATION_THRESHOLD_WORKING_DAYS } from '../../lib/workingDays';
import { useCaseDashboard } from './useCaseDashboard';
import './DashboardPage.css';

/** Matches the OutcomeIndicator silhouettes so the card edge reinforces the same grade. */
const OUTCOME_VARIANT: Record<Outcome, string> = {
  Pass: 'pass',
  'Pass with issues': 'issues',
  'Insufficient evidence': 'insufficient',
  'Potential harm': 'harm',
};

export function DashboardPage() {
  const state = useCaseDashboard();

  return (
    <>
      <PageIntro
        title="Dashboard"
        purpose="See the work that is waiting, what is ageing, what has failed validation and how completed checks were graded."
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
            <Link className="dashboard__stat" to="/cases">
              <span className="dashboard__stat-value">{state.data.totalOpen}</span>
              <span className="dashboard__stat-label">Open cases</span>
            </Link>
            <Link className="dashboard__stat" data-tone="blocked" to="/cases?status=Validation+Failed">
              <span className="dashboard__stat-value">{state.data.validationFailed}</span>
              <span className="dashboard__stat-label">Failed validation</span>
            </Link>
            {/*
              Both of these were plain divs. They carry the same dashboard__stat class as
              the two cards beside them, so they read as cards and invite a click that did
              nothing. Every stat card now opens the set it counted: "Awaiting a route"
              drills into the unrouted cases through the same route=none sentinel the
              outcome filter already uses for "Not yet graded", and the oldest-open age
              opens the one case it is measuring.
            */}
            <Link className="dashboard__stat" data-tone="waiting" to="/cases?route=none">
              <span className="dashboard__stat-value">{state.data.unrouted}</span>
              <span className="dashboard__stat-label">Awaiting a route</span>
            </Link>
            {state.data.oldestOpenCaseId ? (
              <Link
                className="dashboard__stat"
                to={`/cases/${encodeURIComponent(state.data.oldestOpenCaseId)}`}
              >
                <span className="dashboard__stat-value">{state.data.oldestOpenDays}</span>
                <span className="dashboard__stat-label">Oldest open (days)</span>
              </Link>
            ) : (
              <div className="dashboard__stat">
                <span className="dashboard__stat-value">{state.data.oldestOpenDays}</span>
                <span className="dashboard__stat-label">Oldest open (days)</span>
              </div>
            )}
          </section>

          <section aria-labelledby="dashboard-outcomes">
            <h2 id="dashboard-outcomes" className="dashboard__section-heading">
              Completed outcomes
            </h2>
            <p className="dashboard__section-note">
              The grade each checked case currently stands on — its final outcome, or the initial
              one where no final grade is set yet (BR-005, BR-007). Select a card to open the cases
              behind it.
            </p>
            <div className="dashboard__outcomes">
              {state.data.completedOutcomes.map((entry) => (
                <Link
                  key={entry.outcome}
                  className="dashboard__outcome"
                  data-variant={OUTCOME_VARIANT[entry.outcome]}
                  to={`/cases?outcome=${encodeURIComponent(entry.outcome)}`}
                >
                  <span className="dashboard__stat-value">{entry.count}</span>
                  <OutcomeIndicator outcome={entry.outcome} />
                </Link>
              ))}
              <Link className="dashboard__outcome" data-variant="none" to="/cases?outcome=none">
                <span className="dashboard__stat-value">{state.data.ungraded}</span>
                <span className="dashboard__outcome-label">Not yet graded</span>
              </Link>
            </div>
          </section>

          <section aria-labelledby="dashboard-remediation">
            <h2 id="dashboard-remediation" className="dashboard__section-heading">
              Remediation
            </h2>
            <p className="dashboard__section-note">
              Every non-pass outcome raises remediation (BR-006). Over the threshold counts actions
              past {REMEDIATION_THRESHOLD_WORKING_DAYS} working days; bank holidays are not
              excluded, so a span over one reads a day older than it is (OD-018).
            </p>
            <div className="dashboard__stats">
              <Link className="dashboard__stat" data-tone="waiting" to="/cases?status=Awaiting+Remediation">
                <span className="dashboard__stat-value">{state.data.remediationOpen}</span>
                <span className="dashboard__stat-label">Open actions</span>
              </Link>
              <Link className="dashboard__stat" data-tone="blocked" to="/reports">
                <span className="dashboard__stat-value">{state.data.remediationOverdue}</span>
                <span className="dashboard__stat-label">Past their due date</span>
              </Link>
              <Link className="dashboard__stat" data-tone="blocked" to="/reports">
                <span className="dashboard__stat-value">{state.data.remediationBreached}</span>
                <span className="dashboard__stat-label">Over the threshold</span>
              </Link>
              <div className="dashboard__stat">
                <span className="dashboard__stat-value">{state.data.remediationCompleted}</span>
                <span className="dashboard__stat-label">Completed actions</span>
              </div>
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
                      <Link to={`/cases?status=${encodeURIComponent(entry.status)}`}>
                        <StageLabel status={entry.status} />
                      </Link>
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
