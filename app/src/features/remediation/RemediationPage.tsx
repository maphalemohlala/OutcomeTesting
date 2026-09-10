import { Fragment, useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { Notice } from '../../components/feedback/Notice';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import {
  useRemediation,
  type RemediationCase,
  type OutcomeRow,
  type RemediationActionRow,
  type SignoffRow,
} from './useRemediation';
import { ACTION_COLUMNS, groupIssues, outcomeOf } from './remediationIssues';
import { remediationForm, signoffCell } from './remediationForm';
import { remediationClock } from '../../lib/workingDays';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { completeRemediation } from '../../services/commands/completeRemediation';
import { messageForFailure } from '../../services/errors';
import { classify } from '../../services/commands/failures';
import './RemediationPage.css';

interface NoticeState {
  tone: 'success' | 'error';
  message: string;
}

function canComplete(status: string): boolean {
  return status === 'Open' || status === 'In progress';
}

/** The BR-010 age, in the words the portal uses for it. */
function ageOf(action: RemediationActionRow): string {
  if (!action.createdOn) return '—';
  const { current } = remediationClock({
    createdon: action.createdOn,
    al_clockstartedon: action.clockStartedOn ?? undefined,
    al_completedon: action.completedOnRaw ?? undefined,
  });
  return current === 1 ? '1 working day' : `${current} working days`;
}

/**
 * The remediation's details, above the actions.
 *
 * The same four the portal names: the client and adviser the case carries, the outcome the
 * remediation was raised for, and a way back to the case record. The outcome sits here
 * rather than in the table because it is the result every issue in the table is a reason
 * for, so numbering it among them read as one more thing the adviser had to put right
 * (project owner, 2026-09-10).
 *
 * Exported for remediationRender.test.tsx, as ActionsTable is.
 */
export function RemediationDetails({
  outcomeCase,
  outcome,
  caseId,
}: {
  outcomeCase: RemediationCase | null;
  outcome: string | null;
  caseId: string | undefined;
}) {
  if (outcomeCase === null && outcome === null) {
    return null;
  }

  return (
    <dl className="remediation__details">
      <div className="remediation__detail">
        <dt>Client</dt>
        <dd>{outcomeCase?.clientName ?? '—'}</dd>
      </div>
      <div className="remediation__detail">
        <dt>Adviser</dt>
        <dd>{outcomeCase?.adviserName ?? '—'}</dd>
      </div>
      <div className="remediation__detail">
        <dt>Outcome</dt>
        <dd>{outcome ?? '—'}</dd>
      </div>
      <div className="remediation__detail">
        <dt>Case</dt>
        <dd>{caseId ? <Link to={`/cases/${caseId}`}>Open the full case record</Link> : '—'}</dd>
      </div>
    </dl>
  );
}

/** Exported for remediationRender.test.tsx, which reads the drawn rows back out. */
export function ActionsTable({
  actions,
  signoffs,
  adviserName,
  busyId,
  onComplete,
}: {
  actions: RemediationActionRow[];
  signoffs: SignoffRow[];
  adviserName: string | null;
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
      <caption className="remediation__caption">Remediation and escalation</caption>
      <thead>
        <tr>
          <th scope="col">No.</th>
          <th scope="col">Issue / fail reason</th>
          <th scope="col">Remedial action</th>
          <th scope="col">Owner</th>
          <th scope="col">Target date</th>
          <th scope="col">Status</th>
          <th scope="col">Age</th>
          <th scope="col">Sign-off</th>
          <th scope="col">
            <span className="remediation__sr-only">Complete</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {groupIssues(actions).map(({ action, lines, note }) => (
          <Fragment key={action.id}>
            {lines.map(({ number, issue }, index) => (
              <tr key={number}>
                <th scope="row">{number}</th>
                <td className="remediation__issue">{issue}</td>
                {index === 0 ? (
                  <>
                    <td rowSpan={lines.length}>
                      {action.remedialAction ?? '—'}
                      {action.evidenceReference ? (
                        <span className="remediation__note"> IO {action.evidenceReference}</span>
                      ) : null}
                    </td>
                    {/*
                      An action nobody has been given still has an adviser: the case names
                      one, and the portal falls back to it rather than saying "Unassigned"
                      against a case that plainly has an owner.
                    */}
                    <td rowSpan={lines.length}>
                      {action.assignedTo ?? adviserName ?? 'Unassigned'}
                    </td>
                    <td rowSpan={lines.length}>{action.dueOn ?? '—'}</td>
                    <td rowSpan={lines.length}>{action.status}</td>
                    <td rowSpan={lines.length}>{ageOf(action)}</td>
                    <td rowSpan={lines.length}>{signoffCell(action, signoffs)}</td>
                    <td rowSpan={lines.length}>
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
                  </>
                ) : null}
              </tr>
            ))}
            {note ? (
              <tr className="remediation__context">
                <td className="remediation__note" colSpan={ACTION_COLUMNS}>
                  {note}
                </td>
              </tr>
            ) : null}
          </Fragment>
        ))}
      </tbody>
    </table>
  );
}

/**
 * The form's last block, drawn as the paper form draws it and as the portal draws it.
 *
 * This replaced four columns on every row and two tables underneath. The columns repeated
 * one set of answers down the table, and the tables were the app's own shape rather than
 * the form's: the three answers are the adviser's and are answered once for the case, and
 * the regrade and the supervisor's decision belong to the case, not to any one action.
 *
 * Exported for remediationRender.test.tsx.
 */
export function RemediationFormBlock({
  actions,
  outcomes,
  signoffs,
}: {
  actions: RemediationActionRow[];
  outcomes: OutcomeRow[];
  signoffs: SignoffRow[];
}) {
  const block = remediationForm(actions, outcomes, signoffs);
  const fields: Array<[string, string | null]> = [
    ['Client contact required?', block.clientContactRequired],
    ['Recheck required?', block.recheckRequired],
    ['Do the remedial actions change the advice?', block.changesAdvice],
    ['All remedial actions checked and approved?', block.allApproved],
    ['Regraded outcome', block.regradedOutcome],
    ['Date', block.regradedOn],
    ['Supervisor sign-off', block.supervisorSignoff],
    ['Adviser sign-off', block.adviserSignoff],
  ];

  return (
    <>
      <dl className="remediation__form">
        {fields.map(([label, value]) => (
          <div key={label} className="remediation__form-field">
            <dt>{label}</dt>
            <dd>{value ?? '—'}</dd>
          </div>
        ))}
      </dl>
      <p className="remediation__note">
        The three answers are the adviser&rsquo;s, recorded on the remediation action (the
        first action carrying an answer). &ldquo;All remedial actions checked and
        approved&rdquo; reads Yes when every action&rsquo;s latest supervisor decision is
        Approved.
      </p>
    </>
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
  // The outcome the remediation was raised for. Read across every action rather than the
  // filtered set: it is the case's, so narrowing the table by status must not change it.
  const outcome = useMemo(() => outcomeOf(allActions), [allActions]);

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
      .catch((error) => {
        setBusyId(null);
        setNotice({
          tone: 'error',
          message: messageForFailure(classify(error)),
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
            <h2 id="remediation-actions">
              {state.outcomeCase?.reference ?? 'Remediation actions'}
              {state.outcomeCase?.status ? (
                <span className="remediation__status">{state.outcomeCase.status}</span>
              ) : null}
            </h2>
            <RemediationDetails outcomeCase={state.outcomeCase} outcome={outcome} caseId={caseId} />
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
            <ActionsTable
              actions={filteredActions}
              signoffs={state.signoffs}
              adviserName={state.outcomeCase?.adviserName ?? null}
              busyId={busyId}
              onComplete={handleComplete}
            />
            <RemediationFormBlock
              actions={allActions}
              outcomes={state.outcomes}
              signoffs={state.signoffs}
            />
          </section>
        </>
      ) : null}
    </>
  );
}
