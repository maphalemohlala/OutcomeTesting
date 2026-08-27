import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import type { ReviewType } from '../../types/domain';
import { useReviewDetail, type ReviewResponse } from './useReviewDetail';
import './ReviewDetailPage.css';

interface ReviewDetailPageProps {
  reviewType: ReviewType;
}

const INTRO: Record<ReviewType, string> = {
  Tax: 'Read the Tax-owned questions and the answers recorded so far (FR-015). Grading is a permissioned write path and is not yet available here (OD-007).',
  AQS: 'Read the file and advice quality answers recorded so far (FR-011, BR-005). Grading is a permissioned write path and is not yet available here (OD-007).',
};

function AnswerCell({ response }: { response: ReviewResponse }) {
  return (
    <>
      <span
        className="review__answer-value"
        data-empty={response.answer === null ? 'true' : undefined}
      >
        {response.answer ?? 'Not answered'}
      </span>
      {response.note ? <span className="review__answer-note">{response.note}</span> : null}
    </>
  );
}

export function ReviewDetailPage({ reviewType }: ReviewDetailPageProps) {
  const { reviewId } = useParams<{ reviewId: string }>();
  const state = useReviewDetail(reviewId, reviewType);

  return (
    <>
      {state.status === 'ready' && state.detail.header.caseId ? (
        <p className="review__back">
          <Link to={`/cases/${state.detail.header.caseId}`}>← Back to case</Link>
        </p>
      ) : null}

      {state.status === 'loading' ? <p role="status">Loading review…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="review__unavailable" aria-labelledby="review-unavailable">
          <h2 id="review-unavailable">This review is not available</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <PageIntro
            title={`${reviewType} check — ${state.detail.header.reference}`}
            purpose={INTRO[reviewType]}
          />

          {state.detail.header.typeMismatch ? (
            <p className="review__mismatch" role="alert">
              This review is recorded as a {state.detail.header.type} check, not a {reviewType}{' '}
              check. The details below are shown as held in Dataverse.
            </p>
          ) : null}

          <section className="review__summary" aria-label="Review summary">
            <div className="review__summary-item">
              <span className="review__summary-label">Type</span>
              <span className="review__summary-value">{state.detail.header.type}</span>
            </div>
            <div className="review__summary-item">
              <span className="review__summary-label">Status</span>
              <span className="review__summary-value">{state.detail.header.status}</span>
            </div>
            <div className="review__summary-item">
              <span className="review__summary-label">Case</span>
              <span className="review__summary-value">
                {state.detail.header.caseId ? (
                  <Link to={`/cases/${state.detail.header.caseId}`}>
                    {state.detail.header.caseName ?? 'View case'}
                  </Link>
                ) : (
                  state.detail.header.caseName ?? 'Not linked'
                )}
              </span>
            </div>
            <div className="review__summary-item">
              <span className="review__summary-label">Checklist version</span>
              <span className="review__summary-value">
                {state.detail.header.checklistVersion ?? '—'}
              </span>
            </div>
            <div className="review__summary-item">
              <span className="review__summary-label">Owner</span>
              <span className="review__summary-value">
                {state.detail.header.owner ?? 'Unassigned'}
              </span>
            </div>
            <div className="review__summary-item">
              <span className="review__summary-label">Started</span>
              <span className="review__summary-value">
                {state.detail.header.startedOn ?? '—'}
              </span>
            </div>
            <div className="review__summary-item">
              <span className="review__summary-label">Submitted</span>
              <span className="review__summary-value">
                {state.detail.header.submittedOn ?? 'Not submitted'}
              </span>
            </div>
          </section>

          <section className="review__answers" aria-labelledby="review-answers">
            <h2 id="review-answers">Recorded answers</h2>
            {state.detail.responses.length === 0 ? (
              <p className="review__answers-note">
                No answer has been recorded against this review yet.
              </p>
            ) : (
              <table className="review__table">
                <thead>
                  <tr>
                    <th scope="col">Question</th>
                    <th scope="col">Response type</th>
                    <th scope="col">Answer</th>
                    <th scope="col">Answered</th>
                  </tr>
                </thead>
                <tbody>
                  {state.detail.responses.map((response) => (
                    <tr key={response.id}>
                      <th scope="row">{response.question}</th>
                      <td>{response.responseType}</td>
                      <td>
                        <AnswerCell response={response} />
                      </td>
                      <td>{response.answeredOn ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        </>
      ) : null}
    </>
  );
}
