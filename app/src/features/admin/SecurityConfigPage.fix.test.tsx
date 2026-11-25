import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

// Every tab's panel, so the rules and assignments tables both render statically.
vi.mock('../../components/navigation/Tabs', () => ({
  Tabs: ({ items }: { items: { id: string; render: () => unknown }[] }) => (
    <div>{items.map((item) => <section key={item.id}>{item.render() as never}</section>)}</div>
  ),
}));
vi.mock('../../generated', () => ({}));
vi.mock('../../components/form/UserPicker', () => ({ UserPicker: () => null }));
vi.mock('../../app/permissions/permissionContext', () => ({
  usePermissions: () => ({ can: () => true, ready: true }),
}));
vi.mock('../../hooks/useIntentKey', () => ({
  useIntentKeys: () => ({ keyFor: () => 'k', release: () => {} }),
}));
vi.mock('../../services/commands/roles', () => ({ createRole: vi.fn(), updateRole: vi.fn() }));
vi.mock('../../services/commands/permissions', () => ({
  assignUserRole: vi.fn(),
  setPagePermission: vi.fn(),
  setPermissionRuleActive: vi.fn(),
  setRoleAssignmentActive: vi.fn(),
}));
vi.mock('../../services/commands/commandClient', () => ({ executeCommand: vi.fn() }));
vi.mock('../../services/errors', () => ({ logTechnical: vi.fn() }));
vi.mock('../../hooks/useUserDirectory', () => ({ useUserDirectory: () => ({ status: 'loading' }) }));
vi.mock('./useRoles', () => ({
  useRoles: () => ({
    status: 'ready',
    roles: [
      { id: 'r1', name: 'Old Role', code: 'Old Role', description: null, active: false, rowVersion: null },
      { id: 'r2', name: 'Live Role', code: 'Live Role', description: null, active: true, rowVersion: null },
    ],
  }),
}));
vi.mock('./useSecurityConfig', () => ({
  useSecurityConfig: () => ({
    status: 'ready',
    mappings: [
      { id: 'm1', email: 'a@example.com', role: 'old role', active: false },
      { id: 'm2', email: 'b@example.com', role: 'Live Role', active: false },
      // Someone still holds each role, so both matrix columns are shown by default.
      { id: 'm3', email: 'c@example.com', role: 'Live Role', active: true },
      { id: 'm4', email: 'c@example.com', role: 'Old Role', active: true },
    ],
    permissions: [
      { id: 'p1', role: 'Old Role', resource: 'page.cases', level: 'Edit', active: false },
      { id: 'p2', role: 'Live Role', resource: 'page.cases', level: 'Edit', active: false },
    ],
  }),
}));

const { SecurityConfigPage } = await import('./SecurityConfigPage');

describe('Security configuration, retired roles', () => {
  const html = renderToStaticMarkup(
    <MemoryRouter>
      <SecurityConfigPage />
    </MemoryRouter>,
  );

  it('offers Restore only for the rule and the assignment of a role that is still active', () => {
    // One withdrawn assignment and one withdrawn rule per role. The rules table's live row and
    // the role registry's retired role (its own Restore) are the two Restore buttons.
    expect(html.match(/>Restore</g)).toHaveLength(2);
    // In the matrix the live role's withdrawn cell restores; the retired role's says why it cannot.
    expect(html).toContain('aria-label="Restore Live Role for b@example.com"');
    expect(html).toMatch(/<button[^>]*data-state="withdrawn"[^>]*aria-label="old role withdrawn from a@example.com. Role retired, so it cannot be restored"[^>]*disabled/);
    expect(html.match(/Role retired/g)).toHaveLength(2);
    expect(html).toContain('2 of 2 shown');
  });
});
