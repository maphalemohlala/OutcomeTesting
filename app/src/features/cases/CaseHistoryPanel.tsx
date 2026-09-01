import { useCaseHistory } from './useCaseHistory';
import './CaseHistoryPanel.css';

/**
 * Read-only audit trail for one case (FR-033). Audit Events are immutable (BR-012), so this
 * screen never offers an edit or delete affordance.
 */
export function CaseHistoryPanel({ caseId }: { caseId: string }) {
  const state = useCaseHistory(caseId);

  if (state.status === 'loading') {
    return <p role="status">Loading history…</p>;
  }

  if (state.status === 'unavailable') {
    return <p className="case-history__note">{state.reason}</p>;
  }

  if (state.entries.length === 0) {
    return (
      <p className="case-history__note">
        Nothing has been recorded against this case yet. Entries appear here when a command
        changes the case, a check, a remediation action, an outcome or a sign-off.
      </p>
    );
  }

  return (
    <table className="case-history__table">
      <thead>
        <tr>
          <th scope="col">When</th>
          <th scope="col">Action</th>
          <th scope="col">By</th>
          <th scope="col">Applied to</th>
          <th scope="col">Reason</th>
          <th scope="col">Details</th>
        </tr>
      </thead>
      <tbody>
        {state.entries.map((entry) => (
          <tr key={entry.id}>
            <td>{entry.occurredOn ?? '—'}</td>
            <th scope="row">{entry.command}</th>
            <td>{entry.actor ?? '—'}</td>
            <td>{entry.target}</td>
            <td>{entry.reason ?? '—'}</td>
            <td className="case-history__details">{entry.details ?? '—'}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
