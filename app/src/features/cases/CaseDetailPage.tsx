import { useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { SERVICE_CASE_REF_LABEL, UPLOADED_BY_LABEL } from '../../types/domain';
import { StageLabel } from '../../components/status/StageLabel';
import { Tabs } from '../../components/navigation/Tabs';
import { Notice } from '../../components/feedback/Notice';
import { Icon, type IconName } from '../../components/icons/Icon';
import { PersonCell } from '../../components/people/Avatar';
import { useBreadcrumbTail } from '../../components/layout/breadcrumbTail';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useCaseDetail } from './useCaseDetail';
import type { HeaderField } from '../reviews/checklistForm';
import { useCaseReviews } from './useCaseReviews';
import { useCaseOutcome } from './useCaseOutcome';
import { CaseOutcomeSummary } from './CaseOutcomeSummary';
import { FailAccountabilityPanel } from './FailAccountabilityPanel';
import { CaseHistoryPanel } from './CaseHistoryPanel';
import { CaseEditPanel } from './CaseEditPanel';
import { CaseDeleteAction } from './CaseDeleteAction';
import { CaseRemoveAction } from './CaseRemoveAction';
import { CaseAmendOutcomeAction } from './CaseAmendOutcomeAction';
import { CaseTcManagerPanel } from './CaseTcManagerPanel';
import { CaseRemedialActionsPanel } from './CaseRemedialActions';
import { caseKeyDates } from './caseKeyDates';
import { ADVICE_DATE_LABEL } from './caseHeaderDates';
import { sharedEmailNote } from './casePeople';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import './CaseDetailPage.css';
import { Skeleton } from '../../components/feedback/Skeleton';

/*
 * The case header fields, grouped by what they are about (owner, 2026-11-24 redesign). Every
 * field caseHeaderFields returns is drawn: one this map does not place lands in Client, so a
 * field added there later still shows rather than silently dropping off the page.
 */
const GROUPS: { key: string; heading: string; icon: IconName; labels: string[] }[] = [
  { key: 'people', heading: 'Adviser and paraplanner', icon: 'users', labels: ['Adviser status'] },
  {
    key: 'client',
    heading: 'Client and advice',
    icon: 'briefcase',
    labels: [
      'Client name / initials',
      'Vulnerable client?',
      'Case type',
      'Product / solution type',
      'Product(s)',
      ADVICE_DATE_LABEL,
      'Sample source',
      'Business Unit',
      'Pre or post check',
    ],
  },
  {
    key: 'checks',
    heading: 'Checks',
    icon: 'checklist',
    labels: ['Tax check required', 'For Tax team usage', 'Tax Checker', 'AQS Checker', 'Check date'],
  },
  { key: 'refs', heading: 'References', icon: 'hash', labels: ['Case reference', SERVICE_CASE_REF_LABEL, 'IO reference'] },
];
// Drawn as people with their emails, not as fields.
const PEOPLE = ['Adviser name', 'Paraplanner'];

function fieldsFor(header: HeaderField[], key: string): HeaderField[] {
  const placed = new Set([...PEOPLE, ...GROUPS.flatMap((g) => g.labels)]);
  const group = GROUPS.find((g) => g.key === key)!;
  const own = group.labels.flatMap((label) => header.filter((f) => f.label === label));
  return key === 'client' ? [...own, ...header.filter((f) => !placed.has(f.label))] : own;
}

function Fields({ fields }: { fields: HeaderField[] }) {
  return (
    <dl className="case-fields">
      {fields.map((field) => (
        <div key={field.label} className="case-fields__item">
          <dt>{field.label}</dt>
          <dd data-empty={field.value === null ? 'true' : undefined}>{field.value ?? 'Not recorded'}</dd>
        </div>
      ))}
    </dl>
  );
}

function Card({
  id,
  heading,
  icon,
  aside,
  children,
}: {
  id: string;
  heading: string;
  icon?: IconName;
  aside?: ReactNode;
  children: ReactNode;
}) {
  return (
    <section className="case-card" aria-labelledby={id}>
      <div className="case-card__head">
        <h2 id={id}>
          {icon ? <Icon name={icon} className="icon case-card__icon" /> : null}
          {heading}
        </h2>
        {aside ? <span className="case-card__aside">{aside}</span> : null}
      </div>
      {children}
    </section>
  );
}

export function CaseDetailPage() {
  const { caseId } = useParams<{ caseId: string }>();
  const [reloadKey, setReloadKey] = useState(0);
  const state = useCaseDetail(caseId, reloadKey);
  // Same key the header follows. Allocating a check writes a review instance, and a
  // Checks table that did not re-read said "AQS check allocated" in the banner while
  // still listing one check underneath it (F27).
  const reviews = useCaseReviews(caseId, reloadKey);
  const directory = useUserDirectory();
  // One read for the outcome summary and the accountability editor. It follows the page's
  // key, and the editor's own after it records, so its row flips to "Recorded" (F29).
  const [outcomeKey, setOutcomeKey] = useState(0);
  const outcomes = useCaseOutcome(caseId, reloadKey + outcomeKey);
  const [editNotice, setEditNotice] = useState<string | null>(null);
  const [tab, setTab] = useState('details');
  const { can } = usePermissions();
  useBreadcrumbTail(state.status === 'ready' ? state.detail.caseReference : null);

  // Spec §1 / F4 (2026-10-04 final review): "two contacts share <email>" is a different
  // problem from "no contact holds <email>" - a duplicate onboarding to clean up, not a typo
  // to correct - so it gets its own wording using the directory the page already loads.
  const adviserDuplicateNote =
    state.status === 'ready' && state.detail.adviserUnmatched && directory.status === 'ready'
      ? sharedEmailNote(
          state.detail.edit.al_adviseremail || '',
          directory.users.filter((u) => u.active).map((u) => u.email),
        )
      : null;

  if (state.status !== 'ready') {
    return (
      <>
        <p className="case-detail__back">
          <Link to="/cases">
            <Icon name="arrowLeft" />
            Back to case worklist
          </Link>
        </p>
        {state.status === 'loading' ? <Skeleton label="Loading case…" rows={6} /> : null}
        {state.status === 'unavailable' ? (
          <section className="case-detail__unavailable" aria-labelledby="case-unavailable">
            <h2 id="case-unavailable">This case is not available</h2>
            <p>{state.reason}</p>
          </section>
        ) : null}
      </>
    );
  }

  const detail = state.detail;
  const header = detail.header;
  const valueOf = (label: string) => header.find((f) => f.label === label)?.value ?? null;
  const client = detail.edit.al_clientname || null;
  const flagged = detail.checklist?.items ?? [];
  const keyDates = caseKeyDates(reviews.status === 'ready' ? reviews.reviews : [], detail.dueDate);
  const mayRemoveOrDelete = can('page.cases', 'Edit') || can('case.delete', 'Edit');

  const details = (
    <div className="case-detail__layout">
      <div className="case-detail__main">
        {detail.adviserUnmatched ? (
          <p className="case-detail__notice" role="status">
            {adviserDuplicateNote ?? (
              <>
                Adviser not matched: no single active person holds the email{' '}
                {detail.edit.al_adviseremail || '(none recorded)'}, so no adviser can see this case
                yet. Correct the adviser&apos;s email on the case, or add or correct the person on
                the People page.
              </>
            )}
          </p>
        ) : null}

        <section className="case-card case-detail__summary" aria-label="Case summary">
          <dl className="case-fields case-fields--strip">
            <div className="case-fields__item">
              <dt>{SERVICE_CASE_REF_LABEL}</dt>
              <dd>{detail.serviceCaseRef ?? '—'}</dd>
            </div>
            <div className="case-fields__item">
              <dt>Route</dt>
              <dd>{detail.route ?? 'Not routed'}</dd>
            </div>
            <div className="case-fields__item">
              <dt>{UPLOADED_BY_LABEL}</dt>
              <dd>{detail.owner ?? 'Unassigned'}</dd>
            </div>
            <div className="case-fields__item">
              <dt>Priority</dt>
              <dd>{detail.priority ?? 'Not set'}</dd>
            </div>
            <div className="case-fields__item">
              <dt>Due</dt>
              <dd>{detail.dueDate ?? 'Not set'}</dd>
            </div>
            <div className="case-fields__item">
              <dt>Age</dt>
              <dd>{detail.ageInDays} days</dd>
            </div>
          </dl>
        </section>

        <Card id="panel-people" heading="Adviser and paraplanner" icon="users">
          <div className="case-detail__people">
            <div>
              <span className="case-fields__label">Adviser</span>
              {valueOf('Adviser name') ? (
                <PersonCell name={valueOf('Adviser name')!} detail={detail.edit.al_adviseremail || null} />
              ) : (
                <span className="case-fields__empty">Not recorded</span>
              )}
            </div>
            <div>
              <span className="case-fields__label">Paraplanner</span>
              {valueOf('Paraplanner') ? (
                <PersonCell name={valueOf('Paraplanner')!} detail={detail.edit.al_paraplanneremail || null} />
              ) : (
                <span className="case-fields__empty">Not recorded</span>
              )}
            </div>
          </div>
          <Fields fields={fieldsFor(header, 'people')} />
        </Card>

        <Card id="panel-client" heading="Client and advice" icon="briefcase">
          <Fields fields={fieldsFor(header, 'client')} />
        </Card>

        <Card id="panel-checks" heading="Checks on this case" icon="checklist">
          <Fields fields={fieldsFor(header, 'checks')} />
          {reviews.status === 'loading' ? <Skeleton label="Loading checks…" /> : null}
          {reviews.status === 'unavailable' ? (
            <p className="case-card__note">The checks for this case could not be loaded.</p>
          ) : null}
          {reviews.status === 'ready' ? (
            reviews.reviews.length === 0 ? (
              <p className="case-card__note">No Tax or AQS check has been raised for this case yet.</p>
            ) : (
              <div className="case-detail__scroll">
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
              </div>
            )
          ) : null}
        </Card>

        <Card id="panel-refs" heading="References" icon="hash">
          <Fields fields={fieldsFor(header, 'refs')} />
          {detail.previousCase ? (
            <p className="case-card__note">Replaces {detail.previousCase}</p>
          ) : null}
        </Card>
      </div>

      <aside className="case-detail__rail" aria-label="Case at a glance">
        {/*
          What the paraplanner ticked in the IO task (project owner, 2026-09-19): it is why the
          case was raised for checking at all. The same items the review page draws.
        */}
        {flagged.length > 0 ? (
          <Card id="panel-checklist" heading="Checklist items" aside={`${flagged.length} flagged`}>
            <ul className="case-detail__flags">
              {flagged.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </Card>
        ) : null}

        <Card id="panel-dates" heading="Key dates">
          {keyDates.length === 0 ? (
            <p className="case-card__note">No check has started on this case yet.</p>
          ) : (
            <ol className="case-detail__dates">
              {keyDates.map((entry) => (
                <li key={`${entry.date}-${entry.label}`}>
                  <strong>{entry.date}</strong>
                  <span>{entry.label}</span>
                </li>
              ))}
            </ol>
          )}
          <p className="case-card__note">Every change is listed on the History tab.</p>
        </Card>

        {mayRemoveOrDelete ? (
          <section className="case-card case-detail__danger" aria-labelledby="panel-danger">
            <h2 id="panel-danger">Remove or delete this case</h2>
            <p className="case-card__note">
              Kept apart from the everyday actions. Remove takes the case out of checking and keeps
              it; delete erases it.
            </p>
            <div className="case-detail__danger-actions">
              <PermissionGate resource="page.cases" need="Edit">
                <CaseRemoveAction
                  detail={detail}
                  onRemoved={() => setReloadKey((key) => key + 1)}
                  onStale={() => setReloadKey((key) => key + 1)}
                />
              </PermissionGate>
              <CaseDeleteAction detail={{ id: detail.id, caseReference: detail.caseReference }} />
            </div>
          </section>
        ) : null}
      </aside>
    </div>
  );

  const outcome = (
    <div className="case-detail__layout">
      <div className="case-detail__main">
        <CaseOutcomeSummary
          state={outcomes}
          action={
            <CaseAmendOutcomeAction
              caseId={detail.id}
              adviceGrade={
                outcomes.status === 'ready' && outcomes.outcomes.length > 0
                  ? outcomes.outcomes[outcomes.outcomes.length - 1].initialOutcome
                  : null
              }
              onSaved={() => {
                setOutcomeKey((key) => key + 1);
                setReloadKey((key) => key + 1);
              }}
            />
          }
        />

        <PermissionGate resource="page.remediation">
          {/*
            The case's remedial actions, to the same people the remediation link
            has always been shown to (2026-10-05).
          */}
          <CaseRemedialActionsPanel caseId={detail.id} reloadKey={reloadKey} />
          <p className="case-detail__related">
            <Link to={`/cases/${detail.id}/remediation`}>
              Remediation and sign-off
              <Icon name="arrowRight" />
            </Link>
          </p>
        </PermissionGate>

        {/*
          Directly under the grades it describes (item 8, 2026-09-19). Accountability only
          means anything beside the outcome that produced the fail, and the names it offers are
          this case's own adviser and paraplanner.
        */}
        <FailAccountabilityPanel
          state={outcomes}
          onSaved={() => setOutcomeKey((key) => key + 1)}
          people={{
            adviser: detail.edit.al_advisername || null,
            paraplanner: detail.edit.al_paraplanner || null,
          }}
        />
      </div>

      <aside className="case-detail__rail" aria-label="Who signs this case off">
        <CaseTcManagerPanel caseId={detail.id} onSaved={() => setReloadKey((key) => key + 1)} />
      </aside>
    </div>
  );

  return (
    <>
      <p className="case-detail__back">
        <Link to="/cases">
          <Icon name="arrowLeft" />
          Back to case worklist
        </Link>
      </p>

      <div className="case-detail__intro">
        <div>
          <div className="case-detail__title">
            <h1>{detail.title}</h1>
            <StageLabel status={detail.status} />
          </div>
          <p className="case-detail__purpose">
            {client ? `${client}. ` : null}
            How the check was graded, what was put right, and who is accountable.
          </p>
        </div>
        <div className="case-detail__actions">
          <button type="button" className="button-secondary" onClick={() => setTab('history')}>
            <Icon name="history" />
            History
          </button>
          <CaseEditPanel
            detail={detail}
            reviews={reviews}
            directory={directory}
            listRows={state.listOptions}
            onSaved={() => setReloadKey((key) => key + 1)}
            onNotice={setEditNotice}
          />
        </div>
      </div>

      {editNotice ? <Notice tone="success">{editNotice}</Notice> : null}

      <Tabs
        label="Case detail"
        activeId={tab}
        onChange={setTab}
        items={[
          { id: 'details', label: 'Details', render: () => details },
          { id: 'outcome', label: 'Outcome and remediation', render: () => outcome },
          {
            id: 'history',
            label: 'History',
            render: () => <CaseHistoryPanel caseId={detail.id} reloadKey={reloadKey} />,
          },
        ]}
      />
    </>
  );
}
