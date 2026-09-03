import { useMemo } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { CASE_STATUSES, OUTCOMES, REVIEW_ROUTES } from '../../types/domain';
import { CASE_EXPORT_HEADERS, caseExportRow } from './caseExport';
import { useCaseWorklist, type CaseSummary } from './useCaseWorklist';
import './CaseWorklistPage.css';

/**
 * Filters live in the URL so a dashboard card or a person view can link straight to the
 * list it counted, and so the view a manager exports is the view they can share back.
 */
const FILTER_KEYS = ['q', 'status', 'route', 'priority', 'outcome', 'person', 'from', 'to'] as const;

type FilterKey = (typeof FILTER_KEYS)[number];

type Filters = Record<FilterKey, string>;

function withinRange(createdOn: string | null, from: string, to: string): boolean {
  if (!from && !to) return true;
  if (!createdOn) return false;
  const created = createdOn.slice(0, 10);
  if (from && created < from) return false;
  if (to && created > to) return false;
  return true;
}

function matchesPerson(item: CaseSummary, person: string): boolean {
  const name = person.toLowerCase();
  return [item.adviser, item.paraplanner, item.checker, item.owner].some(
    (value) => (value ?? '').toLowerCase() === name,
  );
}

function applyFilters(cases: CaseSummary[], filters: Filters): CaseSummary[] {
  const search = filters.q.trim().toLowerCase();
  return cases.filter((item) => {
    if (filters.status && item.status !== filters.status) return false;
    if (filters.route && item.route !== filters.route) return false;
    if (filters.priority && (item.priority ?? '') !== filters.priority) return false;
    if (filters.outcome === 'none' && item.latestOutcome) return false;
    if (filters.outcome && filters.outcome !== 'none' && item.latestOutcome !== filters.outcome) {
      return false;
    }
    if (filters.person && !matchesPerson(item, filters.person)) return false;
    if (!withinRange(item.createdOn, filters.from, filters.to)) return false;
    if (search) {
      const haystack =
        `${item.caseReference} ${item.owner ?? ''} ${item.client ?? ''} ${item.adviser ?? ''}`.toLowerCase();
      if (!haystack.includes(search)) return false;
    }
    return true;
  });
}

export function CaseWorklistPage() {
  const state = useCaseWorklist();
  const [params, setParams] = useSearchParams();

  const filters = useMemo(
    () => Object.fromEntries(FILTER_KEYS.map((key) => [key, params.get(key) ?? ''])) as Filters,
    [params],
  );

  const allCases = state.status === 'ready' ? state.cases : [];

  const priorities = useMemo(
    () =>
      [...new Set(allCases.map((c) => c.priority).filter((p): p is string => Boolean(p)))].sort(),
    [allCases],
  );

  const filtered = useMemo(() => applyFilters(allCases, filters), [allCases, filters]);
  const isFiltered = FILTER_KEYS.some((key) => filters[key] !== '');

  function set(key: FilterKey, value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  }

  return (
    <>
      <PageIntro
        title="Case worklist"
        purpose="Find the cases you own and decide what to pick up next."
        actions={
          state.status === 'ready' && filtered.length > 0 ? (
            <ExportMenu
              label="Export cases"
              stem={isFiltered ? 'outcome-cases-filtered' : 'outcome-cases'}
              sheetName="Cases"
              headers={CASE_EXPORT_HEADERS}
              rows={filtered.map(caseExportRow)}
              caption={`Exports the ${filtered.length} case${filtered.length === 1 ? '' : 's'} currently listed`}
            />
          ) : null
        }
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
            onClear={() => setParams(new URLSearchParams(), { replace: true })}
            clearDisabled={!isFiltered}
          >
            <FilterField label="Search" htmlFor="worklist-search">
              <input
                id="worklist-search"
                type="search"
                value={filters.q}
                onChange={(e) => set('q', e.target.value)}
                placeholder="Case ref, client, adviser or owner"
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
            <FilterField label="Outcome" htmlFor="worklist-outcome">
              <select
                id="worklist-outcome"
                value={filters.outcome}
                onChange={(e) => set('outcome', e.target.value)}
              >
                <option value="">All outcomes</option>
                {OUTCOMES.map((outcome) => (
                  <option key={outcome} value={outcome}>
                    {outcome}
                  </option>
                ))}
                <option value="none">Not yet graded</option>
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

          {filters.person ? (
            <p className="worklist__scope" role="status">
              Showing cases involving <strong>{filters.person}</strong>.{' '}
              <button
                type="button"
                className="worklist__scope-clear"
                onClick={() => set('person', '')}
              >
                Show everyone
              </button>
            </p>
          ) : null}

          <div className="worklist__scroll">
            <table className="worklist">
              <caption className="visually-hidden">
                Cases assigned to you or your team, oldest first
              </caption>
              <thead>
                <tr>
                  <th scope="col">Case</th>
                  <th scope="col">Client</th>
                  <th scope="col">Adviser</th>
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
                    <td colSpan={10} className="worklist__empty">
                      No cases match your current filters.
                    </td>
                  </tr>
                ) : (
                  filtered.map((item) => (
                    <tr key={item.id}>
                      <th scope="row">
                        <Link to={`/cases/${item.id}`}>{item.caseReference}</Link>
                      </th>
                      <td>{item.client ?? '—'}</td>
                      <td>
                        {item.adviser ? (
                          <Link to={`/people/Adviser/${encodeURIComponent(item.adviser)}`}>
                            {item.adviser}
                          </Link>
                        ) : (
                          '—'
                        )}
                      </td>
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
