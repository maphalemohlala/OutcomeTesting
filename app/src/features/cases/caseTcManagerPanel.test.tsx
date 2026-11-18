import { describe, expect, it, vi } from 'vitest';
import { DEFAULT_PERMISSIONS, can, resolvePermissions } from '../../types/permissions';

/** Handing one case to another T&C Manager (requirements log row 4). */

const reassignTcManager = vi.fn();
vi.mock('../../services/commands/reassignTcManager', () => ({ reassignTcManager }));
vi.mock('../../generated', () => ({ Al_outcomecasesService: { get: vi.fn() } }));
vi.mock('../admin/useSecurityConfig', () => ({ useSecurityConfig: () => ({ status: 'loading' }) }));

const { supervisorEmails, runReassignment } = await import('./CaseTcManagerPanel');

const intent = { keyFor: (token: string) => 'key:' + token, release: vi.fn() };

describe('handing a case to another T&C Manager', () => {
  it('offers only active T&C Supervisors', () => {
    const offered = supervisorEmails([
      { email: ' Cora@Example.com ', role: 'AL Portal - T&C Supervisor', active: true },
      { email: 'old@example.com', role: 'AL Portal - T&C Supervisor', active: false },
      { email: 'checker@example.com', role: 'AL Portal - AQS Reviewer', active: true },
    ]);
    expect([...offered]).toEqual(['cora@example.com']);
  });

  it('is granted to the manager and both admin roles, and to no supervisor or checker', () => {
    const holds = (role: string) =>
      can(resolvePermissions([role], DEFAULT_PERMISSIONS), 'case.reassignmanager', 'Edit');
    for (const role of ['AL Portal - Outcome Testing Manager', 'AL Portal - Portal Administrator', 'Administrators']) {
      expect(holds(role)).toBe(true);
    }
    for (const role of ['AL Portal - T&C Supervisor', 'AL Portal - AQS Reviewer', 'AL Portal - Adviser Remediation']) {
      expect(holds(role)).toBe(false);
    }
  });

  it('sends the manager, or an empty one to hand the case back', async () => {
    reassignTcManager.mockResolvedValue({ ok: true, data: { Status: 'Reassigned', Summary: 's', AuditEventId: 'a' } });

    await expect(runReassignment('c1', 'm1', ' Pat on leave ', intent)).resolves.toEqual({ kind: 'moved', summary: 's' });
    expect(reassignTcManager).toHaveBeenLastCalledWith({
      caseId: 'c1',
      managerId: 'm1',
      reason: 'Pat on leave',
      idempotencyKey: 'key:reassign:c1:m1:Pat on leave',
    });

    await runReassignment('c1', '', 'Pat back', intent);
    expect(reassignTcManager).toHaveBeenLastCalledWith(expect.objectContaining({ managerId: '' }));

    reassignTcManager.mockResolvedValueOnce({ ok: false, kind: 'precondition', message: 'Cora does not hold the role.' });
    expect((await runReassignment('c1', 'm2', 'x', intent)).kind).toBe('refused');
  });
});
