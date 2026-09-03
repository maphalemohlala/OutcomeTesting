import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { Notice } from '../../components/feedback/Notice';
import { ValidationSummary } from '../../components/feedback/ValidationSummary';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import {
  FINAL_OUTCOMES,
  regradeCase,
  type FinalOutcome,
} from '../../services/commands/regradeCase';
import { useCaseDetail } from './useCaseDetail';
import { useCaseOutcomes } from './useCaseOutcomes';

import './RecheckPage.css';

/**
 * Recheck and regrade (FR-024, BR-007, OD-007, AD-031). The T&C Manager records the final
 * outcome on a graded case while the initial outcome is preserved, so both survive.
 *
 * This is the privileged correction route, not a details edit: `Closed` and
 * `No Check Required` are terminal in the lifecycle (AD-057), and al_UpdateCaseDetails
 * refuses to move a case out of them. Regrading is how a wrong grade is put right.
 *
 * The screen is an affordance, not a boundary. al_RegradeCase re-checks command.regrade
 * server-side, so hiding the form changes nothing about what is permitted (NFR-SEC-01).
 *
 * The reason is mandatory here as well as in the command, because a caller who is told
 * why up front does not have to discover it from a rejection.
 */
export function RecheckPage() {
  const { caseId } = useParams<{ caseId: string }>();
  const { can } = usePermissions();
  const [reloadKey, setReloadKey] = useState(0);

  const detail = useCaseDetail(caseId, reloadKey);
  const outcomes = useCaseOutcomes(caseId, reloadKey);

  const [outcomeId, setOutcomeId] = useState('');
  const [finalOutcome, setFinalOutcome] = useState<FinalOutcome | ''>('');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const intent = useIntentKeys();

  const mayRegrade = can('command.regrade', 'Edit');
  const rows = outcomes.status === 'ready' ? outcomes.outcomes : [];
  // With one outcome the choice is not a choice, so the form settles it rather than
  // asking. A Tax-then-AQS case records one per review instance, and then it matters.
  const chosen = rows.length === 1 ? rows[0] : rows.find((row) => row.id === outcomeId);

  function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;

    const found: string[] = [];
    if (!chosen) found.push('Choose which outcome to regrade.');
    if (!finalOutcome) found.push('Choose the corrected final outcome.');
    if (!reason.trim()) {
      found.push('Give the reason for this regrade. It is recorded permanently.');
    }
    setErrors(found);
    if (found.length > 0 || !chosen || !finalOutcome) return;

    setFailure(null);
    setNotice(null);
    setSaving(true);

    // One key per (outcome, grade) intent, so retrying a timed-out regrade replays the
    // original result rather than writing a second correction (NFR-REL-01).
    const token = `regrade:${chosen.id}:${finalOutcome}`;

    regradeCase({
      outcomeId: chosen.id,
      finalOutcome,
      reason: reason.trim(),
      expectedRowVersion: chosen.rowVersion,
      idempotencyKey: intent.keyFor(token),
    })
      .then((result) => {
        setSaving(false);
        if (!result.ok) {
          setFailure(messageForFailure(result));
          return;
        }

        if (result.data.Conflict) {
          // The row moved under us. Reload rather than retry: the grade on screen is no
          // longer the grade being corrected, so a resubmit would act on a stale reading.
          setFailure(
            'Someone else changed this outcome while you were working. Nothing has been changed — the current grade is shown below.',
          );
          setReloadKey((key) => key + 1);
          return;
        }

        intent.release(token);
        setNotice(
          `Regraded to ${result.data.FinalOutcome}. The initial outcome is unchanged and both are kept.`,
        );
        setReason('');
        setFinalOutcome('');
        setReloadKey((key) => key + 1);
      })
      .catch(() => {
        setSaving(false);
        setFailure('We could not record the regrade. Nothing has been changed.');
      });
  }

  return (
    <>
      <p className="recheck__back">
        <Link to={`/cases/${caseId ?? ''}`}>← Back to case</Link>
      </p>

      <PageIntro
        title="Recheck and regrade"
        purpose="Record a recheck and set the final outcome while preserving the initial one (FR-024, BR-007)."
      />

      {detail.status === 'unavailable' ? (
        <Notice tone="error">This case is not available to you, so it cannot be regraded.</Notice>
      ) : null}

      {outcomes.status === 'loading' ? <p role="status">Loading outcomes…</p> : null}

      {outcomes.status === 'unavailable' ? (
        <Notice tone="error">
          The outcomes on this case are not available to you, so there is nothing to regrade.
        </Notice>
      ) : null}

      {outcomes.status === 'ready' && rows.length === 0 ? (
        <Notice tone="info">
          This case has not been graded yet, so there is nothing to regrade. A recheck applies
          to an outcome that has already been recorded.
        </Notice>
      ) : null}

      {!mayRegrade && outcomes.status === 'ready' && rows.length > 0 ? (
        <Notice tone="info">
          You can see the outcomes on this case, but only a T&amp;C Manager can regrade one.
        </Notice>
      ) : null}

      {notice ? <Notice tone="success">{notice}</Notice> : null}
      {failure ? <Notice tone="error">{failure}</Notice> : null}

      {mayRegrade && rows.length > 0 ? (
        <form className="recheck__form" onSubmit={onSubmit}>
          <ValidationSummary errors={errors} />

          {rows.length > 1 ? (
            <div className="recheck__field">
              <label htmlFor="recheck-outcome">Outcome to regrade</label>
              <select
                id="recheck-outcome"
                value={outcomeId}
                onChange={(event) => setOutcomeId(event.target.value)}
              >
                <option value="">Choose an outcome…</option>
                {rows.map((row) => (
                  <option key={row.id} value={row.id}>
                    {row.reviewInstance ?? row.reference} —{' '}
                    {row.finalOutcome ?? row.initialOutcome}
                  </option>
                ))}
              </select>
              <p className="recheck__hint">
                This case carries an outcome for each check, so choose the one being corrected
                (BR-004).
              </p>
            </div>
          ) : null}

          <div className="recheck__field">
            <label htmlFor="recheck-final">Final outcome</label>
            <select
              id="recheck-final"
              value={finalOutcome}
              onChange={(event) => setFinalOutcome(event.target.value as FinalOutcome | '')}
            >
              <option value="">Choose the corrected grade…</option>
              {FINAL_OUTCOMES.map((outcome) => (
                <option key={outcome} value={outcome}>
                  {outcome}
                </option>
              ))}
            </select>
            <p className="recheck__hint">
              {chosen
                ? `The initial outcome stays ${chosen.initialOutcome} and is not overwritten (BR-007).`
                : 'The initial outcome is never overwritten (BR-007).'}
            </p>
          </div>

          <div className="recheck__field">
            <label htmlFor="recheck-reason">Reason</label>
            <textarea
              id="recheck-reason"
              rows={4}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              aria-describedby="recheck-reason-hint"
            />
            <p className="recheck__hint" id="recheck-reason-hint">
              Mandatory. Recorded on the outcome and on an immutable Audit Event that cannot
              later be edited (BR-012, NFR-AUD-01, AD-031).
            </p>
          </div>

          <button type="submit" className="recheck__submit" disabled={saving}>
            {saving ? 'Recording…' : 'Record regrade'}
          </button>
        </form>
      ) : null}

      <section className="recheck__current" aria-labelledby="recheck-current-heading">
        <h2 id="recheck-current-heading">Outcomes on this case</h2>

        {outcomes.status === 'ready' && rows.length > 0 ? (
          <table className="recheck__table">
            <thead>
              <tr>
                <th scope="col">Check</th>
                <th scope="col">Initial outcome</th>
                <th scope="col">Final outcome</th>
                <th scope="col">Regraded</th>
                <th scope="col">Reason</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td>{row.reviewInstance ?? row.reference}</td>
                  <td>{row.initialOutcome}</td>
                  <td>{row.finalOutcome ?? 'Not regraded'}</td>
                  <td>{row.regradedOn ?? '—'}</td>
                  <td>{row.regradeReason ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}
      </section>
    </>
  );
}
