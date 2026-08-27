import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { CASE_STATUSES, REVIEW_ROUTES } from '../../types/domain';
import { useCaseWorklist, type CaseSummary } from './useCaseWorklist';
import './CaseWorklistPage.css';

interface Filters {
  search: string;
  status: string;
  route: string;
  priority: string;
  from: string;
  to: string;
}

const EMPTY_FILTERS: Filters = {
  search: '',
  status: '',
  route: '',
  priority: '',
  from: '',
  to: '',
};

function withinRange(createdOn: string | null, from: string, to: string): boolean {
  if (!from && !to) return true;
  if (!createdOn) return false;
  const created = createdOn.slice(0, 10);
  if (from && created < from) return false;
  if (to && created > to) return false;
  return true;
}

function applyFilters(cases: CaseSummary[], filters: Filters): CaseSummary[] {
  const search = filters.search.trim().toLowerCase();
  return cases.filter((item) => {
    if (filters.status && item.status !== filters.status) return false;
    if (filters.route && item.route !== filters.route) return false;
    if (filters.priority && (item.priority ?? '') !== filters.priority) return false;
    if (!withinRange(item.createdOn, filters.from, filters.to)) return false;
    if (search) {
      const haystack = `${item.caseReference} ${item.owner ?? ''}`.toLowerCase();
      if (!haystack.includes(search)) return false;
    }
    return true;
  });
}

export function CaseWorklistPage() {
  const state = useCaseWorklist();
  const [filters, setFilters] = useState<Filters>(EMPTY_FILTERS);

  const allCases = state.status === 'ready' ? state.cases : [];

  const priorities = useMemo(
    () =>
      [...new Set(allCases.map((c) => c.priority).filter((p): p is string => Boolean(p)))].sort(),
    [allCases],
  );

  const filtered = useMemo(() => applyFilters(allCases, filters), [allCases, filters]);
  const isFiltered = useMemo(
    () => JSON.stringify(filters) !== JSON.stringify(EMPTY_FILTERS),
    [filters],
  );

  function set<K extends keyof Filters>(key: K, value: Filters[K]) {
    setFilters((prev) => ({ ...prev, [key]: value }));
  }

  return (
    <>
      <PageIntro
        title="Case worklist"
        purpose="Find the cases you own and decide what to pick up next."
      />

      {state.status === 'loading' ? <p role="status">Loading cases…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="worklist__unavailable" aria-labelledby="worklist-unavailable">
          <h2 id="worklist-unavailable">No cases can be listed</h2>
          <p>{state.reason}</p>
          <p>
            Nothing has been lost. Once the case tables are deployed this list will populate
            automatically.
          </p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <FilterBar
            summary={`${filtered.length} of ${allCases.length} cases`}
            onClear={() => setFilters(EMPTY_FILTERS)}
            clearDisabled={!isFiltered}
          >
            <FilterField label="Search" htmlFor="worklist-search">
              <input
                id="worklist-search"
                type="search"
                value={filters.search}
                onChange={(e) => set('search', e.target.value)}
                placeholder="Case ref or owner"
              />
            </FilterField>
            <FilterField label="Status" htmlFor="worklist-status">
              <select
                id="worklist-status"
                value={filters.status}
                onChange={(e) => set('status', e.target.value)}
              >
                <option value="">All statuses</option>
                {CASE_STATUSES.map((status) => (
                  <option key={status} value={status}>
                    {status}
                  </option>
                ))}
              </select>
            </FilterField>
            <FilterField label="Route" htmlFor="worklist-route">
              <select
                id="worklist-route"
                value={filters.route}
                onChange={(e) => set('route', e.target.value)}
              >
                <option value="">All routes</option>
                {REVIEW_ROUTES.map((route) => (
                  <option key={route} value={route}>
                    {route}
                  </option>
                ))}
              </select>
            </FilterField>
            {priorities.length > 0 ? (
              <FilterField label="Priority" htmlFor="worklist-priority">
                <select
                  id="worklist-priority"
                  value={filters.priority}
                  onChange={(e) => set('priority', e.target.value)}
                >
                  <option value="">All priorities</option>
                  {priorities.map((priority) => (
                    <option key={priority} value={priority}>
                      {priority}
                    </option>
                  ))}
                </select>
              </FilterField>
            ) : null}
            <FilterField label="Imported from" htmlFor="worklist-from">
              <input
                id="worklist-from"
                type="date"
                value={filters.from}
                onChange={(e) => set('from', e.target.value)}
              />
            </FilterField>
            <FilterField label="Imported to" htmlFor="worklist-to">
              <input
                id="worklist-to"
                type="date"
                value={filters.to}
                onChange={(e) => set('to', e.target.value)}
              />
            </FilterField>
          </FilterBar>

          <div className="worklist__scroll">
            <table className="worklist">
              <caption className="visually-hidden">
                Cases assigned to you or your team, oldest first
              </caption>
              <thead>
                <tr>
                  <th scope="col">Case</th>
                  <th scope="col">Route</th>
                  <th scope="col">Status</th>
                  <th scope="col">Owner</th>
                  <th scope="col">Priority</th>
                  <th scope="col" className="worklist__numeric">
                    Age
                  </th>
                  <th scope="col">Latest outcome</th>
                  <th scope="col">Next action</th>
                </tr>
              </thead>
              <tbody>
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={8} className="worklist__empty">
                      No cases match your current filters.
                    </td>
                  </tr>
                ) : (
                  filtered.map((item) => (
                    <tr key={item.id}>
                      <th scope="row">
                        <Link to={`/cases/${item.id}`}>{item.caseReference}</Link>
                      </th>
                      <td>{item.route ?? 'Not routed'}</td>
                      <td>
                        <StageLabel status={item.status} />
                      </td>
                      <td>{item.owner ?? 'Unassigned'}</td>
                      <td>{item.priority ?? '—'}</td>
                      <td className="worklist__numeric">{item.ageInDays} days</td>
                      <td>
                        {item.latestOutcome ? (
                          <OutcomeIndicator outcome={item.latestOutcome} />
                        ) : (
                          'Not yet graded'
                        )}
                      </td>
                      <td>{item.nextAction}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </>
      ) : null}
    </>
  );
}
