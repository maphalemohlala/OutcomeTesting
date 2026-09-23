import { useState } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import { allocatableDisciplines } from '../cases/allocationScope';
import type { Discipline } from '../cases/caseCheckers';
import { summarise } from './workload';
import { useWorkload } from './useWorkload';
import '../dashboard/DashboardPage.css';
import '../cases/CaseWorklistPage.css';
import './WorkloadPage.css';

/**
 * Team workload (AR-01: "monitor individual and overall Tax Specialist workloads"). A team
 * manager sees their own discipline; Outcome Testing Manager and Administrators, who
 * allocate both, switch between them. Every figure opens the case list it counts.
 */
export function WorkloadPage() {
  const { roles } = usePermissions();
  const disciplines = allocatableDisciplines(roles);
  const [chosen, setChosen] = useState<Discipline | null>(null);
  const discipline = chosen ?? disciplines[0] ?? null;

  return (
    <>
      <PageIntro
        title="Team workload"
        purpose="Who holds what in your team, and how old the oldest open check is."
      />

      {disciplines.length > 1 ? (
        <nav className="workload__switch" aria-label="Team">
          {disciplines.map((option) => (
            <button
              key={option}
              type="button"
              className="dashboard__link"
              aria-pressed={option === discipline}
              onClick={() => setChosen(option)}
            >
              {option} team
            </button>
          ))}
        </nav>
      ) : null}

      {discipline ? (
        <WorkloadBody discipline={discipline} />
      ) : (
        <p role="status">Your role does not manage a review team, so there is no workload to show.</p>
      )}
    </>
  );
}

function WorkloadBody({ discipline }: { discipline: Discipline }) {
  const state = useWorkload(discipline);

  if (state.status === 'loading') return <p role="status">Loading workload…</p>;
  if (state.status === 'unavailable') {
    return (
      <section className="dashboard__unavailable" aria-labelledby="workload-unavailable">
        <h2 id="workload-unavailable">The workload cannot be shown</h2>
        <p>The checks could not be read right now. Try again shortly.</p>
      </section>
    );
  }

  const load = summarise(state.reviews, state.queued, new Date());

  return (
    <>
      <section className="dashboard__strip" aria-label={`${discipline} team totals`}>
        <Link className="dashboard__figure" to="/cases?status=Queued">
          <span className="dashboard__figure-value">{load.totals.awaitingAllocation}</span>
          <span className="dashboard__figure-label">Queued cases your team can see</span>
        </Link>
        <div className="dashboard__figure">
          <span className="dashboard__figure-value">{load.totals.allocated}</span>
          <span className="dashboard__figure-label">Allocated, not started</span>
        </div>
        <div className="dashboard__figure">
          <span className="dashboard__figure-value">{load.totals.inProgress}</span>
          <span className="dashboard__figure-label">In progress</span>
        </div>
        <div className="dashboard__figure">
          <span className="dashboard__figure-value">{load.totals.completedThisMonth}</span>
          <span className="dashboard__figure-label">Completed this month</span>
        </div>
      </section>

      {load.checkers.length > 0 ? (
        <table className="worklist workload__table">
          <caption>{discipline} checks by checker, busiest first. Ages are in working days.</caption>
          <thead>
            <tr>
              <th scope="col">Checker</th>
              <th scope="col">Allocated</th>
              <th scope="col">In progress</th>
              <th scope="col">Completed (last 30 days)</th>
              <th scope="col">Oldest open</th>
            </tr>
          </thead>
          <tbody>
            {load.checkers.map((row) => (
              <tr key={row.checker}>
                <th scope="row">
                  {row.checker === 'Unassigned' ? (
                    row.checker
                  ) : (
                    <Link to={`/cases?person=${encodeURIComponent(row.checker)}`}>{row.checker}</Link>
                  )}
                </th>
                <td>{row.allocated}</td>
                <td>{row.inProgress}</td>
                <td>{row.completedLast30Days}</td>
                <td>{row.oldestOpenWorkingDays === null ? '—' : `${row.oldestOpenWorkingDays} working days`}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p role="status">No {discipline} check has been opened yet.</p>
      )}
    </>
  );
}
