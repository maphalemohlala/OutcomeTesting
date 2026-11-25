import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const allowed = new Set<string>();

vi.mock('./useAdviserMappings', () => ({
  useAdviserMappings: () => ({
    status: 'ready',
    contacts: [],
    known: [],
    mappings: [
      { id: 'r1', adviserEmail: 'ann@example.com', adviserName: 'Ann', managerId: 'm1', managerName: 'Pat', managerEmail: 'pat@example.com', status: null },
      { id: 'r2', adviserEmail: 'ann@example.com', adviserName: 'Ann', managerId: 'm2', managerName: 'Sam', managerEmail: 'sam@example.com', status: 120910501 },
      { id: 'r3', adviserEmail: 'bob@example.com', adviserName: 'Bob', managerId: 'm1', managerName: 'Pat', managerEmail: 'pat@example.com', status: null },
    ],
  }),
  saveAdviserMapping: vi.fn(),
  saveAdviserStatus: vi.fn(),
  removeAdviserMapping: vi.fn(),
}));
vi.mock('../../app/permissions/permissionContext', () => ({
  usePermissions: () => ({ can: (resource: string) => allowed.has(resource) }),
}));

const { AdviserMappingPage } = await import('./AdviserMappingPage');

describe('the adviser status column (client, 2026-11-25)', () => {
  beforeEach(() => allowed.clear());

  it('shows the status as text to someone who runs the mapping but is not an administrator', () => {
    allowed.add('page.admin.advisers');
    const html = renderToStaticMarkup(<AdviserMappingPage />);
    expect(html).toContain('Adviser status');
    expect(html).toContain('<span>CAS</span>');
    expect(html).toContain('<span>Not set (IO decides)</span>');
    expect(html).not.toContain('<select');
  });

  it('lets a holder of adviser.status set it, starting from the value OTIS holds', () => {
    allowed.add('adviser.status');
    const html = renderToStaticMarkup(<AdviserMappingPage />);
    expect(html).toContain('aria-label="Adviser status for Ann"');
    expect(html).toMatch(/<option value="120910501" selected="">CAS<\/option>/);
    expect(html).toContain('PreCAS');
  });
});
