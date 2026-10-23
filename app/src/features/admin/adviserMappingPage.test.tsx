import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

vi.mock('./useAdviserMappings', () => ({
  useAdviserMappings: () => ({ status: 'ready', mappings: [], contacts: [] }),
  saveAdviserMapping: vi.fn(),
  removeAdviserMapping: vi.fn(),
}));
vi.mock('../../app/permissions/permissionContext', () => ({
  usePermissions: () => ({ can: () => false }),
}));

const { AdviserMappingPage } = await import('./AdviserMappingPage');

/**
 * The page once told administrators the mapping "routes notifications only" and that any
 * T&C Manager could sign off without one. The server refuses exactly that, so the page must
 * say the mapping is what lets a manager act.
 */
describe('AdviserMappingPage wording', () => {
  const html = renderToStaticMarkup(<AdviserMappingPage />);

  it('says only the mapped T&C Manager can sign off or record the final outcome', () => {
    expect(html).toContain('only they can sign it off or record its final outcome in the portal');
    expect(html).not.toContain('notifications only');
  });

  it('says an unmapped adviser&#x27;s cases cannot be signed off', () => {
    expect(html).toContain('nobody can sign those cases off');
    expect(html).not.toContain('any T&amp;C Manager can perform one');
  });
});
