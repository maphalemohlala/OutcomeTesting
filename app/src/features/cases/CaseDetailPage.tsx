import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { UPLOADED_BY_LABEL } from '../../types/domain';
import { StageLabel } from '../../components/status/StageLabel';
import { Tabs } from '../../components/navigation/Tabs';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { useCaseDetail } from './useCaseDetail';
import { CaseHeaderTable } from '../reviews/CaseHeaderTable';
import { ChecklistSection } from '../reviews/ChecklistSection';
import { useCaseReviews } from './useCaseReviews';
import { CaseOutcomeSummary } from './CaseOutcomeSummary';
import { FailAccountabilityPanel } from './FailAccountabilityPanel';
import { CaseHistoryPanel } from './CaseHistoryPanel';
import { CaseEditPanel } from './CaseEditPanel';
import './CaseDetailPage.css';

/**
 * The case header is drawn by CaseHeaderTable as the Checker Checklist draws it (project
 * owner, 2026-09-13). This replaced four themed panels - Client, Adviser and paraplanner,
 * Advice and product, Check and tax. The document has no such grouping, so a reader
 * holding the paper form had to hunt across four panels for a field the form puts in one
 * fixed place.
 */
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
              <span className="case-detail__summary-label">{UPLOADED_BY_LABEL}</span>
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

          <Tabs
            label="Case detail"
            items={[
              {
                id: 'details',
                label: 'Details',
                render: () => (
                  <>
                    {/*
                      What the paraplanner ticked in the IO task, first on the tab (project
                      owner, 2026-09-19): it is why the case was raised for checking at all,
                      so it is read before the case's own fields rather than after them. The
                      portal's case record page orders it the same way. The same section the
                      review page draws, from the same component, so a manager looking a case
                      up here sees what a checker opening it sees.
                    */}
                    <ChecklistSection checklist={state.detail.checklist} />

                    <section className="case-detail__panel" aria-labelledby="panel-header">
                      <h2 id="panel-header">Case details</h2>
                      <CaseHeaderTable fields={state.detail.header} variant="case" />
                    </section>

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

                    {/*
                      Directly under the grades it describes (item 8, 2026-09-19).
                      Accountability only means anything beside the outcome that produced
                      the fail, and the names it offers are this case's own adviser and
                      paraplanner, which the header above already shows.
                    */}
                    <FailAccountabilityPanel
                      caseId={state.detail.id}
                      people={{
                        adviser: state.detail.edit.al_advisername || null,
                        paraplanner: state.detail.edit.al_paraplanner || null,
                      }}
                    />

                    <PermissionGate resource="page.remediation">
                      <section className="case-detail__related" aria-label="Related records">
                        <Link to={`/cases/${state.detail.id}/remediation`}>
                          Remediation and sign-off →
                        </Link>
                      </section>
                    </PermissionGate>

                    {state.detail.previousCase ? (
                      <section className="case-detail__lineage" aria-label="Case lineage">
                        <span className="case-detail__summary-label">Replaces</span>
                        <span>{state.detail.previousCase}</span>
                      </section>
                    ) : null}
                  </>
                ),
              },
              {
                id: 'history',
                label: 'History',
                render: () => <CaseHistoryPanel caseId={state.detail.id} />,
              },
            ]}
          />
        </>
      ) : null}
    </>
  );
}
