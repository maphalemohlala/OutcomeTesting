import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { DEFAULT_PERMISSIONS, can, resolvePermissions } from '../../types/permissions';

/** Deleting a case outright (requirements log row 40). */

const deleteCase = vi.fn();
vi.mock('../../services/commands/deleteCase', () => ({ deleteCase }));

let allowed = true;
vi.mock('../../app/permissions/PermissionGate', () => ({
  PermissionGate: ({ children }: { children: unknown }) => (allowed ? children : null),
}));

const { CaseDeleteAction, referenceConfirmed, runDeletion } = await import('./CaseDeleteAction');

const intent = { keyFor: (token: string) => 'key:' + token, release: vi.fn() };

describe('deleting a case', () => {
  it('is offered only behind the case.delete capability', () => {
    allowed = true;
    const shown = renderToStaticMarkup(
      <MemoryRouter>
        <CaseDeleteAction detail={{ id: 'c1', caseReference: 'IO-1' }} />
      </MemoryRouter>,
    );
    expect(shown).toContain('Delete case');

    allowed = false;
    const hidden = renderToStaticMarkup(
      <MemoryRouter>
        <CaseDeleteAction detail={{ id: 'c1', caseReference: 'IO-1' }} />
      </MemoryRouter>,
    );
    expect(hidden).toBe('');
  });

  it('is granted to the manager and both admin roles, and to no checker or adviser', () => {
    const holds = (role: string) => can(resolvePermissions([role], DEFAULT_PERMISSIONS), 'case.delete', 'Edit');
    expect(holds('AL Portal - Outcome Testing Manager')).toBe(true);
    expect(holds('AL Portal - Portal Administrator')).toBe(true);
    expect(holds('Administrators')).toBe(true);
    for (const role of ['AL Portal - AQS Reviewer', 'AL Portal - Tax Reviewer', 'AL Portal - T&C Supervisor', 'AL Portal - Adviser Remediation']) {
      expect(holds(role)).toBe(false);
    }
  });

  it('needs the reference typed, ignoring case and spaces, as the server does', () => {
    expect(referenceConfirmed('IO-100', ' io-100 ')).toBe(true);
    expect(referenceConfirmed('IO-100', 'IO-10')).toBe(false);
    expect(referenceConfirmed('', '')).toBe(false);
  });

  it('sends the typed reference and reason, and reports a refusal in words', async () => {
    deleteCase.mockResolvedValueOnce({ ok: true, data: { Status: 'Deleted', Summary: 's', AuditEventId: 'a' } });
    await expect(runDeletion({ id: 'c1', caseReference: 'IO-1' }, ' IO-1 ', ' Imported in error. ', intent)).resolves.toEqual({
      kind: 'deleted',
    });
    expect(deleteCase).toHaveBeenLastCalledWith({
      caseId: 'c1',
      confirmReference: 'IO-1',
      reason: 'Imported in error.',
      idempotencyKey: 'key:delete:c1:Imported in error.',
    });

    deleteCase.mockResolvedValueOnce({ ok: false, kind: 'validation', message: 'Type the case reference IO-1 exactly to confirm the delete.' });
    const refused = await runDeletion({ id: 'c1', caseReference: 'IO-1' }, 'IO-2', 'x', intent);
    expect(refused.kind).toBe('refused');
  });
});
