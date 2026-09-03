import { useMemo, useState } from 'react';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { useExports, type ExportRecordRow } from './useExports';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { createExportBatch, generateExport } from '../../services/commands/exports';
import { TRAIL_LIGHT_HEADERS, trailLightRow } from './trailLight';
import { buildFullExtract, EXTRACT_ROW_LIMIT } from './fullExtract';
import { downloadWorkbook, stampedFilename } from '../../lib/tabular';
import './ExportsPage.css';

type Notice = { tone: 'ok' | 'error'; message: string } | null;

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString();
}

function inRange(iso: string | null, from: string, to: string): boolean {
  if (!from && !to) return true;
  if (!iso) return false;
  const day = iso.slice(0, 10);
  if (from && day < from) return false;
  if (to && day > to) return false;
  return true;
}

export function ExportsPage() {
  const [reloadKey, setReloadKey] = useState(0);
  const state = useExports(reloadKey);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<Notice>(null);
  const intent = useIntentKeys();

  const [batchStatus, setBatchStatus] = useState('');
  const [batchFrom, setBatchFrom] = useState('');
  const [batchTo, setBatchTo] = useState('');
  const [recordSearch, setRecordSearch] = useState('');
  const [recordGrade, setRecordGrade] = useState('');

  const batches = state.status === 'ready' ? state.batches : [];
  const records = state.status === 'ready' ? state.records : [];

  const batchStatuses = useMemo(
    () => [...new Set(batches.map((b) => b.status).filter(Boolean))].sort(),
    [batches],
  );
  const recordGrades = useMemo(
    () => [...new Set(records.map((r) => r.adviceGrade).filter(Boolean))].sort(),
    [records],
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

  const filteredRecords = useMemo(() => {
    const term = recordSearch.trim().toLowerCase();
    return records.filter(
      (r) =>
        (!recordGrade || r.adviceGrade === recordGrade) &&
        (!term || `${r.adviser} ${r.client} ${r.batchName}`.toLowerCase().includes(term)),
    );
  }, [records, recordSearch, recordGrade]);

  const batchesFiltered = batchStatus !== '' || batchFrom !== '' || batchTo !== '';
  const recordsFiltered = recordSearch.trim() !== '' || recordGrade !== '';

  const recordsByBatch = useMemo(() => {
    const grouped = new Map<string, ExportRecordRow[]>();
    for (const record of records) {
      if (!record.batchId) continue;
      grouped.set(record.batchId, [...(grouped.get(record.batchId) ?? []), record]);
    }
    return grouped;
  }, [records]);

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

      const caveats = [
        extract.unavailable.length > 0
          ? `not readable by you: ${extract.unavailable.join(', ')}`
          : '',
        extract.truncated.length > 0
          ? `capped at ${EXTRACT_ROW_LIMIT} rows: ${extract.truncated.join(', ')}`
          : '',
      ].filter(Boolean);

      setNotice({
        tone: caveats.length > 0 ? 'error' : 'ok',
        message:
          `Downloaded ${extract.sheets.length} sheet(s).` +
          (caveats.length > 0 ? ` Left out or shortened — ${caveats.join('; ')}.` : ''),
      });
    } catch {
      setNotice({ tone: 'error', message: 'The full data extract could not be produced.' });
    } finally {
      setBusy(false);
    }
  }

  async function onCreateBatch() {
    setBusy(true);
    setNotice(null);
    const result = await createExportBatch({ idempotencyKey: intent.keyFor('create-batch') });
    setBusy(false);
    if (result.ok) {
      intent.release('create-batch');
      setNotice({ tone: 'ok', message: 'New draft export batch created.' });
      setReloadKey((k) => k + 1);
    } else {
      setNotice({ tone: 'error', message: result.message });
    }
  }

  async function onGenerate(batchId: string) {
    setBusy(true);
    setNotice(null);
    const result = await generateExport({ batchId, idempotencyKey: intent.keyFor(batchId) });
    setBusy(false);
    if (result.ok) {
      intent.release(batchId);
      setNotice({ tone: 'ok', message: `Generated ${result.data.RowCount} export record(s).` });
      setReloadKey((k) => k + 1);
    } else {
      setNotice({ tone: 'error', message: result.message });
    }
  }

  return (
    <>
      <PageIntro
        title="Exports"
        purpose="Produce the Trail Light export on demand. Each batch snapshots the closed cases into the 20-column contract for reconciliation (AD-039, AD-034 manual only)."
        actions={
          <>
            <button type="button" className="exports__btn exports__btn--ghost" onClick={onFullExtract} disabled={busy}>
              {busy ? 'Working…' : 'Download all data'}
            </button>
            <button type="button" className="exports__btn" onClick={onCreateBatch} disabled={busy}>
              New export batch
            </button>
          </>
        }
      />

      {notice ? (
        <p className={`exports__notice exports__notice--${notice.tone}`} role="status">
          {notice.message}
        </p>
      ) : null}

      {state.status === 'loading' ? <p role="status">Loading exports…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="exports__unavailable" aria-labelledby="exports-unavailable">
          <h2 id="exports-unavailable">Exports cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <section aria-labelledby="batches-heading">
            <h2 id="batches-heading" className="exports__heading">
              Export batches
            </h2>
            {batches.length === 0 ? (
              <p>No export batches yet. Create one to snapshot the closed cases.</p>
            ) : (
              <>
                <FilterBar
                  summary={`${filteredBatches.length} of ${batches.length} batches`}
                  onClear={() => {
                    setBatchStatus('');
                    setBatchFrom('');
                    setBatchTo('');
                  }}
                  clearDisabled={!batchesFiltered}
                >
                  <FilterField label="Status" htmlFor="batch-status">
                    <select
                      id="batch-status"
                      value={batchStatus}
                      onChange={(e) => setBatchStatus(e.target.value)}
                    >
                      <option value="">All statuses</option>
                      {batchStatuses.map((s) => (
                        <option key={s} value={s}>
                          {s}
                        </option>
                      ))}
                    </select>
                  </FilterField>
                  <FilterField label="Generated from" htmlFor="batch-from">
                    <input
                      id="batch-from"
                      type="date"
                      value={batchFrom}
                      onChange={(e) => setBatchFrom(e.target.value)}
                    />
                  </FilterField>
                  <FilterField label="Generated to" htmlFor="batch-to">
                    <input
                      id="batch-to"
                      type="date"
                      value={batchTo}
                      onChange={(e) => setBatchTo(e.target.value)}
                    />
                  </FilterField>
                </FilterBar>
                <table className="exports__table">
                  <thead>
                    <tr>
                      <th scope="col">Batch</th>
                      <th scope="col">Status</th>
                      <th scope="col">Generated</th>
                      <th scope="col">Rows</th>
                      <th scope="col">Action</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredBatches.length === 0 ? (
                      <tr>
                        <td colSpan={5} className="exports__empty">
                          No batches match your current filters.
                        </td>
                      </tr>
                    ) : (
                      filteredBatches.map((b) => {
                        const batchRecords = recordsByBatch.get(b.id) ?? [];
                        return (
                          <tr key={b.id}>
                            <td>{b.name}</td>
                            <td>{b.status}</td>
                            <td>{formatDate(b.generatedOn)}</td>
                            <td>{b.rowCount}</td>
                            <td className="exports__actions">
                              <button
                                type="button"
                                className="exports__btn exports__btn--ghost"
                                onClick={() => onGenerate(b.id)}
                                disabled={busy}
                              >
                                Generate
                              </button>
                              <ExportMenu
                                label="Download"
                                stem={`trail-light-${b.code || b.name || 'batch'}`}
                                sheetName="Trail Light"
                                headers={TRAIL_LIGHT_HEADERS}
                                rows={batchRecords.map((record) => trailLightRow(record.record))}
                                caption={`${batchRecords.length} row(s) in the AD-039 20-column order`}
                                disabled={busy}
                              />
                            </td>
                          </tr>
                        );
                      })
                    )}
                  </tbody>
                </table>
              </>
            )}
          </section>

          <section aria-labelledby="records-heading">
            <div className="exports__section-head">
              <h2 id="records-heading" className="exports__heading">
                Export records
              </h2>
              {filteredRecords.length > 0 ? (
                <ExportMenu
                  label="Download filtered rows"
                  stem="trail-light-filtered"
                  sheetName="Trail Light"
                  headers={TRAIL_LIGHT_HEADERS}
                  rows={filteredRecords.map((record) => trailLightRow(record.record))}
                  caption={`${filteredRecords.length} row(s) in the AD-039 20-column order`}
                />
              ) : null}
            </div>
            {state.records.length === 0 ? (
              <p>No export records yet. Generate a batch to populate them.</p>
            ) : (
              <>
                <FilterBar
                  summary={`${filteredRecords.length} of ${records.length} records`}
                  onClear={() => {
                    setRecordSearch('');
                    setRecordGrade('');
                  }}
                  clearDisabled={!recordsFiltered}
                >
                  <FilterField label="Search" htmlFor="record-search">
                    <input
                      id="record-search"
                      type="search"
                      value={recordSearch}
                      onChange={(e) => setRecordSearch(e.target.value)}
                      placeholder="Adviser, client or batch"
                    />
                  </FilterField>
                  <FilterField label="Advice quality grade" htmlFor="record-grade">
                    <select
                      id="record-grade"
                      value={recordGrade}
                      onChange={(e) => setRecordGrade(e.target.value)}
                    >
                      <option value="">All grades</option>
                      {recordGrades.map((g) => (
                        <option key={g} value={g}>
                          {g}
                        </option>
                      ))}
                    </select>
                  </FilterField>
                </FilterBar>
                <table className="exports__table">
                  <thead>
                    <tr>
                      <th scope="col">Batch</th>
                      <th scope="col">Adviser</th>
                      <th scope="col">Client</th>
                      <th scope="col">Advice quality grade</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredRecords.length === 0 ? (
                      <tr>
                        <td colSpan={4} className="exports__empty">
                          No records match your current filters.
                        </td>
                      </tr>
                    ) : (
                      filteredRecords.map((r) => (
                        <tr key={r.id}>
                          <td>{r.batchName}</td>
                          <td>{r.adviser}</td>
                          <td>{r.client}</td>
                          <td>{r.adviceGrade}</td>
                        </tr>
                      ))
                    )}
                  </tbody>
                </table>
              </>
            )}
          </section>
        </>
      ) : null}
    </>
  );
}
