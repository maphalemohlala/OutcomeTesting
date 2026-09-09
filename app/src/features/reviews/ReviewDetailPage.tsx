import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import type { ReviewType } from '../../types/domain';
import { useReviewDetail, type ReviewResponse } from './useReviewDetail';
import type { FormRow } from './reviewSections';
import {
  formBlocks,
  inlineOptionsFor,
  isTicked,
  optionsFor,
  type ChoiceOption,
  type FailPoint,
  type FormBlock,
  type FormGroup,
  type HeaderField,
} from './checklistForm';
import './ReviewDetailPage.css';

interface ReviewDetailPageProps {
  reviewType: ReviewType;
}

/** Yes, the value a ticked outcome-lens box records (checklistForm.ts, Q-E2-LENS). */
const YES_VALUE = 120910305;

/** Primary root cause, the one list the document lays out as a grid rather than a run. */
const ROOT_CAUSE = 120910003;

const INTRO: Record<ReviewType, string> = {
  Tax: 'The Tax-owned part of the Checker Checklist as recorded so far (FR-015). Grading is a permissioned write path and is not yet available here (OD-007).',
  AQS: 'The Checker Checklist as recorded so far (FR-011, BR-005). Grading is a permissioned write path and is not yet available here (OD-007).',
};

/**
 * A tick box as the document draws it: a square, ticked or empty, never a word. The portal
 * draws the same box with a restyled input; this page is read-only, so it is a span.
 */
function Box({ ticked, label }: { ticked: boolean; label: string }) {
  return (
    <span
      className="cc-box"
      data-ticked={ticked ? 'true' : undefined}
      role="img"
      aria-label={`${label}: ${ticked ? 'ticked' : 'not ticked'}`}
    />
  );
}

/** A run of boxes with their labels beside them, as the document sets an inline scale. */
function Options({ row, options }: { row: FormRow<ReviewResponse>; options: ChoiceOption[] }) {
  return (
    <div className="opts">
      {options.map((option) => (
        <span key={option.value} className="opt">
          <Box ticked={isTicked(row, option)} label={option.label} /> {option.label}
        </span>
      ))}
    </div>
  );
}

/** A free-text or date answer; the empty cell the document leaves for it when unanswered. */
function Value({ row }: { row: FormRow<ReviewResponse> }) {
  const answer = row.response?.answer ?? null;
  return (
    <span className="value" data-empty={answer === null ? 'true' : undefined}>
      {answer ?? ''}
    </span>
  );
}

/** Whatever control the row's response type calls for, inside a value cell. */
function Control({ row }: { row: FormRow<ReviewResponse> }) {
  const options = inlineOptionsFor(row.responseTypeValue);
  return options.length > 0 ? <Options row={row} options={options} /> : <Value row={row} />;
}

type SectionBlock = Extract<FormBlock<ReviewResponse>, { kind: 'section' }>;

/** Whether a row answers on exactly the block's scale, and so takes the grid's tick columns. */
function onScale(row: FormRow<ReviewResponse>, block: SectionBlock): boolean {
  const options = optionsFor(row.responseTypeValue);
  return (
    block.layout === 'grid' &&
    options.length === block.options.length &&
    options.every((option, i) => option.value === block.options[i].value)
  );
}

/**
 * The case header: the document's opening block, its eighteen fields two to a row. Outcome
 * Case columns captured at intake, not checklist questions (checklist-v8.md).
 */
function HeaderTable({ fields }: { fields: HeaderField[] | null }) {
  if (fields === null) {
    return <p className="intro">The case header could not be read, so it is not shown here.</p>;
  }

  const pairs: HeaderField[][] = [];
  for (let i = 0; i < fields.length; i += 2) pairs.push(fields.slice(i, i + 2));

  return (
    <table className="meta">
      <tbody>
        {pairs.map((pair) => (
          <tr key={pair[0].label}>
            {pair.map((field) => [
              <td key={`${field.label}-l`} className="lbl">
                {field.label}
              </td>,
              <td key={`${field.label}-v`} className="val">
                {field.value ?? ''}
              </td>,
            ])}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * Primary root cause, in the 3x3 the document lays it out in, under its own heading rather
 * than in a value cell of the block's table.
 */
function RootCause({ row }: { row: FormRow<ReviewResponse> }) {
  const options = inlineOptionsFor(row.responseTypeValue);
  const groups: ChoiceOption[][] = [];
  for (let i = 0; i < options.length; i += 3) groups.push(options.slice(i, i + 3));

  return (
    <table className="rootcause">
      <tbody>
        <tr>
          <th colSpan={3}>{row.question}</th>
        </tr>
        {groups.map((group) => (
          <tr key={group[0].value}>
            {group.map((option) => (
              <td key={option.value}>
                <span className="opt wrap">
                  <Box ticked={isTicked(row, option)} label={option.label} /> {option.label}
                </span>
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/**
 * One subsection inside a grid block: its heading row where the block has subsections
 * (E1 to E5), its test points, and the document's "Outcome lens" line beneath - which on E2,
 * and on no other section, carries a tick box of its own in the last column.
 */
function GridGroup({ group, block }: { group: FormGroup<ReviewResponse>; block: SectionBlock }) {
  const columns = block.options.length + 1;
  return (
    <tbody>
      {group.heading ? (
        <tr className="section">
          <td colSpan={columns}>{group.heading}</td>
        </tr>
      ) : null}
      {group.rows.map((row) => (
        <tr key={row.key}>
          <td className="label">{row.question}</td>
          {onScale(row, block) ? (
            block.options.map((option) => (
              <td key={option.value} className="optcell">
                <Box ticked={isTicked(row, option)} label={option.label} />
              </td>
            ))
          ) : (
            <td colSpan={columns - 1}>
              <Control row={row} />
            </td>
          )}
        </tr>
      ))}
      {group.lens ? (
        <tr className="lens">
          <td colSpan={group.lensTick ? columns - 1 : columns}>
            <em>Outcome lens:</em> {group.lens}
          </td>
          {group.lensTick ? (
            <td className="optcell">
              <Box
                ticked={group.lensTick.response?.answerChoice === YES_VALUE}
                label={group.lens}
              />
            </td>
          ) : null}
        </tr>
      ) : null}
    </tbody>
  );
}

/**
 * An inline block, as a label / value table. Primary root cause breaks out of it into a
 * table of its own, so the rows either side are split into their own .meta tables around
 * it - which is what the document does, and what the portal template does in Liquid.
 */
function InlineBlock({ block }: { block: SectionBlock }) {
  const rows = block.groups.flatMap((group) => group.rows);
  const chunks: { kind: 'meta' | 'rootcause'; rows: FormRow<ReviewResponse>[] }[] = [];

  for (const row of rows) {
    const kind = row.responseTypeValue === ROOT_CAUSE ? 'rootcause' : 'meta';
    const last = chunks[chunks.length - 1];
    if (kind === 'meta' && last && last.kind === 'meta') last.rows.push(row);
    else chunks.push({ kind, rows: [row] });
  }

  return (
    <>
      {chunks.map((chunk) =>
        chunk.kind === 'rootcause' ? (
          <RootCause key={chunk.rows[0].key} row={chunk.rows[0]} />
        ) : (
          <table className="meta" key={chunk.rows[0].key}>
            <tbody>
              {chunk.rows.map((row) => (
                <tr key={row.key}>
                  <td className="lbl">{row.question}</td>
                  <td colSpan={3}>
                    <Control row={row} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ),
      )}
    </>
  );
}

/**
 * One block of the document: its heading, its intro line where it has one, then its table -
 * a tick grid headed the way the document heads it ("Suitability test point", "Check",
 * "Outcome"), or a label / value table.
 */
function Block({ block }: { block: SectionBlock }) {
  return (
    <>
      <h2>{block.title}</h2>
      {block.intro ? <p className="intro">{block.intro}</p> : null}
      {block.layout === 'grid' ? (
        <table className="grid">
          <thead>
            <tr>
              <th className="label">{block.columnHeading}</th>
              {block.options.map((option) => (
                <th key={option.value} className="opt">
                  {option.label}
                </th>
              ))}
            </tr>
          </thead>
          {block.groups.map((group) => (
            <GridGroup key={group.id} group={group} block={block} />
          ))}
        </table>
      ) : (
        <InlineBlock block={block} />
      )}
    </>
  );
}

/**
 * File Quality - Fail points: its own block on the document, between the checking points
 * and the File Quality outcome, listing every reason with a tick. Not a per-answer picker
 * (AD-096, which supersedes AD-054), and not split by team - the document draws one
 * undivided list and both disciplines pick from it (AD-100).
 */
function FailPoints({ title, points }: { title: string; points: FailPoint[] }) {
  return (
    <>
      <h2>{title}</h2>
      {points.length === 0 ? (
        <p className="intro">No fail reasons are configured.</p>
      ) : (
        <table className="fails">
          <thead>
            <tr>
              <th className="label">File Quality fail reason</th>
              <th className="tick">Tick</th>
            </tr>
          </thead>
          <tbody>
            {points.map((point) => (
              <tr key={point.id}>
                <td className="label">{point.label}</td>
                <td className="tick">
                  <Box ticked={point.ticked} label={point.label} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
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
            actions={
              /*
               * The browser's own print dialogue, which is where "Save as PDF" lives on
               * every platform. It renders what is on screen through the print rules in
               * ReviewDetailPage.css, so the exported checklist cannot drift from the one
               * being read - which a separately generated PDF would.
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
            * The Checker Checklist, drawn as the document draws it and in its order: the
            * "V5 Draft" footer line, the title, the case header, then the blocks - Tax check
            * or AML and CRA, the standalone fail points, File Quality Outcome, Suitability
            * core checks (E1 to E5 as one table), CRP, Consumer Duty, grading.
            *
            * Remediation and escalation is not here. The document marks it "PICKED UP ON
            * ANOTHER FORM" and that is how it is built: it is worked on the remediation page,
            * per case rather than per review (AD-095).
            */}
          <div className="checklist-doc">
            <div className="doc-footer">Outcome Testing Checker Checklist | V5 Draft</div>
            <h1>Outcome Testing - Checker Checklist</h1>

            <HeaderTable fields={state.detail.caseHeader} />

            {state.detail.sections.length === 0 ? (
              <>
                <h2>No questions in this version</h2>
                <p className="intro">
                  The checklist version issued to this review has no sections for this team.
                </p>
              </>
            ) : null}

            {formBlocks(state.detail.sections, state.detail.failPoints).map((block) =>
              block.kind === 'failpoints' ? (
                <FailPoints key={block.id} title={block.title} points={block.points} />
              ) : (
                <Block key={block.id} block={block} />
              ),
            )}
          </div>
        </>
      ) : null}
    </>
  );
}
