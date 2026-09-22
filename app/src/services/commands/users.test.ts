import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * updateUser's own marshalling of staffCode (Task 6). Absent must mean "leave unchanged"
 * and an explicit empty string must mean "clear it" - the server tells the two apart only
 * by whether the StaffCode key arrived at all (CommandHelpers.GetOptionalString returns
 * null for a key that is simply not in InputParameters). So this is the half that decides
 * whether the server ever sees "absent": if the client sent an empty StaffCode string
 * whenever the field was merely unedited, every save would silently wipe the person's
 * code, which is exactly the failure this task exists to prevent.
 */
const sent: Array<{ operationName: string; body: Record<string, unknown> }> = [];

vi.mock('@microsoft/power-apps/data', () => ({
  getClient: () => ({
    executeAsync: async (request: {
      dataverseRequest: {
        parameters: { operationName: string; body: Record<string, unknown> };
      };
    }) => {
      sent.push({ ...request.dataverseRequest.parameters });
      return { success: true, data: {} };
    },
  }),
}));

describe('updateUser staffCode marshalling', () => {
  beforeEach(() => {
    sent.length = 0;
  });

  it('omits StaffCode entirely when the field was not supplied', async () => {
    const { updateUser } = await import('./users');
    await updateUser({ userId: 'u1', fullName: 'A Name', idempotencyKey: 'k1' });

    expect(sent[0].body).not.toHaveProperty('StaffCode');
  });

  it('omits StaffCode when the field is explicitly null', async () => {
    const { updateUser } = await import('./users');
    await updateUser({ userId: 'u2', fullName: 'A Name', staffCode: null, idempotencyKey: 'k2' });

    expect(sent[0].body).not.toHaveProperty('StaffCode');
  });

  it('sends an explicit empty string, so the server clears the stored code', async () => {
    const { updateUser } = await import('./users');
    await updateUser({ userId: 'u3', fullName: 'A Name', staffCode: '', idempotencyKey: 'k3' });

    expect(sent[0].body.StaffCode).toBe('');
  });

  it('sends the value supplied', async () => {
    const { updateUser } = await import('./users');
    await updateUser({ userId: 'u4', fullName: 'A Name', staffCode: 'EMP1', idempotencyKey: 'k4' });

    expect(sent[0].body.StaffCode).toBe('EMP1');
  });
});
