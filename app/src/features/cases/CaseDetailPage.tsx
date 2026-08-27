import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { StageLabel } from '../../components/status/StageLabel';
import { useCaseDetail, type CaseField } from './useCaseDetail';
import { useCaseReviews } from './useCaseReviews';
import { CaseOutcomeSummary } from './CaseOutcomeSummary';
import { CaseEditPanel } from './CaseEditPanel';
import './CaseDetailPage.css';

function FieldList({ fields }: { fields: CaseField[] }) {
  return (
    <dl className="case-detail__fields">
      {fields.map((field) => (
        <div key={field.label} className="case-detail__field">
          <dt>{field.label}</dt>
          <dd data-empty={field.value === null ? 'true' : undefined}>
            {field.value ?? 'Not recorded'}
          </dd>
        </div>
      ))}
    </dl>
  );
}

export function CaseDetailPage() {
  const { caseId } = useParams<{ caseId: string }>();
  const [reloadKey, setReloadKey] = useState(0);
  const state = useCaseDetail(caseId, reloadKey);
  const reviews = useCaseReviews(caseId);

  return (
    <>
      <p className="case-detail__back">
        <Link to="/cases">← Back to case worklist</Link>
      </p>

      {state.status === 'loading' ? <p role="status">Loading case…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="case-detail__unavailable" aria-labelledby="case-unavailable">
          <h2 id="case-unavailable">This case is not available</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <>
          <PageIntro
            title={state.detail.title}
            purpose="Review this case, its status and the details captured at intake."
          />

          <section className="case-detail__summary" aria-label="Case summary">
            <div className="case-detail__summary-item">
              <span className="case-detail__summary-label">Reference</span>
              <span className="case-detail__summary-value">{state.detail.caseReference}</span>
            </div>
            <div className="case-detail__summary-item">
              <span className="case-detail__summary-label">Status</span>
              <StageLabel status={state.detail.status} />
            </div>
            <div className="case-detail__summary-item">
              <span className="case-detail__summary-label">Route</span>
              <span className="case-detail__summary-value">
                {state.detail.route ?? 'Not routed'}
              </span>
            </div>
            <div className="case-detail__summary-item">
              <span className="case-detail__summary-label">Owner</span>
              <span className="case-detail__summary-value">
                {state.detail.owner ?? 'Unassigned'}
              </span>
            </div>
            <div className="case-detail__summary-item">
              <span className="case-detail__summary-label">Priority</span>
              <span className="case-detail__summary-value">
                {state.detail.priority ?? 'Not set'}
              </span>
            </div>
            <div className="case-detail__summary-item">
              <span className="case-detail__summary-label">Due</span>
              <span className="case-detail__summary-value">
                {state.detail.dueDate ?? 'Not set'}
              </span>
            </div>
            <div className="case-detail__summary-item">
              <span className="case-detail__summary-label">Age</span>
              <span className="case-detail__summary-value">{state.detail.ageInDays} days</span>
            </div>
          </section>

          <CaseEditPanel
            detail={state.detail}
            onSaved={() => setReloadKey((key) => key + 1)}
          />

          <div className="case-detail__panels">
            <section className="case-detail__panel" aria-labelledby="panel-client">
              <h2 id="panel-client">Client</h2>
              <FieldList fields={state.detail.client} />
            </section>

            <section className="case-detail__panel" aria-labelledby="panel-adviser">
              <h2 id="panel-adviser">Adviser and paraplanner</h2>
              <FieldList fields={state.detail.adviser} />
            </section>

            <section className="case-detail__panel" aria-labelledby="panel-advice">
              <h2 id="panel-advice">Advice and product</h2>
              <FieldList fields={state.detail.adviceAndProduct} />
            </section>

            <section className="case-detail__panel" aria-labelledby="panel-check">
              <h2 id="panel-check">Check and tax</h2>
              <FieldList fields={state.detail.checkAndTax} />
            </section>
          </div>

          <section className="case-detail__checks" aria-labelledby="panel-checks">
            <h2 id="panel-checks">Checks on this case</h2>
            {reviews.status === 'loading' ? <p role="status">Loading checks…</p> : null}
            {reviews.status === 'unavailable' ? (
              <p className="case-detail__checks-note">
                The checks for this case could not be loaded.
              </p>
            ) : null}
            {reviews.status === 'ready' ? (
              reviews.reviews.length === 0 ? (
                <p className="case-detail__checks-note">
                  No Tax or AQS check has been raised for this case yet.
                </p>
              ) : (
                <table className="case-detail__checks-table">
                  <thead>
                    <tr>
                      <th scope="col">Check</th>
                      <th scope="col">Type</th>
                      <th scope="col">Status</th>
                      <th scope="col">Owner</th>
                      <th scope="col">Started</th>
                      <th scope="col">Submitted</th>
                    </tr>
                  </thead>
                  <tbody>
                    {reviews.reviews.map((review) => (
                      <tr key={review.id}>
                        <th scope="row">
                          {review.type === 'Tax' || review.type === 'AQS' ? (
                            <Link to={`/reviews/${review.id}/${review.type.toLowerCase()}`}>
                              {review.reference}
                            </Link>
                          ) : (
                            review.reference
                          )}
                        </th>
                        <td>{review.type}</td>
                        <td>{review.status}</td>
                        <td>{review.owner ?? 'Unassigned'}</td>
                        <td>{review.startedOn ?? '—'}</td>
                        <td>{review.submittedOn ?? '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )
            ) : null}
          </section>

          <CaseOutcomeSummary caseId={state.detail.id} />

          <section className="case-detail__related" aria-label="Related records">
            <Link to={`/cases/${state.detail.id}/remediation`}>
              Remediation and sign-off →
            </Link>
          </section>

          {state.detail.previousCase ? (
            <section className="case-detail__lineage" aria-label="Case lineage">
              <span className="case-detail__summary-label">Replaces</span>
              <span>{state.detail.previousCase}</span>
            </section>
          ) : null}
        </>
      ) : null}
    </>
  );
}
