import { useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { useExports } from './useExports';
import {
  TRAIL_LIGHT_HEADERS,
  describeCheckRange,
  fileContentsSummary,
  trailLightFilename,
  trailLightRow,
  ukDay,
  withinCheckDates,
} from './trailLight';
import './ExportsPage.css';

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
}

/**
 * One export batch: its cases, and the Trail Light file cut to a check-date range
 * (2026-09-29). The range used to sit above the batch list, applied to every download on
 * the page, and users could not tell what it did. Here it sits next to the rows it
 * decides, the table shows only the cases the file will hold, and one sentence says so.
 *
 * The range is kept in the URL (`?from=&to=`) so a cut batch can be reopened or shared.
 */
export function ExportBatchPage() {
  const { batchId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const from = params.get('from') ?? '';
  const to = params.get('to') ?? '';
  const state = useExports(0);

  function setRange(key: 'from' | 'to', value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  }

  const batch = state.status === 'ready' ? state.batches.find((b) => b.id === batchId) : undefined;
  const allRecords = useMemo(
    () => (state.status === 'ready' ? state.records.filter((r) => r.batchId === batchId) : []),
    [state, batchId],
  );
  const records = useMemo(
    () => allRecords.filter((r) => withinCheckDates(r.record, from, to)),
    [allRecords, from, to],
  );
  const range = describeCheckRange(from, to);
  const isDraft = batch?.status === 'Draft';

  return (
    <>
      <p className="exports__back">
        <Link to="/exports">← Exports</Link>
      </p>

      {state.status === 'loading' ? <p role="status">Loading the batch…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="exports__unavailable" aria-labelledby="batch-unavailable">
          <h2 id="batch-unavailable">This batch cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' && !batch ? (
        <section className="exports__unavailable" aria-labelledby="batch-missing">
          <h2 id="batch-missing">Batch not found</h2>
          <p>No export batch with this link is readable by you.</p>
        </section>
      ) : null}

      {batch ? (
        <>
          <PageIntro
            title={batch.name || 'Export batch'}
            purpose={`${batch.status} · generated ${formatDate(batch.generatedOn)} · ${batch.rowCount} case(s) snapshotted`}
          />

          {isDraft ? (
            <p className="exports__notice exports__notice--error" role="status">
              This batch has not been generated yet, so it holds no cases. Generate it from the
              Exports list first.
            </p>
          ) : (
            <>
              <section aria-labelledby="range-heading">
                <h2 id="range-heading" className="exports__heading">
                  Which cases go in the file
                </h2>
                <p className="exports__note">
                  Choose the check dates to include - the day each case's review was submitted
                  (column G). Both dates are included. Leave both empty to download every case
                  in this batch.
                </p>
                <FilterBar
                  summary={range ? `Cases ${range}` : 'Every case in this batch'}
                  onClear={() => setParams(new URLSearchParams(), { replace: true })}
                  clearDisabled={!from && !to}
                >
                  <FilterField label="Checked from" htmlFor="check-from">
                    <input
                      id="check-from"
                      type="date"
                      value={from}
                      max={to || undefined}
                      onChange={(e) => setRange('from', e.target.value)}
                    />
                  </FilterField>
                  <FilterField label="Checked to" htmlFor="check-to">
                    <input
                      id="check-to"
                      type="date"
                      value={to}
                      min={from || undefined}
                      onChange={(e) => setRange('to', e.target.value)}
                    />
                  </FilterField>
                </FilterBar>
              </section>

              <section aria-labelledby="cases-heading">
                <div className="exports__section-head">
                  <h2 id="cases-heading" className="exports__heading">
                    Cases in the file
                  </h2>
                  <ExportMenu
                    label="Download Trail Light file"
                    stem="trail-light"
                    filenameFor={(format) => trailLightFilename(format)}
                    sheetName="Trail Light"
                    headers={TRAIL_LIGHT_HEADERS}
                    rows={records.map((r) => trailLightRow(r.record))}
                    caption={`${records.length} row(s)${range ? `, ${range},` : ''} in the AD-039 column order`}
                    emptyHint={
                      allRecords.length === 0
                        ? 'This batch generated 0 rows, because no cases were at status Closed.'
                        : `None of this batch's cases were ${range}.`
                    }
                  />
                </div>
                <p className="exports__summary" role="status">
                  {fileContentsSummary(records.length, allRecords.length, from, to)}
                </p>
                <table className="exports__table">
                  <thead>
                    <tr>
                      <th scope="col">Case</th>
                      <th scope="col">Client</th>
                      <th scope="col">Adviser</th>
                      <th scope="col">Check date</th>
                      <th scope="col">File quality</th>
                      <th scope="col">Advice quality</th>
                    </tr>
                  </thead>
                  <tbody>
                    {records.length === 0 ? (
                      <tr>
                        <td colSpan={6} className="exports__empty">
                          {allRecords.length === 0
                            ? 'This batch holds no cases.'
                            : 'No cases in this batch fall in the chosen range.'}
                        </td>
                      </tr>
                    ) : (
                      records.map((r) => (
                        <tr key={r.id}>
                          <td>
                            {r.caseId ? (
                              <Link to={`/cases/${r.caseId}`}>{r.caseReference || r.name || 'Open case'}</Link>
                            ) : (
                              r.caseReference || r.name
                            )}
                          </td>
                          <td>{r.client}</td>
                          <td>{r.adviser}</td>
                          <td>{r.record.al_checkdate ? ukDay(r.record.al_checkdate.slice(0, 10)) : ''}</td>
                          <td>{r.record.al_filequalitygrade ?? ''}</td>
                          <td>{r.adviceGrade}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </section>
            </>
          )}
        </>
      ) : null}
    </>
  );
}
