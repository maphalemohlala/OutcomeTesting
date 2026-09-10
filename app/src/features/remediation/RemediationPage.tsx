import { Fragment, useMemo } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { StageLabel } from '../../components/status/StageLabel';
import {
  useRemediation,
  type RemediationCase,
  type OutcomeRow,
  type RemediationActionRow,
  type SignoffRow,
} from './useRemediation';
import { groupIssues, outcomeOf } from './remediationIssues';
import {
  liveActions,
  remediationForm,
  remediationFormLayout,
  settledChecks,
  signoffCell,
} from './remediationForm';
import { remediationClock } from '../../lib/workingDays';
import type { CaseStatus } from '../../types/domain';
import '../../styles/document.css';
import './RemediationPage.css';



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
}: {
  actions: RemediationActionRow[];
  signoffs: SignoffRow[];
  adviserName: string | null;
}) {
  if (actions.length === 0) {
    return (
      <p className="remediation__note">No remediation action has been raised for this case yet.</p>
    );
  }

  // A check whose every action is approved is settled, so its rows come off the table
  // (AD-114, project owner 2026-09-10). A Tax-then-AQS case remediates twice; drawing both
  // legs as one numbered list put the checker's finished work back in front of them every
  // time the second leg opened. The case record keeps what is not drawn.
  const settled = settledChecks(actions, signoffs);
  const live = liveActions(actions, signoffs);

  if (live.length === 0) {
    return (
      <p className="remediation__note">
        Every issue raised on this case has been remediated and approved
        {settled.length > 0 ? ` (${settled.join(', ')})` : ''}. The numbered rows are closed;
        the case record keeps them in full.
      </p>
    );
  }

  // The check is named once above its run of rows rather than on every one. Worked out here
  // because the map below cannot carry state across iterations without it.
  let previous: string | null = null;
  const groups = groupIssues(live).map((group) => {
    const check = group.action.triggeredBy;
    const heading = check && check !== previous ? check : null;
    previous = check ?? previous;
    return { ...group, heading };
  });

  return (
    <>
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
        </tr>
      </thead>
      <tbody>
        {groups.map(({ action, lines, heading }) => (
          <Fragment key={action.id}>
            {heading ? (
              <tr className="remediation__group">
                <th scope="colgroup" colSpan={8}>
                  {heading}
                </th>
              </tr>
            ) : null}
            {lines.map(({ number, issue }, index) => (
              <tr key={number}>
                <th scope="row">{number}</th>
                <td className="remediation__issue">{issue}</td>
                {index === 0 ? (
                  <>
                    <td rowSpan={lines.length}>{action.remedialAction ?? '—'}</td>
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
                  </>
                ) : null}
              </tr>
            ))}
          </Fragment>
        ))}
      </tbody>
    </table>
    {settled.length > 0 ? (
      // Said rather than left to be noticed: a checker who remembers approving five Tax
      // actions needs to know they were settled, not that the page lost them.
      <p className="remediation__note">
        Settled and not shown: {settled.join(', ')}. Every action on
        {settled.length > 1 ? ' those checks was' : ' that check was'} approved, so
        {settled.length > 1 ? ' they are' : ' it is'} closed. The case record keeps them in
        full.
      </p>
    ) : null}
    </>
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
  const layout = remediationFormLayout(block);

  return (
    <>
      {/* The paper form's answer grid: four answers, two to a row (AD-111). */}
      <table className="remediation__form-grid">
        <caption className="remediation__sr-only">
          Questions for this remediation
        </caption>
        <tbody>
          {layout.answers.map((row) => (
            <tr key={row.map((cell) => cell.label).join('|')}>
              {row.map((cell) => (
                <Fragment key={cell.label}>
                  <th scope="row">{cell.label}</th>
                  <td>{cell.value ?? '—'}</td>
                </Fragment>
              ))}
            </tr>
          ))}
          <tr>
            <th scope="row">
              {layout.regrade.label}
              <span className="remediation__form-note"> ({layout.regrade.note})</span>
            </th>
            <td colSpan={3}>
              {layout.regrade.outcome ?? '—'}
              {layout.regrade.on ? <span className="remediation__form-note"> · {layout.regrade.on}</span> : null}
            </td>
          </tr>
        </tbody>
      </table>

      {/* The sign-off table the document draws under it: signatory and date. */}
      <table className="remediation__signoffs">
        <caption className="remediation__sr-only">Sign-off</caption>
        <thead>
          <tr>
            <th scope="col"></th>
            <th scope="col">Date</th>
          </tr>
        </thead>
        <tbody>
          {layout.signoffs.map((signoff) => (
            <tr key={signoff.label}>
              <th scope="row">
                {signoff.label}
                <span className="remediation__form-note"> ({signoff.note})</span>
              </th>
              <td>{signoff.on ?? signoff.by ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

export function RemediationPage() {
  const { caseId } = useParams<{ caseId: string }>();
  // Read-only: every remediation action is completed and signed off on the portal
  // (project owner, 2026-09-10), so this page reports and never writes. The reload key
  // stays at zero because nothing here changes what it reads.
  const state = useRemediation(caseId, 0);

  const allActions = state.status === 'ready' ? state.actions : [];
  // The outcome the remediation was raised for. Read across every action rather than the
  // filtered set: it is the case's, so narrowing the table by status must not change it.
  const outcome = useMemo(() => outcomeOf(allActions), [allActions]);


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

          {/*
            Drawn as the document, the same way the Checker Checklist is (project owner,
            2026-09-10: a remediation should read like the review forms). The page's own
            chrome - the back link, the intro, the notice - keeps the app's styling; the
            form itself is the V8 sheet.

            Read-only here. Every write path stays where it already was and stays
            permissioned: Mark complete on a row is al_CompleteRemediation, the adviser's
            response is the portal's, and the regrade and supervisor sign-off are their own
            commands. Drawing the form does not add a write path to any of them.
          */}
          <section className="checklist-doc" aria-labelledby="remediation-actions">
            <div className="doc-footer">Outcome Testing — Remediation and escalation | V8</div>
            <h1 id="remediation-actions">
              Remediation and escalation
              {state.outcomeCase?.status ? (
                <StageLabel status={state.outcomeCase.status as CaseStatus} />
              ) : null}
            </h1>
            <RemediationDetails outcomeCase={state.outcomeCase} outcome={outcome} caseId={caseId} />
            <ActionsTable
              actions={allActions}
              signoffs={state.signoffs}
              adviserName={state.outcomeCase?.adviserName ?? null}
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
