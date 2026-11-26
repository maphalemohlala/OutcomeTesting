import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import type { Outcome } from '../../types/domain';
import { stageTone, type CaseStatus } from '../../types/domain';
import { Icon } from '../../components/icons/Icon';
import { REMEDIATION_THRESHOLD_WORKING_DAYS } from '../../lib/workingDays';
import { proportion } from './dashboardShares';
import { aggregate, useCaseDashboard, type DashboardData } from './useCaseDashboard';
import { toSummary } from '../cases/caseWorklistMapping';
import { casesInScope, narrowToScope, worklistLink, type ReportFilters } from './reportFilters';
import { DashboardFilters } from './DashboardFilters';
import { useReportFilters } from './useReportFilters';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import { contactEmailsOf } from '../people/peopleDirectory';
import './DashboardPage.css';
import { Skeleton } from '../../components/feedback/Skeleton';

/** Matches the OutcomeIndicator silhouettes so the bar reinforces the same grade. */
const OUTCOME_VARIANT: Record<Outcome, string> = {
  Pass: 'pass',
  'Pass with issues': 'issues',
  'Insufficient evidence': 'insufficient',
  'Potential harm': 'harm',
};

const STAGE_NOTES: Record<CaseStatus, string> = {
  Imported: 'Uploaded, not yet routed',
  'Validation Failed': 'Needs a correction',
  'Ready for Allocation': 'Ready to be queued',
  Queued: 'Waiting for a checker',
  Assigned: 'With a checker',
  'Review In Progress': 'Being checked',
  Submitted: 'Check submitted',
  'Awaiting Remediation': 'With the adviser',
  'Remediation In Progress': 'The adviser is working on it',
  'Awaiting Sign-off': 'With the T&C Manager',
  'Awaiting Recheck': 'Waiting for the final outcome',
  Closed: 'Finished',
  'No Check Required': 'Needed no check',
};

/** A whole percentage of the graded cases, for the outcome ledger's last column. */
function percentOf(count: number, total: number): number {
  return total > 0 ? Math.round((count / total) * 100) : 0;
}

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
  percent?: number;
  share?: number;
  tone?: string;
  to?: string;
}

/**
 * A ledger row: what it is, how much of the whole it is, how many. The bar is decorative
 * reinforcement of the count beside it, so it is hidden from assistive technology.
 */
function LedgerRow({ label, count, percent, share, tone, to }: LedgerRowProps) {
  const body = (
    <>
      <span className="dashboard__ledger-label">{label}</span>
      {share === undefined ? null : (
        // A unitless custom property, so a panel can draw the bar across or up from the same share.
        <span className="dashboard__track" aria-hidden="true" style={{ '--share': share } as React.CSSProperties}>
          <span className="dashboard__bar" data-tone={tone} />
        </span>
      )}
      <span className="dashboard__count">{count}</span>
      {percent === undefined ? null : <span className="dashboard__percent">{percent}%</span>}
    </>
  );

  const className =
    percent !== undefined
      ? 'dashboard__row dashboard__row--percent'
      : share === undefined
        ? 'dashboard__row dashboard__row--plain'
        : 'dashboard__row';

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
  const [filters, setFilter, clearFilters] = useReportFilters();
  const directory = useUserDirectory();

  const cases = state.status === 'ready' ? state.cases : NO_CASES;
  const summaries = useMemo(() => cases.map((record) => toSummary(record)), [cases]);
  const contactEmails = useMemo(
    () => contactEmailsOf(directory.status === 'ready' ? directory.users : []),
    [directory],
  );
  const scope = useMemo(
    () => casesInScope(summaries, filters, contactEmails),
    [summaries, filters, contactEmails],
  );

  return (
    <>
      <PageIntro
        title="Dashboard"
        purpose="What is waiting, what is ageing, and how completed checks were graded."
        actions={
          state.status === 'ready' ? (
            <DashboardFilters
              cases={summaries}
              inScope={scope ? scope.size : cases.length}
              filters={filters}
              onChange={setFilter}
              onClear={clearFilters}
              contactEmails={contactEmails}
            />
          ) : null
        }
      />

      {state.status === 'loading' ? <Skeleton label="Loading dashboard…" rows={6} /> : null}

      {state.status === 'unavailable' ? (
        <section className="dashboard__unavailable" aria-labelledby="dashboard-unavailable">
          <h2 id="dashboard-unavailable">The dashboard cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <FilteredDashboard
          cases={state.cases}
          outcomes={state.outcomes}
          actions={state.actions}
          scope={scope}
          filters={filters}
        />
      ) : null}
    </>
  );
}

type ReadyState = Extract<ReturnType<typeof useCaseDashboard>, { status: 'ready' }>;
const NO_CASES: ReadyState['cases'] = [];

function FilteredDashboard({
  cases,
  outcomes,
  actions,
  scope,
  filters,
}: Omit<ReadyState, 'status'> & { scope: ReturnType<typeof casesInScope>; filters: ReportFilters }) {
  const data = useMemo(
    () =>
      aggregate(
        narrowToScope(cases, scope, (record) => record.al_outcomecaseid),
        narrowToScope(outcomes, scope, (record) => record._al_outcomecaseid_value),
        narrowToScope(actions, scope, (record) => record._al_outcomecaseid_value),
      ),
    [cases, outcomes, actions, scope],
  );

  return <DashboardBody data={data} filters={filters} />;
}

function DashboardBody({ data, filters }: { data: DashboardData; filters: ReportFilters }) {
  // Every worklist and report link carries the filters, so a figure opens the list it
  // counted and the report opens on the same cases.
  const cases = (to: string) => worklistLink(to, filters);

  // Every case the caller can see, open or closed: the whole the outcome and stage bars
  // are drawn against, so a stage's bar and an outcome's bar are on the same scale.
  const totalCases = data.totalCases;
  const noCheck = data.byStatus.find((entry) => entry.status === 'No Check Required')?.count ?? 0;
  const pipeline = data.byStatus.filter((entry) => entry.status !== 'No Check Required');
  const overThirty = data.ageing[data.ageing.length - 1]?.count ?? 0;
  // The bands between the first and the last (8 to 30 working days); empty says the backlog is old, not growing.
  const middleEmpty = data.ageing.length > 2 && data.ageing.slice(1, -1).every((band) => band.count === 0);
  const graded = data.completedTotal;

  return (
    <>
      {overThirty > 0 || data.remediationOverdue > 0 ? (
        <section className="dashboard__attention" aria-label="Needs attention">
          {overThirty > 0 ? (
            <p className="dashboard__alert">
              <span className="numeral">{overThirty}</span>
              <span>
                <strong>open case{overThirty === 1 ? ' has' : 's have'} waited more than 30 working days</strong>
                The oldest was imported {data.oldestOpenDays} working days ago.
                {middleEmpty ? ' Nothing sits between 8 and 30 working days.' : null}
              </span>
            </p>
          ) : null}
          {data.remediationOverdue > 0 ? (
            <p className="dashboard__alert">
              <span className="numeral">{data.remediationOverdue}</span>
              <span>
                <strong>
                  {data.remediationOverdue === 1 ? 'remediation is past its' : 'remediations are past their'} due date
                </strong>
                {data.remediationBreached} open for more than {REMEDIATION_THRESHOLD_WORKING_DAYS} working days.
              </span>
            </p>
          ) : null}
          {data.oldestOpenCaseId ? (
            <Link
              className="dashboard__attention-action"
              to={`/cases/${encodeURIComponent(data.oldestOpenCaseId)}`}
            >
              Open the oldest case
            </Link>
          ) : null}
        </section>
      ) : null}

      <section className="dashboard__strip" aria-label="Work in hand">
        <Figure
          value={data.totalOpen}
          unit={`of ${totalCases}`}
          label="Open cases"
          to={cases('/cases')}
        />
        <Figure
          value={data.oldestOpenDays}
          unit="working days"
          label="Oldest open case"
          to={
            data.oldestOpenCaseId
              ? `/cases/${encodeURIComponent(data.oldestOpenCaseId)}`
              : undefined
          }
        />
        <Figure
          value={data.validationFailed}
          label="Failed validation"
          to={cases('/cases?status=Validation+Failed')}
        />
        <Figure value={data.unrouted} label="Awaiting a route" to={cases('/cases?route=none')} />
      </section>

      <section className="dashboard__panel dashboard__panel--wide" aria-labelledby="dashboard-status">
        <div className="dashboard__panel-heading">
          <h2 id="dashboard-status">Where cases sit</h2>
          <span className="dashboard__panel-meta">
            {totalCases - noCheck} cases.
            {noCheck > 0 ? ` ${noCheck} more needed no check.` : null}
          </span>
        </div>
        {pipeline.length === 0 ? (
          <p className="dashboard__empty">No cases are visible to you yet.</p>
        ) : (
          <ul className="dashboard__ledger dashboard__ledger--pipeline">
            {pipeline.map((entry) => (
              <LedgerRow
                key={entry.status}
                label={
                  <>
                    <span className="dashboard__stage" data-tone={stageTone(entry.status)}>
                      {entry.status}
                    </span>
                    <span className="dashboard__stage-note">{STAGE_NOTES[entry.status]}</span>
                    <Icon name="chevronRight" className="icon dashboard__chevron" />
                  </>
                }
                count={entry.count}
                share={proportion(entry.count, totalCases)}
                tone={stageTone(entry.status)}
                to={cases(`/cases?status=${encodeURIComponent(entry.status)}`)}
              />
            ))}
          </ul>
        )}
      </section>

      <div className="dashboard__panels dashboard__panels--three">
        <section className="dashboard__panel" aria-labelledby="dashboard-outcomes">
          <div className="dashboard__panel-heading">
            <h2 id="dashboard-outcomes">Completed outcomes</h2>
            <span className="dashboard__panel-meta">
              {graded} graded,{' '}
              <Link className="dashboard__meta-link" to={cases('/cases?outcome=none')}>
                {data.ungraded} not yet
              </Link>
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
                  style={{ flexBasis: `${proportion(entry.count, graded)}%`, flexGrow: 1 }}
                />
              ))}
            {graded === 0 ? <span className="dashboard__stack-segment" data-variant="none" style={{ flexGrow: 1 }} /> : null}
          </div>

          <ul className="dashboard__ledger">
            {data.completedOutcomes.map((entry) => (
              <LedgerRow
                key={entry.outcome}
                label={<OutcomeIndicator outcome={entry.outcome} />}
                count={entry.count}
                percent={percentOf(entry.count, graded)}
                to={cases(`/cases?outcome=${encodeURIComponent(entry.outcome)}`)}
              />
            ))}
          </ul>
        </section>

        <section className="dashboard__panel" aria-labelledby="dashboard-remediation">
          <div className="dashboard__panel-heading">
            <h2 id="dashboard-remediation">Remediation</h2>
            <Link className="dashboard__link" to={cases('/reports')}>
              Open report
            </Link>
          </div>

          <ul className="dashboard__ledger dashboard__ledger--tiles">
            <LedgerRow
              label={
                <span
                  className="dashboard__spine"
                  data-tone={data.remediationOpen > 0 ? 'active' : undefined}
                >
                  Open
                </span>
              }
              count={data.remediationOpen}
              to={cases('/cases?remediation=open')}
            />
            <LedgerRow
              label={
                <span className="dashboard__spine" data-tone="closed">
                  Completed
                </span>
              }
              count={data.remediationCompleted}
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
              to={cases('/reports')}
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
              to={cases('/reports')}
            />
          </ul>
          <p className="dashboard__footnote">
            Working days leave out weekends only, so a span over a bank holiday reads a day
            older than it is.
          </p>
        </section>

        <section className="dashboard__panel" aria-labelledby="dashboard-ageing">
          <div className="dashboard__panel-heading">
            <h2 id="dashboard-ageing">How long open cases have waited</h2>
            <span className="dashboard__panel-meta">{data.totalOpen} open</span>
          </div>
          <ul className="dashboard__ledger dashboard__ledger--columns">
            {data.ageing.map((band) => (
              <LedgerRow
                key={band.label}
                label={band.label}
                count={band.count}
                share={proportion(band.count, data.totalOpen)}
              />
            ))}
          </ul>
          <p className="dashboard__footnote">Working days since import. Weekends are left out; bank holidays are not.</p>
        </section>
      </div>
    </>
  );
}
