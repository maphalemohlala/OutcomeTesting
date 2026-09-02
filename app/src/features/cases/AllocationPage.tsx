import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { Notice } from '../../components/feedback/Notice';
import { ValidationSummary } from '../../components/feedback/ValidationSummary';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { assignCase } from '../../services/commands/assignCase';
import { useCaseDetail } from './useCaseDetail';
import { useCaseReviews } from './useCaseReviews';
import { useAllocationCandidates } from './useAllocationCandidates';
import './AllocationPage.css';

/**
 * Manual case allocation (FR-005, FR-006, BR-003, AD-040, OD-029). A team lead or manager
 * picks a named member of the review team for one check; al_AssignCase writes the
 * assignment history row, releases the previous one, stamps the review instance and moves
 * the case to Assigned.
 *
 * This screen is an affordance, not a boundary. The command re-checks command.assign
 * server-side, so hiding the form changes nothing about what is permitted (NFR-SEC-01).
 *
 * Not offered here, deliberately: a team filter. OD-029 records that per-team scoping is
 * unresolved — Senior Checker carries no team affiliation — so the queue name is free text
 * and nothing stops a Tax lead allocating an AQS check. Presenting a team filter would
 * imply an enforcement that does not exist.
 */
export function AllocationPage() {
  const { caseId } = useParams<{ caseId: string }>();
  const { can } = usePermissions();
  const [reloadKey, setReloadKey] = useState(0);

  const detail = useCaseDetail(caseId, reloadKey);
  const reviews = useCaseReviews(caseId);
  const candidates = useAllocationCandidates();

  const [reviewId, setReviewId] = useState('');
  const [assigneeEmail, setAssigneeEmail] = useState('');
  const [team, setTeam] = useState('');
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [notice, setNotice] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const intent = useIntentKeys();

  const mayAssign = can('command.assign', 'Edit');
  const openReviews =
    reviews.status === 'ready' ? reviews.reviews.filter((review) => !review.submittedOn) : [];

  function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (saving || !caseId) return;

    const found: string[] = [];
    if (!assigneeEmail) found.push('Choose who to allocate this check to.');
    if (!reviewId && openReviews.length > 1) {
      found.push('Choose which check to allocate.');
    }
    setErrors(found);
    if (found.length > 0) return;

    setFailure(null);
    setNotice(null);
    setSaving(true);

    // One key per (case, assignee) intent, so a retry after a timeout replays the original
    // allocation instead of writing a second history row (NFR-REL-01).
    const token = `assign:${caseId}:${assigneeEmail}`;

    assignCase({
      caseId,
      assigneeEmail,
      reviewInstanceId: reviewId || null,
      team: team.trim() || null,
      reason: reason.trim() || null,
      idempotencyKey: intent.keyFor(token),
    })
      .then((result) => {
        setSaving(false);
        if (!result.ok) {
          setFailure(messageForFailure(result));
          return;
        }

        intent.release(token);
        setNotice(
          result.data.Status === 'AlreadyAssigned'
            ? 'That allocation had already been recorded, so nothing was changed.'
            : 'Allocated. The reviewer will see this check in their portal worklist.',
        );
        setAssigneeEmail('');
        setReason('');
        setReloadKey((key) => key + 1);
      })
      .catch(() => {
        setSaving(false);
        setFailure('We could not record the allocation. Nothing has been changed.');
      });
  }

  return (
    <>
      <p className="allocation__back">
        <Link to={`/cases/${caseId ?? ''}`}>← Back to case</Link>
      </p>

      <PageIntro
        title="Allocation"
        purpose="Allocate a check to a named member of the review team and record why (FR-005, FR-006)."
      />

      {detail.status === 'unavailable' ? (
        <Notice tone="error">
          This case is not available to you, so it cannot be allocated.
        </Notice>
      ) : null}

      {reviews.status === 'ready' && openReviews.length === 0 ? (
        <Notice tone="info">
          Every check on this case has been submitted, so there is nothing to allocate.
        </Notice>
      ) : null}

      {!mayAssign ? (
        <Notice tone="info">
          You can see this case&apos;s allocation, but only a team lead or manager can change
          it.
        </Notice>
      ) : null}

      {notice ? <Notice tone="success">{notice}</Notice> : null}
      {failure ? <Notice tone="error">{failure}</Notice> : null}

      {mayAssign && openReviews.length > 0 ? (
        <form className="allocation__form" onSubmit={onSubmit}>
          <ValidationSummary errors={errors} />

          <div className="allocation__field">
            <label htmlFor="allocation-review">Check</label>
            <select
              id="allocation-review"
              value={reviewId}
              onChange={(event) => setReviewId(event.target.value)}
            >
              <option value="">
                {openReviews.length === 1
                  ? `${openReviews[0].type} (the only open check)`
                  : 'Choose a check…'}
              </option>
              {openReviews.map((review) => (
                <option key={review.id} value={review.id}>
                  {review.type} — {review.status}
                </option>
              ))}
            </select>
          </div>

          <div className="allocation__field">
            <label htmlFor="allocation-assignee">Allocate to</label>
            <select
              id="allocation-assignee"
              value={assigneeEmail}
              onChange={(event) => setAssigneeEmail(event.target.value)}
            >
              <option value="">Choose a reviewer…</option>
              {candidates.status === 'ready'
                ? candidates.candidates.map((candidate) => (
                    <option key={candidate.id} value={candidate.workEmail}>
                      {candidate.name} ({candidate.workEmail})
                    </option>
                  ))
                : null}
            </select>
            {candidates.status === 'loading' ? <p role="status">Loading people…</p> : null}
            {candidates.status === 'unavailable' ? (
              <p className="allocation__hint">
                The user registry is not available to you, so there is no one to choose from.
              </p>
            ) : null}
          </div>

          <div className="allocation__field">
            <label htmlFor="allocation-team">Queue</label>
            <input
              id="allocation-team"
              type="text"
              value={team}
              onChange={(event) => setTeam(event.target.value)}
              placeholder="Tax, AQS…"
            />
            <p className="allocation__hint">
              Recorded on the assignment for the audit trail. It does not restrict who may be
              chosen (OD-029).
            </p>
          </div>

          <div className="allocation__field">
            <label htmlFor="allocation-reason">Reason</label>
            <textarea
              id="allocation-reason"
              rows={3}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
            />
            <p className="allocation__hint">
              Required in practice when moving work between people, so the trail explains
              itself later (BR-012).
            </p>
          </div>

          <button type="submit" className="allocation__submit" disabled={saving}>
            {saving ? 'Allocating…' : 'Allocate check'}
          </button>
        </form>
      ) : null}

      <section className="allocation__current" aria-labelledby="allocation-current-heading">
        <h2 id="allocation-current-heading">Checks on this case</h2>

        {reviews.status === 'loading' ? <p role="status">Loading checks…</p> : null}
        {reviews.status === 'unavailable' ? (
          <p>The checks on this case are not available to you.</p>
        ) : null}

        {reviews.status === 'ready' && reviews.reviews.length > 0 ? (
          <table className="allocation__table">
            <thead>
              <tr>
                <th scope="col">Check</th>
                <th scope="col">Status</th>
                <th scope="col">Owner</th>
                <th scope="col">Submitted</th>
              </tr>
            </thead>
            <tbody>
              {reviews.reviews.map((review) => (
                <tr key={review.id}>
                  <td>{review.type}</td>
                  <td>{review.status}</td>
                  <td>{review.owner ?? 'Unassigned'}</td>
                  <td>{review.submittedOn ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : null}

        {reviews.status === 'ready' && reviews.reviews.length === 0 ? (
          <p>This case has no checks raised against it yet.</p>
        ) : null}
      </section>
    </>
  );
}
