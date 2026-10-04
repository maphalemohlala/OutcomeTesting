import { useMemo } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import { ExportMenu } from '../../components/export/ExportMenu';
import { OUTCOMES } from '../../types/domain';
import { CASE_EXPORT_HEADERS, caseExportRow } from '../cases/caseExport';
import { useCaseWorklist } from '../cases/useCaseWorklist';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import { casesForPerson, contactEmailsOf, identityOf, isPersonRole, positions } from './peopleDirectory';
import { checkerLabel } from '../cases/checkerNames';
import './PeoplePage.css';

function day(iso: string | null): string {
  return iso ? iso.slice(0, 10) : '—';
}

export function PersonCasesPage() {
  const { role, key = '' } = useParams<{ role: string; key: string }>();
  const state = useCaseWorklist();
  const directory = useUserDirectory();

  const personRole = isPersonRole(role) ? role : null;

  const contactEmails = useMemo(
    () => contactEmailsOf(directory.status === 'ready' ? directory.users : []),
    [directory],
  );

  const cases = useMemo(() => {
    if (!personRole || state.status !== 'ready') return [];
    return casesForPerson(state.cases, personRole, key, contactEmails);
  }, [state, personRole, key, contactEmails]);

  /**
   * The person's display name: the name of the first matching position, since that is what
   * the case actually recorded. Falls back to the part of the key after `email:` when no
   * case matches (visibility can hide every one), so the page still shows something
   * recognisable rather than the raw `email:`/`contact:`/`name:` key.
   */
  const name = useMemo(() => {
    if (personRole) {
      const target = key.toLowerCase();
      for (const item of cases) {
        const match = positions(item).find(
          (position) => position.role === personRole && identityOf(position, contactEmails) === target,
        );
        if (match?.name) return match.name;
      }
    }
    return key.startsWith('email:') ? key.slice('email:'.length) : key;
  }, [cases, personRole, key, contactEmails]);

  const totals = useMemo(() => {
    const open = cases.filter(
      (item) => item.status !== 'Closed' && item.status !== 'No Check Required',
    ).length;
    const graded = OUTCOMES.map((outcome) => ({
      outcome,
      count: cases.filter((item) => item.latestOutcome === outcome).length,
    })).filter((entry) => entry.count > 0);
    return { open, graded };
  }, [cases]);

  if (!personRole) {
    return (
      <>
        <PageIntro title="Person not found" purpose="That address does not name a known position." />
        <p>
          <Link to="/admin/people">Back to people</Link>
        </p>
      </>
    );
  }

  return (
    <>
      <PageIntro
        title={name}
        purpose={`Every case where ${name} is recorded as the ${personRole.toLowerCase()}. Select a case reference to open its detail.`}
        actions={
          cases.length > 0 ? (
            <ExportMenu
              label="Export these cases"
              stem={`outcome-cases-${personRole.toLowerCase()}`}
              sheetName="Cases"
              headers={CASE_EXPORT_HEADERS}
              rows={cases.map(caseExportRow)}
              caption={`Exports the ${cases.length} case${cases.length === 1 ? '' : 's'} listed below`}
            />
          ) : null
        }
      />

      <p className="people__note">
        <Link to="/admin/people">Back to people</Link>
        {' · '}
        <Link to={`/cases?person=${encodeURIComponent(key)}`}>
          Open in the case worklist with all filters
        </Link>
      </p>

      {state.status === 'loading' ? <p role="status">Loading cases…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="people__unavailable" aria-labelledby="person-unavailable">
          <h2 id="person-unavailable">These cases cannot be listed</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        cases.length === 0 ? (
          <p className="people__empty-state">
            No cases visible to you record {name} as the {personRole.toLowerCase()}. Someone may
            have been renamed on later cases, or the cases may be outside what you may read.
          </p>
        ) : (
          <>
            <section className="people__summary" aria-label="Case totals for this person">
              <div className="people__summary-item">
                <span className="people__summary-value">{cases.length}</span>
                <span className="people__summary-label">Cases</span>
              </div>
              <div className="people__summary-item">
                <span className="people__summary-value">{totals.open}</span>
                <span className="people__summary-label">Still open</span>
              </div>
              {totals.graded.map((entry) => (
                <div key={entry.outcome} className="people__summary-item">
                  <span className="people__summary-value">{entry.count}</span>
                  <span className="people__summary-label">
                    <OutcomeIndicator outcome={entry.outcome} />
                  </span>
                </div>
              ))}
            </section>

            <div className="people__scroll">
              <table className="people">
                <caption className="visually-hidden">Cases for {name}</caption>
                <thead>
                  <tr>
                    <th scope="col">Case</th>
                    <th scope="col">Client</th>
                    {/* Who each check is allocated to (project owner, 2026-09-24). */}
                    <th scope="col">Tax checker</th>
                    <th scope="col">AQS checker</th>
                    <th scope="col">Product / solution type</th>
                    <th scope="col">Check date</th>
                    <th scope="col">Status</th>
                    <th scope="col">Outcome</th>
                    <th scope="col" className="people__numeric">
                      Age
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {cases.map((item) => (
                    <tr key={item.id}>
                      <th scope="row">
                        <Link to={`/cases/${item.id}`}>{item.caseReference}</Link>
                      </th>
                      <td>{item.client ?? '—'}</td>
                      <td>{checkerLabel(item.taxChecker, item.route, 'Tax')}</td>
                      <td>{checkerLabel(item.aqsChecker, item.route, 'AQS')}</td>
                      <td>{item.productSolutionType ?? '—'}</td>
                      <td>{day(item.checkDate)}</td>
                      <td>
                        <StageLabel status={item.status} />
                      </td>
                      <td>
                        {item.latestOutcome ? (
                          <OutcomeIndicator outcome={item.latestOutcome} />
                        ) : (
                          'Not yet graded'
                        )}
                      </td>
                      <td className="people__numeric">{item.ageInDays} days</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )
      ) : null}
    </>
  );
}
