import { Fragment, useDeferredValue, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator, TaxOutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { Icon } from '../../components/icons/Icon';
import { Avatar } from '../../components/people/Avatar';
import { Pager } from '../../components/navigation/Pager';
import { date } from '../../lib/format';
import { pageOf } from '../../components/navigation/paging';
import '../../components/navigation/Tabs.css';
import { CASE_STATUSES, OUTCOMES, REVIEW_ROUTES } from '../../types/domain';
import type { CaseStatus, ReviewRoute } from '../../types/domain';
import { CHECKER_LABELS, checkerState } from './checkerNames';
import { CASE_EXPORT_HEADERS, caseExportRow } from './caseExport';
import { useCaseWorklist } from './useCaseWorklist';
import { CaseReferenceLink } from './CaseReferenceLink';
import type { CaseRemediation } from './caseRemediation';
import type { CaseSummary } from './caseWorklistMapping';
import { applyFilters, FILTER_KEYS, type FilterKey, type Filters } from './worklistFilters';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import {
  adviserIdentity,
  checkerIdentity,
  contactEmailsOf,
  labelForIdentity,
  type ContactEmails,
} from '../people/peopleDirectory';
import './CaseWorklistPage.css';
import { Skeleton } from '../../components/feedback/Skeleton';

const PAGE_SIZE = 15;

/**
 * Both checkers in one cell, one line per discipline, each labelled Tax or AQS. Never one
 * merged name: a case on Tax then AQS has two checkers, and a single unlabelled name is the
 * defect Fixes 2 removed from the header (2026-09-19).
 *
 * A discipline the route never calls for gets no line - the route chips beside it already say
 * so. One that is owed but not allocated says so, muted, because that is outstanding work.
 */
function CheckersCell({ item, contactEmails }: { item: CaseSummary; contactEmails: ContactEmails }) {
  const lines = (
    [
      ['Tax', item.taxChecker, item.taxCheckerId],
      ['AQS', item.aqsChecker, item.aqsCheckerId],
    ] as const
  )
    .map(([type, name, contactId]) => ({ type, contactId, state: checkerState(name, item.route, type) }))
    .filter((line) => line.state.kind !== 'not-required');

  return (
    <td>
      <ul className="worklist__checkers">
        {lines.map(({ type, contactId, state }) => (
          <li key={type}>
            <span className="worklist__checker-type">{type}</span>
            {state.kind === 'named' ? (
              <>
                <Avatar name={state.name} size="sm" />
                <Link
                  to={`/people/Checker/${encodeURIComponent(checkerIdentity(state.name, contactId, contactEmails))}`}
                >
                  {state.name}
                </Link>
              </>
            ) : (
              <span className="worklist__muted">{CHECKER_LABELS.unallocated}</span>
            )}
          </li>
        ))}
      </ul>
    </td>
  );
}

/** The route as the checks it runs, in order: "Tax → AQS". The words are read out, the chips are not. */
function RouteCell({ route }: { route: ReviewRoute | null }) {
  if (!route) return <td className="worklist__muted">Not routed</td>;
  const steps = route === 'Tax then AQS' ? ['Tax', 'AQS'] : [route.replace(' only', '')];
  return (
    <td>
      <span className="visually-hidden">{route}</span>
      <span className="worklist__route" aria-hidden="true">
        {steps.map((step, index) => (
          <Fragment key={step}>
            {index > 0 ? <Icon name="arrowRight" className="icon worklist__route-arrow" /> : null}
            <span className="worklist__chip">{step}</span>
          </Fragment>
        ))}
      </span>
    </td>
  );
}

/** A status as the tabs word it: sentence case, so "Awaiting Sign-off" reads "Awaiting sign-off". */
const tabLabel = (status: string) => status.charAt(0) + status.slice(1).toLowerCase();

/**
 * Where the case's remediation stands, in actions, so a list filtered to Open or Complete
 * shows why each case is in it. Actions, not cases, are what the reporting tile used to
 * count - naming them here keeps the two numbers from being mistaken for each other again.
 */
function RemediationCell({ remediation }: { remediation: CaseRemediation | null | undefined }) {
  if (!remediation) return <td className="worklist__muted">None</td>;
  if (remediation.state === 'complete') {
    return (
      <td className="worklist__remediation">
        <Icon name="check" className="icon worklist__done" />{' '}
        {remediation.total === 1 ? '1 action done' : `All ${remediation.total} done`}
      </td>
    );
  }
  const done = remediation.total - remediation.open;
  return (
    <td className="worklist__remediation">
      <span className="worklist__open">
        {remediation.open} of {remediation.total} open
      </span>
      <span className="worklist__bar" aria-hidden="true">
        <span style={{ width: `${(done / remediation.total) * 100}%` }} />
      </span>
    </td>
  );
}

export function CaseWorklistPage() {
  const state = useCaseWorklist();
  const directory = useUserDirectory();
  const [params, setParams] = useSearchParams();

  const filters = useMemo(
    () => Object.fromEntries(FILTER_KEYS.map((key) => [key, params.get(key) ?? ''])) as Filters,
    [params],
  );

  const allCases = useMemo(() => (state.status === 'ready' ? state.cases : []), [state]);
  const remediationKnown = state.status === 'ready' && state.remediationKnown;

  const contactEmails = useMemo(
    () => contactEmailsOf(directory.status === 'ready' ? directory.users : []),
    [directory],
  );

  const priorities = useMemo(
    () =>
      [...new Set(allCases.map((c) => c.priority).filter((p): p is string => Boolean(p)))].sort(),
    [allCases],
  );

  const checks = useMemo(
    () =>
      [...new Set(allCases.map((c) => c.preOrPostCheck).filter((p): p is string => Boolean(p)))].sort(),
    [allCases],
  );

  // The URL and the inputs move on every keystroke; the list follows a beat behind, so
  // typing in the search box is never held up by re-filtering and redrawing every row.
  const appliedFilters = useDeferredValue(filters);
  const filtered = useMemo(
    () => applyFilters(allCases, appliedFilters, contactEmails),
    [allCases, appliedFilters, contactEmails],
  );
  const exportRows = useMemo(() => filtered.map(caseExportRow), [filtered]);
  // From the filters the rows were drawn with, so the file's name and the banners never
  // describe a list other than the one on screen.
  const isFiltered = FILTER_KEYS.some((key) => appliedFilters[key] !== '');

  // Every status that holds a case, counted over the whole list so a tab's number does not
  // move with the other filters. "All" leaves out No Check Required, as the list itself does.
  const statusCounts = useMemo(() => {
    const counts = new Map<CaseStatus, number>();
    for (const item of allCases) counts.set(item.status, (counts.get(item.status) ?? 0) + 1);
    return counts;
  }, [allCases]);
  const allCount = allCases.length - (statusCounts.get('No Check Required') ?? 0);
  const statusTabs = [
    { status: '', label: 'All', count: allCount },
    ...CASE_STATUSES.filter((status) => statusCounts.has(status) || status === filters.status).map((status) => ({
      status,
      label: tabLabel(status),
      count: statusCounts.get(status) ?? 0,
    })),
  ];
  const tabButtons = useRef<Record<string, HTMLButtonElement | null>>({});

  // The page resets to the first whenever the filters change, without an effect: the page
  // number is held against the filters it was chosen under.
  const filterKey = params.toString();
  const [paging, setPaging] = useState({ key: filterKey, page: 1 });
  const page = pageOf(filtered, paging.key === filterKey ? paging.page : 1, PAGE_SIZE);

  function onTabKey(event: KeyboardEvent<HTMLDivElement>) {
    const index = statusTabs.findIndex((tab) => tab.status === filters.status);
    let next: number;
    if (event.key === 'ArrowRight') next = (index + 1) % statusTabs.length;
    else if (event.key === 'ArrowLeft') next = (index - 1 + statusTabs.length) % statusTabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = statusTabs.length - 1;
    else return;
    event.preventDefault();
    const status = statusTabs[next].status;
    set('status', status);
    tabButtons.current[status]?.focus();
  }

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
              rows={exportRows}
              caption={`Exports the ${filtered.length} case${filtered.length === 1 ? '' : 's'} currently listed`}
            />
          ) : null
        }
      />

      {state.status === 'loading' ? <Skeleton label="Loading cases…" rows={6} /> : null}

      {state.status === 'unavailable' ? (
        <section className="worklist__unavailable" aria-labelledby="worklist-unavailable">
          <h2 id="worklist-unavailable">No cases can be listed</h2>
          <p>{state.reason}</p>
          <p>
            Nothing has been lost. Please try again shortly.
          </p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <div className="tabs__list worklist__tabs" role="tablist" aria-label="Case status" onKeyDown={onTabKey}>
            {statusTabs.map((tab) => {
              const selected = tab.status === filters.status;
              return (
                <button
                  key={tab.status || 'all'}
                  ref={(element) => {
                    tabButtons.current[tab.status] = element;
                  }}
                  type="button"
                  role="tab"
                  id={`worklist-tab-${tab.status.replace(/\W+/g, '-') || 'all'}`}
                  className="tabs__tab"
                  aria-selected={selected}
                  aria-controls="worklist-panel"
                  tabIndex={selected ? 0 : -1}
                  onClick={() => set('status', tab.status)}
                >
                  {tab.label}
                  <span className="tabs__count">{tab.count}</span>
                </button>
              );
            })}
          </div>

          <div
            id="worklist-panel"
            role="tabpanel"
            className="worklist__panel"
            aria-labelledby={`worklist-tab-${filters.status.replace(/\W+/g, '-') || 'all'}`}
          >
          <FilterBar
            summary={`${filtered.length} of ${allCount} cases`}
            onClear={() => setParams(new URLSearchParams(), { replace: true })}
            clearDisabled={!isFiltered}
          >
            <FilterField label="Search" htmlFor="worklist-search" search>
              <input
                id="worklist-search"
                type="search"
                value={filters.q}
                onChange={(e) => set('q', e.target.value)}
                placeholder="Case ref, client or adviser"
                title="Searches the case reference, service case reference, client, adviser and owner"
              />
            </FilterField>
            <FilterField label="Outcome" htmlFor="worklist-outcome">
              <select
                id="worklist-outcome"
                value={filters.outcome}
                onChange={(e) => set('outcome', e.target.value)}
              >
                <option value="">All</option>
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
                  <option value="">All</option>
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
                <option value="">All</option>
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
                  <option value="">All</option>
                  {priorities.map((priority) => (
                    <option key={priority} value={priority}>
                      {priority}
                    </option>
                  ))}
                </select>
              </FilterField>
            ) : null}
            {checks.length > 0 ? (
              <FilterField label="Check" htmlFor="worklist-check">
                <select
                  id="worklist-check"
                  value={filters.check}
                  onChange={(e) => set('check', e.target.value)}
                >
                  <option value="">Pre and post</option>
                  {checks.map((check) => (
                    <option key={check} value={check}>
                      {check}
                    </option>
                  ))}
                </select>
              </FilterField>
            ) : null}
            <FilterField label="Imported" htmlFor="worklist-from">
              <span className="worklist__dates">
                <input
                  id="worklist-from"
                  type="date"
                  aria-label="Imported from"
                  value={filters.from}
                  onChange={(e) => set('from', e.target.value)}
                />
                <span aria-hidden="true">to</span>
                <input
                  type="date"
                  aria-label="Imported to"
                  value={filters.to}
                  onChange={(e) => set('to', e.target.value)}
                />
              </span>
            </FilterField>
          </FilterBar>

          {appliedFilters.adviser || appliedFilters.checker ? (
            <p className="worklist__scope" role="status">
              Showing cases
              {appliedFilters.adviser ? (
                <>
                  {' '}
                  advised by{' '}
                  <strong>
                    {allCases.find(
                      (item) =>
                        (item.adviserEmail ?? '').trim().toLowerCase() ===
                        appliedFilters.adviser.trim().toLowerCase(),
                    )?.adviser ?? appliedFilters.adviser}
                  </strong>
                </>
              ) : null}
              {appliedFilters.adviser && appliedFilters.checker ? ' and' : null}
              {appliedFilters.checker ? (
                <>
                  {' '}
                  checked by{' '}
                  <strong>{labelForIdentity(allCases, appliedFilters.checker, contactEmails)}</strong>
                </>
              ) : null}
              .{' '}
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

          {appliedFilters.person ? (
            <p className="worklist__scope" role="status">
              Showing cases involving{' '}
              <strong>{labelForIdentity(allCases, appliedFilters.person, contactEmails)}</strong>.{' '}
              <button
                type="button"
                className="worklist__scope-clear"
                onClick={() => set('person', '')}
              >
                Show everyone
              </button>
            </p>
          ) : null}

          <div className="worklist__card">
            <div className="worklist__scroll">
              <table className="worklist">
                <caption className="visually-hidden">
                  Cases assigned to you or your team, oldest first
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Case</th>
                    <th scope="col">Adviser</th>
                    <th scope="col">Route</th>
                    <th scope="col">Checkers</th>
                    <th scope="col">Status</th>
                    <th scope="col">Added on</th>
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
                      <td colSpan={remediationKnown ? 9 : 8} className="worklist__empty">
                        No cases match your current filters.
                      </td>
                    </tr>
                  ) : (
                    page.rows.map((item) => (
                      <tr key={item.id}>
                        <th scope="row">
                          <span className="worklist__ref">
                            <CaseReferenceLink id={item.id} caseReference={item.caseReference} serviceCaseRef={item.serviceCaseRef} />
                          </span>
                          <span className="worklist__client" title={item.client ?? undefined}>
                            {item.client ?? 'No client recorded'}
                          </span>
                        </th>
                        <td>
                          {item.adviser ? (
                            <Link to={`/people/Adviser/${encodeURIComponent(adviserIdentity(item))}`}>
                              {item.adviser}
                            </Link>
                          ) : (
                            '—'
                          )}
                        </td>
                        <RouteCell route={item.route} />
                        <CheckersCell item={item} contactEmails={contactEmails} />
                        <td>
                          <StageLabel status={item.status} />
                        </td>
                        <td>{date(item.createdOn) ?? '—'}</td>
                        <td className="worklist__numeric">{item.ageInDays} working day{item.ageInDays === 1 ? '' : 's'}</td>
                        <td>
                          {item.latestOutcome ? (
                            <OutcomeIndicator outcome={item.latestOutcome} />
                          ) : item.taxOutcome ? (
                            // A Tax check grades on its own scale and writes no outcome row, so
                            // a Tax-only case would read as ungraded however it was graded.
                            // Labelled "Tax:" so the two scales are not silently mixed.
                            <TaxOutcomeIndicator outcome={item.taxOutcome} />
                          ) : (
                            <span className="worklist__muted">Not yet graded</span>
                          )}
                        </td>
                        {remediationKnown ? <RemediationCell remediation={item.remediation} /> : null}
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
            <Pager {...page} setPage={(next) => setPaging({ key: filterKey, page: next })} label="cases" />
          </div>
          </div>
        </>
      ) : null}
    </>
  );
}
