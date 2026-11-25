import { Link } from 'react-router-dom';
import { SERVICE_CASE_REF_LABEL } from '../../types/domain';
import './CaseReferenceLink.css';

/**
 * A case list's Case cell: the TaskID as the link, the service case reference beneath it.
 *
 * The TaskID stays the key; ServiceCaseSequentialRef is the reference people quote, but two
 * tasks can share one service case, so it is shown under the key rather than instead of it
 * (client, 2026-11-24). Used by the worklist, a person's cases and the completed-case report.
 */
export function CaseReferenceLink({
  id,
  caseReference,
  serviceCaseRef,
}: {
  id: string;
  caseReference: string;
  serviceCaseRef: string | null;
}) {
  return (
    <>
      <Link to={`/cases/${id}`}>{caseReference}</Link>
      {serviceCaseRef ? (
        <span className="case-ref__service">
          <span className="visually-hidden">{SERVICE_CASE_REF_LABEL} </span>
          {serviceCaseRef}
        </span>
      ) : null}
    </>
  );
}
