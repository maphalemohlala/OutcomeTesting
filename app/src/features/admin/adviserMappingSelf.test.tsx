import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

const row = (id: string, adviser: string, managerEmail: string, managerName: string) => ({
  id,
  adviserEmail: adviser,
  adviserName: null,
  managerId: `m-${managerEmail}`,
  managerName,
  managerEmail,
});

vi.mock('./useAdviserMappings', () => ({
  useAdviserMappings: () => ({
    status: 'ready',
    mappings: [
      row('1', 'self@example.invalid', 'SELF@example.invalid', 'Sam Self'),
      row('2', 'other@example.invalid', 'boss@example.invalid', 'Bo Boss'),
    ],
    contacts: [],
    known: [],
  }),
  saveAdviserMapping: vi.fn(),
  removeAdviserMapping: vi.fn(),
}));
vi.mock('../../app/permissions/permissionContext', () => ({
  usePermissions: () => ({ can: () => true }),
}));

const { AdviserMappingPage } = await import('./AdviserMappingPage');

describe('AdviserMappingPage self-mapping and managers', () => {
  const html = renderToStaticMarkup(<AdviserMappingPage />);

  it('flags the one row whose manager email is the adviser email', () => {
    expect(html.match(/Signs off their own cases/g)).toHaveLength(1);
    expect(html).toContain('1 adviser is mapped to themselves');
    expect(html).toContain('self@example.invalid would');
  });

  it('lists each manager in the rail and names the remove button', () => {
    expect(html).toContain('2 people');
    expect(html).toContain('aria-label="Remove Bo Boss from other@example.invalid"');
    expect(html).toContain('Add a manager');
  });
});
