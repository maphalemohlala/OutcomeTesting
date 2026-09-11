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

/**
 * The Tax check's own grade (AD-055), which is a different scale from BR-005 - it has a
 * Fail, and no Pass with issues or Potential harm.
 *
 * It borrows the BR-005 marks rather than minting new ones, so a Pass reads green and an
 * Insufficient evidence purple on both checks. The scale stays legible because the label is
 * prefixed "Tax:", not because the grade is drawn in grey: colour is how a reader finds a
 * failed check in a list, and withholding it made the Tax column the one place where a grade
 * carried no signal.
 */
const TAX_SHAPE: Record<string, string> = {
  Pass: 'pass',
  Fail: 'harm',
  'Insufficient evidence': 'insufficient',
};

export function TaxOutcomeIndicator({ outcome }: { outcome: string }) {
  const variant = TAX_SHAPE[outcome];
  if (!variant) {
    // A value the Tax scale does not define. Shown as words rather than guessed at a colour.
    return <span className="outcome">Tax: {outcome}</span>;
  }

  return (
    <span className="outcome" data-variant={variant}>
      <span className={`outcome__mark outcome__mark--${variant}`} aria-hidden="true" />
      Tax: {outcome}
    </span>
  );
}
