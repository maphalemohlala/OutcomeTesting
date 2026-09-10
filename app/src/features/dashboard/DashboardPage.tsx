import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import type { Outcome } from '../../types/domain';
import { stageTone } from '../../types/domain';
import { REMEDIATION_THRESHOLD_WORKING_DAYS } from '../../lib/workingDays';
import { proportion } from './dashboardShares';
import { useCaseDashboard, type DashboardData } from './useCaseDashboard';
import './DashboardPage.css';

/** Matches the OutcomeIndicator silhouettes so the bar reinforces the same grade. */
const OUTCOME_VARIANT: Record<Outcome, string> = {
  Pass: 'pass',
  'Pass with issues': 'issues',
  'Insufficient evidence': 'insufficient',
  'Potential harm': 'harm',
};

interface FigureProps {
  value: number;
  label: string;
  unit?: string;
  to?: string;
}

/**
 * One figure in the work-in-hand strip. A zero is still shown, because "nothing has
 * failed validation" is an answer, but it recedes so the figures that need a person
 * carry the page.
 */
function Figure({ value, label, unit, to }: FigureProps) {
  const body = (
    <>
      <span className="dashboard__figure-value" data-zero={value === 0 || undefined}>
        {value}
        {unit ? <span className="dashboard__figure-unit"> {unit}</span> : null}
      </span>
      <span className="dashboard__figure-label">{label}</span>
    </>
  );

  return to ? (
    <Link className="dashboard__figure" to={to}>
      {body}
    </Link>
  ) : (
    <div className="dashboard__figure">{body}</div>
  );
}

interface LedgerRowProps {
  label: React.ReactNode;
  count: number;
  share?: number;
  tone?: string;
  to?: string;
}

/**
 * A ledger row: what it is, how much of the whole it is, how many. The bar is decorative
 * reinforcement of the count beside it, so it is hidden from assistive technology.
 */
function LedgerRow({ label, count, share, tone, to }: LedgerRowProps) {
  const body = (
    <>
      <span className="dashboard__ledger-label">{label}</span>
      {share === undefined ? null : (
        <span className="dashboard__track" aria-hidden="true">
          <span className="dashboard__bar" data-tone={tone} style={{ width: `${share}%` }} />
        </span>
      )}
      <span className="dashboard__count">{count}</span>
    </>
  );

  const className = share === undefined ? 'dashboard__row dashboard__row--plain' : 'dashboard__row';

  return (
    <li>
      {to ? (
        <Link className={className} data-zero={count === 0 || undefined} to={to}>
          {body}
        </Link>
      ) : (
        <div className={className} data-zero={count === 0 || undefined}>
          {body}
        </div>
      )}
    </li>
  );
}

export function DashboardPage() {
  const state = useCaseDashboard();

  return (
    <>
      <PageIntro
        title="Dashboard"
        purpose="What is waiting, what is ageing, and how completed checks were graded."
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

      {state.status === 'ready' ? <DashboardBody data={state.data} /> : null}
    </>
  );
}

function DashboardBody({ data }: { data: DashboardData }) {
  // Every case the caller can see, open or closed: the whole the outcome and stage bars
  // are drawn against, so a stage's bar and an outcome's bar are on the same scale.
  const totalCases = data.completedTotal + data.ungraded;
  const closed = data.byStatus.find((entry) => entry.status === 'Closed')?.count ?? 0;

  return (
    <>
      <section className="dashboard__strip" aria-label="Work in hand">
        <Figure value={data.totalOpen} label="Open cases" to="/cases" />
        <Figure
          value={data.validationFailed}
          label="Failed validation"
          to="/cases?status=Validation+Failed"
        />
        <Figure value={data.unrouted} label="Awaiting a route" to="/cases?route=none" />
        <Figure
          value={data.oldestOpenDays}
          unit="days"
          label="Oldest open case"
          to={
            data.oldestOpenCaseId
              ? `/cases/${encodeURIComponent(data.oldestOpenCaseId)}`
              : undefined
          }
        />
      </section>

      <div className="dashboard__panels dashboard__panels--weighted">
        <section className="dashboard__panel" aria-labelledby="dashboard-outcomes">
          <div className="dashboard__panel-heading">
            <h2 id="dashboard-outcomes">Completed outcomes</h2>
            <span className="dashboard__panel-meta">
              {totalCases} cases, {data.completedTotal} graded
            </span>
          </div>

          <div className="dashboard__stack" aria-hidden="true">
            {data.completedOutcomes
              .filter((entry) => entry.count > 0)
              .map((entry) => (
                <span
                  key={entry.outcome}
                  className="dashboard__stack-segment"
                  data-variant={OUTCOME_VARIANT[entry.outcome]}
                  style={{ flexBasis: `${proportion(entry.count, totalCases)}%` }}
                />
              ))}
            {data.ungraded > 0 ? (
              <span
                className="dashboard__stack-segment"
                data-variant="none"
                style={{ flexBasis: `${proportion(data.ungraded, totalCases)}%` }}
              />
            ) : null}
          </div>

          <ul className="dashboard__ledger">
            {data.completedOutcomes.map((entry) => (
              <LedgerRow
                key={entry.outcome}
                label={<OutcomeIndicator outcome={entry.outcome} />}
                count={entry.count}
                share={proportion(entry.count, totalCases)}
                tone={OUTCOME_VARIANT[entry.outcome]}
                to={`/cases?outcome=${encodeURIComponent(entry.outcome)}`}
              />
            ))}
            <LedgerRow
              label={<span className="dashboard__muted">Not yet graded</span>}
              count={data.ungraded}
              share={proportion(data.ungraded, totalCases)}
              tone="none"
              to="/cases?outcome=none"
            />
          </ul>
        </section>

        <section className="dashboard__panel" aria-labelledby="dashboard-remediation">
          <div className="dashboard__panel-heading">
            <h2 id="dashboard-remediation">Remediation</h2>
            <Link className="dashboard__link" to="/reports">
              Report
            </Link>
          </div>

          <ul className="dashboard__ledger">
            <LedgerRow
              label={
                <span
                  className="dashboard__spine"
                  data-tone={data.remediationOpen > 0 ? 'active' : undefined}
                >
                  Open actions
                </span>
              }
              count={data.remediationOpen}
              to="/cases?status=Awaiting+Remediation"
            />
            <LedgerRow
              label={
                <span
                  className="dashboard__spine"
                  data-tone={data.remediationOverdue > 0 ? 'blocked' : undefined}
                >
                  Past their due date
                </span>
              }
              count={data.remediationOverdue}
              to="/reports"
            />
            <LedgerRow
              label={
                <span
                  className="dashboard__spine"
                  data-tone={data.remediationBreached > 0 ? 'blocked' : undefined}
                >
                  Over {REMEDIATION_THRESHOLD_WORKING_DAYS} working days
                </span>
              }
              count={data.remediationBreached}
              to="/reports"
            />
            <LedgerRow
              label={
                <span className="dashboard__spine" data-tone="closed">
                  Completed
                </span>
              }
              count={data.remediationCompleted}
            />
          </ul>
          <p className="dashboard__footnote">
            Working days exclude weekends only, so a span over a bank holiday reads a day
            older than it is.
          </p>
        </section>
      </div>

      <div className="dashboard__panels">
        <section className="dashboard__panel" aria-labelledby="dashboard-status">
          <div className="dashboard__panel-heading">
            <h2 id="dashboard-status">Where cases sit</h2>
            <span className="dashboard__panel-meta">
              {totalCases} cases, {closed} closed
            </span>
          </div>
          {data.byStatus.length === 0 ? (
            <p className="dashboard__empty">No cases are visible to you yet.</p>
          ) : (
            <>
              <ul className="dashboard__ledger">
                {data.byStatus.map((entry) => (
                  <LedgerRow
                    key={entry.status}
                    label={<StageLabel status={entry.status} />}
                    count={entry.count}
                    share={proportion(entry.count, totalCases)}
                    tone={stageTone(entry.status)}
                    to={`/cases?status=${encodeURIComponent(entry.status)}`}
                  />
                ))}
              </ul>
              <p className="dashboard__footnote">Stages holding no cases are not shown.</p>
            </>
          )}
        </section>

        <section className="dashboard__panel" aria-labelledby="dashboard-ageing">
          <div className="dashboard__panel-heading">
            <h2 id="dashboard-ageing">How long open cases have waited</h2>
            <span className="dashboard__panel-meta">{data.totalOpen} open</span>
          </div>
          <ul className="dashboard__ledger">
            {data.ageing.map((band) => (
              <LedgerRow
                key={band.label}
                label={band.label}
                count={band.count}
                share={proportion(band.count, data.totalOpen)}
              />
            ))}
          </ul>
          <p className="dashboard__footnote">Calendar days since import.</p>
        </section>
      </div>
    </>
  );
}
