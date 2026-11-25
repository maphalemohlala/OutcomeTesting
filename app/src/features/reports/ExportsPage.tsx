import { useMemo, useState, type FormEvent } from 'react';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { useExportBatches } from './useExports';
import { exportTrailLight } from './exportTrailLight';
import { useIntentKeys } from '../../hooks/useIntentKey';
import type { TrailLightFileKind } from '../../services/commands/exports';
import { ukDayOf, ukToday } from '../cases/caseHeaderDates';
import { buildFullExtract } from './fullExtract';
import { downloadWorkbook, stampedFilename } from '../../lib/tabular';
import { dayBefore, recentQuarters } from './trailLight';
import { QuarterSelect } from './QuarterSelect';
import './ExportsPage.css';
import { dateTime } from '../../lib/format';
import { Skeleton } from '../../components/feedback/Skeleton';
import { Pager } from '../../components/navigation/Pager';
import { PAGE_SIZE, usePaged } from '../../components/navigation/paging';

type Notice = { tone: 'ok' | 'error'; message: string } | null;

function inRange(iso: string | null, from: string, to: string): boolean {
  if (!from && !to) return true;
  if (!iso) return false;
  const day = ukDayOf(iso);
  if (!day) return false;
  if (from && day < from) return false;
  if (to && day > to) return false;
  return true;
}

/**
 * The Trail Light export in one action (2026-11-25): choose the file and the check dates, and
 * Export produces the batch on the server and downloads it. The daily file starts on yesterday,
 * the last whole UK day; the quarterly file starts on every closed case.
 */
export function ExportsPage() {
  const [reloadKey, setReloadKey] = useState(0);
  const state = useExportBatches(reloadKey);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<Notice>(null);
  const intent = useIntentKeys();

  const yesterday = useMemo(() => dayBefore(ukToday()), []);
  const quarters = useMemo(() => recentQuarters(ukToday()), []);
  const [file, setFile] = useState<TrailLightFileKind>('Daily');
  const [from, setFrom] = useState(yesterday);
  const [to, setTo] = useState(yesterday);
  const [format, setFormat] = useState<'xlsx' | 'csv'>('xlsx');

  const [batchStatus, setBatchStatus] = useState('');
  const [batchFrom, setBatchFrom] = useState('');
  const [batchTo, setBatchTo] = useState('');
  const batches = useMemo(() => (state.status === 'ready' ? state.batches : []), [state]);

  const batchStatuses = useMemo(
    () => [...new Set(batches.map((b) => b.status).filter(Boolean))].sort(),
    [batches],
  );
  const filteredBatches = useMemo(
    () =>
      batches.filter(
        (b) =>
          (!batchStatus || b.status === batchStatus) &&
          inRange(b.generatedOn, batchFrom, batchTo),
      ),
    [batches, batchStatus, batchFrom, batchTo],
  );

  const batchesFiltered = batchStatus !== '' || batchFrom !== '' || batchTo !== '';
  const paged = usePaged(filteredBatches, PAGE_SIZE, `${batchStatus}|${batchFrom}|${batchTo}`);

  function chooseFile(next: TrailLightFileKind) {
    setFile(next);
    setFrom(next === 'Daily' ? yesterday : '');
    setTo(next === 'Daily' ? yesterday : '');
  }

  async function onFullExtract() {
    setBusy(true);
    setNotice(null);
    try {
      const extract = await buildFullExtract();
      if (extract.sheets.length === 0) {
        setNotice({ tone: 'error', message: 'No data in this system is readable by you.' });
        return;
      }
      downloadWorkbook(stampedFilename('outcome-testing-full-extract', 'xlsx'), extract.sheets);

      const missing = extract.unavailable.join(', ');
      setNotice({
        tone: missing ? 'error' : 'ok',
        message: `Downloaded ${extract.sheets.length} sheet(s).` + (missing ? ` Left out, not readable by you: ${missing}.` : ''),
      });
    } catch {
      setNotice({ tone: 'error', message: 'The full data extract could not be produced.' });
    } finally {
      setBusy(false);
    }
  }

  async function onExport(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setNotice(null);
    // One intent per choice: a retry of the same choice replays the batch already made rather
    // than making a second, and the key doubles as the new batch's id.
    const token = `trail-light|${file}|${from}|${to}`;
    const key = intent.keyFor(token);
    try {
      const outcome = await exportTrailLight({ file, from, to, format, key });
      if (outcome.produced) setReloadKey((k) => k + 1);
      if (outcome.settled) intent.release(token);
      setNotice({ tone: outcome.tone, message: outcome.message });
    } catch {
      setNotice({ tone: 'error', message: 'The Trail Light file could not be produced. Select Export again to retry.' });
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <PageIntro
        title="Exports"
        purpose="Produce the Trail Light file: choose the file and the check dates, then export. Each export is kept below as a snapshot of what was sent, for reconciliation."
        actions={
          <button type="button" className="exports__btn exports__btn--ghost" onClick={onFullExtract} disabled={busy}>
            {busy ? 'Working…' : 'Download all data'}
          </button>
        }
      />

      {notice ? (
        <p className={`exports__notice exports__notice--${notice.tone}`} role="status">
          {notice.message}
        </p>
      ) : null}

      <section aria-labelledby="export-heading">
        <h2 id="export-heading" className="exports__heading">
          Trail Light file
        </h2>
        <p className="exports__note">
          The daily file holds every case with a type of failure: a File Quality Fail, an Advice
          Quality grade first recorded as Pass with issues, Insufficient evidence or Potential harm,
          or a Tax-only check that came back Fail or Pass with issues. It reports the grade first
          recorded. The quarterly file holds every closed case and reports the grade in force after
          any regrade. The check date is the day each case's review was submitted; both dates are
          included, and an empty date sets no limit.
        </p>
        <form className="filter-bar" aria-label="Trail Light file" onSubmit={onExport}>
          <div className="filter-bar__controls">
            <FilterField label="File" htmlFor="file-kind">
              <select id="file-kind" value={file} onChange={(e) => chooseFile(e.target.value as TrailLightFileKind)}>
                <option value="Daily">Daily file</option>
                <option value="Quarterly">Quarterly 12-month file</option>
              </select>
            </FilterField>
            <FilterField label="Quarter" htmlFor="check-quarter">
              <QuarterSelect
                quarters={quarters}
                from={from}
                to={to}
                onChoose={(range) => {
                  setFrom(range.from);
                  setTo(range.to);
                }}
                anyLabel={file === 'Daily' ? 'Any (every check date)' : 'Any (every closed case)'}
              />
            </FilterField>
            <FilterField label="Checked from" htmlFor="check-from">
              <input
                id="check-from"
                type="date"
                value={from}
                max={to || undefined}
                onChange={(e) => setFrom(e.target.value)}
              />
            </FilterField>
            <FilterField label="Checked to" htmlFor="check-to">
              <input
                id="check-to"
                type="date"
                value={to}
                min={from || undefined}
                onChange={(e) => setTo(e.target.value)}
              />
            </FilterField>
            <FilterField label="Format" htmlFor="file-format">
              <select id="file-format" value={format} onChange={(e) => setFormat(e.target.value as 'xlsx' | 'csv')}>
                <option value="xlsx">Excel workbook (.xlsx)</option>
                <option value="csv">Comma-separated values (.csv)</option>
              </select>
            </FilterField>
          </div>
          <button type="submit" className="exports__btn" disabled={busy}>
            {busy ? 'Working…' : 'Export'}
          </button>
        </form>
      </section>

      {state.status === 'loading' ? <Skeleton label="Loading exports…" rows={6} /> : null}

      {state.status === 'unavailable' ? (
        <section className="exports__unavailable" aria-labelledby="exports-unavailable">
          <h2 id="exports-unavailable">Previous exports cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <section aria-labelledby="batches-heading">
          <h2 id="batches-heading" className="exports__heading">
            Previous exports
          </h2>
          {batches.length === 0 ? (
            <p>No exports yet.</p>
          ) : (
            <>
              <FilterBar
                summary={`${filteredBatches.length} of ${batches.length} exports`}
                onClear={() => {
                  setBatchStatus('');
                  setBatchFrom('');
                  setBatchTo('');
                }}
                clearDisabled={!batchesFiltered}
              >
                <FilterField label="Status" htmlFor="batch-status">
                  <select id="batch-status" value={batchStatus} onChange={(e) => setBatchStatus(e.target.value)}>
                    <option value="">All statuses</option>
                    {batchStatuses.map((s) => (
                      <option key={s} value={s}>
                        {s}
                      </option>
                    ))}
                  </select>
                </FilterField>
                <FilterField label="Generated from" htmlFor="batch-from">
                  <input id="batch-from" type="date" value={batchFrom} onChange={(e) => setBatchFrom(e.target.value)} />
                </FilterField>
                <FilterField label="Generated to" htmlFor="batch-to">
                  <input id="batch-to" type="date" value={batchTo} onChange={(e) => setBatchTo(e.target.value)} />
                </FilterField>
              </FilterBar>
              <table className="exports__table">
                <thead>
                  <tr>
                    <th scope="col">Export</th>
                    <th scope="col">Status</th>
                    <th scope="col">Generated</th>
                    <th scope="col">Rows</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredBatches.length === 0 ? (
                    <tr>
                      <td colSpan={4} className="exports__empty">
                        No exports match your current filters.
                      </td>
                    </tr>
                  ) : (
                    paged.rows.map((b) => (
                      <tr key={b.id}>
                        <td>{b.name || b.code || 'Export batch'}</td>
                        <td>{b.status}</td>
                        <td>{dateTime(b.generatedOn)}</td>
                        <td>{b.rowCount}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
              <Pager {...paged} label="exports" />
            </>
          )}
        </section>
      ) : null}
    </>
  );
}
