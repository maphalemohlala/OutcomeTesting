import { PageIntro } from '../../components/layout/PageIntro';
import { OutcomeIndicator } from '../../components/status/OutcomeIndicator';
import { StageLabel } from '../../components/status/StageLabel';
import { useCaseWorklist } from './useCaseWorklist';
import './CaseWorklistPage.css';

export function CaseWorklistPage() {
  const state = useCaseWorklist();

  return (
    <>
      <PageIntro
        title="Case worklist"
        purpose="Find the cases you own and decide what to pick up next."
      />

      {state.status === 'loading' ? <p role="status">Loading cases…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="worklist__unavailable" aria-labelledby="worklist-unavailable">
          <h2 id="worklist-unavailable">No cases can be listed</h2>
          <p>{state.reason}</p>
          <p>
            Nothing has been lost. Once the case tables are deployed this list will populate
            automatically.
          </p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <div className="worklist__scroll">
          <table className="worklist">
            <caption className="visually-hidden">
              Cases assigned to you or your team, oldest first
            </caption>
            <thead>
              <tr>
                <th scope="col">Case</th>
                <th scope="col">Route</th>
                <th scope="col">Status</th>
                <th scope="col">Owner</th>
                <th scope="col" className="worklist__numeric">
                  Age
                </th>
                <th scope="col">Latest outcome</th>
                <th scope="col">Next action</th>
              </tr>
            </thead>
            <tbody>
              {state.cases.length === 0 ? (
                <tr>
                  <td colSpan={7} className="worklist__empty">
                    No cases match your current filters.
                  </td>
                </tr>
              ) : (
                state.cases.map((item) => (
                  <tr key={item.id}>
                    <th scope="row">{item.caseReference}</th>
                    <td>{item.route}</td>
                    <td>
                      <StageLabel status={item.status} />
                    </td>
                    <td>{item.owner ?? 'Unassigned'}</td>
                    <td className="worklist__numeric">{item.ageInDays} days</td>
                    <td>
                      {item.latestOutcome ? (
                        <OutcomeIndicator outcome={item.latestOutcome} />
                      ) : (
                        'Not yet graded'
                      )}
                    </td>
                    <td>{item.nextAction}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  );
}
