import { describe, expect, it, vi } from 'vitest';

const updateCaseDetails = vi.fn();
vi.mock('../../services/commands/updateCaseDetails', () => ({
  updateCaseDetails: (input: unknown) => updateCaseDetails(input),
}));

const { runRemoval } = await import('./CaseRemoveAction');

function intent() {
  const keys = new Map<string, string>();
  return {
    keyFor: (token: string) => keys.get(token) ?? (keys.set(token, `k${keys.size}`), keys.get(token)!),
    release: (token: string) => void keys.delete(token),
  };
}

describe('removing a case someone else changed first', () => {
  it('asks for a re-read, so the next try carries the fresh row version', async () => {
    updateCaseDetails.mockResolvedValueOnce({
      ok: false,
      kind: 'conflict',
      message: 'The record has been modified by another user.',
    });
    const outcome = await runRemoval({ id: 'c1', status: 'Queued', rowVersion: '10' }, 'Added in error', intent());
    expect(outcome).toEqual({
      kind: 'refused',
      message: 'The record has been modified by another user.',
      reread: true,
    });
  });

  it('sends the re-read version on the next try', async () => {
    updateCaseDetails.mockReset();
    updateCaseDetails.mockResolvedValue({ ok: true, data: {} });
    const keys = intent();
    const outcome = await runRemoval({ id: 'c1', status: 'Queued', rowVersion: '11' }, 'Added in error', keys);
    expect(outcome).toEqual({ kind: 'removed' });
    expect(updateCaseDetails.mock.calls[0][0]).toMatchObject({ expectedRowVersion: '11' });
  });

  it('does not re-read for any other refusal', async () => {
    updateCaseDetails.mockResolvedValueOnce({ ok: false, kind: 'validation', message: 'Give a reason.' });
    const outcome = await runRemoval({ id: 'c1', status: 'Queued', rowVersion: '11' }, 'x', intent());
    expect(outcome).toMatchObject({ kind: 'refused', reread: false });
  });
});
