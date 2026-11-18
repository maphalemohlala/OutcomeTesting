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
    // One withdrawn assignment and one withdrawn rule per role. The retired role's two rows
    // say so; the live role's two rows can be restored. Retiring is the role table's own
    // button, so "Restore" there (for the retired role) is the third.
    expect(html.match(/>Restore</g)).toHaveLength(3);
    expect(html.match(/Role retired/g)).toHaveLength(2);
  });
});
