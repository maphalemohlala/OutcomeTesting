import { useMemo } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { useExportBatch, useExportRecords } from './useExports';
import {
  TRAIL_LIGHT_HEADERS,
  adviceGradeFor,
  describeCheckRange,
  recentQuarters,
  fileContentsSummary,
  belongsInDailyFile,
  closedWhenSnapshotted,
  dailyFileDay,
  dailyFileMayBeShort,
  fileRange,
  previousBatchGeneratedOn,
  trailLightFilename,
  trailLightRow,
  ukDay,
  withinCheckDates,
} from './trailLight';
import './ExportsPage.css';
import { dateTime } from '../../lib/format';
import { ukToday } from '../cases/caseHeaderDates';
import { QuarterSelect } from './QuarterSelect';

/**
 * One export batch: its cases, and the Trail Light file cut to a check-date range
 * (2026-09-29). The range used to sit above the batch list, applied to every download on
 * the page, and users could not tell what it did. Here it sits next to the rows it
 * decides, the table shows only the cases the file will hold, and one sentence says so.
 *
 * The range and the file choice are kept in the URL (`?from=&to=&file=`) so a cut batch can
 * be reopened or shared. The daily file is the default, cut (unless dates are chosen) to the days
 * from the previous batch's generation day to the day before this one's; the quarterly file
 * keeps every case that was closed.
 */
export function ExportBatchPage() {
  const { batchId = '' } = useParams();
  const [params, setParams] = useSearchParams();
  const from = params.get('from') ?? '';
  const to = params.get('to') ?? '';
  const quarterly = params.get('file') === 'quarterly';
  const file = quarterly ? 'quarterly' : 'daily';
  const quarters = useMemo(() => recentQuarters(ukToday()), []);
  const batchState = useExportBatch(batchId);
  const recordState = useExportRecords(batchId);
  const state = batchState.status !== 'ready' ? batchState : recordState;

  const batch = batchState.status === 'ready' ? batchState.batches.find((b) => b.id === batchId) : undefined;
  const allRecords = useMemo(
    () => (recordState.status === 'ready' ? recordState.records : []),
    [recordState],
  );
  const previousGeneratedOn =
    batchState.status === 'ready' ? previousBatchGeneratedOn(batchState.batches, batchId) : null;
  const range = fileRange(file, from, to, batch?.generatedOn ?? null, previousGeneratedOn);
  // No hand-written useMemo on these three: `range` is handed to helpers below, so the compiler
  // cannot prove it unchanged and refuses a manual memo that depends on it; it memoises them itself.
  const inRange = allRecords.filter((r) => withinCheckDates(r.record, range.from, range.to));

  function setRange(key: 'from' | 'to' | 'file', value: string) {
    setRanges({ [key]: value });
  }

  function setRanges(values: Partial<Record<'from' | 'to' | 'file', string>>) {
    const next = new URLSearchParams(params);
    // Moving one end of the daily file's default day keeps the other where it was shown.
    if (!quarterly && !from && !to && range.from && !('file' in values)) {
      next.set('from', range.from);
      next.set('to', range.to);
    }
    for (const [key, value] of Object.entries(values)) {
      if (value) next.set(key, value);
      else next.delete(key);
    }
    setParams(next, { replace: true });
  }

  const records = inRange.filter((r) => (quarterly ? closedWhenSnapshotted(r.record) : belongsInDailyFile(r.record)));
  const exportRows = records.map((r) => trailLightRow(r.record, file));
  const leftOut = inRange.length - records.length;
  const nothingInFile =
    allRecords.length === 0
      ? 'This batch holds no cases.'
      : inRange.length === 0
        ? 'No cases in this batch fall in the chosen range.'
        : quarterly
          ? 'No case in range was closed when this batch was generated.'
          : 'No case in range belongs in the daily file. Choose the quarterly file to include them.';
  const rangeWords = describeCheckRange(range.from, range.to);
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
            purpose={`${batch.status} · generated ${dateTime(batch.generatedOn)} · ${batch.rowCount} case(s) snapshotted`}
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
                  The daily file holds every case with a type of failure: a File Quality Fail, an
                  Advice Quality grade first recorded as Pass with issues, Insufficient evidence
                  or Potential harm, or a Tax-only check that came back Fail or Pass with issues.
                  It reports the grade first recorded, and holds each case once: on the day it was
                  checked, whether or not it has closed since. Unless you choose dates, it holds
                  the cases checked from the day the previous batch was generated up to the day
                  before this one, so each day's checks reach one daily file. The quarterly file holds
                  every closed pass and every closed fail, and reports the grade in force after
                  any regrade. Choose a quarter, or the check dates to include - the day each
                  case's review was submitted (column G). Both dates are included. Leave both
                  empty for every closed case in this batch.
                </p>
                <FilterBar
                  summary={rangeWords ? `Cases ${rangeWords}` : 'Every case in this batch'}
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
                  <FilterField label="Quarter" htmlFor="check-quarter">
                    <QuarterSelect
                      quarters={quarters}
                      from={from}
                      to={to}
                      onChoose={setRanges}
                      anyLabel={quarterly ? undefined : 'Since the previous batch'}
                    />
                  </FilterField>
                  <FilterField label="Checked from" htmlFor="check-from">
                    <input
                      id="check-from"
                      type="date"
                      value={range.from}
                      max={range.to || undefined}
                      onChange={(e) => setRange('from', e.target.value)}
                    />
                  </FilterField>
                  <FilterField label="Checked to" htmlFor="check-to">
                    <input
                      id="check-to"
                      type="date"
                      value={range.to}
                      min={range.from || undefined}
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
                    filenameFor={(format) => trailLightFilename(format, undefined, dailyFileDay(file, range))}
                    sheetName="Trail Light"
                    headers={TRAIL_LIGHT_HEADERS}
                    rows={exportRows}
                    disabled={incomplete}
                    caption={`${records.length} row(s)${rangeWords ? `, ${rangeWords},` : ''} in the Trail Light column order`}
                    emptyHint={
                      allRecords.length === 0
                        ? 'This batch generated 0 rows, because no case had been checked.'
                        : nothingInFile
                    }
                  />
                </div>
                {dailyFileMayBeShort(file, range, batch.generatedOn) ? (
                  <p className="exports__notice" role="status">
                    This batch was generated at {dateTime(batch.generatedOn)}, so a case checked later
                    that day is not in it. Generate a new batch once the day is over to include those
                    cases.
                  </p>
                ) : null}
                <p className="exports__summary" role="status">
                  {fileContentsSummary(inRange.length, allRecords.length, range.from, range.to, leftOut, file)}
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
                          <td>{adviceGradeFor(r.record, file)}</td>
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
