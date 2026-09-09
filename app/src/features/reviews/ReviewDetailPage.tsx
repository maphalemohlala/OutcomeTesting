import type { ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import type { ReviewType } from '../../types/domain';
import { useRemediation } from '../remediation/useRemediation';
import { useReviewDetail, type ReviewResponse } from './useReviewDetail';
import type { FormRow, ReviewSection } from './reviewSections';
import {
  FILE_QUALITY_OUTCOME_CODES,
  isOutcomeLens,
  isTicked,
  optionsFor,
  remediationSummary,
  sectionLayout,
  type ChoiceOption,
  type FailPoint,
  type HeaderField,
} from './checklistForm';
import './ReviewDetailPage.css';

interface ReviewDetailPageProps {
  reviewType: ReviewType;
}

const INTRO: Record<ReviewType, string> = {
  Tax: 'The Tax-owned part of the Checker Checklist as recorded so far (FR-015). Grading is a permissioned write path and is not yet available here (OD-007).',
  AQS: 'The Checker Checklist as recorded so far (FR-011, BR-005). Grading is a permissioned write path and is not yet available here (OD-007).',
};

/** A tick box as the document draws it: ticked or empty, never a word. */
function Tick({ ticked, label }: { ticked: boolean; label: string }) {
  return (
    <span
      className="checklist__tick"
      data-ticked={ticked ? 'true' : undefined}
      role="img"
      aria-label={`${label}: ${ticked ? 'ticked' : 'not ticked'}`}
    >
      {ticked ? '☑' : '☐'}
    </span>
  );
}

/** The options of one row laid inline, as the document does for a mixed section. */
function InlineOptions({
  row,
  options,
}: {
  row: FormRow<ReviewResponse>;
  options: ChoiceOption[];
}) {
  return (
    <span className="checklist__options">
      {options.map((option) => (
        <span key={option.value} className="checklist__option">
          <Tick ticked={isTicked(row, option)} label={option.label} />
          {option.label}
        </span>
      ))}
    </span>
  );
}

/** A free-text or date answer; the empty box the document leaves for it when unanswered. */
function ValueCell({ row }: { row: FormRow<ReviewResponse> }) {
  const answer = row.response?.answer ?? null;
  return (
    <span className="checklist__value" data-empty={answer === null ? 'true' : undefined}>
      {answer ?? ''}
    </span>
  );
}

function QuestionCell({ row }: { row: FormRow<ReviewResponse> }) {
  return (
    <>
      {row.question}
      {row.response?.note ? <span className="checklist__note">{row.response.note}</span> : null}
    </>
  );
}

function FieldList({ fields }: { fields: HeaderField[] }) {
  return (
    <dl className="checklist__fields">
      {fields.map((field) => (
        <div key={field.label} className="checklist__field">
          <dt>{field.label}</dt>
          <dd data-empty={field.value === null ? 'true' : undefined}>{field.value ?? ''}</dd>
        </div>
      ))}
    </dl>
  );
}

function SectionCard({ section }: { section: ReviewSection<ReviewResponse> }) {
  const layout = sectionLayout(section);
  const lens = isOutcomeLens(section);
  const headingId = `review-section-${section.id}`;
  const columns = layout.kind === 'grid' ? layout.options.length + 1 : 2;

  return (
    <section className="checklist__card" aria-labelledby={headingId}>
      <h2 id={headingId}>{section.name}</h2>
      {section.helpText && !lens ? <p className="checklist__help">{section.helpText}</p> : null}

      <table
        className={`checklist__table ${layout.kind === 'grid' ? 'checklist__table--grid' : 'checklist__table--inline'}`}
      >
        {layout.kind === 'grid' ? (
          <thead>
            <tr>
              <th scope="col">{lens ? 'Suitability test point' : 'Check'}</th>
              {layout.options.map((option) => (
                <th key={option.value} scope="col" className="checklist__tick-col">
                  {option.label}
                </th>
              ))}
            </tr>
          </thead>
        ) : null}
        <tbody>
          {section.rows.map((row) => (
            <tr key={row.key}>
              <th scope="row">
                <QuestionCell row={row} />
              </th>
              {layout.kind === 'grid' ? (
                layout.options.map((option) => (
                  <td key={option.value} className="checklist__tick-col">
                    <Tick ticked={isTicked(row, option)} label={option.label} />
                  </td>
                ))
              ) : (
                <td>
                  {optionsFor(row.responseTypeValue).length > 0 ? (
                    <InlineOptions row={row} options={optionsFor(row.responseTypeValue)} />
                  ) : (
                    <ValueCell row={row} />
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
        {section.helpText && lens ? (
          <tfoot>
            <tr>
              <td colSpan={columns} className="checklist__lens">
                Outcome lens: {section.helpText}
              </td>
            </tr>
          </tfoot>
        ) : null}
      </table>
    </section>
  );
}

/**
 * File Quality - Fail points: its own block on the document, between the checking points
 * and the File Quality outcome, listing every reason with a tick. Not a per-answer picker
 * (AD-096, which supersedes AD-054).
 */
function FailPointsCard({ points }: { points: FailPoint[] }) {
  return (
    <section className="checklist__card" aria-labelledby="review-fail-points">
      <h2 id="review-fail-points">File Quality – Fail points</h2>
      {points.length === 0 ? (
        <p className="checklist__help">No fail reasons are configured for this team.</p>
      ) : (
        <table className="checklist__table checklist__table--grid">
          <thead>
            <tr>
              <th scope="col">File Quality fail reason</th>
              <th scope="col" className="checklist__tick-col">
                Tick
              </th>
            </tr>
          </thead>
          <tbody>
            {points.map((point) => (
              <tr key={point.id}>
                <th scope="row">{point.label}</th>
                <td className="checklist__tick-col">
                  <Tick ticked={point.ticked} label={point.label} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function HeaderCard({ fields }: { fields: HeaderField[] | null }) {
  return (
    <section className="checklist__card" aria-labelledby="review-form-header">
      <h2 id="review-form-header">Outcome Testing – Checker Checklist</h2>
      {fields === null ? (
        <p className="checklist__help">
          The case header could not be read, so the case details are not shown here.
        </p>
      ) : (
        <FieldList fields={fields} />
      )}
    </section>
  );
}

/**
 * Remediation and escalation, the document's closing block. Per case rather than per
 * review, as the remedial actions are (AD-095), so both the Tax and the AQS form of a
 * case show the same lines.
 */
function RemediationCard({ caseId }: { caseId: string | null }) {
  const remediation = useRemediation(caseId ?? undefined);
  const summary =
    remediation.status === 'ready'
      ? remediationSummary(remediation.actions, remediation.signoffs, remediation.outcomes)
      : null;

  return (
    <section className="checklist__card" aria-labelledby="review-remediation">
      <h2 id="review-remediation">Remediation and escalation</h2>
      {!caseId ? (
        <p className="checklist__help">
          This review is not linked to a case, so it has no remediation record.
        </p>
      ) : remediation.status === 'loading' ? (
        <p className="checklist__help" role="status">
          Loading remediation…
        </p>
      ) : remediation.status === 'unavailable' ? (
        <p className="checklist__help">{remediation.reason}</p>
      ) : summary ? (
        <>
          <table className="checklist__table checklist__table--grid">
            <thead>
              <tr>
                <th scope="col" className="checklist__no-col">
                  No.
                </th>
                <th scope="col">Issue / fail reason</th>
                <th scope="col">Remedial action</th>
                <th scope="col">Owner</th>
                <th scope="col">Target date</th>
                <th scope="col">Sign-off</th>
              </tr>
            </thead>
            <tbody>
              {summary.lines.length === 0 ? (
                <tr>
                  <td className="checklist__no-col">1</td>
                  <td colSpan={5}>
                    <span className="checklist__value" data-empty="true">
                      No remedial action has been raised.
                    </span>
                  </td>
                </tr>
              ) : (
                summary.lines.map((line, index) => (
                  <tr key={line.id}>
                    <td className="checklist__no-col">{index + 1}</td>
                    <td>{line.issue}</td>
                    <td>{line.remedialAction ?? ''}</td>
                    <td>{line.owner ?? ''}</td>
                    <td>{line.targetDate ?? ''}</td>
                    <td>{line.signOff ?? ''}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
          <FieldList
            fields={[
              { label: 'Client contact required?', value: summary.clientContactRequired },
              { label: 'Recheck required?', value: summary.recheckRequired },
              {
                label: 'Do the remedial actions change the advice?',
                value: summary.changesAdvice,
              },
              {
                label: 'All remedial actions checked and approved?',
                value: summary.allApproved,
              },
              { label: 'Regraded Outcome', value: summary.regradedOutcome },
              { label: 'Supervisor sign off', value: summary.supervisorSignOff },
              { label: 'Adviser sign off', value: summary.adviserSignOff },
            ]}
          />
        </>
      ) : null}
    </section>
  );
}

/**
 * The team's sections in their display order, with the standalone fail points placed
 * where the document puts them: directly before the File Quality outcome. A form with no
 * File Quality outcome section (the sections read failed) still shows what was ticked,
 * after whatever else could be shown.
 */
function formCards(sections: ReviewSection<ReviewResponse>[], points: FailPoint[]): ReactNode[] {
  const cards: ReactNode[] = [];
  let placed = false;
  for (const section of sections) {
    if (!placed && FILE_QUALITY_OUTCOME_CODES.has(section.code ?? '')) {
      cards.push(<FailPointsCard key="fail-points" points={points} />);
      placed = true;
    }
    cards.push(<SectionCard key={section.id} section={section} />);
  }
  if (!placed) cards.push(<FailPointsCard key="fail-points" points={points} />);
  return cards;
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
            actions={
              /*
               * The browser's own print dialogue, which is where "Save as PDF" lives on
               * every platform. It renders what is on screen through the print rules in
               * base.css, so the exported checklist cannot drift from the one being read -
               * which a separately generated PDF would.
               */
              <button
                type="button"
                className="dashboard__link"
                onClick={() => window.print()}
              >
                Save as PDF
              </button>
            }
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

          {/*
            * The Checker Checklist document, block for block and in its order: the case
            * header; then the team's sections in their display order - Tax check, File
            * Quality, AML and CRA, E1 to E5, CRP, Consumer Duty, grading - with the
            * standalone File Quality fail points directly before the File Quality outcome;
            * then remediation and escalation.
            */}
          <HeaderCard fields={state.detail.caseHeader} />

          {state.detail.sections.length === 0 ? (
            <section className="checklist__card" aria-labelledby="review-no-questions">
              <h2 id="review-no-questions">No questions in this version</h2>
              <p className="checklist__help">
                The checklist version issued to this review has no sections for this team.
              </p>
            </section>
          ) : null}

          {formCards(state.detail.sections, state.detail.failPoints)}

          <RemediationCard caseId={state.detail.header.caseId} />
        </>
      ) : null}
    </>
  );
}
