import { describe, expect, it } from 'vitest';

/**
 * Project owner, 2026-10-06: the Tax and AQS checker fields on the Edit case details modal
 * are searchable like the adviser fields.
 *
 * They were plain selects listing every active person, which is a long scroll once the
 * directory has more than a handful of people. They now use the same UserPicker the adviser
 * field does, typed into and narrowing as you type. Checked at the source: the app has no
 * DOM test runner, and which control draws the field is a fact about the source.
 */
describe('the checker fields on the edit case modal', () => {
  const sources = import.meta.glob('./CaseEditPanel.tsx', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;

  const panel = Object.values(sources)[0] ?? '';
  const start = panel.indexOf('<legend>Checkers</legend>');
  const checkers = panel.slice(start, panel.indexOf('</fieldset>', start));

  it('is reading the Checkers section at all', () => {
    expect(start).toBeGreaterThan(0);
    expect(checkers).toContain('case-edit-checker-');
  });

  it('draws each allocatable checker with the searchable person picker, not a select', () => {
    expect(checkers).toContain('<UserPicker');
    expect(checkers).not.toContain('<select');
  });

  it('takes the person by contact id, so text matching nobody allocates nobody', () => {
    expect(checkers).toContain('field="id"');
  });

  it('still says who holds the check when the box is empty', () => {
    expect(checkers).toContain('placeholder={`Leave as it is — ');
  });
});
