import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { DEFAULT_PERMISSIONS, can, resolvePermissions } from '../../types/permissions';

/** Amending a case's original grades for an appeal (requirements log row 39). */

const amendOutcome = vi.fn();
vi.mock('../../services/commands/amendOutcome', () => ({
  FILE_GRADES: ['Pass', 'Fail'],
  ADVICE_GRADES: ['Pass', 'Pass with issues', 'Insufficient evidence', 'Potential harm'],
  amendOutcome,
}));

let allowed = true;
vi.mock('../../app/permissions/PermissionGate', () => ({
  PermissionGate: ({ children }: { children: unknown }) => (allowed ? children : null),
}));

const { CaseAmendOutcomeAction, runAmendment } = await import('./CaseAmendOutcomeAction');

const intent = { keyFor: (token: string) => 'key:' + token, release: vi.fn() };

describe('amending a case outcome', () => {
  it('is offered only behind the case.amendoutcome capability', () => {
    allowed = true;
    expect(renderToStaticMarkup(<CaseAmendOutcomeAction caseId="c1" adviceGrade="Pass" onSaved={() => {}} />)).toContain(
      'Amend outcome',
    );

    allowed = false;
    expect(renderToStaticMarkup(<CaseAmendOutcomeAction caseId="c1" adviceGrade="Pass" onSaved={() => {}} />)).toBe('');
  });

  it('is granted to the manager and both admin roles, and to no checker, supervisor or adviser', () => {
    const holds = (role: string) =>
      can(resolvePermissions([role], DEFAULT_PERMISSIONS), 'case.amendoutcome', 'Edit');
    for (const role of ['AL Portal - Outcome Testing Manager', 'AL Portal - Portal Administrator', 'Administrators']) {
      expect(holds(role)).toBe(true);
    }
    for (const role of ['AL Portal - AQS Reviewer', 'AL Portal - Tax Reviewer', 'AL Portal - T&C Supervisor', 'AL Portal - Adviser Remediation']) {
      expect(holds(role)).toBe(false);
    }
  });

  it('sends only the grades chosen, with the trimmed reason, and reports the server summary', async () => {
    amendOutcome.mockResolvedValueOnce({
      ok: true,
      data: { Status: 'Amended', Summary: 'Case R: File grade from Fail to Pass.', AuditEventId: 'a' },
    });

    await expect(runAmendment('c1', 'Pass', '', ' Appeal upheld ', intent)).resolves.toEqual({
      kind: 'amended',
      summary: 'Case R: File grade from Fail to Pass.',
    });
    expect(amendOutcome).toHaveBeenLastCalledWith({
      caseId: 'c1',
      fileGrade: 'Pass',
      adviceGrade: '',
      reason: 'Appeal upheld',
      idempotencyKey: 'key:amend:c1:Pass::Appeal upheld',
    });

    amendOutcome.mockResolvedValueOnce({ ok: false, kind: 'validation', message: 'The case already has those grades.' });
    expect((await runAmendment('c1', 'Pass', '', 'x', intent)).kind).toBe('refused');
  });
});
