import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { useCaseWorklist } from '../cases/useCaseWorklist';
import { casesInScope, narrowToScope, worklistLink } from '../dashboard/reportFilters';
import { ReportFilterBar } from '../dashboard/ReportFilterBar';
import { useReportFilters } from '../dashboard/useReportFilters';
import { CompletedCaseReport } from './CompletedCaseReport';
import { aggregate } from './reportAggregate';
import { useReports } from './useReports';
import './ReportsPage.css';

export function ReportsPage() {
  const state = useReports();
  const worklist = useCaseWorklist();
  const [filters, setFilter, clearFilters] = useReportFilters();

  const cases = useMemo(() => (worklist.status === 'ready' ? worklist.cases : []), [worklist]);
  // Unscoped until the cases are in: filtering against an empty case list would zero every
  // figure, which reads as "nothing happened" rather than "not loaded yet".
  const scope = useMemo(
    () => (worklist.status === 'ready' ? casesInScope(cases, filters) : null),
    [worklist.status, cases, filters],
  );

  const data = useMemo(
    () =>
      state.status === 'ready'
        ? aggregate(
            narrowToScope(state.outcomes, scope, (row) => row._al_outcomecaseid_value),
            narrowToScope(state.actions, scope, (row) => row._al_outcomecaseid_value),
            narrowToScope(state.signoffs, scope, (row) => row._al_outcomecaseid_value),
          )
        : null,
    [state, scope],
  );

  return (
    <>
      <PageIntro
        title="Management reporting"
        purpose="Outcome volumes, remediation ageing and sign-off accountability, read from live case data."
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

      {state.status === 'ready' && data ? (
        <>
          {worklist.status === 'ready' ? (
            <ReportFilterBar
              idPrefix="reports"
              cases={cases}
              inScope={scope ? scope.size : cases.length}
              filters={filters}
              onChange={setFilter}
              onClear={clearFilters}
            />
          ) : null}

          <section className="reports__stats" aria-label="Reporting totals">
            <div className="reports__stat">
              <span className="reports__stat-value">{data.outcomeTotal}</span>
              <span className="reports__stat-label">Recorded outcomes</span>
            </div>
            <div className="reports__stat">
              <span className="reports__stat-value">{data.finalisedCount}</span>
              <span className="reports__stat-label">Finalised outcomes</span>
            </div>
            {/*
              Cases first, actions second. This tile read "84 open remediation" on TEST and was
              taken as 84 cases; it was 84 actions - one per test point marked down - on 11
              cases (2026-09-28). The case count is what the worklist filter it opens lists.
            */}
            <Link
              className="reports__stat reports__stat--link"
              data-tone="waiting"
              to={worklistLink('/cases?remediation=open', filters)}
            >
              <span className="reports__stat-value">{data.openRemediationCases}</span>
              <span className="reports__stat-label">Cases in open remediation</span>
              <span className="reports__stat-detail">
                {data.openRemediation} open action{data.openRemediation === 1 ? '' : 's'}
              </span>
            </Link>
            <Link
              className="reports__stat reports__stat--link"
              data-tone="closed"
              to={worklistLink('/cases?remediation=complete', filters)}
            >
              <span className="reports__stat-value">{data.completedRemediationCases}</span>
              <span className="reports__stat-label">Cases with remediation complete</span>
            </Link>
            <div className="reports__stat" data-tone="blocked">
              <span className="reports__stat-value">{data.overdueRemediation}</span>
              <span className="reports__stat-label">Overdue remediation actions</span>
            </div>
          </section>

          <div className="reports__panels">
            <section className="reports__panel" aria-labelledby="reports-outcomes">
              <h2 id="reports-outcomes">Outcome volumes</h2>
              <p className="reports__note">
                Counted on the final outcome, or the initial outcome where none is set yet.
              </p>
              {data.outcomeTotal === 0 ? (
                <p className="reports__empty">No outcomes are visible to you yet.</p>
              ) : (
                <ul className="reports__list">
                  {data.outcomeVolumes.map((entry) => (
                    <li key={entry.outcome}>
                      <span className="reports__list-label">{entry.outcome}</span>
                      <span className="reports__count">{entry.count}</span>
                    </li>
                  ))}
                  <li className="reports__list-summary">
                    <span className="reports__list-label">Regraded</span>
                    <span className="reports__count">{data.regradedCount}</span>
                  </li>
                </ul>
              )}
            </section>

            <section className="reports__panel" aria-labelledby="reports-ageing">
              <h2 id="reports-ageing">Remediation ageing</h2>
              <p className="reports__note">
                Actions not yet completed, by working days since they were raised. Bank
                holidays are not excluded, so a span over one reads a day older than it is.
              </p>
              <ul className="reports__list">
                {data.remediationAgeing.map((band) => (
                  <li key={band.label}>
                    <span className="reports__list-label">{band.label}</span>
                    <span className="reports__count">{band.count}</span>
                  </li>
                ))}
              </ul>
            </section>

            <section className="reports__panel" aria-labelledby="reports-accountability">
              <h2 id="reports-accountability">Sign-off accountability</h2>
              <p className="reports__note">T&amp;C Manager validation decisions.</p>
              <ul className="reports__list">
                <li>
                  <span className="reports__list-label">Approved</span>
                  <span className="reports__count">{data.signoffApproved}</span>
                </li>
                <li>
                  <span className="reports__list-label">Rejected and returned</span>
                  <span className="reports__count">{data.signoffRejected}</span>
                </li>
              </ul>
            </section>
          </div>

          <CompletedCaseReport cases={cases} scope={scope} />
        </>
      ) : null}
    </>
  );
}
