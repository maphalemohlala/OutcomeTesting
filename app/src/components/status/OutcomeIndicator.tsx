import type { Outcome } from '../../types/domain';
import './OutcomeIndicator.css';

const SHAPE: Record<Outcome, string> = {
  Pass: 'pass',
  'Pass with issues': 'issues',
  'Insufficient evidence': 'insufficient',
  'Potential harm': 'harm',
};

/** Shape and text carry the meaning; colour is reinforcement only (WCAG 2.2 AA, NFR-ACC-01). */
export function OutcomeIndicator({ outcome }: { outcome: Outcome }) {
  const variant = SHAPE[outcome];

  return (
    <span className="outcome" data-variant={variant}>
      <span className={`outcome__mark outcome__mark--${variant}`} aria-hidden="true" />
      {outcome}
    </span>
  );
}
