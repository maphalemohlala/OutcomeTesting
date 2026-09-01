import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { Notice } from '../../components/feedback/Notice';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import {
  useRemediation,
  type OutcomeRow,
  type RemediationActionRow,
  type SignoffRow,
} from './useRemediation';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { completeRemediation } from '../../services/commands/completeRemediation';
import { messageForFailure } from '../../services/errors';
import './RemediationPage.css';

interface NoticeState {
  tone: 'success' | 'error';
  message: string;
}

function canComplete(status: string): boolean {
  return status === 'Open' || status === 'In progress';
}

function ActionsTable({
  actions,
  busyId,
  onComplete,
}: {
  actions: RemediationActionRow[];
  busyId: string | null;
  onComplete: (action: RemediationActionRow) => void;
}) {
  if (actions.length === 0) {
    return (
      <p className="remediation__note">No remediation action has been raised for this case yet.</p>
    );
  }
  return (
    <table className="remediation__table">
      <thead>
        <tr>
          <th scope="col">Action</th>
          <th scope="col">Description</th>
          <th scope="col">Status</th>
          <th scope="col">Triggered by</th>
          <th scope="col">Owner</th>
          <th scope="col">Due</th>
          <th scope="col">Completed</th>
          <th scope="col">
            <span className="remediation__sr-only">Complete</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {actions.map((action) => (
          <tr key={action.id}>
            <th scope="row">{action.reference}</th>
            <td>{action.description}</td>
            <td>{action.status}</td>
            <td>{action.triggeredBy ?? '—'}</td>
            <td>{action.owner ?? 'Unassigned'}</td>
            <td>{action.dueOn ?? '—'}</td>
            <td>{action.completedOn ?? '—'}</td>
            <td>
              {canComplete(action.status) ? (
                <button
                  type="button"
                  className="remediation__action-btn"
                  disabled={busyId !== null}
                  onClick={() => onComplete(action)}
                >
                  {busyId === action.id ? 'Completing…' : 'Mark complete'}
                </button>
              ) : (
                '—'
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function OutcomesTable({ outcomes }: { outcomes: OutcomeRow[] }) {
  if (outcomes.length === 0) {
    return (
      <p className="remediation__note">No graded outcome has been recorded for this case yet.</p>
    );
  }
  return (
    <table className="remediation__table">
      <thead>
        <tr>
          <th scope="col">Outcome</th>
          <th scope="col">Check</th>
          <th scope="col">Initial</th>
          <th scope="col">Final</th>
          <th scope="col">Regrade reason</th>
          <th scope="col">Regraded</th>
          <th scope="col">Finalised</th>
        </tr>
      </thead>
      <tbody>
        {outcomes.map((outcome) => (
          <tr key={outcome.id}>
            <th scope="row">{outcome.reference}</th>
            <td>{outcome.reviewInstance ?? '—'}</td>
            <td>{outcome.initialOutcome}</td>
            <td data-empty={outcome.finalOutcome === null ? 'true' : undefined}>
              {outcome.finalOutcome ?? 'Not regraded'}
            </td>
            <td>{outcome.regradeReason ?? '—'}</td>
            <td>{outcome.regradedOn ?? '—'}</td>
            <td>{outcome.finalisedOn ?? '—'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function SignoffsTable({ signoffs }: { signoffs: SignoffRow[] }) {
  if (signoffs.length === 0) {
    return (
      <p className="remediation__note">No sign-off has been recorded for this case yet.</p>
    );
  }
  return (
    <table className="remediation__table">
      <thead>
        <tr>
          <th scope="col">Sign-off</th>
          <th scope="col">Decision</th>
          <th scope="col">Action</th>
          <th scope="col">Notes</th>
          <th scope="col">Signed off by</th>
          <th scope="col">Signed off</th>
        </tr>
      </thead>
      <tbody>
        {signoffs.map((signoff) => (
          <tr key={signoff.id}>
            <th scope="row">{signoff.reference}</th>
            <td>{signoff.decision}</td>
            <td>{signoff.remediationAction ?? '—'}</td>
            <td>{signoff.notes ?? '—'}</td>
            <td>{signoff.signedOffBy ?? '—'}</td>
            <td>{signoff.signedOffOn ?? '—'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

export function RemediationPage() {
  const { caseId } = useParams<{ caseId: string }>();
  const [reloadKey, setReloadKey] = useState(0);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [notice, setNotice] = useState<NoticeState | null>(null);
  const [actionStatus, setActionStatus] = useState('');
  const state = useRemediation(caseId, reloadKey);
  const intent = useIntentKeys();

  const allActions = state.status === 'ready' ? state.actions : [];
  const actionStatuses = useMemo(
    () => [...new Set(allActions.map((a) => a.status).filter(Boolean))].sort(),
    [allActions],
  );
  const filteredActions = useMemo(
    () => (actionStatus ? allActions.filter((a) => a.status === actionStatus) : allActions),
    [allActions, actionStatus],
  );

  const handleComplete = (action: RemediationActionRow) => {
    if (busyId !== null) return;
    setBusyId(action.id);
    setNotice(null);
    completeRemediation({
      actionId: action.id,
      expectedRowVersion: action.rowVersion,
      idempotencyKey: intent.keyFor(action.id),
    })
      .then((result) => {
        setBusyId(null);
        if (result.ok) {
          intent.release(action.id);
          setNotice({ tone: 'success', message: `${action.reference} marked complete.` });
          setReloadKey((key) => key + 1);
        } else {
          setNotice({ tone: 'error', message: messageForFailure(result) });
        }
      })
      .catch(() => {
        setBusyId(null);
        setNotice({
          tone: 'error',
          message: 'Something went wrong while processing your request. Please try again later.',
        });
      });
  };

  return (
    <>
      <p className="remediation__back">
        <Link to={caseId ? `/cases/${caseId}` : '/cases'}>← Back to case</Link>
      </p>

      {state.status === 'loading' ? <p role="status">Loading remediation…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="remediation__unavailable" aria-labelledby="remediation-unavailable">
          <h2 id="remediation-unavailable">Remediation is not available</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <PageIntro
            title="Remediation and sign-off"
            purpose="Track the actions raised for a non-pass outcome (BR-006), the preserved initial and final outcomes (BR-007) and the T&C Manager sign-off (BR-008, FR-023). Completing, regrading and signing off are permissioned write paths handled server-side (AD-031)."
          />

          <section className="remediation__section" aria-labelledby="remediation-actions">
            <h2 id="remediation-actions">Remediation actions</h2>
            {notice ? <Notice tone={notice.tone}>{notice.message}</Notice> : null}
            {allActions.length > 0 ? (
              <FilterBar
                summary={`${filteredActions.length} of ${allActions.length} actions`}
                onClear={() => setActionStatus('')}
                clearDisabled={actionStatus === ''}
              >
                <FilterField label="Status" htmlFor="remediation-status">
                  <select
                    id="remediation-status"
                    value={actionStatus}
                    onChange={(e) => setActionStatus(e.target.value)}
                  >
                    <option value="">All statuses</option>
                    {actionStatuses.map((s) => (
                      <option key={s} value={s}>
                        {s}
                      </option>
                    ))}
                  </select>
                </FilterField>
              </FilterBar>
            ) : null}
            <ActionsTable actions={filteredActions} busyId={busyId} onComplete={handleComplete} />
          </section>

          <section className="remediation__section" aria-labelledby="remediation-outcomes">
            <h2 id="remediation-outcomes">Outcomes</h2>
            <OutcomesTable outcomes={state.outcomes} />
          </section>

          <section className="remediation__section" aria-labelledby="remediation-signoffs">
            <h2 id="remediation-signoffs">Sign-off</h2>
            <SignoffsTable signoffs={state.signoffs} />
          </section>
        </>
      ) : null}
    </>
  );
}
