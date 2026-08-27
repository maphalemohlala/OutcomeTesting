import { useRef } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { useCaseIntake } from './useCaseIntake';
import { useCaseUpload } from './useCaseUpload';
import { downloadTemplate, downloadValidationReport } from './caseUpload';
import './CaseIntakePage.css';

export function CaseIntakePage() {
  const { state, reload } = useCaseIntake();
  const upload = useCaseUpload(reload);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const busy = upload.state.phase === 'processing';

  function onChooseFile(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) void upload.upload(file);
  }

  return (
    <>
      <PageIntro
        title="Case intake"
        purpose="Track uploaded Intelligent Office extracts and resolve the rows that failed validation (FR-001 to FR-003)."
        actions={
          <div className="intake__actions">
            <button
              type="button"
              className="intake__btn intake__btn--ghost"
              onClick={downloadTemplate}
            >
              Download template
            </button>
            <button
              type="button"
              className="intake__btn"
              disabled={busy}
              onClick={() => fileInputRef.current?.click()}
            >
              {busy ? 'Uploading…' : 'Upload cases'}
            </button>
            <input
              ref={fileInputRef}
              type="file"
              accept=".csv,text/csv"
              className="visually-hidden"
              onChange={onChooseFile}
            />
          </div>
        }
      />

      {upload.state.phase === 'processing' ? (
        <p className="intake__notice" role="status">
          {upload.state.message}
        </p>
      ) : null}

      {upload.state.phase === 'error' ? (
        <div className="intake__notice intake__notice--error" role="alert">
          <p>{upload.state.message}</p>
          <button type="button" className="intake__btn intake__btn--ghost" onClick={upload.reset}>
            Dismiss
          </button>
        </div>
      ) : null}

      {upload.state.phase === 'done' ? (
        <div className="intake__notice intake__notice--success" role="status">
          <p>
            Batch {upload.state.result.batchReference}: {upload.state.result.imported} of{' '}
            {upload.state.result.total} case
            {upload.state.result.total === 1 ? '' : 's'} imported
            {upload.state.result.duplicates > 0
              ? `, ${upload.state.result.duplicates} already existed`
              : ''}
            {upload.state.result.failed > 0
              ? `, ${upload.state.result.failed} failed`
              : ''}
            . Rows needing attention appear under Exceptions to resolve.
          </p>
          <div className="intake__actions">
            {upload.state.result.imported > 0 ? (
              <Link className="intake__btn" to="/cases">
                View imported cases
              </Link>
            ) : null}
            {upload.state.result.report.length > 0 ? (
              <button
                type="button"
                className="intake__btn intake__btn--ghost"
                onClick={() =>
                  upload.state.phase === 'done' &&
                  downloadValidationReport(
                    upload.state.result.report,
                    upload.state.result.batchReference,
                  )
                }
              >
                Download validation report
              </button>
            ) : null}
            <button type="button" className="intake__btn intake__btn--ghost" onClick={upload.reset}>
              Dismiss
            </button>
          </div>
        </div>
      ) : null}

      {state.status === 'loading' ? <p role="status">Loading intake batches…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="intake__unavailable" aria-labelledby="intake-unavailable">
          <h2 id="intake-unavailable">No intake batches can be listed</h2>
          <p>{state.reason}</p>
          <p>Nothing has been lost. Batches appear here once an extract has been uploaded.</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <section className="intake__section" aria-labelledby="intake-batches">
            <h2 id="intake-batches" className="intake__heading">
              Import batches
            </h2>
            <div className="intake__scroll">
              <table className="intake">
                <caption className="visually-hidden">Uploaded extracts, newest first</caption>
                <thead>
                  <tr>
                    <th scope="col">Batch</th>
                    <th scope="col">Source</th>
                    <th scope="col">Imported on</th>
                    <th scope="col">Status</th>
                    <th scope="col" className="intake__numeric">
                      Rows
                    </th>
                    <th scope="col" className="intake__numeric">
                      Imported
                    </th>
                    <th scope="col" className="intake__numeric">
                      Exceptions
                    </th>
                    <th scope="col">Owner</th>
                  </tr>
                </thead>
                <tbody>
                  {state.batches.length === 0 ? (
                    <tr>
                      <td colSpan={8} className="intake__empty">
                        No extracts have been uploaded yet.
                      </td>
                    </tr>
                  ) : (
                    state.batches.map((batch) => (
                      <tr key={batch.id}>
                        <th scope="row">{batch.name}</th>
                        <td>{batch.source}</td>
                        <td>{batch.importedOn ?? 'Not recorded'}</td>
                        <td>{batch.status}</td>
                        <td className="intake__numeric">{batch.totalRows ?? '—'}</td>
                        <td className="intake__numeric">{batch.importedCount ?? '—'}</td>
                        <td className="intake__numeric">{batch.exceptionCount ?? '—'}</td>
                        <td>{batch.owner ?? 'Unassigned'}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </section>

          <section className="intake__section" aria-labelledby="intake-exceptions">
            <h2 id="intake-exceptions" className="intake__heading">
              Exceptions to resolve
            </h2>
            <div className="intake__scroll">
              <table className="intake">
                <caption className="visually-hidden">
                  Rows that failed validation, by row order
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Batch</th>
                    <th scope="col" className="intake__numeric">
                      Row
                    </th>
                    <th scope="col">Case reference</th>
                    <th scope="col">Reason</th>
                    <th scope="col">Status</th>
                    <th scope="col">Resolved on</th>
                  </tr>
                </thead>
                <tbody>
                  {state.exceptions.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="intake__empty">
                        No rows have failed validation.
                      </td>
                    </tr>
                  ) : (
                    state.exceptions.map((exception) => (
                      <tr key={exception.id}>
                        <td>{exception.batch ?? 'Unknown batch'}</td>
                        <td className="intake__numeric">{exception.rowNumber ?? '—'}</td>
                        <td>{exception.caseReference ?? 'Missing'}</td>
                        <td className="intake__reason">{exception.reason}</td>
                        <td>{exception.status}</td>
                        <td>{exception.resolvedOn ?? 'Open'}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </section>
        </>
      ) : null}
    </>
  );
}
