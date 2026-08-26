import type { CaseStatus } from '../../types/domain';
import { stageTone } from '../../types/domain';
import './StageLabel.css';

/** The status spine: a vertical rule in the row gutter paired with the status word. */
export function StageLabel({ status }: { status: CaseStatus }) {
  return (
    <span className="stage" data-tone={stageTone(status)}>
      {status}
    </span>
  );
}
