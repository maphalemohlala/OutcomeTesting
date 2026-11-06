import { useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { useExportBatches, useExportRecords } from './useExports';
import {
  TRAIL_LIGHT_HEADERS,
  describeCheckRange,
  fileContentsSummary,
  isPassWithNoFails,
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
 * The range and the file choice are kept in the URL (`?from=&to=&file=`) so a cut batch can
 * be reopened or shared. The daily file is the default; the quarterly file keeps every case.
 */
export function ExportBatchPage() {
  const { batchId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const from = params.get('from') ?? '';
  const to = params.get('to') ?? '';
  const quarterly = params.get('file') === 'quarterly';
  const batchState = useExportBatches(0);
  const recordState = useExportRecords(batchId);
  const state = batchState.status !== 'ready' ? batchState : recordState;

  function setRange(key: 'from' | 'to' | 'file', value: string) {
    const next = new URLSearchParams(params);
    if (value) next.set(key, value);
    else next.delete(key);
    setParams(next, { replace: true });
  }

  const batch = batchState.status === 'ready' ? batchState.batches.find((b) => b.id === batchId) : undefined;
  const allRecords = useMemo(
    () => (recordState.status === 'ready' ? recordState.records : []),
    [recordState],
  );
  const inRange = useMemo(
    () => allRecords.filter((r) => withinCheckDates(r.record, from, to)),
    [allRecords, from, to],
  );
  const records = useMemo(
    () => (quarterly ? inRange : inRange.filter((r) => !isPassWithNoFails(r.record))),
    [inRange, quarterly],
  );
  const passesLeftOut = inRange.length - records.length;
  const nothingInFile =
    allRecords.length === 0
      ? 'This batch holds no cases.'
      : inRange.length === 0
        ? 'No cases in this batch fall in the chosen range.'
        : 'Every case in range is a pass with no fails. Choose the quarterly file to include them.';
  const range = describeCheckRange(from, to);
  const isDraft = batch?.status === 'Draft';
  // The batch recorded how many rows it wrote. Anything else here means the file would be short.
  const incomplete =
    state.status === 'ready' && batch !== undefined && !isDraft && allRecords.length !== batch.rowCount;

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

      {batch && state.status === 'ready' ? (
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
              {incomplete ? (
                <p className="exports__notice exports__notice--error" role="alert">
                  This batch recorded {batch.rowCount} case(s) but {allRecords.length} could be read,
                  so the file would be incomplete and cannot be downloaded. Reload the page, and
                  report it if this stays.
                </p>
              ) : null}

              <section aria-labelledby="range-heading">
                <h2 id="range-heading" className="exports__heading">
                  Which cases go in the file
                </h2>
                <p className="exports__note">
                  The daily file leaves out passes with no fails. The quarterly file keeps every
                  case. Choose the check dates to include - the day each case's review was
                  submitted (column G). Both dates are included. Leave both empty to use every
                  case in this batch.
                </p>
                <FilterBar
                  summary={range ? `Cases ${range}` : 'Every case in this batch'}
                  onClear={() => setParams(new URLSearchParams(), { replace: true })}
                  clearDisabled={!from && !to && !quarterly}
                >
                  <FilterField label="File" htmlFor="file-kind">
                    <select
                      id="file-kind"
                      value={quarterly ? 'quarterly' : ''}
                      onChange={(e) => setRange('file', e.target.value)}
                    >
                      <option value="">Daily file</option>
                      <option value="quarterly">Quarterly 12-month file</option>
                    </select>
                  </FilterField>
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
                    disabled={incomplete}
                    caption={`${records.length} row(s)${range ? `, ${range},` : ''} in the Trail Light column order`}
                    emptyHint={
                      allRecords.length === 0
                        ? 'This batch generated 0 rows, because no cases were at status Closed.'
                        : nothingInFile
                    }
                  />
                </div>
                <p className="exports__summary" role="status">
                  {fileContentsSummary(inRange.length, allRecords.length, from, to, passesLeftOut)}
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
                          {nothingInFile}
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
