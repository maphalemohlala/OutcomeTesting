import { useMemo } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator, TaxOutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { CASE_STATUSES, OUTCOMES, REVIEW_ROUTES } from '../../types/domain';
import type { ReviewRoute, ReviewType } from '../../types/domain';
import { CHECKER_LABELS, checkerState } from './checkerNames';
import { CASE_EXPORT_HEADERS, caseExportRow } from './caseExport';
import { useCaseWorklist } from './useCaseWorklist';
import type { CaseRemediation } from './caseRemediation';
import { applyFilters, FILTER_KEYS, type FilterKey, type Filters } from './worklistFilters';
import './CaseWorklistPage.css';

/**
 * One discipline's checker.
 *
 * A name is a person; an absence is one of two different things, and they are not
 * interchangeable - a case that will never take a Tax check has not been overlooked, a case
 * awaiting allocation has. `checkerState` reads the route to tell them apart, and the two
 * are styled differently so a column of them can be skimmed.
 */
function CheckerCell({
  name,
  route,
  type,
}: {
  name: string | null;
  route: ReviewRoute | null;
  type: ReviewType;
}) {
  const state = checkerState(name, route, type);

  if (state.kind === 'named') {
    return (
      <td>
        <Link to={`/people/Checker/${encodeURIComponent(state.name)}`}>{state.name}</Link>
      </td>
    );
  }

  return (
    <td className={`worklist__checker worklist__checker--${state.kind}`}>
      {CHECKER_LABELS[state.kind]}
    </td>
  );
}

/**
 * Where the case's remediation stands, in actions, so a list filtered to Open or Complete
 * shows why each case is in it. Actions, not cases, are what the reporting tile used to
 * count - naming them here keeps the two numbers from being mistaken for each other again.
 */
function RemediationCell({ remediation }: { remediation: CaseRemediation | null | undefined }) {
  if (!remediation) return <td className="worklist__muted">None</td>;
  if (remediation.state === 'complete') {
    return (
      <td>
        Complete{' '}
        <span className="worklist__muted">
          ({remediation.total} action{remediation.total === 1 ? '' : 's'})
        </span>
      </td>
    );
  }
  return (
    <td>
      Open{' '}
      <span className="worklist__muted">
        ({remediation.open} of {remediation.total} action{remediation.total === 1 ? '' : 's'})
      </span>
    </td>
  );
}

export function CaseWorklistPage() {
  const state = useCaseWorklist();
  const [params, setParams] = useSearchParams();

  const filters = useMemo(
    () => Object.fromEntries(FILTER_KEYS.map((key) => [key, params.get(key) ?? ''])) as Filters,
    [params],
  );

  const allCases = useMemo(() => (state.status === 'ready' ? state.cases : []), [state]);
  const remediationKnown = state.status === 'ready' && state.remediationKnown;

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
            {remediationKnown ? (
              <FilterField label="Remediation" htmlFor="worklist-remediation">
                <select
                  id="worklist-remediation"
                  value={filters.remediation}
                  onChange={(e) => set('remediation', e.target.value)}
                >
                  <option value="">All cases</option>
                  <option value="open">Open</option>
                  <option value="complete">Complete</option>
                  <option value="none">None raised</option>
                </select>
              </FilterField>
            ) : null}
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
                <option value="none">Not routed</option>
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

          {filters.adviser || filters.checker ? (
            <p className="worklist__scope" role="status">
              Showing cases{filters.adviser ? <> advised by <strong>{filters.adviser}</strong></> : null}
              {filters.adviser && filters.checker ? ' and' : null}
              {filters.checker ? <> checked by <strong>{filters.checker}</strong></> : null}.{' '}
              <button
                type="button"
                className="worklist__scope-clear"
                onClick={() => {
                  const next = new URLSearchParams(params);
                  next.delete('adviser');
                  next.delete('checker');
                  setParams(next, { replace: true });
                }}
              >
                Show everyone
              </button>
            </p>
          ) : null}

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
                  {/*
                    TWO columns, not one. A case on Tax then AQS has two checkers, and the
                    single checker column these replace named whichever discipline was
                    allocated second - which is the defect Fixes 2 removed from the header
                    (2026-09-19). Merging them back here would put it straight back.

                    They sit after Route because Route is what decides which of the two
                    empty states an absent name means.
                  */}
                  <th scope="col">Tax checker</th>
                  <th scope="col">AQS checker</th>
                  <th scope="col">Status</th>
                  <th scope="col" className="worklist__numeric">
                    Age
                  </th>
                  <th scope="col">Latest outcome</th>
                  {remediationKnown ? <th scope="col">Remediation</th> : null}
                </tr>
              </thead>
              <tbody>
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={remediationKnown ? 10 : 9} className="worklist__empty">
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
                      <CheckerCell name={item.taxChecker} route={item.route} type="Tax" />
                      <CheckerCell name={item.aqsChecker} route={item.route} type="AQS" />
                      <td>
                        <StageLabel status={item.status} />
                      </td>
                      <td className="worklist__numeric">{item.ageInDays} days</td>
                      <td>
                        {item.latestOutcome ? (
                          <OutcomeIndicator outcome={item.latestOutcome} />
                        ) : item.taxOutcome ? (
                          // A Tax check grades on its own scale and writes no al_outcome row
                          // (AD-055), so a Tax-only case has no BR-005 outcome to show and
                          // read as ungraded however it was graded. Labelled "Tax:" so the
                          // two scales are not silently mixed in one column.
                          <TaxOutcomeIndicator outcome={item.taxOutcome} />
                        ) : (
                          'Not yet graded'
                        )}
                      </td>
                      {remediationKnown ? <RemediationCell remediation={item.remediation} /> : null}
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
