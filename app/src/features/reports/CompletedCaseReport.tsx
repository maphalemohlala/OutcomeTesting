import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { OUTCOMES } from '../../types/domain';
import { COMPLETED_CASE_HEADERS, completedCaseRow } from '../cases/caseExport';
import { useCaseWorklist } from '../cases/useCaseWorklist';
import './CompletedCaseReport.css';

/** The check date is the business date of the report; where it is missing, intake date stands in. */
function reportDate(item: { checkDate: string | null; createdOn: string | null }): string {
  return (item.checkDate ?? item.createdOn ?? '').slice(0, 10);
}

/**
 * The completed-case report (BR-010, FR-032): every case that has reached a grade, with the
 * product it was advised on and the outcome it stands on. A case is "completed" here when
 * an Outcome exists for it — closure is a separate lifecycle step, so filtering on Closed
 * alone would leave graded cases still in remediation out of the count.
 */
export function CompletedCaseReport() {
  const state = useCaseWorklist();
  const [outcome, setOutcome] = useState('');
  const [product, setProduct] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [closedOnly, setClosedOnly] = useState(false);

  const completed = useMemo(
    () => (state.status === 'ready' ? state.cases.filter((item) => item.latestOutcome) : []),
    [state],
  );

  const products = useMemo(
    () =>
      [...new Set(completed.map((c) => c.productSolutionType).filter((p): p is string => Boolean(p)))].sort(),
    [completed],
  );

  const filtered = useMemo(
    () =>
      completed.filter((item) => {
        if (outcome && item.latestOutcome !== outcome) return false;
        if (product && item.productSolutionType !== product) return false;
        if (closedOnly && item.status !== 'Closed') return false;
        const day = reportDate(item);
        if (from && (!day || day < from)) return false;
        if (to && (!day || day > to)) return false;
        return true;
      }),
    [completed, outcome, product, from, to, closedOnly],
  );

  const isFiltered = Boolean(outcome || product || from || to || closedOnly);

  if (state.status !== 'ready') return null;

  return (
    <section className="completed" aria-labelledby="completed-heading">
      <div className="completed__head">
        <h2 id="completed-heading">Completed case report</h2>
        {filtered.length > 0 ? (
          <ExportMenu
            label="Export report"
            stem="completed-cases"
            sheetName="Completed cases"
            headers={COMPLETED_CASE_HEADERS}
            rows={filtered.map(completedCaseRow)}
            caption={`Exports the ${filtered.length} completed case${filtered.length === 1 ? '' : 's'} currently listed`}
          />
        ) : null}
      </div>

      <p className="completed__note">
        Cases that have reached a grade, with the product advised on and the outcome recorded
        (BR-005, BR-007). Filter by check date, product or outcome before exporting. Restrict to
        Closed if you need only cases whose lifecycle has finished.
      </p>

      <FilterBar
        summary={`${filtered.length} of ${completed.length} completed cases`}
        onClear={() => {
          setOutcome('');
          setProduct('');
          setFrom('');
          setTo('');
          setClosedOnly(false);
        }}
        clearDisabled={!isFiltered}
      >
        <FilterField label="Outcome" htmlFor="completed-outcome">
          <select
            id="completed-outcome"
            value={outcome}
            onChange={(e) => setOutcome(e.target.value)}
          >
            <option value="">All outcomes</option>
            {OUTCOMES.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </FilterField>
        <FilterField label="Product / solution type" htmlFor="completed-product">
          <select id="completed-product" value={product} onChange={(e) => setProduct(e.target.value)}>
            <option value="">All products</option>
            {products.map((value) => (
              <option key={value} value={value}>
                {value}
              </option>
            ))}
          </select>
        </FilterField>
        <FilterField label="Checked from" htmlFor="completed-from">
          <input id="completed-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </FilterField>
        <FilterField label="Checked to" htmlFor="completed-to">
          <input id="completed-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </FilterField>
        <FilterField label="Closed cases only" htmlFor="completed-closed">
          <input
            id="completed-closed"
            type="checkbox"
            checked={closedOnly}
            onChange={(e) => setClosedOnly(e.target.checked)}
          />
        </FilterField>
      </FilterBar>

      <div className="completed__scroll">
        <table className="completed__table">
          <caption className="visually-hidden">Completed cases with product and outcome</caption>
          <thead>
            <tr>
              <th scope="col">Case</th>
              <th scope="col">Client</th>
              <th scope="col">Adviser</th>
              <th scope="col">Product / solution type</th>
              <th scope="col">Check date</th>
              <th scope="col">Initial outcome</th>
              <th scope="col">Outcome</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 ? (
              <tr>
                <td colSpan={7} className="completed__empty">
                  No completed cases match your current filters.
                </td>
              </tr>
            ) : (
              filtered.map((item) => (
                <tr key={item.id}>
                  <th scope="row">
                    <Link to={`/cases/${item.id}`}>{item.caseReference}</Link>
                  </th>
                  <td>{item.client ?? '—'}</td>
                  <td>{item.adviser ?? '—'}</td>
                  <td>{item.productSolutionType ?? '—'}</td>
                  <td>{reportDate(item) || '—'}</td>
                  <td>
                    {item.initialOutcome ? <OutcomeIndicator outcome={item.initialOutcome} /> : '—'}
                  </td>
                  <td>
                    {item.latestOutcome ? <OutcomeIndicator outcome={item.latestOutcome} /> : '—'}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </section>
  );
}
