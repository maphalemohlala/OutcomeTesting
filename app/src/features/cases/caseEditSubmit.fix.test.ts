import { describe, expect, it } from 'vitest';
import { describeSubmit } from './caseEditSubmit';

describe('an allocation refused because someone else allocated first', () => {
  it('re-reads the checks, so the next try carries the fresh row version', () => {
    const outcome = describeSubmit({ kind: 'skipped' }, [
      { label: 'AQS', outcome: { kind: 'failed', message: 'This check was allocated by someone else.', conflict: true } },
    ]);
    expect(outcome.reload).toBe(true);
    expect(outcome.close).toBe(false);
    expect(outcome.error).toContain('allocated by someone else');
  });

  it('does not re-read for any other refusal when nothing committed', () => {
    const outcome = describeSubmit({ kind: 'skipped' }, [
      { label: 'AQS', outcome: { kind: 'failed', message: 'Nope.' } },
    ]);
    expect(outcome.reload).toBe(false);
  });
});
